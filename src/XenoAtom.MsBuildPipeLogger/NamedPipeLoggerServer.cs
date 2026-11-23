// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// A server for receiving MSBuild logging events over a named pipe.
/// </summary>
/// <remarks>
/// MSBuild attaches a logger once per build submission, so one child process can open several sequential
/// connections — <c>dotnet build</c> runs restore and build as separate submissions. By default this server
/// keeps accepting connections so every submission is observed, which means a read only ends once
/// <see cref="StopListening"/> is called. Pass <c>acceptMultipleConnections: false</c> when the caller
/// guarantees a single submission and wants the read to end when the client disconnects.
/// </remarks>
public class NamedPipeLoggerServer : PipeLoggerServer<NamedPipeServerStream>
{
    // On Windows the accept and every read are blocking calls of the reader thread on a synchronous
    // handle, so that receiving events needs no thread of the host's thread pool. A host that runs many
    // build processes at once can have that pool starved, typically by the redirected output of those
    // processes, and a logger whose pipe is not read gives up after a while and drops the rest of the
    // build. A blocking accept is ended by cancelling the I/O of the handle.
    //
    // On Unix the runtime only offers an accept that completes on the thread pool, and its cancellation
    // is what stops the listener there, so the accept and the reads stay asynchronous.
    private static readonly bool UseBlockingCalls = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Unix: how long to wait to establish that no more clients are queued once a stop has been requested.
    // A client that has already connected is accepted immediately, so this only has to cover scheduling
    // the completion, not waiting for anyone to arrive.
    private static readonly TimeSpan BacklogDrainTimeout = TimeSpan.FromMilliseconds(250);

    // Unix: cancelling the accept is the entire stop mechanism. A CancellationToken already makes "observe
    // the stop" and "claim the wait" one atomic step: an accept started with a cancelled token never
    // blocks, and one already blocked is cancelled. So there is no window in which a stop goes unnoticed
    // and no need to wake the reader by connecting to ourselves.
    private readonly CancellationTokenSource _stopListeningSource = new();
    private readonly CancellationTokenRegistration _cancellationRegistration;

    // Windows: guards _accepting, so that a stop cancels the accept and never the read that follows it.
    private readonly object _acceptLock = new();
    private bool _accepting;
    private int _stopRequested;

    /// <summary>
    /// Gets the named pipe name.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets a value indicating whether the server keeps accepting connections after a client disconnects.
    /// </summary>
    public bool AcceptsMultipleConnections { get; }

    /// <summary>
    /// Creates a named pipe server for receiving MSBuild logging events from every build submission.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to create.</param>
    /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace, or is longer
    /// than <see cref="PipeLoggerServer.GetMaximumPipeNameLength()"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
    public NamedPipeLoggerServer(string pipeName)
        : this(pipeName, true, CancellationToken.None)
    {
    }

    /// <summary>
    /// Creates a named pipe server for receiving MSBuild logging events from every build submission.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to create.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
    /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace, or is longer
    /// than <see cref="PipeLoggerServer.GetMaximumPipeNameLength()"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
    public NamedPipeLoggerServer(string pipeName, CancellationToken cancellationToken)
        : this(pipeName, true, cancellationToken)
    {
    }

    /// <summary>
    /// Creates a named pipe server for receiving MSBuild logging events.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to create.</param>
    /// <param name="acceptMultipleConnections">
    /// <see langword="true"/> to keep accepting connections so that every MSBuild build submission is
    /// observed, in which case a read ends only once <see cref="StopListening"/> is called;
    /// <see langword="false"/> to serve a single connection and end the read when that client disconnects.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace, or is longer
    /// than <see cref="PipeLoggerServer.GetMaximumPipeNameLength()"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
    public NamedPipeLoggerServer(string pipeName, bool acceptMultipleConnections)
        : this(pipeName, acceptMultipleConnections, CancellationToken.None)
    {
    }

    /// <summary>
    /// Creates a named pipe server for receiving MSBuild logging events.
    /// </summary>
    /// <param name="pipeName">The name of the pipe to create.</param>
    /// <param name="acceptMultipleConnections">
    /// <see langword="true"/> to keep accepting connections so that every MSBuild build submission is
    /// observed, in which case a read ends only once <see cref="StopListening"/> is called;
    /// <see langword="false"/> to serve a single connection and end the read when that client disconnects.
    /// </param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
    /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace, or is longer
    /// than <see cref="PipeLoggerServer.GetMaximumPipeNameLength()"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
    public NamedPipeLoggerServer(string pipeName, bool acceptMultipleConnections, CancellationToken cancellationToken)
        : base(CreatePipe(pipeName, acceptMultipleConnections), cancellationToken, false)
    {
        PipeName = pipeName;
        AcceptsMultipleConnections = acceptMultipleConnections;
        StartReading();
        _cancellationRegistration = CancellationToken.Register(StopListening);
    }

    /// <inheritdoc/>
    public override void StopListening()
    {
        if (UseBlockingCalls)
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            {
                // Not on the calling thread: the reader may be busy with a client for a while, and the
                // accept that follows has to be cancelled once it is reached.
                var stopThread = new Thread(CancelAcceptUntilReaderEnds)
                {
                    IsBackground = true,
                    Name = "MSBuild pipe logger stop"
                };
                stopThread.Start();
            }

            return;
        }

        try
        {
            // Cancelling is idempotent, and a reader that is mid-drain finishes handing over that client's
            // events before it comes back to the accept and sees the cancellation.
            _stopListeningSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, so the reader has been stopped by other means.
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _cancellationRegistration.Dispose();
        StopListening();
        base.Dispose();

        // Only after the base has joined the reader thread, so nothing is still waiting on the token.
        _stopListeningSource.Dispose();
    }

    /// <inheritdoc/>
    protected override bool Connect() => WaitForNextConnection();

    /// <inheritdoc/>
    protected override bool TryAcceptNextConnection()
    {
        if (!AcceptsMultipleConnections)
        {
            return false;
        }

        // Re-accept on the same listener rather than creating a new one: re-creating it would leave a
        // window with nothing listening, and the next submission's connect would race the operating
        // system releasing the previous instance ("All pipe instances are busy").
        try
        {
            PipeStream.Disconnect();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Never connected, so there is nothing to disconnect. Note ObjectDisposedException derives
            // from this and is handled above.
        }

        return WaitForNextConnection();
    }

    private static NamedPipeServerStream CreatePipe(string pipeName, bool acceptMultipleConnections)
    {
        if (pipeName is null)
        {
            throw new ArgumentNullException(nameof(pipeName));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("The pipe name cannot be empty or whitespace.", nameof(pipeName));
        }

        // On Unix the name becomes part of a domain socket path, and exceeding the platform limit
        // otherwise surfaces as an ArgumentOutOfRangeException about a 'path' the caller never supplied.
        var maximumLength = PipeLoggerServer.GetMaximumPipeNameLength();
        if (PipeLoggerServer.GetPipeNameLength(pipeName) > maximumLength)
        {
            var limitDescription = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"this platform allows at most {maximumLength} characters"
                : $"this platform allows at most {maximumLength} UTF-8 bytes because the name becomes part " +
                  $"of a socket path under '{Path.GetTempPath()}'";
            throw new ArgumentException(
                $"The pipe name '{pipeName}' is too long: {limitDescription}. Use " +
                $"{nameof(PipeLoggerServer)}.{nameof(PipeLoggerServer.CreateUniquePipeName)}() to get a name that fits.",
                nameof(pipeName));
        }

        // On Unix the instance count doubles as the listen backlog, so a submission that connects while the
        // previous one is still being drained needs room to queue instead of being refused.
        var maximumServerInstances = acceptMultipleConnections ? NamedPipeServerStream.MaxAllowedServerInstances : 1;

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maximumServerInstances,
            PipeTransmissionMode.Byte,
            UseBlockingCalls ? PipeOptions.None : PipeOptions.Asynchronous);
    }

    /// <summary>
    /// Waits for the next client, unless the server has been told to stop listening.
    /// </summary>
    /// <returns><see langword="true"/> if a client connected and should be read.</returns>
    private bool WaitForNextConnection() =>
        UseBlockingCalls ? WaitForNextConnectionBlocking() : WaitForNextConnectionAsynchronously();

    /// <summary>
    /// Windows: blocks the reader thread in the accept until a client connects or a stop cancels it.
    /// </summary>
    private bool WaitForNextConnectionBlocking()
    {
        // The accept is entered even when a stop was already requested: it returns at once for a client
        // that has connected while the reader was busy, and the stop cancels it otherwise. A pipe instance
        // only takes a client while it is listening, so no other client can be waiting.
        lock (_acceptLock)
        {
            _accepting = true;
        }

        try
        {
            PipeStream.WaitForConnection();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            lock (_acceptLock)
            {
                _accepting = false;
            }
        }
    }

    /// <summary>
    /// Windows: cancels the accept of the reader thread, now or when it next reaches it, until that thread ends.
    /// </summary>
    /// <remarks>
    /// The request is repeated because the reader can be about to enter the accept when it is made. It is
    /// made under the lock the reader takes when the accept returns, so it cannot cancel a read instead.
    /// </remarks>
    private void CancelAcceptUntilReaderEnds()
    {
        while (IsTransportReaderAlive)
        {
            lock (_acceptLock)
            {
                if (_accepting && !TryCancelPendingIO())
                {
                    // The pipe is closed: cancellation or disposal ends the reader.
                    return;
                }
            }

            WaitForTransportReader(BlockingCallCancellationPeriod);
        }
    }

    /// <summary>
    /// Unix: blocks the reader thread on an asynchronous accept until a client connects or a stop cancels it.
    /// </summary>
    private bool WaitForNextConnectionAsynchronously()
    {
        // A stop that arrives before we reach the accept must not discard clients that have already
        // connected. The build process can finish writing a submission and exit while the reader is still
        // busy, leaving that connection queued, and an accept handed an already-cancelled token returns
        // without taking anything off the queue. Mop the queue up before finishing.
        if (_stopListeningSource.IsCancellationRequested)
        {
            return TryAcceptQueuedConnection();
        }

        try
        {
            // Blocking the dedicated reader thread on the async accept is what lets the stop cancel it.
            // A client that connects before the cancellation is still accepted and drained in full.
            PipeStream.WaitForConnectionAsync(_stopListeningSource.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (OperationCanceledException)
        {
            // Being cancelled here does not mean nothing was queued. A client can connect while we are
            // blocked and have the cancellation win the race to complete the accept, which loses that whole
            // submission, so the queue has to be checked either way.
            return TryAcceptQueuedConnection();
        }
        catch (IOException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (SocketException)
        {
            // Unix named pipes can surface disposal of the accept as a socket error.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits briefly for a client that may have connected before the stop was requested.
    /// </summary>
    /// <remarks>
    /// Accepting is immediate when the queue is not empty, so the timeout is how long we are willing to wait
    /// to establish that it <em>is</em> empty. The operating system does not expose when an unaccepted client
    /// entered the queue, so a client that arrives after the stop request can also be accepted during this
    /// grace period. Callers should request the stop only after the observed process has exited.
    /// </remarks>
    private bool TryAcceptQueuedConnection()
    {
        using (var drainTimeout = new CancellationTokenSource(BacklogDrainTimeout))
        {
            try
            {
                PipeStream.WaitForConnectionAsync(drainTimeout.Token).GetAwaiter().GetResult();
                return true;
            }
            catch (OperationCanceledException)
            {
                // Nothing left queued.
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
