---
title: U8 · The matter — grains, crystals, atoms, electrons, nuclei, quarks, and on below
status: verified
sources: [src/shell/MatterView.cs, src/gen/GrainField.cs, src/render/MeshPipeline.cs, content/minerals.unt, elements.unt, physics.unt, DNA-AND docs/DESIGN.md 255–292, 414, 470–472, 478, 494–509, 524–527, 539–541, 566–572]
last-verified: 1591b7f
---

# U8 · The matter

> "i love the scale when u go down to um u should start to see atoms and shit" · "okay but do we really have the full table of elements mix on all bodies to what they should have mixes of" · "even electrons are zommable to" · "its qwark to galaxy web and super masive black hole not galxy to molecule"

Below the ground's finest piece the same camera goes on into what the ground is made of, every piece at its real size, with no switch and no floor.

## Built

| Level | How it works | Where |
|---|---|---|
| Grains | The ground hands over about a third of a grain up (about 2 micrometres on sand), onto one grain's face; the grains are the ground's own (one field for the whole world), each its mineral from the ground's mix; the things on a face at real sizes (silt specks, clay plates, iron-oxide skins, nanoparticles, the water film, `@surface`) | `GrainField`, `MatterView.GrainPatch`, `SurfaceThings` |
| The face | Drawn from the grain's coarse shape in rings round you, the camera's height measured on that same shape; its detail, stains and atomic terraces (0.3 nm steps) worked out per pixel on the card in double precision; a sheet in the face's plane under it close in, spikes thrown out; greys below light's wavelength | `MatterView.PatchRing`, `MeshPipeline` (detB) |
| Crystals | Every mineral's published unit cell and sites (Crystallography Open Database), tiled by one engine; trace elements on their real share of sites (`trace <host> <element> <share>`); every natural element placed somewhere, the noble gases in the air; the periodic table opened from View | `MatterView.Crystal`, `minerals.unt`, `elements.unt`, `ElementView` |
| The lattice's level | From about ten nanometres down to one: every atom its electron cloud at its real size, its nucleus a spark, the bonds the shared electrons between neighbours, nothing of the single electrons; the levels hand over by degrees (the clouds fading out and the atoms' systems in between five spacings and three; the atom you are at to its inside view between eight widths and four) | `MatterView` (the crystal block, `LatticeCloud`) |
| The atom's level | Below a nanometre no atom is a ball: its nucleus a spark, its electrons in their real subshells and orbitals (Madelung order, Slater distances), each on a path in its orbital's shape, a new path every time round, the valence shell in gold, the bonds the shared pairs themselves; the orbitals as steady clouds for the atom you are at; an electron ridden beside like a comet | `MatterView.Cloud`, `AtomSystem`, `Orbitals`, `SharedPair` |
| The nucleus | Its real protons and neutrons, each drawn as its three quarks in its gluon glow at every size (the flux tubes and the sea's pairs close in), packed by the strong force against the protons' push (`@law nuclear-force`, `coulomb`); binding energy by the mass formula; beta stability and how it would decay; the 1s cloud inside the atom | `MatterView.NucleonsOf`, `Decay` |
| The quarks | Three in each nucleon in their gluon flux tubes, sea pairs popping in and out; points as far as measured; the zoom on below past the Planck length, the card saying what is known there, which is nothing | `MatterView` (the nucleon block), `ScaleHud.Words` |
| Picking | Every grain, face spot, atom, electron, nucleon and quark clickable with its card; a click always picks something; the activating click too | `MatterView.Pick`, `AddBody` |
| Decay | Every unstable atom in view goes at its own moment by its real half-life (`elements.unt` half-life, decays) on the universe's clock at whatever speed it runs: its alpha knot, electron or other half flung out and flying off, and it is its daughter from then on; the cards give the half-life, how long half of them take by the clock here, and what an atom decayed from | `MatterView.SetClock`, `Daughter`, `DecayFlash` |
| Other matter | A nebula's gas, a star's plasma, a neutron star's crust, a black hole's fall, the sea bed, a giant's layers, the air over every ground: the same zoom everywhere | `MatterView.SetSpace`, `SetHole`, `SetNeutronCrust`, `SetAirAbove` |

## Still to come (U12)

Light rendered truly below its wavelength (the greys are in); the fields below the quarks' points.

See also: [U7 Worlds](u07-worlds.md) · [U3 Engine core](u03-engine-core.md) · [How it works](../../docs/in-game/HOW-IT-WORKS.md)
