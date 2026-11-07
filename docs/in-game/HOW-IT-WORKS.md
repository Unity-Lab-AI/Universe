# How it works

> One camera, about 42 factors of ten from your horizon to a quark, and on down without a floor. Nothing is swapped in between.

The universe has no edge. What you see ends at a horizon, the distance from which light has had time to reach you, and it is centred on you: move a light-year and you see a light-year further. Galaxies are made wherever you go. The farthest light comes reddened, because the space it crossed was stretching as it came; past the horizon, space stretches away faster than light can cross it, sped by dark energy.

## One camera, infinite zoom

There are no separate views and no loading screens between scales. One camera flies through one universe that holds every scale: the galaxies, each galaxy's stars, each star's system, each world's ground, and the matter it is made of.

Each roll of the wheel moves the camera **a share of the way to the nearest thing**, so the zoom slows by itself as things come close and never runs out.

### No floor

Rolling in, the wheel comes down onto what is there at each size: an atom, then its nucleus, then a proton or neutron, then a quark, and whatever it comes down onto stays in the picture however fast you roll. Inward there is no end. Below the quark, which shows no size down to a ten-thousandth of a femtometre, the zoom keeps going: the scale bar counts down through attometres, zeptometres and yoctometres, past the Planck length, as far as a double-precision number can count (10⁻²⁹⁰ km, which is to say for ever in practice). What is there is what physics knows, which is nothing, and the card says so. Outward you are held inside the visible universe: past its edge there is nothing anyone can see.

## Precision at every scale

A single number can't hold both the width of the universe and a millimetre. So the camera measures itself from **an anchor** close by:

| You are at | The camera measures from |
|---|---|
| Among the galaxies | The nearest galaxy's centre |
| In a star's system | That star |
| At a world | The world itself |
| In the matter | The grain, then the atom |

Everything near you is worked out exactly, whether you are light-years or atoms away. The screen's depth uses *reversed floating point*, so near and far never fight, from a micrometre in front of you to across a galaxy.

## The coefficient waveform (CDF 9/7)

The lab's own wavelet engine, from **Uni Vs Matics**, using the same wavelet as JPEG 2000. A field of values (heights, colours, densities) becomes a short list of wavelet terms:

- **Coarse terms** are the big shapes, **fine terms** the detail.
- The strongest terms are kept until almost all of the field's energy is captured, then packed small.
- A field can be rebuilt at any level of detail, edited by changing a few terms, and saved small.

It carries each world's changes over its six cube faces: mountains wearing down and rising, and new craters.

## Mode-E: detail that never runs out

Where stored detail ends, **mode-E** grows new detail of the same character, for ever. It uses Newton's method on *z³ − K*, carried into a 3D cubic surface and textured by cellular noise whose cells divide by tenfold steps as you zoom.

## The matter, at its real sizes

Below the finest ground, the camera goes on into what the ground is made of, every piece at its real size:

| Layer | What it is |
|---|---|
| **Grains** | About 0.2 mm across, each one mineral from its ground's own mix: quartz, feldspars, clays, olivine, calcite, salt, ices and more |
| **Crystal** | Rock salt as sodium and chlorine on a cube grid 0.282 nm apart, quartz as a silica network, water molecules bent at 104.5° |
| **Atoms** | Each atom's electrons in their real shells, each on a path in its orbital's own shape (s round, p through its two lobes, d round its leaves), the paths slowly turning so they are never the same twice, round a nucleus about a hundred thousand times smaller than the atom |
| **Nucleus** | That element's real protons and neutrons |
| **Quarks** | Three in each proton and neutron, held by gluons: points as far as anyone has measured, and the zoom goes on below them without a floor |

Click any grain, atom, electron, proton, neutron or quark for its card.

Below light's wavelength (about 400 to 700 nanometres) nothing has a colour, so from there down the grain's face and its stones are shown in greys, as an electron microscope sees them; the colours of the atoms and electrons are marks, not light.

## The four forces, for real

Universe runs on the real laws of nature, with their real numbers (all in `content/physics.unt`):

| Force | What it does here |
|---|---|
| **Gravity** | Orbits follow Kepler's law and each body's speed follows from its orbit; the card of anything with a known mass gives its pull and escape speed where you are; a black hole's horizon is 2GM/c², and it bends the light behind it into a ring; a feeding hole's disc is gas on Kepler orbits (the inner edge fastest, so it shears into spirals), brightened on the side coming at you by its near-light speed, with stars swept round the hole on tight ellipses and, at some, a star torn into a stream and swallowed; a galaxy's stars orbit at a flat speed out to its rim, held by its dark-matter halo; relativity turns each orbit's closest point round its star and slows clocks in a gravity well |
| **Electromagnetism** | Electrons fill their shells and orbitals in their real order; bonds are shared electrons; a star's colour comes from its temperature; protons push apart in the nucleus |
| **Strong nuclear force** | Packs protons and neutrons into the nucleus, with a hard core and a short reach; glues quarks into every proton and neutron; sets each nucleus's binding energy |
| **Weak nuclear force** | Decides which nuclei are stable and how the rest decay: beta-minus, beta-plus, or alpha past lead |

## A galaxy that fills in round you

A galaxy holds about a hundred billion stars, and the game keeps a sample of 150,000. Round the camera, the real ones are made at the galaxy's true density, in cubes about 25 light-years wide, every cube within about 60 light-years of you (both are traits of the universe template), so the sky is as deep in stars on every side. Each cube is made from its own position, so they are the same stars whenever you come back, and each star is placed exactly, to a fraction of an AU, however far across the galaxy you are.

## A living world, even a dead one

Every rocky world runs on its own clock:

- Its heat inside runs down, plates move, volcanoes erupt and quakes shake at real rates.
- The magnetic field flips, and the air and climate follow real chemistry.
- Ice ages come with the orbit's wobbles, mountains wear down, and rocks from space strike.

Its ground changes all at once at big moments, never piece by piece in front of you.

### Under the sea

A world's seas are dived into. Over water the surface is the floor until you are a metre from it; then the sea bed is, and you go on down through the water: its own colour all round, the light dying away with depth (sunlight is gone within a few hundred metres), everything beyond about thirty metres lost in it, and the bed drawn through it. At the bed the matter is the sea floor's own (silt, sand, its minerals) with water molecules above the grains instead of air, and the zoom goes on into it as on land.

## Time and days

Time runs at the speed you choose, from paused and real time up to ten years a second. Every world turns on its own day, so its star rises and sets over the ground. The light on the ground is its star's own colour through its air.

## Your own computer

- The game picks the **strongest graphics card** in the computer.
- The ground is built in the background on **every processor core**, with only what is in view in fine detail and what you need most made first.
- **View > GPU use** sets how much of the card's time it may use, from 10 to 90 per cent. The card's working time is measured every frame and the detail is turned up or down to hold it there.
- **View > Render scale** trades sharpness for speed.
- **View > Show GPU and CPU stats** puts the numbers beside the frames a second: the processor's and the card's time on each frame, the processor's share of every core, the memory in use, the card's own memory in use of what it allows, how busy the card is, the detail, and the ground pieces waiting. They are read twice a second from Windows' own counts, so they cost nothing to show. The same tick is on the landing page.

## Built from scratch

There is no engine under this, no library beside it and no art inside it. The window, the input and the menus are direct calls into Windows. The graphics are Direct3D 12, called through its method tables by our own declarations, with our own shaders compiled as the program starts. The maths (vectors, the CDF 9/7 wavelet, mode-E, the physics), the content format and its reader, every generator (galaxies, stars, systems, worlds, ground, grains, atoms), the camera, the tour, the cards and their blocky font, this document viewer, the installer and the icons are all our own code: about 18,000 lines of C# and shader, and 10,000 lines of templates. What it borrows is Windows itself: Direct3D 12 and its shader compiler for the card, Media Foundation to pack a clip as MP4, the framework's Deflate inside a PNG. The compiler is the one inside every copy of Windows. Nothing is downloaded and nothing is installed to build it.

## Still to come

The waveform carrying every field from galaxies to microbes (gas and star fields, the ground itself, clouds, stars' surfaces), then life, gameplay and the interface.
