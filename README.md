# Biohazrd

[![MIT Licensed](https://img.shields.io/github/license/mochilibraries/biohazrd?style=flat-square)](LICENSE.txt)
[![Sponsor](https://img.shields.io/badge/support_original_creator-%E2%9D%A4-lightgrey?logo=github&style=flat-square)](https://github.com/sponsors/PathogenDavid)

This repo is our maintained fork of the Biohazrd project. The original project can be found [here](https://github.com/MochiLibraries/Biohazrd).

Biohazrd is a framework for creating binding generators for C **and** C++ libraries. It aims to lower the amount of ongoing boilerplate maintenance required to use native libraries from .NET as well as allow direct interoperation with C++ libraries without a C translation later.

We also have peliminary documentation available in [the docs folder](docs/).

## License

This project is licensed under the MIT License. [See the license file for details](LICENSE.txt).

Additionally, this project has some third-party dependencies. [See the third-party notice listing for details](THIRD-PARTY-NOTICES.md).

## Quick Overview

Here's a quick overview of the individual components of this repository:

| Project | Description |
|---------|-------------|
| [`Biohazrd`](https://www.nuget.org/packages/Biohazrd.Core/) | The core of Biohazrd. This is the code is primarily responsible for parsing the Cursor tree of libclang` and translating it into a simplified model that's easier to work with.
| [`Biohazrd.Transformation`](https://www.nuget.org/packages/Biohazrd.Transformation/) | Language-agnostic functionality for transforming the immutable object model output by the core. (As well as a [some common transformations you might need](docs/BuiltInTransformations/))
| [`Biohazrd.OutputGeneration`](https://www.nuget.org/packages/Biohazrd.OutputGeneration/) | Language-agnostic functionality for writing out code and other files.
| [`Biohazrd.CSharp`](https://www.nuget.org/packages/Biohazrd.CSharp/) | Transformations, output generation, and other infrastructure for supporting emitting a C# interop layer.
| [`Biohazrd.Utilities`](https://www.nuget.org/packages/Biohazrd.Utilities/) | Optional helpers that don't fit anywhere else.
| [`Biohazrd.AllInOne`](https://www.nuget.org/packages/Biohazrd/) | A convenience package which brings in all of the other components of Biohazrd.
| `Tests` | Automated tests for Biohazrd.
