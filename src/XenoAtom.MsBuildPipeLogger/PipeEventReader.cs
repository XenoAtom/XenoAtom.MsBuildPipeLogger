// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Reads records in the XenoAtom pipe wire format and materializes them as <see cref="PipeBuildEventArgs"/>.
/// Each record is <c>[kind][payload-length][payload]</c>; unknown record kinds are skipped and any trailing
/// bytes in a known record are ignored, so a newer writer stays readable by an older reader (and vice versa).
/// </summary>
internal sealed class PipeEventReader : IDisposable
{
    private readonly BinaryReader _reader;

    public PipeEventReader(Stream stream) => _reader = new BinaryReader(stream);

    public PipeBuildEventArgs? Read()
    {
        PipeRecordKind kind;
        try
        {
            kind = (PipeRecordKind)_reader.Read7Bit();
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        if (kind == PipeRecordKind.EndOfFile)
        {
            return null;
        }

        var length = _reader.Read7Bit();
        var payload = _reader.ReadBytes(length);
        if (payload.Length < length)
        {
            // The stream ended part-way through a record.
            return null;
        }

        using var memory = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(memory);
        try
        {
            var b = WireBaseFields.Read(reader);
            return Materialize(kind, b, reader);
        }
        catch (EndOfStreamException)
        {
            // A record we cannot fully parse (e.g. a future record kind with a layout this reader does
            // not understand). The length prefix already advanced the underlying stream to the next
            // record, so surface a placeholder and keep reading rather than aborting the whole stream.
            return new PipeCustomBuildEventArgs();
        }
    }

    public void Dispose() => _reader.Dispose();

    private static PipeBuildEventArgs Materialize(PipeRecordKind kind, WireBaseFields b, BinaryReader r) => kind switch
    {
        PipeRecordKind.BuildStarted => ApplyBase(new PipeBuildStartedEventArgs { BuildEnvironment = ReadProperties(r) }, b),
        PipeRecordKind.BuildFinished => ApplyBase(new PipeBuildFinishedEventArgs { Succeeded = r.ReadBoolean() }, b),
        PipeRecordKind.ProjectStarted => ApplyBase(
            new PipeProjectStartedEventArgs
            {
                ProjectId = r.Read7Bit(),
                ProjectFile = r.ReadNullableString(),
                TargetNames = r.ReadNullableString(),
                ToolsVersion = r.ReadNullableString(),
                GlobalProperties = ReadProperties(r),
                Properties = ReadProperties(r),
                Items = ReadItems(r),
            }, b),
        PipeRecordKind.ProjectFinished => ApplyBase(
            new PipeProjectFinishedEventArgs { ProjectFile = r.ReadNullableString(), Succeeded = r.ReadBoolean() }, b),
        PipeRecordKind.ProjectEvaluationStarted => ApplyBase(
            new PipeProjectEvaluationStartedEventArgs { ProjectFile = r.ReadNullableString() }, b),
        PipeRecordKind.ProjectEvaluationFinished => ApplyBase(
            new PipeProjectEvaluationFinishedEventArgs
            {
                ProjectFile = r.ReadNullableString(),
                Properties = ReadProperties(r),
                Items = ReadItems(r),
            }, b),
        PipeRecordKind.TargetStarted => ApplyBase(
            new PipeTargetStartedEventArgs
            {
                TargetName = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                TargetFile = r.ReadNullableString(),
                ParentTarget = r.ReadNullableString(),
            }, b),
        PipeRecordKind.TargetFinished => ApplyBase(
            new PipeTargetFinishedEventArgs
            {
                TargetName = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                TargetFile = r.ReadNullableString(),
                Succeeded = r.ReadBoolean(),
            }, b),
        PipeRecordKind.TaskStarted => ApplyBase(
            new PipeTaskStartedEventArgs
            {
                TaskName = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                TaskFile = r.ReadNullableString(),
            }, b),
        PipeRecordKind.TaskFinished => ApplyBase(
            new PipeTaskFinishedEventArgs
            {
                TaskName = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                TaskFile = r.ReadNullableString(),
                Succeeded = r.ReadBoolean(),
            }, b),
        PipeRecordKind.Message => ApplyBase(ReadMessageFields(new PipeBuildMessageEventArgs(), r), b),
        PipeRecordKind.TaskCommandLine => ApplyBase(ReadTaskCommandLine(r), b),
        PipeRecordKind.Error => ApplyBase(
            new PipeBuildErrorEventArgs
            {
                Subcategory = r.ReadNullableString(),
                Code = r.ReadNullableString(),
                File = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                LineNumber = r.Read7Bit(),
                ColumnNumber = r.Read7Bit(),
                EndLineNumber = r.Read7Bit(),
                EndColumnNumber = r.Read7Bit(),
            }, b),
        PipeRecordKind.Warning => ApplyBase(
            new PipeBuildWarningEventArgs
            {
                Subcategory = r.ReadNullableString(),
                Code = r.ReadNullableString(),
                File = r.ReadNullableString(),
                ProjectFile = r.ReadNullableString(),
                LineNumber = r.Read7Bit(),
                ColumnNumber = r.Read7Bit(),
                EndLineNumber = r.Read7Bit(),
                EndColumnNumber = r.Read7Bit(),
            }, b),

        PipeRecordKind.TaskParameter => ApplyBase(ReadTaskParameter(r), b),

        // Unknown or Custom: the payload beyond the base fields is intentionally ignored.
        _ => ApplyBase(new PipeCustomBuildEventArgs { EventType = ReadOptionalTypeName(r) }, b),
    };

    private static PipeTaskParameterEventArgs ReadTaskParameter(BinaryReader r)
    {
        var kind = (PipeTaskParameterKind)r.Read7Bit();
        var itemType = r.ReadNullableString();
        return new PipeTaskParameterEventArgs
        {
            Kind = kind,
            ItemType = itemType,
            Items = ReadTaskParameterItems(r, itemType),
        };
    }

    private static IReadOnlyList<PipeItem> ReadTaskParameterItems(BinaryReader r, string? itemType)
    {
        var count = r.Read7Bit();
        if (count == 0)
        {
            return Array.Empty<PipeItem>();
        }

        var items = new PipeItem[count];
        for (var i = 0; i < count; i++)
        {
            var spec = r.ReadString();
            items[i] = new PipeItem(itemType ?? string.Empty, spec, ReadProperties(r));
        }

        return items;
    }

    private static PipeTaskCommandLineEventArgs ReadTaskCommandLine(BinaryReader r)
    {
        var e = new PipeTaskCommandLineEventArgs
        {
            CommandLine = r.ReadNullableString(),
            TaskName = r.ReadNullableString(),
        };
        return ReadMessageFields(e, r);
    }

    private static T ReadMessageFields<T>(T e, BinaryReader r)
        where T : PipeBuildMessageEventArgs
    {
        e.Importance = (PipeMessageImportance)r.Read7Bit();
        e.Subcategory = r.ReadNullableString();
        e.Code = r.ReadNullableString();
        e.File = r.ReadNullableString();
        e.ProjectFile = r.ReadNullableString();
        e.LineNumber = r.Read7Bit();
        e.ColumnNumber = r.Read7Bit();
        e.EndLineNumber = r.Read7Bit();
        e.EndColumnNumber = r.Read7Bit();
        return e;
    }

    private static string? ReadOptionalTypeName(BinaryReader r) =>
        r.BaseStream.Position < r.BaseStream.Length ? r.ReadNullableString() : null;

    private static IReadOnlyList<PipeProperty> ReadProperties(BinaryReader r)
    {
        var count = r.Read7Bit();
        if (count == 0)
        {
            return Array.Empty<PipeProperty>();
        }

        var properties = new PipeProperty[count];
        for (var i = 0; i < count; i++)
        {
            var name = r.ReadString();
            var value = r.ReadNullableString();
            properties[i] = new PipeProperty(name, value);
        }

        return properties;
    }

    private static IReadOnlyList<PipeItem> ReadItems(BinaryReader r)
    {
        var count = r.Read7Bit();
        if (count == 0)
        {
            return Array.Empty<PipeItem>();
        }

        var items = new PipeItem[count];
        for (var i = 0; i < count; i++)
        {
            var itemType = r.ReadString();
            var evaluatedInclude = r.ReadString();
            items[i] = new PipeItem(itemType, evaluatedInclude, ReadProperties(r));
        }

        return items;
    }

    private static T ApplyBase<T>(T e, WireBaseFields b)
        where T : PipeBuildEventArgs
    {
        e.Message = b.Message;
        e.HelpKeyword = b.HelpKeyword;
        e.SenderName = b.SenderName;
        e.Timestamp = b.Timestamp;
        e.ThreadId = b.ThreadId;
        e.BuildEventContext = b.HasContext
            ? new PipeBuildEventContext(b.SubmissionId, b.NodeId, b.EvaluationId, b.ProjectInstanceId, b.ProjectContextId, b.TargetId, b.TaskId)
            : null;
        return e;
    }
}
