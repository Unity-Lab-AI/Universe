/* equationalize.js — turn a user-uploaded image into its OWN equation template, entirely in the
 * browser. No server, no upload: the picture is forward-transformed to a sparse CDF 9/7 wavelet
 * expansion (the exact inverse-pair of the decoder in reconstruct.js), the strongest coefficients
 * are kept to an energy target (so each image gets its own term count), quantised to Int16, and
 * packed with the same LEB128 delta-varint position scheme the corpus uses. The result is a
 * `reconstruction` object byte-compatible with reconstructImageData() — so the viewer, the live
 * morph and the world engine all treat a user's image exactly like a corpus piece. Pairs with
 * reconstruct.js (A97/B97/G97/D97/K97, idwt2, reconstructImageData all reused from there).
 */

// ---- forward 1-D multi-level 9/7 lifting (exact reverse of inv1d in reconstruct.js) ----
function fwd1d(get, set, N, tmp) {
  const sizes = []; let sz = N; while (sz >= 4 && sz % 2 === 0) { sizes.push(sz); sz >>= 1; }
  for (let si = 0; si < sizes.length; si++) {
    const size = sizes[si], half = size >> 1;
    for (let i = 0; i < size; i++) tmp[i] = get(i);
    // predict/update in the EXACT reverse order (and sign) of inv1d's undo passes
    tmp[size - 1] += 2 * A97 * tmp[size - 2];
    for (let i = 1; i < size - 1; i += 2) tmp[i] += A97 * (tmp[i - 1] + tmp[i + 1]);
    for (let i = 2; i < size; i += 2) tmp[i] += B97 * (tmp[i - 1] + tmp[i + 1]);
    tmp[0] += 2 * B97 * tmp[1];
    tmp[size - 1] += 2 * G97 * tmp[size - 2];
    for (let i = 1; i < size - 1; i += 2) tmp[i] += G97 * (tmp[i - 1] + tmp[i + 1]);
    for (let i = 2; i < size; i += 2) tmp[i] += D97 * (tmp[i - 1] + tmp[i + 1]);
    tmp[0] += 2 * D97 * tmp[1];
    for (let i = 0; i < size; i += 2) tmp[i] /= K97;
    for (let i = 1; i < size; i += 2) tmp[i] *= K97;
    // deinterleave: lowpass (evens) → first half, highpass (odds) → second half
    for (let k = 0; k < half; k++) { set(k, tmp[2 * k]); set(half + k, tmp[2 * k + 1]); }
  }
}
function fwd2d(a, H, W) {                 // rows then columns (idwt2 inverts as columns then rows)
  const tmpR = new Float64Array(W);
  for (let r = 0; r < H; r++) { const base = r * W; fwd1d(i => a[base + i], (i, v) => a[base + i] = v, W, tmpR); }
  const tmpC = new Float64Array(H);
  for (let c = 0; c < W; c++) fwd1d(i => a[i * W + c], (i, v) => a[i * W + c] = v, H, tmpC);
  return a;
}

function rgbToYCbCr(r, g, b) {            // inverse of ycbcrToRGB in reconstruct.js
  return [0.299 * r + 0.587 * g + 0.114 * b,
          0.5 - 0.168736 * r - 0.331264 * g + 0.5 * b,
          0.5 + 0.5 * r - 0.418688 * g - 0.081312 * b];
}
function padDim(n, m) { m = m || 64; return Math.ceil(n / m) * m; }

// ---- LEB128 delta-varint position encoder (mirror of _enc_pos in reconstruct.py) ----
function encPos(idxSorted) {
  const bytes = [];
  let prev = 0;
  for (let i = 0; i < idxSorted.length; i++) {
    let d = idxSorted[i] - prev; prev = idxSorted[i];
    while (d >= 0x80) { bytes.push((d & 0x7F) | 0x80); d = Math.floor(d / 128); }
    bytes.push(d & 0x7F);
  }
  return bytes;
}
function bytesToB64(arr) {                // Uint8 array → base64
  let s = ''; const CH = 0x8000;
  for (let i = 0; i < arr.length; i += CH) s += String.fromCharCode.apply(null, arr.slice(i, i + CH));
  return btoa(s);
}
function i16ToB64(int16) {
  return bytesToB64(new Uint8Array(int16.buffer, int16.byteOffset, int16.byteLength));
}

// ---- the analyzer: ImageData (or canvas) → a corpus-compatible `reconstruction` object ----
const EQ_LONG_EDGE = 2560;                                  // #270 deeper scan = more real "fabric" before zoom hits math (2560 is the safe-but-deep default; 3072 is opt-in via opts.longEdge in the caller)
const EQ_TOL = [0.015, 0.022, 0.022];                       // #270 tighter than before (was .030/.055/.055) — keep MORE coefficients, esp. CHROMA (Cb/Cr) for colour-gradient depth on deep zoom
const EQ_KMIN = [800, 400, 400];                            // raised chroma floor → hue holds along edges/outlines

function equationalizeImageData(img, opts) {
  // img: ImageData. Downscale to <= EQ_LONG_EDGE handled by caller (canvas draw). Here we just transform.
  // opts (optional, #270 scanner-depth): { tol:[Y,Cb,Cr], kmin:[Y,Cb,Cr] } — tighter tol = keep MORE coefficients
  // = finer real "fabric" (deeper vector/lumen/hue detail) before zoom hits upscale/math. Defaults unchanged.
  opts = opts || {};
  const TOL = opts.tol || EQ_TOL, KMIN = opts.kmin || EQ_KMIN;
  const W0 = img.width, H0 = img.height, d = img.data;
  const W2 = padDim(W0), H2 = padDim(H0);
  const names = ['Y', 'Cb', 'Cr'], chans = {};
  // build padded YCbCr planes (reflect pad, no edge repeat)
  const planes = [new Float64Array(W2 * H2), new Float64Array(W2 * H2), new Float64Array(W2 * H2)];
  const refl = (i, n) => { if (i < n) return i; let r = 2 * (n - 1) - i; if (r < 0) r = 0; return r; };
  for (let y = 0; y < H2; y++) {
    const sy = refl(y, H0);
    for (let x = 0; x < W2; x++) {
      const sx = refl(x, W0), o = (sy * W0 + sx) * 4;
      const ycc = rgbToYCbCr(d[o] / 255, d[o + 1] / 255, d[o + 2] / 255);
      const p = y * W2 + x; planes[0][p] = ycc[0]; planes[1][p] = ycc[1]; planes[2][p] = ycc[2];
    }
  }
  let totalEq = 0;
  for (let ci = 0; ci < 3; ci++) {
    const co = fwd2d(planes[ci], H2, W2);
    const n = co.length;
    // energy-target term selection (largest |coef| until (1-TOL^2) of L2 energy is captured)
    let total = 0; for (let i = 0; i < n; i++) total += co[i] * co[i];
    total = total || 1;
    const order = Array.from({ length: n }, (_, i) => i).sort((a, b) => Math.abs(co[b]) - Math.abs(co[a]));
    const target = (1 - TOL[ci] * TOL[ci]) * total;
    let acc = 0, k = 0;
    while (k < n && acc < target) { acc += co[order[k]] * co[order[k]]; k++; }
    k = Math.max(KMIN[ci], Math.min(k, n));
    const idx = order.slice(0, k).sort((a, b) => a - b);
    let maxAbs = 1e-8; for (let i = 0; i < k; i++) maxAbs = Math.max(maxAbs, Math.abs(co[idx[i]]));
    const qscale = maxAbs / 32000;
    const q = new Int16Array(k);
    for (let i = 0; i < k; i++) { let v = Math.round(co[idx[i]] / qscale); q[i] = Math.max(-32767, Math.min(32767, v)); }
    chans[names[ci]] = { keep: k, qscale, pos_enc: 'dv1',
                         pos_b64: bytesToB64(new Uint8Array(encPos(idx))), val_b64: i16ToB64(q) };
    totalEq += k;
  }
  return {
    model: 'cdf97_wavelet_native_quantized', colorspace: 'YCbCr', wavelet: 'cdf97',
    width: W0, height: H0, pad_w: W2, pad_h: H2,
    channels: chans, equation_count: totalEq, fidelity: { psnr_db: null, source: 'browser' },
  };
}

// Draw an <img>/bitmap onto a canvas capped at EQ_LONG_EDGE and return its ImageData.
function imageToCappedData(srcImg) {
  const W = srcImg.naturalWidth || srcImg.width, H = srcImg.naturalHeight || srcImg.height;
  const s = Math.min(1, EQ_LONG_EDGE / Math.max(W, H));
  const w = Math.max(1, Math.round(W * s)), h = Math.max(1, Math.round(H * s));
  const cv = document.createElement('canvas'); cv.width = w; cv.height = h;
  const cx = cv.getContext('2d'); cx.imageSmoothingEnabled = true; cx.drawImage(srcImg, 0, 0, w, h);
  return cx.getImageData(0, 0, w, h);
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { fwd1d, fwd2d, equationalizeImageData, encPos, rgbToYCbCr, padDim };
