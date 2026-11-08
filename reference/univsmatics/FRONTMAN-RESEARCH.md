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
