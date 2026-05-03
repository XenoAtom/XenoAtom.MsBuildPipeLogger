// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Writes MSBuild events to an anonymous pipe.
/// </summary>
public class AnonymousPipeWriter : PipeWriter
{
    /// <summary>
    /// Gets the anonymous pipe handle used by the writer.
    /// </summary>
    public string Handle { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnonymousPipeWriter"/> class.
    /// </summary>
    /// <param name="pipeHandleAsString">The anonymous pipe client handle as a string.</param>
    /// <exception cref="ArgumentException"><paramref name="pipeHandleAsString"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeHandleAsString"/> is <see langword="null"/>.</exception>
    public AnonymousPipeWriter(string pipeHandleAsString)
        : base(new AnonymousPipeClientStream(PipeDirection.Out, ValidatePipeHandle(pipeHandleAsString)))
    {
        Handle = pipeHandleAsString;
    }

    private static string ValidatePipeHandle(string pipeHandleAsString)
    {
        if (pipeHandleAsString is null)
        {
            throw new ArgumentNullException(nameof(pipeHandleAsString));
        }

        if (string.IsNullOrWhiteSpace(pipeHandleAsString))
        {
            throw new ArgumentException("The pipe handle cannot be empty or whitespace.", nameof(pipeHandleAsString));
        }

        return pipeHandleAsString;
    }
}