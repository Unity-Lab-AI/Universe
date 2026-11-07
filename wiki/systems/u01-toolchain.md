---
title: U1 · Toolchain — Windows' built-in C# compiler
status: verified
sources: [build.cmd, release.cmd, docs/HOW-TO-BUILD.md, DNA-AND docs/DESIGN.md (rounds 19–20)]
last-verified: 1591b7f
---

# U1 · Toolchain

> "Use C# built into Windows"

Universe is plain C# 5 compiled by `csc.exe`, the C# compiler inside the .NET Framework that ships with Windows 10 and 11 (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). Nothing is downloaded and nothing is installed to build it.

## What we use, and nothing else

| Allowed | Why |
|---|---|
| `csc.exe` | The compiler that comes with Windows |
| The core .NET runtime (`mscorlib`, `System`, `System.Drawing` and Windows Forms for the installer's wizard) | Ships with Windows |
| Windows system files (`kernel32`, `user32`, `gdi32`, `psapi`, `d3d12`, `dxgi`, `d3dcompiler_47`, Media Foundation) | Called directly from our own declarations for the window, input, graphics, timing and the MP4 |

Not used: packages, libraries, engines, outside build tools, stored art.

## The build

```
build.cmd        → csc.exe over everything in src/ → build\Universe.exe
build.cmd NAME   → build\NAME\Universe.exe, a separate copy
release.cmd      → the release build, release\Universe-Setup-<version>.exe and release\Universe-<version>-windows.zip
```

Every warning is an error (`/warnaserror+`). The documents in `docs/` are compiled in as resources (`/resource`), so Help shows the same pages as the repo. In the DNA-AND tree the same source is checked with `build.cmd chk` (the HLSL check) before every ship.

## Proof it works

1. `build.cmd` produces the exe with no errors or warnings.
2. The exe opens its landing page and closes cleanly.
3. The same source always builds the same way.

See also: [Technical guide](../../docs/HOW-TO-BUILD.md) · [U10 Packaging](u10-packaging.md)
