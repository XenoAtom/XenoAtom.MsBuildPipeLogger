// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Receives MSBuild logging events over a pipe. This is the base class for <see cref="AnonymousPipeLoggerServer"/>
/// and <see cref="NamedPipeLoggerServer"/>.
/// </summary>
/// <typeparam name="TPipeStream">The concrete pipe stream type.</typeparam>
public abstract class PipeLoggerServer<TPipeStream> : PipeEventDispatcher, IPipeLoggerServer
    where TPipeStream : PipeStream
{
    private static readonly TimeSpan ReaderShutdownTimeout = TimeSpan.FromSeconds(5);

    // How long the reader thread is given to notice a cancelled blocking call before it is cancelled again.
    private protected static readonly TimeSpan BlockingCallCancellationPeriod = TimeSpan.FromMilliseconds(10);

    private readonly PipeEventReader _eventReader;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly object _readLock = new();
    private readonly Thread _readerThread;
    private int _disposed;
    private int _pipeShutdownRequested;
    private int _started;

    internal PipeBuffer Buffer { get; } = new();

    /// <summary>
    /// Gets the pipe stream read by this server.
    /// </summary>
    protected TPipeStream PipeStream { get; }

    /// <summary>
    /// Gets the token used to cancel read operations.
    /// </summary>
    protected CancellationToken CancellationToken { get; }

    /// <summary>
    /// Creates a server that receives MSBuild events over a specified pipe.
    /// </summary>
    /// <param name="pipeStream">The pipe to receive events from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeStream"/> is <see langword="null"/>.</exception>
    protected PipeLoggerServer(TPipeStream pipeStream)
        : this(pipeStream, CancellationToken.None)
    {
    }

    /// <summary>
    /// Creates a server that receives MSBuild events over a specified pipe.
    /// </summary>
    /// <param name="pipeStream">The pipe to receive events from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeStream"/> is <see langword="null"/>.</exception>
    protected PipeLoggerServer(TPipeStream pipeStream, CancellationToken cancellationToken)
        : this(pipeStream, cancellationToken, true)
    {
    }

    /// <summary>
    /// Creates a server that receives MSBuild events over a specified pipe.
    /// </summary>
    /// <param name="pipeStream">The pipe to receive events from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
    /// <param name="autoStart">A value indicating whether the background reader should start immediately.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeStream"/> is <see langword="null"/>.</exception>
    protected PipeLoggerServer(TPipeStream pipeStream, CancellationToken cancellationToken, bool autoStart)
    {
        PipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
        _eventReader = new PipeEventReader(Buffer);
        CancellationToken = cancellationToken;
        if (cancellationToken.CanBeCanceled)
        {
            _cancellationRegistration = cancellationToken.Register(static state =>
                ((PipeLoggerServer<TPipeStream>)state!).RequestPipeShutdown(), this);
        }

        _readerThread = new Thread(ReadFromTransport)
        {
            IsBackground = true,
            Name = "MSBuild pipe logger reader"
        };

        if (autoStart)
        {
            StartReading();
        }
    }

    /// <summary>
    /// Connects the server-side pipe stream to a client.
    /// </summary>
    /// <returns><see langword="true"/> if a client was connected and should be read;
    /// <see langword="false"/> to finish without reading.</returns>
    protected abstract bool Connect();

    /// <summary>
    /// Accepts the next client once the current one has disconnected, for transports that serve more than
    /// one connection. The base implementation serves a single connection and always returns
    /// <see langword="false"/>.
    /// </summary>
    /// <returns><see langword="true"/> if another client was accepted and should be read;
    /// <see langword="false"/> to finish reading.</returns>
    protected virtual bool TryAcceptNextConnection() => false;

    /// <inheritdoc/>
    public virtual void StopListening()
    {
        // A transport that serves a single connection has nothing to stop accepting: it finishes when
        // the client disconnects.
    }

    /// <summary>
    /// Starts the background reader thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reader thread was already started.</exception>
    protected void StartReading()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The logger server has already been started.");
        }

        _readerThread.Start();
    }

    /// <summary>
    /// Gets a value indicating whether the transport is being torn down by cancellation or disposal, in
    /// which case the reader thread must not start another blocking call.
    /// </summary>
    private protected bool IsPipeShutdownRequested => Volatile.Read(ref _pipeShutdownRequested) != 0;

    /// <summary>
    /// Gets a value indicating whether the thread that reads the transport is running.
    /// </summary>
    private protected bool IsTransportReaderAlive => _readerThread.IsAlive;

    /// <summary>
    /// Waits for the thread that reads the transport to end.
    /// </summary>
    /// <returns><see langword="true"/> if the thread has ended or was never started.</returns>
    internal bool WaitForTransportReader(TimeSpan timeout) =>
        Volatile.Read(ref _started) == 0 || _readerThread.Join(timeout);

    private void ReadFromTransport()
    {
        try
        {
            if (!Connect())
            {
                return;
            }

            do
            {
                DrainCurrentConnection();

                // Fence off whatever this client sent, so a record it left half-written cannot run on
                // into the next client's bytes.
                Buffer.WriteConnectionBoundary();
            }
            while (!IsPipeShutdownRequested && TryAcceptNextConnection());
        }
        catch (IOException)
        {
            // The client broke the stream so we're done.
        }
        catch (ObjectDisposedException)
        {
            // The pipe was disposed.
        }
        catch (OperationCanceledException)
        {
            // The operation was canceled.
        }
        catch (SocketException) when (CancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
        {
            // Unix named pipes can surface cancellation/disposal of WaitForConnection as a socket error.
        }
        catch (InvalidOperationException)
        {
            // The pipe reached an invalid state while shutting down.
        }
        finally
        {
            // Add a final 0 (BinaryLogRecordKind.EndOfFile) into the stream in case the BuildEventArgsReader is waiting for a read.
            Buffer.TryWriteEndOfFile();
            Buffer.CompleteAdding();
        }
    }

    /// <summary>
    /// Reads from the currently connected client until it disconnects.
    /// </summary>
    private void DrainCurrentConnection()
    {
        try
        {
            while (!IsPipeShutdownRequested && Buffer.FillFromStream(PipeStream, CancellationToken))
            {
            }
        }
        catch (IOException)
        {
            // This client broke the stream, for example an MSBuild node that died mid-write. That ends
            // the connection rather than the transport, so a later build submission can still be served.
            // Disposal and cancellation surface as other exception types and are left to propagate.
        }
    }

    /// <inheritdoc/>
    public PipeBuildEventArgs? Read()
    {
        if (Volatile.Read(ref _disposed) != 0 || Buffer.IsCompleted)
        {
            return null;
        }

        lock (_readLock)
        {
            while (true)
            {
                try
                {
                    var args = _eventReader.Read();
                    if (args is not null)
                    {
                        Dispatch(args);
                        return args;
                    }
                }
                catch (EndOfStreamException)
                {
                    // A record ran out of bytes part-way through. Whether that is the end of the transport
                    // or just the end of one client is settled by the boundary check below.
                }
                catch (ObjectDisposedException)
                {
                    // The server was disposed while reading.
                    return null;
                }

                // The record stream ran out. If a client simply went away, anything half-read belonged to
                // it and the bytes that follow are a fresh record stream, so resume rather than stop.
                if (!Buffer.TryConsumeConnectionBoundary())
                {
                    return null;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void ReadAll()
    {
        // Deliberately not stopping at PipeBuildFinishedEventArgs: that marks the end of one MSBuild
        // submission, not the end of the transport, and a build can run several submissions.
        while (Read() is not null)
        {
        }
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellationRegistration.Dispose();
        RequestPipeShutdown();

        if (Volatile.Read(ref _started) != 0 && Thread.CurrentThread.ManagedThreadId != _readerThread.ManagedThreadId)
        {
            _readerThread.Join(ReaderShutdownTimeout);
        }

        lock (_readLock)
        {
            _eventReader.Dispose();
            Buffer.Dispose();
        }
    }

    private void RequestPipeShutdown()
    {
        // Unblock readers immediately. Disposing PipeStream can block on some Unix pipe/socket
        // states, and CancellationToken.Cancel() runs callbacks synchronously, so the transport
        // dispose is intentionally moved to a dedicated background thread.
        Buffer.TryWriteEndOfFile();
        Buffer.CompleteAdding();

        if (Interlocked.Exchange(ref _pipeShutdownRequested, 1) != 0)
        {
            return;
        }

        var pipeDisposeThread = new Thread(DisposePipeStream)
        {
            IsBackground = true,
            Name = "MSBuild pipe logger transport disposer"
        };
        pipeDisposeThread.Start();
    }

    private void DisposePipeStream()
    {
        try
        {
            CancelBlockingCallsOfReader();
            PipeStream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Ends the blocking accept or read the reader thread may be in, before the pipe is disposed.
    /// </summary>
    /// <remarks>
    /// On Windows, disposing a pipe does not interrupt a blocking call on a synchronous handle: the handle
    /// stays open until that call returns, which for a read is whenever the client next writes or leaves.
    /// Cancelling the I/O of the handle does interrupt it. The reader thread can be between two calls when
    /// the request is made, so it is repeated until that thread ends; the thread starts no new call once
    /// the shutdown is requested.
    /// </remarks>
    private void CancelBlockingCallsOfReader()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || PipeStream.IsAsync ||
            Volatile.Read(ref _started) == 0 || Thread.CurrentThread.ManagedThreadId == _readerThread.ManagedThreadId)
        {
            return;
        }

        var deadline = DateTime.UtcNow + ReaderShutdownTimeout;
        while (!_readerThread.Join(BlockingCallCancellationPeriod) && DateTime.UtcNow < deadline)
        {
            if (!TryCancelPendingIO())
            {
                return;
            }
        }
    }

    /// <summary>
    /// Cancels the I/O in progress on the pipe, whichever thread started it. Windows only.
    /// </summary>
    /// <returns><see langword="false"/> if the pipe has no handle to cancel on, or it is already closed.</returns>
    private protected bool TryCancelPendingIO()
    {
        try
        {
            // The result is not checked: it fails with ERROR_NOT_FOUND when nothing is in progress.
            WindowsPipeMethods.CancelIoEx(PipeStream.SafePipeHandle, IntPtr.Zero);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // The pipe has no handle yet.
            return false;
        }
    }
}

/// <summary>
/// The Windows functions the pipe servers call directly.
/// </summary>
internal static class WindowsPipeMethods
{
    /// <summary>
    /// Cancels the I/O in progress on a handle, whichever thread started it, blocking calls included.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CancelIoEx(SafePipeHandle handle, IntPtr overlapped);
}
