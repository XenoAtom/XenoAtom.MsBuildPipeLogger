#if NET5_0_OR_GREATER
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MsBuildPipeLogger
{
    internal sealed class UnixDomainSocketServerStream : Stream
    {
        private readonly string _socketPath;
        private readonly Socket _listener;
        private NetworkStream? _stream;

        public UnixDomainSocketServerStream(string socketPath)
        {
            ValidateSocketPath(socketPath);
            _socketPath = socketPath;

            string? directory = Path.GetDirectoryName(socketPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DeleteSocketFile(socketPath);
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                _listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                _listener.Listen(backlog: 1);
            }
            catch
            {
                _listener.Dispose();
                DeleteSocketFile(socketPath);
                throw;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Connect(CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(DisposeListener);
            try
            {
                Socket acceptedSocket = _listener.Accept();
                _stream = new NetworkStream(acceptedSocket, ownsSocket: true);
                DisposeListener();
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(UnixDomainSocketServerStream));
            }
        }

        public override void Flush() => ConnectedStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => ConnectedStream.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ConnectedStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream?.Dispose();
                DisposeListener();
            }

            DeleteSocketFile(_socketPath);
            base.Dispose(disposing);
        }

        private NetworkStream ConnectedStream =>
            _stream ?? throw new InvalidOperationException("The Unix domain socket has not accepted a client connection.");

        private void DisposeListener()
        {
            try
            {
                _listener.Dispose();
            }
            catch (ObjectDisposedException)
            {
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

        private static void DeleteSocketFile(string socketPath)
        {
            try
            {
                File.Delete(socketPath);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
#endif