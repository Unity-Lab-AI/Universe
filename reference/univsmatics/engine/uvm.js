/* uvm.js — the .uvm combiner-file codec (Gee #158/#159/#160).
 *
 * A .uvm IS a standard WAV (plays in any audio player) that ALSO carries the COMBINER field C in a custom
 * RIFF chunk `UVME`. Because it embeds C (the reconstruction = the lossless equation + palette), the player
 * regenerates ALL three modalities from one file: Φ_img → the EXACT image (byte-identical to the viewer),
 * Φ_snd → the music (also the audible PCM), Φ_frc → the fractal/world. The sound file IS a portable combiner.
 *
 * Layout (little-endian RIFF):
 *   'RIFF' <size> 'WAVE'
 *   'fmt ' 16  <PCM mono 16-bit @ sampleRate>          ← the music (for listening; lossy-free)
 *   'data' <n> <int16 PCM>                              ← rendered audio
 *   'UVME' <m> 'UVM1' <crc32> <utf8 JSON {v,rec,palette}>  ← the combiner C (exact). Players ignore it.
 *
 * encode({rec, palette, pcm, sampleRate}) → ArrayBuffer ;  decode(ArrayBuffer) → {rec, palette, pcm, sampleRate}
 * PNG also carries the equation (pngEmbed/pngExtract) — a tEXt chunk, so an exported .png re-imports exactly.
 */
window.UVM = (function () {
  function crc32(bytes) { let c = ~0; for (let i = 0; i < bytes.length; i++) { c ^= bytes[i]; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xEDB88320 & -(c & 1)); } return (~c) >>> 0; }

  // the combiner META (the equation + algorithm + manifest) — shared by the WAV chunk AND the PNG chunk.
  function metaJSON(o) {
    return JSON.stringify({
      schema: 'uvm-combiner@1',
      // MARK every combiner file as EQUATIONAL (Gee #248): it carries the equation field C, so its image +
      // sound faces are EQUATIONAL (re-synthesised from C), not stored media. `original_modality` says which
      // modality (if any) ALSO travels as a true ORIGINAL alongside the equation ('image'|'sound'|null).
      equational: true,
      original_modality: (o.orig && o.orig.kind) || null,
      algo: {
        image: 'I(x,y)=Σ v_k·ψ(r_k,c_k); RGB<-YCbCr; ψ=CDF 9/7 biorthogonal wavelet (lifting, inverse transform)',
        sound: 'ℓ_k=floor(log2 max(r_k,c_k,1)); f_k=F_LO*(F_HI/F_LO)^(ℓ_k/ℓmax); a_k=|v_k|/max|v|; t_k=rank_k/N*T',
        fractal: 'chaos game over IFS affine maps; zoom-fractalize = Newton-projection solve of x^3-2y^3=3'
      },
      constants: { F_LO: 40, F_HI: 3200 },
      produces: ['equation', 'image', 'png', 'jpg', 'webp', 'wav', 'sound', 'eqsound', 'fractal'],
      rec: o.rec, palette: o.palette || [], ifs: o.ifs || null,
      // the ORIGINAL the equation was made FROM, so any one file pulls the FULL combination (Gee #244):
      //   orig.kind  — 'image' | 'sound' (the source modality)
      //   orig.image — base64 PNG of the ORIGINAL image (present when an image original exists)
      //   orig.sound — base64 WAV of the ORIGINAL sound (present when a recorded sound original exists; for a
      //                sound-source .uvme the data chunk already IS the original, so this stays null there)
      // Optional + backward-compatible: pre-#244 files have no `orig` and just fall back to equation-only.
      orig: o.orig || null
    });
  }

  function encode(o) {
    const enc = new TextEncoder();
    const meta = enc.encode(metaJSON(o));
    const crc = crc32(meta);
    const sr = o.sampleRate || 44100, pcm = o.pcm || new Float32Array(0), n = pcm.length;
    const dataBytes = n * 2;
    const uvmePayload = 8 + meta.length;          // 'UVM1'(4) + crc(4) + meta
    const uvmePad = uvmePayload & 1;
    const riffSize = 4 + (8 + 16) + (8 + dataBytes) + (8 + uvmePayload + uvmePad);
    const buf = new ArrayBuffer(8 + riffSize), dv = new DataView(buf); let p = 0;
    const ws = s => { for (let i = 0; i < s.length; i++) dv.setUint8(p++, s.charCodeAt(i)); };
    const u32 = v => { dv.setUint32(p, v >>> 0, true); p += 4; }, u16 = v => { dv.setUint16(p, v, true); p += 2; };
    ws('RIFF'); u32(riffSize); ws('WAVE');
    ws('fmt '); u32(16); u16(1); u16(1); u32(sr); u32(sr * 2); u16(2); u16(16);
    ws('data'); u32(dataBytes);
    for (let i = 0; i < n; i++) { const s = Math.max(-1, Math.min(1, pcm[i])); dv.setInt16(p, s < 0 ? s * 0x8000 : s * 0x7FFF, true); p += 2; }
    ws('UVME'); u32(uvmePayload); ws('UVM1'); u32(crc);
    for (let i = 0; i < meta.length; i++) dv.setUint8(p++, meta[i]);
    if (uvmePad) dv.setUint8(p++, 0);
    return buf;
  }

  function decode(buf) {
    const dv = new DataView(buf), dec = new TextDecoder();
    const rd4 = q => { let s = ''; for (let i = 0; i < 4; i++) s += String.fromCharCode(dv.getUint8(q + i)); return s; };
    if (buf.byteLength < 12 || rd4(0) !== 'RIFF' || rd4(8) !== 'WAVE') throw new Error('not a WAV/.uvm file');
    let o = 12, pcm = null, sr = 44100, meta = null, crc = 0, gotMeta = false;
    while (o + 8 <= buf.byteLength) {
      const id = rd4(o), sz = dv.getUint32(o + 4, true), body = o + 8;
      if (id === 'fmt ') sr = dv.getUint32(body + 4, true);
      else if (id === 'data') { const n = (sz / 2) | 0; pcm = new Float32Array(n); for (let i = 0; i < n; i++) { const v = dv.getInt16(body + i * 2, true); pcm[i] = v < 0 ? v / 0x8000 : v / 0x7FFF; } }
      else if (id === 'UVME') { crc = dv.getUint32(body + 4, true); meta = dec.decode(new Uint8Array(buf, body + 8, sz - 8)); gotMeta = true; }
      o = body + sz + (sz & 1);
    }
    if (!gotMeta) throw new Error('no UVME chunk — this is plain audio, not a Uni Vs Matics combiner file');
    const check = crc32(new TextEncoder().encode(meta));
    if (check !== crc) throw new Error('UVME checksum mismatch — file is corrupt');
    const obj = JSON.parse(meta);
    return { rec: obj.rec, palette: obj.palette || [], ifs: obj.ifs || null, orig: obj.orig || null, equational: obj.equational !== false, original_modality: obj.original_modality || (obj.orig && obj.orig.kind) || null, schema: obj.schema, algo: obj.algo, pcm, sampleRate: sr };
  }

  // ── PNG carries the equation too ───────────────────────────────────────────────────────────────
  // A .png is an IMAGE (won't PLAY), but PNG (like WAV) is chunk-based — so we hide the SAME equation C
  // in a standard `tEXt` chunk (keyword "uvme"). Result: the exported .png shows as a normal picture in
  // any viewer AND re-imports here to rebuild the EXACT image (+ its sound), exactly like a .uvme.
  function b64e(u8) { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000)); return btoa(s); }
  function b64d(b) { const s = atob(b), u8 = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u8[i] = s.charCodeAt(i); return u8; }

  function pngEmbed(pngIn, o) {
    const png = pngIn instanceof Uint8Array ? pngIn : new Uint8Array(pngIn), enc = new TextEncoder();
    const kw = enc.encode('uvme'), text = enc.encode(b64e(enc.encode(metaJSON(o))));
    const data = new Uint8Array(kw.length + 1 + text.length);     // tEXt: keyword + NUL(0) + text
    data.set(kw, 0); data[kw.length] = 0; data.set(text, kw.length + 1);
    const type = enc.encode('tEXt');
    const td = new Uint8Array(type.length + data.length); td.set(type, 0); td.set(data, type.length);
    const chunk = new Uint8Array(12 + data.length), dv = new DataView(chunk.buffer);
    dv.setUint32(0, data.length, false); chunk.set(type, 4); chunk.set(data, 8); dv.setUint32(8 + data.length, crc32(td), false);
    let q = 8, iend = png.length - 12;                            // insert before IEND
    while (q + 8 <= png.length) { const len = ((png[q] << 24) | (png[q + 1] << 16) | (png[q + 2] << 8) | png[q + 3]) >>> 0; const t = String.fromCharCode(png[q + 4], png[q + 5], png[q + 6], png[q + 7]); if (t === 'IEND') { iend = q; break; } q += 12 + len; }
    const out = new Uint8Array(png.length + chunk.length);
    out.set(png.subarray(0, iend), 0); out.set(chunk, iend); out.set(png.subarray(iend), iend + chunk.length);
    return out;
  }
  function pngExtract(pngIn) {
    const png = pngIn instanceof Uint8Array ? pngIn : new Uint8Array(pngIn);
    if (png.length < 8 || png[0] !== 0x89 || png[1] !== 0x50) return null;
    let q = 8;
    while (q + 8 <= png.length) {
      const len = ((png[q] << 24) | (png[q + 1] << 16) | (png[q + 2] << 8) | png[q + 3]) >>> 0;
      const t = String.fromCharCode(png[q + 4], png[q + 5], png[q + 6], png[q + 7]);
      if (t === 'tEXt') {
        const data = png.subarray(q + 8, q + 8 + len); let z = 0; while (z < data.length && data[z] !== 0) z++;
        const kw = String.fromCharCode.apply(null, data.subarray(0, z));
        if (kw === 'uvme') { try { const sub = data.subarray(z + 1); let b = ''; for (let i = 0; i < sub.length; i += 0x8000) b += String.fromCharCode.apply(null, sub.subarray(i, i + 0x8000)); const obj = JSON.parse(new TextDecoder().decode(b64d(b))); return { rec: obj.rec, palette: obj.palette || [], ifs: obj.ifs || null, orig: obj.orig || null, equational: obj.equational !== false, original_modality: obj.original_modality || (obj.orig && obj.orig.kind) || null, schema: obj.schema }; } catch (e) { return null; } }
      }
      if (t === 'IEND') break;
      q += 12 + len;
    }
    return null;
  }

  return { encode, decode, crc32, pngEmbed, pngExtract };
})();
