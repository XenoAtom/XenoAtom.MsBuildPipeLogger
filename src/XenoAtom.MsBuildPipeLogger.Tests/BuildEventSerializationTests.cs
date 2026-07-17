// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;
using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Tests;

[TestClass]
public class BuildEventSerializationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(100000)]
    public void RoundTripsBuildMessages(int messageCount)
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();

        BuildEventAssertions.WriteEvents(new SerializerPipeWriter(serializer, binaryWriter), messageCount);
        binaryWriter.Flush();

        memory.Position = 0;
        var events = new List<PipeBuildEventArgs>();
        using (var reader = new PipeEventReader(memory))
        {
            PipeBuildEventArgs? eventArgs;
            while ((eventArgs = reader.Read()) is not null)
            {
                events.Add(eventArgs);
            }
        }

        BuildEventAssertions.AssertEvents(events, messageCount);
    }

    [TestMethod]
    public void SkipsUnknownRecordKindsAndPreservesFollowingEvents()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();

        // A known event, an unknown/future record kind carrying a payload, then another known event.
        serializer.Write(binaryWriter, new BuildStartedEventArgs("Testing", "help"));
        Write7Bit(binaryWriter, 9999); // an unknown record kind
        Write7Bit(binaryWriter, 3); // payload length
        binaryWriter.Write(new byte[] { 1, 2, 3 });
        serializer.Write(binaryWriter, new BuildFinishedEventArgs("Finished", "help", true));
        binaryWriter.Flush();

        memory.Position = 0;
        var events = new List<PipeBuildEventArgs>();
        using (var reader = new PipeEventReader(memory))
        {
            PipeBuildEventArgs? eventArgs;
            while ((eventArgs = reader.Read()) is not null)
            {
                events.Add(eventArgs);
            }
        }

        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOfType(events[0], typeof(PipeBuildStartedEventArgs));
        Assert.IsInstanceOfType(events[1], typeof(PipeCustomBuildEventArgs));
        Assert.IsInstanceOfType(events[2], typeof(PipeBuildFinishedEventArgs));
    }

    [TestMethod]
    public void SkipsUnknownTrailingBaseHeaderBytes()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();

        // Hand-craft a BuildFinished record whose base header declares MORE bytes than the base
        // fields this reader knows about, as if written by a future writer that appended new base
        // fields. The reader must still land on the event-specific fields that follow.
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var baseFields = BuildBaseFields("hello from the future", timestamp);
        var futureBaseFields = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }; // unknown to this reader

        using var payload = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payload);
        Write7Bit(payloadWriter, baseFields.Length + futureBaseFields.Length);
        payloadWriter.Write(baseFields);
        payloadWriter.Write(futureBaseFields);
        payloadWriter.Write(true); // BuildFinished-specific field: Succeeded
        payloadWriter.Flush();

        Write7Bit(binaryWriter, 2); // PipeRecordKind.BuildFinished
        Write7Bit(binaryWriter, (int)payload.Length);
        binaryWriter.Write(payload.ToArray());
        serializer.Write(binaryWriter, new BuildStartedEventArgs("Testing", "help"));
        binaryWriter.Flush();

        memory.Position = 0;
        var events = ReadAllEvents(memory);

        Assert.AreEqual(2, events.Count);
        Assert.IsInstanceOfType(events[0], typeof(PipeBuildFinishedEventArgs));
        var finished = (PipeBuildFinishedEventArgs)events[0];
        Assert.AreEqual("hello from the future", finished.Message);
        Assert.AreEqual(timestamp, finished.Timestamp);
        Assert.IsTrue(finished.Succeeded);
        Assert.IsInstanceOfType(events[1], typeof(PipeBuildStartedEventArgs));
    }

    [TestMethod]
    public void UnknownRecordKindWithGarbagePayloadDoesNotAbortTheStream()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();

        serializer.Write(binaryWriter, new BuildStartedEventArgs("Testing", "help"));

        // An unknown record kind whose bytes after the base header do NOT form a valid nullable
        // string: a 0x01 "present" flag followed by a huge varint string length.
        var baseFields = BuildBaseFields("future event", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        using var payload = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payload);
        Write7Bit(payloadWriter, baseFields.Length);
        payloadWriter.Write(baseFields);
        payloadWriter.Write((byte)1);
        Write7Bit(payloadWriter, int.MaxValue);
        payloadWriter.Flush();

        Write7Bit(binaryWriter, 9999); // an unknown record kind
        Write7Bit(binaryWriter, (int)payload.Length);
        binaryWriter.Write(payload.ToArray());

        serializer.Write(binaryWriter, new BuildFinishedEventArgs("Finished", "help", true));
        binaryWriter.Flush();

        memory.Position = 0;
        var events = ReadAllEvents(memory);

        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOfType(events[0], typeof(PipeBuildStartedEventArgs));
        Assert.IsInstanceOfType(events[1], typeof(PipeCustomBuildEventArgs));
        Assert.IsInstanceOfType(events[2], typeof(PipeBuildFinishedEventArgs));
    }

    [TestMethod]
    public void RejectsRecordLengthAboveMaximum()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        Write7Bit(binaryWriter, 11); // PipeRecordKind.Message
        Write7Bit(binaryWriter, 200 * 1024 * 1024); // above the 128 MiB record cap
        binaryWriter.Flush();

        memory.Position = 0;
        using var reader = new PipeEventReader(memory);
        Assert.ThrowsExactly<InvalidDataException>(() => reader.Read());
    }

    [TestMethod]
    public void RejectsNegativeRecordLength()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        Write7Bit(binaryWriter, 11); // PipeRecordKind.Message
        Write7Bit(binaryWriter, -1); // encodes as a 5-byte varint that decodes negative
        binaryWriter.Flush();

        memory.Position = 0;
        using var reader = new PipeEventReader(memory);
        Assert.ThrowsExactly<InvalidDataException>(() => reader.Read());
    }

    [TestMethod]
    public void RejectsOverflowing7BitEncodedLength()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        Write7Bit(binaryWriter, 11); // PipeRecordKind.Message
        // A 5-byte varint whose final byte sets a bit beyond bit 31: the writer can never produce
        // this, and before validation it silently decoded to a garbage value.
        binaryWriter.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x10 });
        binaryWriter.Flush();

        memory.Position = 0;
        using var reader = new PipeEventReader(memory);
        Assert.ThrowsExactly<FormatException>(() => reader.Read());
    }

    private static List<PipeBuildEventArgs> ReadAllEvents(MemoryStream memory)
    {
        var events = new List<PipeBuildEventArgs>();
        using var reader = new PipeEventReader(memory);
        PipeBuildEventArgs? eventArgs;
        while ((eventArgs = reader.Read()) is not null)
        {
            events.Add(eventArgs);
        }

        return events;
    }

    // Writes the base fields exactly as WireBaseFields.Write does (message, null help keyword,
    // null sender, timestamp, thread id 0, no build event context).
    private static byte[] BuildBaseFields(string message, DateTime timestamp)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        WriteNullableString(writer, message); // Message
        WriteNullableString(writer, null); // HelpKeyword
        WriteNullableString(writer, null); // SenderName
        writer.Write(timestamp.Ticks);
        writer.Write((byte)timestamp.Kind);
        Write7Bit(writer, 0); // ThreadId
        writer.Write(false); // HasContext
        writer.Flush();
        return memory.ToArray();
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(false);
        }
        else
        {
            writer.Write(true);
            writer.Write(value);
        }
    }

    private static void Write7Bit(BinaryWriter writer, int value)
    {
        var v = (uint)value;
        while (v >= 0x80)
        {
            writer.Write((byte)(v | 0x80));
            v >>= 7;
        }

        writer.Write((byte)v);
    }

    private sealed class SerializerPipeWriter : IPipeWriter
    {
        private readonly PipeEventSerializer _serializer;
        private readonly BinaryWriter _writer;

        public SerializerPipeWriter(PipeEventSerializer serializer, BinaryWriter writer)
        {
            _serializer = serializer;
            _writer = writer;
        }

        public void Write(BuildEventArgs e) => _serializer.Write(_writer, e);

        public void Dispose()
        {
        }
    }
}
