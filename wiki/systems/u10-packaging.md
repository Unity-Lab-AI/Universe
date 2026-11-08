---
title: U10 · Packaging — the installer, the zip, both release pages, its own repos
status: verified
sources: [installer/Setup.cs, installer/MakeIcons.cs, release.cmd, build.cmd, linux/run.sh, DNA-AND universe-sync.cmd, DNA-AND docs/DESIGN.md 377–381, 393, 456, 534]
last-verified: 1591b7f
---

# U10 · Packaging

> "make the program into its own repo with everything needed to build the universe only so its one two remotes on git and github" · "make sure each project is properly named and there is no over lap" · "installer is not finding wehere its already intstaslled and is defaulting to appdata"

## What ships

| File | What it is |
|---|---|
| `release\Universe-Setup-<version>.exe` | Our own installer wizard (`installer/Setup.cs`, Windows Forms, the payload inside itself): installs to the Programs folder with Start menu and desktop shortcuts, an Apps list entry and an uninstaller; finds a copy already installed (from its Apps entry, its shortcuts or the chosen folder) and updates it; over an earlier copy offers to delete its clips, screenshots and settings, each on its own; other files in a folder are never removed |
| `release\Universe-<version>-windows.zip` | The same files to unzip and run, with `run-on-linux.sh` |
| The icons | Drawn by code (`installer/MakeIcons.cs`), the program's own everywhere |

`release.cmd` builds the release copy and packs it with `content/` and the documents. The version is at its top.

## Where it lives

- git.unityailab.com/GFourteen/Universe (origin, over HTTPS) and github.com/Unity-Lab-AI/Universe (github); both public, `.claude/` included; `main` and `develop` moved together on every release and pushed to both; the 0.1.0 release's four assets replaced on both pages on every ship (`gh release upload --clobber` and the Forgejo API).
- Made from the DNA-AND tree by `universe-sync.cmd`: `src/`, `content/` renamed `.unt` with its headers reworded, `installer/`, `reference/` (the Uni Vs Matics equations and builders the `.c97` clip is ported from), `linux/run.sh`, the public pages from `docs/release/`; the program's own name throughout (`installer/universe-names.ps1`). The sync never touches this project's `.claude/`, `wiki/`, workflow docs or `.gitignore`.

## Linux

`linux/run.sh` in the zip: Wine 8 or newer and a Vulkan driver checked, a private prefix, the exe run; the Steam way through Proton. Waits on a Linux desktop's report (U12).

See also: [U1 Toolchain](u01-toolchain.md) · [Technical guide](../../docs/HOW-TO-BUILD.md) · [On Linux](../../docs/LINUX.md)
