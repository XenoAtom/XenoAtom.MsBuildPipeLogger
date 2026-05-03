using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace MsBuildPipeLogger
{
    /// <summary>
    /// Receives MSBuild logging events over a pipe. This is the base class for <see cref="AnonymousPipeLoggerServer"/>
    /// and <see cref="NamedPipeLoggerServer"/>.
    /// </summary>
    /// <typeparam name="TPipeStream">The concrete pipe stream type.</typeparam>
    public abstract class PipeLoggerServer<TPipeStream> : EventArgsDispatcher, IPipeLoggerServer
        where TPipeStream : PipeStream
    {
        private readonly BinaryReader _binaryReader;
        private readonly BuildEventArgsReader _buildEventArgsReader;

        internal PipeBuffer Buffer { get; } = new PipeBuffer();

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
        {
            PipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
            _binaryReader = new BinaryReader(Buffer);
            _buildEventArgsReader = new BuildEventArgsReader(_binaryReader, GetBinaryLoggerFileFormatVersion());
            CancellationToken = cancellationToken;

            Thread readerThread = new Thread(() =>
            {
                try
                {
                    Connect();
                    while (Buffer.FillFromStream(PipeStream, CancellationToken))
                    {
                    }
                }
                catch (IOException)
                {
                    // The client broke the stream so we're done
                }
                catch (ObjectDisposedException)
                {
                    // The pipe was disposed
                }

                // Add a final 0 (BinaryLogRecordKind.EndOfFile) into the stream in case the BuildEventArgsReader is waiting for a read
                Buffer.Write(new byte[1] { 0 }, 0, 1);

                Buffer.CompleteAdding();
            })
            {
                IsBackground = true
            };

            readerThread.Start();
        }

        /// <summary>
        /// Connects the server-side pipe stream to a client.
        /// </summary>
        protected abstract void Connect();

        private static int GetBinaryLoggerFileFormatVersion()
        {
            FieldInfo fileFormatVersionField = typeof(BinaryLogger).GetField(
                "FileFormatVersion",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(BinaryLogger).FullName ?? typeof(BinaryLogger).Name, "FileFormatVersion");

            object? fileFormatVersion = fileFormatVersionField.GetValue(null);
            if (fileFormatVersion is not int version)
            {
                throw new InvalidOperationException(
                    $"Field '{typeof(BinaryLogger).FullName}.FileFormatVersion' must be an integer.");
            }

            return version;
        }

        /// <inheritdoc/>
        public BuildEventArgs? Read()
        {
            if (Buffer.IsCompleted)
            {
                return null;
            }

            try
            {
                BuildEventArgs? args = _buildEventArgsReader.Read();
                if (args is not null)
                {
                    Dispatch(args);
                    return args;
                }
            }
            catch (EndOfStreamException)
            {
                // The stream may have been closed or otherwise stopped
            }

            return null;
        }

        /// <inheritdoc/>
        public void ReadAll()
        {
            BuildEventArgs? args = Read();
            while (args is not null)
            {
                if (args is BuildFinishedEventArgs)
                {
                    return;
                }
                args = Read();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _buildEventArgsReader.Dispose();
            _binaryReader.Dispose();
            Buffer.Dispose();
            PipeStream.Dispose();
        }
    }
}