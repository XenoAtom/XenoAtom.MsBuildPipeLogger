using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace MsBuildPipeLogger
{
    /// <summary>
    /// A server for receiving MSBuild logging events over a named pipe.
    /// </summary>
    public class NamedPipeLoggerServer : PipeLoggerServer<NamedPipeServerStream>
    {
        private const int CancelConnectionTimeoutMilliseconds = 1000;

        private readonly InterlockedBool _connected = new InterlockedBool(false);
        private readonly CancellationTokenRegistration _cancellationRegistration;

        /// <summary>
        /// Gets the named pipe name.
        /// </summary>
        public string PipeName { get; }

        /// <summary>
        /// Creates a named pipe server for receiving MSBuild logging events.
        /// </summary>
        /// <param name="pipeName">The name of the pipe to create.</param>
        /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
        public NamedPipeLoggerServer(string pipeName)
            : this(pipeName, CancellationToken.None)
        {
        }

        /// <summary>
        /// Creates a named pipe server for receiving MSBuild logging events.
        /// </summary>
        /// <param name="pipeName">The name of the pipe to create.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
        /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
        public NamedPipeLoggerServer(string pipeName, CancellationToken cancellationToken)
            : base(CreatePipe(pipeName), cancellationToken, false)
        {
            PipeName = pipeName;
            StartReading();
            _cancellationRegistration = CancellationToken.Register(CancelConnectionWait);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            _cancellationRegistration.Dispose();
            base.Dispose();
        }

        /// <inheritdoc/>
        protected override void Connect()
        {
            PipeStream.WaitForConnection();
            _connected.Set();
        }

        private static NamedPipeServerStream CreatePipe(string pipeName)
        {
            if (pipeName is null)
            {
                throw new ArgumentNullException(nameof(pipeName));
            }
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("The pipe name cannot be empty or whitespace.", nameof(pipeName));
            }

            return new NamedPipeServerStream(pipeName, PipeDirection.In);
        }

        private void CancelConnectionWait()
        {
            if (_connected.Set())
            {
                return;
            }

            try
            {
                // This stops WaitForConnection by connecting a dummy client. Checking IsConnected is not
                // reliable here because a quick connect/disconnect may never be observed as connected.
                using (NamedPipeClientStream pipeStream = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipeStream.Connect(CancelConnectionTimeoutMilliseconds);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (TimeoutException)
            {
            }
        }
    }
}
