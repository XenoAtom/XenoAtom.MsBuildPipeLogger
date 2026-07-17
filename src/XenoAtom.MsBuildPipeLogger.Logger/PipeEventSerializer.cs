// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.Collections;
using System.IO;
using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Serializes MSBuild <see cref="BuildEventArgs"/> into the XenoAtom pipe wire format using only MSBuild's
/// public API. Because the format is XenoAtom's own (not the MSBuild binary-log format), it is independent
/// of the MSBuild version running the build and of whatever the receiver was built against.
/// </summary>
internal sealed class PipeEventSerializer
{
    private readonly MemoryStream _scratch = new();
    private readonly BinaryWriter _scratchWriter;

    public PipeEventSerializer() => _scratchWriter = new BinaryWriter(_scratch);

    public void Write(BinaryWriter output, BuildEventArgs e)
    {
        var kind = GetKind(e);

        _scratch.SetLength(0);
        _scratch.Position = 0;
        WriteBase(_scratchWriter, e);
        WriteSpecific(_scratchWriter, kind, e);
        _scratchWriter.Flush();

        output.Write7Bit((int)kind);
        output.Write7Bit((int)_scratch.Length);
        output.Write(_scratch.GetBuffer(), 0, (int)_scratch.Length);
    }

    private static PipeRecordKind GetKind(BuildEventArgs e) => e switch
    {
        BuildStartedEventArgs => PipeRecordKind.BuildStarted,
        BuildFinishedEventArgs => PipeRecordKind.BuildFinished,
        ProjectStartedEventArgs => PipeRecordKind.ProjectStarted,
        ProjectFinishedEventArgs => PipeRecordKind.ProjectFinished,
        ProjectEvaluationStartedEventArgs => PipeRecordKind.ProjectEvaluationStarted,
        ProjectEvaluationFinishedEventArgs => PipeRecordKind.ProjectEvaluationFinished,
        TargetStartedEventArgs => PipeRecordKind.TargetStarted,
        TargetFinishedEventArgs => PipeRecordKind.TargetFinished,
        TaskStartedEventArgs => PipeRecordKind.TaskStarted,
        TaskFinishedEventArgs => PipeRecordKind.TaskFinished,
        TaskCommandLineEventArgs => PipeRecordKind.TaskCommandLine, // before BuildMessageEventArgs
        TaskParameterEventArgs => PipeRecordKind.TaskParameter, // before BuildMessageEventArgs
        BuildMessageEventArgs => PipeRecordKind.Message,
        BuildErrorEventArgs => PipeRecordKind.Error,
        BuildWarningEventArgs => PipeRecordKind.Warning,
        _ => PipeRecordKind.Custom,
    };

    private static void WriteBase(BinaryWriter w, BuildEventArgs e)
    {
        var fields = new WireBaseFields
        {
            Message = TryGetMessage(e),
            HelpKeyword = e.HelpKeyword,
            SenderName = e.SenderName,
            Timestamp = e.Timestamp,
            ThreadId = e.ThreadId,
        };

        var context = e.BuildEventContext;
        if (context is not null)
        {
            fields.HasContext = true;
            fields.SubmissionId = context.SubmissionId;
            fields.NodeId = context.NodeId;
            fields.EvaluationId = context.EvaluationId;
            fields.ProjectInstanceId = context.ProjectInstanceId;
            fields.ProjectContextId = context.ProjectContextId;
            fields.TargetId = context.TargetId;
            fields.TaskId = context.TaskId;
        }

        fields.Write(w);
    }

    private static void WriteSpecific(BinaryWriter w, PipeRecordKind kind, BuildEventArgs e)
    {
        switch (kind)
        {
            case PipeRecordKind.BuildStarted:
                WriteProperties(w, ((BuildStartedEventArgs)e).BuildEnvironment);
                break;
            case PipeRecordKind.BuildFinished:
                w.Write(((BuildFinishedEventArgs)e).Succeeded);
                break;
            case PipeRecordKind.ProjectStarted:
                var ps = (ProjectStartedEventArgs)e;
                w.Write7Bit(ps.ProjectId);
                w.WriteNullable(ps.ProjectFile);
                w.WriteNullable(ps.TargetNames);
                w.WriteNullable(ps.ToolsVersion);
                WriteProperties(w, ps.GlobalProperties);
                WriteProperties(w, ps.Properties);
                WriteItems(w, ps.Items);
                break;
            case PipeRecordKind.ProjectFinished:
                var pf = (ProjectFinishedEventArgs)e;
                w.WriteNullable(pf.ProjectFile);
                w.Write(pf.Succeeded);
                break;
            case PipeRecordKind.ProjectEvaluationStarted:
                w.WriteNullable(((ProjectEvaluationStartedEventArgs)e).ProjectFile);
                break;
            case PipeRecordKind.ProjectEvaluationFinished:
                var pe = (ProjectEvaluationFinishedEventArgs)e;
                w.WriteNullable(pe.ProjectFile);
                WriteProperties(w, pe.Properties);
                WriteItems(w, pe.Items);
                break;
            case PipeRecordKind.TargetStarted:
                var ts = (TargetStartedEventArgs)e;
                w.WriteNullable(ts.TargetName);
                w.WriteNullable(ts.ProjectFile);
                w.WriteNullable(ts.TargetFile);
                w.WriteNullable(ts.ParentTarget);
                break;
            case PipeRecordKind.TargetFinished:
                var tf = (TargetFinishedEventArgs)e;
                w.WriteNullable(tf.TargetName);
                w.WriteNullable(tf.ProjectFile);
                w.WriteNullable(tf.TargetFile);
                w.Write(tf.Succeeded);
                break;
            case PipeRecordKind.TaskStarted:
                var ks = (TaskStartedEventArgs)e;
                w.WriteNullable(ks.TaskName);
                w.WriteNullable(ks.ProjectFile);
                w.WriteNullable(ks.TaskFile);
                break;
            case PipeRecordKind.TaskFinished:
                var kf = (TaskFinishedEventArgs)e;
                w.WriteNullable(kf.TaskName);
                w.WriteNullable(kf.ProjectFile);
                w.WriteNullable(kf.TaskFile);
                w.Write(kf.Succeeded);
                break;
            case PipeRecordKind.TaskCommandLine:
                var cl = (TaskCommandLineEventArgs)e;
                w.WriteNullable(cl.CommandLine);
                w.WriteNullable(cl.TaskName);
                WriteMessageFields(w, cl);
                break;
            case PipeRecordKind.TaskParameter:
                var tp = (TaskParameterEventArgs)e;
                w.Write7Bit((int)tp.Kind);
                w.WriteNullable(tp.ItemType);
                WriteTaskParameterItems(w, tp.Items);
                break;
            case PipeRecordKind.Message:
                WriteMessageFields(w, (BuildMessageEventArgs)e);
                break;
            case PipeRecordKind.Error:
                var er = (BuildErrorEventArgs)e;
                w.WriteNullable(er.Subcategory);
                w.WriteNullable(er.Code);
                w.WriteNullable(er.File);
                w.WriteNullable(er.ProjectFile);
                w.Write7Bit(er.LineNumber);
                w.Write7Bit(er.ColumnNumber);
                w.Write7Bit(er.EndLineNumber);
                w.Write7Bit(er.EndColumnNumber);
                break;
            case PipeRecordKind.Warning:
                var wa = (BuildWarningEventArgs)e;
                w.WriteNullable(wa.Subcategory);
                w.WriteNullable(wa.Code);
                w.WriteNullable(wa.File);
                w.WriteNullable(wa.ProjectFile);
                w.Write7Bit(wa.LineNumber);
                w.Write7Bit(wa.ColumnNumber);
                w.Write7Bit(wa.EndLineNumber);
                w.Write7Bit(wa.EndColumnNumber);
                break;
            default:
                w.WriteNullable(e.GetType().Name);
                break;
        }
    }

    private static void WriteMessageFields(BinaryWriter w, BuildMessageEventArgs e)
    {
        w.Write7Bit((int)e.Importance);
        w.WriteNullable(e.Subcategory);
        w.WriteNullable(e.Code);
        w.WriteNullable(e.File);
        w.WriteNullable(e.ProjectFile);
        w.Write7Bit(e.LineNumber);
        w.Write7Bit(e.ColumnNumber);
        w.Write7Bit(e.EndLineNumber);
        w.Write7Bit(e.EndColumnNumber);
    }

    private static void WriteProperties(BinaryWriter w, IEnumerable? properties)
    {
        if (properties is null)
        {
            w.Write7Bit(0);
            return;
        }

        var entries = new List<(string Name, string? Value)>();
        foreach (var item in properties)
        {
            if (item is DictionaryEntry entry)
            {
                entries.Add((entry.Key?.ToString() ?? string.Empty, entry.Value?.ToString()));
            }
            else if (item is KeyValuePair<string, string> kvp)
            {
                entries.Add((kvp.Key, kvp.Value));
            }
        }

        w.Write7Bit(entries.Count);
        foreach (var (name, value) in entries)
        {
            w.Write(name);
            w.WriteNullable(value);
        }
    }

    private static void WriteItems(BinaryWriter w, IEnumerable? items)
    {
        if (items is null)
        {
            w.Write7Bit(0);
            return;
        }

        var entries = new List<DictionaryEntry>();
        foreach (var item in items)
        {
            if (item is DictionaryEntry entry)
            {
                entries.Add(entry);
            }
        }

        w.Write7Bit(entries.Count);
        foreach (var entry in entries)
        {
            var itemType = entry.Key?.ToString() ?? string.Empty;
            w.Write(itemType);
            if (entry.Value is ITaskItem taskItem)
            {
                w.Write(taskItem.ItemSpec ?? string.Empty);
                WriteMetadata(w, taskItem);
            }
            else
            {
                w.Write(entry.Value?.ToString() ?? string.Empty);
                w.Write7Bit(0);
            }
        }
    }

    // Task-parameter items share a single parameter name (on the event), so each item is just its spec
    // and metadata; the reader stamps the parameter name onto each resulting PipeItem.
    private static void WriteTaskParameterItems(BinaryWriter w, IList? items)
    {
        if (items is null)
        {
            w.Write7Bit(0);
            return;
        }

        var taskItems = new List<ITaskItem>();
        foreach (var item in items)
        {
            if (item is ITaskItem taskItem)
            {
                taskItems.Add(taskItem);
            }
        }

        w.Write7Bit(taskItems.Count);
        foreach (var taskItem in taskItems)
        {
            w.Write(taskItem.ItemSpec ?? string.Empty);
            WriteMetadata(w, taskItem);
        }
    }

    private static void WriteMetadata(BinaryWriter w, ITaskItem taskItem)
    {
        var metadata = taskItem.CloneCustomMetadata();
        w.Write7Bit(metadata.Count);
        foreach (DictionaryEntry entry in metadata)
        {
            w.Write(entry.Key?.ToString() ?? string.Empty);
            w.WriteNullable(entry.Value?.ToString());
        }
    }

    private static string? TryGetMessage(BuildEventArgs e)
    {
        try
        {
            return e.Message;
        }
        catch (Exception)
        {
            // Some event types format their message lazily and can throw; the message is best-effort.
            return null;
        }
    }
}
