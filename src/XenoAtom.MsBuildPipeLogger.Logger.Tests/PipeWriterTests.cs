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

    [TestMethod]
    public void Dispose_WaitsForAReceiverThatIsSlowButStillReading()
    {
        // Every write takes a while, and together they take far longer than the stall timeout.
        var stream = new SlowWriteStream(TimeSpan.FromMilliseconds(40));
        var writer = new TestPipeWriter(stream, TimeSpan.FromMilliseconds(400));
        const int messageCount = 40;
        for (var i = 0; i < messageCount; i++)
        {
            writer.Write(CreateMessage());
        }

        writer.Dispose();

        Assert.AreEqual(messageCount, stream.WriteCount, "Events were dropped although the receiver was still reading.");
        Assert.AreEqual(0, writer.DroppedEventsReports);
    }

    [TestMethod]
    public void Dispose_GivesUpAndReportsItWhenTheReceiverStopsReading()
    {
        var stream = new StalledWriteStream();
        var writer = new TestPipeWriter(stream, TimeSpan.FromMilliseconds(200));
        writer.Write(CreateMessage());
        writer.Write(CreateMessage());

        writer.Dispose();

        Assert.AreEqual(1, writer.DroppedEventsReports);
    }

    private static BuildMessageEventArgs CreateMessage() =>
        new("message", "help", "sender", MessageImportance.Normal);

    private sealed class TestPipeWriter : PipeWriter
    {
        public TestPipeWriter(Stream stream)
            : base(stream)
        {
        }

        public TestPipeWriter(Stream stream, TimeSpan stallTimeout)
            : base(stream, stallTimeout)
        {
        }

        public int DroppedEventsReports { get; private set; }

        private protected override void ReportDroppedEvents(TimeSpan stallTimeout) => DroppedEventsReports++;
    }

    private sealed class SlowWriteStream : MemoryStream
    {
        private readonly TimeSpan _delay;

        public SlowWriteStream(TimeSpan delay) => _delay = delay;

        public int WriteCount { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delay);
            WriteCount++;
        }
    }

    /// <summary>
    /// A transport whose receiver never reads: a write blocks until the stream is disposed.
    /// </summary>
    private sealed class StalledWriteStream : MemoryStream
    {
        private readonly ManualResetEventSlim _disposed = new(false);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _disposed.Wait();
            throw new ObjectDisposedException(nameof(StalledWriteStream));
        }

        protected override void Dispose(bool disposing)
        {
            _disposed.Set();
            base.Dispose(disposing);
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
