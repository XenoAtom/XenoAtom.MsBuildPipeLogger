# XenoAtom.MsBuildPipeLogger [![ci](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/actions/workflows/ci.yml/badge.svg)](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/actions/workflows/ci.yml)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/XenoAtom/XenoAtom.MsBuildPipeLogger/main/img/XenoAtom.MsBuildPipeLogger.png">

A single package that lets one process run MSBuild with a bundled pipe logger while another process receives MSBuild event data in real time.

## ✨ Features

- `XenoAtom.MsBuildPipeLogger`: receiver APIs that deserialize events and dispatch the normal MSBuild logging callbacks.
- Includes the MSBuild logger assembly as isolated copy-to-output content under `XenoAtom.MsBuildPipeLogger/`.
- Helper APIs return the bundled logger path/specification to pass directly to MSBuild.
- Supports anonymous pipes and named pipes from a `netstandard2.0` logger assembly.
- Nullable-enabled projects with package metadata/readme/icon configured for the publishable package.

## 📦 Package

Install the single package:

```sh
dotnet add package XenoAtom.MsBuildPipeLogger
```

The package copies `XenoAtom.MsBuildPipeLogger.Logger.dll` to an isolated `XenoAtom.MsBuildPipeLogger` output subfolder so MSBuild does not probe unrelated application assemblies from the logger directory.

## 📖 User Guide

For more details on how to use XenoAtom.MsBuildPipeLogger, please visit the [user guide](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/blob/main/doc/readme.md).

## 🪪 License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause).

## 🤗 Author

Alexandre Mutel aka [XenoAtom](https://xoofx.github.io).
