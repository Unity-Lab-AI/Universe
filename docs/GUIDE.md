<p align="center"><img src="img/logo.png" width="90" alt="Universe" title="Universe"></p>

<p align="center">
  <a href="../README.md" title="What Universe is, and how to fly it">Universe</a>
  &nbsp;·&nbsp;
  <a href="COMPARED.md" title="Size, scope and performance against the other universe programs">Against the field</a>
  &nbsp;·&nbsp;
  <a href="GUIDE.md" title="Add kinds of worlds, stars and galaxies, change the laws of physics, write tours">Build your own universe</a>
  &nbsp;·&nbsp;
  <a href="HOW-TO-BUILD.md" title="Build it, make the installer, find your way round the code">Technical guide</a>
  &nbsp;·&nbsp;
  <a href="LINUX.md" title="Universe on Linux, through Wine: one script">On Linux</a>
  &nbsp;·&nbsp;
  <a href="../CREDITS.md" title="Who made it, and the science it is made from">Credits</a>
</p>

# Build your own universe

> Universe is a **toolkit for making universes**: for games, for simulations, for science, for wondering. Everything in it is grown from **templates** you can read and change in plain text. Add a kind of world, a kind of star, a whole new galaxy shape, and every universe you make from then on is full of them. It is built to be taken: **MIT licensed**, for any project of any kind that needs a universe.

---

## The big idea: templates, not things

Nothing in Universe is stored as a finished object. There's no file holding "the planet Kepler-something". Instead there are **templates**, and the generators grow things from them using a **seed**.

| You write | The generator does |
|---|---|
| A **kind** (a lava world, a G-type star, a barred spiral) | Decides where it can appear and how often |
| **Ranges** for its traits (`day-hours: 10..40`) | Gives each one its own value from its seed |
| **Rules** (forms where the orbit is 0.75 to 1.25 of the liquid-water zone) | Places it only where it fits |

So one template makes millions of worlds, and no two are the same. **The same seed always grows the same universe**, so a world you found is still there when you come back.

> **Rule of thumb:** if you find yourself writing a name into the code (`if kind == "Lava planet"`), stop. Give the template a trait instead, and have the code read the trait. That keeps everything open to new kinds.

---

## The content format in five minutes

Every template lives in `content/`, in our own plain-text format (`.unt`). One file per subject.

```
@terrain lava
name: A lava world
forms: rocky
orbit-hz: 0..0.085
day-hours: 10..40
air-colour: 1 0.42 0.18
molten: yes
```

| Piece | Means |
|---|---|
| `@kind id` | Starts an entry: its kind, then a short unique id |
| `key: value` | One trait |
| `a..b` | A range: each thing made from the template gets its own value in it |
| `r g b .. r g b` | A colour range: each thing gets its own colour between the two |
| `# ...` | A comment |
| A blank line | Ends the entry |

The files are read at start-up. **Change a file, start the program, see the change.** No compiling needed for content.

---

## Add a new kind of world

Worlds are templates in `content/terrain.unt`. Each one says **where it forms** and **what it looks like**.

### 1. Copy an entry close to what you want

Say you want **carbon worlds**: dark, graphite-and-diamond planets close to their star.

```
@terrain carbon
name: A carbon world
forms: rocky
label: Carbon world
orbit-hz: 0.2..0.6
weight: 0.4
size-km: 6371
size-spread: 0.6..1.5
moons: 0..1
dot-colour: 0.35 0.33 0.32
interest: 4
day-hours: 12..50
makeup: rock: graphite, diamond and carbides; tar-dark plains; air: carbon monoxide, methane
air-colour: 0.7 0.6 0.5 .. 0.85 0.7 0.55
rock-colour: 0.12 0.12 0.13 .. 0.2 0.19 0.18
cloud-thickness: 0.3..0.6
land-share: 0.3..0.5
dry: yes
life: no
ground: 99 0.14 0.13 0.13 tinted
```

### 2. What each part does

#### Where it forms

| Trait | Means |
|---|---|
| `forms` | `rocky` (inside the frost line), `giant` (past it) or `hot-jupiter` (the closest orbit of a hot-Jupiter system) |
| `orbit-hz` | Where its orbit lies against the middle of the **liquid-water zone**: `1` is the middle, `0.5` half as far out, `2` twice as far. The zone moves out for brighter stars |
| `orbit-frost` | Where its orbit lies against the **frost line**, where ices can form |
| `star-brightness` | Only round stars this bright, in solar luminosities (a star of one solar mass is about `1`; red dwarfs are below `0.08`) |
| `weight` | When several kinds fit the same orbit, how often this one is chosen |
| `moon-weight` | How often a **moon** is of this kind. Moons are worlds too: any kind with a moon-weight can be one, by where its planet circles the star. `@terrain moon` and `@terrain icy-moon` are the usual airless rock and ice |
| `grains` | The ground its matter is made of, down to the atoms (a mix in `content/minerals.unt`) |

> The liquid-water zone is often called the *habitable* zone, but it's only habitable for life like ours. Universe uses it purely as a place: where water can be liquid.

#### What it is

| Trait | Means |
|---|---|
| `size-km`, `size-spread` | Its size: `size-km` times a value in the spread |
| `density-g-cc` | What a world of this kind weighs for its size (grams a cubic centimetre); its mass follows, and from the mass the pull at its ground, its escape speed and how much it slows clocks |
| `moons` | How many moons it gets |
| `day-hours` | One turn of its spin, its day |
| `label`, `makeup` | What its info card calls it and says it's made of (lines split by `;`) |
| `interest` | How much the grand tour's director wants to visit it |
| `dot-colour` | Its colour as a dot from far away |

#### How it looks up close

| Trait | Means |
|---|---|
| `air-colour`, `air-km` | The glow of its air at the edge, and how deep the air is |
| `cloud-colour`, `cloud-km`, `cloud-thickness`, `cloud-shift` | Its clouds |
| `rock-colour`, `ground` | Bare rock, and the land's colours by height |
| `land-share` | More land (higher) or more sea (lower) |
| `water`, `dry`, `frozen-sea`, `molten`, `dunes`, `snow-patches`, `polar-ice` | What its surface is like |
| `bands`, `band-dark`, `band-light`, `great-storm`, `storm-colour` | For giants: belts, zones and storms |
| `tides`, `volcanoes`, `volcano-count`, `seafloor-volcanoes`, `rivers`, `life` | Its behaviour |

> **Tidal locking is not a kind.** Any world whose orbit is close enough to its star turns once each orbit, one face always toward it: its day is its year, its day side scorched and its night side frozen. How close is a law of physics (`tidal-lock` in `content/physics.unt`), not a trait of the kind.

#### Rings

Any kind of world can have rings, giants and rocky worlds alike.

| Trait | Means |
|---|---|
| `rings-chance` | How often a world of the kind has rings (`1` always; every giant of ours has them. Rocky worlds now and then, from a moon torn apart by its planet's pull) |
| `rings` | How many specks make them |
| `rings-radii` | From and to, in the world's radii (`1.4..2.2`) |
| `rings-gap` | A gap in them, in radii |
| `rings-shade` | `banded` (bright and banded, like Saturn's) or a number (faint and even, like Uranus's) |
| `rings-colour` | Their colour |

Anything you leave out is taken from `@terrain default`.

### 3. Run it

Start Universe and fly to stars until you find one. Every star's worlds are chosen afresh by these rules, so your new kind turns up wherever it fits.

---

## Add a new kind of star

Stars are templates in `content/stars.unt`.

```
@star my-blue-giant
name: Blue giant
color: blue
temp-k: 20000..30000
mass-sol: 10..20
radius-sol: 6..12
share-of-stars: rare
```

| Trait | Means |
|---|---|
| `color` | `blue`, `blue-white`, `white`, `yellow-white`, `yellow`, `orange`, `red` or `magenta-red` |
| `temp-k` | Surface temperature in kelvin. With `radius-sol`, it sets how bright the star shines, and so where its liquid-water zone and frost line fall |
| `mass-sol`, `radius-sol` | Mass and size in solar masses and solar radii |
| `@layer` | A star's layers from its surface to its core (`from-r`, `to-r` as shares of its radius; `temp-k` and `density-g-cc` from the layer's top to its bottom; `makeup` where it differs from the star's; `in`: which kinds of star have it): fly into a star and each is a place, with its own plasma to dive into |
| `share-of-stars` | How common it is: `most common`, `common`, `uncommon`, `rare` or `rarest` |
| `remnant: yes` | A dead star (a white dwarf, a neutron star) |

Its worlds follow from its brightness: a hot bright star pushes the liquid-water zone and frost line far out, while a dim red star pulls them in close.

---

## Add a new kind of galaxy

Galaxies are templates in `content/galaxies.unt`.

```
@galaxy my-spiral
name: Loose-armed spiral
layout: spiral
shape: a faint disk with wide open arms
example: (yours)
```

| Trait | Means |
|---|---|
| `layout` | The shape it's grown in: `spiral`, `barred-spiral`, `elliptical`, `lenticular`, `irregular` or `ring` |
| `arms` | How many spiral arms (left out: 2 to 4 by chance) |
| `arm-spread` | How loose its arms are (`1` usual, about `2.2` for ragged, flocculent arms) |
| `star-forming-rate` | How much it still makes stars (a spiral like ours `1`). Sets its young clusters, glowing and dark nebulae and remnants |
| `structure-size` | How much it holds against ours (giants about `3`, dwarfs about `0.15`) |
| `globular-clusters` | Old clusters in its halo (ours about `160`, a giant elliptical about `12000`) |
| `bodies-on-arms` | Its young clusters and nebulae lie along its arms |
| `bodies-spread` | `disc` (a flat disc) or `ball` (a round swarm) |

Every number can be a range, and each galaxy of the kind gets its own size, tilt, turn, stars and values from its seed.

> A new *shape* (not just a new kind on an existing shape) is a code change in `src/gen/GalaxyGen.cs`. See *Add a feature in code* below.

---

## Lay out star systems

The kinds of star system are templates in `content/solar-systems.unt`. The usual one, `usual`, is the base, and other kinds give only what differs.

```
@system compact
name: Compact system
planets: 6..8
first-orbit-au: 0.012
first-orbit-spread: 0.8..1.2
scales-with-star: no
spacing: 1.25..1.4
```

| Trait | Means |
|---|---|
| `planets` | How many worlds |
| `first-orbit-au`, `first-orbit-spread` | The first world's orbit in AU, times a value in the spread |
| `scales-with-star` | `yes`: orbits move out with the square root of the star's brightness |
| `spacing` | Each orbit over the one before (in our own system, about 1.4 to 2) |
| `share-in-universe` | How often a system in the universe is of this kind |
| `tilt-spread` | How far its worlds' orbits lie out of the system's plane, in degrees. Born in one flat disc they stay within a few (ours: 1.5); a **stirred** system, pulled at by a passing star or a second star, scatters them tens of degrees apart |
| `wild-tilt-share`, `wild-tilt` | The share of its worlds far out of line, and how far |

Which kind of world forms in each orbit then follows from the world templates above.

---

## The laws of nature

Universe runs on the real laws of physics, and their numbers are templates too. They live in `content/physics.unt`, so you can read every one, and change them to build a universe with different physics.

### The four forces

| Force | What it does in Universe | Its numbers |
|---|---|---|
| **Gravity** | A **live force** in the open star system (`src/gen/SystemGravity.cs`): the star and every planet and moon pull on each other (Newton), stepped with a symplectic leapfrog, started from their **Kepler** orbits (each planet's pull from its own moons' orbits); far systems and the small bodies keep Kepler's law, each body's speed from its orbit (vis-viva). Disc galaxies turn at their rim speed (the flat rotation curve), and space stretches by the scale factor of a universe of matter and dark energy (`@law cosmos`). Every info card for a massive body gives its mass, pull and escape speed |
| **Electromagnetism** | Each atom's **electrons** fill their shells and orbitals in their real order (1s 2s 2p 3s ...). **Bonds** are shared electrons. A star's **colour** comes from its temperature the way a glowing body's does (Planck's law). Inside the nucleus, the **protons push each other apart** | `@law coulomb`: `k-mev-fm` (the push between two protons), `@law light`: `speed-m-s` |
| **Strong nuclear force** | **Packs the nucleus**: protons and neutrons pull together just beyond a hard core where they push apart, and feel nothing past a few femtometres. It glues the **quarks** in every proton and neutron. A nucleus's **binding energy** comes from the strong pull against the electric push | `@law nuclear-force`: `core-fm`, `spacing-fm`, `range-fm`; `@law mass-formula`: the five terms of the Bethe–Weizsäcker formula |
| **Weak nuclear force** | Decides **which nuclei are stable** and how the rest decay: too many neutrons and one turns into a proton (beta-minus), too many protons and one turns into a neutron (beta-plus). Past lead, the biggest nuclei throw out alpha particles. Every nucleus's card says which | `@law beta-stability`: `c` (the valley of stability), `alpha-above-z` |

### Matter, air and stars

| Law | What it sets |
|---|---|
| `@law orbits` | Orbits from speeds, as the laws of motion have it: each body moves at some share of the circular speed where it was placed (`planet-speed-spread`, `wild-share` and `wild-speed` for the share flung into long loops, `moon-close-speed` and `moon-far-speed`), and its ellipse, its period and its speed everywhere follow. Near escape speed a body flies through its closest point ultra-fast and crawls through its farthest |
| `@law tidal-lock` | How close to a star a world turns once each orbit, one face always toward it (`lock-au-sol`: half an AU for a star of one solar mass, growing with the cube root of the star's mass) |
| `@law air-density` | How many molecules are in each cubic centimetre of air at about one bar, so you meet the right number going into an air |
| `@law frame-dragging` | Every black hole's spin (`spin-range`, a share of the most a hole can have): its horizon pulled in, its ergosphere, how fast space is swept round at its horizon, how far in its disc reaches, and the twist of the fall's funnel |
| `@law hole-inside` | The way in from a black hole's horizon to its singularity, by its mass (`way-in-ly` for a hole of `way-in-mass-sol`, scaling with the mass to `way-in-power`); the fall's card counts the way in against it |
| `@law gravity-off` | What the Time menu's Gravity switch does: every body straight on at its speed, every star blown apart at about its sound speed (`swell-over-freefall` times its free-fall time to double), a galaxy's stars flung off at the rim speed |
| `@gas ...` | The atoms in each molecule of each gas, so an air's make-up gives the atoms you find in it |
| `@law star-plasma` | What a star's surface is made of, and how dense it is |
| `@force ...`, `@particle ...`, `@atom ...` | What each force, particle and part of an atom is and does, for the cards |

### Change the physics

```
@law nuclear-force
name: The strong force between nucleons
core-fm: 1.7
spacing-fm: 1.8
range-fm: 2.6
```

Make the strong force reach further and nuclei pack differently. Change gravity's pull and every escape speed, pull and black-hole horizon follows. These are the same numbers physics textbooks use; changing them is how you build a universe where the laws are different.

> **Still to come:** gravity as a live force beyond the open system (the small bodies, other systems), and the fields particles are waves in.

---

## Change the whole universe

`content/universe.unt` holds the universe itself:

| Trait | Means |
|---|---|
| `across-ly` | How far you see, in light-years: your horizon, centred on you wherever you go (the universe has no edge) |
| `galaxies` | How many galaxies it holds |
| `web-ly`, `web-wall-ly`, `web-contrast` | The cosmic web: the size of its cells, its walls, and how much galaxies crowd onto it |
| `region-ly`, `near-ly` | How finely galaxies are made round you, and how far out they're made in full |
| `glow-points` | How many points stand for the far galaxies |
| `real-stars-cube-ly`, `real-stars-reach-ly` | Inside a galaxy, the cubes its real stars are made in round you, and how far round you they are made |

A smaller universe with fewer galaxies? A web with sharper walls? Change the numbers.

---

## Minerals, elements and matter

Below the ground, everything is real matter at its real size.

| File | What it holds |
|---|---|
| `content/elements.unt` | All 118 elements: their atoms, shells and most common isotopes, and where each was forged (`made-in`: the big bang, cosmic rays, dying low-mass stars, exploding massive stars, exploding white dwarfs, merging neutron stars, uranium's and thorium's decay, the lab); for the unstable ones `half-life` and `decays` (alpha, beta-minus, electron-capture, fission), so each decays before your eyes at its own moment |
| `content/minerals.unt` | Minerals (their crystal from its published unit cell, and their **trace elements**: `trace <host> <element> <share>`, each element standing in for the host atom on that share of its sites, so nickel sits in olivine, strontium in feldspar, gold in pyrite, and the sea's ions among its water) and the **mix** each kind of ground is made of |
| `content/physics.unt` | The laws of nature, gases and particles (see *The laws of nature* above) |

Add a mineral, then put it in a ground's mix, and its grains appear on every world of that kind.

---

### What the small bodies and stars are made of

An asteroid's kind lives in `content/small-bodies.unt` (`@asteroid` with `mix`, `makeup`, `density`, `albedo` and `belt-share`; its mix's `vary` in `content/minerals.unt` gives each body its own shares of the kind's minerals, scattered by its seed, its share at the belt's inner edge .. outer edge; the belt draws each asteroid's kind by these at its place), a Kuiper body's and a comet's in the same file, a rogue planet's in `content/planets.unt`. Each `mix` is a ground in `content/minerals.unt` (`@mix`), and the card works the elements and traces out from the minerals' own atoms and `trace` lines. A star's `makeup` (`content/stars.unt`, by count of atoms) is what its card gives and what its plasma is made of when you dive into it; `makeup-note` is what the card says of it.

Where each kind of small body lives and how many are made is `content/bodies.unt` (`@place`). Its `size-km` as a range `lo..hi` gives each body its own size, drawn by `size-power` (higher: more of them small), and `round-above-km` is the size gravity has pulled a body round at; below it a body is lumpy and stretched, the smallest the most. Every small body is a world like any: within forty of its radii its ground is made as a planet's is, with its own relief, minerals and grains (`@terrain asteroid` in `content/terrain.unt`, its `relief-of-radius`), and it is landed on and zoomed into the same way.

### Supernovae

`content/physics.unt` `@law supernova` holds the rate (per century, for a galaxy of a hundred billion stars), the free-expansion years, the remnant years and the neutron-star mass limit; each `@supernova` kind has its `share`, `peak-sol`, `rise-days`, `plateau-days`, `halving-days`, `tail-share`, `peak-k`, `ejecta-km-s`, `colour` and what it `leaves` (`core`, `nothing` or `magnetar`). Every galaxy's supernovae follow from these, its seed and the game's clock.

## Write a tour

Tours are lists of camera moves in `content/tours/`. The grand tour is `grand-tour.unt`. Copy it and make your own:

```
@stop pick-a-galaxy
do: pick
target: far-galaxy
far-ly: 1e9..5e9
caption: A far galaxy

@stop fly-there
do: shot
to-radii: 8
sweep: 20
rise: 10
max-rate: 0.45
seconds: 8
caption: Across the universe, straight to one far away
```

| `do:` | What the camera does |
|---|---|
| `pick` | Chooses what to look at: `start-galaxy`; `far-galaxy` (with `far-ly: 1e9..5e9`, a true crossing: a galaxy that far ahead, made from its own region before you set off, one whose heart is feeding when there is one); `black-hole` (a feeding one before a quiet one); `star`; `planet` (never a world that is all sea); `land` (a spot of dry land under you, to come down on); `asteroid`, `comet`, `giant` (a gas or ice giant of the system), `neutron` (the nearest neutron star), `nebula` (the nearest nebula of the galaxy), `supernova`, `grain`, `spot`, `atom`, `bonded-atom`, `electron`, `nucleus`, `proton`, `quark` and more, or `none`. A pick looks for a few seconds; if it finds nothing (a system with no belt, say) every stop up to the next pick is skipped, so a caption never tells of what is not in view |
| `shot` | A framed film shot round what is picked (a small body is framed from its lit side: the sweep carries the camera round to it; at a black hole the frame is the disc's own plane): `to-radii` or `to-km` (how far), `sweep` (degrees round), `rise` (degrees up), `max-rate` (how fast the scale may change, tenfolds a second: on the way in to the subject, and on the way out from whatever was in view where the shot began, so an asteroid you were beside pulls away smoothly on the way to a comet, never left in a frame) |
| `zoom` | A straight push in or pull out, `until-km`; `through: yes` flies on through without slowing into the next stop; `pan` turns slowly round what it flies to as it goes; `out: yes` for a pull out that ends once back out of the matter (a body's ground, a black hole) into space |
| `orbit`, `look`, `arc`, `fly`, `glide` | Circle, look round, dolly while circling, fly forward, skim over the land |

Every stop has a `caption`, which is shown on screen. A caption of `{sky}` is written on the spot from what is really up from where you stand: its star only if it is in view, its moons, the other worlds, the galaxy's stars by night or the day sky by day. A caption of `{hole}` is written from the black hole you picked: its jets if it fires them, its disc, or that it is quiet, and how big its horizon is. `{horizon}` is written from where you stand on a world, `{supernova}` from the supernova picked: its kind, its stage, its light, what it leaves.

---

## Add a feature in code

When a template trait isn't enough, the code is plain **C# 5 on Direct3D 12**, built with the compiler that comes with Windows. It is dense (read it by its comments: a fifth of its lines are plain English); unless you are an advanced programmer, we suggest changing it with an AI coding assistant rather than by hand. See **Build your own (technical)** for the full map and how to read it. The short version:

| You want | Go to |
|---|---|
| A new trait for worlds | Read it in `PlanetTerrain.Build` (`src/gen/PlanetTerrain.cs`), use it where the ground is shaped or coloured |
| A new rule for where worlds form | `SpaceGen.WorldKind` (`src/gen/SpaceGen.cs`) |
| A new kind of thing in space | `src/gen/BodyGen.cs` and `content/bodies.unt` |
| A black hole's disc, swarm and torn stars | `content/bodies.unt`: `eating-stars` (how many stars a feeding hole tears apart at once), `disc-rs` (its disc's reach in horizon radii), `inner-orbit-seconds` (how long its inner edge takes round, as watched), `swarm-stars` and `swarm-rs` (stars on tight ellipses round it), `eating-share` (how many feeding holes are tearing a star apart now). Every motion is worked out on the card from these |
| Dark matter and dark energy | `content/physics.unt` `@law cosmos`: `dark-matter-share` and `rim-speed-km-s` (a galaxy's card: its mass with its halo, its stars' flat speed), `dark-energy-share` and `hubble-km-s-mpc` (the horizon's words) |
| A new galaxy shape | `src/gen/GalaxyGen.cs` |
| New controls or camera moves | `UniverseView.Update` (`src/shell/UniverseView.cs`) and the tour's `TourStep` (`src/shell/UniverseView.Tour.cs`) |
| What the info cards say | `UniverseView.Card` (`src/shell/UniverseView.Card.cs`) |
| What a body is made of inside | its `@layer` entries (`content/planets.unt`, `content/small-bodies.unt`, `content/stars.unt`, `content/gas-giants.unt`), read by `src/gen/BodyLayers.cs` |
| Every atom at every size (its electron density, its ion, the light of the matter) | `MatterView.Density` (`src/shell/MatterView.Air.cs`), the card's code 7 (`src/render/Pipeline.cs`), `src/shell/MatterView.Photons.cs` |
| Down through a world's layers (Page Down, Page Up) | `src/shell/UniverseView.Depth.cs` |
| A black hole's swarm, disc, jets and torn star, clickable | `src/shell/UniverseView.Swarm.cs` |
| How bright each planet, moon and star is from where you are | their apparent magnitude in `UniverseView.Draw` (`src/shell/UniverseView.Draw.cs`) |
| Gravity as a live force, and gravity off | `src/gen/SystemGravity.cs`, `src/shell/UniverseView.Gravity.cs` |
| Novae, kilonovae and supernovae | `content/physics.unt` `@supernova` (a kind with `rate-a-year` has its own rate, `remnant-mass-sol` what it leaves, `leaves: white-dwarf` for a nova), `src/shell/UniverseView.Supernovae.cs` |
| Where you are, from the universe down | the one tree: `src/core/Thing.cs`, `UniverseView.Here` (`src/shell/UniverseView.Tree.cs`), `MatterView.Below` (`src/shell/MatterView.Tree.cs`) |

**The rules that keep it a toolkit:**

1. **Templates, not names.** Read traits from the content file; never test for a kind's name in code.
2. **Ranges, not single values.** Let every world, star and galaxy be its own.
3. **Seeds, not storage.** Make things from their seed and place, so they never need saving and are always the same.
4. **The graphics card first.** Heavy work goes in shaders, not on the processor.

---

## Ship it

`release.cmd` builds the program and its installer. Change the name, the icon and the content, and you've made your own universe for your own game. **Build your own (technical)** has the details. The licence is MIT: it is yours to take.
