// Universe · render · our own VP9 writer, every line ours, written from the published VP9 bitstream specification (its rules and
// its fixed tables, read into Vp9Tables.cs): the front man a chat app's player reads, in our own WebM container (WebmFile.cs).
// Version 1: profile 0, 8-bit 4:2:0, a key frame every KeyEvery frames and change frames between; each 64 x 64 superblock one block
// (split only at the frame's edges), DC prediction in key frames, the frame before (no motion) in change frames, a block with nothing
// left to code skipped, 4 x 4 transforms, the coefficients in the specification's token tree with its default odds, no loop filter. The
// writer keeps the decoder's own reconstruction (it predicts from it), so what a player shows is known here exactly.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    /// <summary>The boolean (arithmetic) coder's writing side: the exact reverse of the specification's section 9.2 reading.</summary>
    internal sealed class Vp9Bool
    {
        private readonly List<byte> _b = new List<byte>(); private uint _low; private int _range = 255, _count = -24;
        public void Put(int bit, int prob)
        {
            uint split = 1 + (((uint)(_range - 1) * (uint)prob) >> 8);
            if (bit != 0) { _low += split; _range -= (int)split; } else _range = (int)split;
            int shift = 0; while ((_range << shift) < 128) shift++;
            _range <<= shift; _count += shift;
            if (_count >= 0)
            {
                int offset = shift - _count;
                if (((_low << (offset - 1)) & 0x80000000u) != 0) { int x = _b.Count - 1; while (x >= 0 && _b[x] == 0xFF) { _b[x] = 0; x--; } _b[x]++; }
                _b.Add((byte)(_low >> (24 - offset)));
                _low <<= offset; shift = _count; _low &= 0xFFFFFF; _count -= 8;
            }
            _low <<= shift;
        }
        public void Lit(int v, int n) { for (int i = n - 1; i >= 0; i--) Put((v >> i) & 1, 128); }
        /// <summary>Ends the coder: enough zero bits to carry every pending bit out, and a last byte that is never a superframe marker.</summary>
        public byte[] Finish() { for (int i = 0; i < 32; i++) Put(0, 128); if (_b.Count == 0 || (_b[_b.Count - 1] & 0xE0) == 0xC0) _b.Add(0); return _b.ToArray(); }
    }

    internal sealed class Vp9Writer : IDisposable
    {
        // ── plain bits, most significant first (the uncompressed header) ──
        private sealed class Raw { public readonly List<byte> B = new List<byte>(); int _c, _n; public void U(int n, long v) { for (int i = n - 1; i >= 0; i--) { _c = (_c << 1) | (int)((v >> i) & 1); if (++_n == 8) { B.Add((byte)_c); _c = 0; _n = 0; } } } public void Align() { while (_n != 0) U(1, 0); } }

        static readonly int[] TokenTree = { 0, 2, -1, 4, 6, 10, -2, 8, -3, -4, 12, 14, -5, -6, 16, 18, -7, -8, -9, -10 };
        static readonly int[] PartitionTree = { 0, 2, -1, 4, -2, -3 };   // NONE, HORZ, VERT, SPLIT

        private readonly string _path; private readonly int _w, _h, _fw, _fh, _miCols, _miRows, _sbCols, _sbRows, _q;
        private readonly int _dcq, _acq;
        /// <summary>The picture a player shows for the last frame written (Y, then Cb, then Cr), as the decoder rebuilds it; rows of
        /// <see cref="Stride"/> (Y) and <see cref="Stride"/>/2 (Cb, Cr).</summary>
        public byte[] Y, Cb, Cr;
        public int Stride { get { return _sbCols * 64; } }
        private readonly List<byte[]> _frames = new List<byte[]>(); private readonly List<uint> _times = new List<uint>(); private uint _t;
        public long Bytes { get { long n = 0; foreach (var f in _frames) n += f.Length; return n; } }

        /// <summary>A whole frame every this many; the rest change frames (a block unchanged since the frame before is copied).</summary>
        public int KeyEvery = 30;
        private int _count; private byte[][] _prev = new byte[3][]; private byte[] _skips;
        private readonly List<bool> _keys = new List<bool>();

        public Vp9Writer(string path, int w, int h, int qIndex = 100)
        {
            _path = path; _w = w & ~1; _h = h & ~1; _fw = (_w + 7) & ~7; _fh = (_h + 7) & ~7;
            _miCols = _fw / 8; _miRows = _fh / 8; _sbCols = (_miCols + 7) / 8; _sbRows = (_miRows + 7) / 8;
            _q = Math.Max(1, Math.Min(255, qIndex)); _dcq = Vp9Tables.DcQLookup[_q]; _acq = Vp9Tables.AcQLookup[_q];
            Y = new byte[_sbCols * 64 * _sbRows * 64]; Cb = new byte[Y.Length / 4]; Cr = new byte[Y.Length / 4];
            _skips = new byte[_miRows * _miCols];
        }

        // the frame being written: its source planes, the contexts the decoder keeps
        private int[][] _src = new int[3][]; private byte[][] _rec = new byte[3][];
        private byte[][] _aboveNz = new byte[3][], _leftNz = new byte[3][]; private byte[] _abovePart, _leftPart;
        private Vp9Bool _bc; private bool _key;

        /// <summary>One frame (RGB, three bytes a pixel, top row first, <paramref name="srcW"/> wide), shown for <paramref name="ms"/>.</summary>
        public void Add(byte[] rgb, int srcW, int srcH, uint ms) { AddPrepared(Prepare(rgb, srcW, srcH), ms); }
        /// <summary>A frame (RGB, top row first) as this writer's Y, Cb and Cr planes (studio range, 4:2:0, padded); it touches nothing
        /// of the writer's state, so any thread may call it.</summary>
        public byte[][] Prepare(byte[] rgb, int srcW, int srcH)
        {
            int S = Stride, CS = S / 2; var pl = new[] { new byte[Y.Length], new byte[Cb.Length], new byte[Cr.Length] };
            for (int y = 0; y < _fh; y++) for (int x = 0; x < _fw; x++)
            {
                int ox = Math.Min(srcW - 1, Math.Min(x, _w - 1) * srcW / _w), oy = Math.Min(srcH - 1, Math.Min(y, _h - 1) * srcH / _h), o = (oy * srcW + ox) * 3;
                double r = rgb[o], g = rgb[o + 1], bb = rgb[o + 2];
                pl[0][y * S + x] = (byte)Clip(16 + (65.738 * r + 129.057 * g + 25.064 * bb) / 256);
                if ((x & 1) == 0 && (y & 1) == 0) { pl[1][(y / 2) * CS + x / 2] = (byte)Clip(128 + (-37.945 * r - 74.494 * g + 112.439 * bb) / 256); pl[2][(y / 2) * CS + x / 2] = (byte)Clip(128 + (112.439 * r - 94.154 * g - 18.285 * bb) / 256); }
            }
            return pl;
        }
        /// <summary>The last frame written, as its bytes, and whether it was a whole one.</summary>
        public byte[] LastFrame { get { return _frames.Count > 0 ? _frames[_frames.Count - 1] : null; } }
        public bool LastKey { get { return _keys.Count > 0 && _keys[_keys.Count - 1]; } }
        /// <summary>A frame already in this writer's planes (<see cref="Prepare"/>), shown for <paramref name="ms"/>.</summary>
        public void AddPrepared(byte[][] planes, uint ms)
        {
            for (int p = 0; p < 3; p++) { var a = planes[p]; var d = _src[p] = new int[a.Length]; for (int i = 0; i < a.Length; i++) d[i] = a[i]; }
            _key = _count % KeyEvery == 0; _count++;
            _rec[0] = Y; _rec[1] = Cb; _rec[2] = Cr;
            if (!_key) for (int p = 0; p < 3; p++) _prev[p] = (byte[])_rec[p].Clone();   // (the reference: the frame before, as the decoder holds it)
            _aboveNz[0] = new byte[_sbCols * 16]; _aboveNz[1] = new byte[_sbCols * 8]; _aboveNz[2] = new byte[_sbCols * 8];
            _leftNz[0] = new byte[_sbRows * 16]; _leftNz[1] = new byte[_sbRows * 8]; _leftNz[2] = new byte[_sbRows * 8];
            _abovePart = new byte[_sbCols * 8]; _leftPart = new byte[_sbRows * 8]; Array.Clear(_skips, 0, _skips.Length);

            // the tile (one): every superblock, row by row
            _bc = new Vp9Bool(); _bc.Put(0, 128);                                       // (the marker bit init_bool reads)
            for (int r = 0; r < _miRows; r += 8)
            {
                Array.Clear(_leftNz[0], 0, _leftNz[0].Length); Array.Clear(_leftNz[1], 0, _leftNz[1].Length); Array.Clear(_leftNz[2], 0, _leftNz[2].Length); Array.Clear(_leftPart, 0, _leftPart.Length);
                for (int c = 0; c < _miCols; c += 8) Partition(r, c, 3);
            }
            byte[] tile = _bc.Finish();

            // the compressed header: transforms 4 x 4 only, every default odds kept (each "update?" answered no)
            var ch = new Vp9Bool(); ch.Put(0, 128);
            ch.Lit(0, 2);                                                               // tx_mode ONLY_4X4
            ch.Lit(0, 1);                                                               // the 4 x 4 coefficient odds
            int keep = 3;                                                               // the skip odds
            if (!_key) keep += 7 * 3 + 4 + 5 * 2 + 4 * 9 + 16 * 3 + 3 + 2 * (1 + 10 + 1 + 10) + 2 * (2 * 3 + 3);   // inter modes, is-inter, single reference, y modes, partitions, motion
            for (int i = 0; i < keep; i++) ch.Put(0, 252);
            byte[] comp = ch.Finish();

            // the uncompressed header
            var u = new Raw();
            u.U(2, 2); u.U(1, 0); u.U(1, 0); u.U(1, 0);                                // frame marker; profile 0; not a shown-again frame
            u.U(1, _key ? 0 : 1); u.U(1, 1); u.U(1, 0);                                // key or not; shown; not error resilient
            if (_key)
            {
                u.U(8, 0x49); u.U(8, 0x83); u.U(8, 0x42);                              // the sync code
                u.U(3, 1); u.U(1, 0);                                                  // colour: BT.601, studio range
                u.U(16, _fw - 1); u.U(16, _fh - 1);
            }
            else
            {
                u.U(2, 0);                                                             // reset_frame_context: none
                u.U(8, 1);                                                             // this frame becomes reference 0
                for (int i = 0; i < 3; i++) { u.U(3, 0); u.U(1, 0); }                  // last, golden and altref all reference 0, no sign bias
                u.U(1, 1);                                                             // its size: reference 0's
            }
            if (_fw != _w || _fh != _h) { u.U(1, 1); u.U(16, _w - 1); u.U(16, _h - 1); } else u.U(1, 0);   // the size shown
            if (!_key) { u.U(1, 0); u.U(1, 0); u.U(2, 1); }                            // no 1/8 motion; one filter, EIGHTTAP
            u.U(1, 0); u.U(1, 1); u.U(2, 0);                                           // no odds saved back; frame-parallel; context 0
            u.U(6, 0); u.U(3, 0); u.U(1, 0);                                           // no loop filter
            u.U(8, _q); u.U(1, 0); u.U(1, 0); u.U(1, 0);                               // the quantiser, no deltas
            u.U(1, 0);                                                                 // no segmentation
            int maxLog2 = 1; while ((_sbCols >> maxLog2) >= 4) maxLog2++; maxLog2--;
            int minLog2 = 0; while ((64 << minLog2) < _sbCols) minLog2++;
            if (minLog2 > 0) throw new InvalidOperationException("wider than one VP9 tile column holds");
            if (maxLog2 > 0) u.U(1, 0);                                                // tile columns: the fewest
            u.U(1, 0);                                                                 // one tile row
            u.U(16, comp.Length); u.Align();
            var f = new byte[u.B.Count + comp.Length + tile.Length];
            u.B.CopyTo(f); Array.Copy(comp, 0, f, u.B.Count, comp.Length); Array.Copy(tile, 0, f, u.B.Count + comp.Length, tile.Length);
            _frames.Add(f); _times.Add(_t); _keys.Add(_key); _t += Math.Max(1u, ms);
        }

        // decode_partition, written: one block wherever it fits whole, split at the frame's edges
        void Partition(int r, int c, int bsl)
        {
            if (r >= _miRows || c >= _miCols) return;
            int num8 = 1 << bsl, half = num8 >> 1;
            bool hasRows = r + half < _miRows, hasCols = c + half < _miCols;
            int above = 0, left = 0; for (int i = 0; i < num8; i++) { above |= _abovePart[c + i]; left |= _leftPart[((r & 7) + i) & 7]; }
            int boff = 3 - bsl; int ctx = bsl * 4 + (((left & (1 << boff)) > 0) ? 2 : 0) + (((above & (1 << boff)) > 0) ? 1 : 0);
            var probs = _key ? Vp9Tables.KfPartitionProbs : Vp9Tables.DefaultPartitionProbs; int pbase = ctx * 3;
            bool split;
            if (hasRows && hasCols) { _bc.Put(0, probs[pbase]); split = false; }
            else if (hasCols) { _bc.Put(1, probs[pbase + 1]); split = true; }
            else if (hasRows) { _bc.Put(1, probs[pbase + 2]); split = true; }
            else split = true;
            if (split)
            {
                Partition(r, c, bsl - 1); Partition(r, c + half, bsl - 1); Partition(r + half, c, bsl - 1); Partition(r + half, c + half, bsl - 1);
                return;
            }
            Block(r, c, bsl);
            byte v = (byte)(15 >> (bsl + 1));
            for (int i = 0; i < num8; i++) { if (c + i < _abovePart.Length) _abovePart[c + i] = v; if ((r & 7) + i < 8) _leftPart[(r & 7) + i] = v; }
        }

        private sealed class Tx { public int P, X, Y; public int[] Coef; public int Eob; }
        private readonly List<Tx> _txs = new List<Tx>();

        // decode_block, written. A key frame's block: DC prediction for luma and chroma. A change frame's: the last frame, no motion
        // (ZEROMV), so the frame before is the prediction. The residual worked out and rebuilt first; if nothing of it is left after
        // quantising, the block is skipped (in a change frame: copied as it was).
        void Block(int r, int c, int bsl)
        {
            bool availU = r > 0, availL = c > 0;
            _txs.Clear(); int eobTotal = 0;
            for (int p = 0; p < 3; p++)
            {
                int sub = p > 0 ? 1 : 0, n4 = (2 << bsl) >> sub, baseX = (c * 8) >> sub, baseY = (r * 8) >> sub;
                int maxx = (_miCols * 8) >> sub, maxy = (_miRows * 8) >> sub;
                for (int y = 0; y < n4; y++) for (int x = 0; x < n4; x++)
                {
                    int sx = baseX + 4 * x, sy = baseY + 4 * y;
                    var t = new Tx { P = p, X = sx, Y = sy };
                    if (sx < maxx && sy < maxy) { Tx4(t, availL || x > 0, availU || y > 0); eobTotal += t.Eob; }
                    _txs.Add(t);
                }
            }
            int skip = eobTotal == 0 ? 1 : 0;
            int sctx = (availU ? _skips[(r - 1) * _miCols + c] : 0) + (availL ? _skips[r * _miCols + c - 1] : 0);
            _bc.Put(skip, Vp9Tables.DefaultSkipProb[sctx]);
            if (_key)
            {
                _bc.Put(0, Vp9Tables.KfYModeProbs[0]);                                 // DC_PRED (above and left DC_PRED)
                _bc.Put(0, Vp9Tables.KfUvModeProbs[0]);                                // DC_PRED for chroma
            }
            else
            {
                _bc.Put(1, Vp9Tables.DefaultIsInterProb[0]);                           // inter (the neighbours inter too: context 0)
                _bc.Put(0, Vp9Tables.DefaultSingleRefProb[(availU || availL ? 4 : 2) * 2]);   // the last frame
                // the inter mode's context: the two nearest candidates (mv_ref_blocks), each a ZEROMV block inside the frame
                int[] cand = bsl == 3 ? new[] { -1, 3, 3, -1 } : bsl == 2 ? new[] { -1, 1, 1, -1 } : new[] { -1, 0, 0, -1 };
                int counter = 0; for (int k = 0; k < 4; k += 2) { int cr = r + cand[k], cc = c + cand[k + 1]; if (cr >= 0 && cr < _miRows && cc >= 0 && cc < _miCols) counter += 3; }
                int mctx = counter == 0 ? 2 : counter == 3 ? 1 : 0;                    // BOTH_PREDICTED, ZERO_PLUS_PREDICTED, BOTH_ZERO
                _bc.Put(0, Vp9Tables.DefaultInterModeProbs[mctx * 3]);                 // ZEROMV
            }
            int n8 = 1 << bsl;                                                         // (an inter block with nothing coded counts as skipped)
            for (int y = 0; y < n8; y++) for (int x = 0; x < n8; x++) if (r + y < _miRows && c + x < _miCols) _skips[(r + y) * _miCols + c + x] = (byte)skip;
            foreach (var t in _txs)
            {
                int sub = t.P > 0 ? 1 : 0; byte nz = 0;
                if (skip == 0 && t.Coef != null) nz = Tokens(t);
                _aboveNz[t.P][t.X >> 2] = nz; _leftNz[t.P][(t.Y >> 2) & ((16 >> sub) - 1)] = nz;
            }
        }

        // one 4 x 4: predicted, its residual moved to frequencies, quantised, and rebuilt as the decoder will
        void Tx4(Tx t, bool haveLeft, bool haveAbove)
        {
            int p = t.P, x0 = t.X, y0 = t.Y;
            int S = p == 0 ? Stride : Stride / 2; var rec = _rec[p]; var src = _src[p];
            var pred = new int[16];
            if (_key)
            {
                int sa = 0, sl = 0, dc; for (int k = 0; k < 4; k++) { if (haveAbove) sa += rec[(y0 - 1) * S + x0 + k]; if (haveLeft) sl += rec[(y0 + k) * S + x0 - 1]; }
                if (haveAbove && haveLeft) dc = (sa + sl + 4) >> 3; else if (haveLeft) dc = (sl + 2) >> 2; else if (haveAbove) dc = (sa + 2) >> 2; else dc = 128;
                for (int k = 0; k < 16; k++) pred[k] = dc;
            }
            else { var pv = _prev[p]; for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) pred[i * 4 + j] = pv[(y0 + i) * S + x0 + j]; }
            // the forward transform: 8 x the orthonormal DCT (the specification's inverse is 1/8 of its transpose)
            var res = new double[16]; for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) res[i * 4 + j] = src[(y0 + i) * S + x0 + j] - pred[i * 4 + j];
            var coef = new int[16]; int eob = 0;
            for (int u = 0; u < 4; u++) for (int v = 0; v < 4; v++)
            {
                double s = 0; for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) s += D[u, i] * res[i * 4 + j] * D[v, j];
                double qd = (u == 0 && v == 0) ? _dcq : _acq; double a = Math.Abs(s * 8) / qd;
                int qv = (int)(a + ((u | v) == 0 ? 0.5 : 0.36)); coef[u * 4 + v] = s < 0 ? -qv : qv;
            }
            var scan = Vp9Tables.DefaultScan4x4;
            for (int k = 0; k < 16; k++) if (coef[scan[k]] != 0) eob = k + 1;
            t.Coef = coef; t.Eob = eob;
            // rebuilt exactly as the decoder rebuilds it (sections 8.6.2 and 8.7)
            var dq = new int[16]; for (int k = 0; k < 16; k++) dq[k] = coef[k] * (k == 0 ? _dcq : _acq);
            if (eob > 0) Inverse4(dq); else Array.Clear(dq, 0, 16);
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { int v = pred[i * 4 + j] + dq[i * 4 + j]; rec[(y0 + i) * S + x0 + j] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }
        }

        // one 4 x 4's coefficients in the token tree (section 6.4.24), its contexts as the decoder works them out
        byte Tokens(Tx t)
        {
            int p = t.P, x0 = t.X, y0 = t.Y, eob = t.Eob; var coef = t.Coef; var scan = Vp9Tables.DefaultScan4x4;
            int pl = p > 0 ? 1 : 0, rf = _key ? 0 : 1;
            int ctx0 = _aboveNz[p][x0 >> 2] + _leftNz[p][(y0 >> 2) & ((p > 0 ? 8 : 16) - 1)];
            var cache = new int[16]; bool checkEob = true;
            for (int c = 0; c < 16; c++)
            {
                int pos = scan[c], band = Vp9Tables.Coefband4x4[c], ctx;
                if (c == 0) ctx = ctx0;
                else
                {
                    int i = pos >> 2, j = pos & 3, nb0, nb1;
                    if (i > 0 && j > 0) { nb0 = (i - 1) * 4 + j; nb1 = i * 4 + j - 1; } else if (i > 0) nb0 = nb1 = (i - 1) * 4 + j; else nb0 = nb1 = i * 4 + j - 1;
                    ctx = (1 + cache[nb0] + cache[nb1]) >> 1;
                }
                int pb = ((((0 * 2 + pl) * 2 + rf) * 6 + band) * 6 + ctx) * 3;
                if (checkEob) { _bc.Put(c < eob ? 1 : 0, Vp9Tables.DefaultCoefProbs[pb]); if (c >= eob) break; }
                int val = coef[pos], av = Math.Abs(val), tok = TokenOf(av);
                PutToken(tok, Vp9Tables.DefaultCoefProbs[pb + 1], Vp9Tables.DefaultCoefProbs[pb + 2]);
                cache[pos] = Vp9Tables.EnergyClass[tok];
                if (tok == 0) checkEob = false;
                else
                {
                    if (tok >= 5)
                    {
                        int cat = Vp9Tables.ExtraBits[tok * 3], ne = Vp9Tables.ExtraBits[tok * 3 + 1], bse = Vp9Tables.ExtraBits[tok * 3 + 2]; var cp = CatProbs(cat); int ex = av - bse;
                        for (int e = 0; e < ne; e++) _bc.Put((ex >> (ne - 1 - e)) & 1, cp[e]);
                    }
                    _bc.Put(val < 0 ? 1 : 0, 128); checkEob = true;
                }
            }
            return (byte)(eob > 0 ? 1 : 0);
        }

        static int TokenOf(int a) { if (a < 5) return a; if (a < 7) return 5; if (a < 11) return 6; if (a < 19) return 7; if (a < 35) return 8; if (a < 67) return 9; return 10; }
        static byte[] CatProbs(int cat) { switch (cat) { case 1: return Vp9Tables.CatProbs1; case 2: return Vp9Tables.CatProbs2; case 3: return Vp9Tables.CatProbs3; case 4: return Vp9Tables.CatProbs4; case 5: return Vp9Tables.CatProbs5; default: return Vp9Tables.CatProbs6; } }
        // the token tree, each node's odds: node 0 and 1 the coefficient odds, the rest the Pareto table's (section 9.3.2)
        void PutToken(int tok, int p1, int p2)
        {
            var path = new List<int>(); Find(0, tok, path);
            for (int k = 0; k < path.Count; k += 2)
            {
                int node = path[k] >> 1, prob;
                if (node == 0) prob = p1; else if (node == 1) prob = p2;
                else { int x = (p2 - 1) / 2; prob = (p2 & 1) != 0 ? Vp9Tables.ParetoTable[x * 8 + node - 2] : (Vp9Tables.ParetoTable[x * 8 + node - 2] + Vp9Tables.ParetoTable[(x + 1) * 8 + node - 2]) >> 1; }
                _bc.Put(path[k + 1], prob);
            }
        }
        static bool Find(int n, int v, List<int> path)
        {
            for (int b = 0; b < 2; b++)
            {
                int ch = TokenTree[n + b]; path.Add(n); path.Add(b);
                if (ch <= 0 ? -ch == v : Find(ch, v, path)) return true;
                path.RemoveRange(path.Count - 2, 2);
            }
            return false;
        }

        // ── the inverse transform, section 8.7 (4 x 4 DCT both ways), exactly ──
        static readonly double[,] D = MakeD();
        static double[,] MakeD() { var d = new double[4, 4]; for (int k = 0; k < 4; k++) for (int n = 0; n < 4; n++) d[k, n] = (k == 0 ? 0.5 : Math.Sqrt(0.5)) * Math.Cos((2 * n + 1) * k * Math.PI / 8); return d; }
        static int Cos64(int angle) { int a = angle & 127; var t = Vp9Tables.Cos64Lookup; if (a <= 32) return t[a]; if (a <= 64) return -t[64 - a]; if (a <= 96) return -t[a - 64]; return t[128 - a]; }
        static int Sin64(int angle) { return Cos64(angle - 32); }
        static long R14(long x) { return (x + (1 << 13)) >> 14; }
        static void B(int[] T, int a, int b, int angle, bool flip)
        {
            long x = (long)T[a] * Cos64(angle) - (long)T[b] * Sin64(angle), y = (long)T[a] * Sin64(angle) + (long)T[b] * Cos64(angle);
            T[a] = (int)R14(x); T[b] = (int)R14(y); if (flip) { int s = T[a]; T[a] = T[b]; T[b] = s; }
        }
        static void H(int[] T, int a, int b) { int x = T[a], y = T[b]; T[a] = x + y; T[b] = x - y; }
        static void Idct4(int[] T)
        {
            int t1 = T[1]; T[1] = T[2]; T[2] = t1;                                     // the bit-reversed order (brev(2, i))
            B(T, 0, 1, 16, true); B(T, 2, 3, 24, false); H(T, 0, 3); H(T, 1, 2);
        }
        static void Inverse4(int[] d)
        {
            var T = new int[4];
            for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) T[j] = d[i * 4 + j]; Idct4(T); for (int j = 0; j < 4; j++) d[i * 4 + j] = T[j]; }
            for (int j = 0; j < 4; j++) { for (int i = 0; i < 4; i++) T[i] = d[i * 4 + j]; Idct4(T); for (int i = 0; i < 4; i++) d[i * 4 + j] = (T[i] + 8) >> 4; }
        }
        static int Clip(double v) { int i = (int)(v + 0.5); return i < 0 ? 0 : i > 255 ? 255 : i; }

        public void Dispose() { WebmFile.Write(_path, "V_VP9", null, _w, _h, _frames, _times, _keys, _t); }
    }
}
