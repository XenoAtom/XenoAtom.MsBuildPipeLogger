// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// The fields common to every MSBuild event, serialized at the start of each record payload. Shared by
/// the logger (which fills it from <c>Microsoft.Build.Framework.BuildEventArgs</c>) and the receiver
/// (which maps it onto <c>PipeBuildEventArgs</c>), so the field order is defined in exactly one
/// place. Fields are append-only: never remove or reorder, only add new ones at the end.
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
