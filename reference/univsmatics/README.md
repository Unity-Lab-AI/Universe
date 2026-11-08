# Uni Vs Matics reference — the equations and builders behind the `.c97` clip

This folder carries the maths and the working code the Universe program's equational clip format is built from.
Everything here was lifted verbatim from the lab's Uni Vs Matics project (`fractal_templater`, commit d7a950e,
2026-06-27) so the port can be checked line by line against the source. Nothing in this folder is compiled into the
program; `build.cmd` only compiles `src\*.cs`. The port itself lives in `src\` once written.

## What is here

| Path | What it is | Why it is here |
|------|------------|----------------|
| `engine/equationalize.js` | The builder. Picture → sparse CDF 9/7 wavelet field: forward lifting, energy-target term selection, Int16 quantisation, LEB128 delta positions. 131 lines. | The exact forward transform and selection rule the clip writer must reproduce. |
| `engine/reconstruct.js` | The player's maths. Field → picture: inverse lifting, dequantise, YCbCr → RGB, crop. Holds the five lifting constants. 98 lines. | The exact inverse the clip player must reproduce, in a compute shader. |
| `engine/uvm.js` | Their still-image container: a WAV with the field in a `UVME` RIFF chunk, plus the PNG `tEXt` carrier. | Shows how they package a field and checksum it. We do not package ours in a WAV or a PNG. |
| `engine/eqvideo.js` | Their motion engine: frame sampling, the `.uvmv` container, playback with crossfade, webm/mp4/gif export, zoom-dive. 308 lines. | The frame-sequence shape and container layout we simplify. The sound, zoom-dive, gif and export paths are not ours. |
| `papers/equations.html` | The written equation reference: the canonical forms, the combiner equation, the nine derived equations, the reject ladder. | The maths in words and symbols. Equation 0 (image reconstruction) is the one the clip uses. |
| `papers/thesis.html` | The positive claim, the falsification log, what survived, the method. | What was tested and what broke, so we do not retest it. |
| `papers/docs.html` | The whole site documentation page: every file type, every direction, honest scope notes. | Says plainly what plays where (see "What the files actually do" below). |
| `papers/ARCHITECTURE.md` | Their project architecture. | Module map of the source the engine files came from. |
| `papers/NOVELTY-CROSSCHECK.md` | What of theirs is standard and what is new. | The CDF 9/7 wavelet, the lifting scheme and the YCbCr split are standard (JPEG 2000 uses the same wavelet). The combination and the carriers are theirs. |

## What the files actually do (from their own documentation)

- A `.uvme` is a real WAV. Any audio program plays its **sound**. The picture lives in an extra `UVME` chunk that only their page and their Converter read. Media players skip the chunk.
- A `.uvmv` is their own container of per-frame fields. Only their page and Converter open it.
- Their "plays everywhere" files are the exported `.webm` and `.mp4`, made by rebuilding each frame and recording it with the browser's own H.264 or VP9 encoder. Their `docs.html`: "The .webm/.gif play everywhere; the .uvmv is the exact-equation file."
- A media player runs only the decoders it was built with. No file can carry an equation a player will execute. So the `.c97` is opened by the Universe program itself, and the readme on GitHub links the file rather than embedding it.

## The maths, step by step (from `equationalize.js` and `reconstruct.js`)

**Constants** (CDF 9/7 lifting, `reconstruct.js` line 8):

```
A = -1.586134342059924
B = -0.052980118572961
G =  0.882911075530934
D =  0.443506852043971
K =  1.230174104914001
```

**1. Pad.** Width and height are each rounded up to a multiple of 64. The pad is filled by reflection: sample `i` maps to `i` while `i < n`, else to `2(n-1) - i`, clamped at 0.

**2. Colour.** Each pixel goes RGB (0..1) → YCbCr with the JPEG weights, Cb and Cr offset by 0.5:

```
Y  = 0.299 R + 0.587 G + 0.114 B
Cb = 0.5 - 0.168736 R - 0.331264 G + 0.5 B
Cr = 0.5 + 0.5 R - 0.418688 G - 0.081312 B
```

**3. Forward transform**, one plane at a time, rows first then columns. Each row or column of length N is run through every level: level sizes are N, N/2, N/4 … while the size is at least 4 and even. At each level, on the `size` leading samples:

```
t[size-1] += 2A t[size-2];   odd i: t[i] += A (t[i-1] + t[i+1])
even i ≥ 2: t[i] += B (t[i-1] + t[i+1]);   t[0] += 2B t[1]
t[size-1] += 2G t[size-2];   odd i: t[i] += G (t[i-1] + t[i+1])
even i ≥ 2: t[i] += D (t[i-1] + t[i+1]);   t[0] += 2D t[1]
even i: t[i] /= K;   odd i: t[i] *= K
deinterleave: evens → first half (lowpass), odds → second half (highpass)
```

**4. Term selection** (equation 0 of `equations.html`, "energy target"). Per plane: sort coefficients by |c| descending, keep the largest until (1 - tol²) of the total squared energy is captured, then keep at least `kmin`. Defaults `tol = [0.015, 0.022, 0.022]` and `kmin = [800, 400, 400]` for Y, Cb, Cr. A tighter `tol` keeps more terms. The tour-clip probes in this project used `[0.004, 0.008, 0.008]` to hold a star field.

**5. Quantise.** `qscale = max|c| / 32000`; each kept value is `round(c / qscale)` clamped to ±32767, stored Int16.

**6. Positions.** Kept positions (row-major index into the padded plane) sorted ascending, stored as LEB128 varints of the deltas between neighbours.

**7. Inverse** (`reconstruct.js`): scatter each value × qscale into a zeroed padded plane, then run the inverse lifting, columns first then rows, levels from the smallest size up:

```
interleave: first half → evens, second half → odds
even i: t[i] *= K;   odd i: t[i] /= K
t[0] -= 2D t[1];   even i ≥ 2: t[i] -= D (t[i-1] + t[i+1])
odd i: t[i] -= G (t[i-1] + t[i+1]);   t[size-1] -= 2G t[size-2]
t[0] -= 2B t[1];   even i ≥ 2: t[i] -= B (t[i-1] + t[i+1])
odd i: t[i] -= A (t[i-1] + t[i+1]);   t[size-1] -= 2A t[size-2]
```

Then YCbCr → RGB (`R = Y + 1.402 (Cr - 0.5)`, `G = Y - 0.344136 (Cb - 0.5) - 0.714136 (Cr - 0.5)`, `B = Y + 1.772 (Cb - 0.5)`), clamp to 0..255, crop the padded plane to the real width and height.

## Their frame record and container (what we simplify)

A frame in `eqvideo.js` is the JSON object `equationalizeImageData` returns: model name, colour space, wavelet name, width, height, padded width and height, and for each of Y, Cb, Cr the count, `qscale`, positions as base64 and values as base64. The `.uvmv` is the four bytes `UVMV`, a JSON header (schema, fps, width, height, sample rate, frame count, palette), one length-prefixed JSON frame after another, then `AUDI` and Int16 PCM.

JSON plus base64 costs about a third more than the bytes it carries. The `.c97` carries the bytes.

## The `.c97` clip — what the Universe program writes and plays

The brief: a small clip of pictures only. No sound, no zoom-dive, no palette, no original riding alongside, no WAV or PNG wrapper, no JSON. One new file type nobody else uses. (The `.c97` name is the working name; `.eqv` belongs to IDL's equation viewer and `.uvv` to UltraViolet video, so neither is free.)

Proposed layout, little-endian, every number binary:

```
'C97W'            4 bytes   magic
u16 version       1
u16 width, u16 height          the real picture size
u16 pad_w, u16 pad_h           multiples of 64
f32 fps
u32 frame_count
f32 tol[3]        the Y, Cb, Cr tolerances the writer used (so a reader can report fidelity)
per frame, per plane (Y, Cb, Cr):
  u32 keep
  f32 qscale
  u32 pos_bytes
  pos_bytes × u8    LEB128 deltas of the sorted positions
  keep × i16        the quantised values
```

Size per frame is the kept term count × about 3.3 bytes (one to three position bytes plus two value bytes). At 480 wide and the default tolerances the tour clip kept about 20,000 terms a frame, so about 66 KB a frame, against 90 KB a frame in their JSON `.uvmv` and 23 KB a frame in the recorded MP4. Whether the file comes out small depends on how few terms a frame needs, not on the container. Dark space with points of light keeps few terms. A full planet face keeps many.

**Writer, in the program.** The clip pipeline already captures frames for the recorder. For the `.c97` each captured frame runs steps 1 to 6 and appends to the file. Per the lab's GPU-first rule the forward lifting runs in a compute shader (rows pass, columns pass, per level), the energy sort and the packing on the processor since they are per-frame bookkeeping, not pixel work.

**Player, in the program.** No separate program. File → Open clip in the Universe program reads the header, and each frame's planes are scattered into a padded texture and run through the inverse lifting in a compute shader, then YCbCr → RGB in the present pass. Playback timing uses `fps` from the header. The same drop-down that picks the clip format on the landing page and in the View menu offers `.c97`.

**What makes it a tenth.** Their documentation calls keyframe-plus-delta the documented v2 and never built it. A clip where most frames differ little from the last can store only the changed terms. That is the one path to a file far smaller than an MP4 at the same width; a full field every frame is not. It is not built yet on either side.
