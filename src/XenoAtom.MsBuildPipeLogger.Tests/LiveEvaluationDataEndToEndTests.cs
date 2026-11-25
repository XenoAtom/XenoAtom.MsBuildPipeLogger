// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace XenoAtom.MsBuildPipeLogger.Tests;

/// <summary>
/// The evaluated properties and items of a real build. Once any logger opts in to receiving them after
/// evaluation (the binary logger, the dotnet CLI telemetry logger or MSBUILDLOGPROPERTIESANDITEMSAFTEREVALUATION),
/// MSBuild moves them from ProjectStarted to ProjectEvaluationFinished for every logger, and in a live build
/// they are then MSBuild's own objects rather than the DictionaryEntry pairs a replayed binary log carries.
/// The serializer dropped every one of them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LiveEvaluationDataEndToEndTests
{
    private const string TargetFramework = "net10.0";
    private const string LogAfterEvaluationVariable = "MSBUILDLOGPROPERTIESANDITEMSAFTEREVALUATION";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(180);

    public TestContext? TestContext { get; set; }

    [TestMethod]
    [DataRow(OptIn.EnvironmentVariable)]
    [DataRow(OptIn.BinaryLogger)]
    public async Task OptedInBuild_StreamsEvaluationPropertiesAndItemsOnEvaluationFinished(OptIn optIn)
    {
        var (events, projectPath) = await BuildAsync(optIn).ConfigureAwait(false);

        var evaluation = events.OfType<PipeProjectEvaluationFinishedEventArgs>().SingleOrDefault(e => e.ProjectFile == projectPath);
        Assert.IsNotNull(evaluation, "MSBuild should raise ProjectEvaluationFinished for the project.");
        AssertEvaluationData(evaluation.Properties, evaluation.Items, "ProjectEvaluationFinished");
    }

    [TestMethod]
    public async Task BuildWithoutOptIn_StreamsEvaluationPropertiesAndItemsOnProjectStarted()
    {
        var (events, projectPath) = await BuildAsync(OptIn.None).ConfigureAwait(false);

        var started = events.OfType<PipeProjectStartedEventArgs>().FirstOrDefault(e => e.ProjectFile == projectPath);
        Assert.IsNotNull(started, "MSBuild should raise ProjectStarted for the project.");
        AssertEvaluationData(started.Properties, started.Items, "ProjectStarted");
    }

    public enum OptIn
    {
        None,
        EnvironmentVariable,
        BinaryLogger,
    }

    private static void AssertEvaluationData(IReadOnlyList<PipeProperty> properties, IReadOnlyList<PipeItem> items, string eventName)
    {
        Assert.IsTrue(
            properties.Any(p => p.Name == "TargetFramework" && p.Value == TargetFramework),
            $"{eventName} should carry the TargetFramework property; it carried {properties.Count} properties.");

        var compile = items.Where(i => i.ItemType == "Compile").ToList();
        Assert.AreEqual(1, compile.Count, $"{eventName} should carry the one Compile item; it carried {items.Count} items.");
        Assert.AreEqual("C.cs", compile[0].EvaluatedInclude);
        Assert.IsTrue(
            compile[0].Metadata.Any(m => m.Name == "Custom" && m.Value == "Value"),
            $"The Compile item should carry its metadata; it carried {compile[0].Metadata.Count}.");
    }

    private async Task<(List<PipeBuildEventArgs> Events, string ProjectPath)> BuildAsync(OptIn optIn)
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), $"xa-eval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectDirectory);
        var projectPath = Path.Combine(projectDirectory, "TinyLib.csproj");

        // An SDK project, so evaluation yields real ProjectPropertyInstances and an ItemDictionary. The target
        // does nothing, so the test needs neither a restore nor a compile.
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            $"    <TargetFramework>{TargetFramework}</TargetFramework>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "  <ItemGroup>" + Environment.NewLine +
            "    <Compile Update=\"C.cs\" Custom=\"Value\" />" + Environment.NewLine +
            "  </ItemGroup>" + Environment.NewLine +
            "  <Target Name=\"Noop\" />" + Environment.NewLine +
            "</Project>" + Environment.NewLine);
        File.WriteAllText(Path.Combine(projectDirectory, "C.cs"), "public class C { }");

        var pipeName = PipeLoggerServer.CreateUniquePipeName("xa-test");
        using var server = new NamedPipeLoggerServer(pipeName);
        var events = new ConcurrentQueue<PipeBuildEventArgs>();
        server.AnyEventRaised += events.Enqueue;
        var readTask = Task.Run(server.ReadAll);

        using var process = CreateDotNetMsBuildProcess(projectPath, pipeName, optIn);
        var output = new ConcurrentQueue<string>();

        try
        {
            process.OutputDataReceived += (_, e) => EnqueueOutput(output, e.Data);
            process.ErrorDataReceived += (_, e) => EnqueueOutput(output, e.Data);

            Assert.IsTrue(process.Start(), "The dotnet msbuild process should start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Waiting on the process handle alone, for the reason given in DotNetBuildMultiSubmissionTests.
            if (!await Task.Run(() => process.WaitForExit((int)TestTimeout.TotalMilliseconds)).ConfigureAwait(false))
            {
                throw new TimeoutException($"dotnet msbuild did not exit within {TestTimeout.TotalSeconds:F0}s.");
            }

            server.StopListening();
            await readTask.WaitAsync(TestTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            server.Dispose();
            KillProcess(process);
            throw;
        }
        finally
        {
            TryDeleteDirectory(projectDirectory);
        }

        var processOutput = string.Join(Environment.NewLine, output);
        TestContext?.WriteLine(processOutput);
        Assert.AreEqual(0, process.ExitCode, processOutput);
        return (events.ToList(), projectPath);
    }

    private static Process CreateDotNetMsBuildProcess(string projectPath, string pipeName, OptIn optIn)
    {
        var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        DotNetProcess.RemoveInheritedSdkVariables(process.StartInfo);

        // The CLI telemetry logger opts in on its own, and so does the console logger of MSBuild 17.14 and later
        // (.NET SDK 9.0.300). Either would make the control build opt in as well.
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment.Remove(LogAfterEvaluationVariable);
        if (optIn == OptIn.EnvironmentVariable)
        {
            process.StartInfo.Environment[LogAfterEvaluationVariable] = "1";
        }

        process.StartInfo.ArgumentList.Add("msbuild");
        process.StartInfo.ArgumentList.Add(projectPath);
        process.StartInfo.ArgumentList.Add("-t:Noop");
        process.StartInfo.ArgumentList.Add("-nologo");
        process.StartInfo.ArgumentList.Add("-nr:false");
        if (optIn == OptIn.None)
        {
            process.StartInfo.ArgumentList.Add("-noconsolelogger");
        }
        else if (optIn == OptIn.BinaryLogger)
        {
            process.StartInfo.ArgumentList.Add($"-bl:{Path.Combine(Path.GetDirectoryName(projectPath)!, "build.binlog")}");
        }

        process.StartInfo.ArgumentList.Add($"-logger:{PipeLoggerServer.GetLoggerSpecification($"name={pipeName}")}");
        return process;
    }

    private static void EnqueueOutput(ConcurrentQueue<string> output, string? line)
    {
        if (line is not null)
        {
            output.Enqueue(line);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
