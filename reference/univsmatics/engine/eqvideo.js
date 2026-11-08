/* eqvideo.js — EQUATIONAL VIDEO (Gee: handle mp4 "with proper encoding for image/sound/video calculations
 * and settings for proper playing on all sources"). A video = a SEQUENCE of per-frame equations {C_t} + an
 * audio track + timing (fps). Each frame is equationalized with the SAME CDF 9/7 the stills use; playback
 * reconstructs each frame and animates it in sync with the audio.
 *
 * HONEST SCOPE (v1, no smoke):
 *  - Frame extraction + reconstruction + playback + a plays-EVERYWHERE .webm export are browser-side (need
 *    <video>/canvas/Web Audio/MediaRecorder). The .uvmv CONTAINER pack/unpack is pure + node-testable.
 *  - The portable file mirrors the .uvme split: a `.uvmv` carries the exact equation-sequence (rebuild here);
 *    `toWebm()` exports a standard VP9/Opus .webm that plays in any modern player/OS ("all sources").
 *  - Size: a full per-frame field × many frames is BIG. v1 samples at a chosen fps + caps resolution + keeps a
 *    compact field per frame (opts). Keyframe+delta inter-frame compression is a documented v2.
 *  - mp4 IN: we decode an mp4's frames (via <video> seek) + its audio; mp4 OUT (H.264) needs WebCodecs
 *    (Chromium) — webm is the universal export. Documented honestly.
 *
 * Depends (browser): equationalize.js (equationalizeImageData/imageToCappedData), reconstruct.js
 * (reconstructImageData), eqsound.js (toWav, optional). Pure parts use only TextEncoder/Decoder + base64.
 */
var EQVIDEO = (function () {
  const MAGIC = 'UVMV', SCHEMA = 'uvmv@1';
  const _b64 = (u8) => { let s = ''; const CH = 0x8000; for (let i = 0; i < u8.length; i += CH) s += String.fromCharCode.apply(null, u8.subarray(i, i + CH)); return (typeof btoa !== 'undefined' ? btoa : (x) => Buffer.from(x, 'binary').toString('base64'))(s); };
  const _unb64 = (b) => { const bin = (typeof atob !== 'undefined' ? atob : (x) => Buffer.from(x, 'base64').toString('binary'))(b); const u = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i); return u; };

  // ---- CONTAINER (.uvmv): pure, node-testable. Carries the frame-equation sequence + audio + timing. ----
  // Layout: 'UVMV' + u32 metaLen + meta JSON {schema,fps,width,height,sampleRate,frameCount,palette} +
  //         per-frame: u32 len + frame-rec JSON ;  then 'AUDI' + u32 n + int16 PCM.
  function packVideo(o) {
    const enc = new TextEncoder();
    const meta = enc.encode(JSON.stringify({ schema: SCHEMA, fps: o.fps || 12, width: o.width, height: o.height, sampleRate: o.sampleRate || 44100, frameCount: o.frames.length, palette: o.palette || [] }));
    const frameBufs = o.frames.map(f => enc.encode(JSON.stringify(f)));
    const pcm = o.audioPcm || new Float32Array(0), aN = pcm.length;
    let total = 4 + 4 + meta.length + frameBufs.reduce((s, b) => s + 4 + b.length, 0) + 4 + 4 + aN * 2;
    const buf = new ArrayBuffer(total), dv = new DataView(buf); let p = 0;
    const ws = s => { for (let i = 0; i < s.length; i++) dv.setUint8(p++, s.charCodeAt(i)); };
    const u32 = v => { dv.setUint32(p, v >>> 0, true); p += 4; };
    ws(MAGIC); u32(meta.length); for (let i = 0; i < meta.length; i++) dv.setUint8(p++, meta[i]);
    for (const b of frameBufs) { u32(b.length); for (let i = 0; i < b.length; i++) dv.setUint8(p++, b[i]); }
    ws('AUDI'); u32(aN); for (let i = 0; i < aN; i++) { const s = Math.max(-1, Math.min(1, pcm[i])); dv.setInt16(p, s < 0 ? s * 0x8000 : s * 0x7FFF, true); p += 2; }
    return buf;
  }
  // clean linear reader
  function _unpackFixed(buf) {
    const dv = new DataView(buf), dec = new TextDecoder(); let p = 0;
    const rd4 = () => { let s = ''; for (let i = 0; i < 4; i++) s += String.fromCharCode(dv.getUint8(p++)); return s; };
    const u32 = () => { const v = dv.getUint32(p, true); p += 4; return v; };
    if (rd4() !== MAGIC) throw new Error('not a .uvmv equational video');
    const mLen = u32(); const meta = JSON.parse(dec.decode(new Uint8Array(buf, p, mLen))); p += mLen;
    const frames = [];
    for (let i = 0; i < meta.frameCount; i++) { const fl = u32(); frames.push(JSON.parse(dec.decode(new Uint8Array(buf, p, fl)))); p += fl; }
    let audioPcm = new Float32Array(0);
    if (p + 8 <= buf.byteLength && rd4() === 'AUDI') { const n = u32(); audioPcm = new Float32Array(n); for (let i = 0; i < n; i++) { const v = dv.getInt16(p, true); p += 2; audioPcm[i] = v < 0 ? v / 0x8000 : v / 0x7FFF; } }
    return { schema: meta.schema, fps: meta.fps, width: meta.width, height: meta.height, sampleRate: meta.sampleRate, palette: meta.palette || [], frames, audioPcm };
  }

  // ---- ENCODE (browser): a <video> element → sample frames at fps → equationalize each → pack + audio ----
  async function fromVideo(videoEl, opts) {
    opts = opts || {}; const fps = opts.fps || 12, maxEdge = opts.maxEdge || 480;
    let dur = videoEl.duration;
    // MediaRecorder/streamed webm often report duration=Infinity until you seek to the end — force-resolve it.
    if (!isFinite(dur) || dur <= 0) {
      dur = await new Promise(res => {
        const h = () => { if (isFinite(videoEl.duration) && videoEl.duration > 0) { videoEl.removeEventListener('durationchange', h); res(videoEl.duration); } };
        videoEl.addEventListener('durationchange', h);
        try { videoEl.currentTime = 1e7; } catch (e) {}
        setTimeout(() => { videoEl.removeEventListener('durationchange', h); res(videoEl.duration); }, 2000);
      });
      try { videoEl.currentTime = 0; } catch (e) {}
      if (!isFinite(dur) || dur <= 0) throw new Error('video has no usable duration');
    }
    const vw = videoEl.videoWidth, vh = videoEl.videoHeight, s = Math.min(1, maxEdge / Math.max(vw, vh));
    const W = Math.max(2, Math.round(vw * s)), H = Math.max(2, Math.round(vh * s));
    const cv = document.createElement('canvas'); cv.width = W; cv.height = H; const cx = cv.getContext('2d');
    const seek = t => new Promise(res => { const on = () => { videoEl.removeEventListener('seeked', on); res(); }; videoEl.addEventListener('seeked', on); videoEl.currentTime = Math.min(t, dur - 1e-3); });
    const frames = []; const n = Math.max(1, Math.floor(dur * fps));
    for (let i = 0; i < n; i++) {
      await seek(i / fps); cx.drawImage(videoEl, 0, 0, W, H);
      frames.push(equationalizeImageData(cx.getImageData(0, 0, W, H)));
      if (opts.onProgress) opts.onProgress(i + 1, n);
    }
    return { frames, width: W, height: H, fps, palette: [], audioPcm: opts.audioPcm || null, sampleRate: opts.sampleRate || 44100 };
  }

  // ---- PLAYBACK (browser): reconstruct each frame's equation → animate on canvas, in sync with audio ----
  function play(canvas, pack, opts) {
    opts = opts || {}; const ctx = canvas.getContext('2d'); const fps = pack.fps || 12;
    const off = pack.frames.map(rec => { const id = reconstructImageData(rec, 0); const c = document.createElement('canvas'); c.width = rec.width; c.height = rec.height; c.getContext('2d').putImageData(id, 0, 0); return c; });
    let ac = null, src = null;
    // opts.silent → play no audio (image only); opts.noVideo → draw nothing (audio only). (#264 combination play)
    if (!opts.silent && pack.audioPcm && pack.audioPcm.length) { ac = new (window.AudioContext || window.webkitAudioContext)(); const b = ac.createBuffer(1, pack.audioPcm.length, pack.sampleRate || 44100); b.copyToChannel(pack.audioPcm, 0); src = ac.createBufferSource(); src.buffer = b; src.connect(ac.destination); }
    if (ac && ac.state === 'suspended') { try { ac.resume(); } catch (e) {} }   // suspended ctx → no audio/sync
    const t0 = (ac ? ac.currentTime : performance.now() / 1000); if (src) src.start();
    let raf = 0;
    // POLARIZED CROSSFADE: dissolve equation-frame i0 → i1 by the inter-frame fraction bf (outgoing at 1,
    // incoming fading in at bf) so the equational video MORPHS smoothly instead of hard-cutting (#255).
    const W = canvas.width, H = canvas.height;
    const blit = (f, a) => { if (!f || a <= 0) return; const base = Math.min(W / f.width, H / f.height); ctx.globalAlpha = a; ctx.drawImage(f, (W - f.width * base) / 2, (H - f.height * base) / 2, f.width * base, f.height * base); };
    const draw = () => {
      const t = (ac ? ac.currentTime : performance.now() / 1000) - t0, fp = t * fps, i0 = Math.floor(fp);
      if (i0 >= off.length) { if (opts.loop) { return play(canvas, pack, opts); } cancelAnimationFrame(raf); return; }
      const i1 = Math.min(off.length - 1, i0 + 1), bf = fp - i0;
      ctx.fillStyle = '#000'; ctx.fillRect(0, 0, W, H); ctx.imageSmoothingEnabled = true;
      if (!opts.noVideo) { blit(off[i0], 1); blit(off[i1], bf); } ctx.globalAlpha = 1;
      raf = requestAnimationFrame(draw);
    };
    draw(); return { stop: () => { cancelAnimationFrame(raf); if (src) try { src.stop(); } catch (e) {} } };
  }

  // ---- EXPORT a plays-EVERYWHERE clip: reconstruct frames → MediaRecorder(canvas+audio) → .webm/.mp4 ----
  // Shared recorder for toWebm + toMp4 (#263): identical capture + POLARIZED-CROSSFADE bake, only the codec
  // mime + blob type differ. `recMime` is the MediaRecorder mimeType (codec); `blobType` is the output Blob type.
  async function _record(pack, recMime, blobType, opts) {
    opts = opts || {};
    if (typeof MediaRecorder === 'undefined') throw new Error('MediaRecorder unavailable in this environment');
    const fps = pack.fps || 12, W = opts.width || (pack.frames[0] && pack.frames[0].width) || 480, H = opts.height || (pack.frames[0] && pack.frames[0].height) || 360;
    const cv = document.createElement('canvas'); cv.width = W; cv.height = H; const cx = cv.getContext('2d');
    const SMOOTH = Math.max(fps, 30);                          // bake the crossfade at >=30fps so the morph is smooth
    const stream = cv.captureStream(SMOOTH);
    if (pack.audioPcm && pack.audioPcm.length) { const ac = new (window.AudioContext || window.webkitAudioContext)(); const b = ac.createBuffer(1, pack.audioPcm.length, pack.sampleRate || 44100); b.copyToChannel(pack.audioPcm, 0); const sn = ac.createBufferSource(); sn.buffer = b; const dest = ac.createMediaStreamDestination(); sn.connect(dest); sn.start(); stream.addTrack(dest.stream.getAudioTracks()[0]); }
    const mime = MediaRecorder.isTypeSupported(recMime) ? recMime : (MediaRecorder.isTypeSupported(blobType) ? blobType : '');
    const rec = new MediaRecorder(stream, mime ? { mimeType: mime, videoBitsPerSecond: opts.bitrate || 8_000_000 } : { videoBitsPerSecond: opts.bitrate || 8_000_000 }), chunks = [];
    rec.ondataavailable = e => { if (e.data.size) chunks.push(e.data); };
    const done = new Promise(res => rec.onstop = () => res(new Blob(chunks, { type: blobType })));
    rec.start();
    const off = pack.frames.map(r => { const id = reconstructImageData(r, 0); const c = document.createElement('canvas'); c.width = r.width; c.height = r.height; c.getContext('2d').putImageData(id, 0, 0); return c; });
    // POLARIZED CROSSFADE baked into the export: render at SMOOTH fps, dissolving frame i0 → i1 each step (#255)
    const blit = (f, a) => { if (!f || a <= 0) return; const base = Math.min(W / f.width, H / f.height); cx.globalAlpha = a; cx.drawImage(f, (W - f.width * base) / 2, (H - f.height * base) / 2, f.width * base, f.height * base); };
    const steps = Math.max(1, Math.round((off.length / fps) * SMOOTH));
    for (let s = 0; s < steps; s++) {
      const fp = (s / SMOOTH) * fps, i0 = Math.min(off.length - 1, Math.floor(fp)), i1 = Math.min(off.length - 1, i0 + 1), bf = fp - i0;
      cx.fillStyle = '#000'; cx.fillRect(0, 0, W, H); cx.imageSmoothingEnabled = true; cx.globalAlpha = 1;
      blit(off[i0], 1); blit(off[i1], bf); cx.globalAlpha = 1;
      await new Promise(r => setTimeout(r, 1000 / SMOOTH));
    }
    rec.stop(); return await done;
  }
  // .webm (VP9/Opus) — the universal export, plays in any modern player/OS.
  async function toWebm(pack, opts) { const hasA = pack.audioPcm && pack.audioPcm.length; return await _record(pack, hasA ? 'video/webm;codecs=vp9,opus' : 'video/webm;codecs=vp9', 'video/webm', opts); }
  // .mp4 (H.264/AAC) IN THE BROWSER (#263) — Chrome 126+/Edge/Safari support H.264 in MediaRecorder, no muxer
  // needed. Where unsupported (Firefox), throw a clear pointer to .webm here or the Converter's true-mp4 path.
  async function toMp4(pack, opts) {
    const hasA = pack.audioPcm && pack.audioPcm.length;
    const want = hasA ? ['video/mp4;codecs=avc1.42E01E,mp4a.40.2', 'video/mp4;codecs=avc1,mp4a', 'video/mp4'] : ['video/mp4;codecs=avc1.42E01E', 'video/mp4;codecs=avc1', 'video/mp4'];
    let mime = null; if (typeof MediaRecorder !== 'undefined') for (const w of want) if (MediaRecorder.isTypeSupported(w)) { mime = w; break; }
    if (!mime) throw new Error("This browser can't record .mp4 (no MediaRecorder H.264). Use ↓ .webm here, or the downloaded Converter for a true .mp4 (Rust ffmpeg).");
    return await _record(pack, mime, 'video/mp4', opts);
  }
  // ---- EXPORT an animated GIF from the equation sequence (#263): reconstruct each frame → gifenc.encodeGIF. ----
  async function toGif(pack, opts) {
    opts = opts || {};
    if (typeof encodeGIF === 'undefined') throw new Error('gifenc.js not loaded for GIF export');
    const fps = pack.fps || 12, delayCs = Math.max(2, Math.round(100 / fps));
    const W = opts.width || (pack.frames[0] && pack.frames[0].width) || 320, H = opts.height || (pack.frames[0] && pack.frames[0].height) || 240;
    const cv = document.createElement('canvas'); cv.width = W; cv.height = H; const cx = cv.getContext('2d');
    const rgba = [];
    for (const r of pack.frames) {
      const id = reconstructImageData(r, 0), fc = document.createElement('canvas'); fc.width = r.width; fc.height = r.height; fc.getContext('2d').putImageData(id, 0, 0);
      cx.fillStyle = '#000'; cx.fillRect(0, 0, W, H); const base = Math.min(W / r.width, H / r.height); cx.imageSmoothingEnabled = true;
      cx.drawImage(fc, (W - r.width * base) / 2, (H - r.height * base) / 2, r.width * base, r.height * base);
      rgba.push(new Uint8ClampedArray(cx.getImageData(0, 0, W, H).data));
      await new Promise(rs => setTimeout(rs));                 // YIELD per frame so a long GIF export can't freeze the page (#265)
    }
    return encodeGIF(rgba, W, H, { delayCs, loop: opts.loop != null ? opts.loop : 0 });   // Uint8Array (animated GIF89a)
  }

  // ---- DECODE an animated GIF → per-frame equations (#263). gifdec.js extracts every composited frame
  // (browsers only paint frame 1), then each is equationalized with the same CDF 9/7 as the stills/video. ----
  async function fromGif(input, opts) {
    opts = opts || {};
    if (typeof GIFDEC === 'undefined') throw new Error('gifdec.js not loaded for GIF decoding');
    const buf = input instanceof ArrayBuffer ? input : (input.buffer ? input.buffer : input);
    const g = await GIFDEC.decode(buf, 'image/gif');
    const maxEdge = opts.maxEdge || 360, s = Math.min(1, maxEdge / Math.max(g.width, g.height));
    const W = Math.max(2, Math.round(g.width * s)), H = Math.max(2, Math.round(g.height * s));
    const src = document.createElement('canvas'); src.width = g.width; src.height = g.height; const sctx = src.getContext('2d');
    const dst = document.createElement('canvas'); dst.width = W; dst.height = H; const dctx = dst.getContext('2d');
    let avg = 0; for (const fr of g.frames) avg += fr.delayMs; avg = (avg / g.frames.length) || 100;
    const fps = Math.max(1, Math.min(30, opts.fps || Math.round(1000 / avg)));
    const frames = [];
    for (let i = 0; i < g.frames.length; i++) {
      sctx.putImageData(new ImageData(new Uint8ClampedArray(g.frames[i].rgba), g.width, g.height), 0, 0);
      dctx.imageSmoothingEnabled = true; dctx.drawImage(src, 0, 0, W, H);
      frames.push(equationalizeImageData(dctx.getImageData(0, 0, W, H)));
      if (opts.onProgress) opts.onProgress(i + 1, g.frames.length);
      await new Promise(r => setTimeout(r));                   // YIELD per frame so the page never goes "unresponsive" (#265)
    }
    return { frames, width: W, height: H, fps, palette: opts.palette || [], audioPcm: opts.audioPcm || null, sampleRate: opts.sampleRate || 44100 };
  }

  // ---- ZOOM-DIVE frames from ONE equation (#263): the infinite zoom-fractalize, but baked frame-by-frame.
  // Mirrors video.html vDraw() exactly — the REAL reconstruction scaled by an exponentially-growing zoom, and
  // past native detail (zoom>3) FT_COMBINER.fractalize grows new self-similar Newton→N³ detail (equational N³,
  // vector-collapse K + angular-entropy seed). Each baked frame is re-equationalized → a true equational video. ----
  async function _zoomDiveFrames(rec, palette, opts) {
    opts = opts || {}; palette = palette || [];
    const fps = opts.fps || 12, N = Math.max(2, opts.diveFrames || Math.round(fps * (opts.diveSeconds || 4)));
    const maxZoom = opts.maxZoom || 24, FRACTAL = opts.fractal !== false;
    const W = opts.diveW || 360, H = opts.diveH || Math.max(2, Math.round(W * rec.height / rec.width)) || 240;
    const off = document.createElement('canvas'); off.width = rec.width; off.height = rec.height;
    off.getContext('2d').putImageData(reconstructImageData(rec, 0), 0, 0);
    const cv = document.createElement('canvas'); cv.width = W; cv.height = H; const ctx = cv.getContext('2d');
    const cxp = rec.width / 2, cyp = rec.height / 2, frames = [];   // dive toward the center (v1)
    for (let i = 0; i < N; i++) {
      const z = Math.pow(maxZoom, N > 1 ? i / (N - 1) : 0);
      ctx.fillStyle = '#000'; ctx.fillRect(0, 0, W, H);
      const base = Math.min(W / off.width, H / off.height) * z, dw = off.width * base, dh = off.height * base;
      const dx = W / 2 - cxp * base, dy = H / 2 - cyp * base;
      ctx.imageSmoothingEnabled = true; ctx.drawImage(off, dx, dy, dw, dh);
      if (FRACTAL && z > 3 && base > 8 && typeof FT_COMBINER !== 'undefined') {
        const alpha = Math.min(0.85, (z - 3) / 8);
        const sx = Math.max(0, Math.min(off.width - 1, Math.floor((W / 2 - dx) / base)));
        const sy = Math.max(0, Math.min(off.height - 1, Math.floor((H / 2 - dy) / base)));
        const depth = Math.log2(Math.max(1, base));
        const view = { mode: 'n3', cx: (sx / off.width - 0.5) * 3.2 + depth * 0.06, cy: (sy / off.height - 0.5) * 3.2 + depth * 0.05, span: Math.max(0.08, 1.8 / (base / 2)), seed: 0.5, zoom: base };
        const _c = FT_COMBINER.vectorCollapse(off, sx, sy, 8); view.seed = _c.aEntropy; view.override = { ang: _c.ang, kbias: _c.mag };
        FT_COMBINER.fractalize(ctx, W, H, view, palette, alpha);
      }
      frames.push(equationalizeImageData(ctx.getImageData(0, 0, W, H)));
      if (opts.onProgress) opts.onProgress(i + 1, N);
      await new Promise(r => setTimeout(r));                   // YIELD per frame — keep the page responsive (#265)
    }
    return frames;
  }

  // ---- SOUND → equational VIDEO (#263): a sound has no frames, so we derive its equation-image (additive C
  // from its spectrum, the same path the Combiner uses) and animate a zoom-dive into it. Audio track per
  // opts.audioTrack: 'equation' (the equation-MUSIC via sonify.renderPCM) | 'original' (the source sound) |
  // 'none'. This is the sound>.gif / sound>.mp4 direction. ----
  async function fromSound(pcm, sr, opts) {
    opts = opts || {};
    let rec = opts.rec, palette = opts.palette || [];
    if (!rec) { if (typeof FT_COMBINER === 'undefined') throw new Error('combiner.js needed to equationalize a sound'); rec = equationalizeImageData(FT_COMBINER.fromAudio(pcm, sr, { palette })); }
    const frames = await _zoomDiveFrames(rec, palette, opts);
    let audioPcm = null, sampleRate = 44100;
    const track = opts.audioTrack || 'equation';
    if (track === 'original' && pcm && pcm.length) { audioPcm = pcm; sampleRate = sr || 44100; }
    else if (track === 'equation' && typeof FT_SONIFIER !== 'undefined') { const m = await FT_SONIFIER.renderPCM(rec, palette, opts.seconds); audioPcm = m.pcm; sampleRate = m.sampleRate; }
    return { frames, width: frames[0].width, height: frames[0].height, fps: opts.fps || 12, palette, audioPcm, sampleRate };
  }

  // ---- IMAGE → equational VIDEO (#263): zoom-dive into a still's equation. Optional equation-music track. ----
  async function fromImage(rec, palette, opts) {
    opts = opts || {}; palette = palette || [];
    const frames = await _zoomDiveFrames(rec, palette, opts);
    let audioPcm = null, sampleRate = 44100;
    if (opts.audioTrack === 'equation' && typeof FT_SONIFIER !== 'undefined') { const m = await FT_SONIFIER.renderPCM(rec, palette, opts.seconds); audioPcm = m.pcm; sampleRate = m.sampleRate; }
    return { frames, width: frames[0].width, height: frames[0].height, fps: opts.fps || 12, palette, audioPcm, sampleRate };
  }

  // ---- ONE ROUTER for every motion source (#263), so a portal hands any File and gets back a pack. Routes
  // .uvmv → unpack, .gif → fromGif, video → fromVideo, image/sound → combiner field C → zoom-dive video. ----
  async function fromAnyMotion(file, opts) {
    opts = opts || {};
    const name = (file.name || '').toLowerCase(), type = file.type || '';
    if (/\.uvmv$/.test(name)) return _unpackFixed(await file.arrayBuffer());
    if (type === 'image/gif' || /\.gif$/.test(name)) return await fromGif(await file.arrayBuffer(), opts);
    if (type.indexOf('video/') === 0 || /\.(mp4|webm|mov|m4v|ogv|mkv|avi|wmv|flv|m2ts)$/.test(name)) {
      const v = opts.videoEl || document.createElement('video'); v.muted = true; v.playsInline = true;
      const url = URL.createObjectURL(file); v.src = url;
      await new Promise((res, rej) => { v.onloadedmetadata = res; v.onerror = () => rej(new Error('could not load that video')); });
      let audioPcm = null, sampleRate = 44100;
      try { const OAC = window.OfflineAudioContext || window.webkitOfflineAudioContext; const ab = await new OAC(1, 1, 44100).decodeAudioData((await file.arrayBuffer()).slice(0)); audioPcm = ab.getChannelData(0).slice(0); sampleRate = ab.sampleRate; } catch (e) {}
      const pack = await fromVideo(v, Object.assign({ audioPcm, sampleRate }, opts)); URL.revokeObjectURL(url); return pack;
    }
    // image OR sound → the combiner extracts the field C, then we animate a zoom-dive into it.
    if (typeof FT_COMBINER === 'undefined') throw new Error('combiner.js needed to turn an image/sound into motion');
    const r = await FT_COMBINER.fromAnyFile(file);
    if (r.origKind === 'sound') return await fromSound(r.pcm, r.sampleRate || 44100, Object.assign({ rec: r.rec, palette: r.palette }, opts));
    return await fromImage(r.rec, r.palette || [], opts);
  }

  // ---- THE "ANSWERS" (#263 D1-D4): from a motion pack derive the same four answers the still combiner gives —
  //   'image'   → a representative frame's reconstruction (ImageData)                                    (D1)
  //   'png'     → that frame as a PNG Blob carrying its equation in a tEXt chunk (re-imports exact)       (D1/E9)
  //   'sound'   → the ORIGINAL audio track if the pack carries one (mp4 has it, gif doesn't) → .wav Blob  (D2)
  //   'eqmusic' → the equation-MUSIC of the representative frame (sonify.renderPCM) → .wav Blob           (D3)
  //   'frames'  → every frame's reconstruction (ImageData[])                                             (D4)
  //   'sheet'   → a contact-sheet PNG Blob of all frames                                                 (D4)
  // This also RESOLVES the matrix's audio directions: gif→sound = 'eqmusic' (a gif has no original track);
  // mp4→sound = 'sound' (original) and/or 'eqmusic' (equation-music). (#263 E7/E8/E9.)
  function _reprFrame(pack) { return pack.frames[Math.floor(pack.frames.length / 2)] || pack.frames[0]; }
  async function answer(pack, kind, opts) {
    opts = opts || {}; kind = (kind || 'image').toLowerCase();
    const rec = _reprFrame(pack), palette = pack.palette || [];
    if (kind === 'image') return reconstructImageData(rec, 0);
    if (kind === 'png') {
      const id = reconstructImageData(rec, 0), cv = document.createElement('canvas'); cv.width = rec.width; cv.height = rec.height; cv.getContext('2d').putImageData(id, 0, 0);
      const base = await new Promise(r => cv.toBlob(r, 'image/png'));
      if (typeof UVM !== 'undefined' && UVM.pngEmbed) { const bytes = UVM.pngEmbed(new Uint8Array(await base.arrayBuffer()), { rec, palette }); return new Blob([bytes], { type: 'image/png' }); }
      return base;
    }
    if (kind === 'sound' || kind === 'wav') { if (!pack.audioPcm || !pack.audioPcm.length || typeof EQSND === 'undefined') return null; return new Blob([EQSND.toWav(pack.audioPcm, pack.sampleRate || 44100)], { type: 'audio/wav' }); }
    if (kind === 'eqmusic') { if (typeof FT_SONIFIER === 'undefined') throw new Error('sonify.js needed for equation-music'); const m = await FT_SONIFIER.renderPCM(rec, palette, opts.seconds); return new Blob([EQSND.toWav(m.pcm, m.sampleRate)], { type: 'audio/wav' }); }
    if (kind === 'frames') return pack.frames.map(r => reconstructImageData(r, 0));
    if (kind === 'sheet') {
      const n = pack.frames.length, cols = Math.ceil(Math.sqrt(n)), rows = Math.ceil(n / cols);
      const fw = (pack.frames[0] && pack.frames[0].width) || 160, fh = (pack.frames[0] && pack.frames[0].height) || 120;
      const cv = document.createElement('canvas'); cv.width = cols * fw; cv.height = rows * fh; const cx = cv.getContext('2d');
      cx.fillStyle = '#000'; cx.fillRect(0, 0, cv.width, cv.height);
      pack.frames.forEach((r, i) => { const id = reconstructImageData(r, 0), fc = document.createElement('canvas'); fc.width = r.width; fc.height = r.height; fc.getContext('2d').putImageData(id, 0, 0); cx.drawImage(fc, (i % cols) * fw, Math.floor(i / cols) * fh, fw, fh); });
      return await new Promise(r => cv.toBlob(r, 'image/png'));
    }
    throw new Error('unknown answer kind: ' + kind);
  }

  return { packVideo, unpackVideo: _unpackFixed, fromVideo, fromGif, fromSound, fromImage, fromAnyMotion, _zoomDiveFrames, answer, play, toWebm, toMp4, toGif, MAGIC, SCHEMA };
})();
if (typeof window !== 'undefined') window.EQVIDEO = EQVIDEO;
if (typeof module !== 'undefined' && module.exports) module.exports = EQVIDEO;
