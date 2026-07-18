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
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(30);

    private readonly BlockingCollection<BuildEventArgs> _queue = new(new ConcurrentQueue<BuildEventArgs>());

    private readonly ManualResetEventSlim _doneProcessing = new(false);
    private readonly Stream _stream;
    private readonly BinaryWriter _binaryWriter;
    private readonly PipeEventSerializer _serializer;

    // Buffer writes through a memory stream since the args writer does a bunch of small writes
    private readonly MemoryStream _memoryStream = new();

    private int _disposed;
    private int _stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeWriter"/> class.
    /// </summary>
    /// <param name="stream">The connected stream to write to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    protected PipeWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
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

        var writerCompleted = _doneProcessing.Wait(DisposeTimeout);
        if (!writerCompleted)
        {
            // A broken peer can block a pipe write indefinitely. Disposing the transport unblocks the
            // background writer at the cost of dropping any data that could not be flushed in time.
            DisposeStream();
            writerCompleted = _doneProcessing.Wait(DisposeTimeout);
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

    private void ProcessQueue()
    {
        try
        {
            foreach (var eventArgs in _queue.GetConsumingEnumerable())
            {
                // Reset the memory stream (but reuse the memory)
                _memoryStream.Seek(0, SeekOrigin.Begin);
                _memoryStream.SetLength(0);

                // Buffer to the memory stream
                _serializer.Write(_binaryWriter, eventArgs);
                _binaryWriter.Flush();

                // ...then write that to the pipe
                _memoryStream.WriteTo(_stream);
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
