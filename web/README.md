# The .cdf97w player for the web

A page that plays a `.cdf97w.webm` clip in a browser, or inside a Discord call as an Activity. Every line is ours: `index.html` and `player.js`.

## What it plays

- **A waveform clip** (`.cdf97w` version 8) plays as its pictures. The page unpacks each frame's wavelet terms on a worker thread, with the same range decoder and odds as the program. The graphics card then turns the terms into values, runs the inverse CDF 9/7 and converts the colour, all through WebGL 2. The weights come from the clip's own metadata. A changed frame is added onto the one before it. The clip loops.
- **A filmed `.cdf97w.webm`** carries its front man (our own VP9, in our own WebM layout), which the page plays as video. The universe's own equations ride inside it (the seed, the camera's track, the scene), and "What the clip holds" shows them.
- **An older clip of the universe itself** (`.cdf97w` version 9, no picture track) holds no pixels: just a seed, the camera's track and the scene's calls. The page plays the track against the clip's own clock: the camera's path seen from above, its scale, the galaxy, star system and ground it was in, and the scene calls as they happened. The universe itself is made again from the seed by the program, so open the file in the program to fly it as it was filmed.

## Using it

- Open a clip with the button, or drop one on the page.
- Name a clip in the address to play it straight away: `index.html?clip=clips/name.cdf97w`, with the clip served from the same server as the page.
- Space pauses and plays. The bar along the bottom seeks.
- "What the clip holds" shows the clip's metadata: what made it, when, and every number that turns its waveforms back into pixels.

It needs a browser with WebGL 2 and float render targets. Current Chrome, Edge and Firefox have both.

## As a Discord Activity

An Activity is a web page Discord shows inside a call. To put this page up as one:

1. Serve the `web/` folder over HTTPS.
2. Make an application in the Discord Developer Portal and turn on Activities for it.
3. Map the Activity's root URL to that server.
4. Put the clips to share in `clips/` beside the page, and open them with `?clip=`.

The page has not yet been run inside Discord.
