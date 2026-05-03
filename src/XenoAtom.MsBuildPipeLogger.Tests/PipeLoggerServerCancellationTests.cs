// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Microsoft.Build.Framework;

namespace XenoAtom.MsBuildPipeLogger.Tests;

[TestClass]
[DoNotParallelize]
public class PipeLoggerServerCancellationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task NamedPipe_ReadReturnsNullWhenCanceledBeforeClientConnects()
    {
        var buildEvent = await ReadWithCancellationAsync(
            token => new NamedPipeLoggerServer(CreatePipeName(), token)).ConfigureAwait(false);

        Assert.IsNull(buildEvent);
    }

    [TestMethod]
    public async Task AnonymousPipe_ReadReturnsNullWhenCanceledBeforeClientConnects()
    {
        var buildEvent = await ReadWithCancellationAsync(
            token => new AnonymousPipeLoggerServer(token)).ConfigureAwait(false);

        Assert.IsNull(buildEvent);
    }

    [TestMethod]
    public async Task AnonymousPipe_ReadReturnsNullWhenCanceledAfterClientHandleIsCreated()
    {
        using var tokenSource = new CancellationTokenSource();
        using var server = new AnonymousPipeLoggerServer(tokenSource.Token);
        _ = server.GetClientHandle();
        var readTask = Task.Run(server.Read);

        tokenSource.CancelAfter(TimeSpan.FromMilliseconds(100));

        var buildEvent = await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        Assert.IsNull(buildEvent);
    }

    [TestMethod]
    public async Task NamedPipe_DisposeUnblocksPendingRead()
    {
        using var server = new NamedPipeLoggerServer(CreatePipeName());
        var readTask = Task.Run(server.Read);

        await Task.Delay(100).ConfigureAwait(false);
        server.Dispose();

        var buildEvent = await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        Assert.IsNull(buildEvent);
    }

    [TestMethod]
    public async Task AnonymousPipe_DisposeUnblocksPendingRead()
    {
        using var server = new AnonymousPipeLoggerServer();
        var readTask = Task.Run(server.Read);

        await Task.Delay(100).ConfigureAwait(false);
        server.Dispose();

        var buildEvent = await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        Assert.IsNull(buildEvent);
    }

    [TestMethod]
    public async Task AnonymousPipe_DisposeUnblocksPendingReadAfterClientHandleIsCreated()
    {
        using var server = new AnonymousPipeLoggerServer();
        _ = server.GetClientHandle();
        var readTask = Task.Run(server.Read);

        await Task.Delay(100).ConfigureAwait(false);
        server.Dispose();

        var buildEvent = await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        Assert.IsNull(buildEvent);
    }

    private static async Task<BuildEventArgs?> ReadWithCancellationAsync(Func<CancellationToken, IPipeLoggerServer> createServer)
    {
        using var tokenSource = new CancellationTokenSource();
        using var server = createServer(tokenSource.Token);
        tokenSource.CancelAfter(TimeSpan.FromMilliseconds(100));
        return await Task.Run(server.Read).WaitAsync(TestTimeout).ConfigureAwait(false);
    }

    private static string CreatePipeName() => $"xenoatom-msbuild-{Guid.NewGuid():N}";
}
