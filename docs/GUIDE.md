# Build your own universe

> Universe is a **toolkit for making universes**: for games, for simulations, for science, for wondering. Everything in it is grown from **templates** you can read and change in plain text. Add a kind of world, a kind of star, a whole new galaxy shape, and every universe you make from then on is full of them.

---

## 🧠 The big idea: templates, not things

Nothing in Universe is stored as a finished object. There's no file holding "the planet Kepler-something". Instead there are **templates**, and the generators grow things from them using a **seed**.

| You write | The generator does |
|---|---|
| A **kind** (a lava world, a G-type star, a barred spiral) | Decides where it can appear and how often |
| **Ranges** for its traits (`day-hours: 10..40`) | Gives each one its own value from its seed |
| **Rules** (forms where the orbit is 0.75 to 1.25 of the liquid-water zone) | Places it only where it fits |

So one template makes millions of worlds, and no two are the same. **The same seed always grows the same universe**, so a world you found is still there when you come back.

> **Rule of thumb:** if you find yourself writing a name into the code (`if kind == "Lava planet"`), stop. Give the template a trait instead, and have the code read the trait. That keeps everything open to new kinds.

---

## ✍️ The content format in five minutes

Every template lives in `content/`, in our own plain-text format (`.dnat`). One file per subject.

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

## 🌍 Add a new kind of world

Worlds are templates in `content/terrain.dnat`. Each one says **where it forms** and **what it looks like**.

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
| `star-brightness` | Only round stars this bright (the Sun is `1`; red dwarfs are below `0.08`) |
| `weight` | When several kinds fit the same orbit, how often this one is chosen |

> The liquid-water zone is often called the *habitable* zone, but it's only habitable for life like ours. Universe uses it purely as a place: where water can be liquid.

#### What it is

| Trait | Means |
|---|---|
| `size-km`, `size-spread` | Its size: `size-km` times a value in the spread |
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
| `tidally-locked`, `tides`, `volcanoes`, `life` | Its behaviour |

Anything you leave out is taken from `@terrain default`.

### 3. Run it

Start Universe and fly to stars until you find one. Every star's worlds are chosen afresh by these rules, so your new kind turns up wherever it fits.

---

## ⭐ Add a new kind of star

Stars are templates in `content/stars.dnat`.

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
| `mass-sol`, `radius-sol` | Mass and size against the Sun's |
| `share-of-stars` | How common it is: `most common`, `common`, `uncommon`, `rare` or `rarest` |
| `remnant: yes` | A dead star (a white dwarf, a neutron star) |

Its worlds follow from its brightness: a hot bright star pushes the liquid-water zone and frost line far out, while a dim red star pulls them in close.

---

## 🌀 Add a new kind of galaxy

Galaxies are templates in `content/galaxies.dnat`.

```
@galaxy my-spiral
name: Loose-armed spiral
layout: spiral
shape: a faint disk with wide open arms
example: (yours)
```

`layout` picks the shape the galaxy is grown in: `spiral`, `barred-spiral`, `elliptical`, `lenticular`, `irregular` or `ring`. Each galaxy of the kind gets its own size, tilt, turn and stars from its seed.

> A new *shape* (not just a new kind on an existing shape) is a code change in `src/gen/GalaxyGen.cs`. See *Add a feature in code* below.

---

## 🔭 Change the whole universe

`content/universe.dnat` holds the universe itself:

| Trait | Means |
|---|---|
| `across-ly` | How wide the visible universe is, in light-years |
| `galaxies` | How many galaxies it holds |
| `web-ly`, `web-wall-ly`, `web-contrast` | The cosmic web: the size of its cells, its walls, and how much galaxies crowd onto it |
| `region-ly`, `near-ly` | How finely galaxies are made round you, and how far out they're made in full |
| `glow-points` | How many points stand for the far galaxies |

A smaller universe with fewer galaxies? A web with sharper walls? Change the numbers.

---

## ⚗️ Minerals, elements and matter

Below the ground, everything is real matter at its real size.

| File | What it holds |
|---|---|
| `content/elements.dnat` | All 118 elements: their atoms, shells and most common isotopes |
| `content/minerals.dnat` | Minerals (their crystal from its published unit cell) and the **mix** each kind of ground is made of |
| `content/physics.dnat` | The laws and gases the matter and air follow |

Add a mineral, then put it in a ground's mix, and its grains appear on every world of that kind.

---

## 🎬 Write a tour

Tours are lists of camera moves in `content/tours/`. The grand tour is `grand-tour.dnat`. Copy it and make your own:

```
@stop pick-a-galaxy
do: pick
target: far-galaxy
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
| `pick` | Chooses what to look at: `start-galaxy`, `far-galaxy`, `black-hole`, `star`, `planet`, `asteroid`, `comet`, `quark` and more, or `none` |
| `shot` | A framed film shot round what is picked: `to-radii` or `to-km` (how far), `sweep` (degrees round), `rise` (degrees up), `max-rate` (how fast the scale may change) |
| `zoom` | A straight push in or pull out, `until-km` |
| `orbit`, `look`, `arc`, `fly`, `glide` | Circle, look round, dolly while circling, fly forward, skim over the land |

Every stop has a `caption`, which is shown on screen.

---

## 🛠️ Add a feature in code

When a template trait isn't enough, the code is plain **C# 5 on Direct3D 12**, built with the compiler that comes with Windows. See **Build your own (technical)** for the full map. The short version:

| You want | Go to |
|---|---|
| A new trait for worlds | Read it in `PlanetTerrain.Build` (`src/gen/PlanetTerrain.cs`), use it where the ground is shaped or coloured |
| A new rule for where worlds form | `SpaceGen.WorldKind` (`src/gen/SpaceGen.cs`) |
| A new kind of thing in space | `src/gen/BodyGen.cs` and `content/bodies.dnat` |
| A new galaxy shape | `src/gen/GalaxyGen.cs` |
| New controls or camera moves | `UniverseView.Update` and the tour's `TourStep` (`src/shell/UniverseView.cs`) |
| What the info cards say | `UniverseView.Card` |

**The rules that keep it a toolkit:**

1. **Templates, not names.** Read traits from the content file; never test for a kind's name in code.
2. **Ranges, not single values.** Let every world, star and galaxy be its own.
3. **Seeds, not storage.** Make things from their seed and place, so they never need saving and are always the same.
4. **The graphics card first.** Heavy work goes in shaders, not on the processor.

---

## 📦 Ship it

`release.cmd` builds the program and its installer. Change the name, the icon and the content, and you've made your own universe for your own game. **Build your own (technical)** has the details.
