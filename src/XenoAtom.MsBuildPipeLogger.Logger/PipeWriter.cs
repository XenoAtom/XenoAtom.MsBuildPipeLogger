// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.Collections.Concurrent;
using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Base class for transports that write serialized MSBuild events to a pipe stream.
/// </summary>
public abstract class PipeWriter : IPipeWriter
{
    // How long the receiver may go without reading anything before the events left to write are dropped.
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    // A large event is written in pieces, so that a receiver that reads it slowly is seen to make progress.
    private const int WriteChunkSize = 64 * 1024;

    private readonly BlockingCollection<BuildEventArgs> _queue = new(new ConcurrentQueue<BuildEventArgs>());

    private readonly ManualResetEventSlim _doneProcessing = new(false);
    private readonly Stream _stream;
    private readonly BinaryWriter _binaryWriter;
    private readonly PipeEventSerializer _serializer;

    // Buffer writes through a memory stream since the args writer does a bunch of small writes
    private readonly MemoryStream _memoryStream = new();

    private readonly TimeSpan _stallTimeout;
    private long _bytesWritten;
    private int _disposed;
    private int _stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeWriter"/> class.
    /// </summary>
    /// <param name="stream">The connected stream to write to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    protected PipeWriter(Stream stream)
        : this(stream, DefaultStallTimeout)
    {
    }

    private protected PipeWriter(Stream stream, TimeSpan stallTimeout)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _stallTimeout = stallTimeout;
        _binaryWriter = new BinaryWriter(_memoryStream);
        _serializer = new PipeEventSerializer();
        var writerThread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "MSBuild pipe logger writer"
        };
        writerThread.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }

        var writerCompleted = WaitForWriter();
        if (!writerCompleted)
        {
            // A broken peer can block a pipe write indefinitely. Disposing the transport unblocks the
            // background writer at the cost of dropping the events that are left.
            ReportDroppedEvents(_stallTimeout);
            DisposeStream();
            writerCompleted = _doneProcessing.Wait(_stallTimeout);
        }

        // Do not wait for the peer to drain the pipe here: a crashed or paused server could otherwise
        // hang MSBuild shutdown indefinitely after all writes have completed.
        DisposeStream();
        _binaryWriter.Dispose();
        _memoryStream.Dispose();
        if (writerCompleted)
        {
            _queue.Dispose();
            _doneProcessing.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Write(BuildEventArgs e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        try
        {
            _queue.Add(e);
        }
        catch (ObjectDisposedException)
        {
            // The writer is shutting down and no longer accepts events.
        }
        catch (InvalidOperationException)
        {
            // The writer is shutting down and no longer accepts events.
        }
    }

    /// <summary>
    /// Waits for the background writer to write the events that are queued.
    /// </summary>
    /// <remarks>
    /// A receiver can be slow without being broken, for example in a host process that is short of threads
    /// while many builds run. The wait therefore goes on for as long as the receiver keeps reading, and
    /// only ends when nothing was written for the whole stall timeout.
    /// </remarks>
    /// <returns><see langword="true"/> if every queued event was written or the transport failed;
    /// <see langword="false"/> if the receiver stopped reading.</returns>
    private bool WaitForWriter()
    {
        while (true)
        {
            var bytesWritten = Interlocked.Read(ref _bytesWritten);
            if (_doneProcessing.Wait(_stallTimeout))
            {
                return true;
            }

            if (Interlocked.Read(ref _bytesWritten) == bytesWritten)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Reports that the receiver stopped reading and that the remaining events are dropped.
    /// </summary>
    /// <remarks>
    /// The build is not failed for it, but a receiver that then misses the end of the build needs a trace
    /// of the reason, and the standard error of the build process is the only place left to leave one.
    /// </remarks>
    private protected virtual void ReportDroppedEvents(TimeSpan stallTimeout)
    {
        try
        {
            Console.Error.WriteLine(
                $"XenoAtom.MsBuildPipeLogger: the receiver of the pipe read nothing for {stallTimeout.TotalSeconds:0.#} seconds. " +
                "The build events that were not sent yet are dropped.");
        }
        catch (Exception)
        {
            // Reporting is best effort.
        }
    }

    private void ProcessQueue()
    {
        try
        {
            foreach (var eventArgs in _queue.GetConsumingEnumerable())
            {
                // Reset the memory stream (but reuse the memory)
                _memoryStream.Seek(0, SeekOrigin.Begin);
                _memoryStream.SetLength(0);

                try
                {
                    // Buffer to the memory stream
                    _serializer.Write(_binaryWriter, eventArgs);
                    _binaryWriter.Flush();
                }
                catch (Exception)
                {
                    // A single event that can't be serialized (e.g. a type or member that is missing on
                    // an older MSBuild) must not tear down the whole stream. Nothing is written to the
                    // pipe until serialization fully succeeds, so the transport stays consistent; drop
                    // just this event and keep going.
                    continue;
                }

                // ...then write that to the pipe
                var buffer = _memoryStream.GetBuffer();
                var length = (int)_memoryStream.Length;
                for (var offset = 0; offset < length; offset += WriteChunkSize)
                {
                    var count = Math.Min(WriteChunkSize, length - offset);
                    _stream.Write(buffer, offset, count);
                    Interlocked.Add(ref _bytesWritten, count);
                }

                _stream.Flush();
            }
        }
        catch (ObjectDisposedException)
        {
            // The transport or queue was disposed while the writer was shutting down.
        }
        catch (InvalidOperationException)
        {
            // The queue was completed or disposed while the writer was shutting down.
        }
        catch (IOException)
        {
            // The server closed the transport.
        }
        catch (Exception)
        {
            // A logger transport failure must not tear down the build process.
        }
        finally
        {
            Volatile.Write(ref _stopped, 1);
            _doneProcessing.Set();
        }
    }

    private void DisposeStream()
    {
        try
        {
            _stream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
