// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Writes serialized MSBuild events to a logger transport.
/// </summary>
public interface IPipeWriter : IDisposable
{
    /// <summary>
    /// Writes a single MSBuild event to the transport.
    /// </summary>
    /// <param name="e">The MSBuild event to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
    void Write(BuildEventArgs e);
}