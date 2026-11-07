---
title: U5 · Generators — every kind a template
status: verified
sources: [src/gen/GalaxyGen.cs, StarGen.cs, SpaceGen.cs, PlanetGen.cs, PlanetTerrain.cs, BodyGen.cs, GrainField.cs, ElementGen.cs, IconFont.cs, content/*.unt]
last-verified: 1591b7f
---

# U5 · Generators

> "thats a problem they were all suppose to be dynamic and procedurally genreated templet like things not hard code" · "yeah day length is the spin of the planet"

Nothing is stored and nothing is named in code. Every kind is an entry in `content/` with ranges, and a thing made from it takes its own value in each range from its seed and its place, so it never needs saving and is always the same when you return.

## Built

| Generator | From | Makes |
|---|---|---|
| Galaxies (`GalaxyGen`) | `galaxies.unt` (layout, arms, arm-spread, star-forming-rate, structure-size, globular-clusters, bodies-on-arms, bodies-spread) | Every galaxy's shape, tilt, turn, stars and colour; the cosmic web's regions; dead stars kept out of young arms |
| Stars (`StarGen`) | `stars.unt` (temp-k, mass-sol, radius-sol, share-of-stars, surface-kind, spots, remnant, makeup, makeup-note) | 26 shining kinds, colour from temperature by Planck's law, brightness from size and temperature, the surface's plasma and corona |
| Systems (`SpaceGen`) | `solar-systems.unt` (planets, first-orbit-au, spacing, scales-with-star, tilt-spread, wild-tilt) and `terrain.unt` (forms, orbit-hz, orbit-frost, star-brightness, weight, moon-weight) | Which kind of world forms where, the orbits as ellipses from speeds, moons as worlds, rings by rings-chance, belts, the Kuiper belt, the Oort cloud |
| Worlds (`PlanetTerrain`, `PlanetGen`) | `terrain.unt`, `gas-giants.unt`, `moons.unt`, `physics.unt` | A world's ground, seas, air, clouds, rock, temperature and behaviours from its kind; the giants' layers; the living planet's processes |
| Bodies in space (`UniverseView`, `BodyGen`) | `bodies.unt` (@place: where, share, feeding shares, disc-rs, swarm, eating-stars), `small-bodies.unt`, `black-holes.unt`, `nebulas.unt`, `planets.unt` | Black holes at every galaxy's heart and among the stars, neutron stars, rogue planets, asteroids by class, Kuiper bodies, comets, clusters and nebulae |
| Grains (`GrainField`) | `minerals.unt` (@mix, @mineral, @surface) | One field of loose rock for a whole world: each grain's size, depth, mineral and shape from its cell |
| Elements (`ElementGen`) | `elements.unt` (118, with colour, covalent-a, atomic-a, mass-number) | Each element as it really is at any temperature and pressure; the periodic table |
| The font (`IconFont`) | its own tables | The blocky letters of the scale, the cards and the captions |

## The rules that keep it a toolkit

1. Templates, not names. 2. Ranges, not single values. 3. Seeds, not storage. 4. The graphics card first. (`docs/GUIDE.md`)

See also: [Build your own universe](../../docs/GUIDE.md) · [U6 Universe](u06-universe.md) · [U7 Worlds](u07-worlds.md)
