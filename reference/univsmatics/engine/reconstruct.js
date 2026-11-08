/* reconstruct.js — rebuild the actual image (native res + aspect) from its CDF 9/7
 * wavelet equations. Mirrors ftcore/reconstruct.py EXACTLY: separable multi-level 9/7
 * lifting inverse (columns then rows), reflect-padded power-of-two grid cropped to native
 * W×H, dequantize (Int16 × qscale), YCbCr→RGB. A magnitude threshold drops the weakest
 * terms (deviation control).
 */

const A97 = -1.586134342059924, B97 = -0.052980118572961, G97 = 0.882911075530934, D97 = 0.443506852043971, K97 = 1.230174104914001;

function b64bytes(s) { const bin = atob(s), n = bin.length, u = new Uint8Array(n); for (let i = 0; i < n; i++) u[i] = bin.charCodeAt(i); return u; }
function b64u32(s) { const b = b64bytes(s); return new Uint32Array(b.buffer, b.byteOffset, b.byteLength >> 2); }
function b64i16(s) { const b = b64bytes(s); return new Int16Array(b.buffer, b.byteOffset, b.byteLength >> 1); }

// Decode LEB128 varint deltas of sorted positions -> absolute Uint32 positions.
// Mirrors _enc_pos/_dec_pos in ftcore/reconstruct.py exactly. Positions are < 2^21
// (grid <= ~2M cells) so deltas are <=3 varint bytes; multiply-accumulate keeps it
// exact without relying on 32-bit shift behaviour.
function decPos(u8, count) {
  const out = new Uint32Array(count);
  let val = 0, acc = 0, shift = 0, n = 0;
  for (let i = 0; i < u8.length; i++) {
    const b = u8[i];
    acc += (b & 0x7F) * Math.pow(2, shift);
    if (b & 0x80) { shift += 7; }
    else { val += acc; out[n++] = val; acc = 0; shift = 0; }
  }
  return out;
}
function decodePositions(c, count) {
  return c.pos_enc === 'dv1' ? decPos(b64bytes(c.pos_b64), count) : b64u32(c.pos_b64);
}

// 1-D multi-level 9/7 inverse on a length-N segment accessed via get(i)/set(i)
function inv1d(get, set, N, tmp) {
  const sizes = []; let sz = N; while (sz >= 4 && sz % 2 === 0) { sizes.push(sz); sz >>= 1; }
  for (let si = sizes.length - 1; si >= 0; si--) {
    const size = sizes[si], half = size >> 1;
    for (let k = 0; k < half; k++) { tmp[2 * k] = get(k); tmp[2 * k + 1] = get(half + k); }
    for (let i = 0; i < size; i += 2) tmp[i] *= K97;
    for (let i = 1; i < size; i += 2) tmp[i] /= K97;
    tmp[0] -= 2 * D97 * tmp[1];
    for (let i = 2; i < size; i += 2) tmp[i] -= D97 * (tmp[i - 1] + tmp[i + 1]);
    for (let i = 1; i < size - 1; i += 2) tmp[i] -= G97 * (tmp[i - 1] + tmp[i + 1]);
    tmp[size - 1] -= 2 * G97 * tmp[size - 2];
    tmp[0] -= 2 * B97 * tmp[1];
    for (let i = 2; i < size; i += 2) tmp[i] -= B97 * (tmp[i - 1] + tmp[i + 1]);
    for (let i = 1; i < size - 1; i += 2) tmp[i] -= A97 * (tmp[i - 1] + tmp[i + 1]);
    tmp[size - 1] -= 2 * A97 * tmp[size - 2];
    for (let i = 0; i < size; i++) set(i, tmp[i]);
  }
}
function idwt2(a, H, W) {
  const tmpC = new Float64Array(H);
  for (let c = 0; c < W; c++) inv1d(i => a[i * W + c], (i, v) => a[i * W + c] = v, H, tmpC);
  const tmpR = new Float64Array(W);
  for (let r = 0; r < H; r++) { const base = r * W; inv1d(i => a[base + i], (i, v) => a[base + i] = v, W, tmpR); }
  return a;
}
function ycbcrToRGB(y, cb, cr) { return [y + 1.402 * (cr - 0.5), y - 0.344136 * (cb - 0.5) - 0.714136 * (cr - 0.5), y + 1.772 * (cb - 0.5)]; }

// `dev` is a 0..1 DEVIATION FRACTION (0 = exact, 1 = most abstract). The drop
// threshold is computed PER CHANNEL relative to that channel's OWN peak amplitude,
// so colour (Cb/Cr, whose coefficients are far smaller than luma Y) fades together
// WITH the luma instead of being wiped out first. Passing one absolute threshold
// scaled to Y's max — the old behaviour — killed all chroma at low deviation and
// turned every image greyscale; this keeps the colour balance as you abstract.
function reconstructImageData(rec, dev) {
  const W = rec.width, H = rec.height, W2 = rec.pad_w, H2 = rec.pad_h;
  const f = Math.max(0, Math.min(1, dev || 0));
  const chans = {};
  for (const name of ['Y', 'Cb', 'Cr']) {
    const c = rec.channels[name], val = b64i16(c.val_b64), qs = c.qscale, pos = decodePositions(c, val.length);
    let mx = 0; if (f > 0) for (let i = 0; i < val.length; i++) { const a = Math.abs(val[i] * qs); if (a > mx) mx = a; }
    const thr = f > 0 ? f * mx * 0.6 : 0;
    const flat = new Float64Array(W2 * H2);
    for (let i = 0; i < pos.length; i++) { const v = val[i] * qs; if (thr && Math.abs(v) < thr) continue; flat[pos[i]] = v; }
    idwt2(flat, H2, W2); chans[name] = flat;
  }
  const img = new ImageData(W, H), d = img.data, Y = chans.Y, Cb = chans.Cb, Cr = chans.Cr;
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const gi = y * W2 + x, o = (y * W + x) * 4, rgb = ycbcrToRGB(Y[gi], Cb[gi], Cr[gi]);
    d[o] = Math.max(0, Math.min(255, rgb[0] * 255)); d[o + 1] = Math.max(0, Math.min(255, rgb[1] * 255)); d[o + 2] = Math.max(0, Math.min(255, rgb[2] * 255)); d[o + 3] = 255;
  }
  return img;
}
function termsAboveThreshold(rec, dev) {
  const f = Math.max(0, Math.min(1, dev || 0));
  let n = 0;
  for (const name of ['Y', 'Cb', 'Cr']) {
    const c = rec.channels[name], val = b64i16(c.val_b64), qs = c.qscale;
    if (!f) { n += val.length; continue; }
    let mx = 0; for (let i = 0; i < val.length; i++) { const a = Math.abs(val[i] * qs); if (a > mx) mx = a; }
    const thr = f * mx * 0.6;
    for (let i = 0; i < val.length; i++) if (Math.abs(val[i] * qs) >= thr) n++;
  }
  return n;
}
if (typeof module !== 'undefined' && module.exports) module.exports = { reconstructImageData, termsAboveThreshold, idwt2, ycbcrToRGB, decPos, b64bytes, decodePositions };
