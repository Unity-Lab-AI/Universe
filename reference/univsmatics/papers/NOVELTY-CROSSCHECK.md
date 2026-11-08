# Uni Vs Matics — novelty cross-check (light, NOT peer review)

> Requested by Gee: of the equations the artist **specified** and what we **did** with them, what might be
> **new**? This is a knowledgeable + light **online** cross-check — **not exhaustive, not a peer review.**
> Honest bottom line: the building blocks are all **established prior art** (and credited in the site's
> provenance table). What *may* be novel is the **specific combination** + one coupling.

## The four pieces, cross-checked

| Piece | Verdict | What the check found |
|------|---------|----------------------|
| **Shared wavelet field → image + sound** (the combiner) | **Prior art** | A 2026 preprint, *"Wavelet as Tokenizer: a Shared Wavelet Token Schema for Natural Signals"*, uses a shared wavelet-coefficient representation across audio/image/video. Multimodal wavelet fusion is an active field (WIFE-Fusion, MFFNet). Ours is **non-ML, exact-reconstruction**, and adds **sound + fractal** — a distinct *system*, but the core idea (shared wavelet representation across modalities) is **not new**. |
| **`.uvme` = a WAV that plays AND rebuilds the image** | **Not novel** | Standard **RIFF** behaviour: conforming readers ignore unknown chunks, so a custom chunk carrying the image rides inside a playable WAV. WAV even has a standardised **EXIF/LIST** path for embedded imagery. This is a known technique. |
| **Equational sound** (image ⇄ playable audio tones) | **Not novel in mechanism** | This is essentially **SSTV** (Slow-Scan TV) — a decades-old ham-radio standard that encodes images as FSK audio tones and FFT-decodes them — plus general audio-modem tools (minimodem, GGWave). Our twist is **modulating the wavelet coefficient field** (MFSK) rather than raw scanlines; a *variation* on an old mechanism, not a new one. |
| **Mode E — Newton fractal driven by the image's own wavelet coefficients, zoom-synced** | **Not found in a light search → plausibly original (unconfirmed)** | The parts exist (Newton fractals; fractal infinite-zoom is actively researched, e.g. chaotic differentiable point-splatting). But the **specific coupling — wavelet coefficients → Newton/cubic parameters → detail synced to zoom depth** — did not surface in the search. A search result explicitly called it "a genuinely original synthesis," but **a light search is not proof.** |

## Honest verdict

- Individually, **everything is established prior art** (shared-wavelet-multimodal; RIFF embed-image; SSTV; Newton fractals) — all credited on the site (`docs.html#provenance`).
- What *may* be genuinely new:
  1. **Mode E's coupling** — driving a Newton/cubic fractal's parameters from the image's own wavelet field, with detail synced to zoom depth.
  2. **The unified single deterministic artifact** — one wavelet field `C` that is, at once, an **exact image**, an **invertible sound**, and an **image-driven fractal**, read into any of them by one master `extract()` (the `.uvme` "Rosetta Stone").
- **We make no firm novelty claim.** This is a cross-check against general knowledge + a light web search, not a literature review.

## Sources (light web check)
- Wavelet as Tokenizer (shared wavelet schema, 2026) — https://arxiv.org/html/2606.02631
- WAV / RIFF (embedded chunks, ignore-unknown) — https://en.wikipedia.org/wiki/WAV · http://soundfile.sapp.org/doc/WaveFormat/ · https://exiftool.org/TagNames/RIFF.html
- SSTV (image ⇄ audio tones) — https://github.com/dotStart/go-sstv · https://sstv.bashige.com/
- Learning Image Fractals (infinite-zoom detail) — https://arxiv.org/pdf/2502.17230
- Newton fractal — https://www.fractal.garden/newton-fractal
- USPTO: embedding data into wavelet coefficients of an image — https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/7171017

---

## Email draft (to send — no automated mail was available)

```
To:      contact@unityailab.com
Subject: Uni Vs Matics — novelty cross-check + "The sky is NOT the limit" (TM, application pending)

Team,

Light novelty cross-check (NOT peer review) on the Uni Vs Matics equations + what we built:

- Shared wavelet field -> image+sound (combiner): PRIOR ART (e.g. "Wavelet as Tokenizer",
  arXiv 2606.02631; multimodal wavelet fusion). Ours is non-ML/exact + adds sound+fractal.
- .uvme (WAV that plays AND rebuilds the image): NOT NOVEL — standard RIFF unknown-chunk behaviour
  (EXIF/LIST even standardises embedded images in WAV).
- Equational sound (image <-> playable tones): NOT NOVEL in mechanism — this is SSTV / audio modems.
  Our twist: modulating the wavelet field rather than scanlines.
- Mode E (Newton fractal driven by the image's own wavelet coefficients, zoom-synced): NOT FOUND in a
  light search -> plausibly original, UNCONFIRMED.

Verdict: building blocks are established/credited; what may be new is (a) mode-E's coupling and (b) the
unified single deterministic artifact (one wavelet field = exact image + invertible sound + image-driven
fractal via one extract — the .uvme "Rosetta Stone"). No firm novelty claim.

Trademark: "The sky is NOT the limit" — claimed mark (TM), application pending.

Invitation published on the site: anyone with prior, peer-reviewed work on these should email
contact@unityailab.com and we will reference it.

Full writeup: docs/NOVELTY-CROSSCHECK.md in the repo; published at /highlights.html (Notes section).
```
