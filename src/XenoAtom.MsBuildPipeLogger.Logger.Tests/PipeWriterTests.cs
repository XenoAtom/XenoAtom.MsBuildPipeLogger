// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Logger.Tests;

[TestClass]
public class PipeWriterTests
{
    [TestMethod]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        var writer = new TestPipeWriter(new MemoryStream());

        writer.Dispose();
        writer.Dispose();
    }

    [TestMethod]
    public void Write_AfterDispose_DoesNotThrow()
    {
        var writer = new TestPipeWriter(new MemoryStream());
        writer.Dispose();

        writer.Write(CreateMessage());
    }

    [TestMethod]
    public void Dispose_WhenTransportWriteThrows_DoesNotThrow()
    {
        var writer = new TestPipeWriter(new ThrowingWriteStream());

        writer.Write(CreateMessage());
        writer.Dispose();
    }

    private static BuildMessageEventArgs CreateMessage() =>
        new("message", "help", "sender", MessageImportance.Normal);

    private sealed class TestPipeWriter : PipeWriter
    {
        public TestPipeWriter(Stream stream)
            : base(stream)
        {
        }
    }

    private sealed class ThrowingWriteStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The transport rejected the write.");
        }
    }
}
