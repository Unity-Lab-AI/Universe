// DNA-AND · core · the coefficient waveform engine: the CDF 9/7 biorthogonal wavelet (JPEG2000's wavelet), ported from the
// lab's own Uni Vs Matics engine (UnityAILab/UniVsMatics, ftcore/reconstruct.py) so the numbers match it exactly.
// A grid of values (a height field, a colour channel, a density field...) becomes a sparse sum of wavelet terms,
//     value(x, y) = Σ_k c_k · ψ_k(x, y)
// kept until a share of the grid's energy is captured (an energy target, not a fixed count), packed as sorted positions
// (LEB128 deltas) and Int16 values, and rebuilt by the inverse. Coarse terms are the big shapes, fine terms the detail,
// so a field can be rebuilt at any level of detail, edited by changing a few terms, and saved small.
// Lifting scheme, rows then columns; whole-sample symmetric edges; each side halves while it stays even and at least 4.
using System;
using System.Collections.Generic;

namespace DnaAnd.Core
{
    internal static class Wavelet97
    {
        public const double A97 = -1.586134342059924, B97 = -0.052980118572961, G97 = 0.882911075530934, D97 = 0.443506852043971, K97 = 1.230174104914001;

        /// <summary>A side padded up to the next multiple of 64 (as the reference engine does), so it halves cleanly many times.</summary>
        public static int PadSide(int n) { return (n + 63) / 64 * 64; }

        // one line, forward, every level (in place), the reference engine's _fwd_rows for one row
        private static void FwdLine(double[] a, int off, int stride, int n, double[] s, double[] tmp)
        {
            int size = n;
            while (size >= 4 && size % 2 == 0)
            {
                for (int i = 0; i < size; i++) s[i] = a[off + i * stride];
                for (int i = 1; i < size - 1; i += 2) s[i] += A97 * (s[i - 1] + s[i + 1]); s[size - 1] += 2 * A97 * s[size - 2];
                s[0] += 2 * B97 * s[1]; for (int i = 2; i < size; i += 2) s[i] += B97 * (s[i - 1] + s[i + 1]);
                for (int i = 1; i < size - 1; i += 2) s[i] += G97 * (s[i - 1] + s[i + 1]); s[size - 1] += 2 * G97 * s[size - 2];
                s[0] += 2 * D97 * s[1]; for (int i = 2; i < size; i += 2) s[i] += D97 * (s[i - 1] + s[i + 1]);
                for (int i = 0; i < size; i += 2) s[i] /= K97;
                for (int i = 1; i < size; i += 2) s[i] *= K97;
                int half = size / 2;
                for (int i = 0; i < half; i++) { tmp[i] = s[2 * i]; tmp[half + i] = s[2 * i + 1]; }
                for (int i = 0; i < size; i++) a[off + i * stride] = tmp[i];
                size /= 2;
            }
        }

        // one line, inverse, every level (in place), the reference engine's _inv_rows for one row
        private static void InvLine(double[] a, int off, int stride, int n, double[] s)
        {
            var sizes = new List<int>(); int sz = n;
            while (sz >= 4 && sz % 2 == 0) { sizes.Add(sz); sz /= 2; }
            for (int li = sizes.Count - 1; li >= 0; li--)
            {
                int size = sizes[li], half = size / 2;
                for (int i = 0; i < half; i++) { s[2 * i] = a[off + i * stride]; s[2 * i + 1] = a[off + (half + i) * stride]; }
                for (int i = 0; i < size; i += 2) s[i] *= K97;
                for (int i = 1; i < size; i += 2) s[i] /= K97;
                s[0] -= 2 * D97 * s[1]; for (int i = 2; i < size; i += 2) s[i] -= D97 * (s[i - 1] + s[i + 1]);
                for (int i = 1; i < size - 1; i += 2) s[i] -= G97 * (s[i - 1] + s[i + 1]); s[size - 1] -= 2 * G97 * s[size - 2];
                s[0] -= 2 * B97 * s[1]; for (int i = 2; i < size; i += 2) s[i] -= B97 * (s[i - 1] + s[i + 1]);
                for (int i = 1; i < size - 1; i += 2) s[i] -= A97 * (s[i - 1] + s[i + 1]); s[size - 1] -= 2 * A97 * s[size - 2];
                for (int i = 0; i < size; i++) a[off + i * stride] = s[i];
            }
        }

        /// <summary>Forward 2D transform of a w × h grid (row-major), in place: rows, then columns. Rows run on every core.</summary>
        public static void Forward(double[] g, int w, int h)
        {
            Jobs.For(h, y => { var s = new double[w + 1]; var t = new double[w]; FwdLine(g, y * w, 1, w, s, t); });
            Jobs.For(w, x => { var s = new double[h + 1]; var t = new double[h]; FwdLine(g, x, w, h, s, t); });
        }

        /// <summary>Inverse 2D transform, in place: columns, then rows (the exact reverse of <see cref="Forward"/>).</summary>
        public static void Inverse(double[] g, int w, int h)
        {
            Jobs.For(w, x => { var s = new double[h + 1]; InvLine(g, x, w, h, s); });
            Jobs.For(h, y => { var s = new double[w + 1]; InvLine(g, y * w, 1, w, s); });
        }

        /// <summary>A grid as wavelet equations: the kept terms (sorted positions and quantized values) and the grid's size.</summary>
        public sealed class Field
        {
            public int W, H;                 // padded grid size
            public int[] Pos;                // kept term positions, ascending
            public short[] Val;              // kept term values, quantized
            public double QScale;            // value = Val × QScale
            public int Terms { get { return Pos.Length; } }
        }

        /// <summary>Turns a transformed grid into its equations: keeps the strongest terms until (1 − tol²) of the energy is
        /// captured (so a busy field honestly carries more terms than a plain one), then quantizes them to Int16.</summary>
        public static Field Keep(double[] co, int w, int h, double tol, int minTerms = 16)
        {
            int n = co.Length; double total = 0;
            for (int i = 0; i < n; i++) total += co[i] * co[i];
            if (total <= 0) total = 1;
            var order = new int[n]; for (int i = 0; i < n; i++) order[i] = i;
            var mag = new double[n]; for (int i = 0; i < n; i++) mag[i] = -Math.Abs(co[i]);
            Array.Sort(mag, order);                                              // strongest first
            double want = (1 - tol * tol) * total, cum = 0; int k = 0;
            while (k < n && cum < want) { cum += co[order[k]] * co[order[k]]; k++; }
            k = Math.Max(Math.Min(minTerms, n), k);
            var pos = new int[k]; Array.Copy(order, pos, k); Array.Sort(pos);
            double max = 0; foreach (int p in pos) max = Math.Max(max, Math.Abs(co[p]));
            double q = Math.Max(1e-8, max / 32000.0);
            var val = new short[k];
            for (int i = 0; i < k; i++) val[i] = (short)Math.Max(-32767, Math.Min(32767, Math.Round(co[pos[i]] / q)));
            return new Field { W = w, H = h, Pos = pos, Val = val, QScale = q };
        }

        /// <summary>Rebuilds the grid from its equations. <paramref name="fraction"/> below 1 keeps only the strongest share
        /// of the terms (from exact toward the broad shapes), as the reference engine's deviation dial does.</summary>
        public static double[] Rebuild(Field f, double fraction = 1)
        {
            var g = new double[f.W * f.H];
            int k = f.Terms;
            if (fraction < 1 && k > 0)
            {
                int keep = Math.Max(1, (int)(k * fraction));
                var idx = new int[k]; var key = new int[k];
                for (int i = 0; i < k; i++) { idx[i] = i; key[i] = -Math.Abs((int)f.Val[i]); }
                Array.Sort(key, idx);
                for (int i = 0; i < keep; i++) g[f.Pos[idx[i]]] = f.Val[idx[i]] * f.QScale;
            }
            else for (int i = 0; i < k; i++) g[f.Pos[i]] = f.Val[i] * f.QScale;
            Inverse(g, f.W, f.H);
            return g;
        }

        /// <summary>The kept positions packed as the reference engine's dv1: ascending, as deltas, each a LEB128 varint.</summary>
        public static byte[] PackPositions(int[] pos)
        {
            var o = new List<byte>(pos.Length + 8); long prev = 0;
            for (int i = 0; i < pos.Length; i++)
            {
                ulong d = (ulong)(i == 0 ? pos[0] : pos[i] - prev); prev = pos[i];
                do { byte b = (byte)(d & 0x7F); d >>= 7; if (d != 0) b |= 0x80; o.Add(b); } while (d != 0);
            }
            return o.ToArray();
        }

        public static int[] UnpackPositions(byte[] buf, int count)
        {
            var o = new int[count]; long val = 0, acc = 0; int shift = 0, n = 0;
            foreach (byte b in buf)
            {
                acc |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) != 0) shift += 7;
                else { val += acc; if (n < count) o[n++] = (int)val; acc = 0; shift = 0; }
            }
            return o;
        }

        /// <summary>A field written into a save chunk's bytes: size, scale, term count, packed positions, values.</summary>
        public static void Write(System.IO.BinaryWriter o, Field f)
        {
            o.Write(f.W); o.Write(f.H); o.Write(f.QScale); o.Write(f.Terms);
            var p = PackPositions(f.Pos); o.Write(p.Length); o.Write(p);
            foreach (short v in f.Val) o.Write(v);
        }

        public static Field Read(System.IO.BinaryReader r)
        {
            var f = new Field { W = r.ReadInt32(), H = r.ReadInt32(), QScale = r.ReadDouble() };
            int k = r.ReadInt32(), pl = r.ReadInt32();
            f.Pos = UnpackPositions(r.ReadBytes(pl), k); f.Val = new short[k];
            for (int i = 0; i < k; i++) f.Val[i] = r.ReadInt16();
            return f;
        }

        /// <summary>How close a rebuilt grid is to the original, in decibels (peak signal to noise, over the given range).</summary>
        public static double Psnr(double[] a, double[] b, int w, int h, int padW, double range)
        {
            double se = 0; int n = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { double d = a[y * padW + x] - b[y * padW + x]; se += d * d; n++; }
            double mse = se / Math.Max(1, n);
            return mse <= 0 ? 999 : 10 * Math.Log10(range * range / mse);
        }
    }
}
