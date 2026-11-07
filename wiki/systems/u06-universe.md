---
title: U6 · The universe — the web, galaxies, stars, systems, every body, black holes, supernovae
status: verified
sources: [src/shell/UniverseView.cs, src/render/Pipeline.cs, src/gen/SpaceGen.cs, GalaxyGen.cs, content/universe.unt, bodies.unt, physics.unt, DNA-AND docs/DESIGN.md 282–289, 307, 479, 482, 483, 501, 516–521, 546–568]
last-verified: 1591b7f
---

# U6 · The universe

> "okay lets properly add 100 billion galaxies ... and lets do a properl 13.4 biillion light year across vissible universe" · "its not really a end all of the universe its the distance light can no longer reach" · "black holes tho will be a special case to fall into event horzon in pitch black till u get to the singularity" · "and what about super novas and nurton star creation and black hole creations"

## Built

| | How it works | Where |
|---|---|---|
| The cosmic web | About 100 billion galaxies within a horizon 13.4 billion light-years across, centred on you and moving with you (no edge), the far light reddened; galaxies made by regions round you along walls, filaments and voids, the rest a glow sifted per point on the card, each point a galaxy you can click; dark matter and dark energy from `@law cosmos` on the cards and in the horizon's words | `UniverseView` (`SpawnRegion`, `FarGalaxy`), `content/universe.unt` |
| A galaxy | One continuous star field; its real stars made round you in cubes at true density (`real-stars-cube-ly`, `real-stars-reach-ly`), placed exact to a fraction of an AU; its clusters and nebulae by its star-forming rate; galaxies and stars told apart from a galaxy's edge | `UniverseView.LocalStars`, `ClusterStars`, `SpaceGen.Structures` |
| A star's system | Every star's worlds, moons, belts, Kuiper belt and Oort cloud from the templates; orbits ellipses from speeds, stirred systems out of the plane; the camera rides along with the body it is at; the star's real body and corona | `SpaceGen`, `UniverseView.PlaceSystem`, `FollowBody` |
| Every body, its make-up | Supermassive black holes at every galaxy's heart, stellar and intermediate holes, neutron stars and rogue planets among the stars, stars and rogue planets between galaxies, asteroids by class, Kuiper bodies, comets with coma and tails seen across the system; each clickable with its card, minerals, elements and traces, dived into at its surface | `bodies.unt`, `small-bodies.unt`, `UniverseView.Extra`, `MatterView.SetGround` |
| Black holes | Feeding (the disc from 3 to about 60 horizon radii on Kepler orbits, hot inside, beamed on the side coming at you, jets at some, a swarm of stars on tight ellipses, stars torn into a stream and eaten) or quiet (a shadow and bent starlight); the shadow black at the size the law gives from where you stand; the approach down to the horizon with the disc all round; the fall through it as travel on the law's own clock down the axis of entry, the gravity well round you as a grid from the metric (Flamm's surface outside the throat, a long funnel to the point inside), looking back at the horizon, the escape speed, time since and time left on the card, to the singularity at the quarks' size | `Pipeline.cs` (the hole frame), `UniverseView.DrawShadows`, `DrawFallGrid`, `MatterView` kind 12 |
| Masses and gravity | Every world and moon has its mass from its size and its kind's density (`terrain.unt` density-g-cc); its card gives it, the pull at its ground and its escape speed. The Time menu's Gravity switch: every body straight on at its speed, the star swelling at its sound speed, a galaxy's stars flung off on the card | `SpaceGen.MassOf`, `GravityOffYear`, `UniverseView.ToggleGravity`, `GalaxyDrift`, the point shader |
| A star's layers | Fly into a star: its layers from the photosphere to its core (`stars.unt` @layer, each with its heat, density and make-up and which kinds have it), the view filled with the glow of the layer's heat, a click ahead a point in it, the wheel diving into its own plasma | `UniverseView.StarLayers`, `StarLayerHere`, `MatterView.SetPlasma` |
| The fall's funnel | Forty tenfolds from the horizon to the point for every hole (the way in is not the hole's size), an inward spike closing to a line; the fall's card gives the way in so far in the funnel's own stretched measure, into the light-years | `DrawFallGrid`, `MatterView` fall card |
| Spinning holes | Every hole's spin from `@law frame-dragging`: its horizon pulled in, its ergosphere, space swept round at its horizon, its disc's inner edge at the innermost stable orbit for its spin, the fall's funnel twisted by the drag; redshift from near its horizon on its card | `Extra.Spin`, `IscoRs`, `PartsFor`, `DrawFallGrid` |
| The tour's end | The last caption's `{seen}`: the places the tour went to against every galaxy, star, world and moon there is, as a true percentage | `SeenWords` |
| Clocks in gravity | Your own clock against far clocks in the title wherever you are (the star's, the world's and the holes' pulls summed), slower in a well, stopped at a horizon, past it unseen; a world's card gives the microseconds a day its pull costs clocks on its ground and a satellite's gain four radii up; the fall's card the far watcher who never sees you cross | `UniverseView.ClockFactorHere`, `WorldGm`, `ClockWords`; `MatterView` fall card |
| Supernovae | Every galaxy's own from its seed and the clock: two to five a century, six real kinds (II-P, II-L, IIn, Ib/Ic, Ia, superluminous) with their light curves, speeds and colours; as bright as physics says from where you are; up close a fireball in the colour of its heat; the shell free then slowing, seen only from near in its real colours; the neutron star, black hole or magnetar born; the dead star gone with its worlds; the neighbours scorched | `UniverseView.Supernovae`, `DrawSupernovae`, `@law supernova`, `@supernova` |
| The nebulae and clusters | Dived into: their gas at real densities, atom by atom, their composition from the content | `MatterView.SetSpace` |

## Still to come (U12)

Gravity as a live force shaping every orbit moment by moment (the switch is in).

See also: [U4 Renderer](u04-renderer.md) · [U7 Worlds](u07-worlds.md) · [U9 Tour](u09-tour-and-captures.md)
