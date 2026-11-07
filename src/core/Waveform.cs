// Universe · core · the waveform: every surface's fine detail, at every size, from the CDF 9/7 wavelet and mode-E.
//   The stored levels: a set of detail tiles, each made in the CDF 9/7 coefficient domain (white noise taken into the wavelet
//   domain, every term weighted by its level so finer levels are fainter, as a natural rough surface is, then kept to its
//   strongest terms: the waveform's own equations) and rebuilt from those terms. Each octave of detail is one of these
//   waveforms, turned and shifted its own way.
//   Past the finest stored level: mode-E (ModeE.At) carries on, Newton's method on z^3 - K with cellular octaves that
//   subdivide as the zoom rises, so detail of the same character goes on growing at every finer size, without end and
//   without repeating.
//   The ground's fine roughness (PlanetTerrain) and every grain's and stone's face and stains (GrainField) take their detail
//   from here, so one engine makes the detail of the land from the planet down to the face of a grain.
using System;

namespace Universe.Core
{
    internal static class Waveform
    {
        private const int T = 128;               // each stored waveform's side
        public const int Stored = 12;            // octaves made from stored CDF 9/7 waveforms; finer ones come from mode-E
        private static double[][] Tiles;
        /// <summary>Makes the stored waveforms: once, at start, on the main thread (never first on a worker, which could wait
        /// on the very workers the wavelet uses).</summary>
        public static void Init() { if (Tiles == null) Tiles = MakeTiles(); }
        /// <summary>All the stored waveforms, one after another (6 of 128 x 128), to upload to the card once.</summary>
        public static float[] All()
        {
            Init(); var a = new float[Tiles.Length * T * T];
            for (int k = 0; k < Tiles.Length; k++) for (int i = 0; i < T * T; i++) a[k * T * T + i] = (float)Tiles[k][i];
            return a;
        }

        private static double[][] MakeTiles()
        {
            var t = new double[6][];
            for (int k = 0; k < t.Length; k++) t[k] = Synth(k);
            return t;
        }

        // one stored waveform: noise taken into the CDF 9/7 domain, shaped there (each level's terms fainter the finer it is),
        // kept to its strongest terms (its equations), and rebuilt from them; scaled to an even strength
        private static double[] Synth(int k)
        {
            var g = new double[T * T]; var r = new Rng((ulong)(k * 7919 + 17));
            for (int i = 0; i < g.Length; i++) g[i] = r.NextDouble() * 2 - 1;
            Wavelet97.Forward(g, T, T);
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    int m = Math.Max(x, y) + 1;                                            // which level this term is in (coarse at the top left)
                    g[y * T + x] *= Math.Pow(m, -1.1);
                }
            var f = Wavelet97.Keep(g, T, T, 0.03);
            var back = Wavelet97.Rebuild(f);
            double sum = 0; for (int i = 0; i < T * T; i++) sum += back[i] * back[i];
            double rms = Math.Sqrt(sum / (T * T)); if (rms <= 0) rms = 1;
            var o = new double[T * T]; for (int i = 0; i < T * T; i++) o[i] = back[i] / rms / 2.5;
            return o;
        }

        // a stored waveform at (u, v), read smoothly, mirrored at its edges so it carries on without a seam
        private static double Tile(int k, double u, double v)
        {
            var t = Tiles[k % Tiles.Length];
            u = Mirror(u * T); v = Mirror(v * T);
            int x0 = (int)u, y0 = (int)v, x1 = Math.Min(T - 1, x0 + 1), y1 = Math.Min(T - 1, y0 + 1); double fx = u - x0, fy = v - y0;
            double a = t[y0 * T + x0] + (t[y0 * T + x1] - t[y0 * T + x0]) * fx, b = t[y1 * T + x0] + (t[y1 * T + x1] - t[y1 * T + x0]) * fx;
            return a + (b - a) * fy;
        }
        private static double Mirror(double p)
        {
            double m = p % (2 * (T - 1)); if (m < 0) m += 2 * (T - 1);
            return m > T - 1 ? 2 * (T - 1) - m : m;
        }

        private static double H(long a, long b) { ulong h = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL; h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29; return (h & 0xFFFFFF) / (double)0x1000000; }

        // one octave at a point: a stored waveform (or mode-E past the stored levels), on the three planes through the point,
        // each turned and shifted its own way, blended
        /// <summary>One octave of the waveform at a point: octave k (0 the broadest), from a stored CDF 9/7 waveform while k is
        /// below <paramref name="stored"/>, from mode-E past it.</summary>
        public static double Layer(double x, double y, double z, long key, int k, int stored) { return Octave(x, y, z, key, k, stored); }
        private static double Octave(double x, double y, double z, long key, int k) { return Octave(x, y, z, key, k, Stored); }
        private static double Octave(double x, double y, double z, long key, int k, int stored)
        {
            double sum = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                double u = plane == 0 ? x : plane == 1 ? y : z, v = plane == 0 ? y : plane == 1 ? z : x;
                double a = H(key + plane * 31, k) * Math.PI * 2, c = Math.Cos(a), s = Math.Sin(a);
                double uu = u * c - v * s + H(key + plane * 31, k + 97) * 37, vv = u * s + v * c + H(key + plane * 31, k + 191) * 41;
                if (k < stored) sum += Tile(k + plane, uu * 0.02, vv * 0.02);
                else
                {
                    // past the stored waveforms: mode-E, its zoom rising with each octave, so the cells keep subdividing
                    var view = new ModeE.View { Cx = 0, Cy = 0, Span = 1.5, Seed = H(key, k + 5), Zoom = Math.Pow(2, k - stored + 1), KBias = H(key, k + 7), Ang = H(key, k + 9) * 6.28 };
                    var pt = ModeE.At((uu * 0.02 % 3) - 1.5, (vv * 0.02 % 3) - 1.5, view);
                    sum += (pt.Shade - 0.75) * 1.4;
                }
            }
            return sum / 3;
        }

        /// <summary>Detail at a point (in units where the first octave is a whole cycle across f0 = 1): the sum of octaves
        /// from frequency f0, each a factor of two finer and gain times as strong, every one whose size is at least about
        /// half of <paramref name="limit"/>'s (the finest that can show), the finest fading in as you come in, never switching.
        /// Octaves past <see cref="Stored"/> come from mode-E. Returns roughly -1..1 times amp0.</summary>
        public static double Octaves(double x, double y, double z, long key, double f0, double amp0, double gain, double maxFreq)
        {
            double sum = 0, amp = amp0, f = f0;
            for (int k = 0; k < 48; k++)
            {
                double fine = maxFreq / f; if (fine < 0.5) break;
                double w = Math.Min(1, fine - 0.5);
                sum += w * amp * Octave(x * f, y * f, z * f, key, k);
                amp *= gain; f *= 2;
            }
            return sum;
        }
    }
}
