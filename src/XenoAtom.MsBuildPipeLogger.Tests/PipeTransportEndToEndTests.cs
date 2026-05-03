// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace XenoAtom.MsBuildPipeLogger.Tests;

[TestClass]
[DoNotParallelize]
public class PipeTransportEndToEndTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public TestContext? TestContext { get; set; }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(100000)]
    public async Task NamedPipe_TransportsEventsInProcess(int messageCount)
    {
        var pipeName = CreatePipeName();
        using var server = new NamedPipeLoggerServer(pipeName);
        var events = SubscribeAnyEvents(server);
        var readTask = Task.Run(server.ReadAll);

        using (var writer = ParameterParser.GetPipeFromParameters($"name={pipeName}"))
        {
            BuildEventAssertions.WriteEvents(writer, messageCount);
        }

        await WaitForReadAllAsync(readTask, server).ConfigureAwait(false);
        BuildEventAssertions.AssertEvents(events, messageCount);
    }

    [TestMethod]
    public async Task ReadAll_StopsAfterBuildFinishedEvent()
    {
        var pipeName = CreatePipeName();
        using var server = new NamedPipeLoggerServer(pipeName);
        var events = SubscribeAnyEvents(server);
        var readTask = Task.Run(server.ReadAll);

        using (var writer = ParameterParser.GetPipeFromParameters($"name={pipeName}"))
        {
            BuildEventAssertions.WriteEvents(writer, messageCount: 1, includeBuildFinished: true, includeMessageAfterBuildFinished: true);
        }

        await WaitForReadAllAsync(readTask, server).ConfigureAwait(false);
        BuildEventAssertions.AssertEvents(events, messageCount: 1, includeBuildFinished: true);
    }

    [TestMethod]
    public async Task Server_DispatchesTypedEvents()
    {
        var pipeName = CreatePipeName();
        var buildStartedCount = 0;
        var messageCount = 0;
        var buildFinishedCount = 0;
        using var server = new NamedPipeLoggerServer(pipeName);
        server.BuildStarted += (_, _) => buildStartedCount++;
        server.MessageRaised += (_, _) => messageCount++;
        server.BuildFinished += (_, _) => buildFinishedCount++;
        var readTask = Task.Run(server.ReadAll);

        using (var writer = ParameterParser.GetPipeFromParameters($"name={pipeName}"))
        {
            BuildEventAssertions.WriteEvents(writer, messageCount: 3, includeBuildFinished: true);
        }

        await WaitForReadAllAsync(readTask, server).ConfigureAwait(false);
        Assert.AreEqual(1, buildStartedCount);
        Assert.AreEqual(3, messageCount);
        Assert.AreEqual(1, buildFinishedCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(100000)]
    public async Task AnonymousPipe_TransportsEventsFromChildProcess(int messageCount)
    {
        using var server = new AnonymousPipeLoggerServer();
        var events = SubscribeAnyEvents(server);

        var exitCode = await RunClientProcessAsync(server, server.GetClientHandle(), messageCount, includeBuildFinished: true).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode);
        BuildEventAssertions.AssertEvents(events, messageCount, includeBuildFinished: true);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(100000)]
    public async Task NamedPipe_TransportsEventsFromChildProcess(int messageCount)
    {
        var pipeName = CreatePipeName();
        using var server = new NamedPipeLoggerServer(pipeName);
        var events = SubscribeAnyEvents(server);

        var exitCode = await RunClientProcessAsync(server, $"name={pipeName}", messageCount, includeBuildFinished: true).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode);
        BuildEventAssertions.AssertEvents(events, messageCount, includeBuildFinished: true);
    }

    private static string CreatePipeName() => $"xenoatom-msbuild-{Guid.NewGuid():N}";

    private static List<BuildEventArgs> SubscribeAnyEvents(EventArgsDispatcher server)
    {
        var events = new List<BuildEventArgs>();
        server.AnyEventRaised += (_, e) => events.Add(e);
        return events;
    }

    private static async Task WaitForReadAllAsync(Task readTask, IPipeLoggerServer server)
    {
        try
        {
            await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            server.Dispose();
            await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<int> RunClientProcessAsync(IPipeLoggerServer server, string loggerParameters, int messageCount, bool includeBuildFinished)
    {
        using var process = CreateClientProcess(loggerParameters, messageCount, includeBuildFinished);
        var readTask = Task.Run(server.ReadAll);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start the test client process.");
            }

            WriteLine($"Started process {process.Id}");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.WhenAll(readTask, process.WaitForExitAsync()).WaitAsync(TestTimeout).ConfigureAwait(false);
            WriteLine($"Exited process {process.Id} with code {process.ExitCode}");
            return process.ExitCode;
        }
        catch (TimeoutException)
        {
            server.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private Process CreateClientProcess(string loggerParameters, int messageCount, bool includeBuildFinished)
    {
        var testDirectory = Path.GetDirectoryName(typeof(PipeTransportEndToEndTests).Assembly.Location)
                            ?? throw new InvalidOperationException("Could not locate test assembly directory.");
        var clientDirectory = testDirectory.Replace(
            "XenoAtom.MsBuildPipeLogger.Tests",
            "XenoAtom.MsBuildPipeLogger.Tests.Client",
            StringComparison.Ordinal);
        var clientAssemblyPath = Path.Combine(clientDirectory, "XenoAtom.MsBuildPipeLogger.Tests.Client.dll");

        var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.WorkingDirectory = clientDirectory;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.ArgumentList.Add(clientAssemblyPath);
        process.StartInfo.ArgumentList.Add(loggerParameters);
        process.StartInfo.ArgumentList.Add(messageCount.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add(includeBuildFinished.ToString());
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                WriteLine(e.Data);
            }
        };
        return process;
    }

    private void WriteLine(string message)
    {
        TestContext?.WriteLine(message);
    }
}
