# The front-man equation — problem, hypotheses, experiments

Goal: one small file that any MP4 player plays as the tour, and that carries the tour's CDF 9/7 equations for our program.

## 1. The problem as maths

Notation:

- `I_t` is frame t of the tour, the picture the program rendered.
- `C_t = E(I_t)` is its CDF 9/7 equation field, the kept terms. `E` is the equationalizer (`Wavelet97.Forward` + `Keep`), and `R(C_t)` rebuilds a picture from it.
- `B` is an H.264 bitstream, and `D(B)_t` is the picture a player shows for frame t. `D` is fixed and bit-exact: the H.264 standard says exactly which pixels every decoder must output. So we can compute on our side exactly what Media Player will show.
- `|x|` is a size in bytes.

The front-man equation is a map `F : {C_t} → B` with three demands:

1. **Plays:** `B` is a valid H.264 stream (in an MP4 box), so every player decodes it.
2. **Looks right:** `D(B)_t ≈ I_t` for every t, judged by peak signal-to-noise ratio (PSNR) against the rendered frame, as `Wavelet97.Psnr` already measures.
3. **Small:** `|B| + |P|` is as small as possible, where `P` is whatever extra payload rides in the private box so our program gets the exact `C_t` back.

The size to beat is the recorder's own MP4 of the same tour, about 23 KB a frame at 480 wide.

## 2. Facts about the H.264 decoder the equation has to speak

- **Pictures come in 16 × 16 macroblocks.** Each block is predicted, either from its neighbours in the same frame (intra) or from a block of an earlier frame shifted by a motion vector (inter, quarter-pixel accuracy). What prediction misses, the residual, is sent through a 4 × 4 or 8 × 8 integer DCT.
- **Quantisation sets size against quality.** The step size doubles for every 6 steps of the quantisation parameter QP: `Qstep(QP) ≈ 0.625 · 2^(QP/6)`.
- **A block that copies the predicted block with no residual is a skip, and costs about one bit.** Black space costs almost nothing in H.264 already.
- **A deblocking filter smooths block edges after decoding.** It's part of `D`, so we know it exactly.
- **The encoder chooses by minimising `J = Dist + λ · Rate`.** The reference encoder uses `λ ≈ 0.85 · 2^((QP − 12)/3)`.

Everything a player can show is a picture made of these pieces. So the equation's output must be written in these pieces: prediction modes, motion vectors, quantised DCT residuals.

## 3. Hypotheses

**H1. The DCT terms of a frame follow from its wavelet terms, linearly.**
A picture is `I = Σ_k c_k ψ_k`. A block DCT `T` is linear, so `T·I = Σ_k c_k (T·ψ_k)`. The DCT residual of each block can therefore be computed straight from the sparse wavelet terms, with no full-picture rebuild. A fine term `ψ_k` (7 to 9 taps a side at its level) touches one to four blocks. A coarse term touches many, but there are few coarse terms.
*What this buys:* speed, an encode that works on the equations. On its own it does **not** make the file smaller, because H.264 still spends its normal bits on the picture.
*Kill test:* compute `T·I` both ways on one tour frame. They must agree to rounding. Time both.

**H2. We know the motion exactly, so inter frames come almost free.** *(the main size win)*
A normal encoder has to guess motion by searching. We rendered the frames, so we know the camera matrix and the depth of every pixel. The true screen motion of a pixel at depth `z` is `m(x) = π(M_{t+1} · M_t⁻¹ · π⁻¹(x, z)) − x`, where `π` is the projection. H.264 takes one vector per block, so take each block's mean `m`, rounded to a quarter pixel. During a slow pan or zoom over a mostly black sky, most blocks become a skip or a vector with a tiny residual.
Zooms have an extra property in wavelet terms: a 2× zoom moves a term from level `j` to level `j − 1` at the same place. Between frames, most of `C_{t+1}` is `C_t` moved, and only the terms that newly show up are new.
*Prediction:* inter frames cost well below an encoder that has to search, by an amount set by how far the searching encoder's guesses miss. Unknown until measured.
*Kill test:* encode the same frames twice, once with searched motion and once with the exact vectors, at the same PSNR, and compare bytes. It only works when the vectors come straight from the renderer, so it needs a recorded camera path.

**H3. Spend bits where the equations say the picture has something.**
Uni Vs Matics keeps terms until `(1 − tol²)` of the energy is captured. Use the same rule per macroblock: `E_b` is the wavelet energy falling in block `b`, and its QP is chosen so the block's error stays under `tol² · E_b`. Since `Dist ∝ Qstep²`, this gives `QP_b = 6 · log2( tol · √(E_b / N_b) / 0.625 )` (up to a constant to calibrate). Stars, which are tiny, sharp and bright, keep a fine QP. Empty space gets the coarsest QP or a skip.
*Kill test:* compare file size at equal PSNR, and check the star count in decoded frames, against the encoder's own rate control.

**H4. Don't carry the equations at all. Rebuild them from the picture and carry only the correction.** *(the main idea for the box's size)*
The private box doesn't need the whole `C_t`. Our program can decode `B` itself and equationalize what the player would show: `C'_t = E(D(B)_t)`. Carry only `ΔC_t = C_t − C'_t`, the terms that differ. If the front man looks right, `ΔC` is small, mostly fine-detail terms H.264 rounded away. So:

`|file| = |B| + |ΔC|`, with `|ΔC| ≪ |C|`.

At the limit, if exact equations aren't needed, the box is empty and the file is just `B`. In that case it's a well-made MP4, and the equations come back from it on load.
*Kill test:* on tour frames, measure `|ΔC_t|` against `|C_t|` at a few QPs.

**H5. The lower bound: no equation beats this.**
Whatever the front man is, the player shows `D(B)`. That stream has to carry at least the information in the pictures it shows, at the quality shown. So `|B|` can't go below the entropy of the shown video at that quality. The front-man equation can only win by:

- exact motion (H2),
- better choices of where to spend bits (H3),
- not paying twice for the equations (H4).

It can't make H.264 carry more picture per bit than H.264's own tools allow. Any plan that claims a whole tour in far less than an excellent H.264 encode at the same quality breaks this bound and is wrong.

## 4. The equation we're aiming for

    F({C_t}, {M_t}, {z_t}) =
        for each frame t:
            if t is a keyframe:  intra blocks; residual from T·Σ c_k ψ_k            (H1)
            else:                vectors from the camera:  v_b = round¼( mean_{x∈b} m(x) )   (H2)
                                 residual = T·(I_t − warp(D(B)_{t−1}, v)), quantised at QP_b   (H3)
                                 a block whose residual quantises to zero → skip
        box  = { ΔC_t = C_t − E(D(B)_t) }                                            (H4)
        file = MP4( B ) + box

## 5. Experiments, in order

| # | What | Needs | Decides |
|---|------|-------|---------|
| E1 | H4 on the existing tour MP4: decode frames, equationalize, measure the terms a recorded `.cdf97w` would need as a correction | The tour MP4, `Wavelet97`, a frame decoder (Media Foundation in the program, or Python `cv2` offline) | Whether the box can be small. The cheapest test, and it needs no new encoder. |
| E2 | H1 correctness and speed on one frame | `Wavelet97`, a 4×4/8×8 integer DCT | Whether to encode from the equations directly |
| E3 | H3 per-block QP against the encoder's own rate control | An encoder that accepts per-block QP: our own H.264 writer, or Media Foundation's region-of-interest settings if the card's encoder supports them | Bits saved at equal PSNR and equal star count |
| E4 | H2 exact motion against searched motion | The camera matrices and depth logged per frame while filming, plus our own H.264 inter writer | The main size win, or not |
| E5 | The whole of F on a one-minute stretch of the tour | E1 to E4 | The real file size against the recorder's MP4 |

E3 and E4 need us to write H.264 ourselves, at least the macroblock layer, CAVLC entropy coding and the MP4 box writer, because Windows' encoder doesn't take motion vectors from outside. That's a large piece of work. E1 and E2 don't need it and should come first.

## 6. Honest expectations

- The front man plays everywhere only because it's real H.264. That part is settled.
- How small it gets is open. H2 and H4 are the real chances to beat a normal MP4, but neither is measured yet.
- H5 sets a hard floor. Nothing here gets a whole tour into a few kilobytes.

## 7. Results

### E1 — the correction box (2026-11-08)

Setup: five frames of the recorded tour (30, 120, 240, 360 and 480 s), at 480 wide. The equations use the `.cdf97w` writer's tolerances (0.006, 0.012, 0.012). A DCT-coded copy stands in for what an H.264 player shows: JPEG at qualities 95, 85 and 70, the same 8 × 8 DCT family as H.264 intra. A term counts as wrong in the shown copy if it is off by more than 64 quantisation steps. Script: `shots/e1.py` in the working folder.

| Frame | Front man (q85) | PSNR | Full equations | Correction box |
|------:|---------------:|-----:|---------------:|---------------:|
| 30 s | 43 KB | 32.4 dB | 888 KB | 795 KB (90 %) |
| 120 s | 19 KB | 37.9 dB | 446 KB | 337 KB (76 %) |
| 240 s | 20 KB | 37.5 dB | 452 KB | 317 KB (70 %) |
| 360 s | 20 KB | 35.8 dB | 796 KB | 608 KB (76 %) |
| 480 s | 17 KB | 38.1 dB | 400 KB | 289 KB (72 %) |

What it shows:

- **H4 fails as a size win.** The correction is 63 to 92 % of the full equations at every quality tried. The terms a DCT copy loses are mostly the fine ones, and those make up most of the field.
- **The equations themselves are the size problem.** At these tolerances, one frame's equations are 10 to 30 times bigger than a DCT picture of the same frame. A full-size `.cdf97w` recorded at these tolerances is very large.
- **So the smallest file is the front man alone, with an empty box (H4's limit).** The program rebuilds the equations from the picture when it loads it. They're then the equations of what was shown, not the exact ones. Carrying the exact ones costs 10 to 30 times the picture.
- **Next:** measure how the equation size falls as the tolerance loosens (Uni Vs Matics' defaults are 0.015, 0.022, 0.022), and the PSNR at each step, against a DCT copy of the same size. Then E2.

### E1b — looser tolerances (2026-11-08)

The same frames, Uni Vs Matics keep-and-pack, against a DCT picture (JPEG) no bigger than the equations. Script: `shots/e1b.py`.

| Frame | Tolerance (Y) | Equations | Their PSNR | DCT at that size, PSNR |
|------:|------:|------:|------:|------:|
| 240 s | 0.006 | 452 KB | 59.9 dB | 41.3 dB (q95, 33 KB) |
| 240 s | 0.015 | 374 KB | 50.8 dB | 41.3 dB |
| 240 s | 0.03 | 286 KB | 45.0 dB | 41.3 dB |
| 240 s | 0.06 | 210 KB | 39.1 dB | 41.3 dB |

Loosening the tolerance shrinks the equations slowly. Even at 0.06 they're still about six times the size of a q95 DCT picture that looks better. What weighs is the packing: about 3.3 bytes a kept term (LEB128 positions and Int16 values), with no entropy coding.

### E1c — the same wavelet, entropy-packed (2026-11-08)

Every coefficient is quantised with one uniform step (a dead zone, so most become zero), then packed with LZMA as a stand-in for a real entropy coder. Script: `shots/e1c.py`.

| Frame | Step | Packed equations | PSNR | DCT at the same size |
|------:|------:|------:|------:|------:|
| 240 s | 0.004 | 75 KB | 40.7 dB | 62 KB, 43.2 dB |
| 240 s | 0.016 | 35 KB | 35.7 dB | 33 KB, 41.3 dB |
| 480 s | 0.004 | 65 KB | 41.4 dB | 52 KB, 43.3 dB |
| 30 s | 0.004 | 162 KB | 38.8 dB | 132 KB, 38.1 dB |

What it shows:

- **Packing cuts the equations 5 to 10 times.** At 240 s they go from 452 KB to 35 to 75 KB, now the same order as the DCT picture.
- **They still trail the DCT picture at equal size**, by 2 to 6 dB. LZMA is a general-purpose packer that knows nothing about wavelets. JPEG 2000 uses this same CDF 9/7 wavelet with a wavelet-aware entropy coder (bit planes, context modelling) and beats JPEG at equal size. So the gap is the coder, not the equations.
- **New hypothesis H6:** a wavelet-aware entropy coder for the `.cdf97w` (zerotree or bit-plane coding with contexts, as in JPEG 2000) brings a frame's equations to the size of an H.264 intra frame or below. Combined with H2's inter-frame terms carried over from the last frame, that's the path to a small `.cdf97w` on its own.
- **For the program now:** the `.cdf97w` writer should drop the per-term Int16 packing for uniform quantisation plus entropy coding. That's the one change these numbers support today.

### Built: the `.cdf97w` version 2 (2026-11-08)

What E1b and E1c support, now in the program (`src/render/ClipWriter.cs`, `src/platform/ClipPlayer.cs`):

- Every wavelet term in a plane is quantised with one step (Y 0.004, Cb and Cr 0.008, a dead zone). There's no per-term keep or Int16 packing anymore.
- Every 30th written frame is stored whole. Each frame in between is stored as its change from the last, packed with Deflate (built into Windows, so no outside code).
- The player adds the changes forward from the last whole frame, so a frame it skips still has its change applied and the terms stay exact.

Measured the same way on the tour at 480 wide and 30 a second, with Deflate, the compressor the program uses. Script: `shots/e1d.py`.

| Stretch | Whole frame | As a change from the last |
|------:|------:|------:|
| 30 s (galaxies, busy) | 188 KB | 134 KB |
| 240 s | 90 KB | 47 KB |
| 480 s | 66 KB | 27 KB |

Against version 1 (400 to 890 KB a frame), that's 10 to 15 times smaller. Against the recorder's MP4 at the same width (about 23 KB a frame), it's still 1.2 to 6 times bigger. What's left to close is H6 (a wavelet-aware entropy coder in place of Deflate, worth about 1.5 to 2 times by E1c's LZMA against Deflate, and more with contexts) and H2 (a change taken after moving by the camera's motion, not just subtracted in place).

### E2 — H1, block DCT straight from the wavelet terms (2026-11-08)

Setup: the tour frame at 240 s, 480 wide, the 20,000 strongest Y terms. Script: `shots/e2.py`.

- **Linearity holds.** The block DCT of the whole rebuilt picture matched the sum of the block DCTs of two halves of the terms to within 4.4 × 10⁻¹⁵, which is rounding.
- **The direct path is the slow one.** Rebuilding the picture and then taking the block DCT took 14 ms for the frame. Doing it term by term was about 225 s with whole-frame footprints. Even with each term reduced to its footprint, every term still pays for an inverse of its own, while the full inverse is linear time over the whole frame.

H1 is true and useless. The front man is fastest made by rebuilding the picture from the equations, which the player already does, and handing that to the H.264 encoder. No new code is needed for this. E3 is next.

### E3 — H3, a quantising step per block from the wavelet energy (2026-11-08)

Setup: frames at 120, 240 and 480 s, 480 wide, luma only. Each frame is cut into 16 × 16 blocks with 8 × 8 DCTs inside, as in H.264, and packed with Deflate. One run uses a single step for every block. The other sets each block's step from its wavelet detail energy (step ∝ energy^−¼), searched until both files come out the same size. Stars are counted as single pixels brighter than all eight neighbours by 40 levels, and checked for survival. Script: `shots/e3.py`.

| Frame | One step for all | Energy-set steps, same size |
|------:|------:|------:|
| 120 s, 20 KB | 39.7 dB, 75 % of 157 stars | 39.6 dB, 79 % |
| 240 s, 22 KB | 39.0 dB, 83 % of 253 stars | 38.4 dB, 71 % |
| 480 s, 18 KB | 40.1 dB, 82 % of 131 stars | 40.3 dB, 78 % |

Across all 15 runs, the energy-set steps were never better than one step by more than 0.2 dB. They were up to 2 dB worse, with a few more stars kept at 120 s and fewer at 240 s.

**H3 fails.** A star is a strong detail term, so energy-set steps give its block a coarse step, and that drowns the star. A single step already treats every block alike. Nothing to build. The steps stay uniform per plane, as in the `.cdf97w` version 2. E4 is next. It needs the camera recorded per frame while filming.

### E4 — H2, the change between frames taken along the motion (2026-11-08)

Setup: eight frames from each of four places in the tour (30 a second, 480 wide), the `.cdf97w` version 2 quantising, Deflate. Each frame's change is coded two ways: in place, as the program does now, and after moving the last shown frame along the motion. The motion is one vector per 16 × 16 block, rounded to a quarter pixel as H.264 does, measured from the frames by dense optical flow. That's the best case for what exact camera motion could supply. Script: `shots/e4.py`.

| Place | Change in place | Change after moving + vectors |
|------:|------:|------:|
| 30 s | 133.4 KB | 132.1 + 0.15 KB |
| 120 s | 52.3 KB | 56.6 + 0.11 KB |
| 240 s | 48.0 KB | 46.6 + 0.12 KB |
| 480 s | 28.7 KB | 31.7 + 0.05 KB |

**H2 shows no win here.** Moving along the motion saves 1 to 3 % in two places and costs 8 to 10 % in two. Two reasons:

- **Wavelet terms don't shift cleanly.** A picture moved by a fraction of a pixel has different terms at every level, so a moved prediction leaves a change about as large as the picture's own.
- **The source has flicker of its own.** The recorded tour is already an H.264 file, and its block noise changes every frame. At step 0.004 that noise is coded too, and no motion removes it.

The second reason means a clean test needs frames the program filmed itself as a `.cdf97w`, not frames decoded from an MP4. Exact camera vectors could only beat the measured flow by the little the flow got wrong, so building camera logging isn't supported by this. The in-place change stays.

### Where the numbers leave the front man

- The front man is a plain H.264 picture, which the program's MP4 writer already makes. The smallest file that plays everywhere is that MP4 alone.
- Carrying the `.cdf97w` inside it in a private box costs the `.cdf97w`'s own size on top: 1.2 to 6 times the MP4 at 480 wide.
- E5 builds that combined file, so one file plays in every player as the MP4 and in this program as the equations.

### E5 — the front man built (2026-11-08)

Built in the program (`src/render/ClipWriter.cs`, `FrontmanWriter`):

- A third clip format, "MP4 carrying the .cdf97w", offered on the start page and under View > Clip format.
- While filming, the MP4 writer and the `.cdf97w` writer both take every frame. When the clip ends, the `.cdf97w` is appended to the MP4 as its last top-level box: a `uuid` box with a 64-bit size and the id `C97W-UniVsMatics`. Every MP4 reader skips a box it doesn't know by its size.
- The start page's Play checks an MP4 for that box. If it's there, the program plays the equations in its own player. A plain MP4 still goes to Windows' player.

Check: the same kind of box was appended to a copy of a sample MP4 (`web/samples/unity-born.mp4`). It still opened and decoded with the same 591 frames. Script: `shots/e5`.

Size: the MP4 plus the `.cdf97w` of the same frames. At 480 wide and 30 a second, that's about 23 KB plus 27 to 134 KB a frame (section 7). Any player shows the MP4, and this program plays the equations.

### Summary of E1 to E5

| Hypothesis | Result | In the program |
|-----------|--------|----------------|
| H1 DCT from the terms | True, but thousands of times slower | No |
| H2 change along the motion | No gain on the recorded tour | No (in-place change kept) |
| H3 per-block step from energy | Fails, and drowns stars | No (one step a plane kept) |
| H4 box as a correction only | The correction is 63 to 92 % of the equations | No |
| H5 the floor | Stands | — |
| H6 quantise and entropy-pack | 10 to 15 times smaller than version 1 | Yes: `.cdf97w` version 2 |
| E5 front man | An MP4 carrying the `.cdf97w` | Yes |

Still open: a wavelet-aware entropy coder in place of Deflate (H6's second half), and a clean rerun of E4 on a tour filmed as a `.cdf97w`.

### The chain as built (2026-11-08)

Media player ← front man (H.264 pictures, what players expect) ← the solving step (each frame's CDF 9/7 terms turned into pixels: dequantise, inverse lifting, YCbCr to RGB) ← the packing step (the `.cdf97w`: the frame's terms quantised, whole or as a change, compressed).

The solving step runs in the program while filming, because no media player runs code from a file. Its pixels are what the MP4 is made of, so any player shows exactly what the equations solve to. The `.cdf97w` rides in the same file for this program's player.

### H6 — a wavelet-aware coder in place of Deflate (2026-11-08)

The coder: an adaptive binary range coder, the same arithmetic as LZMA's. Each quantised term is coded as three parts. First, whether it's nonzero, with the odds learned separately for every wavelet level and for how many of its left and upper neighbours are nonzero. Then its sign. Then its size, as a learned unary count up to 15 for each level, then Exp-Golomb. Script: `shots/e6.py`. Same frames as before, 480 wide.

| Place | Whole frame, Deflate → coder | Change, Deflate → coder |
|------:|------:|------:|
| 240 s | 89.5 → 59.7 KB (1.50×) | 49.3 → 30.6 KB (1.61×) |
| 480 s | 78.2 → 51.3 KB (1.52×) | 22.4 → 12.8 KB (1.75×) |
| 30 s | 193.1 → 140.7 KB (1.37×) | 145.4 → 101.4 KB (1.43×) |

Built as the `.cdf97w` version 3 (`src/render/WaveletCoder.cs`). A one-off round-trip check outside the program (four sets of 491,520 terms, up to ±32,767) gave back every term exactly. Against the recorder's MP4 at 480 wide, about 23 KB a frame, a change frame now runs 13 to 101 KB.

### The `.cdf97w` on the graphics card, and its packing on every core (2026-11-08)

- **The wavelet on the card** (`src/render/GpuWavelet.cs`): the same lifting as `Wavelet97`, in a compute shader on a queue of its own, one line of a plane to a thread. A standalone check outside the game, with no window, compared it with the processor's transform. The largest difference was 1.2 × 10⁻⁶ on terms up to 1.3. A full forward and back on the card returned the plane to within 2.2 × 10⁻⁶. Both are far below the quantising step of 0.004. The writer uses it for the forward transform and, for the front man, the inverse. If the card refuses any part, the writer falls back to the processor and says so in the run log.
- **Where a frame's time went** (1080p, measured outside the game): building the planes 9 ms, forward 30 ms on the processor (about 10 on the card), quantising 10 ms, packing 130 ms on one core, and the inverse for the front man 67 ms on the processor (about 10 on the card). So the packing was the bottleneck, not the transform.
- **The packing on every core (version 4):** each plane is cut into 4 strips of rows, and each strip is coded on its own. All 12 strips run at once. That cut packing from 130 ms to 27 ms, at a cost of about half a percent more bytes. The round-trip check still gives back every term exactly.
- A full 1080p frame now takes about 70 ms, roughly 14 a second. A Half-size clip has a quarter of the pixels, so it keeps up with 30.

### E10 — the filmed clip, the theory checked, and the 100 MB target (2026-11-08)

**The clip as filmed:** `tour 2026-11-08 02-42-26.cdf97w`, version 4, full size (3840 × 2024), 2,732 frames over 637 s. That's 4.3 frames a second, the gaps 218 ms at the median, 4.6 GB in all.

- Whole frames averaged 1,871 KB. Changes averaged 1,635 KB. Frames 218 ms apart differ almost everywhere, so a change barely beats a whole frame, and at times it is 7 times bigger. That matches E4: wavelet terms don't survive motion.
- The coarsest levels had 15 to 55 % of their terms nonzero, coded at the same step as the finest.

**The model, checked against the clip.** Frames rebuilt from the clip were sized by the entropy of the `.cdf97w` coder's own model. Against the bytes those same frames really took in the file, it came within 5 to 10 %:

| Frame | Model | Real |
|------:|------:|------:|
| 600, whole | 2,614 KB | 2,475 KB |
| 601, change | 2,209 KB | 2,000 KB |
| 1500, whole | 1,307 KB | 1,203 KB |
| 1501, change | 1,199 KB | 1,133 KB |

So it can be trusted to predict other settings. Scripts: `shots/e10.py`, `shots/e10b.py`, `shots/rt/an.cs`, `shots/rt/dump.cs`.

**The theory that held:**

1. **Size scales with picture size.** Each halving of the width and height divides the bytes by about 3 (1,685 → 561 → 170 KB a frame at the same step), not by 4. Smaller pictures have more detail per pixel.
2. **Size scales with the step.** A step 4 times coarser divides the bytes by about 3.
3. **H8, a step per level by each level's synthesis energy, holds.** A term at a coarse level spreads its error over many pixels. Giving each level a step of `s₀ / √(Eⱼ / E₁)` spreads the error evenly over the levels, which is the least picture error for the bytes. At the same size it gave 6 to 8 dB better pictures than one step for every level. At small sizes it beat a DCT picture (JPEG) of equal size outright:

| Setting (scaled to the whole clip) | Size | Quality | DCT at that size |
|-----------------------------------|-----:|--------:|-------:|
| Quarter, step 0.032, one step | 82 MB | 29.0 dB | 28.1 dB |
| **Quarter, step 0.032, by level** | **92 MB** | **34.5 dB** | 30.8 dB |
| Quarter, step 0.064, by level | 51 MB | 30.6 dB | 14.0 dB |
| Half, step 0.064, by level | 195 MB | 31.1 dB | 14.7 dB |
| Half, step 0.032, by level | 340 MB | 34.9 dB | 28.2 dB |

**The 100 MB target.** The clip as filmed fits in about 90 MB at quarter size, step 0.032 by level, 34.5 dB. At 30 frames a second there are 7 times more frames. Each change then spans 33 ms instead of 218 and should be much smaller, but that isn't measured yet. It needs a clip filmed with the new writer.

**Built from it (version 5):** steps per level by synthesis energy, finest steps Y 0.032 and Cb, Cr 0.064. The front man is now just what a player needs to know the file: a quarter-size picture at the start, one every ten seconds, and the last one held to the end. The clip itself is the `.cdf97w` in the box.

### The `.cdf97w` made inside the frame (2026-11-08)

Before this, every clip frame stalled the card and copied the whole picture back to the processor: about 33 MB a frame at 4K. Now `src/render/CardClip.cs` records the clip's work into the frame's own command list, right where the frame is drawn. The finished picture is copied into a buffer beside it on the card. One compute pass splits it into Y, Cb and Cr at the clip's size, reflect-padded to 64. The wavelet then runs rows and columns for each plane, and a last pass quantises every term by its plane's step weighted for its level.

Only the whole-number terms come back, a frame later, once the card has finished that frame anyway. The picture never comes back and the game never waits. The packing, an arithmetic coder that has to run in order, is the only part on the processor.

Checked outside the game with no window. A known 1280 × 720 picture at half size went through the card: all 14,788 nonzero terms matched the processor's exactly, with not one differing. The frame took 14 ms on the card, shader compile included. Without a card, or if it refuses, the picture comes to the processor as before.

### E11 — stars at full 4K (2026-11-08)

On the filmed clip's frame 600 (3840 × 2024), a star counts if its pixel is brighter than all eight neighbours by 30 levels. It counts as kept if one is still within a pixel of it after the round trip. The steps are set by level. Script: `shots/e11.py`.

| Finest step (Y) | Whole frame | Quality | Stars kept of 47 |
|------:|------:|------:|------:|
| 0.032 | 869 KB | 34.6 dB | 30 (64 %) |
| 0.016 | 1,361 KB | 38.8 dB | 35 (74 %) |
| 0.008 | 2,025 KB | 44.2 dB | 42 (89 %) |

A 4K picture's single-pixel stars live in its finest terms. Built: above 1920 wide the steps are a quarter (0.008), and at or below it they stay 0.032.

### The `.cdf97w` is waveforms and metadata only (version 7)

On the creator's word, the H.264 part is gone. The file is now three things: the header, a metadata block (the program, when it was filmed, its size, frames a second, how the waveform is made, and the seed and tour for a tour), then the frames of waveforms. The installer no longer marks `.cdf97w` as an MP4 video.

## 8. The conversion equation — how a media player turns the waveforms into pixels (open)

**The constraint, unchanged.** A player runs only the decoders it ships with or has installed. A file can't hand it new code. So the waveforms have to reach the screen through a decoder the player already has, or one we install.

**E12 — JPEG 2000 is the same wavelet (2026-11-08).** JPEG 2000's lossy mode uses the same CDF 9/7 lifting and the same YCbCr split. It quantises with a dead zone and a step per subband, which is what the `.cdf97w`'s per-level steps are. VLC, ffmpeg, MPV and anything built on OpenJPEG decode it, and Motion JPEG 2000 (`.mj2`) is its video form. Measured on the same frame, by OpenJPEG at equal sizes (script `shots/e12.py`):

| Size | JPEG 2000: quality, stars | `.cdf97w`: quality, stars |
|------:|------:|------:|
| ~1.36 MB | 43.8 dB, 27 of 47 | 38.8 dB, 35 of 47 |
| ~0.9 MB | 38.6 dB, 14 of 47 | 34.6 dB, 30 of 47 |

JPEG 2000's coder packs better, by 4 to 5 dB at equal size. Its rate control trades single bright points away for average error, so it keeps up to half as many stars. Ours keeps them.

**Hypotheses to work on:**

- **C1, a transcode to JPEG 2000.** Each frame's own quantised terms are written as a JPEG 2000 codestream. The same wavelet, the same colour split and the same per-level steps map one to one onto its subbands, so no picture is re-encoded and our stars survive. Only the packing changes, to JPEG 2000's bit-plane coder, which E12 suggests is no bigger. Frames go in Motion JPEG 2000. Plays in VLC, ffmpeg and MPV. Not in Windows Media Player, browsers or Discord's inline player. To build: JPEG 2000's tier-1 coder (bit planes with context modelling) and tier-2 (packets), then the MJ2 boxes.
- **C2, a decoder installed on Windows.** A Media Foundation decoder for `.cdf97w`, registered by the installer, so Windows' own players and Explorer thumbnails decode the waveforms directly. Windows only.
- **C3, a player for the web.** The inverse wavelet, the coder and the colour step in a page (JavaScript or WebAssembly, as Uni Vs Matics already does for stills). It plays anywhere a page opens, including a project site, with no install.
- **C4, keeping the frames between whole frames.** C1 and C2 decode whole frames. JPEG 2000 has no changes-between-frames, so a C1 file stores every frame whole and is larger. Either C1 accepts that, or the change frames are carried in a way a JPEG 2000 decoder ignores and our player uses.

The next experiment is C1 on one frame: write our terms as a JPEG 2000 codestream, decode it with OpenJPEG, and check it reproduces our solved picture exactly.

**C1 rejected (2026-11-08), on the creator's word: "WE DO NOT USE OTHER PEOPLES CODE SCRIPS FILES SOFTWARE CREATIONS WHAT THE FUCVK IS WRONG WITH YOU@!@@@@@@".** C1 rests on JPEG 2000, someone else's format, and its decoders, other people's software. It is not pursued, and no prototype of it is kept. The measurements E1 to E12 used outside tools (OpenCV with OpenJPEG, numpy) on the researcher's side only; the program never contained or used any of them. From here, measurements use the program's own code only.

## 9. The equations, own code only (2026-11-08)

All measurements from here use the program's own code (`shots/rt/eq.cs`, `v8.cs` and `ct.cs`: C# programs built on `Wavelet97`, `WaveletCoder` and `CardClip`). No outside tools.

**H9, the dyadic layout.** Each level splits only the low corner, rows then columns. Before, every level ran along all the rows, then all the columns. Measured on the filmed clip's 4K frame 600, with the steps weighted for each layout:

| Step | Rows then columns | Dyadic |
|------:|------:|------:|
| 0.004 | 2,583 KB, 48.3 dB, 42 of 46 stars | 3,110 KB, 51.4 dB, 44 of 46 |
| 0.008 | 1,905 KB, 42.9 dB, 40 | 2,132 KB, 48.8 dB, 44 |
| 0.016 | 1,291 KB, 38.2 dB, 33 | 1,496 KB, 45.5 dB, 43 |
| 0.032 | 828 KB, 34.1 dB, 28 | 1,031 KB, 41.6 dB, 36 |

At equal size, dyadic is about 7 dB better and keeps more stars. **H9 holds.**

**H10, a richer context model.** The odds were also conditioned on the four causal neighbours' sizes and the parent term one level coarser. It saved 2 to 3 %. Its parent part can't run with the strips packed in parallel, since a parent can sit in another strip's rows, so it isn't built.

**Built: the `.cdf97w` version 8.** The layout is dyadic with 6 levels, on the card (one level a dispatch, low corner only), on the processor (`Wavelet97.ForwardDyadic` and `InverseDyadic`) and in the player. Subbands are labelled level × 4 + orientation, each with its own weight from its synthesis energy. Full 4K uses step 0.016 (Y): 1.5 MB a whole frame, 45.5 dB, 43 of 46 stars. That is smaller, sharper and keeps more stars than version 7's 0.008. Checked: the card's terms match the processor's (one in 18,023 off by one at a rounding edge, single against double precision), the solved picture is within one level of 255, and the packing gives every term back exactly.

**The waveforms-to-pixels equations, in the file.** The metadata block, about 2 KB of readable lines, now carries everything that turns the waveforms into pixels: the picture size and padding, the layout and its 6 levels, the five lifting constants, the forward and inverse lifting step by step, the subband labels, the three steps and every subband's weight, how a term becomes a coefficient, the colour equations, how frames are kept (whole or as changes) and how they're packed. Then the program, the date, and the seed and tour.


## 10. The version 8 clip, and where its bytes go (2026-11-08)

**The clip.** `tour 2026-11-08 04-18-21.cdf97w`, version 8, full 4K, made on the card inside the frame. 9,115 frames over 642 s, so 14.2 frames a second (version 4 managed 4.3), 11.2 GB in all. Whole frames average 1,492 KB, change frames 1,192 KB, and frames are 66 ms apart at the median.

**Where the bytes are.** Change frames: 92 KB at the 10th percentile, 781 KB at the median, 2,993 KB at the 90th. The change frames over 600 KB carry 88 % of the file. By half-minute, change frames average 130 to 490 KB in the calm stretches (5:30 to 6:30, 8:00 to 9:30) and 2.8 MB from 4:00 to 5:30. That's nearly twice a whole frame: the picture there changes almost entirely between frames, as with shimmering clouds or moving particles.

**H11, following the motion (own code, `shots/rt/mc.cs`).** On three pairs of consecutive frames from the clip, a searched whole-frame zoom and pan found none worth taking at 50 to 70 ms apart. Refining each 16 × 16 block by up to 4 pixels saved 1 to 13 % on the change. In the calm pairs the change is already 4 to 5 times smaller than a whole frame. **H11 is no help here:** the motion between frames this close is small, and what costs is content that's new each frame.

**H12, each frame the smaller of change or whole.** Estimated from the clip, using the nearest whole frame as a stand-in: 11.2 → 9.8 GB, 13 %. Worth building, since it's cheap, but it isn't the gap.

**Where that leaves the size.** At 4K a whole frame is about 1.5 MB and the busy stretches change completely every frame, so a 4K clip of this tour weighs several GB whatever the packing. To reach 100 MB for ten minutes at 14 frames a second, a frame has to average 11 KB. The waveforms can reach that only in the calm stretches.

**Hypotheses still open:**
- **H13, the choice per subband.** Code the coarse subbands, which stay alike from frame to frame, as changes, and the fine ones, which shimmer, whole.
- **H14, steps that follow what the eye can see in motion.** Coarser fine-subband steps while the picture is changing fast, since fast motion hides fine detail.
- **H15, the universe as the clip.** The `.cdf97u` is 86 bytes for the whole tour, because the universe itself is the equation. Extending it to free flight means storing the camera's path, a few KB a minute. That's the one route under 100 MB at any size.

### H13, H14, H17, H18 on the version 8 clip (2026-11-08, own code: `shots/rt/tw.cs`, `ff.cs`)

8 consecutive 4K frames from a busy stretch (4:30) and 8 from a calm one (8:50):

| | Busy | Calm |
|---|---:|---:|
| Version 8 (first whole, then closed-loop changes) | 3,359 KB a frame, 58.3 dB | 250 KB, 62.3 dB |
| H13, change or whole per subband | 2,919 KB (−13 %), 58.6 dB | 250 KB, no gain |
| H14, the two finest levels of each change at twice the step | 2,979 KB (−11 %), 56.5 dB | 237 KB (−5 %), 61.8 dB |
| H17, the CDF 9/7 through time (8 frames, 2 levels) | 4,146 KB, worse | 339 KB, worse |

**H18, fill instead of store.** The finest levels are not stored, and the decoder fills them. "Left empty" is the 9/7 inverse lifting filling the rows and columns from the coarser levels, the equations already solved. "Fractal fill" scales each term's four children from it, one number per subband by least squares.

| 4K frame | Every level | Finest level filled | With the fractal fill |
|---|---:|---:|---:|
| Busy (5,163 stars) | 2,942 KB, 58.5 dB, 5,124 stars | 1,503 KB, 29.0 dB, 307 stars | 1,503 KB, 29.1 dB, 305 |
| Calm (88 stars) | 708 KB, 62.6 dB, 87 | 424 KB, 33.2 dB, 20 | 424 KB, 33.2 dB, 18 |

**H18 fails.** A single-pixel star is almost all finest-level energy, and the coarser levels hold only a faint smudge of it. Any fill, lifting or fractal (Uni Vs Matics' cubic included), spreads what the coarser levels hold or invents new detail in its place. It can't put back a star that wasn't stored. Across the levels the children's signs don't follow the parent's, so the least-squares scale comes out near zero.

**Where the lab stands.** Every pixel equation tested leaves a busy 4K frame at 1 to 3 MB, and the stars force the finest level to be stored. The stars are themselves exact equations in the universe, made from the seed. So the one route to 4K with every star in 100 to 900 MB for ten minutes is H15: the file stores the seed and the camera's path, and plays back by making the universe again. That's one file, the `.cdf97w` itself, under 1 MB.

## 11. H15 — the clip as the universe's own equations ("dark energy") (design, 2026-11-08)

The creator's words: "we have no resoluation and no fucking pixiles that means all kinds of possibilities... we could do this 100mb for 10 minutes easily as draws and infinate zoom only have to tep out side the visuals and work the data of the equations we already can solve out all variables by arithmatic and can build the front man and its handler script for the cdf97w file to properlyspan patternized fractual universal "lets call it ;dark energy;" that is the universal mesh with fill calculous for the cordinateal syatem for plot location and 0,0,0Time stamp will be loaded in of the of cam at 500mm scale".

**The idea.** No pixels are stored. The universe already solves every galaxy, star, world, grain and atom from its seed. A clip is the seed plus the camera's track through the universe's own coordinates (the "dark energy" mesh), with 0,0,0 and the time stamp as its origin. The handler is our program, which makes the universe from the seed and draws each recorded camera point. It has no resolution: played at any size, every star is exact. It's one file, the `.cdf97w`, with no waveform clip beside it.

**One camera point** (from the program's own state, `UniverseView`):

| Part | Fields | Bytes |
|---|---|---:|
| Plot location | the anchor (`_ax, _ay, _az`) and the offset from it (`_ox, _oy, _oz`), doubles | 48 |
| Facing | forward (`_fx, _fy, _fz`) and up (`_camUp`) | 24 (as floats) |
| Scale | `_farScale`, `_spaceScale`, `_spaceTop`, `_matterScale` (the depth in the matter: 500 mm and below) | 32 |
| Time | the universe's clock (`_simYears`) and the frame's own time | 16 |

About 120 bytes a point. At 30 a second for ten minutes that's 2.2 MB raw. Each point stored as its change from the last and packed, a few hundred KB.

**Scene events.** These are what the camera's position alone doesn't settle. Each one is stored with its frame and its exact arguments:
- the star system opened or closed (its index)
- the world or small body whose ground is made
- every matter setting: a ground's mineral and spot, a gas, a plasma, a black hole's fall, a neutron crust, with the arguments the call was given
- the matter view's own camera (its focus and scale)

**Replay.** The program makes the universe from the seed, then for each frame:
1. It applies the frame's scene events with their recorded arguments.
2. It sets the camera to the recorded point.
3. It draws.

The universe's pieces load at their own pace, so replay waits for each event's scene to be ready before drawing that frame. Playback time is the recorded time, held while the loading catches up, so replay can't drift.

**The handler checks itself.** Each event's scene is identified by what it made (the system's star kind, the ground's mineral, the matter's make-up), recorded beside the event. A replay whose scene doesn't match says so instead of showing a different place.

**Build order:**
1. Capture: the camera point and the scene events, written to a `.cdf97w` (format: `universe`).
2. Replay: the handler, with events, camera, waiting on loading, and the self-check.
3. Then the start page's Play and the menu, and the docs.

**H15 step 1 built (2026-11-08): recording.** A third clip format, "the .cdf97w of the universe itself" (version 9, kind 1). It holds:
- the header and a metadata block that describes every record;
- a camera point each frame, 30 a second at most: the anchor and offset, the facing, the four scales, the universe's clock, the matter view's focus and its target, and the galaxy, system and ground as a check, 141 bytes;
- every scene-setting call of the matter view as it's made, with its exact arguments.

The calls are `Set`, `SetGround`, `SetSpace`, `SetPlasma`, `SetNeutronCrust`, `SetHole`, `SetAir`, `SetAirAbove` and `SetLife`. Only the outermost call is recorded: a call made inside another is rebuilt when that one is replayed. The matter view keeps the calls that built the scene now shown, so a clip begun mid-dive starts with them. Everything after the metadata is packed with Deflate. No pixels are taken back from the card while it records. Next: step 2, the replay.


## 12. The front man: a translator equation into H.264, VP9 and AV1, all three written in our own code (opened 2026-11-08)

The creator's words: "all three and see which maskes a bettrer translator equation that we can use inline with metadata and the frontman".

**The task.** Discord's own player reads H.264, VP9 and AV1. For each, write our own encoder, every line ours with no library and no Windows encoder, to serve as the `.cdf97w`'s front man. It shows a picture in a chat app's player while the metadata and the universe clip ride behind it for our player. Then measure which format our CDF 9/7 terms translate into best: the least picture lost in the crossing, and the fewest bytes.

**What each format is inside, and how our terms would cross into it:**

| | H.264 | VP9 | AV1 |
|---|---|---|---|
| Picture blocks | 16 × 16 macroblocks; 4 × 4 and 8 × 8 integer DCT | 4 × 4 to 32 × 32; DCT and ADST | 4 × 4 to 64 × 64, rectangles too; DCT, ADST, flipped ADST and identity |
| Packing | CAVLC (simple) or CABAC | a boolean arithmetic coder with set odds | a multi-symbol arithmetic coder with learned odds |
| Prediction | 9 intra directions; motion at a quarter pixel | 10 intra directions; motion at an eighth | 56 intra directions, palette, intra block copy; motion at an eighth with warping |
| Closest to our equations | none; a straight DCT | none; DCT and ADST | **film grain synthesis**: noise rebuilt from a few parameters instead of stored, as a fill is; the identity transform keeps single-pixel points (stars) as they are |
| Containers | MP4 | WebM (Matroska) | MP4 or WebM |
| An encoder of our own, simplest valid version | smallest: intra-only frames, CAVLC, the DC predictor | middle | largest: its syntax is the longest of the three |

**The translator equation.** All three rebuild a picture as blocks of transform terms. Ours is a wavelet over the whole picture. The crossing:
1. Rebuild our picture from our terms with the inverse CDF 9/7.
2. Cut it into the format's blocks.
3. Take each block's transform: the DCT or ADST, or for AV1 the identity transform where a block holds a star.
4. Quantise with the format's own step.

The measure is the same for all three: the picture's quality after the crossing against our solved picture, and the front man's bytes for ten minutes.

**Order of work:**
1. H.264 first. Its smallest valid encoder (intra frames, CAVLC, the DC predictor) is the shortest to write, and every chat app reads it. It gives a working front man and the first numbers.
2. Then VP9.
3. Then AV1, whose film grain and identity transform are the most likely to carry the stars and the shimmer for few bytes.

Each container (MP4, WebM) also gets written by our own code.

### H.264 front man, version 0 (2026-11-08)

`src/render/H264Writer.cs`, every line ours. It writes the H.264 baseline profile: every frame an IDR of Intra16x16 macroblocks with DC prediction, CAVLC, each macroblock carrying its 16 luma DCs (one for each 4 × 4) and its chroma DCs. The MP4 boxes around it are ours too. The writer keeps the picture a decoder will rebuild, since it predicts from it.

Checked with Windows' own decoder (Windows: an allowed dependency, used only to check; `shots/rt/h264t.cs`). It decoded every frame of flat, ramp and random-patch pictures and of the filmed tour's 4K frame. Against our writer's own reconstruction: 45 to 60 dB, the same picture apart from rounding in Windows' colour conversion. Full 4K (3840 × 2024, cropped from 2032): 105 KB a frame.

Still coarse: no AC terms yet, so a 4 × 4 patch is one value. Every frame is whole, too: 10 minutes at 30 a second would be about 1.9 GB. Next: P frames (an unchanged macroblock costs about one bit), the AC terms (CAVLC's other tables), and then the comparison tiles.

### H.264 front man, version 1: change frames and the detail terms (2026-11-08)

- **Change frames.** Between whole frames (one every 30), P frames. A 16 × 16 block with a mean difference under 1.5 levels from what is shown is skipped: about a bit, and the decoder copies it. A changed block is coded as in a whole frame. The `stss` box marks the whole frames.
- **The detail terms.** Each 4 × 4 goes through H.264's integer core transform. Its DC joins the macroblock's DC set, and its other 15 terms are quantised by class and coded with CAVLC. The coeff_token table is picked by nC from the counts of the blocks to the left and above, kept for every block exactly as a decoder keeps them. All four coeff_token tables, the total_zeros and run_before tables, and the level coding are in use. Chroma is still DC only.
- **Checked with Windows' own decoder:** random-patch pictures, weak and strong, at two quality settings, and the filmed 4K frames as a whole frame, an unchanged one and a changed one. Every frame decoded, 50 to 60 dB against our writer's reconstruction: the same picture.

### The front man compared, side by side (2026-11-08)

View > Front man compared (`src/shell/FrontmanCompare.cs`). Over the universe, live, it shows three tiles:
- the frame as drawn, taken at about 960 wide;
- the same frame through the `.cdf97w`'s own sums (dyadic CDF 9/7, steps by subband, our coder) as a whole frame, solved back;
- the same frame through our own H.264 writer as a whole frame, its own reconstruction: exactly what a chat app's player shows.

Each tile carries its bytes for the frame and its quality against the frame as drawn. A right-click keeps a tile, with a green frame, remembered in the settings. It refreshes every three seconds, worked out on a thread of its own, and is never drawn into a clip. VP9 and AV1 tiles join as their writers are built.

**H15 step 2 built (2026-11-08): playback.** `src/render/UniverseClip.cs`. The reader takes back the header, the seed and every record. The player:
- holds its clock while the universe is still being made round the camera (loading, or a large ground backlog);
- applies each recorded scene call to the matter view by name with its exact arguments;
- sets the camera point before and after the universe's own update, so nothing in the update moves it;
- checks the galaxy, star system and ground made against the ones recorded, putting any mismatch in the title and the run log.

The title shows the clip's progress.

**H15 step 3 built: the handover.** The installer registers `.cdf97w` to the program. Double-clicked, a clip of the universe itself plays straight away, made again from its seed. A waveform clip plays in the program's own player, then the start page opens. The start page's Play does the same.

