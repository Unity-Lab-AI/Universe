# Universe

**One camera. One world. A visible universe 13.4 billion light-years across, about 100 billion galaxies, down to the quarks inside the protons of the atoms of a grain of sand, without a single loading screen.**

## What is this?

Universe is a whole universe you can fly through: a visible universe 13.4 billion light-years across, holding about 100 billion galaxies gathered along the cosmic web (filaments and walls of galaxies, knotted into clusters, round great empty voids). Pull back far enough and galaxies are pinpoints of light along that web. Roll the mouse wheel, and one of those specks grows into a spinning island of stars. Keep going and you're among its stars, then circling one of them, then watching its planets swing around it. Pick a planet and fall toward it: oceans, mountains, clouds. Keep falling and the ground becomes grains, the grains become crystal, the crystal becomes atoms, and on inside an atom: its electrons, each element's own (in their real subshells and orbital shapes, the inner ones of a heavy atom held tight and fast), a nucleus of protons and neutrons, and inside those, quarks.

You never "change level." It's one long dive, and you can turn around at any point and fly all the way back out.

## Nothing is stored. Everything is grown.

No photos, no painted backdrops, no pre-made maps. Every galaxy, star, planet and pebble is worked out on the spot from a single starting number, the seed. Same seed, same universe, every time. Fly away from a star and come back, and it's the same star, because it was never thrown away; it was simply worked out again.

A galaxy holds about a hundred billion stars. Your computer could never keep them all, so it doesn't try: it makes the stars around you as you go, at the real density stars have in a real galaxy.

## How does the zoom go on, from the universe to the quarks?

It is one camera the whole way, from the edge of the visible universe to a quark, about 42 factors of ten, with nothing swapped in between. Four things make it work.

**The moving anchor.** Computers lose precision with huge numbers: measuring an atom in units of "width of the universe" just doesn't work. So the camera always measures itself from something nearby: a galaxy's centre, then a star, then a planet's ground, then the grain you are on, then the atom. Everything near you is exact, whether you are light-years or atoms away. Depth uses reversed floating point, so near and far never fight.

**Everything is made where you are.** Galaxies are made in regions round you (each region always holds the same ones, from its place); stars in cubes round you at a galaxy's real density; a planet's ground in pieces finer the closer you are; grains, crystals and atoms round the point you dive into. Nothing is stored, so there is nothing to run out of.

**The waveform.** Our own CDF 9/7 wavelet engine (the same family of math behind JPEG 2000) stores a shape as a list of waves: big waves are the big shapes, small waves the fine detail. The detail of every surface at every size comes from it: a set of waveforms made in the wavelet's own terms and kept as its equations, one for each finer octave, so zooming in adds the next octave of waves. The planet's ground and the face of every grain and stone take their roughness, steps and stains from these.

**Mode-E, past the stored waves.** Where the stored octaves run out, mode-E carries on: Newton's method on z³ − K with cellular octaves that subdivide as the zoom rises, so detail of the same character keeps growing, finer and finer, without repeating. That is what makes the zoom endless rather than only very deep.

**Where it stops today.** The surfaces' detail has no floor. The bodies do: below the quarks (about 10⁻¹⁹ km) there is nothing further built yet; what lies beyond, the fields the particles are waves in, is still to come.

## Black holes, comets and the bodies between

Black holes sit among a galaxy's stars, and a giant one at every galaxy's heart. Each is feeding or quiet: a feeding one glows with its accretion disc, white-hot at the inner edge and cooling to red further out, and some fire jets from their poles at nearly the speed of light; a quiet one is only its shadow and the starlight it bends. Asteroids fill a star's belts, lumpy cratered rocks lit from their star's side when you come close; a few comets fall past their star, a dark nucleus in its glowing coma with its ion tail straight away from the star and its curved dust tail; more icy bodies drift in its far Oort cloud; neutron stars and rogue planets wander among the stars, and every one of them can be clicked for its card: its mass, its size, its pull and escape speed where you are.

## A dead universe that's still alive

There's no life here yet, but the planets aren't frozen. Their insides cool, plates drift, volcanoes erupt, the magnetic field flips, ice ages come and go, mountains wear down and rocks from space leave craters. Every planet runs on its own clock.

## Start

The landing page opens first. Type a seed (or press **Random**, or leave it blank for a new universe), then **Begin**, or **Take the tour**. Your settings and the **Guide and wiki** are there too.

## How to fly

- **Mouse wheel:** fly toward what the mouse points at. You slow down as you get close, so you never crash and never stop.
- **Ctrl + wheel:** eight times faster.
- **W A S D, Q and E:** fly like in a first-person game: forward, back, left, right, down and up. The speed scales to whatever is nearest, so crossing between galaxies feels the same as skimming a metre over the ground. Hold Shift to go four times faster.
- **Click** a galaxy, star, planet or spot on the ground: the camera turns to it and the wheel flies you straight there.
- **Right click:** let go of what you picked.
- **Space:** pause time. **Comma** and **full stop:** slow it down or speed it up, from real time to ten years a second (the Time menu has them too). Every planet turns on its own day, so you can sit on the ground and watch its star rise and set.
- **Drag:** with something picked, circle round it at the distance you are at, to see a solar system from above or a galaxy edge-on. With nothing picked, look around.
- **Info cards:** whatever you pick gets a card beside it: what it is, its id, and its facts (a star's temperature, planets and habitable zone; a planet's orbit, year and what it's made of; the ground's height and whether it's land or sea).
- **H:** show or hide the cards and the scale bar, which tells you how big what you're looking at is, from light-years to picometres.
- **Help menu:** Readme, How it works, Credits, About.
- **B (among atoms):** glide across the bond to the next atom, through the outer electrons the two share.
- **F12:** a screenshot (PNG). **F9:** start or stop recording a clip (MP4 video). Both go in the `captures` folder.
- **View menu:** drag speed, show FPS (frames a second, top right), invert either drag direction, GPU use.
- **The grand tour (T, or Take the tour on the landing page):** a five-minute film that shows the scale of things, from the whole visible universe down to the quarks: the cosmic web, a galaxy, a black hole, two star systems with an asteroid and a comet, a world and a glide over its land, the sky from the ground, its stones and grains, one grain and a spot on its face, a flight through a crystal's lattice of atoms, an atom and its neighbours, an electron, the nucleus, a proton and its quarks, circling each as it goes, picking something new at every scale, and never stopping on the way down from the ground to the atoms. Its captions tell the story and name what is in view. Any key or click takes the controls back. The tour is a plain text list of stops (`content/tours/grand-tour.dnat`), so you can write your own.

## Want to build your own?

Everything needed to build, change and ship your own copy is in this project. See the technical guide in `HOW-TO-BUILD.md`, which comes from `docs/release/UNIVERSE-HOW-TO-BUILD.md` here.

## Who made it

The Unity AI Lab: four people, open-source, built on stubbornness. https://unityailab.com
