// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;
using System.Linq;
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
    public void RoundTripsTargetAndProjectFidelityFields()
    {
        var parentContext = new BuildEventContext(
            submissionId: 1, nodeId: 2, evaluationId: 3, projectInstanceId: 4, projectContextId: 5, targetId: 6, taskId: 7);

        var targetOutput = new Microsoft.Build.Utilities.TaskItem("obj/Foo.dll");
        targetOutput.SetMetadata("Culture", "en-US");

        var projectStarted = new ProjectStartedEventArgs(
            projectId: 42,
            message: "Project started",
            helpKeyword: "help",
            projectFile: "Foo.csproj",
            targetNames: "Build",
            properties: null,
            items: null,
            parentBuildEventContext: parentContext);

        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var targetStarted = new TargetStartedEventArgs(
            message: "Target started",
            helpKeyword: "help",
            targetName: "CoreCompile",
            projectFile: "Foo.csproj",
            targetFile: "Microsoft.CSharp.targets",
            parentTarget: "Compile",
            buildReason: TargetBuiltReason.DependsOn,
            eventTimestamp: timestamp);

        var targetFinished = new TargetFinishedEventArgs(
            message: "Target finished",
            helpKeyword: "help",
            targetName: "CoreCompile",
            projectFile: "Foo.csproj",
            targetFile: "Microsoft.CSharp.targets",
            succeeded: true,
            targetOutputs: new[] { targetOutput });

        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();
        serializer.Write(binaryWriter, projectStarted);
        serializer.Write(binaryWriter, targetStarted);
        serializer.Write(binaryWriter, targetFinished);
        binaryWriter.Flush();

        memory.Position = 0;
        var events = ReadAllEvents(memory);

        Assert.AreEqual(3, events.Count);

        var ps = (PipeProjectStartedEventArgs)events[0];
        Assert.IsNotNull(ps.ParentProjectBuildEventContext);
        var parent = ps.ParentProjectBuildEventContext.Value;
        Assert.AreEqual(1, parent.SubmissionId);
        Assert.AreEqual(2, parent.NodeId);
        Assert.AreEqual(3, parent.EvaluationId);
        Assert.AreEqual(4, parent.ProjectInstanceId);
        Assert.AreEqual(5, parent.ProjectContextId);
        Assert.AreEqual(6, parent.TargetId);
        Assert.AreEqual(7, parent.TaskId);

        var ts = (PipeTargetStartedEventArgs)events[1];
        Assert.AreEqual(PipeTargetBuiltReason.DependsOn, ts.BuildReason);

        var tf = (PipeTargetFinishedEventArgs)events[2];
        Assert.AreEqual(1, tf.TargetOutputs.Count);
        Assert.AreEqual("obj/Foo.dll", tf.TargetOutputs[0].EvaluatedInclude);
        Assert.AreEqual(string.Empty, tf.TargetOutputs[0].ItemType);
        var culture = tf.TargetOutputs[0].Metadata.Single(m => m.Name == "Culture");
        Assert.AreEqual("en-US", culture.Value);
    }

    [TestMethod]
    public void PipeTargetBuiltReasonMirrorsMsBuildAndRoundTripsEveryValue()
    {
        // The pipe enum must reproduce Microsoft.Build.Framework.TargetBuiltReason member for
        // member: the serializer writes the MSBuild value as a raw integer, so any divergence
        // mislabels or loses the reason on the wire.
        CollectionAssert.AreEqual(
            Enum.GetNames(typeof(TargetBuiltReason)),
            Enum.GetNames(typeof(PipeTargetBuiltReason)));

        var reasons = (TargetBuiltReason[])Enum.GetValues(typeof(TargetBuiltReason));

        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();
        foreach (var reason in reasons)
        {
            serializer.Write(binaryWriter, new TargetStartedEventArgs(
                message: "Target started",
                helpKeyword: null,
                targetName: "CoreCompile",
                projectFile: "Foo.csproj",
                targetFile: "Microsoft.CSharp.targets",
                parentTarget: null,
                buildReason: reason,
                eventTimestamp: new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        }

        binaryWriter.Flush();

        memory.Position = 0;
        var events = ReadAllEvents(memory);

        Assert.AreEqual(reasons.Length, events.Count);
        for (var i = 0; i < reasons.Length; i++)
        {
            var ts = (PipeTargetStartedEventArgs)events[i];
            Assert.AreEqual((int)reasons[i], (int)ts.BuildReason);
            Assert.AreEqual(reasons[i].ToString(), ts.BuildReason.ToString());
        }
    }

    [TestMethod]
    public void OlderRecordsWithoutAppendedFidelityFieldsDegradeToDefaults()
    {
        // A TargetStarted record produced by a pre-fidelity writer: base header + the four original
        // event-specific fields, with NO trailing BuildReason varint. The reader must default it rather
        // than treating the record as unparseable.
        var baseFields = BuildBaseFields("older target", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        using var payload = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payload);
        Write7Bit(payloadWriter, baseFields.Length);
        payloadWriter.Write(baseFields);
        WriteNullableString(payloadWriter, "CoreCompile"); // TargetName
        WriteNullableString(payloadWriter, "Foo.csproj"); // ProjectFile
        WriteNullableString(payloadWriter, "Microsoft.CSharp.targets"); // TargetFile
        WriteNullableString(payloadWriter, "Compile"); // ParentTarget
        payloadWriter.Flush();

        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        Write7Bit(binaryWriter, 7); // PipeRecordKind.TargetStarted
        Write7Bit(binaryWriter, (int)payload.Length);
        binaryWriter.Write(payload.ToArray());
        binaryWriter.Flush();

        memory.Position = 0;
        var events = ReadAllEvents(memory);

        Assert.AreEqual(1, events.Count);
        var ts = (PipeTargetStartedEventArgs)events[0];
        Assert.AreEqual("CoreCompile", ts.TargetName);
        Assert.AreEqual(PipeTargetBuiltReason.None, ts.BuildReason);
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
    public void MalformedDateTimeInBaseHeaderDoesNotAbortTheStream()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);
        var serializer = new PipeEventSerializer();

        serializer.Write(binaryWriter, new BuildStartedEventArgs("Testing", "help"));

        // A Message record whose base header carries an out-of-range DateTimeKind byte: the reader
        // builds a DateTime from these bytes, which throws ArgumentException. It must degrade to a
        // placeholder and keep reading rather than aborting the whole stream.
        var baseFields = BuildBaseFieldsWithRawKind("bad timestamp", new DateTime(2026, 1, 2, 3, 4, 5).Ticks, rawKind: 0x7F);
        using var payload = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payload);
        Write7Bit(payloadWriter, baseFields.Length);
        payloadWriter.Write(baseFields);
        payloadWriter.Flush();

        Write7Bit(binaryWriter, 11); // PipeRecordKind.Message
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
    public void RejectsBaseHeaderLengthAboveMaximum()
    {
        using var memory = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memory);

        // A record whose payload declares a base-header length above the 128 MiB cap: hostile input
        // that must fail the stream cleanly instead of being treated as a skippable bad record.
        using var payload = new MemoryStream();
        using var payloadWriter = new BinaryWriter(payload);
        Write7Bit(payloadWriter, 200 * 1024 * 1024);
        payloadWriter.Flush();

        Write7Bit(binaryWriter, 2); // PipeRecordKind.BuildFinished
        Write7Bit(binaryWriter, (int)payload.Length);
        binaryWriter.Write(payload.ToArray());
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

    // As BuildBaseFields, but writes an arbitrary raw byte in the DateTimeKind position so a test can
    // exercise a base header the reader cannot turn into a valid DateTime.
    private static byte[] BuildBaseFieldsWithRawKind(string message, long ticks, byte rawKind)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        WriteNullableString(writer, message); // Message
        WriteNullableString(writer, null); // HelpKeyword
        WriteNullableString(writer, null); // SenderName
        writer.Write(ticks);
        writer.Write(rawKind); // out-of-range DateTimeKind
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
