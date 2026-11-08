---
title: U9 · The tour and the captures
status: verified
sources: [content/tours/grand-tour.unt, src/shell/UniverseView.cs (TourStep), src/render/Mp4Writer.cs, src/render/ClipWriter.cs, src/render/CardClip.cs, src/render/WaveletCoder.cs, src/platform/ClipPlayer.cs, PngWriter.cs, src/platform/Launcher.cs, DNA-AND docs/DESIGN.md 282–289, 408–415, 423–424, 479, 483, 511, 529, 536–538, 571]
last-verified: 16bbc1c
---

# U9 · The tour and the captures

> "okay do a full panoramic scenic view fly by and down and into attoms like fly bys and follow an electron and then zoom in more to a proton and then into its qwerks nice and panoramic like for human viewability" · "put on ur movieaehat and make it proper dynamically and prcedural like" · "it should only make one clip as long as its recording and not a a gig file"

## The tour

A tour is a plain-text list of stops in `content/tours/` (`grand-tour.unt`, 81 stops), flown with the real controls. Each stop is `do: pick | shot | zoom | orbit | look | arc | fly | glide` with its own `caption` (`{sky}`, `{hole}`, `{horizon}`, `{supernova}` written on the spot from what is really there); a pick looks for a few seconds and skips every stop up to the next pick if it finds nothing, so a caption never tells of what is not in view. A `shot` is a framed film shot round what is picked (`to-radii`, `sweep`, `rise`, `max-rate`, `seconds`), its departure paced from what was in view, its start kept in the subject's own frame; a `zoom` runs at one steady rate in scale (`until-km`, `through`, `pan`, `out`). Every viewing is shot afresh (each shot its own way round, speed and crane). The grand tour: the web, across billions of light-years to a far galaxy with a feeding heart, down to its disc and through the horizon to the singularity and out, a supernova, a star's system, an asteroid landed on and zoomed into, a comet, a world, its land, head height and the sky, the grains, the face, the lattice, an atom, its electrons, an electron ridden, a bond crossed, the nucleus, a proton, a quark, and all the way back out to the web. About a quarter of an hour. A click, the wheel or a key pauses it with a note saying why; T carries on, R starts again. The stops and picks are logged to `%TEMP%\universe_zoom.log`.

## The captures

| | How it works | Where |
|---|---|---|
| Screenshots | F12, PNG, in `captures` beside the program | `PngWriter.cs` |
| The tour's end | Its last caption ends with a true figure: the places the tour went to against every galaxy, star, world and moon there is, as a percentage written out in full (`{seen}`) | `UniverseView.SeenWords` |
| The clip's size and text | Clip size on the landing page (beside Film the tour) or under View: auto, full, half, quarter. In a clip smaller than 1080 high every letter and card is drawn larger, so each letter cell is at least two of the clip's pixels, as on a 1080 screen | `Launcher`, `ScaleHud.TextScale`, `Program` |
| Clips | F9 or Film the tour on the landing page, in the format picked on the landing page or under View > Clip format. MP4: H.264 through Media Foundation, every other frame at 30 a second, rate control by quality (black space costs nothing). .cdf97w: our own format, the frame worked out on the graphics card inside the frame (dyadic CDF 9/7, 6 levels, steps weighted by subband, half above 1920 wide), only the terms read back, packed by our own range coder, whole every 30th frame and changes between, a metadata block carrying the waveforms-to-pixels equations. A REC notice on screen but off the tape; the clip's size from View > Clip size (auto, full, half, quarter). A filmed tour also leaves a .cdf97u (its seed and tour) | `Mp4Writer.cs`, `ClipWriter.cs`, `CardClip.cs`, `WaveletCoder.cs` |
| Playback | The landing page's Clips list plays them back: an MP4 in Windows' player, a .cdf97w in the program's own player (the changes added back, the dyadic inverse, the colour), a .cdf97u by flying its tour again through the universe made from its seed; Open folder shows where they are | `Launcher.cs`, `ClipPlayer.cs` |
| The readme's clip | Only from a take the creator approves; its highlights cut into `docs/img/journey.gif` and the social images | U12 |

See also: [U6 Universe](u06-universe.md) · [Build your own universe](../../docs/GUIDE.md#write-a-tour)
