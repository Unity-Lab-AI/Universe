# Universe: how to build, change and ship your own

Universe is plain C# 5 with Direct3D 12, built by the C# compiler that ships with Windows. There are no packages, no outside libraries and no stored art: everything on screen is made at run time from a seed.

## What you need

- Windows 10 or 11, 64-bit
- A graphics card with Direct3D 12 (the program picks the strongest one in the computer)
- Nothing to install: the compiler is `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, part of Windows

## Build it

```
build.cmd                 builds build\Universe.exe
build.cmd NAME            builds build\NAME\Universe.exe, a separate copy (a running copy is never touched)
```

Started on its own it opens on the landing page (the seed, Begin, the tour, the settings, the guide). With options it goes straight in: `--seed N` plays the universe of that seed again; `--tour grand-tour` flies the guided tour; `--record clip.mp4` with `--tour` films the whole tour as an MP4; `--shots S` saves a PNG of the game every S seconds; `--native` opens it at your screen's full resolution. Screenshots (F12) and clips (F9) go in `captures` beside the program.

## Make the installer and the zip

```
release.cmd
```

This builds the release copy and puts it, the `content` folder and `README.txt` into a zip, which is packed inside our own installer (`installer\Setup.cs`, no outside installer tools). It writes:

- `release\Universe-Setup-<version>.exe`: installs to your Programs folder, with Start menu and desktop shortcuts, an Apps list entry and an uninstaller
- `release\Universe-<version>-windows.zip`: the same files, to unzip and run

Change the version at the top of `release.cmd`.

## Where things are

| Folder | What it holds |
|---|---|
| `src/core` | maths: vectors, matrices, random numbers; the CDF 9/7 wavelet (`Wavelet97`), mode-E (`ModeE`) and the waveform that gives every surface its detail at every size (`Waveform`) |
| `src/platform` | the window, input, menus, settings (`%LOCALAPPDATA%\Universe\settings.txt`), the document viewer (`DocViewer`) |
| `src/render` | Direct3D 12: the device, picking the graphics card, timing its work each frame (`Gpu.GpuMs`), the point and mesh pipelines and their shaders |
| `src/gen` | the universe's making: galaxies (`GalaxyGen`), star systems and units (`SpaceGen`), planet ground (`PlanetTerrain`), the CDF 9/7 wavelet (`Wavelet97`), mode-E detail (`ModeE`), the letters (`IconFont`) |
| `src/shell` | the one world (`WorldView`: the camera, zoom, picking, info cards, time and days), the scale and cards on screen (`ScaleHud`), the matter below the ground at real sizes (`MatterView`: grains, crystals, molecules, atoms, electron shells, nuclei), the Help texts (`PreviewText`) |
| `content/*.dnat` | the facts the universe is made from: the visible universe (`universe.dnat`: its size, its 100 billion galaxies, the cosmic web), tours (`tours/`), each kind of world (`terrain.dnat`), physics laws and gases (`physics.dnat`), kinds of stars (mass, radius, temperature), galaxies, zones (habitable, frost line), the 118 elements with their most common isotopes (`elements.dnat`), minerals and what each kind of ground is made of (`minerals.dnat`) |
| `installer` | our own installer |

## How the world holds together

- **One camera, one world.** `WorldView` keeps the camera as an anchor (a galaxy's centre, a star, a planet) plus an offset from it, in double precision, so it stays exact from light-years to atoms. Galaxies, stars, systems, planet ground and matter are all drawn by the same camera; nothing switches views.
- **Zoom.** Each notch of the wheel moves a share of the way to the nearest thing (or to what you picked), so it slows by itself as you close in and never runs out. W A S D Q E fly at half the distance to the nearest thing a second.
- **Depth.** Reversed floating-point depth (`Mat4.Perspective`, compare GREATER), so near and far never fight.
- **Galaxies.** Each galaxy is one star field: the far view is the first part of the near view, so nothing swaps as you close in. Real stars around you are made in 25-light-year cubes at true density, each from its own position.
- **Planets.** Ground is built in the background on every core, finer under the camera, sized to your screen. Changes to a planet (mountains, craters) are stored as CDF 9/7 wavelet terms and swapped in whole.
- **The galaxies.** About 100 billion in a sphere 13.4 billion light-years across (`content/universe.dnat`), made by regions round you at the real density along the cosmic web; beyond, a glow of points along the same web; you are held inside the edge.
- **Grains.** `GrainField` is one field of loose rock for the whole planet, every size from boulders to sand, each grain's place, size, mineral and shape from the seed; the planet's ground is the top of these grains, and the matter view goes into the same ones.
- **Detail at every size.** `Waveform`: stored CDF 9/7 waveforms, one per octave, and mode-E past them, for the ground's roughness and every grain's face and stains.
- **Below the ground.** `MatterView` places everything at its real size round the point you dive into: the grain's face in rings of detail, the things lying on it, its crystal from its published unit cell, each atom's electron shells, bonds as shared electrons, and the nucleus settled by the strong and electric forces (`content/physics.dnat`).
- **The card's time.** Two Direct3D 12 timestamps a frame measure how long the card works; the GPU use setting (10 to 90%) holds it there by turning the detail up or down.
- **Time.** `WorldView.TimeSpeeds` sets how many years pass each second; each planet's day (`DayHours`) turns its starlight across the ground.

## Change it

- **New kinds of stars or galaxies:** add entries to `content/*.dnat`. The universe reads them at start.
- **A different universe:** change the seed; the same seed always makes the same universe.
- **The look of points:** the pixel shader in `src/render/Pipeline.cs`.
- **Controls:** `WorldView.Update`.
- **The info cards:** `WorldView.Card` (what they say) and `ScaleHud.DrawCard` (how they look).
- **Help texts:** `src/shell/PreviewText.cs`. The plain-words guide and this guide are built into the .exe from `docs/release` (`build.cmd` `/resource`).
- **Minerals and elements:** `content/minerals.dnat` and `content/elements.dnat`.

Every warning is an error (`/warnaserror+`), so a build that finishes is clean.

## License and credits

Made by the Unity AI Lab. https://unityailab.com
