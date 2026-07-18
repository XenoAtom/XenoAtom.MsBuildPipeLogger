// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>Raised for a build message.</summary>
public class PipeBuildMessageEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the importance of the message.</summary>
    public PipeMessageImportance Importance { get; internal set; }

    /// <summary>Gets the message subcategory, if any.</summary>
    public string? Subcategory { get; internal set; }

    /// <summary>Gets the message code, if any.</summary>
    public string? Code { get; internal set; }

    /// <summary>Gets the file the message refers to, if any.</summary>
    public string? File { get; internal set; }

    /// <summary>Gets the full path of the project the message belongs to, if any.</summary>
    public string? ProjectFile { get; internal set; }

    /// <summary>Gets the line number the message refers to.</summary>
    public int LineNumber { get; internal set; }

    /// <summary>Gets the column number the message refers to.</summary>
    public int ColumnNumber { get; internal set; }

    /// <summary>Gets the end line number the message refers to.</summary>
    public int EndLineNumber { get; internal set; }

    /// <summary>Gets the end column number the message refers to.</summary>
    public int EndColumnNumber { get; internal set; }
}

/// <summary>Raised with the command line of a task, most notably the compiler invocation.</summary>
public sealed class PipeTaskCommandLineEventArgs : PipeBuildMessageEventArgs
{
    /// <summary>Gets the full command line of the task.</summary>
    public string? CommandLine { get; init; }

    /// <summary>Gets the name of the task that produced the command line.</summary>
    public string? TaskName { get; init; }
}

/// <summary>Raised for a build error.</summary>
public sealed class PipeBuildErrorEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the error subcategory, if any.</summary>
    public string? Subcategory { get; init; }

    /// <summary>Gets the error code, if any.</summary>
    public string? Code { get; init; }

    /// <summary>Gets the file the error refers to, if any.</summary>
    public string? File { get; init; }

    /// <summary>Gets the full path of the project the error belongs to, if any.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the line number the error refers to.</summary>
    public int LineNumber { get; init; }

    /// <summary>Gets the column number the error refers to.</summary>
    public int ColumnNumber { get; init; }

    /// <summary>Gets the end line number the error refers to.</summary>
    public int EndLineNumber { get; init; }

    /// <summary>Gets the end column number the error refers to.</summary>
    public int EndColumnNumber { get; init; }
}

/// <summary>Raised for a build warning.</summary>
public sealed class PipeBuildWarningEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the warning subcategory, if any.</summary>
    public string? Subcategory { get; init; }

    /// <summary>Gets the warning code, if any.</summary>
    public string? Code { get; init; }

    /// <summary>Gets the file the warning refers to, if any.</summary>
    public string? File { get; init; }

    /// <summary>Gets the full path of the project the warning belongs to, if any.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Gets the line number the warning refers to.</summary>
    public int LineNumber { get; init; }

    /// <summary>Gets the column number the warning refers to.</summary>
    public int ColumnNumber { get; init; }

    /// <summary>Gets the end line number the warning refers to.</summary>
    public int EndLineNumber { get; init; }

    /// <summary>Gets the end column number the warning refers to.</summary>
    public int EndColumnNumber { get; init; }
}

/// <summary>
/// Raised for any MSBuild event that does not have a dedicated <see cref="PipeBuildEventArgs"/> subtype.
/// The common base fields (message, timestamp, context) are preserved and <see cref="EventType"/> carries
/// the originating .NET type name for diagnostics.
/// </summary>
public sealed class PipeCustomBuildEventArgs : PipeBuildEventArgs
{
    /// <summary>Gets the simple name of the originating <c>Microsoft.Build.Framework</c> event type.</summary>
    public string? EventType { get; init; }
}
