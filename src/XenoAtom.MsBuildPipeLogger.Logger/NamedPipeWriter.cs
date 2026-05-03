// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// Writes MSBuild events to a named pipe.
/// </summary>
public class NamedPipeWriter : PipeWriter
{
    private const int ConnectTimeoutMilliseconds = 30000;

    /// <summary>
    /// Gets the named pipe server name.
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// Gets the named pipe name.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeWriter"/> class for a local named pipe.
    /// </summary>
    /// <param name="pipeName">The named pipe name.</param>
    /// <exception cref="ArgumentException"><paramref name="pipeName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="pipeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">The named pipe server did not accept the connection before the connection timeout elapsed.</exception>
    public NamedPipeWriter(string pipeName)
        : this(".", pipeName)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeWriter"/> class.
    /// </summary>
    /// <param name="serverName">The named pipe server name.</param>
    /// <param name="pipeName">The named pipe name.</param>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> or <paramref name="pipeName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> or <paramref name="pipeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">The named pipe server did not accept the connection before the connection timeout elapsed.</exception>
    public NamedPipeWriter(string serverName, string pipeName)
        : base(InitializePipe(serverName, pipeName))
    {
        ServerName = serverName;
        PipeName = pipeName;
    }

    private static PipeStream InitializePipe(string serverName, string pipeName)
    {
        ValidatePipeEndpoint(serverName, pipeName);
        var pipeStream = new NamedPipeClientStream(serverName, pipeName, PipeDirection.Out);
        try
        {
            pipeStream.Connect(ConnectTimeoutMilliseconds);
            return pipeStream;
        }
        catch
        {
            pipeStream.Dispose();
            throw;
        }
    }

    private static void ValidatePipeEndpoint(string serverName, string pipeName)
    {
        if (serverName is null)
        {
            throw new ArgumentNullException(nameof(serverName));
        }

        if (pipeName is null)
        {
            throw new ArgumentNullException(nameof(pipeName));
        }

        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new ArgumentException("The pipe server name cannot be empty or whitespace.", nameof(serverName));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("The pipe name cannot be empty or whitespace.", nameof(pipeName));
        }
    }
}
