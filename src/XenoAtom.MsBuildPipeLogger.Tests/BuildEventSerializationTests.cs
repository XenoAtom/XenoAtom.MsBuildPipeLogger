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
