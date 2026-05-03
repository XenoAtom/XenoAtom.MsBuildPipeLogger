// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Logger to send messages from the MSBuild logging system over an anonymous or named pipe.
/// </summary>
/// <remarks>
/// Heavily based on the work of Kirill Osenkov and the MSBuildStructuredLog project.
/// </remarks>
public class PipeLogger : Logger
{
    private IEventSource? _eventSource;

    /// <summary>
    /// Gets the active pipe writer after the logger has been initialized.
    /// </summary>
    protected IPipeWriter? Pipe { get; private set; }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="eventSource"/> is <see langword="null"/>.</exception>
    public override void Initialize(IEventSource eventSource)
    {
        if (eventSource is null)
        {
            throw new ArgumentNullException(nameof(eventSource));
        }

        InitializeEnvironmentVariables();
        Pipe = InitializePipeWriter();
        InitializeEvents(eventSource);
    }

    /// <summary>
    /// Initializes environment variables that enable additional MSBuild logging data.
    /// </summary>
    protected virtual void InitializeEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "true");
        Environment.SetEnvironmentVariable("MSBUILDLOGIMPORTS", "1");
    }

    /// <summary>
    /// Creates the pipe writer specified by the logger parameters.
    /// </summary>
    /// <returns>The initialized pipe writer.</returns>
    protected virtual IPipeWriter InitializePipeWriter() => ParameterParser.GetPipeFromParameters(Parameters ?? string.Empty);

    /// <summary>
    /// Subscribes to MSBuild events and forwards them to the active pipe writer.
    /// </summary>
    /// <param name="eventSource">The MSBuild event source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventSource"/> is <see langword="null"/>.</exception>
    protected virtual void InitializeEvents(IEventSource eventSource)
    {
        if (eventSource is null)
        {
            throw new ArgumentNullException(nameof(eventSource));
        }

        _eventSource = eventSource;
        eventSource.AnyEventRaised += OnAnyEventRaised;
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        base.Shutdown();
        if (_eventSource is not null)
        {
            _eventSource.AnyEventRaised -= OnAnyEventRaised;
            _eventSource = null;
        }

        Pipe?.Dispose();
        Pipe = null;
    }

    private void OnAnyEventRaised(object sender, BuildEventArgs e)
    {
        Pipe?.Write(e);
    }
}
