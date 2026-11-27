# Third-party components

ZeroUnity Batch is MIT licensed (see [LICENSE](LICENSE)), but it stands on other people's work:

## Bundled in this repository

| Component | Where | License |
|---|---|---|
| [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET), `.Texture`, `.MonoCecil` binaries | `Libs/` | MIT — © nesrak1 |
| `classdata.tpk` class database (shipped with UABEA / AssetsTools.NET) | `data/` | MIT — © nesrak1 |
| Crunch decoder (C# port of `crn_decomp.h`, `src/Zub.Core/Crn/`) | `src/Zub.Core/Crn/` | MIT — © nesrak1 (AssetsTools.NET), port of ZLIB-licensed crn_decomp © 2010-2016 Rich Geldreich, Jr. and Binomial LLC |

The vendored `src/Zub.Core/Crn/` files were taken from `AssetsTools.NET.Texture` (the decoder UABEA
Next uses) and patched in two places, both marked with comments: `ReadUInt24` is read by hand because
the version of `AssetsFileReader` shipped in `Libs/` consumes four bytes instead of three, which
shifted the whole crn header.

`Libs/` is committed on purpose so the project builds with a plain `dotnet build`, without
downloading binaries by hand. `data/classdata.tpk` is copied next to the executable by
`Zub.Core.csproj` and is required to read MonoBehaviour fields.

## NuGet dependencies

Avalonia 11 (+ Desktop, Fluent, Fonts.Inter, Controls.DataGrid), AvaloniaEdit + TextMate,
SkiaSharp, Newtonsoft.Json, Mono.Cecil, SixLabors.ImageSharp, AssetRipper.TextureDecoder —
all MIT or Apache-2.0.

## Not in this repository (portable `tools/` folder, built locally)

| Component | License |
|---|---|
| apktool | Apache-2.0 |
| zipalign / apksigner (Android build-tools) | Apache-2.0 |
| Il2CppDumper | MIT |
| Trimmed Java runtime built with `jlink` | GPLv2 + Classpath Exception |

The `dist/tools/` and `installer/stage/` folders are ignored by git: they hold ~92 MB of binaries
that are downloaded or generated when you build the portable/installer package (see README).
