---
title: U4 · Renderer
status: verified
sources: [src/render/D3D12.cs, Com.cs, Gpu.cs, Pipeline.cs, MeshPipeline.cs, Draw.cs, GpuLog.cs, PngWriter.cs, GifWriter.cs, Mp4Writer.cs, CardClip.cs, src/shell/UniverseView.cs, ScaleHud.cs]
last-verified: 16bbc1c
---

# U4 · Renderer

> "no handoffs, that is the shit zoom staging i was talking about" · "and it better be rela infinate zoom in the code so if its not fix it" · "nothing should run on cpu unless absolutely necessary"

## Built

| Part | How it works | Where |
|---|---|---|
| Direct3D 12 by hand | The device, swap chain, fences, a frame in flight, timestamps for the card's time, the DRED crash record naming the draw the card died on; buffers freed eight frames late | `D3D12.cs`, `Com.cs`, `Gpu.cs`, `GpuLog.cs` |
| Points | Glowing or faceted points for galaxies, stars, gas, belts, rings, the web's light sifted per point on the card, the black hole's frame (the shadow by the law, the disc bent, beaming, the stream of a torn star) | `Pipeline.cs`, `Draw.cs` |
| Lit meshes | Triangles lit by one star, flat or smooth; the ground's sand per pixel on the card; a grain's face with its detail, terraces and stains per pixel in double precision; minerals' lustre and clarity; greys below light's wavelength | `MeshPipeline.cs` |
| Depth | Reversed float depth (near at 1, far at 0), so near and far never fight from a micrometre to across a galaxy | `Gpu.cs` |
| One camera | An anchor plus an offset in double; the wheel flies toward what you point at by a share of the way to the nearest thing, so it slows by itself and never ends; inward there is no floor; outward a horizon that moves with you; nothing switches views | `UniverseView.cs` |
| The scale and the cards | A bar of a round length in the program's own letters; one card at a time, short lines, between the tour's bars; the pick's ring | `ScaleHud.cs` |
| GPU use and render scale | The card's time measured each frame and the detail turned to hold the share chosen (10 to 90%); drawing at 50 to 200% of the window | `Gpu.cs`, `Window.cs`, `Program.cs` |
| The strongest card | At start the card with the most video memory of its own is taken; small cards start at a lower render scale | `Gpu.PickAdapter` |
| Captures | PNG (chunks, filters, checksums), GIF (palette, LZW), MP4 (Media Foundation, 30 a second, rate control by quality, the clip's size against the screen), .cdf97w (our own: the frame split, the dyadic CDF 9/7 and the quantising in compute shaders inside the frame's own command list, only the terms read back a frame later) | `PngWriter.cs`, `GifWriter.cs`, `Mp4Writer.cs`, `CardClip.cs` |

Display safety: windowed only, vsync, no display change, a watchdog on timed runs.

See also: [U6 Universe](u06-universe.md) · [U7 Worlds](u07-worlds.md) · [U8 Matter](u08-matter.md)
