using System;
using Microsoft.Build.Framework;

namespace MsBuildPipeLogger
{
    /// <summary>
    /// Receives serialized MSBuild events from a logger transport.
    /// </summary>
    public interface IPipeLoggerServer : IDisposable
    {
        /// <summary>
        /// Reads a single event from the pipe. This method blocks until an event is received,
        /// there are no more events, or the pipe is closed.
        /// </summary>
        /// <returns>The read event or <see langword="null"/> if there are no more events or the pipe is closed.</returns>
        BuildEventArgs? Read();

        /// <summary>
        /// Reads all events from the pipe and blocks until there are no more events or the pipe is closed.
        /// </summary>
        void ReadAll();
    }
}