// Universe · render · the .cdf97w's own coder: a wavelet's quantised terms packed by what they are, not as plain bytes. An
// adaptive binary range coder (the same arithmetic as LZMA's), each term as: is it nonzero (its odds learned separately for every
// level of the wavelet and for how many of its left and upper neighbours are nonzero), its sign, and its size (a learned unary up
// to 15 for each level, then Exp-Golomb). Measured on the tour (reference/univsmatics/FRONTMAN-RESEARCH.md, H6): 1.4 to 1.75
// times smaller than Deflate on the same terms. Each plane is cut into strips of rows, each its own stream, all coded at once.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    internal static class WaveletCoder
    {
        private const int Levels = 16, Unary = 15, Golomb = 32;

        /// <summary>Which level of the wavelet each position of a <paramref name="pw"/> × <paramref name="ph"/> plane belongs to (1 the finest).</summary>
        public static byte[] LevelMap(int pw, int ph)
        {
            var L = new byte[pw * ph]; int w = pw, h = ph; byte lv = 1;
            while (w >= 4 && h >= 4 && w % 2 == 0 && h % 2 == 0)
            {
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) L[y * pw + x] = lv;
                w /= 2; h /= 2; if (lv < Levels - 1) lv++;
            }
            return L;
        }

        private sealed class Model
        {
            public readonly ushort[] Sig = new ushort[Levels * 3], Mag = new ushort[Levels * Unary], Pre = new ushort[Golomb]; public ushort Sign = 1024;
            public Model() { Fill(Sig); Fill(Mag); Fill(Pre); }
            private static void Fill(ushort[] a) { for (int i = 0; i < a.Length; i++) a[i] = 1024; }
        }

        // ── the range coder (LZMA's: 11-bit odds, moved a thirty-second of the way each bit) ──
        private sealed class Encoder
        {
            private ulong _low; private uint _range = 0xFFFFFFFF; private byte _cache; private long _cacheSize = 1; public readonly List<byte> Out = new List<byte>();
            public void Bit(ref ushort p, int bit)
            {
                uint bound = (_range >> 11) * p;
                if (bit == 0) { _range = bound; p += (ushort)((2048 - p) >> 5); }
                else { _low += bound; _range -= bound; p -= (ushort)(p >> 5); }
                while (_range < (1u << 24)) { _range <<= 8; ShiftLow(); }
            }
            public void Direct(int bit) { _range >>= 1; if (bit != 0) _low += _range; while (_range < (1u << 24)) { _range <<= 8; ShiftLow(); } }
            private void ShiftLow()
            {
                if ((uint)_low < 0xFF000000u || (uint)(_low >> 32) != 0)
                {
                    byte temp = _cache;
                    do { Out.Add((byte)(temp + (byte)(_low >> 32))); temp = 0xFF; } while (--_cacheSize != 0);
                    _cache = (byte)((uint)_low >> 24);
                }
                _cacheSize++; _low = (uint)_low << 8;
            }
            public void Flush() { for (int i = 0; i < 5; i++) ShiftLow(); }
        }
        private sealed class Decoder
        {
            private uint _range = 0xFFFFFFFF, _code; private readonly byte[] _in; private int _at;
            public Decoder(byte[] data) { _in = data; for (int i = 0; i < 5; i++) _code = (_code << 8) | Next(); }
            private byte Next() { return _at < _in.Length ? _in[_at++] : (byte)0; }
            public int Bit(ref ushort p)
            {
                uint bound = (_range >> 11) * p; int bit;
                if (_code < bound) { _range = bound; p += (ushort)((2048 - p) >> 5); bit = 0; }
                else { _code -= bound; _range -= bound; p -= (ushort)(p >> 5); bit = 1; }
                while (_range < (1u << 24)) { _range <<= 8; _code = (_code << 8) | Next(); }
                return bit;
            }
            public int Direct()
            {
                _range >>= 1; int bit = 0; if (_code >= _range) { _code -= _range; bit = 1; }
                while (_range < (1u << 24)) { _range <<= 8; _code = (_code << 8) | Next(); }
                return bit;
            }
        }

        /// <summary>How many strips of rows each plane is cut into: each strip coded on its own, every strip of every plane at once on
        /// the processor's cores (one stream had taken 130 ms a 1080p frame on one core; a strip starts its learning afresh, which costs
        /// about a percent).</summary>
        public const int Strips = 4;

        /// <summary>Packs the planes' quantised terms (each <paramref name="pw"/> × <paramref name="ph"/>): a count of streams, each stream's
        /// length, then the streams, one for each strip of each plane.</summary>
        public static byte[] Pack(short[][] planes, int pw, int ph)
        {
            var L = LevelMap(pw, ph); int n = planes.Length * Strips; var parts = new byte[n][];
            Universe.Core.Jobs.For(n, k => parts[k] = PackStrip(planes[k / Strips], L, pw, Row(k % Strips, ph), Row(k % Strips + 1, ph)));
            var o = new System.IO.MemoryStream(); var w = new System.IO.BinaryWriter(o);
            w.Write((byte)n); foreach (var b in parts) w.Write(b.Length); foreach (var b in parts) w.Write(b);
            return o.ToArray();
        }
        private static int Row(int strip, int ph) { return (int)((long)ph * strip / Strips); }

        private static byte[] PackStrip(short[] q, byte[] L, int pw, int y0, int y1)
        {
            var e = new Encoder(); var m = new Model();
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < pw; x++)
                {
                    int i = y * pw + x, v = q[i], c = (x > 0 && q[i - 1] != 0 ? 1 : 0) + (y > y0 && q[i - pw] != 0 ? 1 : 0), lv = L[i];
                    e.Bit(ref m.Sig[lv * 3 + c], v != 0 ? 1 : 0);
                    if (v == 0) continue;
                    e.Bit(ref m.Sign, v < 0 ? 1 : 0);
                    int a = Math.Abs(v) - 1, k = 0;
                    while (k < Unary) { int b = a > k ? 1 : 0; e.Bit(ref m.Mag[lv * Unary + k], b); if (b == 0) break; k++; }
                    if (k == Unary)
                    {
                        int r = a - Unary, nb = 0; while ((r >> nb) != 0) nb++;
                        for (int j = 0; j < nb; j++) e.Bit(ref m.Pre[j], 1);
                        e.Bit(ref m.Pre[nb], 0);
                        for (int j = nb - 1; j >= 0; j--) e.Direct((r >> j) & 1);
                    }
                }
            e.Flush(); return e.Out.ToArray();
        }

        /// <summary>The exact reverse of <see cref="Pack"/>: the terms back into <paramref name="into"/>, every strip at once.</summary>
        public static void Unpack(byte[] data, short[][] into, int pw, int ph)
        {
            var L = LevelMap(pw, ph); var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
            int n = r.ReadByte(); var len = new int[n]; for (int k = 0; k < n; k++) len[k] = r.ReadInt32();
            var parts = new byte[n][]; for (int k = 0; k < n; k++) parts[k] = r.ReadBytes(len[k]);
            Universe.Core.Jobs.For(Math.Min(n, into.Length * Strips), k => UnpackStrip(parts[k], into[k / Strips], L, pw, Row(k % Strips, ph), Row(k % Strips + 1, ph)));
        }

        private static void UnpackStrip(byte[] data, short[] q, byte[] L, int pw, int y0, int y1)
        {
            var d = new Decoder(data); var m = new Model();
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < pw; x++)
                {
                    int i = y * pw + x, c = (x > 0 && q[i - 1] != 0 ? 1 : 0) + (y > y0 && q[i - pw] != 0 ? 1 : 0), lv = L[i];
                    if (d.Bit(ref m.Sig[lv * 3 + c]) == 0) { q[i] = 0; continue; }
                    bool neg = d.Bit(ref m.Sign) == 1;
                    int k = 0; while (k < Unary && d.Bit(ref m.Mag[lv * Unary + k]) == 1) k++;
                    int a = k;
                    if (k == Unary)
                    {
                        int nb = 0; while (nb < Golomb - 1 && d.Bit(ref m.Pre[nb]) == 1) nb++;
                        int r = 0; for (int j = 0; j < nb; j++) r = (r << 1) | d.Direct();
                        a = Unary + r;
                    }
                    int v = a + 1; q[i] = (short)(neg ? -v : v);
                }
        }
    }
}
