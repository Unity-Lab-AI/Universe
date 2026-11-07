# Universe

> **One camera. One universe. A visible universe 13.4 billion light-years across, about 100 billion galaxies, down to the quarks inside the protons of the atoms of a grain of sand, without a single loading screen.**

**⬇️ [Download for Windows](https://git.unityailab.com/GFourteen/Universe/releases)** · **🛠️ [Build your own universe](docs/GUIDE.md)** · **⚙️ [Technical guide](docs/HOW-TO-BUILD.md)** · **👥 [Credits](CREDITS.md)**

---

## ✨ Two things in one

| | |
|---|---|
| 🚀 **A universe to fly through** | Install it, press **T**, and watch a five-minute film from the cosmic web down to a quark. Then take the controls and go anywhere. |
| 🧰 **A toolkit to build universes** | Every galaxy, star, world and grain is grown from **templates** in plain text files. Add a kind of world, a kind of star, a new galaxy, and use it for your own **game or simulation**. |

---

## 🔭 What is this?

Pull back far enough and galaxies are pinpoints of light, strung along the **cosmic web**: filaments and walls of galaxies knotted into clusters round great thin voids.

Roll the mouse wheel and one of those specks grows into a spinning island of stars. Keep going:

1. 🌀 You're **among its stars**, about a hundred billion of them.
2. ☀️ You're **circling one star**, watching its worlds swing round it.
3. 🌍 You **fall toward a world**: oceans, mountains, clouds.
4. 🪨 The ground becomes **stones**, then **grains**, then **crystal**.
5. ⚛️ The crystal becomes **atoms**: electrons in their real orbitals, a nucleus of protons and neutrons, and inside those, **quarks**.

You never "change level". It's one long dive, and you can turn round at any point and fly all the way back out.

---

## 🌱 Nothing is stored. Everything is grown.

No photos, no painted backdrops, no pre-made maps. Every galaxy, star, world and pebble is worked out on the spot from one starting number, the **seed**.

> **Same seed, same universe, every time.** Fly away from a star and come back, and it's the same star. It was never thrown away, just worked out again.

A galaxy holds about a hundred billion stars. Your computer could never keep them all, so it doesn't try: it makes the stars round you as you go, at the density stars have in a real galaxy.

---

## 🧬 Templates, not things

Each kind of thing is a **template** with ranges, and every one made from it gets its own values:

```
@terrain lava
name: A lava world
forms: rocky
orbit-hz: 0..0.085
day-hours: 10..40
air-colour: 1 0.42 0.18
molten: yes
```

That one entry makes a lava world wherever an orbit is close enough to its star, each with its own size, spin and colours. Write a new entry and your new kind appears across every universe you make. **[Build your own universe](docs/GUIDE.md)** shows you how, step by step.

---

## 🎥 How does the zoom go on, from the universe to the quarks?

It's one camera the whole way, about **42 factors of ten**, with nothing swapped in between. Four things make it work.

| | |
|---|---|
| ⚓ **The moving anchor** | Computers lose precision with huge numbers. So the camera always measures itself from something nearby (a galaxy's centre, a star, a world's ground, a grain, an atom), and everything near you is exact. |
| 📍 **Made where you are** | Galaxies are made in regions round you, stars in cubes round you, a world's ground in pieces finer the closer you are, and grains and atoms round the point you dive into. Nothing is stored, so there's nothing to run out of. |
| 🌊 **The waveform** | Our own **CDF 9/7 wavelet** engine (the maths family behind JPEG 2000) stores shapes as waves: big waves are big shapes, small waves fine detail. Zooming in adds the next octave of waves. |
| ♾️ **Mode-E** | Where the stored waves run out, mode-E carries on (Newton's method on *z³ − K*, with cells that split as you zoom), so detail keeps growing and never repeats. |

> **Where it stops today:** surface detail has no floor. The bodies stop at the quarks, about 10⁻¹⁹ km. What lies beyond, the fields particles are waves in, is still to come.

---

## 🕳️ Black holes, comets and the bodies between

| | |
|---|---|
| 🕳️ **Black holes** | Among a galaxy's stars, and a giant one at every galaxy's heart. A feeding one glows with its accretion disc, and some fire jets from their poles. A quiet one is only its shadow and the starlight it bends. |
| 🪨 **Asteroids** | Lumpy, cratered rock in a star's belts, lit from their star's side. |
| ☄️ **Comets** | A dark nucleus in a glowing coma, an ion tail straight away from the star and a curved dust tail. |
| 🧊 **The far cloud** | Icy bodies in a star's far Oort cloud. |
| 🌑 **Wanderers** | Neutron stars and rogue planets drifting between the stars. |

Click any of them for its card: its mass, its size, its pull and escape speed where you are.

---

## 🌋 A dead universe that's still alive

There's no life here yet, but the worlds aren't frozen. Their insides cool, plates drift, volcanoes erupt, the magnetic field flips, ice ages come and go, mountains wear down, and rocks from space leave craters. **Every world runs on its own clock.**

---

## 🚀 Start

The landing page opens first. Type a seed (or press **Random**, or leave it blank for a new universe), then **Begin**, or **Take the tour**. Your settings and the **Guide and wiki** are there too.

## 🖱️ How to fly

| Control | What it does |
|---|---|
| **Mouse wheel** | Fly toward what the mouse points at. You slow as you come close, so you never crash and never stop |
| **Ctrl + wheel** | Eight times faster |
| **W A S D, Q / E** | Fly like in a first-person game, at a speed that suits what is nearest. **Shift** for four times faster |
| **Left click** | Pick a galaxy, star, world, spot on the ground, grain or atom. The wheel flies you straight there |
| **Right click** | Let go of what you picked |
| **Drag** | With something picked, circle round it. With nothing picked, look around |
| **Space** / **,** / **.** | Pause time, slower, faster (real time up to ten years a second) |
| **H** | Show or hide the scale bar and info cards |
| **B** | Among atoms: glide across the bond to the next atom |
| **T** | The grand tour. A click, the wheel or a key pauses it; **T** carries on, **R** starts again |
| **F12** / **F9** | A screenshot (PNG) / start or stop a clip (MP4), in the `captures` folder |

The **Time** menu has **Always day**, the **View** menu has drag speed, FPS, render scale and GPU use, and **Help** has every document.

## 🎬 The grand tour

A five-minute film of the scale of things: the cosmic web, a galaxy, a black hole, star systems with an asteroid and a comet, a world and a glide over its land, the sky from the ground, its stones and grains, a crystal's lattice of atoms, an electron, the nucleus, a proton and its quarks, and all the way back out to the web.

- 🎞️ **Every viewing is shot afresh.** Each shot picks its own way round, speed and crane.
- ⏸️ **Pause any time.** A note says why it paused; **T** carries on, **R** starts again.
- 📹 **Film it.** Tick **Film the tour** on the landing page to save it as an MP4. The **Clips** list plays what you've filmed.
- ✍️ **Write your own.** A tour is a plain text list of camera moves (`content/tours/grand-tour.dnat`).

---

## 🛠️ Make it yours

| Start here | |
|---|---|
| 🧰 **[Build your own universe](docs/GUIDE.md)** | Add kinds of worlds, stars and galaxies, change the whole universe, write tours, add features |
| ⚙️ **[Technical guide](docs/HOW-TO-BUILD.md)** | Build it, make the installer, find your way round the code |

Plain **C# 5 on Direct3D 12**, built with the compiler that comes with Windows. No packages, no outside libraries, no stored art.

## 👥 Who made it

The **Unity AI Lab**: four people, open-source, built on stubbornness. [unityailab.com](https://unityailab.com) · [Credits](CREDITS.md)
