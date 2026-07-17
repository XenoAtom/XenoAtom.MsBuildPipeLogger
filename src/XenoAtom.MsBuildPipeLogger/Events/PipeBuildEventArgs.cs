// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Base class for the MSBuild logging events surfaced by <see cref="IPipeLoggerServer"/>. These are
/// XenoAtom-owned types (not <c>Microsoft.Build.Framework</c> types), so the receiving process needs no
/// MSBuild assemblies and is insulated from the MSBuild version used to produce the build.
/// </summary>
public abstract class PipeBuildEventArgs
{
    /// <summary>Gets the human-readable message for the event, if any.</summary>
    public string? Message { get; internal set; }

    /// <summary>Gets the help keyword associated with the event, if any.</summary>
    public string? HelpKeyword { get; internal set; }

    /// <summary>Gets the name of the component that raised the event (e.g. a task name), if any.</summary>
    public string? SenderName { get; internal set; }

    /// <summary>Gets the time the event was raised.</summary>
    public DateTime Timestamp { get; internal set; }

    /// <summary>Gets the thread that raised the event.</summary>
    public int ThreadId { get; internal set; }

    /// <summary>Gets the MSBuild context that raised the event, if one was attached.</summary>
    public PipeBuildEventContext? BuildEventContext { get; internal set; }
}

/// <summary>
/// Identifies where in a build an event originated. Mirrors the identifiers on
/// <c>Microsoft.Build.Framework.BuildEventContext</c>.
/// </summary>
public readonly struct PipeBuildEventContext
{
    /// <summary>Initializes a new instance of the <see cref="PipeBuildEventContext"/> struct.</summary>
    public PipeBuildEventContext(int submissionId, int nodeId, int evaluationId, int projectInstanceId, int projectContextId, int targetId, int taskId)
    {
        SubmissionId = submissionId;
        NodeId = nodeId;
        EvaluationId = evaluationId;
        ProjectInstanceId = projectInstanceId;
        ProjectContextId = projectContextId;
        TargetId = targetId;
        TaskId = taskId;
    }

    /// <summary>Gets the build submission identifier.</summary>
    public int SubmissionId { get; }

    /// <summary>Gets the node identifier.</summary>
    public int NodeId { get; }

    /// <summary>Gets the evaluation identifier, used to correlate evaluation results with a project build.</summary>
    public int EvaluationId { get; }

    /// <summary>Gets the project instance identifier.</summary>
    public int ProjectInstanceId { get; }

    /// <summary>Gets the project context identifier.</summary>
    public int ProjectContextId { get; }

    /// <summary>Gets the target identifier.</summary>
    public int TargetId { get; }

    /// <summary>Gets the task identifier.</summary>
    public int TaskId { get; }
}

/// <summary>Importance of a build message. Mirrors <c>Microsoft.Build.Framework.MessageImportance</c>.</summary>
public enum PipeMessageImportance
{
    /// <summary>High importance, displayed at all but the lowest verbosity settings.</summary>
    High = 0,

    /// <summary>Normal importance.</summary>
    Normal = 1,

    /// <summary>Low importance, displayed only at detailed verbosity.</summary>
    Low = 2,
}

/// <summary>An evaluated MSBuild property (name/value pair).</summary>
public readonly struct PipeProperty
{
    /// <summary>Initializes a new instance of the <see cref="PipeProperty"/> struct.</summary>
    public PipeProperty(string name, string? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the evaluated property value.</summary>
    public string? Value { get; }
}

/// <summary>An evaluated MSBuild item, including its metadata.</summary>
public sealed class PipeItem
{
    /// <summary>Initializes a new instance of the <see cref="PipeItem"/> class.</summary>
    public PipeItem(string itemType, string evaluatedInclude, IReadOnlyList<PipeProperty> metadata)
    {
        ItemType = itemType;
        EvaluatedInclude = evaluatedInclude;
        Metadata = metadata;
    }

    /// <summary>Gets the item type (the item group name).</summary>
    public string ItemType { get; }

    /// <summary>Gets the evaluated <c>Include</c> value (the item spec).</summary>
    public string EvaluatedInclude { get; }

    /// <summary>Gets the item metadata as name/value pairs.</summary>
    public IReadOnlyList<PipeProperty> Metadata { get; }
}
