# XenoAtom.MsBuildPipeLogger User Guide

XenoAtom.MsBuildPipeLogger lets one process run MSBuild with a custom logger while another process receives MSBuild logging events in real time.

There are two packages:

- `XenoAtom.MsBuildPipeLogger.Logger` - the logger assembly passed to MSBuild.
- `XenoAtom.MsBuildPipeLogger.Server` - the receiving library used by your host process.

## Transports

The logger currently supports:

- Anonymous pipes: pass the server's client handle as the logger parameter.
- Named pipes: pass `name=<pipeName>` and optionally `server=<serverName>`.
- Unix domain sockets: pass `socket=<socketPath>` when using the modern `net8.0` package target.

The Unix domain socket API is not available from the `netstandard2.0` target; use the `net8.0` assets when selecting that transport.

## Anonymous pipe example

```csharp
using MsBuildPipeLogger;

using var server = new AnonymousPipeLoggerServer();
server.AnyEventRaised += (_, e) => Console.WriteLine(e.Message);

string loggerParameter = server.GetClientHandle();
// Pass loggerParameter to MSBuild as the logger parameters value.

server.ReadAll();
```

## Named pipe example

```csharp
using MsBuildPipeLogger;

using var server = new NamedPipeLoggerServer("build-events");
server.AnyEventRaised += (_, e) => Console.WriteLine(e.Message);

string loggerParameter = "name=build-events";
// Pass loggerParameter to MSBuild as the logger parameters value.

server.ReadAll();
```

## Unix domain socket example

```csharp
using MsBuildPipeLogger;

string socketPath = Path.Combine(Path.GetTempPath(), $"msbuild-{Guid.NewGuid():N}.sock");
using var server = new UnixDomainSocketLoggerServer(socketPath);
server.AnyEventRaised += (_, e) => Console.WriteLine(e.Message);

string loggerParameter = $"socket={socketPath}";
// Pass loggerParameter to MSBuild as the logger parameters value.

server.ReadAll();
```

## Logger parameters

The logger accepts a small semicolon-separated parameter set:

- `<handle>` or `handle=<handle>`: connect to an anonymous pipe client handle.
- `name=<pipeName>`: connect to a local named pipe.
- `name=<pipeName>;server=<serverName>`: connect to a named pipe on a specific server.
- `socket=<socketPath>`: connect to a Unix domain socket (`net8.0` package target only).

`Read()` blocks until an event is available, the transport closes, or cancellation/disposal unblocks the server. `ReadAll()` keeps dispatching events until the stream ends or a `BuildFinishedEventArgs` is received.