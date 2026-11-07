---
title: U3 · Engine core
status: verified
sources: [src/core/MathCore.cs, Wavelet97.cs, Waveform.cs, ModeE.cs, Physics.cs, Content.cs, Jobs.cs, content/physics.unt]
last-verified: 1591b7f
---

# U3 · Engine core

> "this is real fucking univaresal laws we shall be using electomagnatism strong and weak nuclear forces all of it"

## Built

| Module | File | What it does |
|---|---|---|
| Maths | `MathCore.cs` | Vectors, matrices, our own seeded random numbers (SplitMix64 and xoshiro256**), value noise |
| The waveform | `Wavelet97.cs`, `Waveform.cs` | The lab's CDF 9/7 wavelet engine (from Uni Vs Matics): a field becomes sparse wavelet terms kept to an energy target, packed, rebuilt exactly or at any level of detail; stored octaves give every surface its detail |
| Mode-E | `ModeE.cs` | Endless self-similar detail past the finest stored level: Newton on z³ − K, a 3D cubic continuation, cellular octaves by tenfold steps; identical to the lab's fractalize.ts |
| Physics | `Physics.cs` | Reads `content/physics.unt`: Kepler orbits and the vis-viva speed, pulls and escape speeds, the Schwarzschild horizon, the turning perihelion and slowed clocks, tidal locking, air density, the supernova law; the matter reads the strong force, the protons' push, the mass formula and beta stability |
| Content | `Content.cs` | Reads every `.unt` at start-up: `@kind id`, `key: value`, ranges, lists, colour ranges; a comment inside an entry never ends it, a blank line does |
| Jobs | `Jobs.cs` | One worker per core on a shared queue; the ground is built on them |
| Precision | `UniverseView` | The camera as an anchor (a galaxy's centre, a star, a world, a grain, an atom) plus an offset in double precision, so it is exact from light-years to quarks and below |

## The laws, as content

`content/physics.unt`: `@law gravity`, `coulomb`, `light`, `nuclear-force`, `mass-formula`, `beta-stability`, `orbits`, `tidal-lock`, `air-density`, `cosmos` (dark matter and dark energy), `supernova`; the `@supernova` kinds; `@gas`, `@force`, `@particle`, `@atom` for the cards. Change a number and the physics changes (`docs/GUIDE.md`).

## Still to come (U12)

Gravity as a live force shaping orbits moment by moment, and the gravity switch; the rest of relativity; decay before your eyes; the fields.

See also: [U6 Universe](u06-universe.md) · [U8 Matter](u08-matter.md) · [Build your own universe](../../docs/GUIDE.md)
