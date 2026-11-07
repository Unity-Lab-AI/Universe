# Universe Builder Wiki

How the program is built, system by system, all from scratch. `verified` = checked against the code · `partial` = built in part, checked · `draft` = plan only · `stale` = retired or no longer true. The code lives in the DNA-AND tree (`src/`, `content/`) and is synced here; every page's `last-verified` is this repo's commit.

## Foundations

| Status | Page | What it covers |
|---|---|---|
| verified | [U1 · Toolchain](systems/u01-toolchain.md) | `build.cmd` and the C# compiler that ships with Windows; every warning an error |
| verified | [U2 · Windows layer](systems/u02-windows-layer.md) | The window, input, menus, settings, the landing page, the document viewer, the stats, the watchdog; Linux through Wine |
| verified | [U3 · Engine core](systems/u03-engine-core.md) | Maths, the CDF 9/7 wavelet and mode-E, the physics, the content reader, jobs on every core, precision from a moving anchor |
| verified | [U4 · Renderer](systems/u04-renderer.md) | Direct3D 12 by hand, one camera with no handoffs, reversed depth, points and lit meshes, detail per pixel on the card, GPU use, render scale |
| verified | [U5 · Generators](systems/u05-generators.md) | Galaxies, stars, systems, worlds, bodies, grains, elements, icons: every kind a template, nothing named in code |

## The universe

| Status | Page | What it covers |
|---|---|---|
| verified | [U6 · Universe](systems/u06-universe.md) | The cosmic web and the horizon, galaxies and their real stars, star systems, every body and its make-up, black holes and the fall, supernovae |
| verified | [U7 · Worlds](systems/u07-worlds.md) | Every kind of world, its ground to a grain, seas dived into, air and clouds, rings, moons, day and night, the living planet without life |
| verified | [U8 · Matter](systems/u08-matter.md) | Grains, crystals with their traces, atoms, electrons, nuclei, quarks, the zoom with no floor |
| verified | [U9 · Tour and captures](systems/u09-tour-and-captures.md) | The grand tour as a film of stops, screenshots, clips |

## Shipping and docs

| Status | Page | What it covers |
|---|---|---|
| verified | [U10 · Packaging](systems/u10-packaging.md) | The installer, the zip, both release pages, its own repos, the sync from DNA-AND |
| partial | [U11 · Docs](systems/u11-docs.md) | The public pages built into the program, these workflow files, what is still to write |

## Content

The templates the program grows from, in `content/` (`.unt`; the format is in `docs/GUIDE.md`). Each system page lists its own content files.

## Related

- [Work laws](../docs/WORK-LAWS.md): read before writing any code
- [Design record](../docs/DESIGN.md) · [Roadmap](../docs/ROADMAP.md) · [To-do](../docs/TODO.md) · [Finalized](../docs/FINALIZED.md)
- Public pages: [Readme](../README.md) · [Build your own universe](../docs/GUIDE.md) · [Technical guide](../docs/HOW-TO-BUILD.md) · [Against the field](../docs/COMPARED.md) · [On Linux](../docs/LINUX.md)
- [Schema](CLAUDE.md) · [Log](log.md)
