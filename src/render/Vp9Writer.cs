// Universe · render · our own VP9 writer, every line ours, written from the published VP9 bitstream specification (its rules and
// its fixed tables, read into Vp9Tables.cs): the front man a chat app's player reads, in our own WebM container (WebmFile.cs).
// Version 3: profile 0, 8-bit 4:2:0, a key frame every KeyEvery frames and change frames between; each 64 x 64 superblock one block
// (split at the frame's edges, and in change frames into four 32 x 32s where their motion differs), DC prediction in key frames;
// in change frames the frame before moved by each block's own motion (searched here, in whole pairs of pixels, so the colour planes
// move by whole pixels too), coded as the specification codes it (its candidate list from the blocks around, NEARESTMV, NEARMV,
// ZEROMV or NEWMV against the best candidate); a block with nothing left to code skipped, 4 x 4 transforms, the coefficients in
// the specification's token tree with its default odds, no loop filter. Change frames are error resilient (each stands on the
// frame before alone), so no motion of an older frame enters a candidate list.
// The frame is cut into as many tile columns as the specification allows (8 at 4K), each coded on a core of its own, its contexts
// kept to its own columns as a decoder keeps them; the forward transform separable, nothing made anew per block, so a 4K frame codes
// in a few tens of milliseconds. The writer keeps the decoder's own reconstruction (it predicts from it), so what a player shows is
// known here exactly.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    /// <summary>The boolean (arithmetic) coder's writing side: the exact reverse of the specification's section 9.2 reading.</summary>
    internal sealed class Vp9Bool
    {
        private readonly List<byte> _b = new List<byte>(1 << 16); private uint _low; private int _range = 255, _count = -24;
        public void Put(int bit, int prob)
        {
            uint split = 1 + (((uint)(_range - 1) * (uint)prob) >> 8);
            if (bit != 0) { _low += split; _range -= (int)split; } else _range = (int)split;
            int shift = Norm[_range];
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
        static readonly byte[] Norm = MakeNorm();
        static byte[] MakeNorm() { var n = new byte[256]; for (int r = 1; r < 256; r++) { int s = 0; while ((r << s) < 128) s++; n[r] = (byte)s; } return n; }
        public void Lit(int v, int n) { for (int i = n - 1; i >= 0; i--) Put((v >> i) & 1, 128); }
        /// <summary>Ends the coder: enough zero bits to carry every pending bit out, and a last byte that is never a superframe marker.</summary>
        public byte[] Finish() { for (int i = 0; i < 32; i++) Put(0, 128); if (_b.Count == 0 || (_b[_b.Count - 1] & 0xE0) == 0xC0) _b.Add(0); return _b.ToArray(); }
    }

    internal sealed class Vp9Writer : IDisposable
    {
        // ── plain bits, most significant first (the uncompressed header) ──
        private sealed class Raw { public readonly List<byte> B = new List<byte>(); int _c, _n; public void U(int n, long v) { for (int i = n - 1; i >= 0; i--) { _c = (_c << 1) | (int)((v >> i) & 1); if (++_n == 8) { B.Add((byte)_c); _c = 0; _n = 0; } } } public void Align() { while (_n != 0) U(1, 0); } }

        static readonly int[] TokenTree = { 0, 2, -1, 4, 6, 10, -2, 8, -3, -4, 12, 14, -5, -6, 16, 18, -7, -8, -9, -10 };
        // each token's way down the tree, made once: pairs of (node, bit)
        static readonly int[][] TokenPath = MakePaths();
        static int[][] MakePaths()
        {
            var p = new int[11][];
            for (int t = 0; t < 11; t++) { var l = new List<int>(); Find(0, t, l); p[t] = l.ToArray(); }
            return p;
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

        private readonly string _path; private readonly int _w, _h, _fw, _fh, _miCols, _miRows, _sbCols, _sbRows, _tileLog2, _minLog2;
        private int _q, _dcq, _acq;
        /// <summary>The picture a player shows for the last frame written (Y, then Cb, then Cr), as the decoder rebuilds it; rows of
        /// <see cref="Stride"/> (Y) and <see cref="Stride"/>/2 (Cb, Cr).</summary>
        public byte[] Y, Cb, Cr;
        public int Stride { get { return _sbCols * 64; } }
        private readonly List<byte[]> _frames = new List<byte[]>(); private readonly List<uint> _times = new List<uint>(); private uint _t;
        public long Bytes { get { long n = 0; foreach (var f in _frames) n += f.Length; return n; } }

        /// <summary>A whole frame every this many; the rest change frames (a block unchanged since the frame before is copied).</summary>
        public int KeyEvery = 30;
        /// <summary>Keep every frame's bytes (for <see cref="Dispose"/>'s file); a writer feeding a track keeps only the last.</summary>
        public bool KeepFrames = true;
        private int _count; private bool _key; private readonly byte[][] _prev = new byte[3][]; private readonly byte[] _skips;
        private readonly List<bool> _keys = new List<bool>();
        private byte[] _last; private bool _lastKey;

        public Vp9Writer(string path, int w, int h, int qIndex = 100)
        {
            _path = path; _w = w & ~1; _h = h & ~1; _fw = (_w + 7) & ~7; _fh = (_h + 7) & ~7;
            _miCols = _fw / 8; _miRows = _fh / 8; _sbCols = (_miCols + 7) / 8; _sbRows = (_miRows + 7) / 8;
            _q = Math.Max(1, Math.Min(255, qIndex)); _dcq = Vp9Tables.DcQLookup[_q]; _acq = Vp9Tables.AcQLookup[_q];
            Y = new byte[_sbCols * 64 * _sbRows * 64]; Cb = new byte[Y.Length / 4]; Cr = new byte[Y.Length / 4];
            for (int p = 0; p < 3; p++) _prev[p] = new byte[p == 0 ? Y.Length : Cb.Length];
            _skips = new byte[_miRows * _miCols];
            _mvR = new short[_skips.Length]; _mvC = new short[_skips.Length]; _lastR = new short[_skips.Length]; _lastC = new short[_skips.Length]; _mode = new byte[_skips.Length];
            _qw = _fw / 4; _qh = _fh / 4; _qs = new byte[_qw * _qh]; _qr = new byte[_qw * _qh];
            // tile columns: as many as the specification allows (section 6.2.14), each coded on a core of its own
            int maxLog2 = 1; while ((_sbCols >> maxLog2) >= 4) maxLog2++; maxLog2--;
            _minLog2 = 0; while ((64 << _minLog2) < _sbCols) _minLog2++;
            _tileLog2 = Math.Max(_minLog2, maxLog2);
            _maxLog2 = maxLog2;
        }
        private readonly int _maxLog2;

        // the frame being written: its source planes and its reconstruction (shared, each tile writing only its own columns)
        private byte[][] _src = new byte[3][]; private readonly byte[][] _rec = new byte[3][];
        private byte[][] _aboveNz = new byte[3][]; private byte[] _abovePart;
        // the motion of every 8 x 8 of the frame being written (in eighths of a pixel, as coded) and its mode; the last frame's, a
        // starting guess for the search only (never in the bitstream: change frames are error resilient)
        private readonly short[] _mvR, _mvC, _lastR, _lastC; private readonly byte[] _mode;
        // the frame and the frame before, a quarter each way (each the mean of 4 x 4 pixels): the search's first, coarse look
        private byte[] _qs, _qr; private int _qw, _qh;
        const int ZeroMv = 12, NewMv = 13, NearestMv = 10, NearMv = 11;

        /// <summary>One frame (RGB, three bytes a pixel, top row first, <paramref name="srcW"/> wide), shown for <paramref name="ms"/>.</summary>
        public void Add(byte[] rgb, int srcW, int srcH, uint ms) { AddPrepared(Prepare(rgb, srcW, srcH), ms); }
        /// <summary>A frame (RGB, top row first) as this writer's Y, Cb and Cr planes (studio range, 4:2:0, padded); it touches nothing
        /// of the writer's state, so any thread may call it.</summary>
        public byte[][] Prepare(byte[] rgb, int srcW, int srcH)
        {
            int S = Stride, CS = S / 2; var pl = new[] { new byte[Y.Length], new byte[Cb.Length], new byte[Cr.Length] };
            int fw = _fw, fh = _fh, w = _w, h = _h;
            Universe.Core.Jobs.For(fh, y =>
            {
                int oy = Math.Min(srcH - 1, Math.Min(y, h - 1) * srcH / h);
                for (int x = 0; x < fw; x++)
                {
                    int ox = Math.Min(srcW - 1, Math.Min(x, w - 1) * srcW / w), o = (oy * srcW + ox) * 3;
                    int r = rgb[o], g = rgb[o + 1], b = rgb[o + 2];
                    pl[0][y * S + x] = (byte)(16 + ((16829 * r + 33039 * g + 6416 * b + 32768) >> 16));
                    if ((x & 1) == 0 && (y & 1) == 0)
                    {
                        pl[1][(y / 2) * CS + x / 2] = (byte)(128 + ((-9714 * r - 19070 * g + 28784 * b + 32768) >> 16));
                        pl[2][(y / 2) * CS + x / 2] = (byte)(128 + ((28784 * r - 24103 * g - 4681 * b + 32768) >> 16));
                    }
                }
            });
            return pl;
        }
        /// <summary>The quantiser index for the frames from here on (1 finest .. 255 coarsest): the rate control's dial.</summary>
        public int Quality { get { return _q; } set { _q = Math.Max(1, Math.Min(255, value)); _dcq = Vp9Tables.DcQLookup[_q]; _acq = Vp9Tables.AcQLookup[_q]; } }
        /// <summary>A copy of the picture as the decoder holds it now, as planes for <see cref="AddPrepared"/>: a frame made of it
        /// repeats the last one exactly (every block skipped).</summary>
        public byte[][] Recon() { return new[] { (byte[])Y.Clone(), (byte[])Cb.Clone(), (byte[])Cr.Clone() }; }
        /// <summary>The last frame written, as its bytes, and whether it was a whole one.</summary>
        public byte[] LastFrame { get { return _last; } }
        public bool LastKey { get { return _lastKey; } }

        /// <summary>A frame already in this writer's planes (<see cref="Prepare"/>), shown for <paramref name="ms"/>.</summary>
        public void AddPrepared(byte[][] planes, uint ms)
        {
            _src = planes;
            _key = _count % KeyEvery == 0; _count++;
            _rec[0] = Y; _rec[1] = Cb; _rec[2] = Cr;
            if (!_key)
            {
                for (int p = 0; p < 3; p++) Buffer.BlockCopy(_rec[p], 0, _prev[p], 0, _rec[p].Length);   // (the reference: the frame before, as the decoder holds it)
                Quarter(_src[0], _qs); Quarter(_prev[0], _qr);
            }
            Buffer.BlockCopy(_mvR, 0, _lastR, 0, _mvR.Length * 2); Buffer.BlockCopy(_mvC, 0, _lastC, 0, _mvC.Length * 2);
            if (_key) { Array.Clear(_lastR, 0, _lastR.Length); Array.Clear(_lastC, 0, _lastC.Length); }
            Array.Clear(_mvR, 0, _mvR.Length); Array.Clear(_mvC, 0, _mvC.Length); Array.Clear(_mode, 0, _mode.Length);
            _aboveNz[0] = new byte[_sbCols * 16]; _aboveNz[1] = new byte[_sbCols * 8]; _aboveNz[2] = new byte[_sbCols * 8];
            _abovePart = new byte[_sbCols * 8]; Array.Clear(_skips, 0, _skips.Length);

            // the tiles: each tile column coded on its own, at once
            int nt = 1 << _tileLog2; var tiles = new byte[nt][];
            Universe.Core.Jobs.For(nt, k =>
            {
                int c0 = Math.Min(((k * _sbCols) >> _tileLog2) << 3, _miCols), c1 = Math.Min((((k + 1) * _sbCols) >> _tileLog2) << 3, _miCols);
                tiles[k] = new Tile(this, c0, c1).Code();
            });

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
            u.U(1, _key ? 0 : 1); u.U(1, 1); u.U(1, _key ? 0 : 1);                     // key or not; shown; change frames error resilient
            if (_key)
            {
                u.U(8, 0x49); u.U(8, 0x83); u.U(8, 0x42);                              // the sync code
                u.U(3, 1); u.U(1, 0);                                                  // colour: BT.601, studio range
                u.U(16, _fw - 1); u.U(16, _fh - 1);
            }
            else
            {
                u.U(8, 1);                                                             // this frame becomes reference 0
                for (int i = 0; i < 3; i++) { u.U(3, 0); u.U(1, 0); }                  // last, golden and altref all reference 0, no sign bias
                u.U(1, 1);                                                             // its size: reference 0's
            }
            if (_fw != _w || _fh != _h) { u.U(1, 1); u.U(16, _w - 1); u.U(16, _h - 1); } else u.U(1, 0);   // the size shown
            if (!_key) { u.U(1, 0); u.U(1, 0); u.U(2, 1); }                            // no 1/8 motion; one filter, EIGHTTAP
            if (_key) { u.U(1, 0); u.U(1, 1); }                                        // no odds saved back; frame-parallel (error resilient: both so already)
            u.U(2, 0);                                                                 // context 0
            u.U(6, 0); u.U(3, 0); u.U(1, 0);                                           // no loop filter
            u.U(8, _q); u.U(1, 0); u.U(1, 0); u.U(1, 0);                               // the quantiser, no deltas
            u.U(1, 0);                                                                 // no segmentation
            for (int i = _minLog2; i < _tileLog2; i++) u.U(1, 1);                      // tile columns: as many as allowed
            if (_tileLog2 < _maxLog2) u.U(1, 0);
            u.U(1, 0);                                                                 // one tile row
            u.U(16, comp.Length); u.Align();
            // the frame: the headers, then each tile but the last with its size before it (f(32)), then the last
            int len = u.B.Count + comp.Length; for (int k = 0; k < nt; k++) len += tiles[k].Length + (k < nt - 1 ? 4 : 0);
            var f = new byte[len]; int at = 0;
            u.B.CopyTo(f); at += u.B.Count; Array.Copy(comp, 0, f, at, comp.Length); at += comp.Length;
            for (int k = 0; k < nt; k++)
            {
                if (k < nt - 1) { int n = tiles[k].Length; f[at++] = (byte)(n >> 24); f[at++] = (byte)(n >> 16); f[at++] = (byte)(n >> 8); f[at++] = (byte)n; }
                Array.Copy(tiles[k], 0, f, at, tiles[k].Length); at += tiles[k].Length;
            }
            _last = f; _lastKey = _key;
            if (KeepFrames) { _frames.Add(f); _times.Add(_t); _keys.Add(_key); }
            _t += Math.Max(1u, ms);
        }

        // a luma plane a quarter each way (each the rounded mean of 4 x 4), the frame's own area only
        void Quarter(byte[] y, byte[] q)
        {
            int S = Stride, qw = _qw;
            Universe.Core.Jobs.For(_qh, r =>
            {
                for (int c = 0; c < qw; c++)
                {
                    int o = r * 4 * S + c * 4, sum = 0;
                    for (int i = 0; i < 4; i++) { int k = o + i * S; sum += y[k] + y[k + 1] + y[k + 2] + y[k + 3]; }
                    q[r * qw + c] = (byte)((sum + 8) >> 4);
                }
            });
        }

        // ── one tile column: its own coder, its own left contexts, its scratch made once ──
        private sealed class Tile
        {
            private readonly Vp9Writer _v; private readonly int _c0, _c1; private readonly Vp9Bool _bc = new Vp9Bool();
            private readonly byte[][] _leftNz; private readonly byte[] _leftPart = new byte[8];
            private readonly int[][] _coef = new int[384][]; private readonly int[] _eob = new int[384], _tp = new int[384], _tx = new int[384], _ty = new int[384]; private readonly bool[] _in = new bool[384];
            private readonly int[] _pred = new int[16], _dq = new int[16], _cache = new int[16]; private readonly double[] _res = new double[16], _tmp = new double[16];
            public Tile(Vp9Writer v, int c0, int c1) { _v = v; _c0 = c0; _c1 = c1; _leftNz = new[] { new byte[16], new byte[8], new byte[8] }; for (int i = 0; i < 384; i++) _coef[i] = new int[16]; }
            public byte[] Code()
            {
                _bc.Put(0, 128);                                                          // (the marker bit init_bool reads)
                for (int r = 0; r < _v._miRows; r += 8)
                {
                    Array.Clear(_leftNz[0], 0, 16); Array.Clear(_leftNz[1], 0, 8); Array.Clear(_leftNz[2], 0, 8); Array.Clear(_leftPart, 0, 8);
                    for (int c = _c0; c < _c1; c += 8) Partition(r, c, 3);
                }
                return _bc.Finish();
            }

            // decode_partition, written: one block wherever it fits whole, split at the frame's edges
            void Partition(int r, int c, int bsl)
            {
                var v = _v; if (r >= v._miRows || c >= v._miCols) return;
                int num8 = 1 << bsl, half = num8 >> 1;
                bool hasRows = r + half < v._miRows, hasCols = c + half < v._miCols;
                int above = 0, left = 0; for (int i = 0; i < num8; i++) { above |= v._abovePart[c + i]; left |= _leftPart[((r & 7) + i) & 7]; }
                int boff = 3 - bsl; int ctx = bsl * 4 + (((left & (1 << boff)) > 0) ? 2 : 0) + (((above & (1 << boff)) > 0) ? 1 : 0);
                var probs = v._key ? Vp9Tables.KfPartitionProbs : Vp9Tables.DefaultPartitionProbs; int pbase = ctx * 3;
                bool split;
                if (hasRows && hasCols && bsl == 3 && !v._key && SplitPays(r, c)) { _bc.Put(1, probs[pbase]); _bc.Put(1, probs[pbase + 1]); _bc.Put(1, probs[pbase + 2]); split = true; }
                else if (hasRows && hasCols) { _bc.Put(0, probs[pbase]); split = false; }
                else if (hasCols) { _bc.Put(1, probs[pbase + 1]); split = true; }
                else if (hasRows) { _bc.Put(1, probs[pbase + 2]); split = true; }
                else split = true;
                if (split)
                {
                    Partition(r, c, bsl - 1); Partition(r, c + half, bsl - 1); Partition(r + half, c, bsl - 1); Partition(r + half, c + half, bsl - 1);
                    return;
                }
                Block(r, c, bsl);
                byte pv = (byte)(15 >> (bsl + 1));
                for (int i = 0; i < num8; i++) { if (c + i < v._abovePart.Length) v._abovePart[c + i] = pv; if ((r & 7) + i < 8) _leftPart[(r & 7) + i] = pv; }
            }

            // decode_block, written. A key frame's block: DC prediction for luma and chroma. A change frame's: the last frame, no motion
            // (ZEROMV), so the frame before is the prediction. The residual worked out and rebuilt first; if nothing of it is left after
            // quantising, the block is skipped (in a change frame: copied as it was). Left of the tile's first column counts as missing.
            void Block(int r, int c, int bsl)
            {
                var v = _v; bool availU = r > 0, availL = c > _c0;
                int n = 0, eobTotal = 0;
                int mode = ZeroMv, mctx = 0; _dy = _dx = 0; _bestR = _bestC = 0;
                if (!v._key) mode = ChooseMotion(r, c, bsl, out mctx);
                for (int p = 0; p < 3; p++)
                {
                    int sub = p > 0 ? 1 : 0, n4 = (2 << bsl) >> sub, baseX = (c * 8) >> sub, baseY = (r * 8) >> sub;
                    int maxx = (v._miCols * 8) >> sub, maxy = (v._miRows * 8) >> sub;
                    for (int y = 0; y < n4; y++) for (int x = 0; x < n4; x++)
                    {
                        int sx = baseX + 4 * x, sy = baseY + 4 * y;
                        _tp[n] = p; _tx[n] = sx; _ty[n] = sy; _in[n] = sx < maxx && sy < maxy; _eob[n] = 0;
                        if (_in[n]) { Tx4(n, availL || x > 0, availU || y > 0); eobTotal += _eob[n]; }
                        n++;
                    }
                }
                int skip = eobTotal == 0 ? 1 : 0;
                int sctx = (availU ? v._skips[(r - 1) * v._miCols + c] : 0) + (availL ? v._skips[r * v._miCols + c - 1] : 0);
                _bc.Put(skip, Vp9Tables.DefaultSkipProb[sctx]);
                if (v._key)
                {
                    _bc.Put(0, Vp9Tables.KfYModeProbs[0]);                             // DC_PRED (above and left DC_PRED)
                    _bc.Put(0, Vp9Tables.KfUvModeProbs[0]);                            // DC_PRED for chroma
                }
                else
                {
                    _bc.Put(1, Vp9Tables.DefaultIsInterProb[0]);                       // inter (the neighbours inter too: context 0)
                    _bc.Put(0, Vp9Tables.DefaultSingleRefProb[(availU || availL ? 4 : 2) * 2]);   // the last frame
                    // the inter mode (inter_mode_tree: ZEROMV 0; NEARESTMV 10; NEARMV 110; NEWMV 111), in its context from the candidates
                    int mp = mctx * 3; var imp = Vp9Tables.DefaultInterModeProbs;
                    if (mode == ZeroMv) _bc.Put(0, imp[mp]);
                    else
                    {
                        _bc.Put(1, imp[mp]);
                        if (mode == NearestMv) _bc.Put(0, imp[mp + 1]);
                        else { _bc.Put(1, imp[mp + 1]); _bc.Put(mode == NewMv ? 1 : 0, imp[mp + 2]); }
                    }
                    if (mode == NewMv) PutMv(_dy * 8 - _bestR, _dx * 8 - _bestC);      // (against the best candidate, BestMv)
                }
                int n8 = 1 << bsl;                                                     // (an inter block with nothing coded counts as skipped)
                for (int y = 0; y < n8; y++) for (int x = 0; x < n8; x++) if (r + y < v._miRows && c + x < v._miCols)
                {
                    int g = (r + y) * v._miCols + c + x; v._skips[g] = (byte)skip;
                    v._mvR[g] = (short)(_dy * 8); v._mvC[g] = (short)(_dx * 8); v._mode[g] = (byte)mode;
                }
                for (int k = 0; k < n; k++)
                {
                    int p = _tp[k], sub = p > 0 ? 1 : 0; byte nz = 0;
                    if (skip == 0 && _in[k]) nz = Tokens(k);
                    v._aboveNz[p][_tx[k] >> 2] = nz; _leftNz[p][(_ty[k] >> 2) & ((16 >> sub) - 1)] = nz;
                }
            }

            // one 4 x 4: predicted, its residual moved to frequencies (separably), quantised, and rebuilt as the decoder will
            void Tx4(int k, bool haveLeft, bool haveAbove)
            {
                var v = _v; int p = _tp[k], x0 = _tx[k], y0 = _ty[k];
                int S = p == 0 ? v.Stride : v.Stride / 2; var rec = v._rec[p]; var src = v._src[p]; var pred = _pred;
                if (v._key)
                {
                    int sa = 0, sl = 0, dc; for (int i = 0; i < 4; i++) { if (haveAbove) sa += rec[(y0 - 1) * S + x0 + i]; if (haveLeft) sl += rec[(y0 + i) * S + x0 - 1]; }
                    if (haveAbove && haveLeft) dc = (sa + sl + 4) >> 3; else if (haveLeft) dc = (sl + 2) >> 2; else if (haveAbove) dc = (sa + 2) >> 2; else dc = 128;
                    for (int i = 0; i < 16; i++) pred[i] = dc;
                }
                else
                {
                    // the frame before, moved by the block's motion (whole pixels: the filters' first phase, a plain copy), each sample
                    // taken from inside the frame as the decoder takes it (section 8.5.2.4: clamped to the last row and column)
                    var pv = v._prev[p]; int my = p == 0 ? _dy : _dy / 2, mx = p == 0 ? _dx : _dx / 2;
                    int lastX = p == 0 ? v._fw - 1 : ((v._fw + 1) >> 1) - 1, lastY = p == 0 ? v._fh - 1 : ((v._fh + 1) >> 1) - 1;
                    int ry = y0 + my, rx = x0 + mx;
                    if (ry >= 0 && rx >= 0 && ry + 3 <= lastY && rx + 3 <= lastX) for (int i = 0; i < 4; i++) { int o = (ry + i) * S + rx; pred[i * 4] = pv[o]; pred[i * 4 + 1] = pv[o + 1]; pred[i * 4 + 2] = pv[o + 2]; pred[i * 4 + 3] = pv[o + 3]; }
                    else for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { int yy = Math.Max(0, Math.Min(lastY, ry + i)), xx = Math.Max(0, Math.Min(lastX, rx + j)); pred[i * 4 + j] = pv[yy * S + xx]; }
                }
                var res = _res; bool any = false;
                for (int i = 0; i < 4; i++) { int o = (y0 + i) * S + x0; for (int j = 0; j < 4; j++) { double d = src[o + j] - pred[i * 4 + j]; res[i * 4 + j] = d; if (d != 0) any = true; } }
                var coef = _coef[k]; int eob = 0;
                if (any || v._key)
                {
                    // the forward transform: 8 x the orthonormal DCT (the specification's inverse is 1/8 of its transpose), rows then columns
                    var t = _tmp;
                    for (int i = 0; i < 4; i++) for (int u = 0; u < 4; u++) t[i * 4 + u] = Df[u * 4] * res[i * 4] + Df[u * 4 + 1] * res[i * 4 + 1] + Df[u * 4 + 2] * res[i * 4 + 2] + Df[u * 4 + 3] * res[i * 4 + 3];
                    for (int u = 0; u < 4; u++) for (int w = 0; w < 4; w++)
                    {
                        double s = Df[u * 4] * t[w] + Df[u * 4 + 1] * t[4 + w] + Df[u * 4 + 2] * t[8 + w] + Df[u * 4 + 3] * t[12 + w];
                        double qd = (u | w) == 0 ? v._dcq : v._acq; double a = Math.Abs(s) / qd;
                        int qv = (int)(a + ((u | w) == 0 ? 0.5 : 0.36)); coef[u * 4 + w] = s < 0 ? -qv : qv;
                    }
                    var scan = Vp9Tables.DefaultScan4x4;
                    for (int i = 0; i < 16; i++) if (coef[scan[i]] != 0) eob = i + 1;
                }
                else Array.Clear(coef, 0, 16);
                _eob[k] = eob;
                // rebuilt exactly as the decoder rebuilds it (sections 8.6.2 and 8.7)
                var dq = _dq;
                if (eob > 0) { for (int i = 0; i < 16; i++) dq[i] = coef[i] * (i == 0 ? v._dcq : v._acq); Inverse4(dq); }
                for (int i = 0; i < 4; i++) { int o = (y0 + i) * S + x0; for (int j = 0; j < 4; j++) { int val = pred[i * 4 + j] + (eob > 0 ? dq[i * 4 + j] : 0); rec[o + j] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val); } }
            }

            // ── motion ──
            private int _dy, _dx, _bestR, _bestC;                                     // the block's motion (whole pixels, luma), BestMv (eighths)
            private readonly int[] _tgtDy = new int[4], _tgtDx = new int[4]; private int _tgtR = -1, _tgtC = -1;   // a split 64's four quarters, as searched
            static readonly sbyte[][] MvRefBlocks =
            {
                new sbyte[] { -1, 0, 0, -1, -1, -1, -2, 0, 0, -2, -2, -1, -1, -2, -2, -2 },   // 8 x 8 (mv_ref_blocks: row, column)
                new sbyte[] { -1, 0, 0, -1, -1, 1, 1, -1, -1, -1, -3, 0, 0, -3, -3, -3 },     // 16 x 16
                new sbyte[] { -1, 1, 1, -1, -1, 2, 2, -1, -1, -1, -3, 0, 0, -3, -3, -3 },     // 32 x 32
                new sbyte[] { -1, 3, 3, -1, -1, 4, 4, -1, -1, -1, -1, 0, 0, -1, -1, 6 },      // 64 x 64
            };
            static readonly byte[] Mode2Counter = { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 0, 0, 3, 1 };
            static readonly sbyte[] CounterToContext = { 2, 3, 4, 1, 3, -1, 0, -1, -1, 5, 5, -1, 5, -1, -1, -1, -1, -1, 6 };
            const int MvBorder = 16 << 3, BestBorder = (160 - 4) << 3;               // MV_BORDER; (BORDERINPIXELS - INTERP_EXTEND) << 3

            // find_mv_refs and find_best_ref_mvs (sections 6.5.1, 6.5.12) as the decoder runs them, for a frame whose every block is
            // inter, of the last frame, with no older frame's motion in play: the candidates are the blocks around (inside the tile),
            // the first two counted for the context; then each clamped as the decoder clamps it
            void FindRefs(int r, int c, int bsl, out int nr0, out int nc0, out int nr1, out int nc1, out int ctx)
            {
                var v = _v; var t = MvRefBlocks[bsl]; int count = 0, counter = 0; nr0 = nc0 = nr1 = nc1 = 0;
                for (int i = 0; i < 8; i++)
                {
                    int cr = r + t[i * 2], cc = c + t[i * 2 + 1];
                    if (cr < 0 || cr >= v._miRows || cc < _c0 || cc >= _c1) continue;
                    int g = cr * v._miCols + cc;
                    if (i < 2) counter += Mode2Counter[v._mode[g]];
                    if (count >= 2) continue;
                    int mr = v._mvR[g], mc = v._mvC[g];
                    if (count == 0) { nr0 = mr; nc0 = mc; count = 1; }
                    else if (mr != nr0 || mc != nc0) { nr1 = mr; nc1 = mc; count = 2; }
                }
                ctx = CounterToContext[counter];
                int bh = 1 << bsl;
                int top = -(r * 64), bottom = (v._miRows - bh - r) * 64, left = -(c * 64), right = (v._miCols - bh - c) * 64;
                // clamp_mv_ref, then (no eighth-pixel motion: odd parts made even, ours never odd) find_best_ref_mvs' clamp
                nr0 = Clip(Clip(nr0, top - MvBorder, bottom + MvBorder), top - BestBorder, bottom + BestBorder);
                nc0 = Clip(Clip(nc0, left - MvBorder, right + MvBorder), left - BestBorder, right + BestBorder);
                nr1 = Clip(Clip(nr1, top - MvBorder, bottom + MvBorder), top - BestBorder, bottom + BestBorder);
                nc1 = Clip(Clip(nc1, left - MvBorder, right + MvBorder), left - BestBorder, right + BestBorder);
            }
            static int Clip(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

            // the block's mode and motion: the searched motion against the candidates the decoder already has (NEARESTMV, NEARMV and
            // ZEROMV cost a bit or two; NEWMV its difference too), each priced as the picture's difference plus its bits
            int ChooseMotion(int r, int c, int bsl, out int ctx)
            {
                var v = _v; int nr0, nc0, nr1, nc1; FindRefs(r, c, bsl, out nr0, out nc0, out nr1, out nc1, out ctx);
                _bestR = nr0; _bestC = nc0;
                int x = c * 8, y = r * 8, n = 8 << bsl, lam = Math.Max(1, v._acq / 8);
                int sdy, sdx;
                if (_tgtR >= 0 && bsl == 2 && (r & ~7) == _tgtR && (c & ~7) == _tgtC) { int q = ((r >> 2) & 1) * 2 + ((c >> 2) & 1); sdy = _tgtDy[q]; sdx = _tgtDx[q]; }
                else
                {
                    int g = r * v._miCols + c;
                    Search(x, y, n, new[] { 0, 0, nr0 / 8, nc0 / 8, nr1 / 8, nc1 / 8, v._lastR[g] / 8, v._lastC[g] / 8 }, out sdy, out sdx);
                }
                long best = long.MaxValue; int mode = ZeroMv;
                Try(ref best, ref mode, ZeroMv, 0, 0, x, y, n, lam * 1L);
                Try(ref best, ref mode, NearestMv, nr0 / 8, nc0 / 8, x, y, n, lam * 2L);
                Try(ref best, ref mode, NearMv, nr1 / 8, nc1 / 8, x, y, n, lam * 3L);
                Try(ref best, ref mode, NewMv, sdy, sdx, x, y, n, lam * (3L + MvBits(sdy * 8 - nr0) + MvBits(sdx * 8 - nc0)));
                if (mode == NearestMv) { _dy = nr0 / 8; _dx = nc0 / 8; }
                else if (mode == NearMv) { _dy = nr1 / 8; _dx = nc1 / 8; }
                else if (mode == NewMv) { _dy = sdy; _dx = sdx; }
                else { _dy = 0; _dx = 0; }
                return mode;
            }
            void Try(ref long best, ref int mode, int m, int dy, int dx, int x, int y, int n, long bits)
            {
                long cost = Sad(x, y, n, dy, dx, 1, long.MaxValue) + bits;
                if (cost < best) { best = cost; mode = m; }
            }
            static int MvBits(int d) { if (d == 0) return 0; int a = Math.Abs(d) >> 3, b = 3; while (a > 1) { a >>= 1; b += 2; } return b; }

            // whether a whole 64's four quarters, each searched on its own, beat the 64 moved as one by more than their extra motions
            bool SplitPays(int r, int c)
            {
                var v = _v; int x = c * 8, y = r * 8, lam = Math.Max(1, v._acq / 8);
                int g = r * v._miCols + c;
                int nr0, nc0, nr1, nc1, ctx; FindRefs(r, c, 3, out nr0, out nc0, out nr1, out nc1, out ctx);
                int dy, dx; long whole = Search(x, y, 64, new[] { 0, 0, nr0 / 8, nc0 / 8, nr1 / 8, nc1 / 8, v._lastR[g] / 8, v._lastC[g] / 8 }, out dy, out dx);
                long parts = 0;
                for (int q = 0; q < 4; q++)
                {
                    int qx = x + (q & 1) * 32, qy = y + (q >> 1) * 32, gq = (qy >> 3) * v._miCols + (qx >> 3);
                    parts += Search(qx, qy, 32, new[] { dy, dx, 0, 0, v._lastR[gq] / 8, v._lastC[gq] / 8 }, out _tgtDy[q], out _tgtDx[q]);
                }
                bool split = parts + lam * 40L < whole;
                if (split) { _tgtR = r; _tgtC = c; } else _tgtR = -1;
                return split;
            }

            // the luma's absolute difference between the frame's block at (x, y), n wide (its part inside the frame), and the frame
            // before moved by (dy, dx), every step-th row; samples outside the frame taken from its edge, as the decoder takes them
            long Sad(int x, int y, int n, int dy, int dx, int step, long stop)
            {
                var v = _v; int S = v.Stride, w = Math.Min(n, v._fw - x), h = Math.Min(n, v._fh - y); var src = v._src[0]; var pv = v._prev[0];
                long sum = 0; int lastX = v._fw - 1, lastY = v._fh - 1;
                bool inside = x + dx >= 0 && y + dy >= 0 && x + dx + w - 1 <= lastX && y + dy + h - 1 <= lastY;
                for (int i = 0; i < h; i += step)
                {
                    int so = (y + i) * S + x;
                    if (inside) { int ro = (y + dy + i) * S + x + dx; for (int j = 0; j < w; j++) { int d = src[so + j] - pv[ro + j]; sum += d < 0 ? -d : d; } }
                    else { int yy = Clip(y + dy + i, 0, lastY) * S; for (int j = 0; j < w; j++) { int d = src[so + j] - pv[yy + Clip(x + dx + j, 0, lastX)]; sum += d < 0 ? -d : d; } }
                    if (sum > stop) return sum * step;
                }
                return sum * step;
            }
            // the same on the quarter-size planes (a block qn wide there), only where the block moved stays inside the frame
            long QSad(int qx, int qy, int qn, int dy, int dx, long stop)
            {
                var v = _v; int W = v._qw, w = Math.Min(qn, v._qw - qx), h = Math.Min(qn, v._qh - qy); long sum = 0;
                if (w <= 0 || h <= 0 || qx + dx < 0 || qy + dy < 0 || qx + dx + w > v._qw || qy + dy + h > v._qh) return long.MaxValue;
                for (int i = 0; i < h; i++)
                {
                    int so = (qy + i) * W + qx, ro = (qy + dy + i) * W + qx + dx;
                    for (int j = 0; j < w; j++) { int d = v._qs[so + j] - v._qr[ro + j]; sum += d < 0 ? -d : d; }
                    if (sum > stop) return sum;
                }
                return sum;
            }

            const int Range = 256;                                                     // (the farthest a block is looked for, in pixels)
            // the block's motion: the best of the starting guesses, then (big blocks) a coarse look on the quarter-size planes, then a
            // fine one in steps of 8, 4 and 2 pixels; whole pairs of pixels only, the block moved kept within the frame before
            long Search(int x, int y, int n, int[] starts, out int bdy, out int bdx)
            {
                var v = _v; int w = Math.Min(n, v._fw - x), h = Math.Min(n, v._fh - y);
                int minY = Math.Max(-Range, -y), maxY = Math.Min(Range, v._fh - h - y), minX = Math.Max(-Range, -x), maxX = Math.Min(Range, v._fw - w - x);
                long best = long.MaxValue; bdy = 0; bdx = 0;
                for (int k = 0; k + 1 < starts.Length; k += 2)
                {
                    int sy = Clip(starts[k], minY, maxY) & ~1, sx = Clip(starts[k + 1], minX, maxX) & ~1;
                    long cst = Sad(x, y, n, sy, sx, 2, best); if (cst < best) { best = cst; bdy = sy; bdx = sx; }
                }
                if (n >= 32)
                {
                    // coarse: a quarter each way, from the best guess, in steps of 8, 4, 2 and 1 there (32, 16, 8 and 4 pixels here)
                    int qx = x / 4, qy = y / 4, qn = n / 4, cy = bdy / 4, cx = bdx / 4; long qb = QSad(qx, qy, qn, cy, cx, long.MaxValue);
                    for (int st = 8; st >= 1; st >>= 1)
                        for (int it = 0; it < 8; it++)
                        {
                            int by = cy, bx = cx;
                            for (int d = 0; d < 8; d++)
                            {
                                int ty = cy + DY[d] * st, tx = cx + DX[d] * st;
                                if (ty * 4 < minY || ty * 4 > maxY || tx * 4 < minX || tx * 4 > maxX) continue;
                                long cs = QSad(qx, qy, qn, ty, tx, qb); if (cs < qb) { qb = cs; by = ty; bx = tx; }
                            }
                            if (by == cy && bx == cx) break; cy = by; cx = bx;
                        }
                    if (cy * 4 >= minY && cy * 4 <= maxY && cx * 4 >= minX && cx * 4 <= maxX) { long fc = Sad(x, y, n, cy * 4, cx * 4, 2, best); if (fc < best) { best = fc; bdy = cy * 4; bdx = cx * 4; } }
                }
                for (int st = 8; st >= 2; st >>= 1)
                    for (int it = 0; it < 6; it++)
                    {
                        int by = bdy, bx = bdx;
                        for (int d = 0; d < 8; d++)
                        {
                            int ty = bdy + DY[d] * st, tx = bdx + DX[d] * st;
                            if (ty < minY || ty > maxY || tx < minX || tx > maxX) continue;
                            long cs = Sad(x, y, n, ty, tx, 2, best); if (cs < best) { best = cs; by = ty; bx = tx; }
                        }
                        if (by == bdy && bx == bdx) break; bdy = by; bdx = bx;
                    }
                return best;
            }
            static readonly int[] DY = { -1, 1, 0, 0, -1, -1, 1, 1 }, DX = { 0, 0, -1, 1, -1, 1, -1, 1 };

            // a motion difference (eighths, even: no eighth-pixel motion) as read_mv reads it: the joint, then each part that is not 0
            void PutMv(int dr, int dc)
            {
                int joint = (dr != 0 ? 2 : 0) | (dc != 0 ? 1 : 0);                     // MV_JOINT_ZERO 0, HNZVZ 1, HZVNZ 2, HNZVNZ 3
                // mv_joint_tree { -ZERO, 2, -HNZVZ, 4, -HZVNZ, -HNZVNZ }, its default odds 32, 64, 96
                if (joint == 0) _bc.Put(0, 32);
                else { _bc.Put(1, 32); if (joint == 1) _bc.Put(0, 64); else { _bc.Put(1, 64); _bc.Put(joint == 3 ? 1 : 0, 96); } }
                if (dr != 0) PutMvPart(dr, 0);
                if (dc != 0) PutMvPart(dc, 1);
            }
            static readonly byte[][] MvClassProbs = { new byte[] { 224, 144, 192, 168, 192, 176, 192, 198, 198, 245 }, new byte[] { 216, 128, 176, 160, 176, 176, 192, 198, 198, 208 } };
            static readonly byte[] MvClass0Bit = { 216, 208 };
            static readonly byte[][][] MvClass0Fr = { new[] { new byte[] { 128, 128, 64 }, new byte[] { 96, 112, 64 } }, new[] { new byte[] { 128, 128, 64 }, new byte[] { 96, 112, 64 } } };
            static readonly byte[][] MvFr = { new byte[] { 64, 96, 64 }, new byte[] { 64, 96, 64 } };
            static readonly byte[] MvBitsP = { 136, 140, 148, 160, 176, 192, 224, 234, 234, 240 };
            static readonly int[] MvClassTree = { 0, 2, -1, 4, 6, 8, -2, -3, 10, 12, -4, -5, -6, 14, 16, 18, -7, -8, -9, -10 };
            static readonly int[][] ClassPath = MakeClassPaths();
            static int[][] MakeClassPaths() { var p = new int[11][]; for (int t = 0; t < 11; t++) { var l = new List<int>(); FindIn(MvClassTree, 0, t, l); p[t] = l.ToArray(); } return p; }
            static bool FindIn(int[] tree, int n, int v, List<int> path)
            {
                for (int b = 0; b < 2; b++)
                {
                    int ch = tree[n + b]; path.Add(n); path.Add(b);
                    if (ch <= 0 ? -ch == v : FindIn(tree, ch, v, path)) return true;
                    path.RemoveRange(path.Count - 2, 2);
                }
                return false;
            }
            // read_mv_component, written: the sign, the class, then (class 0) its bit or (above it) the class's bits, then the
            // fraction; the high precision bit is not sent (no eighth-pixel motion: taken as 1)
            void PutMvPart(int v, int comp)
            {
                _bc.Put(v < 0 ? 1 : 0, 128);
                int z = Math.Abs(v) - 1, ip = z >> 3, fr = (z >> 1) & 3, cls = 0;
                if (ip >= 2) { cls = 1; while ((ip >> (cls + 1)) != 0) cls++; }
                var path = ClassPath[cls]; var cp = MvClassProbs[comp];
                for (int k = 0; k < path.Length; k += 2) _bc.Put(path[k + 1], cp[path[k] >> 1]);
                byte[] frp;
                if (cls == 0) { _bc.Put(ip, MvClass0Bit[comp]); frp = MvClass0Fr[comp][ip]; }
                else { int d = ip - (1 << cls); for (int i = 0; i < cls; i++) _bc.Put((d >> i) & 1, MvBitsP[i]); frp = MvFr[comp]; }
                // mv_fr_tree { -0, 2, -1, 4, -2, -3 }
                if (fr == 0) _bc.Put(0, frp[0]); else { _bc.Put(1, frp[0]); if (fr == 1) _bc.Put(0, frp[1]); else { _bc.Put(1, frp[1]); _bc.Put(fr == 3 ? 1 : 0, frp[2]); } }
            }

            // one 4 x 4's coefficients in the token tree (section 6.4.24), its contexts as the decoder works them out
            byte Tokens(int k)
            {
                var v = _v; int p = _tp[k], x0 = _tx[k], y0 = _ty[k], eob = _eob[k]; var coef = _coef[k]; var scan = Vp9Tables.DefaultScan4x4;
                int pl = p > 0 ? 1 : 0, rf = v._key ? 0 : 1;
                int ctx0 = v._aboveNz[p][x0 >> 2] + _leftNz[p][(y0 >> 2) & ((p > 0 ? 8 : 16) - 1)];
                var cache = _cache; Array.Clear(cache, 0, 16); bool checkEob = true;
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
            // the token tree, each node's odds: node 0 and 1 the coefficient odds, the rest the Pareto table's (section 9.3.2)
            void PutToken(int tok, int p1, int p2)
            {
                var path = TokenPath[tok];
                for (int k = 0; k < path.Length; k += 2)
                {
                    int node = path[k] >> 1, prob;
                    if (node == 0) prob = p1; else if (node == 1) prob = p2;
                    else { int x = (p2 - 1) / 2; prob = (p2 & 1) != 0 ? Vp9Tables.ParetoTable[x * 8 + node - 2] : (Vp9Tables.ParetoTable[x * 8 + node - 2] + Vp9Tables.ParetoTable[(x + 1) * 8 + node - 2]) >> 1; }
                    _bc.Put(path[k + 1], prob);
                }
            }
        }

        static int TokenOf(int a) { if (a < 5) return a; if (a < 7) return 5; if (a < 11) return 6; if (a < 19) return 7; if (a < 35) return 8; if (a < 67) return 9; return 10; }
        static byte[] CatProbs(int cat) { switch (cat) { case 1: return Vp9Tables.CatProbs1; case 2: return Vp9Tables.CatProbs2; case 3: return Vp9Tables.CatProbs3; case 4: return Vp9Tables.CatProbs4; case 5: return Vp9Tables.CatProbs5; default: return Vp9Tables.CatProbs6; } }

        // ── the transforms: the forward one 8 x the orthonormal DCT (sqrt 8 each way); the inverse, section 8.7, exactly ──
        static readonly double[] Df = MakeDf();
        static double[] MakeDf() { var d = new double[16]; for (int k = 0; k < 4; k++) for (int n = 0; n < 4; n++) d[k * 4 + n] = Math.Sqrt(8) * (k == 0 ? 0.5 : Math.Sqrt(0.5)) * Math.Cos((2 * n + 1) * k * Math.PI / 8); return d; }
        static int Cos64(int angle) { int a = angle & 127; var t = Vp9Tables.Cos64Lookup; if (a <= 32) return t[a]; if (a <= 64) return -t[64 - a]; if (a <= 96) return -t[a - 64]; return t[128 - a]; }
        static int Sin64(int angle) { return Cos64(angle - 32); }
        static long R14(long x) { return (x + (1 << 13)) >> 14; }
        static void B(int[] T, int o, int a, int b, int angle, bool flip)
        {
            long x = (long)T[o + a] * Cos64(angle) - (long)T[o + b] * Sin64(angle), y = (long)T[o + a] * Sin64(angle) + (long)T[o + b] * Cos64(angle);
            T[o + a] = (int)R14(x); T[o + b] = (int)R14(y); if (flip) { int s = T[o + a]; T[o + a] = T[o + b]; T[o + b] = s; }
        }
        // one 4-point inverse DCT on 4 values at a stride (the bit-reversed order, B(0,1,16,1), B(2,3,24,0), H(0,3), H(1,2))
        static void Idct4(int[] d, int o, int st, int[] T)
        {
            T[0] = d[o]; T[1] = d[o + 2 * st]; T[2] = d[o + st]; T[3] = d[o + 3 * st];
            B(T, 0, 0, 1, 16, true); B(T, 0, 2, 3, 24, false);
            int a0 = T[0], a3 = T[3], a1 = T[1], a2 = T[2];
            d[o] = a0 + a3; d[o + 3 * st] = a0 - a3; d[o + st] = a1 + a2; d[o + 2 * st] = a1 - a2;
        }
        [ThreadStatic] static int[] _t4;
        static void Inverse4(int[] d)
        {
            var T = _t4 ?? (_t4 = new int[4]);
            for (int i = 0; i < 4; i++) Idct4(d, i * 4, 1, T);
            for (int j = 0; j < 4; j++) { Idct4(d, j, 4, T); for (int i = 0; i < 4; i++) d[i * 4 + j] = (d[i * 4 + j] + 8) >> 4; }
        }

        public void Dispose() { if (_path != null) WebmFile.Write(_path, "V_VP9", null, _w, _h, _frames, _times, _keys, _t); }
    }
}
