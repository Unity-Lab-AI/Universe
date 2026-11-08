// DNA-AND · web · the .cdf97w player, every line ours (the page's own script; it also runs as its own worker, which unpacks the
// terms off the page's thread). The sums are the program's own (src/render/WaveletCoder.cs, src/core/Wavelet97.cs,
// src/render/ClipWriter.cs): the same range decoder and odds, the same lifting, the same weights read from the clip's metadata.
'use strict';
const MAGIC = 0x57373943, WAVE_VERSION = 8, UNIVERSE_VERSION = 9, UNIVERSE_KIND = 1;
const STRIPS = 16, LEVELS = 32, UNARY = 15, GOLOMB = 32, DYADIC = 6;
const A97 = -1.586134342059924, B97 = -0.052980118572961, G97 = 0.882911075530934, D97 = 0.443506852043971, K97 = 1.230174104914001;

// ── the clip's header, both kinds ──
function readHeader(buf) {
  const v = new DataView(buf);
  if (buf.byteLength < 8 || v.getUint32(0, true) !== MAGIC) throw new Error('not a .cdf97w clip');
  const ver = v.getUint16(4, true);
  if (ver === UNIVERSE_VERSION && v.getUint8(6) === UNIVERSE_KIND) {
    const ml = v.getUint16(7, true);
    return { kind: 'universe', meta: new TextDecoder().decode(new Uint8Array(buf, 9, ml)), body: 9 + ml };
  }
  if (ver !== WAVE_VERSION) throw new Error('an older .cdf97w (version ' + ver + '): film it again');
  const w = v.getUint16(6, true), h = v.getUint16(8, true), pw = v.getUint16(10, true), ph = v.getUint16(12, true);
  const steps = [v.getFloat32(18, true), v.getFloat32(22, true), v.getFloat32(26, true)];
  const ml = v.getUint16(30, true), meta = new TextDecoder().decode(new Uint8Array(buf, 32, ml));
  const frames = []; let at = 32 + ml;
  while (at + 9 <= buf.byteLength) {
    const ms = v.getUint32(at, true), key = v.getUint8(at + 4) === 0, n = v.getInt32(at + 5, true);
    if (at + 9 + n > buf.byteLength) break;
    frames.push({ ms, key, at: at + 9, n }); at += 9 + n;
  }
  while (frames.length && !frames[0].key) frames.shift();
  // each subband's share of its plane's step, as the clip's metadata gives it ("weights: label=value ...")
  const weights = new Float32Array(LEVELS).fill(1);
  const wl = meta.split('\n').find(l => l.startsWith('weights:'));
  if (wl) for (const p of wl.substring(8).trim().split(/\s+/)) { const kv = p.split('='); if (kv.length === 2) weights[+kv[0]] = +kv[1]; }
  return { kind: 'wave', w, h, pw, ph, steps, meta, frames, weights };
}

// ── a .cdf97w that is our own WebM (a picture track any player shows): the universe's records carried inside as an attached file ──
const ATTACH_NAME = 'universe.cdf97w';
function isWebm(buf) { const b = new Uint8Array(buf, 0, 4); return b[0] === 0x1A && b[1] === 0x45 && b[2] === 0xDF && b[3] === 0xA3; }
function findAttachment(buf, name) {
  const d = new Uint8Array(buf); let at = 0;
  const id = () => { const b = d[at++]; const n = b & 0x80 ? 1 : b & 0x40 ? 2 : b & 0x20 ? 3 : 4; let v = b; for (let k = 1; k < n; k++) v = v * 256 + d[at++]; return v; };
  const vint = () => { const b = d[at++]; let n = 1, m = 0x80; while (n <= 8 && (b & m) === 0) { n++; m >>= 1; } let v = b & (m - 1); for (let k = 1; k < n; k++) v = v * 256 + d[at++]; return v; };
  if (id() !== 0x1A45DFA3) return null; { const l = vint(); at += l; }
  if (id() !== 0x18538067) return null; vint();
  while (at < d.length) {
    const e = id(), n = vint();
    if (e === 0x1F43B675) return null;
    if (e !== 0x1941A469) { at += n; continue; }
    const end = at + n;
    while (at < end) {
      id(); const fl = vint(), fend = at + fl; let nm = null;
      while (at < fend) {
        const c = id(), cl = vint();
        if (c === 0x466E) { nm = new TextDecoder().decode(d.subarray(at, at + cl)); at += cl; }
        else if (c === 0x465C && nm === name) return buf.slice(at, at + cl);
        else at += cl;
      }
    }
    return null;
  }
  return null;
}

// ── the terms, unpacked (the exact reverse of WaveletCoder.Pack): an adaptive binary range coder, strip by strip ──
function levelMap(pw, ph) {
  const L = new Uint8Array(pw * ph);
  for (let y = 0; y < ph; y++) for (let x = 0; x < pw; x++) {
    let l = 1, w = pw >> 1, h = ph >> 1, o;
    for (;;) { if (x >= w || y >= h) { o = (x >= w ? 1 : 0) + (y >= h ? 2 : 0); break; } if (l === DYADIC) { o = 0; break; } l++; w >>= 1; h >>= 1; }
    L[y * pw + x] = l * 4 + o;
  }
  return L;
}
function unpackStrip(bytes, q, L, pw, y0, y1) {
  let range = 0xFFFFFFFF, code = 0, at = 0;
  const next = () => (at < bytes.length ? bytes[at++] : 0);
  for (let i = 0; i < 5; i++) code = ((code << 8) | next()) >>> 0;
  const sig = new Uint16Array(LEVELS * 3).fill(1024), mag = new Uint16Array(LEVELS * UNARY).fill(1024), pre = new Uint16Array(GOLOMB).fill(1024), sign = new Uint16Array(1).fill(1024);
  const bit = (p, i) => {
    const bound = (range >>> 11) * p[i]; let b;
    if (code < bound) { range = bound >>> 0; p[i] += (2048 - p[i]) >> 5; b = 0; }
    else { code = (code - bound) >>> 0; range = (range - bound) >>> 0; p[i] -= p[i] >> 5; b = 1; }
    while (range < 0x1000000) { range = (range << 8) >>> 0; code = ((code << 8) | next()) >>> 0; }
    return b;
  };
  const direct = () => {
    range = range >>> 1; let b = 0; if (code >= range) { code = (code - range) >>> 0; b = 1; }
    while (range < 0x1000000) { range = (range << 8) >>> 0; code = ((code << 8) | next()) >>> 0; }
    return b;
  };
  for (let y = y0; y < y1; y++) for (let x = 0; x < pw; x++) {
    const i = y * pw + x, c = (x > 0 && q[i - 1] !== 0 ? 1 : 0) + (y > y0 && q[i - pw] !== 0 ? 1 : 0), lv = L[i];
    if (bit(sig, lv * 3 + c) === 0) { q[i] = 0; continue; }
    const neg = bit(sign, 0) === 1;
    let k = 0; while (k < UNARY && bit(mag, lv * UNARY + k) === 1) k++;
    let a = k;
    if (k === UNARY) { let nb = 0; while (nb < GOLOMB - 1 && bit(pre, nb) === 1) nb++; let r = 0; for (let j = 0; j < nb; j++) r = (r << 1) | direct(); a = UNARY + r; }
    const val = a + 1; q[i] = neg ? -val : val;
  }
}
function unpack(buf, off, n, into, L, pw, ph) {
  const v = new DataView(buf, off, n), parts = v.getUint8(0), len = [];
  for (let k = 0; k < parts; k++) len.push(v.getInt32(1 + 4 * k, true));
  let at = off + 1 + 4 * parts;
  for (let k = 0; k < Math.min(parts, into.length * STRIPS); k++) {
    const row = s => Math.floor(ph * s / STRIPS), st = k % STRIPS;
    unpackStrip(new Uint8Array(buf, at, len[k]), into[(k / STRIPS) | 0], L, pw, row(st), row(st + 1)); at += len[k];
  }
}

// ── the worker: the clip's frames unpacked, a whole frame and the changes after it added in ──
if (typeof window === 'undefined' && typeof self !== 'undefined') {
  let clip, buf, L, cur, delta, built = -1;
  self.onmessage = e => {
    const m = e.data;
    if (m.buf) { buf = m.buf; clip = readHeader(buf); L = levelMap(clip.pw, clip.ph); const n = clip.pw * clip.ph; cur = [0, 1, 2].map(() => new Int16Array(n)); delta = [0, 1, 2].map(() => new Int16Array(n)); built = -1; return; }
    const i = m.frame, f = clip.frames; let from;
    let anyKey = false; for (let j = built + 1; j <= i; j++) if (f[j].key) anyKey = true;
    if (built >= 0 && built < i && !anyKey) from = built + 1; else { from = i; while (from > 0 && !f[from].key) from--; }
    for (let j = from; j <= i; j++) {
      if (f[j].key) unpack(buf, f[j].at, f[j].n, cur, L, clip.pw, clip.ph);
      else { unpack(buf, f[j].at, f[j].n, delta, L, clip.pw, clip.ph); for (let c = 0; c < 3; c++) { const a = cur[c], d = delta[c]; for (let k = 0; k < a.length; k++) a[k] = (a[k] + d[k]) << 16 >> 16; } }
    }
    built = i;
    const out = cur.map(p => p.slice());
    self.postMessage({ frame: i, planes: out }, out.map(p => p.buffer));
  };
} else if (typeof window !== 'undefined') {
  window.addEventListener('DOMContentLoaded', page);
}

// ── the graphics card: the terms to values, the inverse dyadic CDF 9/7, the colour ──
const VS = `#version 300 es
void main() { vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }`;
const DEQ = `#version 300 es
precision highp float; precision highp int;
uniform highp isampler2D qy, qb, qr; uniform int pw, ph; uniform vec3 steps; uniform float weights[32];
out vec4 o;
float val(int q, float st) { return q == 0 ? 0.0 : (float(q) + (q > 0 ? 0.5 : -0.5)) * st; }
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy); int l = 1, w = pw / 2, h = ph / 2, k;
  for (int i = 0; i < 8; i++) { if (p.x >= w || p.y >= h) { k = (p.x >= w ? 1 : 0) + (p.y >= h ? 2 : 0); break; } if (l == 6) { k = 0; break; } l++; w /= 2; h /= 2; }
  float wt = weights[l * 4 + k];
  o = vec4(val(texelFetch(qy, p, 0).r, steps.x * wt), val(texelFetch(qb, p, 0).r, steps.y * wt), val(texelFetch(qr, p, 0).r, steps.z * wt), 1.0);
}`;
// one step of one level's lifting along a direction (0 down the columns, 1 along the rows), inside the level's low corner
const LIFT = `#version 300 es
precision highp float; precision highp int;
uniform sampler2D a; uniform int dir, lw, lh, op; uniform float c;
out vec4 o;
vec4 at(ivec2 p, int i) { return texelFetch(a, dir == 0 ? ivec2(p.x, i) : ivec2(i, p.y), 0); }
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy); vec4 s = texelFetch(a, p, 0);
  if (p.x >= lw || p.y >= lh) { o = s; return; }
  int i = dir == 0 ? p.y : p.x, n = dir == 0 ? lh : lw, half_ = n / 2;
  if (op == 0) { o = (i % 2 == 0) ? at(p, i / 2) * ${K97} : at(p, half_ + (i - 1) / 2) / ${K97}; return; }
  bool even = (i % 2) == 0;
  if (op == 1 && even) { o = i == 0 ? s - 2.0 * c * at(p, 1) : s - c * (at(p, i - 1) + at(p, i + 1)); return; }
  if (op == 2 && !even) { o = i == n - 1 ? s - 2.0 * c * at(p, i - 1) : s - c * (at(p, i - 1) + at(p, i + 1)); return; }
  o = s;
}`;
const RGB = `#version 300 es
precision highp float; precision highp int;
uniform sampler2D a; uniform int h;
out vec4 o;
void main() {
  ivec2 p = ivec2(gl_FragCoord.xy); vec3 v = texelFetch(a, ivec2(p.x, h - 1 - p.y), 0).rgb; float cb = v.y - 0.5, cr = v.z - 0.5;
  o = vec4(clamp(vec3(v.x + 1.402 * cr, v.x - 0.344136 * cb - 0.714136 * cr, v.x + 1.772 * cb), 0.0, 1.0), 1.0);
}`;

function makeGpu(canvas, clip) {
  const gl = canvas.getContext('webgl2', { antialias: false, preserveDrawingBuffer: true });
  if (!gl || !gl.getExtension('EXT_color_buffer_float')) throw new Error('this browser has no WebGL 2 with float pictures');
  const prog = fs => {
    const p = gl.createProgram();
    for (const [t, src] of [[gl.VERTEX_SHADER, VS], [gl.FRAGMENT_SHADER, fs]]) { const s = gl.createShader(t); gl.shaderSource(s, src); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); gl.attachShader(p, s); }
    gl.linkProgram(p); if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p)); return p;
  };
  const deq = prog(DEQ), lift = prog(LIFT), rgb = prog(RGB);
  const { pw, ph, w, h } = clip;
  const tex = (fmt, f, t) => { const x = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, x); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST); gl.texStorage2D(gl.TEXTURE_2D, 1, fmt, pw, ph); return x; };
  const q = [0, 1, 2].map(() => tex(gl.R16I));
  const ping = [tex(gl.RGBA32F), tex(gl.RGBA32F)], fb = ping.map(t => { const f = gl.createFramebuffer(); gl.bindFramebuffer(gl.FRAMEBUFFER, f); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, t, 0); return f; });
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 2);
  const U = (p, n) => gl.getUniformLocation(p, n);
  return planes => {
    for (let c = 0; c < 3; c++) { gl.bindTexture(gl.TEXTURE_2D, q[c]); gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, pw, ph, gl.RED_INTEGER, gl.SHORT, planes[c]); }
    // the terms to values
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb[0]); gl.viewport(0, 0, pw, ph); gl.useProgram(deq);
    for (let c = 0; c < 3; c++) { gl.activeTexture(gl.TEXTURE0 + c); gl.bindTexture(gl.TEXTURE_2D, q[c]); }
    gl.uniform1i(U(deq, 'qy'), 0); gl.uniform1i(U(deq, 'qb'), 1); gl.uniform1i(U(deq, 'qr'), 2);
    gl.uniform1i(U(deq, 'pw'), pw); gl.uniform1i(U(deq, 'ph'), ph); gl.uniform3f(U(deq, 'steps'), clip.steps[0], clip.steps[1], clip.steps[2]); gl.uniform1fv(U(deq, 'weights'), clip.weights);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    // the inverse dyadic transform: from the coarsest level, the low corner's columns, then its rows; each a deinterleave and
    // the four lifting steps undone (delta, gamma, beta, alpha)
    gl.useProgram(lift); gl.activeTexture(gl.TEXTURE0); gl.uniform1i(U(lift, 'a'), 0);
    let src = 0;
    const pass = (dir, lw, lh, op, c) => {
      gl.bindFramebuffer(gl.FRAMEBUFFER, fb[1 - src]); gl.bindTexture(gl.TEXTURE_2D, ping[src]);
      gl.uniform1i(U(lift, 'dir'), dir); gl.uniform1i(U(lift, 'lw'), lw); gl.uniform1i(U(lift, 'lh'), lh); gl.uniform1i(U(lift, 'op'), op); gl.uniform1f(U(lift, 'c'), c);
      gl.drawArrays(gl.TRIANGLES, 0, 3); src = 1 - src;
    };
    for (let l = DYADIC - 1; l >= 0; l--) {
      const lw = pw >> l, lh = ph >> l;
      for (const dir of [0, 1]) { pass(dir, lw, lh, 0, 0); pass(dir, lw, lh, 1, D97); pass(dir, lw, lh, 2, G97); pass(dir, lw, lh, 1, B97); pass(dir, lw, lh, 2, A97); }
    }
    // the colour, onto the page
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.viewport(0, 0, w, h); gl.useProgram(rgb); gl.bindTexture(gl.TEXTURE_2D, ping[src]);
    gl.uniform1i(U(rgb, 'a'), 0); gl.uniform1i(U(rgb, 'h'), h); gl.drawArrays(gl.TRIANGLES, 0, 3);
  };
}

// ── the clip of the universe itself: the seed, the camera's track and the scene's calls ──
async function readUniverse(buf, head) {
  const z = new Blob([new Uint8Array(buf, head.body)]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  const raw = new Uint8Array(await new Response(z).arrayBuffer()), v = new DataView(raw.buffer);
  let at = 0; const points = [], events = [];
  const str = () => { let n = 0, s = 0, b; do { b = raw[at++]; n |= (b & 0x7F) << s; s += 7; } while (b & 0x80); const t = new TextDecoder().decode(raw.subarray(at, at + n)); at += n; return t; };
  const d = () => { const x = v.getFloat64(at, true); at += 8; return x; }, f = () => { const x = v.getFloat32(at, true); at += 4; return x; };
  while (at + 5 <= raw.length) {
    const type = raw[at++], ms = v.getUint32(at, true); at += 4;
    if (type === 1) {
      if (at + 172 > raw.length) break;
      const p = { ms, a: [d(), d(), d()], o: [d(), d(), d()], fwd: [f(), f(), f()], up: [f(), f(), f()], far: d(), space: d(), top: d(), matter: d(), years: d(), focus: [d(), d(), d(), d(), d(), d()] };
      p.near = v.getInt32(at, true); p.sys = v.getInt32(at + 4, true); p.planet = v.getInt32(at + 8, true); at += 12; points.push(p);
    } else if (type === 2) {
      const name = str(), n = raw[at++], args = [];
      for (let i = 0; i < n; i++) {
        const t = String.fromCharCode(raw[at++]);
        if (t === 'n') args.push(null); else if (t === 'd') args.push(d()); else if (t === 'f') args.push(f());
        else if (t === 'u') { args.push(v.getBigUint64(at, true)); at += 8; } else if (t === 'i') { args.push(v.getInt32(at, true)); at += 4; }
        else if (t === 'b') args.push(raw[at++] !== 0); else if (t === 's') args.push(str());
        else if (t === 'p') { const c = v.getInt32(at, true); at += 4; const parts = []; for (let k = 0; k < c; k++) { const nm = str(), el = str(), sh = d(); parts.push(nm + ' (' + el + ', ' + sh.toPrecision(3) + ')'); f(); f(); f(); } args.push(parts.join('; ')); }
        else throw new Error('an argument of an unknown kind');
      }
      events.push({ ms, name, args });
    } else break;
  }
  return { points, events };
}
function drawUniverse(cx, W, H, u, t, head) {
  cx.fillStyle = '#05060a'; cx.fillRect(0, 0, W, H);
  const P = u.points; if (!P.length) return;
  let k = 0; while (k + 1 < P.length && P[k + 1].ms <= t) k++;
  // the track, seen from above in the universe's own coordinates (plot location = anchor + offset), each point's distance
  // from where the clip starts on a log scale so the whole dive fits: galaxies to ground
  const loc = p => [p.a[0] + p.o[0], p.a[1] + p.o[1], p.a[2] + p.o[2]], o0 = loc(P[0]);
  const xy = p => { const l = loc(p), dx = l[0] - o0[0], dz = l[2] - o0[2], r = Math.hypot(dx, dz), s = r > 0 ? Math.log10(1 + r) / r : 0; return [dx * s, dz * s]; };
  let mx = 1e-9; for (const p of P) { const q = xy(p); mx = Math.max(mx, Math.abs(q[0]), Math.abs(q[1])); }
  const sc = Math.min(W, H) * 0.42 / mx, X = q => W / 2 + q[0] * sc, Y = q => H / 2 - q[1] * sc;
  cx.strokeStyle = 'rgba(120, 150, 255, 0.35)'; cx.beginPath(); P.forEach((p, i) => { const q = xy(p); i ? cx.lineTo(X(q), Y(q)) : cx.moveTo(X(q), Y(q)); }); cx.stroke();
  cx.strokeStyle = 'rgba(160, 220, 255, 0.95)'; cx.lineWidth = 2; cx.beginPath(); for (let i = 0; i <= k; i++) { const q = xy(P[i]); i ? cx.lineTo(X(q), Y(q)) : cx.moveTo(X(q), Y(q)); } cx.stroke(); cx.lineWidth = 1;
  const p = P[k], q = xy(p); cx.fillStyle = '#fff'; cx.beginPath(); cx.arc(X(q), Y(q), 4, 0, 7); cx.fill();
  cx.strokeStyle = '#ffd27a'; cx.beginPath(); cx.moveTo(X(q), Y(q)); cx.lineTo(X(q) + p.fwd[0] * 24, Y(q) - p.fwd[2] * 24); cx.stroke();
  const ev = u.events.filter(e => e.ms <= t).slice(-8).map(e => (e.ms / 1000).toFixed(1) + ' s  ' + e.name + '(' + e.args.map(a => typeof a === 'number' ? +a.toPrecision(4) : String(a)).join(', ') + ')');
  cx.fillStyle = '#d8dbe6'; cx.font = '14px system-ui, sans-serif';
  const lines = ['A clip of the universe itself: ' + (head.seed ? 'seed ' + head.seed : 'no seed recorded') + (head.tour ? ', tour ' + head.tour : ''),
    (t / 1000).toFixed(1) + ' of ' + (P[P.length - 1].ms / 1000).toFixed(1) + ' s · the camera\'s track, seen from above (log distance from its start)',
    'scale: space ' + p.space.toPrecision(3) + ', far ' + p.far.toPrecision(3) + ', matter ' + p.matter.toPrecision(3) + ' · galaxy ' + p.near + ', star system ' + p.sys + ', ground ' + p.planet,
    'The universe is made again from the seed by DNA-AND itself: open this file in the program to fly it as filmed.'];
  lines.forEach((s, i) => cx.fillText(s, 14, 24 + i * 20));
  cx.fillStyle = 'rgba(216, 219, 230, 0.75)'; cx.font = '12px ui-monospace, monospace';
  ev.forEach((s, i) => cx.fillText(s.length > 140 ? s.substring(0, 140) + '...' : s, 14, H - 16 - (ev.length - 1 - i) * 16));
}

// ── the page ──
function page() {
  const $ = id => document.getElementById(id);
  let stop = null, paused = false, seekTo = null, length = 1;
  const note = s => { $('note').textContent = s; };
  async function open(buf, name) {
    if (stop) stop();
    // our own WebM: its picture track played by the page's own video element; the universe's records read for what the clip holds
    if (isWebm(buf)) {
      const recs = findAttachment(buf, ATTACH_NAME);
      const old = $('pic'), video = document.createElement('video'); video.id = 'pic'; video.loop = true; video.autoplay = true; video.muted = true; video.controls = true;
      const url = URL.createObjectURL(new Blob([buf], { type: 'video/webm' })); video.src = url; old.replaceWith(video);
      stop = () => { video.pause(); URL.revokeObjectURL(url); };
      let what = 'its picture track';
      if (recs) { try { const h = readHeader(recs); $('info').textContent = h.meta; const u = await readUniverse(recs, h); what += ' · inside it the universe itself: ' + u.points.length + ' camera points, ' + u.events.length + ' scene calls'; } catch (e) { what += ' (its records could not be read: ' + e.message + ')'; } }
      note(name + ' · ' + what); return;
    }
    let head; try { head = readHeader(buf); } catch (e) { note(name + ': ' + e.message); return; }
    const old = $('pic'), canvas = document.createElement('canvas'); canvas.id = 'pic'; old.replaceWith(canvas);
    $('info').textContent = head.meta;
    const metaOf = k => { const l = head.meta.split('\n').find(x => x.startsWith(k + ': ')); return l ? l.substring(k.length + 2).trim() : null; };
    head.seed = metaOf('seed'); head.tour = metaOf('tour');
    let t0 = performance.now(), clock = 0, alive = true; stop = () => { alive = false; };
    if (head.kind === 'universe') {
      const u = await readUniverse(buf, head); if (!u.points.length) { note(name + ': no camera points'); return; }
      length = u.points[u.points.length - 1].ms + 1000;
      const fit = () => { canvas.width = $('view').clientWidth; canvas.height = $('view').clientHeight; }; fit(); window.onresize = fit;
      const cx = canvas.getContext('2d');
      note(name + ' · a clip of the universe itself, ' + u.points.length + ' camera points, ' + u.events.length + ' scene calls');
      const tick = now => { if (!alive) return; if (seekTo !== null) { clock = seekTo * length; seekTo = null; } else if (!paused) clock += now - t0; t0 = now; if (clock >= length) clock = 0; drawUniverse(cx, canvas.width, canvas.height, u, clock, head); $('seek').value = (1000 * clock / length) | 0; requestAnimationFrame(tick); };
      requestAnimationFrame(tick); return;
    }
    if (!head.frames.length) { note(name + ': no frames'); return; }
    canvas.width = head.w; canvas.height = head.h; window.onresize = null;
    let draw; try { draw = makeGpu(canvas, head); } catch (e) { note(e.message); return; }
    const F = head.frames; length = F[F.length - 1].ms + 1000;
    const worker = new Worker('player.js'); worker.postMessage({ buf }, [buf]);   // (the clip handed over whole: the page keeps only its frame list)
    let busy = false, shown = -1;
    worker.onmessage = e => { busy = false; if (!alive) return; draw(e.data.planes); shown = e.data.frame; };
    stop = () => { alive = false; worker.terminate(); };
    note(name + ' · ' + head.w + ' x ' + head.h + ', ' + F.length + ' frames, ' + (length / 1000).toFixed(1) + ' s');
    const tick = now => {
      if (!alive) return;
      if (seekTo !== null) { clock = F[0].ms + seekTo * length; seekTo = null; } else if (!paused) clock += now - t0;
      t0 = now; if (clock >= length) clock = F[0].ms;                                   // (it loops)
      let i = 0; while (i + 1 < F.length && F[i + 1].ms <= clock) i++;
      if (!busy && i !== shown) { busy = true; worker.postMessage({ frame: i }); }        // (always the frame the clock has reached; the rest skipped)
      $('seek').value = (1000 * clock / length) | 0; requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }
  const openFile = file => file.arrayBuffer().then(b => open(b, file.name));
  $('file').onchange = e => { if (e.target.files[0]) openFile(e.target.files[0]); };
  $('play').onclick = () => { paused = !paused; $('play').textContent = paused ? 'Play' : 'Pause'; const v = $('pic'); if (v.tagName === 'VIDEO') { if (paused) v.pause(); else v.play(); } };
  $('meta').onclick = () => { const i = $('info'); i.style.display = i.style.display === 'block' ? 'none' : 'block'; };
  $('seek').oninput = e => { seekTo = e.target.value / 1000; };
  document.addEventListener('keydown', e => { if (e.code === 'Space') { $('play').click(); e.preventDefault(); } });
  document.addEventListener('dragover', e => { e.preventDefault(); $('drop').style.display = 'flex'; });
  document.addEventListener('dragleave', e => { if (e.target === $('drop')) $('drop').style.display = 'none'; });
  document.addEventListener('drop', e => { e.preventDefault(); $('drop').style.display = 'none'; if (e.dataTransfer.files[0]) openFile(e.dataTransfer.files[0]); });
  // a clip named in the address (?clip=clips/name.cdf97w, on the page's own server): played straight away (how the Activity opens one)
  const named = new URLSearchParams(location.search).get('clip');
  if (named) fetch(named).then(r => { if (!r.ok) throw new Error(r.status + ' ' + r.statusText); return r.arrayBuffer(); }).then(b => open(b, named.split('/').pop())).catch(e => note(named + ': ' + e.message));
}

// (the same sums, reachable from a check run outside a page)
if (typeof module !== 'undefined') module.exports = { readHeader, levelMap, unpack, unpackStrip, readUniverse, isWebm, findAttachment };
