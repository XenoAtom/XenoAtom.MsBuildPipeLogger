// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;

namespace XenoAtom.MsBuildPipeLogger;

/// <summary>
/// A server for receiving MSBuild logging events over an anonymous pipe.
/// </summary>
public class AnonymousPipeLoggerServer : PipeLoggerServer<AnonymousPipeServerStream>
{
    private readonly object _clientHandleLock = new();
    private readonly CancellationTokenRegistration _clientHandleCancellationRegistration;

    private string? _clientHandle;

    /// <summary>
    /// Creates an anonymous pipe server for receiving MSBuild logging events.
    /// </summary>
    public AnonymousPipeLoggerServer()
        : this(CancellationToken.None)
    {
    }

    /// <summary>
    /// Creates an anonymous pipe server for receiving MSBuild logging events.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that will cancel read operations if triggered.</param>
    public AnonymousPipeLoggerServer(CancellationToken cancellationToken)
        : base(new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable), cancellationToken, false)
    {
        if (cancellationToken.CanBeCanceled)
        {
            _clientHandleCancellationRegistration = cancellationToken.Register(DisposeLocalClientHandle);
        }

        StartReading();
    }

    /// <summary>
    /// Gets the client handle as a string. The local copy of the handle will be automatically disposed on the first read, cancellation, or disposal.
    /// </summary>
    /// <returns>The client handle as a string.</returns>
    public string GetClientHandle()
    {
        lock (_clientHandleLock)
        {
            return _clientHandle ?? (_clientHandle = PipeStream.GetClientHandleAsString());
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _clientHandleCancellationRegistration.Dispose();
        DisposeLocalClientHandle();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override void Connect()
    {
        // Wait for the first write, there's a chicken-and-egg problem with the pipe handle.
        // I can only dispose the local handle after the first pipe read, which blocks.
        // Cancellation and disposal also close the local client handle to unblock this read when no client connects.
        try
        {
            Buffer.FillFromStream(PipeStream, CancellationToken);
        }
        finally
        {
            DisposeLocalClientHandle();
        }
    }

    private void DisposeLocalClientHandle()
    {
        lock (_clientHandleLock)
        {
            // Dispose the client handle if we asked for one.
            // If we don't do this we won't get notified when the stream closes, see https://stackoverflow.com/q/39682602/807064.
            if (_clientHandle is null)
            {
                return;
            }

            try
            {
                PipeStream.DisposeLocalCopyOfClientHandle();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            _clientHandle = null;
        }
    }
}
