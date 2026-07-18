// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>Raised when a build starts.</summary>
public sealed class PipeBuildStartedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the environment variables captured at the start of the build, if any.</summary>
    public IReadOnlyList<PipeProperty> BuildEnvironment { get; init; } = Array.Empty<PipeProperty>();
}

/// <summary>Raised when a build finishes.</summary>
public sealed class PipeBuildFinishedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets a value indicating whether the build succeeded.</summary>
    public bool Succeeded { get; init; }
}

/// <summary>Raised when a project build starts.</summary>
public sealed class PipeProjectStartedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the project identifier.</summary>
    public int ProjectId { get; init; }

    /// <summary>Gets the full path of the project file.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the semicolon-delimited list of targets that were requested.</summary>
    public string? TargetNames { get; init; }

    /// <summary>Gets the tools version the project is being built with.</summary>
    public string? ToolsVersion { get; init; }

    /// <summary>Gets the global properties the project is being built with.</summary>
    public IReadOnlyList<PipeProperty> GlobalProperties { get; init; } = Array.Empty<PipeProperty>();

    /// <summary>Gets the evaluated properties, when the build attaches them to the project-started event.</summary>
    public IReadOnlyList<PipeProperty> Properties { get; init; } = Array.Empty<PipeProperty>();

    /// <summary>Gets the evaluated items, when the build attaches them to the project-started event.</summary>
    public IReadOnlyList<PipeItem> Items { get; init; } = Array.Empty<PipeItem>();

    /// <summary>Gets the build context of the project that caused this project to build, if any. Used to
    /// reconstruct the project dependency tree.</summary>
    public PipeBuildEventContext? ParentProjectBuildEventContext { get; init; }
}

/// <summary>Raised when a project build finishes.</summary>
public sealed class PipeProjectFinishedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the full path of the project file.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets a value indicating whether the project build succeeded.</summary>
    public bool Succeeded { get; init; }
}

/// <summary>Raised when project evaluation starts.</summary>
public sealed class PipeProjectEvaluationStartedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the full path of the project file being evaluated.</summary>
    public string? ProjectFile { get; init; }
}

/// <summary>Raised when project evaluation finishes, carrying the evaluated properties and items.</summary>
public sealed class PipeProjectEvaluationFinishedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the full path of the project file that was evaluated.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the evaluated properties.</summary>
    public IReadOnlyList<PipeProperty> Properties { get; init; } = Array.Empty<PipeProperty>();

    /// <summary>Gets the evaluated items.</summary>
    public IReadOnlyList<PipeItem> Items { get; init; } = Array.Empty<PipeItem>();
}

/// <summary>Raised when a target starts executing.</summary>
public sealed class PipeTargetStartedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the name of the target.</summary>
    public string? TargetName { get; init; }

    /// <summary>Gets the full path of the project file the target belongs to.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the full path of the file declaring the target.</summary>
    public string? TargetFile { get; init; }

    /// <summary>Gets the name of the parent target that invoked this target, if any.</summary>
    public string? ParentTarget { get; init; }

    /// <summary>Gets the reason the target was built (e.g. as a dependency, or before/after another target).</summary>
    public PipeTargetBuiltReason BuildReason { get; init; }
}

/// <summary>Raised when a target finishes executing.</summary>
public sealed class PipeTargetFinishedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the name of the target.</summary>
    public string? TargetName { get; init; }

    /// <summary>Gets the full path of the project file the target belongs to.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the full path of the file declaring the target.</summary>
    public string? TargetFile { get; init; }

    /// <summary>Gets a value indicating whether the target succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the output items the target produced. Only populated when the build enables target-output
    /// logging (otherwise empty); each item's <see cref="PipeItem.EvaluatedInclude"/> is the output value and
    /// its <see cref="PipeItem.ItemType"/> is empty.</summary>
    public IReadOnlyList<PipeItem> TargetOutputs { get; init; } = Array.Empty<PipeItem>();
}

/// <summary>Why a target was built. Mirrors <c>Microsoft.Build.Framework.TargetBuiltReason</c>.</summary>
public enum PipeTargetBuiltReason
{
    /// <summary>The target was built for no reason other than being asked for directly.</summary>
    None = 0,

    /// <summary>The target was run because it appears in a <c>BeforeTargets</c> attribute of another target.</summary>
    BeforeTargets = 1,

    /// <summary>The target was run because it appears in a <c>DependsOnTargets</c> list of another target.</summary>
    DependsOn = 2,

    /// <summary>The target was run because it appears in an <c>AfterTargets</c> attribute of another target.</summary>
    AfterTargets = 3,

    /// <summary>The target was one of the entry targets requested for the build.</summary>
    EntryTarget = 4,
}

/// <summary>Raised when a task starts executing.</summary>
public sealed class PipeTaskStartedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the name of the task.</summary>
    public string? TaskName { get; init; }

    /// <summary>Gets the full path of the project file the task belongs to.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the full path of the file declaring the task.</summary>
    public string? TaskFile { get; init; }
}

/// <summary>Raised when a task finishes executing.</summary>
public sealed class PipeTaskFinishedEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the name of the task.</summary>
    public string? TaskName { get; init; }

    /// <summary>Gets the full path of the project file the task belongs to.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the full path of the file declaring the task.</summary>
    public string? TaskFile { get; init; }

    /// <summary>Gets a value indicating whether the task succeeded.</summary>
    public bool Succeeded { get; init; }
}
