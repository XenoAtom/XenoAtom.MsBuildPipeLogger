// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Identifies the kind of a record on the XenoAtom pipe wire format. The value is written as the first
/// field of every record, followed by a length prefix, so a reader can dispatch known kinds and skip
/// unknown ones. Values are append-only and must never be reused or renumbered.
/// </summary>
internal enum PipeRecordKind
{
    /// <summary>Marks the end of the stream. Carries no payload.</summary>
    EndOfFile = 0,

    BuildStarted = 1,
    BuildFinished = 2,
    ProjectStarted = 3,
    ProjectFinished = 4,
    ProjectEvaluationStarted = 5,
    ProjectEvaluationFinished = 6,
    TargetStarted = 7,
    TargetFinished = 8,
    TaskStarted = 9,
    TaskFinished = 10,
    Message = 11,
    TaskCommandLine = 12,
    Error = 13,
    Warning = 14,

    /// <summary>Any event we do not have a dedicated kind for. Carries only the common base fields plus
    /// the originating .NET type name so consumers can still observe the message and severity.</summary>
    Custom = 15,

    /// <summary>A task input/output parameter (e.g. the Csc task's resolved <c>Sources</c> or <c>References</c>).</summary>
    TaskParameter = 16,
}
