# XenoAtom.MsBuildPipeLogger User Guide

XenoAtom.MsBuildPipeLogger lets one process run MSBuild with a custom logger while another process receives MSBuild logging events in real time.

There are two packages:

- `XenoAtom.MsBuildPipeLogger.Logger` - the logger assembly passed to MSBuild.
- `XenoAtom.MsBuildPipeLogger.Server` - the receiving library used by your host process.

## Transports

The `netstandard2.0` logger assembly currently supports:

- Anonymous pipes: pass the server's client handle as the logger parameter.
- Named pipes: pass `name=<pipeName>` and optionally `server=<serverName>`.

Unix domain sockets are not exposed because the logger package must remain a single `netstandard2.0` assembly and the required socket endpoint API is not available there without reflection-based workarounds.

## Anonymous pipe example

```csharp
using XenoAtom.MsBuildPipeLogger;

using var server = new AnonymousPipeLoggerServer();
server.AnyEventRaised += (_, e) => Console.WriteLine(e.Message);

string loggerParameter = server.GetClientHandle();
// Pass loggerParameter to MSBuild as the logger parameters value.

server.ReadAll();
```

## Named pipe example

```csharp
using XenoAtom.MsBuildPipeLogger;

using var server = new NamedPipeLoggerServer("build-events");
server.AnyEventRaised += (_, e) => Console.WriteLine(e.Message);

string loggerParameter = "name=build-events";
// Pass loggerParameter to MSBuild as the logger parameters value.

server.ReadAll();
```

## Logger parameters

The logger accepts a small semicolon-separated parameter set:

- `<handle>` or `handle=<handle>`: connect to an anonymous pipe client handle.
- `name=<pipeName>`: connect to a local named pipe.
- `name=<pipeName>;server=<serverName>`: connect to a named pipe on a specific server.

`Read()` blocks until an event is available, the transport closes, or cancellation/disposal unblocks the server. `ReadAll()` keeps dispatching events until the stream ends or a `BuildFinishedEventArgs` is received.
