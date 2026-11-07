<p align="center"><img src="docs/img/logo.png" width="140" alt="Universe: a spiral galaxy" title="Universe"></p>

<h1 align="center">Universe</h1>

<p align="center"><b><i>One camera. One universe. From the cosmic web down to a quark.</i></b></p>

<p align="center">
  <a href="https://git.unityailab.com/GFourteen/Universe/releases" title="Download Universe-Setup.exe from Unity AI Lab Git"><b>Download · Unity AI Lab Git</b></a>
  &nbsp;|&nbsp;
  <a href="https://github.com/Unity-Lab-AI/Universe/releases" title="Download Universe-Setup.exe from GitHub"><b>Download · GitHub</b></a>
</p>

<p align="center">Windows 10 and 11 &nbsp;·&nbsp; Direct3D 12 &nbsp;·&nbsp; 1.7 MB installed &nbsp;·&nbsp; 13.4 billion light-years &nbsp;·&nbsp; 100 billion galaxies &nbsp;·&nbsp; down to the quarks and below &nbsp;·&nbsp; built from scratch</p>

<p align="center">
  <a href="docs/GUIDE.md" title="Add kinds of worlds, stars and galaxies, change the laws of physics, write tours">Build your own universe</a>
  &nbsp;·&nbsp;
  <a href="docs/HOW-TO-BUILD.md" title="Build it, make the installer, find your way round the code">Technical guide</a>
  &nbsp;·&nbsp;
  <a href="CREDITS.md" title="Who made it, and the science it is made from">Credits</a>
</p>

---

## Fall through everything

![One zoom from a sky full of galaxies down to a world's ground](docs/img/journey.gif "One zoom, galaxies to ground, recorded in the game")

*One zoom from a sky full of galaxies, into a galaxy, past its stars, to a world and down to its ground. Recorded in the game.*

Pull back and galaxies are pinpoints on the **cosmic web**. Roll the wheel and one grows into an island of stars. Keep going.

| You are | What you see |
|---|---|
| **Among the galaxies** | 100 billion of them, strung along filaments and walls round great empty voids |
| **Inside a galaxy** | A hundred billion stars, made round you as you fly |
| **At a star** | Its worlds, moons, belts and comets swinging round it |
| **Over a world** | Seas, mountains, clouds, ice caps, volcanoes, rings |
| **Under the sea** | Down through the water, the light dying with depth, to the bed and its silt |
| **On the ground** | Stones, then grains, then the steps of a crystal's face |
| **In the matter** | Atoms, their electrons, the nucleus, its protons and neutrons, and the **quarks** inside them |

> **No loading screens along the way.** One camera across about 42 factors of ten, and you can turn round anywhere and fly all the way back out. Below the quark the zoom in simply goes on: there is no floor.

---

## Two things in one

| | |
|---|---|
| **A universe to fly through** | Install it, press **T**, and watch the grand tour from the cosmic web to a quark. Then take the controls and go anywhere. |
| **A toolkit to build universes** | Every galaxy, star and world grows from a **template** in plain text. Add your own kinds and use it for your own **game or simulation**. |

---

## Take it

Universe is built to be taken. It is **MIT licensed**: use it, change it, sell what you build with it, no permission needed. Any project of any kind that needs a universe:

| | |
|---|---|
| **A game** | Your worlds, grown from your templates, in your own style: the generators and the camera are yours to keep |
| **A simulation** | Real orbits, real physics numbers, a universe that is the same every run from one seed |
| **A film or a screensaver** | Write a tour in plain text and film it in the program |
| **A classroom** | Fly from the cosmic web to a quark, with a card on everything you click |
| **A piece of it** | The waveform engine, the galaxy maker, the star systems, the ground, the matter: each is its own folder of plain C#, with no packages to drag along |

Rename it, swap the icon, change the content, and `release.cmd` makes your installer. [Build your own universe](docs/GUIDE.md "Templates, worlds, stars, galaxies, physics, tours") shows the templates; the [Technical guide](docs/HOW-TO-BUILD.md "Build, ship and change the code") shows the code.

---

## A whole universe in under 2 MB

| | |
|---|---|
| **Download** | 0.59 MB installer |
| **The program** | 0.86 MB |
| **Every template** | 0.26 MB of plain text |
| **Everything installed** | 1.7 MB |
| **What it holds** | About 100 billion galaxies of about 100 billion stars each: some 10²² stars, every one with its own worlds, and every world down to its grains and their atoms |

That's less than one photo from a phone. It fits because **nothing in it is stored. Everything is grown.**

| | |
|---|---|
| **A seed, not a save** | The whole universe comes from one starting number. Every galaxy, star and pebble is worked out from that number and where it is, so it never needs to be kept: come back to a star and it's worked out again, exactly the same |
| **Templates, not copies** | A lava world isn't a picture of a lava world. It's a dozen lines of rules (where it forms, how big, what colours, how fast it spins), and every lava world in the universe is grown from those same lines |
| **Equations, not pictures** | The roughness of a mountain, a stone or a grain's face is a few waves of the waveform engine, kept as their equations, not as images. Zooming in just works out the next, finer wave |
| **Only what you can see** | Galaxies are made in the regions round you, stars in the cubes round you, ground in pieces finer the closer you are, atoms round the point you dive into. What you leave behind is let go, and made again if you return |
| **The graphics card does the work** | The fine detail, the stars' colours, the web of galaxies' light are worked out on the card for each pixel, every frame, instead of being loaded from disk |

So the 1.7 MB is the **recipe**, not the meal: the rules of the universe, and the code that follows them.

### Against the field

The honest comparison, with each project's own published numbers: every well-known program that makes or shows a universe, and the two tiny pioneers that showed the way. Sizes are what each asks for on disk.

| | On disk | What it holds | How |
|---|---|---|---|
| **Universe** | **1.7 MB** | About 10²² stars in about 100 billion galaxies, every world down to its grains, atoms and quarks, in one unbroken zoom | Everything grown from one seed and 0.26 MB of templates; no stored art, no sound |
| [Noctis IV](https://archive.org/details/msdos_noctis_shareware "Noctis IV on the Internet Archive") (2002) | About 1 to 5 MB | One galaxy of about 78 billion stars, with planets to walk on | Grown from a seed, by one person, for DOS |
| [Elite](https://www.theregister.com/2024/01/18/elite_game_retro/ "Elite at 40, The Register") (1984) | 22 KB | Eight galaxies of 256 planets | Grown from a seed: the first game to do it |
| [.kkrieger](https://en.wikipedia.org/wiki/.kkrieger ".kkrieger on Wikipedia") (2004) | 96 KB | One shooter level, about ten minutes of play (not a universe: the famous size benchmark) | Textures, models and music grown at start-up |
| [SpaceEngine](https://store.steampowered.com/app/314650/SpaceEngine/ "SpaceEngine on Steam") | 4 GB, 50 GB with its HD packs | Billions of galaxies of billions of stars, down to a world's ground; 130,000 real catalogued objects | Grown from position, plus catalogues and textures |
| [Elite Dangerous](https://www.elitedangerous.com/explore "Elite Dangerous: Explore") | About 25 GB | One galaxy, 400 billion star systems | Stellar Forge grows systems from first principles, plus art and sound |
| [No Man's Sky](https://www.gamespot.com/articles/no-mans-skys-18-quintillion-planets-take-up-just-6/1100-6441663/ "GameSpot, 2016: 18 quintillion planets in 6 GB") | 6 GB at launch (mostly sound), 10 to 15 GB now | 18 quintillion planets | Grown from a 64-bit seed, plus art and sound |
| [Universe Sandbox](https://universesandbox.com/faq/ "Universe Sandbox FAQ") | 4 GB | A system of bodies at a time, under gravity | A physics toy, not a universe to fly through |
| [Celestia](https://en.wikipedia.org/wiki/Celestia "Celestia on Wikipedia") 1.6 | About 36 MB | 118,000 real stars and the known worlds | Catalogues, nothing grown |
| [Pioneer](https://sourceforge.net/projects/pioneer-space-simulator.mirror/files/20260907/ "Pioneer downloads") | About 550 MB | A galaxy of millions of systems to fly and trade in | Grown from a seed, plus art and sound |

**Where it stands.** Nothing in that table goes from the cosmic web to a quark in one unbroken camera, and nothing but the two tiny pioneers comes near the size. Noctis is the closest in spirit: one person, one galaxy, a few megabytes, twenty years ago. SpaceEngine is the closest in scope, more than two thousand times the size, and it stops at the ground.

**To be fair.** The others carry what Universe doesn't yet: sound, stored art and textures, gameplay, ships, trade, missions, years of polish. Universe is a 0.1 preview with no life on its worlds yet. And the size is small because every pixel is worked out on the graphics card from equations, so it needs a Direct3D 12 card and does its work there; on a card with little memory it starts at a lower render scale by itself.

---

## Templates, not things

Every kind of world is a few lines of text, and **every world made from it is its own**:

```
@terrain lava
name: A lava world
forms: rocky
orbit-hz: 0..0.085
day-hours: 10..40
air-colour: 1 0.42 0.18
molten: yes
```

That makes a lava world wherever an orbit hugs its star, each with its own size, spin and colours. Write a new entry and **your** kind turns up across every universe. See [Build your own universe](docs/GUIDE.md "Step by step: worlds, stars, galaxies, physics, tours").

---

## How the zoom never ends

| | |
|---|---|
| **The moving anchor** | The camera measures itself from something close (a galaxy, a star, a world, a grain, an atom), so everything near you is exact |
| **The waveform** | Our own **CDF 9/7 wavelet** engine (the maths family behind JPEG 2000) stores shapes as waves; zooming in adds the next octave |
| **Mode-E** | Past the stored waves, detail keeps growing (Newton's method on *z³ − K*) and never repeats |
| **Real steps** | Close in, a grain's face steps in single **atomic layers**, then shows its **atoms**, before the atoms themselves arrive |
| **No floor** | Past the quark, which shows no size down to 10⁻¹⁹ m, the zoom in keeps going: past the Planck length, as far as a number can count. The scale bar says where you are and the card says what is known there, which is nothing. Outward you are held inside the visible universe, because that is all there is to see |

---

## Built from nothing

| | |
|---|---|
| **No engine** | Not Unity, not Unreal, not Godot. The engine is ours: the window, the input, the renderer, the camera, the generators, the physics, the tour, the cards, even the font |
| **No libraries** | No packages, no downloads, nothing to install to build it. It talks to the graphics card and to Windows directly, through declarations we wrote |
| **No art** | No pictures, no textures, no models, no sound files. Every galaxy, star, world, cloud, stone, grain and atom is worked out from its seed, every frame |
| **No build tools** | One command, and the C# compiler that is already inside every copy of Windows. That is the whole toolchain |
| **Our own formats** | The content format, the wavelet engine, the icon drawer, the installer, the document viewer, the PNG and GIF writers: ours |
| **What it borrows** | Windows itself: Direct3D 12 and its shader compiler for the card, Media Foundation to pack a clip as MP4, the framework's Deflate inside a PNG. Nothing from anywhere else |

About 18,000 lines of C# and shader code, and 10,000 lines of templates. That is the whole thing.

---

## Real physics, all four forces

| Force | What it does here |
|---|---|
| **Gravity** | Orbits follow Kepler's law, each body's speed from its orbit. A body's card gives its pull and escape speed where you are. Black holes have real horizons and bend the light behind them. Relativity turns each orbit's closest point round its star, and slows clocks in a gravity well |
| **Electromagnetism** | Electrons in their real shells and orbitals, bonds as shared electrons, stars the colour of their heat |
| **Strong force** | Packs every nucleus, and glues the quarks in every proton and neutron |
| **Weak force** | Decides which nuclei are stable, and how the rest would decay |

Every law's numbers are in `content/physics.unt`. **Change them, and you change the physics.**

---

## What's out there

| | |
|---|---|
| **Black holes** | A giant at every galaxy's heart, more among the stars. Feeding ones blaze with discs and jets; quiet ones are a shadow and bent starlight |
| **Worlds of every kind** | Temperate, ocean, desert, lava and ice worlds, gas giants, ice giants and hot Jupiters. Some have **rings**. Those that orbit close to their star are **tidally locked**: one face always toward it, a day as long as a year |
| **Moons** | Worlds in their own right: mostly airless rock or ice, now and then an ocean, a lava world or one with air |
| **Orbits** | Never perfect circles: every orbit an ellipse, most gently oval, some long and wild, each body fastest at its closest point |
| **Comets** | A dark nucleus a few km across in a glowing coma up to a million km wide, an ion tail straight from the star and a curved dust tail, tens of millions of km long: seen right across their system |
| **Asteroids** | Lumpy, cratered rock in a star's belts |
| **Wanderers** | Neutron stars and rogue planets drifting between the stars |

**Dead, but alive.** There's no life yet, but every world runs on its own clock: plates drift, volcanoes erupt, ice ages come and go, and rocks from space leave craters.

---

## Controls

| Key | Does |
|---|---|
| **Mouse wheel** | Fly toward what the mouse points at; you slow as you close in |
| **Ctrl + wheel** | Eight times faster |
| **W A S D, Q / E** | Fly like a first-person game; **Shift** for four times faster |
| **Left click** | Pick anything: a galaxy, star, world, spot, grain or atom |
| **Right click** | Let go |
| **Drag** | Circle what you picked, or look around |
| **Space · , · .** | Pause time · slower · faster (it starts at a minute a second) |
| **H** | The scale bar and info cards, on or off (picking something brings them back) |
| **B** | Among atoms: glide across the bond to the next one |
| **T · R** | The grand tour: start or carry on · start again |
| **F12 · F9** | A screenshot · start or stop a clip |

The **Time** menu has **Always day**, on to start with; turn it off for true day and night. **View** has drag speed, FPS with the GPU and CPU stats, render scale and GPU use. **Help** has every guide, and **Help > Documents** lists them all, what each is made of and when to read it.

---

## The grand tour

A film of the scale of things, from the cosmic web down to a quark and all the way back out.

| | |
|---|---|
| **Shot afresh every time** | Each shot picks its own way round, speed and crane |
| **A true crossing** | Billions of light-years, straight to one far galaxy, made from its own place before you set off |
| **A feeding black hole** | It crosses to a galaxy whose heart is feeding, flies down to it, round its accretion disc, edge-on through the light it bends, in to the shadow at its horizon, and back out: with its jets, if it fires them |
| **Pause any time** | A click, the wheel or a key; a note says why. **T** carries on, **R** starts again |
| **Film it** | Tick **Film the tour** on the landing page; the **Clips** list plays them back |
| **Write your own** | A tour is a plain-text list of camera moves (`content/tours/grand-tour.unt`) |

---

## Start

1. **Download** the installer from the links at the top, and run it. Over an earlier copy it offers to delete your old clips, screenshots and settings, each on its own.
2. **Pick a seed** on the landing page (or **Random**, or leave it blank).
3. **Begin** to fly, or **Take the tour**.

A bar shows the universe being made, then it opens, whole.

---

## Which guide

| | Who it is for | What is in it |
|---|---|---|
| **This page**, and the **Help** menu's pages in the program | Anyone, in plain words | What it is, how to fly it, how it works |
| [**Build your own universe**](docs/GUIDE.md "Templates, worlds, stars, galaxies, physics, tours, features") | Builders: no code needed | Add kinds of worlds, stars and galaxies, change the physics, write tours |
| [**Technical guide**](docs/HOW-TO-BUILD.md "Build, ship and change the code") | Advanced coders, or anyone with an AI coding assistant | Build it, ship it, find your way round the code (it is dense), change it |

**Help > Documents** in the program lists every guide, what each is made of and when to read it.

Plain **C# 5 on Direct3D 12**, built with the compiler that comes with Windows. No packages, no outside libraries, no stored art.

---

<p align="center">Made by the <b>Unity AI Lab</b> · four people, open-source, built on stubbornness<br>
<a href="https://unityailab.com" title="The Unity AI Lab">unityailab.com</a> &nbsp;·&nbsp; <a href="CREDITS.md" title="The team, and the science it is made from">Credits</a></p>
