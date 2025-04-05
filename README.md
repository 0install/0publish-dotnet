# 0publish - .NET version

[![Build](https://github.com/0install/0publish-dotnet/workflows/Build/badge.svg)](https://github.com/0install/0publish-dotnet/actions?query=workflow%3ABuild)  
0publish is a command-line tool for creating and modifying Zero Install feed files.

**[Documentation and download instructions](https://docs.0install.net/tools/0publish/)**

## Other versions

See also the [Python version](https://github.com/0install/0publish) of this program, for use on Linux and macOS.

## Building

The source code is in [`src/`](src/) and generated artifacts are placed in `artifacts/`.  
The source code does not contain version numbers. Instead the version is determined during CI using [GitVersion](https://gitversion.net/).

To build install [Visual Studio 2022 v17.13 or newer](https://www.visualstudio.com/downloads/) and run `.\build.ps1`.  

## Contributing

We welcome contributions to this project such as bug reports, recommendations, pull requests and [translations](https://www.transifex.com/eicher/0install-win/). If you have any questions feel free to pitch in on our [friendly mailing list](https://0install.net/support.html#lists).

This repository contains an [EditorConfig](http://editorconfig.org/) file. Please make sure to use an editor that supports it to ensure consistent code style, file encoding, etc.. For full tooling support for all style and naming conventions consider using JetBrains' [ReSharper](https://www.jetbrains.com/resharper/) or [Rider](https://www.jetbrains.com/rider/) products.
