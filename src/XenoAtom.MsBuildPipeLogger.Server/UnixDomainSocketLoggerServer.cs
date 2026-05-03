#if NET5_0_OR_GREATER
using System;
using System.IO;
using System.Threading;

namespace MsBuildPipeLogger
{
    /// <summary>
    /// A server for receiving MSBuild logging events over a Unix domain socket.
    /// </summary>
    public class UnixDomainSocketLoggerServer : PipeLoggerServer<Stream>
    {
        /// <summary>
        /// Gets the Unix domain socket path.
        /// </summary>
        public string SocketPath { get; }

        /// <summary>
        /// Creates a Unix domain socket server for receiving MSBuild logging events.
        /// </summary>
        /// <param name="socketPath">The Unix domain socket path to create.</param>
        /// <exception cref="ArgumentException"><paramref name="socketPath"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="socketPath"/> is <see langword="null"/>.</exception>
        public UnixDomainSocketLoggerServer(string socketPath)
            : this(socketPath, CancellationToken.None)
        {
        }

        /// <summary>
        /// Creates a Unix domain socket server for receiving MSBuild logging events.
        /// </summary>
        /// <param name="socketPath">The Unix domain socket path to create.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel accept and read operations if triggered.</param>
        /// <exception cref="ArgumentException"><paramref name="socketPath"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="socketPath"/> is <see langword="null"/>.</exception>
        public UnixDomainSocketLoggerServer(string socketPath, CancellationToken cancellationToken)
            : base(new UnixDomainSocketServerStream(socketPath), cancellationToken, false)
        {
            SocketPath = socketPath;
            StartReading();
        }

        /// <inheritdoc/>
        protected override void Connect() => ((UnixDomainSocketServerStream)PipeStream).Connect(CancellationToken);
    }
}
#endif
