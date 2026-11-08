# Uni Vs Matics — ARCHITECTURE

> Mathematical analysis + template engine that converts a hand-drawn fractal gallery into
> **equations, not image files**, and serves a static site that reconstructs, morphs, and
> sonifies them. Source art: a schizophrenic artist with photographic memory who recalled
> their hallucinations and drew them by hand (deviantart.com/gfourteen/gallery).
> Live: <https://univsmatics.git.unityailab.com>.

This document is the canonical structural map. Pipeline mechanics in the workflow live in
`.claude/WORKFLOW.md`; task history in `TODO.md`; session handoff in `RESUME.md`.

---

## 1 · Repo layout

```
ftcore/             analysis + reconstruction engine (pure Python: OpenCV + NumPy + SciPy)
  __init__.py
  config.py         all thresholds + grid sizes (orient_grid=48, lumen_*_grid=48, max_dim=1024 detection)
  imageio.py        image load/guards; load_image (bounded) + load_full_rgb (NATIVE-res for reconstruction)
  detect.py         reject ladder — fog/blur, TV-snow static, text/emoji dominance
  analyze.py        vectors, colour (palette + GMM + spatial colour_field), lumen, waveform
  generative.py     IFS / fractal-flame collage fit + box-counting dimension
  reconstruct.py    CDF 9/7 wavelet (lifting) analyze/reconstruct + dv1 position codec + caption removal
  template.py       assembles the 3-form template + symbolic equations + feature vector
  uniqueness.py     corpus-level cosine dedup (drop near-duplicate equation signatures)
run.py              CLI: corpus → templates (multiprocessing); loads NATIVE rgb for reconstruction
build_web.py        templates → web/data/ bundle (dv1 coeffs, webp thumbs, motion DNA, equations index)
web/                the static site (HTML/CSS/JS + JSON data) — see §4
screenshots.mjs     Playwright capture → social og:images
docs/               THIS doc + SKILL_TREE.md + ROADMAP.md (canonical project docs)
TODO.md             task list (original spec captured verbatim, LAW #0)
README.md           run/build/deploy workflow
RESUME.md           session handoff
.forgejo/workflows/deploy.yml   push-to-main → rsync web/ to the pages host
```

---

## 2 · The analysis pipeline (`run.py` → `ftcore/`)

Each image flows through a conservative reject ladder, then full extraction, then assembly:

1. **Load & guard** (`imageio.py`) — raster only; animated gif/apng/webp + non-images skipped.
   Detection runs at a bounded resolution (`config.max_dim`, 1024 px long edge); reconstruction
   runs on the TRUE native rgb via `load_full_rgb` (do not regress this — native detail is the point).
2. **Fog / blur reject** (`detect.py`) — Laplacian variance + Canny edge density + high-frequency
   spectral energy. Smooth, vector-less images dropped.
3. **TV-snow static reject** (`detect.py`) — luminance entropy + spectral flatness + lag-1
   autocorrelation. White-noise "snow" dropped.
4. **Vector extraction** (`analyze.py`) — unsharp mask → Canny → probabilistic Hough → colinear
   "same-trajectory" decimation, plus a structure-tensor orientation field.
5. **Text / emoji reject** (`detect.py`) — MSER glyph-row geometry with a **dominance gate**: only a
   text-FILLED frame is rejected; strong line-vector structure vetoes the reject, so a small title
   caption never disqualifies vectored art.
6. **Colour / lumen / waveform** (`analyze.py`) — palette + per-channel GMMs + a spatial
   `color_field` (Gc=32 RGB grid for the world engine); luminance density grid; lumen-degradation
   gradient field; morph-allotment gradients; radial/angular FFT spectra + power-law β.
7. **Generative IFS** (`generative.py`) — self-similarity collage fit → affine maps + weights +
   variation tag + box-counting dimension.
8. **Quality gate** — intricacy score (vector richness, edge density, spectral structure, luminance
   range) must clear a floor.
9. **Corpus uniqueness** (`uniqueness.py`) — feature-vector cosine dedup; the highest-intricacy
   representative of a near-duplicate cluster is kept.
10. **Reconstruction** (`reconstruct.py`) — forward CDF 9/7 on the native rgb, energy-target
    coefficient selection (per-image term counts), Int16 quantization + dv1 position packing.
11. **Assembly** (`template.py`) — three forms: structured attributes + symbolic equations +
    generative IFS, plus the feature vector and the reconstruction object.

Output: `out_full/templates/*.json` + `*.eq.txt`, `out_full/rejected.json`, `out_full/summary.json`.

---

## 3 · The reconstruction format (the equations)

The primary equation per image, per YCbCr channel:

```
channel(x,y) = Σ_k c_k · ψ_k(x − x_k, y − y_k)
```

- **ψ = separable CDF 9/7 biorthogonal wavelet** (the JPEG2000 wavelet), applied via the **lifting
  scheme** over multiple dyadic levels. Constants: A97=−1.586134342059924, B97=−0.052980118572961,
  G97=0.882911075530934, D97=0.443506852043971, K97=1.230174104914001. Forward = rows then cols;
  inverse (`idwt2`) = cols then rows; level rule: halve while size ≥ 4 and even.
- **Padding** is to a **multiple of 64** (NOT a power of two) — keeps the grid tight and roughly halves
  native-res cost vs pow2.
- **Coefficient selection** is an **energy target** (relative L2 tolerance `TOL`), NOT a hard cap — so a
  dense image genuinely carries more terms than a sparse one (~460k … ~4M here). `KMAX` is set
  non-binding; TOL drives per-image counts. (Historical bug: a binding KMAX once made every count
  identical — keep it non-binding.)
- **Encoding** (`pos_enc: "dv1"`): positions are sorted → deltas → **LEB128 varint** (~1 byte each,
  ~half the payload); values are **Int16** quantized × a per-channel `qscale`; both base64. Never pixels.
- **Colour** carried exactly in YCbCr; reconstruction object is **byte/pixel-identical** between
  Python (`reconstruct.py`) and JS (`assets/reconstruct.js`) — verified by position-SHA match + 0.0
  maxdiff. The browser runs the matching 9/7 inverse to rebuild at native resolution + aspect.
- **Caption removal** (`remove_caption`): bottom-band-only, tight no-smear lift. The hand-drawn title
  is KEPT faithful by decision; aggressive border removal (which smeared) is NOT used.

---

## 4 · The static site (`web/`)

Pure static (HTML/CSS/JS + JSON), `fetch()`es its data, must be served over HTTP.

| File | Role |
|------|------|
| `index.html` | landing — corpus stats, source-art provenance, feature grid, **+ the flight portal (#273)** (see below) |
| `browser.html` | template library — searchable/sortable grid; card → `viewer.html?id=` |
| `viewer.html` | **exact-copy engine** — native dv1 reconstruction, deviation dial, pan/zoom, full-equation download, **math-layer still**, **prev/next + ←/→ scroll through all pieces**, live-morph, sonify |
| `video.html` | **world engine ("the 3D engine")** — flat **layered math compositor**; transport, filters, PNG/GIF/webm capture, playlist/loop/shuffle, scroll zoom-fractalize, **+ a drop portal (#263): drop your own .gif/.mp4/image/sound → it equationalizes (`fromAnyMotion`) and morphs IN the compositor via `Layers3D.loadRecs`** |
| `videocombiner.html` | **Eq Video** — the motion portal: drop **mp4/webm/mov · animated .gif · image · sound · .uvmv** → per-frame equations (video/gif) or a zoom-dive (image/sound); ▶ play; ⤓ **.uvmv / .webm / .mp4 / .gif**; the four **answers** (image/original-sound/equation-music/sheet); sound-track toggle (#263) |
| `equations.html` | the canonical equation forms + every image's actual equations (MathJax) |
| `uploads.html` | **in-browser equationalizer** — upload → forward 9/7 client-side → IndexedDB → reconstruct/morph/sonify |
| `workflow.html` | public end-to-end pipeline doc |
| `thesis.html` | the thesis (one claim — picture/sound/clip are three faces of one equation, modality-independent, classical-maths-not-AI) + an honest falsification log of what was tried and rejected |
| `docs.html` | full docs + wiki/glossary |
| `assets/common.js` | nav (**13-link**, mobile hamburger ☰ via `.navtoggle`) + footer + **per-page tailored TL;DR** + canvas right-click/drag blockers + `FT` helpers + **`FT.VERSION` cache-bust** + mobile/no-GPU `FT.NO_ENGINE` gate + **injects the site-wide 18+ gate** (`age-verification.js`, #274 — see below) |
| `assets/age-verification.js` | **site-wide 18+ gate (#274)** — Unity AI Lab age + ToS/Privacy acceptance, persisted in `localStorage` (once per browser), injected from `common.js` so every page inherits it with no per-page edits. Self-contained (own styles, full-viewport overlay). Decline-age or close-legal → kicked to google.com; legal-version bump → re-prompt for legal only. Legal links → `https://www.unityailab.com/terms` + `/privacy` |
| `assets/reconstruct.js` | shared decoder — `idwt2`, `decPos`/`decodePositions` (dv1), `reconstructImageData`, `ycbcrToRGB` |
| `assets/reconstruct-worker.js` | Web Worker — runs the heavy `idwt2` reconstruction OFF the main thread (importScripts reconstruct.js → transferable RGBA) so audio + animation never freeze while a piece loads (kills the per-image pauses) |
| (zoom-fractalize, mode E) | The **∞ zoom-fractalize** (viewer + video + combiner): scroll zooms the REAL reconstruction; past native detail the flat regions **fractalize** in the piece's own equation via `FT_COMBINER.fractalize` (`combiner.js`) — complex Newton z³−K → 3D cubic x³−2y³−z³=K continuation, Worley + fBm, Sobel-masked + additive `lighter` so detail grows OUT of the image's own edges/vectors. The ∞ button is **reset view**. (Replaced the rejected `ifs3d.js` "Innerspace 3D fly-through" + the earlier `ifszoom.js`, both removed per Phase 9.9.) |
| `assets/layers3d.js` | `Layers3D` (aliased `World3D`) — flat layered math compositor; **reconstructs each piece live from its equation (`reconstruct.js`) + derives its math layers in-browser** (no stored layer images); geometry locked; FULL effects palette (all blend modes + hue/blur/brightness/sepia/invert/grayscale filters + per-layer toggles + `evolve` build/collapse); capture/GIF/record. Supersedes the removed `world.js`/`world3d.js` |
| `assets/equationalize.js` | forward CDF 9/7 (machine-exact inverse-pair of the decoder) + dv1 encode |
| `assets/sonify.js` | **master music equation** — `Sonifier`; pure function of the wavelet coefficients (band→octave, wide range, no scales/tunes). `play()` one-shot (stills/uploads) + `startStream()` continuous crossfading scheduler (video, no pauses). Formulas on equations.html form 9 |
| `assets/eqvideo.js` | **equational video + motion** (`EQVIDEO`, #263) — `.uvmv` pack/unpack; `fromVideo`/`fromGif` (per-frame equations); `fromSound`/`fromImage`/`_zoomDiveFrames` (a still/sound → a zoom-dive video); `fromAnyMotion` (one router for uvmv/gif/video/image/sound); `play`; exporters `toWebm`/`toMp4` (MediaRecorder H.264 + .webm fallback)/`toGif` (gifenc); `answer(pack,kind)` (image/png/sound/eqmusic/frames/sheet) |
| `assets/gifenc.js` | self-contained animated **GIF89a encoder** (`encodeGIF`) — median-cut palette + LZW; equations → `.gif` |
| `assets/gifdec.js` | self-contained animated **GIF89a decoder** (`GIFDEC`, #263) — WebCodecs `ImageDecoder` fast-path + a pure-JS LZW/disposal parser fallback; every composited frame → equationalizable |
| `data/` | the bundle (see §5) |

### Home-page flight portal (`index.html`, #273)

The landing page's hero demo of the whole pitch — **image → 3D-equation render → infinite zoom-fractalize** — in
one self-contained block. `web/samples/unity-born.mp4` ("Unity Born", ~16 MB) is **equationalized in-browser** on
click-to-enter (a mid clip frame → `equationalizeImageData` → reconstruct), then rendered as **ONE equation plane**
inside a CSS `preserve-3d` 3D frame (`#flightworld` `transform-style:preserve-3d` + `perspective:760px`).

- **Idle = a screensaver.** A continuous **full-360 tumble on all axes** (rx/ry/rz drift freely — no clamp, no gimbal
  lock) plus an auto **dive → hover → re-fall** cycle that flies INTO the equation's fractal depth:
  past zoom ×3 it calls `FT_COMBINER.fractalize` (the viewer's grow-off-vectors `n3` math) so detail keeps
  generating with no bottom-out, hovers when full (`FILL`/`HOVER`), then falls back and dives again. Kicks in after
  ~45 idle frames.
- **User takes the stick.** Scroll = dive in/out (throttle on `contentZoom`); drag / **W·S·A·D·Q·E** = spin in 3D
  (full 360, no plane/gimbal lock); **R / double-click / ⟲ button** = snap home; Esc releases. Let go and the screensaver resumes.
- **Click-to-enter** so the 16 MB mp4 is `preload="none"` and never auto-downloads — it loads + equationalizes only
  when the visitor enters. **Mobile / no-GPU** (`FT.NO_ENGINE`) falls back to a flat tap-to-play `<video>` (no engine).
- Engine stack loaded for it: `reconstruct.js` + `equationalize.js` + `combiner.js` (+ the rest) on the page directly.

### Two-engine architecture (settled)

- **Exact-copy engine** (`viewer.html` + `reconstruct.js`) — rebuilds the ACTUAL image faithfully from
  its 9/7 equations at native resolution. Deviation dial drops weakest terms (precise → abstract).
- **World engine** (`video.html` + `layers3d.js`) — a flat **layered math compositor**. It **reconstructs
  each piece live from its equation in the browser** (`reconstruct.js` idwt2) and **derives the math layers
  on the fly** from that one reconstruction via cheap canvas ops — recon (faithful base), base glow (blur),
  mid structure, colour-field wash, lumen, edges (Sobel), fine spark (high-freq difference). **No rendered
  layer images are stored on the server** (Gee: "no jpgs and pngs on server — use the engine"). The engine
  stacks the layers flat with their blend modes, geometry **locked to the frame edges** (no drift, no sway,
  no camera; portrait pieces rotated 90° + cover-cropped), and cross-fades piece→piece. Not a slideshow, not
  a camera around a 3D plane. The viewer's **math-layer still** is this same compositor pinned to one piece.
  (Supersedes the removed particle `world.js` and WebGL-displaced-plane `world3d.js`.)

### Effects palette (compositor)

The world engine exposes the FULL compositing palette (Gee: "use all the filters and mixers"): every canvas
blend mode (`Layers3D.BLEND_MODES`), the CSS filter set folded into every layer (`_globalFilter`: saturate,
contrast, brightness, hue-rotate, blur, sepia, invert, grayscale), per-layer on/off toggles
(`setLayerEnabled`, `Layers3D.LAYER_NAMES`), a global blend-mode override, and an **evolve** control that
deepens each enricher's oscillation into a build/collapse cycle and cycles hue over time — so the world
"populates, builds, collapses, merges and marries" (#41-44) WITHOUT any geometry movement (geometry stays
locked per #105). `Layers3D.WEIGHT` keeps recon dominant so enrichment never buries the faithful base.

### Master music equation (`sonify.js`)

Sound is **not a drone and not a pre-set tune** (#112/#113/#119/#122). `Sonifier._notesFor(rec)` is a PURE
FUNCTION of the image's luma wavelet coefficients — **no scale, key, melody or rhythm added ahead of time**.
Each coefficient's wavelet **band** (dyadic level) → octave (coarse=bass, fine=treble) so the register is
**wide straight from the maths** (~40Hz–3.2kHz); strongest few **per band** are voiced so every octave speaks
and each image differs; magnitude → loudness; image scan order → the melody. Full formulas are published on
`equations.html` (form 9) for recreation. Two playback paths: `play(rec)` (one-shot, viewer stills + uploads)
and **`startStream(provider)`** — a continuous look-ahead scheduler that crossfades the current piece's
note-set into the next by the live morph amount, so in the video the music **morphs/merges with the images
with no pauses** (#120). One plug-in for ALL pieces.

### Combiner — sound ⇄ image (`combiner.js` / `uvm.js` / `eqsound.js`)

One shared coefficient field **C = {(rₖ,cₖ,vₖ)}** is the hub; every modality is a transform of it
(`Φ_img`/`Φ_snd`/`Φ_frc`). The **Combiner** page (`audio.html`) round-trips **both ways**:

- **`combiner.js`** (`FT_COMBINER`) — the master module: `coeffField`/`band`/`pitch`, `solveField`/`fractalize`
  (Φ_frc), **`fromAnyAudio(buf)`** (the audio extrapolator: UVME-chunk → exact dense C; equational-sound → MFSK
  demod; plain sound → additive STFT loader), **`fromAnyFile(file)`** (the ONE unifying converter — image OR
  sound → C), and **`extract(file, kind)` / `produce(rec, kind)`** (the master code that solves C for any
  representation: equation/image/png/jpg/webp/wav/sound/eqsound/fractal/**eqtxt**).
- **`.eqtxt`** — the **plain-text equation**: `produce(rec,'eqtxt')` writes `{ schema:'uvm-eqtxt@1', palette, rec }`
  as `text/plain` JSON (the field C with its base64-coded coefficients), and `fromAnyFile` re-imports any `.eqtxt`
  straight back to the exact C. Human-portable, diff-able, version-controllable round-trip → exact image/sound/3D.
  Same C as the binary `.uvme`, text-encoded instead of RIFF-chunked.
- **`uvm.js`** — the `.uvme` codec: a standard WAV + custom `UVME` RIFF chunk carrying the serialized C (CRC-32,
  schema `uvm-combiner@1`, `produces` manifest). Plays in any audio app; only our code reads the chunk.
- **`eqsound.js`** — the **equational sound**: a 16-tone MFSK modem whose *waveform itself* encodes the compact
  C (Goertzel demod, CRC), bit-exact over lossless PCM. The branded extension is **`.uvme`** (fewer collisions);
  legacy `.uvm` + `.wav` still decode (content-based, not extension-gated).
- **`additive` loader** (`fromAudio`) — derives a C from any plain sound's STFT spectrum (generated image, not exact).

Honest boundary: a byte-exact *dense* image is MB → it rides the `.uvme` data chunk (the audible music is not
invertible); the equational sound carries the *compact* field. A single file is one container (opens as `.wav`);
the polyglot is the **equation + code**, not the bytes — each `.uvme` is a "Rosetta Stone" read by `extract()`.
The world engine (`video.html`) can also mux the music **into** a recorded `.webm` (record with/without sound).

### Equational video + motion — `.uvmv` / `.gif` / `.mp4` (`eqvideo.js`, #263)

Motion is the same combiner field C, **per frame**. `EQVIDEO` extends the Rosetta to time-based media so `.gif`
and `.mp4` are first-class equational citizens, interconvertible **all directions** (sound↔gif↔mp4↔image):

- **In** — `fromVideo` (mp4/webm/mov via `<video>` seek) and `fromGif` (animated GIF → every frame via `gifdec.js`)
  equationalize each frame with the same CDF 9/7 as the stills. `fromSound`/`fromImage` turn a *still* source into
  motion by baking the infinite **zoom-dive** (the viewer's `fractalize` math, equational N³) frame-by-frame; the
  sound→video audio track is chosen by a toggle — **equation-music** (`sonify.renderPCM`) / **keep-original** / **none**.
  `fromAnyMotion(file)` is the one router every portal calls.
- **Out** — `toWebm` (VP9/Opus, universal), **`toMp4`** (in-browser H.264 via MediaRecorder where supported —
  Chrome 126+/Edge/Safari — else a clear pointer to `.webm`/the Converter's Rust-ffmpeg path), `toGif` (animated
  GIF89a via `gifenc.js`), and the `.uvmv` container (exact per-frame equation sequence + audio).
- **The "answers"** — `answer(pack, kind)` derives, from any motion file, the same four answers the still combiner
  gives: a representative **image** (.png carrying its equation), the **original sound** (if the clip had a track —
  a gif doesn't → null), the **music of the equation**, and the **equational images** (per-frame stills / a contact
  sheet). This resolves gif→sound = equation-music; mp4→sound = original track and/or equation-music.
- **Portals** — Eq Video (`videocombiner.html`) is the home; the world engine (`video.html`) gains a drop portal
  that morphs dropped motion in the compositor (`Layers3D.loadRecs`); and the still portals (Combiner/Your-equations/
  Viewer) PLAY a dropped animated gif/video on a loop instead of freezing to frame 1 (scroll/click stops the loop to
  zoom a single equation). The native Converter (`player/ui/`) gains gif in/out alongside its existing mp4 path.

Honest scope: per-frame full fields are big (v1 keeps a compact field per frame; inter-frame delta is a documented
v2). In-browser `.mp4` depends on the browser's MediaRecorder H.264 support; the Converter is the guaranteed path.

---

## 5 · The web data bundle (`build_web.py` → `web/data/`)

| Artifact | Contents |
|----------|----------|
| `index.json` | per-piece summary (id, title, thumb, quality, psnr, equation_count, palette) — drives browser + viewer nav |
| `corpus.json` | aggregate stats (counts, avg PSNR, total/avg equation terms) |
| `templates/<id>.json` | the full template incl. `reconstruction.channels.{Y,Cb,Cr}` dv1 coeffs + `symbolic_equations` |
| `equations_by_image.json` | each image name synced to its actual equations (equations.html list) |
| `thumbs/*.webp` | webp thumbnails (the only rasters shipped — list previews) |
| `version.txt` | build marker (timestamp + commit) |

**No stored layer/texture rasters.** The world engine's math layers are NOT pre-rendered or shipped — the
browser reconstructs each piece from its equation and derives the layers live (see §4). Layers per piece,
all derived from one in-browser reconstruction: **recon** (full faithful reconstruction — the base),
**base** (coarse glow = blur), **mid** (medium blur), **colour** (heavy blur + saturate wash), **lumen**
(grayscale + blur), **edges** (Sobel magnitude), **spark** (high-freq = recon − mid, difference-composite).
The compositor stacks them recon-first; enrichers stay low-weight (`Layers3D.WEIGHT`) so recon dominates.

---

## 6 · Cache-busting + versioning

All asset/data freshness is gated on a single stamp, `FT.VERSION` in `common.js` (currently
`20260626b`). `FT.json()` appends `?v=` to every data fetch, and every `<script src="assets/*.js?v=…">`
tag across all HTML pages carries the same query. **Bump it on every deploy** so a browser can never
serve a stale JS/data mix — the root cause of the Phase 4 "empty lists / Template not found / dead 3D"
(old cached pre-9/7 JS against fresh data). `web/version.txt` is the human-readable build marker.

---

## 7 · Deploy

Pure-static deploy via Forgejo Actions (`.forgejo/workflows/deploy.yml`): on push to `main`, `web/` is
rsynced to **`univsmatics.git.unityailab.com`** (lab convention `<lowercased-repo>.git.unityailab.com`,
wildcard nginx vhost). **Auth is the box's SSH key — nothing to log into.** The repo MUST live under the
`UnityAILab` org so the Action sees the org-level `PAGES_DEPLOY_KEY`/`HOST`/`USER` secrets. See
`README.md → ## Deploy` for the full mechanism + the 2026-06-22 personal-namespace-vs-org-deploy fix.

## Native Converter app (`player/`) + public binary downloads (`/dl/`)

A standalone **Tauri (Rust) desktop app** that hosts a **conversion-only** frontend (`player/ui/`) — the same
equation engine, **none** of the morph / 3D world / fractal-zoom. Full back-and-forth, offline: image ⇄ `.uvme` ⇄
sound, video ⇄ `.uvmv` ⇄ mp4; auto cross-decode on open (image + sound at once); original-track ⇄ equation-music
toggle; native Save-As; mp4 via the Rust side. The Rust shell (`player/src-tauri/`) does only native glue
(file open / Save-As / mp4 / file-associations); the equations live in the JS engine, synced in by
`player/sync-engine.mjs` (no `common.js` chrome). Icons generated by `player/make-icon.mjs` (no `cargo tauri icon`).

**Build/publish pipeline** (`.forgejo/workflows/build-converter.yml`, tag `converter-v*`): the lab's Forgejo runner
can't fetch third-party actions (`data.forgejo.org` mirror only) and its container can't raise `fs.inotify`
(=128, which `cargo tauri build`'s watcher EMFILEs on), so the job installs Rust via **rustup** and builds with
plain **`cargo build --release`** (no tauri-cli; `tauri-build` still embeds the frontend) — Linux native **+
Windows cross-compiled via mingw** (`x86_64-pc-windows-gnu`). It then **rsyncs the binaries to the PUBLIC pages
site `/dl/`** (the repo is private — login-walled — so downloads can't live there; `deploy.yml` excludes `/dl`
from `--delete`). The styled **`web/download.html`** page (nav + TL;DR + full back-and-forth + per-OS setup)
links them; `/dl/index.html` redirects there. macOS pending a runner.
