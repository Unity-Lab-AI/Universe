# ROADMAP — Major Task List (High-Level Phases / Milestones)

**Tier 1 of 3 — the MAJOR task list.** Holds the high-level phases / milestones that span multiple sessions or multiple PRs. Each major item breaks down into MINOR tasks in `docs/TODO.md`, which break down further into DECOMPOSED tasks in `docs/DECOMPOSED.md`.

YOLO mode reads all three tiers on activation. The cascade rule: complete the current decomposed task → next decomposed task in the same minor → if minor is done, next minor in the same major → if major is done, surface a milestone-boundary check-in to the user → start next major unless user stops.

Status markers (same scheme as TODO.md):
- `[ ]` pending
- `[~]` in_progress
- `[x]` complete (move to FINALIZED.md immediately, never leave here)

LAW #0 reminder: every milestone description preserves the user's verbatim words.

**Three-tier cascade:** ROADMAP.md (major, this file) → TODO.md (minor) → DECOMPOSED.md (decomposed). YOLO mode reads all three and works the cascade — see `.claude/commands/yolo.md` and `.claude/WORKFLOW.md §YOLO MODE`.

---

## What this project is

Universe is the universe of DNA-AND shipped on its own: "the full universe we have sdo anyone can use it and build with it" / "this is a tool menat to be stolen and used for any number of projects of any types wehere a universe is needed" / "noting this is just the univerese without game play or ui or features yet". One camera from the cosmic web of galaxies and the supermassive black holes at their hearts down to the quarks, every body with its real make-up, the four forces, the living planet without life, the grand tour, the templates, and the toolkit docs. It is made from the DNA-AND tree by `universe-sync.cmd` (the source, the content renamed `.unt`, the installer, the Linux script, the public pages) and built, packed and released by its own `release.cmd`. Life, gameplay and the interface are DNA-AND's and never come here. Its repos: git.unityailab.com/GFourteen/Universe and github.com/Unity-Lab-AI/Universe, public, `.claude/` included.

---

## In progress

- **U11 — Docs**: written in full; kept current with every change (`docs/TODO.md`).
- **U12 — The program's own road**: what is still to come for the program on its own (below).

---

## Stages

### [x] U1 — Build setup: C# with the compiler built into Windows
> "Use C# built into Windows"

`build.cmd` runs Windows' own `csc.exe` over `src/`, every warning an error, and writes `build\Universe.exe`; `build.cmd NAME` a separate copy. No packages, no outside libraries, no build tools. Spec: `wiki/systems/u01-toolchain.md`.

### [x] U2 — Talking to Windows directly (Linux through Wine; native Linux and Mac later)
> "windows only for now" / "do .exe run on linix.. how does linix people install?" / "okay well make sure that path is real and its documented propley for how linix people can use the app with out crazy workarounds"

The window, the input, the menus, the settings, the landing page, the document viewer, the stats, the watchdog, by our own Win32 declarations. On Linux, Wine with `linux/run.sh` in the zip. Spec: `wiki/systems/u02-windows-layer.md`.

### [x] U3 — Engine core
> "this is real fucking univaresal laws we shall be using electomagnatism strong and weak nuclear forces all of it"

Maths, the CDF 9/7 wavelet and mode-E (the waveform), the physics (`content/physics.unt`), the content reader, jobs on every core, double precision from a moving anchor. Spec: `wiki/systems/u03-engine-core.md`.

### [x] U4 — The renderer: Direct3D 12 by hand, one camera, no handoffs
> "no handoffs, that is the shit zoom staging i was talking about" / "and it better be rela infinate zoom in the code so if its not fix it" / "nothing should run on cpu unless absolutely necessary"

Direct3D 12 and DXGI through their COM tables, our own HLSL compiled as the program starts, reversed float depth, points and lit meshes, the detail worked out per pixel on the card, the GPU-use setting, the render scale. Spec: `wiki/systems/u04-renderer.md`.

### [x] U5 — The generators, every one a template
> "thats a problem they were all suppose to be dynamic and procedurally genreated templet like things not hard code"

Galaxies, stars, star systems, which world forms where, the ground and its traits, the bodies in space, the grains, the elements, the icons: every kind an entry in `content/`, every value a range, nothing named in code. Spec: `wiki/systems/u05-generators.md`.

### [x] U6 — The universe: the cosmic web, galaxies, stars, systems, every body, black holes, supernovae
> "okay lets properly add 100 billion galaxies ... and lets do a properl 13.4 biillion light year across vissible universe" / "okay why cant i sleect obejct in ort clouds and astorid belsts and rouge planets and stars inbetween glaxies and where is the super massive black holdes" / "black holes tho will be a special case to fall into event horzon in pitch black till u get to the singularity" / "and what about super novas and nurton star creation and black hole creations"

About 100 billion galaxies along the cosmic web within a horizon that moves with you, each galaxy's real stars made round you, every star's system from the templates, every kind of body clickable with its make-up, black holes feeding or quiet with the fall to the singularity down the gravity well, supernovae of every real kind with what they leave. Spec: `wiki/systems/u06-universe.md`.

### [x] U7 — The worlds: the ground, the seas, the air, the living planet without life
> "a planet is a living breathing thing even without life, did you do all the magnma cores mangnetic fielsds atetonic plates volcanos earthqueakes all of that shit in totaltiy" / "need to fix water worlds and water zommming in to go to bed rock"

Every kind of world from `content/terrain.unt`, its ground built in the background on every core down to a grain, its seas dived into to the bed, its air and clouds, rings, moons as worlds, true day and night with Always day; plates, quakes, eruptions, the field, the air, the weather, ice ages, erosion, impacts on its own clock. Spec: `wiki/systems/u07-worlds.md`.

### [x] U8 — The matter: grains, crystals, atoms, electrons, nuclei, quarks, and on below
> "i love the scale when u go down to um u should start to see atoms and shit" / "okay but do we really have the full table of elements mix on all bodies" / "even electrons are zommable to"

The grain's face, every mineral's real crystal with its traces, every natural element placed, each atom its electrons in their real shells and orbitals, the nucleus by the strong force and the protons' push, the quarks in their gluon field, the zoom on below with no floor. Spec: `wiki/systems/u08-matter.md`.

### [x] U9 — The tour, the captures
> "okay do a full panoramic scenic view fly by and down and into attoms" / "put on ur movieaehat and make it proper dynamically and prcedural like" / "it should only make one clip as long as its recording"

The grand tour as a film of stops in `content/tours/grand-tour.unt`, shot afresh each viewing, from the cosmic web across to a far galaxy, down its feeding heart and through the horizon, a supernova, a star's system, an asteroid, a comet, a world, its ground, the atoms, a quark, and back out; screenshots (PNG) and clips (MP4, 30 a second, packed by quality, the clip's size a View choice). Spec: `wiki/systems/u09-tour-and-captures.md`.

### [x] U10 — Packaging: the installer, the zip, both release pages, its own repos
> "make the program into its own repo with everything needed to build the universe only so its one two remotes on git and github" / "make sure each project is properly named and there is no over lap"

Our own installer (`installer/Setup.cs`), `release.cmd`, the zip with the Linux script, 0.1.0 on git.unityailab.com/GFourteen/Universe and github.com/Unity-Lab-AI/Universe. Spec: `wiki/systems/u10-packaging.md`.

### [~] U11 — Docs: the readme, the guides, the comparison, the in-game pages, and these workflow files
> "the docs are all properly formated and created and designed around explaining the systems and shit technical and non techinical dos" / "make sure all docs are not stale, and when i say "all" i mean it all fucking documents, top to bottom look for single words that are wrong too"

The public pages (`README.md`, `CREDITS.md`, `docs/GUIDE.md`, `docs/HOW-TO-BUILD.md`, `docs/COMPARED.md`, `docs/LINUX.md`, `docs/in-game/`) built into the program's Help; the workflow files (this file, `docs/DESIGN.md`, `docs/TODO.md`, `docs/FINALIZED.md`, `docs/DECOMPOSED.md`, `docs/WORK-LAWS.md`, `wiki/`). Kept current with every change. Spec: `wiki/systems/u11-docs.md`.

### [~] U12 — The program's own road: what is still to come, apart from the game
> "noting this is just the univerese without game play or ui or features yet"

Each item keeps the creator's words from its TODO line; the public side of this list is "Still to come" in `README.md`.

- (built, waiting on the test: DNA-AND DESIGN 582) A star's layers down through to the heavier elements at its core: "stars shoiuld be mostly hydrogen till getting to it heavey m,etal cores ect ect"
- Gravity as a live force: orbits shaped moment by moment (today Kepler's law exactly), and the gravity switch (the switch built, waiting on the test: DNA-AND DESIGN 580): "like if i has a setting to turn off all gravity.. would stars explode pressures of all bodies zero and every body move to straight line travel... right?"
- (built, waiting on the test: DNA-AND DESIGN 581) The rest of relativity: length contraction, redshift, frame dragging
- Decay happening before your eyes (built, waiting on the test: DNA-AND DESIGN 583), and the fields particles are waves in
- Light rendered truly below its wavelength: "later when ther is color properly do it"
- The waveform carrying every field, from the cosmic web to the microbes (gas and star fields, the ground itself, clouds, stars' surfaces)
- Native Linux and Mac builds; the Wine path proven on a Linux desktop: "okay well make sure that path is real"
- Screens of the landing page, the installer and the document viewer in the readmes
- The readme's clip from a take the creator approves, and the social images from it

## TOMBSTONES

_(none)_

---

## How major milestones get added

Major milestones are added in any of these situations:

1. **User explicitly says one** — "the next big thing is X" / "I want to ship Y by Friday" / "make Z the goal" — add verbatim per LAW #0
2. **Sprint planning or roadmap session** — when planning multiple weeks/months of work, each major chunk becomes a milestone here
3. **YOLO mode reads existing minor tasks and identifies a clustering** — Unity may propose a major milestone if she sees several minor tasks that share a coherent goal; she proposes it and waits for user confirmation before adding it here

NEVER auto-add major milestones without user awareness. The MAJOR tier is the user-visible roadmap; surprise items break the contract.

## How major milestones get closed

When a major milestone's last minor task moves to FINALIZED:
1. Append a `## Milestone closed: <name> — <date>` block to FINALIZED.md with the full milestone summary (every minor task it contained + every decomposed task under those minors + the final test plan)
2. Flip the status here to `[x]`
3. Move the milestone block to a `## Closed milestones` section at the bottom (NEVER delete — same rule as TODO/FINALIZED ledger integrity)
4. If YOLO is active, surface a milestone-boundary check-in to the user before starting the next milestone — do NOT auto-continue past a major milestone boundary
