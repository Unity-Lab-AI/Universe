// Universe · render · our own H.264 writer, every line ours: the front man a chat app's player reads (FRONTMAN-RESEARCH.md section 12).
// Version 0: baseline profile, every frame an IDR of Intra16x16 macroblocks, DC prediction, CAVLC; each macroblock carries its 16
// luma DCs (one a 4 x 4 patch) and its chroma DCs (one a quarter), no AC yet: a coarse picture, but a valid one. The writer keeps
// the decoder's own reconstruction (it predicts from it), so what a player shows is known here exactly. The MP4 boxes around it are
// ours too.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    internal sealed class Bits
    {
        private readonly List<byte> _b = new List<byte>(); private int _cur, _n;
        public void U(int nbits, long v) { for (int i = nbits - 1; i >= 0; i--) Bit((int)((v >> i) & 1)); }
        public void Bit(int b) { _cur = (_cur << 1) | (b & 1); if (++_n == 8) { _b.Add((byte)_cur); _cur = 0; _n = 0; } }
        public void Ue(long v) { long x = v + 1; int len = 0; while ((x >> len) > 1) len++; for (int i = 0; i < len; i++) Bit(0); U(len + 1, x); }
        public void Se(long v) { Ue(v <= 0 ? -2 * v : 2 * v - 1); }
        public void Code(string s) { foreach (char c in s) Bit(c == '1' ? 1 : 0); }
        public void Trailing() { Bit(1); while (_n != 0) Bit(0); }
        public byte[] ToArray() { return _b.ToArray(); }
        /// <summary>The bytes with emulation prevention: a 3 after any two zero bytes followed by a byte of 3 or less.</summary>
        public static byte[] Escape(byte[] raw)
        {
            var o = new List<byte>(raw.Length + 16); int zeros = 0;
            foreach (byte b in raw) { if (zeros >= 2 && b <= 3) { o.Add(3); zeros = 0; } o.Add(b); zeros = b == 0 ? zeros + 1 : 0; }
            return o.ToArray();
        }
    }

    internal sealed class H264Writer : IDisposable
    {
        // ── CAVLC tables (ITU-T H.264 table 9-5 and 9-7 to 9-10), as the bit strings themselves ──
        // coeff_token for 0 <= nC < 2: [totalCoeff][trailingOnes]
        static readonly string[][] Tok0 = {
            new[] { "1" },
            new[] { "000101", "01" },
            new[] { "00000111", "000100", "001" },
            new[] { "000000111", "00000110", "0000101", "00011" },
            new[] { "0000000111", "000000110", "00000101", "000011" },
            new[] { "00000000111", "0000000110", "000000101", "0000100" },
            new[] { "0000000001111", "00000000110", "0000000101", "00000100" },
            new[] { "0000000001011", "0000000001110", "00000000101", "000000100" },
            new[] { "0000000001000", "0000000001010", "0000000001101", "0000000100" },
            new[] { "00000000001111", "00000000001110", "0000000001001", "00000000100" },
            new[] { "00000000001011", "00000000001010", "00000000001101", "0000000001100" },
            new[] { "000000000001111", "000000000001110", "00000000001001", "00000000001100" },
            new[] { "000000000001011", "000000000001010", "000000000001101", "00000000001000" },
            new[] { "0000000000001111", "000000000000001", "000000000001001", "000000000001100" },
            new[] { "0000000000001011", "0000000000001110", "0000000000001101", "000000000001000" },
            new[] { "0000000000000111", "0000000000001010", "0000000000001001", "0000000000001100" },
            new[] { "0000000000000100", "0000000000000110", "0000000000000101", "0000000000001000" } };
        // coeff_token for nC = -1 (chroma DC, 4:2:0)
        static readonly string[][] TokC = {
            new[] { "01" },
            new[] { "000111", "1" },
            new[] { "000100", "000110", "001" },
            new[] { "000011", "0000011", "0000010", "000101" },
            new[] { "000010", "00000011", "00000010", "0000000" } };
        // total_zeros, 4 x 4 blocks: [totalCoeff 1..15][totalZeros]
        static readonly string[][] Tz = {
            null,
            new[] { "1", "011", "010", "0011", "0010", "00011", "00010", "000011", "000010", "0000011", "0000010", "00000011", "00000010", "000000011", "000000010", "000000001" },
            new[] { "111", "110", "101", "100", "011", "0101", "0100", "0011", "0010", "00011", "00010", "000011", "000010", "000001", "000000" },
            new[] { "0101", "111", "110", "101", "0100", "0011", "100", "011", "0010", "00011", "00010", "000001", "00001", "000000" },
            new[] { "00011", "111", "0101", "0100", "110", "101", "100", "0011", "011", "0010", "00010", "00001", "00000" },
            new[] { "0101", "0100", "0011", "111", "110", "101", "100", "011", "0010", "00001", "0001", "00000" },
            new[] { "000001", "00001", "111", "110", "101", "100", "011", "010", "0001", "001", "000000" },
            new[] { "000001", "00001", "101", "100", "011", "11", "010", "0001", "001", "000000" },
            new[] { "000001", "0001", "00001", "011", "11", "10", "010", "001", "000000" },
            new[] { "000001", "000000", "0001", "11", "10", "001", "01", "00001" },
            new[] { "00001", "00000", "001", "11", "10", "01", "0001" },
            new[] { "0000", "0001", "001", "010", "1", "011" },
            new[] { "0000", "0001", "01", "1", "001" },
            new[] { "000", "001", "1", "01" },
            new[] { "00", "01", "1" },
            new[] { "0", "1" } };
        // total_zeros, chroma DC: [totalCoeff 1..3][totalZeros]
        static readonly string[][] TzC = { null, new[] { "1", "01", "001", "000" }, new[] { "1", "01", "00" }, new[] { "1", "0" } };
        // run_before: [zerosLeft 1..7 (7: more than 6)][run]
        static readonly string[][] Rb = {
            null,
            new[] { "1", "0" },
            new[] { "1", "01", "00" },
            new[] { "11", "10", "01", "00" },
            new[] { "11", "10", "01", "001", "000" },
            new[] { "11", "10", "011", "010", "001", "000" },
            new[] { "11", "000", "001", "011", "010", "101", "100" },
            new[] { "111", "110", "101", "100", "011", "010", "001", "0001", "00001", "000001", "0000001", "00000001", "000000001", "0000000001", "00000000001" } };
        static readonly int[] Zig = { 0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15 };
        static readonly int[] MF0 = { 13107, 11916, 10082, 9362, 8192, 7282 };   // the quantiser's multiplier at position (0, 0), by QP % 6
        static readonly int[] V0 = { 10, 11, 13, 14, 16, 18 };                   // the decoder's scale at (0, 0), by QP % 6

        /// <summary>One block's coefficients (in scan order) as CAVLC; chromaDc: the 2 x 2 chroma DC table.</summary>
        static void Residual(Bits b, int[] c, int max, bool chromaDc)
        {
            int total = 0, last = -1; for (int i = 0; i < max; i++) if (c[i] != 0) { total++; last = i; }
            var lv = new List<int>(); var pos = new List<int>();
            for (int i = max - 1; i >= 0; i--) if (c[i] != 0) { lv.Add(c[i]); pos.Add(i); }
            int t1 = 0; for (int i = 0; i < lv.Count && t1 < 3; i++) { if (Math.Abs(lv[i]) == 1) t1++; else break; }
            b.Code(chromaDc ? TokC[total][t1] : Tok0[total][t1]);
            if (total == 0) return;
            for (int i = 0; i < t1; i++) b.Bit(lv[i] < 0 ? 1 : 0);
            int sl = total > 10 && t1 < 3 ? 1 : 0;
            for (int i = t1; i < total; i++)
            {
                int level = lv[i]; int code = level > 0 ? 2 * level - 2 : -2 * level - 1;
                if (i == t1 && t1 < 3) code -= 2;
                if (sl == 0)
                {
                    if (code < 14) { b.U(code + 1, 1); }
                    else if (code < 30) { b.U(15, 1); b.U(4, code - 14); }
                    else { b.U(16, 1); b.U(12, code - 30); }
                }
                else
                {
                    int prefix = code >> sl;
                    if (prefix < 15) { b.U(prefix + 1, 1); b.U(sl, code & ((1 << sl) - 1)); }
                    else { b.U(16, 1); b.U(12, code - (15 << sl)); }
                }
                if (sl == 0) sl = 1;
                if (Math.Abs(level) > (3 << (sl - 1)) && sl < 6) sl++;
            }
            if (total < max)
            {
                int tz = last + 1 - total;
                b.Code(chromaDc ? TzC[total][tz] : Tz[total][tz]);
                int left = tz;
                for (int i = 0; i < total - 1 && left > 0; i++)
                {
                    int run = pos[i] - pos[i + 1] - 1;
                    b.Code(Rb[Math.Min(left, 7)][run]); left -= run;
                }
            }
        }

        private readonly int _w, _h, _mbw, _mbh, _qp; private readonly List<byte[]> _samples = new List<byte[]>(); private readonly List<uint> _durations = new List<uint>();
        private readonly string _path; private readonly byte[] _sps, _pps; private int _idr;
        /// <summary>The picture a player shows for the last frame written (Y, then Cb, then Cr), as the decoder rebuilds it.</summary>
        public byte[] Y, Cb, Cr;
        public long Bytes { get { long n = 0; foreach (var s in _samples) n += s.Length; return n; } }

        public H264Writer(string path, int w, int h, int qp = 28)
        {
            _path = path; _w = w & ~1; _h = h & ~1; _mbw = (_w + 15) / 16; _mbh = (_h + 15) / 16; _qp = qp;
            var s = new Bits();
            s.U(8, 66); s.U(8, 0xC0); s.U(8, 51); s.Ue(0);           // baseline, constraint sets 0 and 1, level 5.1, SPS 0
            s.Ue(0); s.Ue(2); s.Ue(0); s.Bit(0);                       // frame_num bits 4; picture order type 2; no reference frames; no gaps
            s.Ue(_mbw - 1); s.Ue(_mbh - 1); s.Bit(1); s.Bit(1);        // the size in macroblocks; frames only; direct 8x8 inference
            int cr = _mbw * 16 - _w, cb = _mbh * 16 - _h;
            if (cr > 0 || cb > 0) { s.Bit(1); s.Ue(0); s.Ue(cr / 2); s.Ue(0); s.Ue(cb / 2); } else s.Bit(0);
            s.Bit(0); s.Trailing(); _sps = s.ToArray();
            var p = new Bits();
            p.Ue(0); p.Ue(0); p.Bit(0); p.Bit(0); p.Ue(0); p.Ue(0); p.Ue(0); p.Bit(0); p.U(2, 0);
            p.Se(_qp - 26); p.Se(0); p.Se(0); p.Bit(1); p.Bit(0); p.Bit(0); p.Trailing(); _pps = p.ToArray();
            Y = new byte[_mbw * 16 * _mbh * 16]; Cb = new byte[_mbw * 8 * _mbh * 8]; Cr = new byte[_mbw * 8 * _mbh * 8];
        }

        /// <summary>One frame (RGB, three bytes a pixel, top row first, <paramref name="srcW"/> wide), shown for <paramref name="ms"/>.</summary>
        public void Add(byte[] rgb, int srcW, int srcH, uint ms)
        {
            int W = _mbw * 16, H = _mbh * 16, CW = W / 2;
            var sy = new int[W * H]; var su = new int[CW * (H / 2)]; var sv = new int[CW * (H / 2)];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                int ox = Math.Min(srcW - 1, x * srcW / _w), oy = Math.Min(srcH - 1, y * srcH / _h), o = (oy * srcW + ox) * 3;
                double r = rgb[o], g = rgb[o + 1], bb = rgb[o + 2];
                sy[y * W + x] = Clip(16 + (65.738 * r + 129.057 * g + 25.064 * bb) / 256);
                if ((x & 1) == 0 && (y & 1) == 0) { su[(y / 2) * CW + x / 2] = Clip(128 + (-37.945 * r - 74.494 * g + 112.439 * bb) / 256); sv[(y / 2) * CW + x / 2] = Clip(128 + (112.439 * r - 94.154 * g - 18.285 * bb) / 256); }
            }
            var b = new Bits();
            b.U(8, 0x65);                                               // NAL: an IDR slice, of the highest importance
            b.Ue(0); b.Ue(7); b.Ue(0); b.U(4, 0); b.Ue(_idr++ & 1);   // first macroblock 0; all I; PPS 0; frame_num 0; idr_pic_id
            b.Bit(0); b.Bit(0);                                         // dec_ref_pic_marking: no_output_of_prior_pics, long_term_reference
            b.Se(0); b.Ue(1);                                           // slice_qp_delta 0; deblocking off
            int qp = _qp, qc = QpChroma(qp);
            for (int my = 0; my < _mbh; my++) for (int mx = 0; mx < _mbw; mx++)
            {
                b.Ue(7); b.Ue(0); b.Se(0);                              // I16x16, DC prediction, chroma DC only; chroma DC prediction; qp delta 0
                // luma: the DC prediction from the decoded picture, each 4 x 4's mean residual, a Hadamard, the quantiser
                int pred = PredDc16(mx, my, W);
                var dc = new int[16];
                for (int by = 0; by < 4; by++) for (int bx = 0; bx < 4; bx++)
                {
                    int s = 0; for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) s += sy[(my * 16 + by * 4 + y) * W + mx * 16 + bx * 4 + x] - pred;
                    dc[by * 4 + bx] = s;                                // the 4 x 4 transform's DC term (its rows and columns all 1)
                }
                var hd = Hadamard4(dc); var q = new int[16]; int qbits = 15 + qp / 6, f = (1 << qbits) / 3;
                for (int i = 0; i < 16; i++) { int a = Math.Abs(hd[i]) / 2; int z = (int)(((long)a * MF0[qp % 6] + 2 * f) >> (qbits + 1)); q[i] = hd[i] < 0 ? -z : z; }
                var scan = new int[16]; for (int i = 0; i < 16; i++) scan[i] = q[Zig[i]];
                Residual(b, scan, 16, false);
                // the decoder's own rebuild: the inverse Hadamard, the scale, each 4 x 4 a flat patch of its DC
                var ih = Hadamard4(q); int ls = 16 * V0[qp % 6];
                for (int by = 0; by < 4; by++) for (int bx = 0; bx < 4; bx++)
                {
                    int c = ih[by * 4 + bx], d = qp >= 36 ? (c * ls) << (qp / 6 - 6) : (c * ls + (1 << (5 - qp / 6))) >> (6 - qp / 6);
                    int v = Clip255(pred + ((d + 32) >> 6));
                    for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) Y[(my * 16 + by * 4 + y) * W + mx * 16 + bx * 4 + x] = (byte)v;
                }
                ChromaDc(b, su, Cb, mx, my, CW, qc); ChromaDc(b, sv, Cr, mx, my, CW, qc);
            }
            b.Trailing();
            var raw = b.ToArray(); var esc = Bits.Escape(raw);
            var sample = new byte[4 + esc.Length]; sample[0] = (byte)(esc.Length >> 24); sample[1] = (byte)(esc.Length >> 16); sample[2] = (byte)(esc.Length >> 8); sample[3] = (byte)esc.Length; Array.Copy(esc, 0, sample, 4, esc.Length);
            _samples.Add(sample); _durations.Add(ms);
        }

        void ChromaDc(Bits b, int[] src, byte[] rec, int mx, int my, int CW, int qc)
        {
            var dc = new int[4]; var pred = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int bx = k & 1, by = k >> 1; pred[k] = PredDcChroma(rec, mx, my, bx, by, CW);
                int s = 0; for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) s += src[(my * 8 + by * 4 + y) * CW + mx * 8 + bx * 4 + x] - pred[k];
                dc[k] = s;
            }
            int[] h = { dc[0] + dc[1] + dc[2] + dc[3], dc[0] - dc[1] + dc[2] - dc[3], dc[0] + dc[1] - dc[2] - dc[3], dc[0] - dc[1] - dc[2] + dc[3] };
            int qbits = 15 + qc / 6, f = (1 << qbits) / 3; var q = new int[4];
            for (int i = 0; i < 4; i++) { int z = (int)(((long)Math.Abs(h[i]) * MF0[qc % 6] + 2 * f) >> (qbits + 1)); q[i] = h[i] < 0 ? -z : z; }
            Residual(b, q, 4, true);
            int[] ih = { q[0] + q[1] + q[2] + q[3], q[0] - q[1] + q[2] - q[3], q[0] + q[1] - q[2] - q[3], q[0] - q[1] - q[2] + q[3] };
            int ls = 16 * V0[qc % 6];
            for (int k = 0; k < 4; k++)
            {
                int bx = k & 1, by = k >> 1, d = ((ih[k] * ls) << (qc / 6)) >> 5, v = Clip255(pred[k] + ((d + 32) >> 6));
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) rec[(my * 8 + by * 4 + y) * CW + mx * 8 + bx * 4 + x] = (byte)v;
            }
        }
        int PredDc16(int mx, int my, int W)
        {
            bool top = my > 0, left = mx > 0; int s = 0;
            if (top) for (int x = 0; x < 16; x++) s += Y[(my * 16 - 1) * W + mx * 16 + x];
            if (left) for (int y = 0; y < 16; y++) s += Y[(my * 16 + y) * W + mx * 16 - 1];
            return top && left ? (s + 16) >> 5 : top || left ? (s + 8) >> 4 : 128;
        }
        // chroma DC prediction (8.3.4.1-3): the top-right block prefers the row above, the bottom-left the column at the left
        int PredDcChroma(byte[] rec, int mx, int my, int bx, int by, int CW)
        {
            int x0 = mx * 8 + bx * 4, y0 = my * 8 + by * 4; bool top = my > 0, left = mx > 0; int st = 0, sl = 0;
            if (top) for (int x = 0; x < 4; x++) st += rec[(my * 8 - 1) * CW + x0 + x];
            if (left) for (int y = 0; y < 4; y++) sl += rec[(y0 + y) * CW + mx * 8 - 1];
            bool both = (bx == by);                                    // blocks 0 and 3 use both; 1 prefers the top; 2 the left
            if (both) return top && left ? (st + sl + 4) >> 3 : top ? (st + 2) >> 2 : left ? (sl + 2) >> 2 : 128;
            if (bx == 1) return top ? (st + 2) >> 2 : left ? (sl + 2) >> 2 : 128;
            return left ? (sl + 2) >> 2 : top ? (st + 2) >> 2 : 128;
        }
        static int[] Hadamard4(int[] a)
        {
            var t = new int[16]; var o = new int[16];
            for (int i = 0; i < 4; i++) { int p0 = a[i * 4], p1 = a[i * 4 + 1], p2 = a[i * 4 + 2], p3 = a[i * 4 + 3]; t[i * 4] = p0 + p1 + p2 + p3; t[i * 4 + 1] = p0 + p1 - p2 - p3; t[i * 4 + 2] = p0 - p1 - p2 + p3; t[i * 4 + 3] = p0 - p1 + p2 - p3; }
            for (int j = 0; j < 4; j++) { int p0 = t[j], p1 = t[4 + j], p2 = t[8 + j], p3 = t[12 + j]; o[j] = p0 + p1 + p2 + p3; o[4 + j] = p0 + p1 - p2 - p3; o[8 + j] = p0 - p1 - p2 + p3; o[12 + j] = p0 - p1 + p2 - p3; }
            return o;
        }
        static int QpChroma(int qp) { int[] t = { 29, 30, 31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38, 39, 39, 39, 39 }; return qp < 30 ? qp : t[Math.Min(21, qp - 30)]; }
        static int Clip(double v) { int i = (int)(v + 0.5); return i < 0 ? 0 : i > 255 ? 255 : i; }
        static int Clip255(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        // ── the MP4 boxes, ours ──
        static void Be32(List<byte> o, long v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }
        static void Be16(List<byte> o, int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
        static List<byte> Box(string t, params List<byte>[] parts) { var o = new List<byte>(); int n = 8; foreach (var p in parts) n += p.Count; Be32(o, n); foreach (char c in t) o.Add((byte)c); foreach (var p in parts) o.AddRange(p); return o; }
        static List<byte> L(params byte[] b) { return new List<byte>(b); }
        static List<byte> Zeros(int n) { return new List<byte>(new byte[n]); }
        public void Dispose()
        {
            const int ts = 1000; uint dur = 0; foreach (var d in _durations) dur += d;
            var ftyp = Box("ftyp", L((byte)'i', (byte)'s', (byte)'o', (byte)'m'), L(0, 0, 2, 0), L((byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'a', (byte)'v', (byte)'c', (byte)'1'));
            var mdatBody = new List<byte>(); foreach (var s in _samples) mdatBody.AddRange(s);
            long mdatStart = ftyp.Count + 8;
            var mvhd = new List<byte>(); Be32(mvhd, 0); Be32(mvhd, 0); Be32(mvhd, 0); Be32(mvhd, ts); Be32(mvhd, dur); Be32(mvhd, 0x00010000); Be16(mvhd, 0x0100); mvhd.AddRange(Zeros(10)); Matrix(mvhd); mvhd.AddRange(Zeros(24)); Be32(mvhd, 2);
            var tkhd = new List<byte>(); Be32(tkhd, 3); Be32(tkhd, 0); Be32(tkhd, 0); Be32(tkhd, 1); Be32(tkhd, 0); Be32(tkhd, dur); tkhd.AddRange(Zeros(8)); Be16(tkhd, 0); Be16(tkhd, 0); Be16(tkhd, 0); Be16(tkhd, 0); Matrix(tkhd); Be32(tkhd, _w << 16); Be32(tkhd, _h << 16);
            var mdhd = new List<byte>(); Be32(mdhd, 0); Be32(mdhd, 0); Be32(mdhd, 0); Be32(mdhd, ts); Be32(mdhd, dur); Be16(mdhd, 0x55C4); Be16(mdhd, 0);
            var hdlr = new List<byte>(); Be32(hdlr, 0); Be32(hdlr, 0); foreach (char c in "vide") hdlr.Add((byte)c); hdlr.AddRange(Zeros(12)); foreach (char c in "front man") hdlr.Add((byte)c); hdlr.Add(0);
            var vmhd = new List<byte>(); Be32(vmhd, 1); vmhd.AddRange(Zeros(8));
            var dref = new List<byte>(); Be32(dref, 0); Be32(dref, 1); var url = Box("url ", L(0, 0, 0, 1)); dref.AddRange(url);
            var avcC = new List<byte>(); avcC.Add(1); avcC.Add(_sps[0]); avcC.Add(_sps[1]); avcC.Add(_sps[2]); avcC.Add(0xFF); avcC.Add(0xE1);
            var spsN = Bits.Escape(Prepend(0x67, _sps)); Be16(avcC, spsN.Length); avcC.AddRange(spsN); avcC.Add(1); var ppsN = Bits.Escape(Prepend(0x68, _pps)); Be16(avcC, ppsN.Length); avcC.AddRange(ppsN);
            var avc1 = new List<byte>(); avc1.AddRange(Zeros(6)); Be16(avc1, 1); avc1.AddRange(Zeros(16)); Be16(avc1, _w); Be16(avc1, _h); Be32(avc1, 0x00480000); Be32(avc1, 0x00480000); Be32(avc1, 0); Be16(avc1, 1); avc1.AddRange(Zeros(32)); Be16(avc1, 0x18); Be16(avc1, 0xFFFF); avc1.AddRange(Box("avcC", avcC));
            var stsd = new List<byte>(); Be32(stsd, 0); Be32(stsd, 1); stsd.AddRange(Box("avc1", avc1));
            var stts = new List<byte>(); Be32(stts, 0); Be32(stts, _durations.Count); foreach (var d in _durations) { Be32(stts, 1); Be32(stts, Math.Max(1, d)); }
            var stsc = new List<byte>(); Be32(stsc, 0); Be32(stsc, 1); Be32(stsc, 1); Be32(stsc, _samples.Count); Be32(stsc, 1);
            var stsz = new List<byte>(); Be32(stsz, 0); Be32(stsz, 0); Be32(stsz, _samples.Count); foreach (var s in _samples) Be32(stsz, s.Length);
            var stco = new List<byte>(); Be32(stco, 0); Be32(stco, 1); Be32(stco, mdatStart);
            var stbl = Box("stbl", Box("stsd", stsd), Box("stts", stts), Box("stsc", stsc), Box("stsz", stsz), Box("stco", stco));
            var minf = Box("minf", Box("vmhd", vmhd), Box("dinf", Box("dref", dref)), stbl);
            var moov = Box("moov", Box("mvhd", mvhd), Box("trak", Box("tkhd", tkhd), Box("mdia", Box("mdhd", mdhd), Box("hdlr", hdlr), minf)));
            using (var f = System.IO.File.Create(_path))
            {
                var hdr = new List<byte>(); Be32(hdr, 8 + mdatBody.Count); foreach (char c in "mdat") hdr.Add((byte)c);
                f.Write(ftyp.ToArray(), 0, ftyp.Count); f.Write(hdr.ToArray(), 0, 8); f.Write(mdatBody.ToArray(), 0, mdatBody.Count); f.Write(moov.ToArray(), 0, moov.Count);
            }
        }
        static byte[] Prepend(byte b, byte[] a) { var o = new byte[a.Length + 1]; o[0] = b; Array.Copy(a, 0, o, 1, a.Length); return o; }
        static void Matrix(List<byte> o) { Be32(o, 0x00010000); Be32(o, 0); Be32(o, 0); Be32(o, 0); Be32(o, 0x00010000); Be32(o, 0); Be32(o, 0); Be32(o, 0); Be32(o, 0x40000000); }
    }
}
