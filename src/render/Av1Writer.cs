// Universe · render · our own AV1 writer, every line ours, written from the published AV1 bitstream specification (its rules and its
// fixed tables, read into Av1Tables.cs): the front man a chat app's player reads, in our own WebM container (WebmFile.cs).
// Version 1: profile 0, 8-bit 4:2:0, a key frame every KeyEvery frames and change frames between. The frame is padded to a multiple
// of 32 (the size shown is the real one); each 64 x 64 superblock split into four 32 x 32 blocks, each with one 32 x 32 luma
// transform and two 16 x 16 chroma ones (DCT only). In a key frame each block is DC-predicted; in a change frame each is an inter
// block on the last frame with no motion (GLOBALMV, identity), and a block with nothing left after quantising is skipped (copied).
// The coefficients in the specification's symbols with its default CDFs (no adaptation), no loop filter, no CDEF, no restoration. The writer keeps the decoder's own reconstruction, so what a player shows is known here exactly.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    /// <summary>The symbol (multi-symbol arithmetic) coder's writing side: the exact reverse of the specification's section 8.2.</summary>
    internal sealed class Av1Symbols
    {
        private readonly List<int> _d = new List<int>();   // emitted bytes, each before its carry is settled
        private ulong _low; private int _l = 15; private uint _r = 1 << 15;
        /// <summary>Codes <paramref name="s"/> with the CDF of <paramref name="n"/> symbols at <paramref name="o"/> in <paramref name="cdf"/>.</summary>
        public void Put(int s, ushort[] cdf, int o, int n)
        {
            uint prev = s == 0 ? _r : Cur(cdf, o, n, s - 1), cur = Cur(cdf, o, n, s);
            _low += _r - prev; _r = prev - cur;
            int d = 0; while ((_r << d) < (1u << 15)) d++;
            _r <<= d; _low <<= d; _l += d;
            Carry();
            while (_l >= 24) { _d.Add((int)(_low >> (_l - 8))); _low &= (1UL << (_l - 8)) - 1; _l -= 8; }
        }
        uint Cur(ushort[] cdf, int o, int n, int s)
        {
            uint f = (uint)(32768 - cdf[o + s]);
            return (((_r >> 8) * (f >> 6)) >> 1) + 4u * (uint)(n - s - 1);
        }
        void Carry() { if (_d.Count > 0 && (_low >> _l) != 0) { _d[_d.Count - 1] += (int)(_low >> _l); _low &= (1UL << _l) - 1; } }
        static readonly ushort[] Half = { 1 << 14, 1 << 15, 0 };
        public void Bit(int b) { Put(b & 1, Half, 0, 2); }
        public void Lit(int v, int n) { for (int i = n - 1; i >= 0; i--) Bit((v >> i) & 1); }
        /// <summary>Ends the coder: the shortest value inside the last interval followed by the trailing one bit, padded to a byte.</summary>
        public byte[] Finish()
        {
            // the value the decoder reads: the code bits, then a one, then zeros (section 8.2.4)
            _low = ((_low + (1UL << 15) - 1 - (1UL << 14)) >> 15 << 15) + (1UL << 14);
            Carry();
            for (int i = _d.Count - 1; i > 0; i--) if (_d[i] > 255) { _d[i - 1] += _d[i] >> 8; _d[i] &= 255; }
            var o = new List<byte>(); foreach (int v in _d) o.Add((byte)v);
            int bits = _l - 14; int acc = 0, n = 0;                                  // (the code bits and the trailing one)
            for (int i = bits - 1; i >= 0; i--) { acc = (acc << 1) | (int)((_low >> (14 + i)) & 1); if (++n == 8) { o.Add((byte)acc); acc = 0; n = 0; } }
            if (n > 0) o.Add((byte)(acc << (8 - n)));
            return o.ToArray();
        }
    }

    internal sealed class Av1Writer : IDisposable
    {
        private sealed class Raw
        {
            public readonly List<byte> B = new List<byte>(); int _c, _n;
            public void U(int n, long v) { for (int i = n - 1; i >= 0; i--) { _c = (_c << 1) | (int)((v >> i) & 1); if (++_n == 8) { B.Add((byte)_c); _c = 0; _n = 0; } } }
            public void Trailing() { U(1, 1); while (_n != 0) U(1, 0); }
            public void Align() { while (_n != 0) U(1, 0); }
        }

        private readonly string _path; private readonly int _w, _h, _fw, _fh, _miCols, _miRows, _q, _qctx;
        private readonly int _dcq, _acq;
        private readonly byte[] _seq;
        /// <summary>The picture a player shows for the last frame written (Y, then Cb, then Cr), as the decoder rebuilds it; rows of
        /// <see cref="Stride"/> (Y) and <see cref="Stride"/>/2 (Cb, Cr).</summary>
        public byte[] Y, Cb, Cr;
        public int Stride { get { return _fw; } }
        private readonly List<byte[]> _frames = new List<byte[]>(); private readonly List<uint> _times = new List<uint>(); private readonly List<bool> _keys = new List<bool>(); private uint _t;
        public long Bytes { get { long n = 0; foreach (var f in _frames) n += f.Length; return n; } }
        /// <summary>A whole frame every this many; the rest change frames (a block unchanged since the frame before is copied).</summary>
        public int KeyEvery = 30;
        private int _count; private bool _key; private byte[][] _prev = new byte[3][];

        public Av1Writer(string path, int w, int h, int qIndex = 100)
        {
            _path = path; _w = w & ~1; _h = h & ~1; _fw = (_w + 31) & ~31; _fh = (_h + 31) & ~31;
            _miCols = _fw / 4; _miRows = _fh / 4;
            _q = Math.Max(1, Math.Min(255, qIndex)); _qctx = _q <= 20 ? 0 : _q <= 60 ? 1 : _q <= 120 ? 2 : 3;
            _dcq = Av1Tables.DcQlookup[_q]; _acq = Av1Tables.AcQlookup[_q];
            Y = new byte[_fw * _fh]; Cb = new byte[Y.Length / 4]; Cr = new byte[Y.Length / 4];
            // the sequence header (section 5.5)
            var s = new Raw();
            s.U(3, 0); s.U(1, 0); s.U(1, 0);                                          // profile 0; not a still picture; the full header
            s.U(1, 0); s.U(1, 0); s.U(5, 0);                                          // no timing info; no display delays; one operating point
            s.U(12, 0); s.U(5, 13); s.U(1, 0);                                        // it covers every layer; level 5.1, main tier
            s.U(4, 15); s.U(4, 15); s.U(16, _fw - 1); s.U(16, _fh - 1);               // the largest frame size
            s.U(1, 0); s.U(1, 0); s.U(1, 0); s.U(1, 0);                               // no frame ids; 64 x 64 superblocks; no filter intra; no edge filter
            s.U(1, 0); s.U(1, 0); s.U(1, 0); s.U(1, 0); s.U(1, 0);                    // no inter-intra, masked compound, warped motion, dual filter, order hints
            s.U(1, 0); s.U(1, 0);                                                     // screen content tools: never
            s.U(1, 0); s.U(1, 0); s.U(1, 0);                                          // no superres, no CDEF, no restoration
            s.U(1, 0); s.U(1, 0); s.U(1, 0); s.U(1, 0); s.U(2, 0); s.U(1, 0);         // 8-bit; colour; no colour description; studio range; chroma position; one uv delta
            s.U(1, 0);                                                                // no film grain
            s.Trailing(); _seq = s.B.ToArray();
        }

        // the frame being written: its source planes, the contexts the decoder keeps
        private int[][] _src = new int[3][]; private byte[][] _rec = new byte[3][];
        private byte[][] _aboveLevel = new byte[3][], _leftLevel = new byte[3][], _aboveDc = new byte[3][], _leftDc = new byte[3][];
        private byte[] _skips; private Av1Symbols _sc;

        static byte[] Obu(int type, byte[] payload)
        {
            var o = new List<byte> { (byte)((type << 3) | 2) };
            long n = payload.Length; do { byte b = (byte)(n & 0x7F); n >>= 7; if (n != 0) b |= 0x80; o.Add(b); } while (n != 0);
            o.AddRange(payload); return o.ToArray();
        }

        /// <summary>One frame (RGB, three bytes a pixel, top row first, <paramref name="srcW"/> wide), shown for <paramref name="ms"/>.</summary>
        public void Add(byte[] rgb, int srcW, int srcH, uint ms)
        {
            int S = _fw, CS = S / 2;
            _src[0] = new int[Y.Length]; _src[1] = new int[Cb.Length]; _src[2] = new int[Cr.Length];
            for (int y = 0; y < _fh; y++) for (int x = 0; x < _fw; x++)
            {
                int ox = Math.Min(srcW - 1, Math.Min(x, _w - 1) * srcW / _w), oy = Math.Min(srcH - 1, Math.Min(y, _h - 1) * srcH / _h), o = (oy * srcW + ox) * 3;
                double r = rgb[o], g = rgb[o + 1], bb = rgb[o + 2];
                _src[0][y * S + x] = Clip(16 + (65.738 * r + 129.057 * g + 25.064 * bb) / 256);
                if ((x & 1) == 0 && (y & 1) == 0) { _src[1][(y / 2) * CS + x / 2] = Clip(128 + (-37.945 * r - 74.494 * g + 112.439 * bb) / 256); _src[2][(y / 2) * CS + x / 2] = Clip(128 + (112.439 * r - 94.154 * g - 18.285 * bb) / 256); }
            }
            _rec[0] = Y; _rec[1] = Cb; _rec[2] = Cr;
            _key = _count % KeyEvery == 0; _count++;
            if (!_key) for (int p = 0; p < 3; p++) _prev[p] = (byte[])_rec[p].Clone();   // (the reference: the frame before, as the decoder holds it)
            for (int p = 0; p < 3; p++) { int n = p == 0 ? _miCols : _miCols / 2, m = p == 0 ? _miRows : _miRows / 2; _aboveLevel[p] = new byte[n]; _aboveDc[p] = new byte[n]; _leftLevel[p] = new byte[m]; _leftDc[p] = new byte[m]; }
            _skips = new byte[_miRows * _miCols];

            // the tile (one): every superblock, row by row
            _sc = new Av1Symbols();
            for (int r = 0; r < _miRows; r += 16)
            {
                for (int p = 0; p < 3; p++) { Array.Clear(_leftLevel[p], 0, _leftLevel[p].Length); Array.Clear(_leftDc[p], 0, _leftDc[p].Length); }
                for (int c = 0; c < _miCols; c += 16) Superblock(r, c);
            }
            byte[] tile = _sc.Finish();

            // the frame header (section 5.9)
            var u = new Raw();
            u.U(1, 0); u.U(2, _key ? 0 : 1); u.U(1, 1);                              // not shown again; a key frame or an inter frame; shown
            if (!_key) u.U(1, 0);                                                     // not error resilient
            u.U(1, 1);                                                                // no CDF adaptation
            u.U(1, 0);                                                                // the sequence's frame size
            if (!_key)
            {
                u.U(3, 7);                                                            // no primary reference frame: the default CDFs
                u.U(8, 1);                                                            // this frame becomes reference 0
                for (int i = 0; i < 7; i++) u.U(3, 0);                                // every reference is reference 0
            }
            if (_fw != _w || _fh != _h) { u.U(1, 1); u.U(16, _w - 1); u.U(16, _h - 1); } else u.U(1, 0);   // the size shown
            if (!_key) { u.U(1, 0); u.U(1, 0); u.U(2, 0); u.U(1, 0); }               // no 1/8-sample motion; one filter (EIGHTTAP); simple motion only
            // tile info: uniform; the fewest tile columns and rows
            int sbCols = (_miCols + 15) >> 4, sbRows = (_miRows + 15) >> 4;
            int maxC = 0; while ((1 << maxC) < Math.Min(sbCols, 64)) maxC++;
            int maxR = 0; while ((1 << maxR) < Math.Min(sbRows, 64)) maxR++;
            int minC = 0; while ((64 << minC) < sbCols) minC++;
            if (minC > 0) throw new InvalidOperationException("wider than one AV1 tile column holds");
            u.U(1, 1); if (maxC > 0) u.U(1, 0); if (maxR > 0) u.U(1, 0);
            u.U(8, _q); u.U(1, 0); u.U(1, 0); u.U(1, 0); u.U(1, 0);                  // the quantiser, no deltas, no quantiser matrices
            u.U(1, 0);                                                                // no segmentation
            u.U(1, 0);                                                                // no quantiser deltas by block
            u.U(6, 0); u.U(6, 0); u.U(3, 0); u.U(1, 0);                               // no loop filter
            u.U(1, 0);                                                                // the largest transform for each block
            if (!_key) u.U(1, 0);                                                     // single references only
            u.U(1, 0);                                                                // the full transform sets
            if (!_key) for (int i = 0; i < 7; i++) u.U(1, 0);                         // no global motion
            u.Align();                                                                // (the frame header ends; the tile group starts byte aligned)
            var fp = new List<byte>(u.B); fp.AddRange(tile);
            bool key = _key;
            var tu = new List<byte>();
            if (key) tu.AddRange(Obu(1, _seq));
            tu.AddRange(Obu(6, fp.ToArray()));
            _frames.Add(tu.ToArray()); _times.Add(_t); _keys.Add(key); _t += Math.Max(1u, ms);
        }

        // a superblock: split into four 32 x 32 blocks (section 5.11.4)
        void Superblock(int r, int c)
        {
            bool availU = r > 0, availL = c > 0;
            bool hasRows = r + 8 < _miRows, hasCols = c + 8 < _miCols;
            int ctx = (availL ? 2 : 0) + (availU ? 1 : 0); int o = ctx * 11;
            var cdf = Av1Tables.PartitionW64Cdf;
            if (hasRows && hasCols) _sc.Put(3, cdf, o, 10);                           // PARTITION_SPLIT
            else if (hasCols || hasRows)
            {
                // split_or_horz / split_or_vert: a two-symbol CDF made from the partition CDF (section 8.3.2)
                int[] parts = hasCols ? new[] { 2, 3, 4, 6, 7, 9 } : new[] { 1, 3, 4, 5, 6, 8 };
                int psum = 0; foreach (int k in parts) psum += cdf[o + k] - cdf[o + k - 1];
                _sc.Put(1, new[] { (ushort)(32768 - psum), (ushort)32768, (ushort)0 }, 0, 2);
            }
            for (int k = 0; k < 4; k++)
            {
                int br = r + (k >> 1) * 8, bc = c + (k & 1) * 8;
                if (br >= _miRows || bc >= _miCols) continue;
                _sc.Put(0, Av1Tables.PartitionW32Cdf, 0, 10);                         // PARTITION_NONE (neighbours never smaller: context 0)
                Block(br, bc);
            }
        }

        private sealed class Tx { public int P, X, Y, N; public int[] Quant; public int Eob; }

        // a 32 x 32 block: its residual worked out and rebuilt first, then its mode and its coefficients written (section 5.11.5)
        void Block(int r, int c)
        {
            bool availU = r > 0, availL = c > 0;
            var txs = new Tx[3]; int eobTotal = 0;
            for (int p = 0; p < 3; p++)
            {
                int n = p == 0 ? 32 : 16, x = p == 0 ? c * 4 : c * 2, y = p == 0 ? r * 4 : r * 2;
                txs[p] = new Tx { P = p, X = x, Y = y, N = n };
                Transform(txs[p], availL, availU); eobTotal += txs[p].Eob;
            }
            int skip = eobTotal == 0 ? 1 : 0;
            int sctx = (availU ? _skips[(r - 1) * _miCols + c] : 0) + (availL ? _skips[r * _miCols + c - 1] : 0);
            _sc.Put(skip, Av1Tables.SkipCdf, sctx * 3, 2);
            if (_key)
            {
                _sc.Put(0, Av1Tables.IntraFrameYModeCdf, 0, 13);                      // DC_PRED (above and left DC_PRED)
                _sc.Put(0, Av1Tables.UvModeCflAllowedCdf, 0, 14);                     // DC_PRED for chroma
            }
            else
            {
                // an inter block on the last frame, no motion. Every block of a change frame is one, so each context has a closed
                // form: is_inter's is 0; the single-reference ones count the last frame among the available neighbours; the motion
                // vector stack finds a match above if there is a block above and to the left if there is one to the left, none of
                // them NEWMV (section 7.10.2), so NewMvContext is 0, 3 or 5
                _sc.Put(1, Av1Tables.IsInterCdf, 0, 2);
                int rctx = availU || availL ? 2 : 1;
                _sc.Put(0, Av1Tables.SingleRefCdf, (rctx * 6 + 0) * 3, 2);           // single_ref_p1: a forward reference
                _sc.Put(0, Av1Tables.SingleRefCdf, (rctx * 6 + 2) * 3, 2);           // single_ref_p3: LAST or LAST2
                _sc.Put(0, Av1Tables.SingleRefCdf, (rctx * 6 + 3) * 3, 2);           // single_ref_p4: LAST
                int close = (availU ? 1 : 0) + (availL ? 1 : 0), nctx = close == 0 ? 0 : close == 1 ? 3 : 5;
                _sc.Put(1, Av1Tables.NewMvCdf, nctx * 3, 2);                          // not NEWMV
                _sc.Put(0, Av1Tables.ZeroMvCdf, 0, 2);                                // GLOBALMV (identity: no motion)
            }
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (r + y < _miRows && c + x < _miCols) _skips[(r + y) * _miCols + c + x] = (byte)skip;
            foreach (var t in txs)
            {
                if (skip == 1)
                {
                    int x4 = t.X >> 2, y4 = t.Y >> 2, n4 = t.N >> 2;
                    for (int i = 0; i < n4; i++) { _aboveLevel[t.P][x4 + i] = 0; _aboveDc[t.P][x4 + i] = 0; _leftLevel[t.P][y4 + i] = 0; _leftDc[t.P][y4 + i] = 0; }   // (reset_block_context)
                }
                else Coeffs(t);
            }
        }

        // the 2D inverse transform's gain against the orthonormal one, measured once per size on the exact transform itself
        static readonly Dictionary<int, double> Gain = new Dictionary<int, double>();
        static double GainOf(int n)
        {
            lock (Gain)
            {
                double g; if (Gain.TryGetValue(n, out g)) return g;
                var d = new int[n * n]; d[0] = 4096; var res = Inverse(d, n);
                g = res[0] * n / 4096.0; Gain[n] = g; return g;
            }
        }

        // one transform block: predicted (DC), its residual moved to frequencies, quantised, rebuilt exactly as the decoder rebuilds it
        void Transform(Tx t, bool haveLeft, bool haveAbove)
        {
            int p = t.P, n = t.N, x0 = t.X, y0 = t.Y, S = p == 0 ? _fw : _fw / 2; var rec = _rec[p]; var src = _src[p];
            var pred = new int[n * n];
            if (_key)
            {
                int sa = 0, sl = 0, dc; for (int k = 0; k < n; k++) { if (haveAbove) sa += rec[(y0 - 1) * S + x0 + k]; if (haveLeft) sl += rec[(y0 + k) * S + x0 - 1]; }
                int lg = n == 32 ? 5 : 4;
                if (haveAbove && haveLeft) dc = (sa + sl + n) / (2 * n); else if (haveLeft) dc = (sl + (n >> 1)) >> lg; else if (haveAbove) dc = (sa + (n >> 1)) >> lg; else dc = 128;
                for (int k = 0; k < n * n; k++) pred[k] = dc;
            }
            else { var pv = _prev[p]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) pred[i * n + j] = pv[(y0 + i) * S + x0 + j]; }
            // the forward transform: the orthonormal DCT, scaled to the specification's inverse, quantised
            var res = new double[n * n]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) res[i * n + j] = src[(y0 + i) * S + x0 + j] - pred[i * n + j];
            var D = Dct(n); var tmp = new double[n * n];
            for (int i = 0; i < n; i++) for (int v = 0; v < n; v++) { double s = 0; for (int j = 0; j < n; j++) s += res[i * n + j] * D[v * n + j]; tmp[i * n + v] = s; }
            double g = GainOf(n); int den = n == 32 ? 2 : 1;
            var quant = new int[n * n];
            for (int u = 0; u < n; u++) for (int v = 0; v < n; v++)
            {
                double s = 0; for (int i = 0; i < n; i++) s += D[u * n + i] * tmp[i * n + v];
                double q = (u | v) == 0 ? _dcq : _acq; double a = Math.Abs(s / g) * den / q;
                int qv = (int)(a + ((u | v) == 0 ? 0.5 : 0.36)); quant[u * n + v] = s < 0 ? -qv : qv;
            }
            var scan = n == 32 ? Av1Tables.Scan32x32 : Av1Tables.Scan16x16; int eob = 0;
            for (int k = 0; k < n * n; k++) if (quant[scan[k]] != 0) eob = k + 1;
            t.Quant = quant; t.Eob = eob;
            var dq = new int[n * n];
            for (int k = 0; k < n * n; k++) { long d = (long)quant[k] * (k == 0 ? _dcq : _acq); long a = (Math.Abs(d) & 0xFFFFFF) / den; dq[k] = (int)Math.Max(-(1 << 15), Math.Min((1 << 15) - 1, d < 0 ? -a : a)); }
            var resid = eob > 0 ? Inverse(dq, n) : new int[n * n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { int v = pred[i * n + j] + resid[i * n + j]; rec[(y0 + i) * S + x0 + j] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }
        }

        static readonly Dictionary<int, double[]> Dcts = new Dictionary<int, double[]>();
        static double[] Dct(int n)
        {
            lock (Dcts)
            {
                double[] d; if (Dcts.TryGetValue(n, out d)) return d;
                d = new double[n * n]; for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) d[k * n + i] = (k == 0 ? Math.Sqrt(1.0 / n) : Math.Sqrt(2.0 / n)) * Math.Cos((2 * i + 1) * k * Math.PI / (2 * n));
                Dcts[n] = d; return d;
            }
        }

        // one transform block's coefficients in the specification's symbols (section 5.11.39), the contexts as the decoder keeps them
        void Coeffs(Tx t)
        {
            int p = t.P, n = t.N, eob = t.Eob, x4 = t.X >> 2, y4 = t.Y >> 2, n4 = n >> 2, bwl = n == 32 ? 5 : 4, area = n * n;
            int txSz = n == 32 ? 3 : 2, txSzCtx = txSz, ptype = p > 0 ? 1 : 0;
            int maxX4 = p == 0 ? _miCols : _miCols >> 1, maxY4 = p == 0 ? _miRows : _miRows >> 1;
            var scan = n == 32 ? Av1Tables.Scan32x32 : Av1Tables.Scan16x16;
            // all_zero
            int actx;
            if (p == 0) actx = 0;                                                     // (the block is the transform: context 0)
            else
            {
                int above = 0, left = 0;
                for (int i = 0; i < n4; i++) { if (x4 + i < maxX4) above |= _aboveLevel[p][x4 + i] | _aboveDc[p][x4 + i]; if (y4 + i < maxY4) left |= _leftLevel[p][y4 + i] | _leftDc[p][y4 + i]; }
                actx = 7 + (above != 0 ? 1 : 0) + (left != 0 ? 1 : 0);
            }
            _sc.Put(eob == 0 ? 1 : 0, Av1Tables.TxbSkipCdf, ((_qctx * 5 + txSz) * 13 + actx) * 3, 2);
            if (eob > 0 && p == 0 && !_key) _sc.Put(1, Av1Tables.InterTxTypeSet3Cdf, 3 * 3, 2);   // inter_tx_type: DCT_DCT (set 3, 32 x 32)
            int cul = 0, dcCat = 0;
            if (eob > 0)
            {
                // the end of block: its class, the class's first extra bit by CDF, the rest as bits
                int eobPt = eob; if (eob > 2) { eobPt = 2; while ((1 << (eobPt - 1)) < eob) eobPt++; }   // (class k holds 2^(k-2)+1 .. 2^(k-1))
                if (n == 32) _sc.Put(eobPt - 1, Av1Tables.EobPt1024Cdf, (_qctx * 2 + ptype) * 12, 11);
                else _sc.Put(eobPt - 1, Av1Tables.EobPt256Cdf, ((_qctx * 2 + ptype) * 2 + 0) * 10, 9);
                if (eobPt >= 3)
                {
                    int rest = eob - ((1 << (eobPt - 2)) + 1), shift = eobPt - 3;
                    _sc.Put((rest >> shift) & 1, Av1Tables.EobExtraCdf, (((_qctx * 5 + txSzCtx) * 2 + ptype) * 9 + (eobPt - 3)) * 3, 2);
                    for (int i = 1; i < Math.Max(0, eobPt - 2); i++) { int sh = Math.Max(0, eobPt - 2) - 1 - i; _sc.Bit((rest >> sh) & 1); }
                }
                // the levels, from the end back to the start
                var q = new int[area];
                for (int c = eob - 1; c >= 0; c--)
                {
                    int pos = scan[c], level = Math.Abs(t.Quant[pos]), row = pos >> bwl, col = pos - (row << bwl);
                    if (c == eob - 1)
                    {
                        int ectx = c == 0 ? 0 : c <= area / 8 ? 1 : c <= area / 4 ? 2 : 3;
                        _sc.Put(Math.Min(level, 3) - 1, Av1Tables.CoeffBaseEobCdf, (((_qctx * 5 + txSzCtx) * 2 + ptype) * 4 + ectx) * 4, 3);
                    }
                    else
                    {
                        int bctx;
                        if (pos == 0) bctx = 0;
                        else
                        {
                            int mag = 0;
                            for (int k = 0; k < 5; k++) { int rr = row + SigRef[k * 2], cc = col + SigRef[k * 2 + 1]; if (rr < n && cc < n) mag += Math.Min(Math.Abs(q[(rr << bwl) + cc]), 3); }
                            bctx = Math.Min((mag + 1) >> 1, 4) + BaseOffset[Math.Min(row, 4) * 5 + Math.Min(col, 4)];
                        }
                        _sc.Put(Math.Min(level, 3), Av1Tables.CoeffBaseCdf, (((_qctx * 5 + txSzCtx) * 2 + ptype) * 42 + bctx) * 5, 4);
                    }
                    if (level > 2)
                    {
                        int mag = 0;
                        for (int k = 0; k < 3; k++) { int rr = row + MagRef[k * 2], cc = col + MagRef[k * 2 + 1]; if (rr < n && cc < n) mag += Math.Min(q[rr * n + cc], 15); }
                        mag = Math.Min((mag + 1) >> 1, 6);
                        int rctx = pos == 0 ? mag : (row < 2 && col < 2) ? mag + 7 : mag + 14;
                        int left = level - 3;
                        for (int idx = 0; idx < 4; idx++) { int b = Math.Min(left, 3); _sc.Put(b, Av1Tables.CoeffBrCdf, (((_qctx * 5 + Math.Min(txSzCtx, 3)) * 2 + ptype) * 21 + rctx) * 5, 4); left -= b; if (b < 3) break; }
                    }
                    q[pos] = Math.Min(level, 15);
                }
                // the signs, and the rest of each large level (Exp-Golomb)
                for (int c = 0; c < eob; c++)
                {
                    int pos = scan[c], v = t.Quant[pos]; if (v == 0) continue;
                    int sign = v < 0 ? 1 : 0, level = Math.Abs(v);
                    if (c == 0)
                    {
                        int ds = 0;
                        for (int k = 0; k < n4; k++) { if (x4 + k < maxX4) { int s = _aboveDc[p][x4 + k]; if (s == 1) ds--; else if (s == 2) ds++; } if (y4 + k < maxY4) { int s = _leftDc[p][y4 + k]; if (s == 1) ds--; else if (s == 2) ds++; } }
                        int dctx = ds < 0 ? 1 : ds > 0 ? 2 : 0;
                        _sc.Put(sign, Av1Tables.DcSignCdf, ((_qctx * 2 + ptype) * 3 + dctx) * 3, 2);
                    }
                    else _sc.Bit(sign);
                    if (level > 14)
                    {
                        int x = level - 14, len = 0; while ((x >> len) > 1) len++;
                        for (int i = 0; i < len; i++) _sc.Bit(0); _sc.Bit(1);
                        for (int i = len - 1; i >= 0; i--) _sc.Bit((x >> i) & 1);
                    }
                    if (pos == 0) dcCat = sign == 1 ? 1 : 2;
                    cul += level & 0xFFFFF;
                }
                cul = Math.Min(63, cul);
            }
            for (int i = 0; i < n4; i++) { _aboveLevel[p][x4 + i] = (byte)cul; _aboveDc[p][x4 + i] = (byte)dcCat; _leftLevel[p][y4 + i] = (byte)cul; _leftDc[p][y4 + i] = (byte)dcCat; }
        }
        static readonly int[] SigRef = { 0, 1, 1, 0, 1, 1, 0, 2, 2, 0 };
        static readonly int[] MagRef = { 0, 1, 1, 0, 1, 1 };
        static readonly int[] BaseOffset = { 0, 1, 6, 6, 21, 1, 6, 6, 21, 21, 6, 6, 21, 21, 21, 6, 21, 21, 21, 21, 21, 21, 21, 21, 21 };

        // ── the inverse transform, section 7.13 (the DCT both ways, 16 and 32 points), exactly ──
        static readonly int[] Cos128Lookup = { 4096, 4095, 4091, 4085, 4076, 4065, 4052, 4036, 4017, 3996, 3973, 3948, 3920, 3889, 3857, 3822, 3784, 3745, 3703, 3659, 3612, 3564, 3513, 3461, 3406, 3349, 3290, 3229, 3166, 3102, 3035, 2967, 2896, 2824, 2751, 2675, 2598, 2520, 2440, 2359, 2276, 2191, 2106, 2019, 1931, 1842, 1751, 1660, 1567, 1474, 1380, 1285, 1189, 1092, 995, 897, 799, 700, 601, 501, 401, 301, 201, 101, 0 };
        static int Cos128(int a) { a &= 255; if (a <= 64) return Cos128Lookup[a]; if (a <= 128) return -Cos128Lookup[128 - a]; if (a <= 192) return -Cos128Lookup[a - 128]; return Cos128Lookup[256 - a]; }
        static int Sin128(int a) { return Cos128(a - 64); }
        static int Brev(int bits, int x) { int t = 0; for (int i = 0; i < bits; i++) t |= ((x >> i) & 1) << (bits - 1 - i); return t; }
        static void B(long[] T, int a, int b, int angle, int flip)
        {
            long x = T[a] * Cos128(angle) - T[b] * Sin128(angle), y = T[a] * Sin128(angle) + T[b] * Cos128(angle);
            T[a] = (x + 2048) >> 12; T[b] = (y + 2048) >> 12; if (flip != 0) { long s = T[a]; T[a] = T[b]; T[b] = s; }
        }
        static void H(long[] T, int a, int b, int flip, int r)
        {
            if (flip != 0) { int s = a; a = b; b = s; }
            long x = T[a], y = T[b], lo = -(1L << (r - 1)), hi = (1L << (r - 1)) - 1;
            T[a] = Math.Max(lo, Math.Min(hi, x + y)); T[b] = Math.Max(lo, Math.Min(hi, x - y));
        }
        static void Idct(long[] T, int n, int r)
        {
            int n0 = 1 << n; var cp = (long[])T.Clone(); for (int i = 0; i < n0; i++) T[i] = cp[Brev(n, i)];
            if (n >= 5) for (int i = 0; i < 8; i++) B(T, 16 + i, 31 - i, 6 + (Brev(3, 7 - i) << 3), 0);
            if (n >= 4) for (int i = 0; i < 4; i++) B(T, 8 + i, 15 - i, 12 + (Brev(2, 3 - i) << 4), 0);
            if (n >= 5) for (int i = 0; i < 8; i++) H(T, 16 + 2 * i, 17 + 2 * i, i & 1, r);
            if (n >= 3) for (int i = 0; i < 2; i++) B(T, 4 + i, 7 - i, 56 - 32 * i, 0);
            if (n >= 4) for (int i = 0; i < 4; i++) H(T, 8 + 2 * i, 9 + 2 * i, i & 1, r);
            if (n >= 5) for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) B(T, 30 - 4 * i - j, 17 + 4 * i + j, 24 + (j << 6) + ((1 - i) << 5), 1);
            for (int i = 0; i < 2; i++) B(T, 2 * i, 2 * i + 1, 32 + 16 * i, 1 - i);
            if (n >= 3) for (int i = 0; i < 2; i++) H(T, 4 + 2 * i, 5 + 2 * i, i, r);
            if (n >= 4) for (int i = 0; i < 2; i++) B(T, 14 - i, 9 + i, 48 + 64 * i, 1);
            if (n >= 5) for (int i = 0; i < 4; i++) for (int j = 0; j < 2; j++) H(T, 16 + 4 * i + j, 19 + 4 * i - j, i & 1, r);
            for (int i = 0; i < 2; i++) H(T, i, 3 - i, 0, r);
            if (n >= 3) B(T, 6, 5, 32, 1);
            if (n >= 4) for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) H(T, 8 + 4 * i + j, 11 + 4 * i - j, i, r);
            if (n >= 5) for (int i = 0; i < 4; i++) B(T, 29 - i, 18 + i, 48 + (i >> 1) * 64, 1);
            if (n >= 3) for (int i = 0; i < 4; i++) H(T, i, 7 - i, 0, r);
            if (n >= 4) for (int i = 0; i < 2; i++) B(T, 13 - i, 10 + i, 32, 1);
            if (n >= 5) for (int i = 0; i < 2; i++) for (int j = 0; j < 4; j++) H(T, 16 + i * 8 + j, 23 + i * 8 - j, i, r);
            if (n >= 4) for (int i = 0; i < 8; i++) H(T, i, 15 - i, 0, r);
            if (n >= 5) for (int i = 0; i < 4; i++) B(T, 27 - i, 20 + i, 32, 1);
            if (n >= 5) for (int i = 0; i < 16; i++) H(T, i, 31 - i, 0, r);
        }
        /// <summary>The 2D inverse transform (section 7.13.3) of an n x n DCT_DCT block, 8-bit.</summary>
        internal static int[] Inverse(int[] dq, int n)
        {
            int lg = n == 32 ? 5 : n == 16 ? 4 : n == 8 ? 3 : 2, rowShift = n == 4 ? 0 : n == 8 ? 1 : 2;
            var res = new long[n * n]; var T = new long[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) T[j] = dq[i * n + j];
                Idct(T, lg, 16);
                for (int j = 0; j < n; j++) { long v = rowShift > 0 ? (T[j] + (1L << (rowShift - 1))) >> rowShift : T[j]; res[i * n + j] = Math.Max(-32768, Math.Min(32767, v)); }
            }
            var o = new int[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++) T[i] = res[i * n + j];
                Idct(T, lg, 16);
                for (int i = 0; i < n; i++) o[i * n + j] = (int)((T[i] + 8) >> 4);
            }
            return o;
        }
        static int Clip(double v) { int i = (int)(v + 0.5); return i < 0 ? 0 : i > 255 ? 255 : i; }

        // the codec's private data in the container (the AV1 configuration record: version, profile and level, the colour, then the
        // sequence header)
        public void Dispose()
        {
            var cp = new List<byte> { 0x81, (byte)((0 << 5) | 13), (byte)((0 << 7) | (1 << 3) | (1 << 2)), 0 }; cp.AddRange(Obu(1, _seq));
            WebmFile.Write(_path, "V_AV1", cp.ToArray(), _w, _h, _frames, _times, _keys, _t);
        }
    }
}
