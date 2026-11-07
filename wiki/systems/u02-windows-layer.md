---
title: U2 · Windows layer
status: verified
sources: [src/platform/Win32.cs, Window.cs, Input.cs, Launcher.cs, DocViewer.cs, Settings.cs, Stats.cs, Watchdog.cs, linux/run.sh, docs/LINUX.md]
last-verified: 1591b7f
---

# U2 · Windows layer

> "windows only for now" · Using Windows itself and the graphics card driver: "sure"

The only code we don't write is Windows itself and the graphics card driver. Everything talks to them directly, through our own declarations (`src/platform/Win32.cs`), with nothing in between.

## Built

| Piece | File | What it does |
|---|---|---|
| The window | `Window.cs` | The window and its message pump, the menu bar (Exit, Start page; View: GPU use, Time with Always day, FPS and the GPU and CPU stats, the tour, the periodic table, screenshot, record a clip, clip size, drag speed, invert drags, render scale; Help: Documents, Readme, How it works, Against the field, Credits, About), per-monitor DPI, windowed only, Alt+Enter blocked, `--native` for a window the size of the screen |
| Input | `Input.cs` | Keys and mouse, the wheel's glide, Ctrl for eight notches, the activating click |
| The landing page | `Launcher.cs` | Seed, Random, Begin, Take the tour, Film the tour, the Clips list with Play and Open folder, the tick boxes (frames a second, the stats, invert up-down, drag speed) |
| The document viewer | `DocViewer.cs` | Markdown rendered to rich text (headings, bold, lists, code, tables, links), the contents list of every document and its headings, Find across all documents (Ctrl+F, Enter, F3), text size, Back, copy; the RichEdit control |
| Settings | `Settings.cs` | `%LOCALAPPDATA%\Universe\settings.txt`: frames a second, the stats, drag speed, the inverts, always day, GPU use, render scale, clip size |
| The stats | `Stats.cs` | Twice a second: the processor's and the card's time a frame, this process's share of every core (`GetProcessTimes`), its working set, the card's memory and budget (`IDXGIAdapter3::QueryVideoMemoryInfo`), the detail, the ground pieces waiting |
| The watchdog | `Watchdog.cs` | Timed test runs force-closed if they overrun; the run log beside the program |

## Linux

Through Wine, which carries Direct3D 12 on Vulkan: `linux/run.sh` in the zip checks Wine 8 or newer and a Vulkan driver, makes a private Wine prefix, and runs the exe. The Steam way through Proton is in `docs/LINUX.md`. The path waits on a Linux desktop's report. Native Linux and Mac layers come later (U12).

See also: [U4 Renderer](u04-renderer.md) · [On Linux](../../docs/LINUX.md)
