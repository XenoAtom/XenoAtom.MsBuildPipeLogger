# XenoAtom.MsBuildPipeLogger [![ci](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/actions/workflows/ci.yml/badge.svg)](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/actions/workflows/ci.yml)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/XenoAtom/XenoAtom.MsBuildPipeLogger/main/img/XenoAtom.MsBuildPipeLogger.png">

A pair of MSBuild logger/server packages that stream MSBuild event data to another process.

## ✨ Features

- `XenoAtom.MsBuildPipeLogger.Logger`: an MSBuild logger that serializes build events using MSBuild's binary log event format.
- `XenoAtom.MsBuildPipeLogger.Server`: a receiver that deserializes events and dispatches the normal MSBuild logging callbacks.
- Supports anonymous pipes, named pipes, and Unix domain sockets on the modern `net8.0` package target.
- Nullable-enabled projects with package metadata/readme/icon configured for the two publishable packages.

## 📦 Packages

Only these projects are intended to be published:

- `XenoAtom.MsBuildPipeLogger.Logger`
- `XenoAtom.MsBuildPipeLogger.Server`

## 📖 User Guide

For more details on how to use XenoAtom.MsBuildPipeLogger, please visit the [user guide](https://github.com/XenoAtom/XenoAtom.MsBuildPipeLogger/blob/main/doc/readme.md).

## 🪪 License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause).

## 🤗 Author

Alexandre Mutel aka [XenoAtom](https://xoofx.github.io).