---
title: U7 · Worlds — the ground, the seas, the air, the living planet without life
status: verified
sources: [src/gen/PlanetTerrain.cs, src/sim/PlanetCore.cs, PlanetPlates.cs, PlanetSurface.cs, PlanetAir.cs, PlanetWeather.cs, PlanetImpacts.cs, content/terrain.unt, gas-giants.unt, moons.unt, geology.unt, climate.unt]
last-verified: 1591b7f
---

# U7 · Worlds

> "a planet is a living breathing thing even without life, did you do all the magnma cores mangnetic fielsds atetonic plates volcanos earthqueakes all of that shit in totaltiy" · "need to fix water worlds and water zommming in to go to bed rock" · "and we should default day only cycles on"

## Built

| | How it works | Where |
|---|---|---|
| Every kind of world | Temperate, ocean, desert, lava, snowball, tidally locked, gas giants, ice giants, hot Jupiters, moons and icy moons, from `@terrain` entries: where each forms, its size, moons, spin, colours, air, clouds, rock, water, behaviours, make-up; anything left out from `@terrain default` | `terrain.unt`, `PlanetTerrain.Instance`, `SpaceGen.WorldKind` |
| The ground | A cube blown into a ball, each face split finer under the camera (to a grain's size, never finer than a few grains), built in the background on every core nearest first, never waited for; the camera lands on finished ground and never sees through it; the sand per pixel on the card within a few thousand grains of the ground | `PlanetTerrain` (`Collect`, `Visit`, `StartBuilding`), `MeshPipeline` |
| The seas | See-through water with glints; dived into within a metre of the surface: the water's own colour, light dying with depth, the bed drawn through it, the bed's own grains with water molecules above | `UniverseView.Surface`, `PlanetTerrain.Dim`, `GrainField.For("ocean")` |
| The air and clouds | The air's glow at the edge in the kind's colour, turning clouds worked out per pixel, weather near the ground; inside a giant, its layers down to its core, each layer's own matter | `PlanetTerrain.DrawAir`, `gas-giants.unt in:` |
| Rings and moons | Rings on any kind by `rings-chance`, banded or faint; moons as worlds in their own right from the same templates (`moon-weight`) | `UniverseView.DrawRings`, `SpaceGen` |
| Day and night | Every world turns on its own day (`day-hours`); the star's light through its air by day, half brightness with a cool tint by night; Time > Always day on to start with | `UniverseView.GroundLight`, `SpunSince` |
| Tidal locking | A world close enough to its star turns once each orbit, one face always lit (`@law tidal-lock`) | `Physics` |
| The living planet | On its own clock: the core's heat running down, a magma ocean when young, plates (8 to 15) drifting at real rates with ridges and mountains at their edges, quakes with real magnitude counts, eruptions and super-eruptions, the geodynamo and its flips, the air (pressure, carbon dioxide, oxygen, methane, the carbon-silicate cycle, runaway and snowball), rain belts, hurricanes, currents, tides, ice ages from the orbit's wobbles, erosion and soil, impacts at real rates with the heavy bombardment, the star brightening with age; the visible ground follows as a whole-planet swap at big moments, the changes kept as CDF 9/7 terms | `src/sim/Planet*.cs`, `PlanetTerrain.BakeChanges`, `geology.unt`, `climate.unt` |

No life: the stages of life, the species and the microbes are DNA-AND's and never run here.

See also: [U5 Generators](u05-generators.md) · [U8 Matter](u08-matter.md) · [Build your own universe](../../docs/GUIDE.md)
