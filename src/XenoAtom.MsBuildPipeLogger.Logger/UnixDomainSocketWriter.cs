#if NET5_0_OR_GREATER
using System;
using System.IO;
using System.Net.Sockets;

namespace MsBuildPipeLogger
{
    /// <summary>
    /// Writes MSBuild events to a Unix domain socket.
    /// </summary>
    public class UnixDomainSocketWriter : PipeWriter
    {
        /// <summary>
        /// Gets the Unix domain socket path used by the writer.
        /// </summary>
        public string SocketPath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnixDomainSocketWriter"/> class.
        /// </summary>
        /// <param name="socketPath">The Unix domain socket path to connect to.</param>
        /// <exception cref="ArgumentException"><paramref name="socketPath"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="socketPath"/> is <see langword="null"/>.</exception>
        /// <exception cref="SocketException">The socket connection could not be established.</exception>
        public UnixDomainSocketWriter(string socketPath)
            : base(Connect(socketPath))
        {
            SocketPath = socketPath;
        }

        private static Stream Connect(string socketPath)
        {
            ValidateSocketPath(socketPath);
            Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(socketPath));
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static void ValidateSocketPath(string socketPath)
        {
            if (socketPath is null)
            {
                throw new ArgumentNullException(nameof(socketPath));
            }
            if (string.IsNullOrWhiteSpace(socketPath))
            {
                throw new ArgumentException("The socket path cannot be empty or whitespace.", nameof(socketPath));
            }
        }
    }
}
#endif