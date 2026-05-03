using System;
using Microsoft.Build.Framework;

namespace MsBuildPipeLogger
{
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
}