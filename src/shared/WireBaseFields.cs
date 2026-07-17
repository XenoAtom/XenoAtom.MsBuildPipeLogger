// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// The fields common to every MSBuild event, serialized at the start of each record payload as a
/// length-prefixed header: <c>[base-header-length varint][base fields]</c>, followed by the
/// event-specific fields. Shared by the logger (which fills it from
/// <c>Microsoft.Build.Framework.BuildEventArgs</c>) and the receiver (which maps it onto
/// <c>PipeBuildEventArgs</c>), so the field order is defined in exactly one place. Fields are
/// append-only: never remove or reorder, only add new ones at the end. Because the header carries
/// its own length, a reader always lands on the event-specific fields no matter how many base
/// fields either side knows about: trailing unknown header bytes written by a newer writer are
/// simply skipped.
/// </summary>
internal struct WireBaseFields
{
    public string? Message;
    public string? HelpKeyword;
    public string? SenderName;
    public DateTime Timestamp;
    public int ThreadId;
    public bool HasContext;
    public int SubmissionId;
    public int NodeId;
    public int EvaluationId;
    public int ProjectInstanceId;
    public int ProjectContextId;
    public int TargetId;
    public int TaskId;

    /// <summary>
    /// Writes the base fields as a length-prefixed header into <paramref name="writer"/>. The caller
    /// supplies a reusable scratch stream and a <see cref="BinaryWriter"/> over that same stream,
    /// used to measure the header, so writing a record incurs no per-event allocation.
    /// </summary>
    public void WriteFramed(BinaryWriter writer, MemoryStream scratch, BinaryWriter scratchWriter)
    {
        scratch.SetLength(0);
        scratch.Position = 0;
        Write(scratchWriter);
        scratchWriter.Flush();
        writer.Write7Bit((int)scratch.Length);
        writer.Write(scratch.GetBuffer(), 0, (int)scratch.Length);
    }

    /// <summary>
    /// Reads a length-prefixed base header written by <see cref="WriteFramed"/>. The fields are
    /// parsed from a bounded view over exactly the declared header bytes, so trailing header bytes
    /// appended by a newer writer are skipped, and a shorter header from an older writer can never
    /// over-read into the event-specific fields that follow.
    /// </summary>
    public static WireBaseFields ReadFramed(BinaryReader reader)
    {
        var length = reader.Read7Bit();
        if (length < 0 || length > WireIO.MaxFrameLength)
        {
            throw new InvalidDataException($"Base header length {length} is negative or exceeds the {WireIO.MaxFrameLength} byte limit.");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length < length)
        {
            throw new EndOfStreamException("The record payload ended before the declared base header length.");
        }

        using var memory = new MemoryStream(bytes, writable: false);
        using var bounded = new BinaryReader(memory);
        return Read(bounded);
    }

    public void Write(BinaryWriter writer)
    {
        writer.WriteNullable(Message);
        writer.WriteNullable(HelpKeyword);
        writer.WriteNullable(SenderName);
        writer.WriteDateTime(Timestamp);
        writer.Write7Bit(ThreadId);
        writer.Write(HasContext);
        if (HasContext)
        {
            writer.Write7Bit(SubmissionId);
            writer.Write7Bit(NodeId);
            writer.Write7Bit(EvaluationId);
            writer.Write7Bit(ProjectInstanceId);
            writer.Write7Bit(ProjectContextId);
            writer.Write7Bit(TargetId);
            writer.Write7Bit(TaskId);
        }
    }

    public static WireBaseFields Read(BinaryReader reader)
    {
        var fields = new WireBaseFields
        {
            Message = reader.ReadNullableString(),
            HelpKeyword = reader.ReadNullableString(),
            SenderName = reader.ReadNullableString(),
            Timestamp = reader.ReadDateTime(),
            ThreadId = reader.Read7Bit(),
            HasContext = reader.ReadBoolean(),
        };

        if (fields.HasContext)
        {
            fields.SubmissionId = reader.Read7Bit();
            fields.NodeId = reader.Read7Bit();
            fields.EvaluationId = reader.Read7Bit();
            fields.ProjectInstanceId = reader.Read7Bit();
            fields.ProjectContextId = reader.Read7Bit();
            fields.TargetId = reader.Read7Bit();
            fields.TaskId = reader.Read7Bit();
        }

        return fields;
    }
}
