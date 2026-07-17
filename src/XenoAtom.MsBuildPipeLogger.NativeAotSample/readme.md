# XenoAtom.MsBuildPipeLogger — Native AOT sample

A minimal console host that proves the **receiver** (`XenoAtom.MsBuildPipeLogger`) works from a
[Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)-compiled application.

## Why this works

The receiver deserializes MSBuild events into XenoAtom-owned `PipeBuildEventArgs` types. It has **no
dependency on any `Microsoft.Build` assembly** and uses **no reflection**, so everything it touches is
statically reachable and trim/AOT-safe.

`Microsoft.Build.Locator` is deliberately *not* used and could not help here anyway: it resolves and
loads an MSBuild assembly into the current process on demand at runtime, which is exactly the kind of
dynamic loading Native AOT does not support. This host never loads MSBuild — it only *spawns*
`dotnet msbuild` and receives events over a named pipe. The bundled `netstandard2.0` logger assembly
(the only `Microsoft.Build`-based part) is loaded by that child `dotnet msbuild` process, not by the
AOT binary.

## Run it

```sh
# from the src directory
dotnet publish XenoAtom.MsBuildPipeLogger.NativeAotSample -c Release -r <rid>
./XenoAtom.MsBuildPipeLogger.NativeAotSample/bin/Release/net10.0/<rid>/publish/XenoAtom.MsBuildPipeLogger.NativeAotSample
```

Replace `<rid>` with your runtime identifier (e.g. `osx-arm64`, `linux-x64`, `win-x64`). The published
folder contains a self-contained native binary plus the isolated logger under
`XenoAtom.MsBuildPipeLogger/`. On success the host prints the streamed events and exits `0`:

```
SUCCESS: MSBuild events were received over the pipe by a Native AOT host.
```

Native AOT publishing requires the platform toolchain (e.g. Xcode command line tools on macOS,
`clang`/`zlib` on Linux, the MSVC build tools on Windows).
