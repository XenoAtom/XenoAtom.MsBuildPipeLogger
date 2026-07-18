# XenoAtom.MsBuildPipeLogger User Guide

XenoAtom.MsBuildPipeLogger lets one process run MSBuild with a custom pipe logger while another process receives MSBuild logging events in real time.

Install the single package in the receiving/host process:

```sh
dotnet add package XenoAtom.MsBuildPipeLogger
```

The package contains:

- `XenoAtom.MsBuildPipeLogger.dll` - the receiving/server library used by your host process.
- `XenoAtom.MsBuildPipeLogger/XenoAtom.MsBuildPipeLogger.Logger.dll` - the bundled MSBuild logger copied to an isolated output subfolder.

The logger assembly is intentionally isolated in its own output folder. MSBuild task/logger loading can probe assemblies from the logger directory, so the package keeps unrelated host-process assemblies out of that folder.

## Local event model — no Microsoft.Build required

The receiver deserializes events into XenoAtom's own `PipeBuildEventArgs` types (`PipeBuildStartedEventArgs`, `PipeProjectStartedEventArgs`, `PipeProjectEvaluationFinishedEventArgs`, `PipeBuildMessageEventArgs`, `PipeTaskCommandLineEventArgs`, `PipeBuildErrorEventArgs`, and so on). It does **not** reference any `Microsoft.Build` assembly.

Because the wire format is XenoAtom's own — not the MSBuild binary-log format — the receiver is independent of the MSBuild version that produced the build. There is **no need for `Microsoft.Build.Locator`, no exact version to match, and nothing to keep out of your output folder**:

```xml
<ItemGroup>
  <PackageReference Include="XenoAtom.MsBuildPipeLogger" Version="..." />
</ItemGroup>
```

```csharp
using var server = new NamedPipeLoggerServer("my-pipe");
server.MessageRaised += m => Console.WriteLine(m.Message);
server.ReadAll();
```

The bundled logger assembly is loaded by MSBuild itself (in the process running the build) from the isolated `XenoAtom.MsBuildPipeLogger/` subfolder, where it reads MSBuild's real `BuildEventArgs` through the public API and serializes them into the wire format. Only the logger side touches `Microsoft.Build`, and it uses whatever MSBuild is already loaded in the build process.

If your host application uses `Microsoft.Build` APIs for its *own* reasons (unrelated to receiving pipe events), that remains your concern and the usual [Microsoft.Build.Locator](https://www.nuget.org/packages/Microsoft.Build.Locator) guidance applies — but XenoAtom.MsBuildPipeLogger itself no longer requires it.

Because the receiver has no `Microsoft.Build` dependency and uses no reflection, it is safe to consume from a [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) host. The `XenoAtom.MsBuildPipeLogger.NativeAotSample` project under `src/` publishes a native binary that spawns `dotnet msbuild` and receives events over a pipe end to end; `Microsoft.Build.Locator` is not used and would not work under AOT anyway, since the host never loads MSBuild into its own process.

## Wire fidelity

The pipe format is a deliberately curated projection of MSBuild's events, not a byte-for-byte copy of every `BuildEventArgs` field. Each `Pipe*EventArgs` type carries the fields the receiver needs to reconstruct a build, populated exclusively through MSBuild's public API. Every event carries the common base fields (`Message`, `HelpKeyword`, `SenderName`, `Timestamp`, `ThreadId`, and the originating `BuildEventContext`) plus the event-specific fields documented on each `Pipe*EventArgs` type, including:

- `PipeProjectStartedEventArgs`: project id/file, requested targets, tools version, global/evaluated properties and items, and the **parent project build context** (`ParentProjectBuildEventContext`) used to reconstruct the project tree.
- `PipeTargetStartedEventArgs`: target/project/target-file names, parent target, and the **build reason** (`BuildReason`).
- `PipeTargetFinishedEventArgs`: target/project/target-file names, success, and the target's **output items** (`TargetOutputs`, populated only when the build enables target-output logging).
- `PipeProjectEvaluationFinishedEventArgs` / `PipeTaskParameterEventArgs`: evaluated properties and items, and resolved task inputs/outputs.

The wire format is **append-only**: fields are only ever added to the end of a record, never removed or reordered. A newer writer can therefore stream to an older receiver (which ignores trailing bytes it does not understand), and a newer receiver reads records from an older writer by treating the absent trailing fields as their defaults. If you rely on an MSBuild event field that is not currently projected, it can be added the same way — open an issue or PR.

## Transports

The bundled `netstandard2.0` logger currently supports:

- Anonymous pipes: pass the server's client handle as the logger parameter.
- Named pipes: pass `name=<pipeName>` and optionally `server=<serverName>`.

Unix domain sockets are not exposed because the logger must remain a single `netstandard2.0` assembly and the required socket endpoint API is not available there without reflection-based workarounds.

## Passing the bundled logger to MSBuild

Use `PipeLoggerServer.GetLoggerSpecification(...)` to build the `type,assembly;parameters` string expected by MSBuild's logger option. This uses `AppContext.BaseDirectory` and the package's isolated logger subfolder.

```csharp
using System.Diagnostics;
using XenoAtom.MsBuildPipeLogger;

var pipeName = $"build-events-{Guid.NewGuid():N}";
using var server = new NamedPipeLoggerServer(pipeName);
server.AnyEventRaised += e => Console.WriteLine(e.Message);

using var process = new Process();
process.StartInfo.FileName = "dotnet";
process.StartInfo.ArgumentList.Add("msbuild");
process.StartInfo.ArgumentList.Add("MyProject.csproj");
process.StartInfo.ArgumentList.Add("/nologo");
process.StartInfo.ArgumentList.Add("/nr:false");
var loggerSpecification = PipeLoggerServer.GetLoggerSpecification($"name={pipeName}");
process.StartInfo.ArgumentList.Add($"/logger:{loggerSpecification}");
process.StartInfo.UseShellExecute = false;

var readTask = Task.Run(server.ReadAll);
process.Start();
process.WaitForExit();
readTask.Wait();
```

If you only need the logger assembly path, use `PipeLoggerServer.GetLoggerAssemblyPath()`.

## Anonymous pipe example

```csharp
using System.Diagnostics;
using XenoAtom.MsBuildPipeLogger;

using var server = new AnonymousPipeLoggerServer();
server.AnyEventRaised += e => Console.WriteLine(e.Message);

var loggerSpecification = PipeLoggerServer.GetLoggerSpecification(server.GetClientHandle());

using var process = new Process();
process.StartInfo.FileName = "dotnet";
process.StartInfo.ArgumentList.Add("msbuild");
process.StartInfo.ArgumentList.Add("MyProject.csproj");
process.StartInfo.ArgumentList.Add($"/logger:{loggerSpecification}");
process.StartInfo.UseShellExecute = false;

var readTask = Task.Run(server.ReadAll);
process.Start();
process.WaitForExit();
readTask.Wait();
```

## Named pipe example

```csharp
using System.Diagnostics;
using XenoAtom.MsBuildPipeLogger;

var pipeName = $"build-events-{Guid.NewGuid():N}";
using var server = new NamedPipeLoggerServer(pipeName);
server.AnyEventRaised += e => Console.WriteLine(e.Message);

var loggerSpecification = PipeLoggerServer.GetLoggerSpecification($"name={pipeName}");

using var process = new Process();
process.StartInfo.FileName = "dotnet";
process.StartInfo.ArgumentList.Add("msbuild");
process.StartInfo.ArgumentList.Add("MyProject.csproj");
process.StartInfo.ArgumentList.Add($"/logger:{loggerSpecification}");
process.StartInfo.UseShellExecute = false;

var readTask = Task.Run(server.ReadAll);
process.Start();
process.WaitForExit();
readTask.Wait();
```

## Logger parameters

The logger accepts a small semicolon-separated parameter set:

- `<handle>` or `handle=<handle>`: connect to an anonymous pipe client handle.
- `name=<pipeName>`: connect to a local named pipe.
- `name=<pipeName>;server=<serverName>`: connect to a named pipe on a specific server.

`Read()` returns the next `PipeBuildEventArgs` and blocks until an event is available, the transport closes, or cancellation/disposal unblocks the server. `ReadAll()` keeps dispatching events until the stream ends or a `PipeBuildFinishedEventArgs` is received.
