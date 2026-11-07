# Build your own (technical)

> Universe is plain **C# 5 on Direct3D 12**, built by the C# compiler that ships with Windows. No packages, no outside libraries, no stored art: everything on screen is made at run time from a seed and the templates in `content/`.

For adding kinds of worlds, stars and galaxies without touching code, start with **Build your own universe** (`docs/GUIDE.md`). This page is for building, shipping and changing the code.

---

## 🧰 What you need

| | |
|---|---|
| 💻 **Windows 10 or 11**, 64-bit | |
| 🎮 **A Direct3D 12 graphics card** | The program picks the strongest one in the computer |
| 🔧 **Nothing to install** | The compiler is `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, part of Windows |

---

## 🏗️ Build it

```
build.cmd                 builds build\Universe.exe
build.cmd NAME            builds build\NAME\Universe.exe, a separate copy
```

Every warning is an error (`/warnaserror+`), so a build that finishes is clean.

### Start-up options

| Option | What it does |
|---|---|
| *(none)* | Opens on the landing page: the seed, Begin, the tour, the settings, the guide |
| `--seed N` | Opens the universe of that seed again |
| `--tour grand-tour` | Flies the guided tour (any tour in `content/tours/`) |
| `--record clip.mp4` | With `--tour`, films the whole tour as an MP4 |
| `--shots S` | Saves a PNG of the game every S seconds |
| `--native` | Opens at your screen's full resolution |

Screenshots (**F12**) and clips (**F9**) go in `captures` beside the program.

---

## 📦 Make the installer and the zip

```
release.cmd
```

This builds the release copy and packs it, the `content` folder and the documents into our own installer (`installer\Setup.cs`, no outside installer tools). It writes:

| File | What it is |
|---|---|
| `release\Universe-Setup-<version>.exe` | Installs to your Programs folder, with Start menu and desktop shortcuts, an Apps list entry and an uninstaller |
| `release\Universe-<version>-windows.zip` | The same files, to unzip and run |

Change the version at the top of `release.cmd`.

---

## 🗺️ Where things are

| Folder | What it holds |
|---|---|
| `content/*.dnat` | **The templates** the universe grows from: the universe itself (`universe.dnat`), galaxies, stars, each kind of world and where it forms (`terrain.dnat`), zones such as the liquid-water zone and the frost line, the 118 elements, minerals and ground mixes, physics laws and gases, tours (`tours/`) |
| `src/core` | Maths: vectors, matrices, random numbers, the CDF 9/7 wavelet (`Wavelet97`), mode-E (`ModeE`), and the waveform that gives every surface its detail (`Waveform`); reading content (`Content`) |
| `src/gen` | The making: galaxies (`GalaxyGen`), star systems and which kind of world forms where (`SpaceGen`), a world's ground and its own traits from its template (`PlanetTerrain`), bodies in space (`BodyGen`), grains (`GrainField`) |
| `src/shell` | The universe view (`UniverseView`: camera, zoom, picking, info cards, time, days, the tour), the scale and cards on screen (`ScaleHud`), the matter below the ground (`MatterView`), the Help menu's documents (`PreviewText`) |
| `src/render` | Direct3D 12: the device, picking the graphics card, timing its work, the point and mesh pipelines and their shaders |
| `src/platform` | The window, input, menus, the landing page (`Launcher`), settings (`%LOCALAPPDATA%\Universe\settings.txt`), the document viewer (`DocViewer`) |
| `docs/` | The guides. They're built into the program too, so **Help** shows the same pages |
| `installer` | Our own installer, and the icons, drawn by our own `MakeIcons.cs` |

---

## ⚙️ How it holds together

| | |
|---|---|
| 🧬 **Templates** | Each kind of thing is an entry in `content/` with ranges. A thing made from it takes its own value in each range from its seed (`PlanetTerrain.Instance`), so every world is its own and a seed always gives the same one. Which kind forms where is read from the template too (`SpaceGen.WorldKind`) |
| ⚓ **One camera** | `UniverseView` keeps the camera as an anchor (a galaxy's centre, a star, a world) plus an offset, in double precision, so it's exact from light-years to atoms. Nothing switches views |
| 🔍 **Zoom** | Each notch of the wheel moves a share of the way to the nearest thing, so it slows by itself and never runs out. W A S D Q E fly at half the distance to the nearest thing a second |
| 📏 **Depth** | Reversed floating-point depth (`Mat4.Perspective`, compare GREATER), so near and far never fight |
| 🌌 **The galaxies** | About 100 billion in a sphere 13.4 billion light-years across, made in regions round you along the cosmic web; beyond them, a glow of points along the same web that fades in just where the galaxies made in full fade out |
| 🌀 **A galaxy** | One star field: the far view is the first part of the near view, so nothing swaps. Real stars round you are made in 25-light-year cubes at true density, each from its own position |
| 🌍 **Worlds** | Ground is built in the background on every core, finer under the camera. Changes (mountains, craters) are stored as CDF 9/7 wavelet terms and swapped in whole |
| 🪨 **Grains and matter** | `GrainField` is one field of loose rock for a whole world. `MatterView` places grains, crystals, atoms, electrons and nuclei at their real sizes round where you dive in |
| 🌊 **Detail at every size** | `Waveform`: stored CDF 9/7 waveforms, one per octave, and mode-E past them |
| ⏱️ **The card's time** | Two Direct3D 12 timestamps a frame measure the card's work; the GPU use setting (10 to 90%) holds it there by turning the detail up or down |
| ⚛️ **Physics** | `Physics` (`src/core/Physics.cs`) reads gravity and light from `content/physics.dnat`: Kepler orbits, pulls, escape speeds, black-hole horizons. `MatterView` reads the strong force, the protons' push, the binding-energy formula and beta stability for every nucleus |
| 🕰️ **Time** | `UniverseView.TimeSpeeds` sets how many years pass each second; a world's day is one turn of its spin (its template's `day-hours`) |

---

## ✏️ Change it

| You want to change | Go to |
|---|---|
| Kinds of worlds, stars, galaxies | `content/*.dnat` (see **Build your own universe**) |
| A different universe | Change the seed: the same seed always makes the same universe |
| The look of points | The pixel shader in `src/render/Pipeline.cs` |
| Controls | `UniverseView.Update` |
| The tour's camera | `UniverseView.TourStep`, and the stops in `content/tours/` |
| The info cards | `UniverseView.Card` (what they say), `ScaleHud.DrawCard` (how they look) |
| The Help documents | `docs/` (built in by `build.cmd`'s `/resource` lines), shown by `DocViewer` |
| Minerals and elements | `content/minerals.dnat`, `content/elements.dnat` |
| The laws of nature | `content/physics.dnat`, read by `Physics` and `MatterView` |

> **Keep it a toolkit:** read traits from templates, never test for a kind's name in code; give every trait a range; make things from their seed, never store them; and do heavy work on the graphics card.

---

## 👥 License and credits

Made by the **Unity AI Lab**. [unityailab.com](https://unityailab.com)
