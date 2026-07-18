// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>How a <see cref="PipeTaskParameterEventArgs"/> relates to a task. Mirrors MSBuild's
/// <c>TaskParameterMessageKind</c>.</summary>
public enum PipeTaskParameterKind
{
    /// <summary>An item or property passed as an input to a task.</summary>
    TaskInput = 0,

    /// <summary>An item or property produced as an output by a task.</summary>
    TaskOutput = 1,

    /// <summary>Items added to an item list.</summary>
    AddItem = 2,

    /// <summary>Items removed from an item list.</summary>
    RemoveItem = 3,

    /// <summary>The inputs of a target that was skipped because it was up to date.</summary>
    SkippedTargetInputs = 4,

    /// <summary>The outputs of a target that was skipped because it was up to date.</summary>
    SkippedTargetOutputs = 5,
}

/// <summary>
/// Raised for a task input or output parameter, carrying the parameter's items. This is how the resolved,
/// post-build compiler inputs (e.g. the <c>Csc</c> task's <c>Sources</c>, <c>References</c>, <c>Analyzers</c>)
/// are surfaced as structured items instead of a flattened command line. Requires task-parameter logging to
/// be enabled in the build (e.g. <c>MSBUILDLOGTASKINPUTS=1</c> or an attached binary logger).
/// </summary>
public sealed class PipeTaskParameterEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets how this parameter relates to the task.</summary>
    public PipeTaskParameterKind Kind { get; init; }

    /// <summary>Gets the parameter (item) name, e.g. <c>Sources</c> or <c>References</c>.</summary>
    public string? ItemType { get; init; }

    /// <summary>Gets the parameter's items. Each item's <see cref="PipeItem.EvaluatedInclude"/> is the value.</summary>
    public IReadOnlyList<PipeItem> Items { get; init; } = Array.Empty<PipeItem>();
}
