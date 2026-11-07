# How it works

> One camera, about 42 factors of ten, from the edge of the visible universe to a quark. Nothing is swapped in between.

## 🎥 One camera, infinite zoom

There are no separate views and no loading screens between scales. One camera flies through one universe that holds every scale: the galaxies, each galaxy's stars, each star's system, each world's ground, and the matter it is made of.

Each roll of the wheel moves the camera **a share of the way to the nearest thing**, so the zoom slows by itself as things come close and never runs out.

## 🎯 Precision at every scale

A single number can't hold both the width of the universe and a millimetre. So the camera measures itself from **an anchor** close by:

| You are at | The camera measures from |
|---|---|
| Among the galaxies | The nearest galaxy's centre |
| In a star's system | That star |
| At a world | The world itself |
| In the matter | The grain, then the atom |

Everything near you is worked out exactly, whether you are light-years or atoms away. The screen's depth uses *reversed floating point*, so near and far never fight, from a micrometre in front of you to across a galaxy.

## 🌊 The coefficient waveform (CDF 9/7)

The lab's own wavelet engine, from **Uni Vs Matics**, using the same wavelet as JPEG 2000. A field of values (heights, colours, densities) becomes a short list of wavelet terms:

- **Coarse terms** are the big shapes, **fine terms** the detail.
- The strongest terms are kept until almost all of the field's energy is captured, then packed small.
- A field can be rebuilt at any level of detail, edited by changing a few terms, and saved small.

It carries each world's changes over its six cube faces: mountains wearing down and rising, and new craters.

## ♾️ Mode-E: detail that never runs out

Where stored detail ends, **mode-E** grows new detail of the same character, for ever. It uses Newton's method on *z³ − K*, carried into a 3D cubic surface and textured by cellular noise whose cells divide by tenfold steps as you zoom.

## ⚛️ The matter, at its real sizes

Below the finest ground, the camera goes on into what the ground is made of, every piece at its real size:

| Layer | What it is |
|---|---|
| **Grains** | About 0.2 mm across, each one mineral from its ground's own mix: quartz, feldspars, clays, olivine, calcite, salt, ices and more |
| **Crystal** | Rock salt as sodium and chlorine on a cube grid 0.282 nm apart, quartz as a silica network, water molecules bent at 104.5° |
| **Atoms** | Each atom's electrons in their real shells, round a nucleus about a hundred thousand times smaller than the atom |
| **Nucleus** | That element's real protons and neutrons |
| **Quarks** | Three in each proton and neutron, held by gluons, down to a hundredth of a femtometre |

Click any grain, atom, electron, proton, neutron or quark for its card.

## ⚛️ The four forces, for real

Universe runs on the real laws of nature, with their real numbers (all in `content/physics.unt`):

| Force | What it does here |
|---|---|
| 🪐 **Gravity** | Orbits follow Kepler's law; the card of anything with a known mass gives its pull and escape speed where you are; a black hole's horizon is 2GM/c², and it bends the light behind it into a ring |
| ⚡ **Electromagnetism** | Electrons fill their shells and orbitals in their real order; bonds are shared electrons; a star's colour comes from its temperature; protons push apart in the nucleus |
| 🔴 **Strong nuclear force** | Packs protons and neutrons into the nucleus, with a hard core and a short reach; glues quarks into every proton and neutron; sets each nucleus's binding energy |
| 🟣 **Weak nuclear force** | Decides which nuclei are stable and how the rest decay: beta-minus, beta-plus, or alpha past lead |

## 🌀 A galaxy that fills in round you

A galaxy holds about a hundred billion stars, and the game keeps a sample of 150,000. Round the camera, the real ones are made at the galaxy's true density, in cubes about 25 light-years wide. Each cube is made from its own position, so they are the same stars whenever you come back.

## 🌋 A living world, even a dead one

Every rocky world runs on its own clock:

- Its heat inside runs down, plates move, volcanoes erupt and quakes shake at real rates.
- The magnetic field flips, and the air and climate follow real chemistry.
- Ice ages come with the orbit's wobbles, mountains wear down, and rocks from space strike.

Its ground changes all at once at big moments, never piece by piece in front of you.

## 🕰️ Time and days

Time runs at the speed you choose, from paused and real time up to ten years a second. Every world turns on its own day, so its star rises and sets over the ground. The light on the ground is its star's own colour through its air.

## 💻 Your own computer

- The game picks the **strongest graphics card** in the computer.
- The ground is built in the background on **every processor core**, with only what is in view in fine detail and what you need most made first.
- **View > GPU use** sets how much of the card's time it may use, from 10 to 90 per cent. The card's working time is measured every frame and the detail is turned up or down to hold it there.
- **View > Render scale** trades sharpness for speed.

## 🚧 Still to come

The waveform carrying every field from galaxies to microbes (gas and star fields, the ground itself, clouds, stars' surfaces), then life, gameplay and the interface.
