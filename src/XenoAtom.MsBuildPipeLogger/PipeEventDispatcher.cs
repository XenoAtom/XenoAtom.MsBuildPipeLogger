// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Raises strongly-typed events for the <see cref="PipeBuildEventArgs"/> read from a pipe. This is the
/// XenoAtom-owned equivalent of MSBuild's <c>EventArgsDispatcher</c>; it depends on no MSBuild assemblies.
/// </summary>
public abstract class PipeEventDispatcher
{
    /// <summary>Raised for every event, before the corresponding strongly-typed event.</summary>
    public event Action<PipeBuildEventArgs>? AnyEventRaised;

    /// <summary>Raised when a build starts.</summary>
    public event Action<PipeBuildStartedEventArgs>? BuildStarted;

    /// <summary>Raised when a build finishes.</summary>
    public event Action<PipeBuildFinishedEventArgs>? BuildFinished;

    /// <summary>Raised when a project build starts.</summary>
    public event Action<PipeProjectStartedEventArgs>? ProjectStarted;

    /// <summary>Raised when a project build finishes.</summary>
    public event Action<PipeProjectFinishedEventArgs>? ProjectFinished;

    /// <summary>Raised when project evaluation starts.</summary>
    public event Action<PipeProjectEvaluationStartedEventArgs>? ProjectEvaluationStarted;

    /// <summary>Raised when project evaluation finishes.</summary>
    public event Action<PipeProjectEvaluationFinishedEventArgs>? ProjectEvaluationFinished;

    /// <summary>Raised when a target starts.</summary>
    public event Action<PipeTargetStartedEventArgs>? TargetStarted;

    /// <summary>Raised when a target finishes.</summary>
    public event Action<PipeTargetFinishedEventArgs>? TargetFinished;

    /// <summary>Raised when a task starts.</summary>
    public event Action<PipeTaskStartedEventArgs>? TaskStarted;

    /// <summary>Raised when a task finishes.</summary>
    public event Action<PipeTaskFinishedEventArgs>? TaskFinished;

    /// <summary>Raised for a build message (including <see cref="PipeTaskCommandLineEventArgs"/>).</summary>
    public event Action<PipeBuildMessageEventArgs>? MessageRaised;

    /// <summary>Raised for a build error.</summary>
    public event Action<PipeBuildErrorEventArgs>? ErrorRaised;

    /// <summary>Raised for a build warning.</summary>
    public event Action<PipeBuildWarningEventArgs>? WarningRaised;

    /// <summary>Raised for a task input/output parameter (e.g. the Csc task's resolved Sources).</summary>
    public event Action<PipeTaskParameterEventArgs>? TaskParameterRaised;

    /// <summary>Raised for any event without a dedicated type.</summary>
    public event Action<PipeCustomBuildEventArgs>? CustomEventRaised;

    /// <summary>Raises <see cref="AnyEventRaised"/> and the strongly-typed event for <paramref name="e"/>.</summary>
    protected void Dispatch(PipeBuildEventArgs e)
    {
        AnyEventRaised?.Invoke(e);
        switch (e)
        {
            case PipeBuildStartedEventArgs s: BuildStarted?.Invoke(s); break;
            case PipeBuildFinishedEventArgs s: BuildFinished?.Invoke(s); break;
            case PipeProjectStartedEventArgs s: ProjectStarted?.Invoke(s); break;
            case PipeProjectFinishedEventArgs s: ProjectFinished?.Invoke(s); break;
            case PipeProjectEvaluationStartedEventArgs s: ProjectEvaluationStarted?.Invoke(s); break;
            case PipeProjectEvaluationFinishedEventArgs s: ProjectEvaluationFinished?.Invoke(s); break;
            case PipeTargetStartedEventArgs s: TargetStarted?.Invoke(s); break;
            case PipeTargetFinishedEventArgs s: TargetFinished?.Invoke(s); break;
            case PipeTaskStartedEventArgs s: TaskStarted?.Invoke(s); break;
            case PipeTaskFinishedEventArgs s: TaskFinished?.Invoke(s); break;
            case PipeTaskParameterEventArgs s: TaskParameterRaised?.Invoke(s); break;
            case PipeBuildMessageEventArgs s: MessageRaised?.Invoke(s); break;
            case PipeBuildErrorEventArgs s: ErrorRaised?.Invoke(s); break;
            case PipeBuildWarningEventArgs s: WarningRaised?.Invoke(s); break;
            case PipeCustomBuildEventArgs s: CustomEventRaised?.Invoke(s); break;
        }
    }
}
