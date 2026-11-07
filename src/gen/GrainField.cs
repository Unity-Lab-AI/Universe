// Universe · the grains of a planet's ground, one field for the whole world: where every grain is, how big, its shape and
// what mineral it is. The ground's finest detail is the top of these grains, and the matter view reads the same grains
// when you go in, so the land carries on down as one surface: no layer of stones laid over it.
//   Grains sit one to each cube of space a grain across (planet km), on the planet's shell: a grain's place on the ground
//   is its cube's point (jittered) pushed out to the surface; it lies sunk into the ground by most of its height.
//   Its mineral comes from its ground's mix (content/minerals.unt @mix: shares and grain sizes).
using System;
using System.Collections.Generic;
using Universe.Core;

namespace Universe.Gen
{
    internal static class GrainField
    {
        public sealed class Mix { public string Id; public string[] Minerals; public double[] Share; public float[][] Colour; public double Size; public double[] ClassSize = new double[0], ClassCover = new double[0]; public int Index; }
        // size class c: 0 the sand itself, 1 and on its clasts (granules, pebbles, cobbles, boulders), biggest last
        public static double SizeOf(Mix m, int c) { return c == 0 ? m.Size : m.ClassSize[c - 1]; }
        public static int Classes(Mix m) { return 1 + m.ClassSize.Length; }
        private static readonly Dictionary<string, Mix> Mixes = new Dictionary<string, Mix>();
        private static readonly List<Mix> Order = new List<Mix>();
        /// <summary>The most minerals of a mix the card is given, and each mix's length in the palette.</summary>
        public const int PalMinerals = 16, PalStride = 2 + PalMinerals + PalMinerals * 3;
        /// <summary>Every mix for the card, one after another: its mineral count, then each mineral's share added up (as a
        /// share of the whole), then each mineral's colour; so the card picks a grain's mineral and colour as the processor does.</summary>
        public static float[] Palette()
        {
            var p = new float[Order.Count * PalStride];
            foreach (var m in Order)
            {
                int b = m.Index * PalStride, n = Math.Min(PalMinerals, m.Share.Length); double total = 0, run = 0; foreach (var v in m.Share) total += v;
                p[b] = n;
                for (int q = 0; q < n; q++) { run += m.Share[q]; p[b + 2 + q] = q == n - 1 ? 1f : (float)(run / total); var c = m.Colour[q]; p[b + 2 + PalMinerals + q * 3] = c[0]; p[b + 3 + PalMinerals + q * 3] = c[1]; p[b + 4 + PalMinerals + q * 3] = c[2]; }
            }
            return p;
        }
        /// <summary>The planet's grain seed folded to the one 32-bit word the hash takes (the card's shaders take it so).</summary>
        public static uint SeedWord(ulong seed) { return (uint)seed ^ (uint)(seed >> 32) * 0x27D4EB2Fu; }

        /// <summary>Reads every ground's mix: its minerals, their shares and colours, and its grains' size (km).</summary>
        public static void Load(ContentLibrary content)
        {
            if (Mixes.Count > 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            var col = new Dictionary<string, float[]>();
            foreach (var e in content.OfKind("minerals.unt", "mineral"))
            {
                var w = e.Get("colour", "0.7 0.7 0.7").Split(' '); float r = 0.7f, g = 0.7f, b = 0.7f;
                if (w.Length == 3) { float.TryParse(w[0], fs, ci, out r); float.TryParse(w[1], fs, ci, out g); float.TryParse(w[2], fs, ci, out b); }
                col[e.Id] = new[] { r, g, b };
            }
            foreach (var e in content.OfKind("minerals.unt", "mix"))
            {
                var ids = new List<string>(); var sh = new List<double>(); var cs = new List<float[]>();
                foreach (var part in e.Get("minerals", "").Split(','))
                {
                    var w = part.Trim().Split(' '); double share; float[] c;
                    if (w.Length == 2 && double.TryParse(w[1], fs, ci, out share) && col.TryGetValue(w[0], out c)) { ids.Add(w[0]); sh.Add(share); cs.Add(c); }
                }
                double lo, hi; if (ids.Count == 0 || !e.TryRange("grain-mm", out lo, out hi)) continue;
                var mx = new Mix { Id = e.Id, Minerals = ids.ToArray(), Share = sh.ToArray(), Colour = cs.ToArray(), Size = (lo + hi) * 0.5 * 1e-6 };
                var csz = new List<double>(); var ccv = new List<double>();
                foreach (var part in e.Get("clasts", "").Split(','))
                {
                    var w = part.Trim().Split(' '); if (w.Length != 3) continue; var rg = w[1].Split(new[] { ".." }, StringSplitOptions.None); double a, b2, cv;
                    if (rg.Length == 2 && double.TryParse(rg[0], fs, ci, out a) && double.TryParse(rg[1], fs, ci, out b2) && double.TryParse(w[2], fs, ci, out cv)) { csz.Add(Math.Sqrt(a * b2) * 1e-6); ccv.Add(cv); }
                }
                mx.ClassSize = csz.ToArray(); mx.ClassCover = ccv.ToArray();
                mx.Index = Order.Count; Order.Add(mx); Mixes[e.Id] = mx;
            }
        }

        public static Mix For(string id) { Mix m; return id != null && Mixes.TryGetValue(id, out m) ? m : null; }
        /// <summary>A body's own mix: its kind's minerals with each share scattered from the kind's by the body's seed (vary: the spread of
        /// the share's factor, as a logarithm; about half up or down at one, a third at the usual setting), registered under its own id
        /// (the kind's id, a hash sign, the seed); the same id again is the same mix. Its palette slot is its kind's: the same minerals
        /// and colours, only the shares differ, and the shares are picked on the processor.</summary>
        public static Mix Vary(string id, ulong seed, double vary)
        {
            Mix b = For(id); if (b == null || vary <= 0) return b;
            string vid = id + "#" + (seed % 1000000UL); Mix m; if (Mixes.TryGetValue(vid, out m)) return m;
            var sh = new double[b.Share.Length];
            for (int q = 0; q < sh.Length; q++) { double g = (Hash(q, 1, 0, seed) + Hash(q, 2, 0, seed) + Hash(q, 3, 0, seed) - 1.5) * 2; sh[q] = b.Share[q] * Math.Exp(vary * g); }
            m = new Mix { Id = vid, Minerals = b.Minerals, Share = sh, Colour = b.Colour, Size = b.Size, ClassSize = b.ClassSize, ClassCover = b.ClassCover, Index = b.Index };
            Mixes[vid] = m; return m;
        }

        public static double Hash(long i, long j, long k, ulong seed)
        {
            // 32-bit wrapped words and a 32-bit mix, so the card's shaders work out exactly the same grains (HLSL has no 64-bit
            // multiply to rely on): the same bits on the processor and the card, never two grain beds
            uint h = (uint)i * 0x9E3779B1u ^ (uint)j * 0x85EBCA77u ^ (uint)k * 0xC2B2AE3Du ^ SeedWord(seed);
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }

        /// <summary>One grain: its key, its direction from the planet's middle (unit), its size, how deep its middle lies
        /// below the ground, its mineral (index into the mix) and its shade.</summary>
        public struct Grain { public long Key; public double Dx, Dy, Dz, Rad, Depth; public int Mineral; public float Shade; }

        private static long Key(long i, long j, long k) { return (uint)((uint)i * 73856093u ^ (uint)j * 19349663u ^ (uint)k * 83492791u); }   // 32-bit wrapped, as the card works it out

        // the grain of cube (i, j, k), if that cube's point lies on the planet's shell (within half a grain of radius R)
        public static bool At(long i, long j, long k, int layer, double R, Mix m, ulong seed, out Grain g) { return At(i, j, k, layer, 0, R, m, seed, out g); }
        public static bool At(long i, long j, long k, int layer, int cls, double R, Mix m, ulong seed, out Grain g)
        {
            g = new Grain(); double G = SizeOf(m, cls); long key = (Key(i, j, k) * 8 + cls) * 2 + layer;
            if (cls > 0 && (layer > 0 || Hash(key, 8, 0, seed) > Math.Min(1, m.ClassCover[cls - 1] * 2))) return false;   // the clasts lie only here and there, by how much they cover
            double px = (i + 0.2 + 0.6 * Hash(key, 1, 0, seed)) * G, py = (j + 0.2 + 0.6 * Hash(key, 2, 0, seed)) * G, pz = (k + 0.2 + 0.6 * Hash(key, 3, 0, seed)) * G;
            double l = Math.Sqrt(px * px + py * py + pz * pz); if (Math.Abs(l - R) > G * 0.5) return false;
            g.Key = key; g.Dx = px / l; g.Dy = py / l; g.Dz = pz / l;
            g.Rad = G * (0.3 + 0.2 * Hash(key, 4, 0, seed));
            g.Depth = cls > 0 ? g.Rad * (0.3 + 0.35 * Hash(key, 5, 0, seed)) : layer == 0 ? g.Rad * (0.55 + 0.3 * Hash(key, 5, 0, seed)) : G * 0.75 + g.Rad * 0.6;   // stones rest half sunk; the sand's top grains mostly sunk in, the ones under them fill the gaps
            double total = 0; foreach (var s in m.Share) total += s;
            double t = Hash(key, 6, 0, seed) * total; g.Mineral = m.Share.Length - 1;
            for (int q = 0; q < m.Share.Length; q++) { t -= m.Share[q]; if (t <= 0) { g.Mineral = q; break; } }
            g.Shade = (float)(0.75 + 0.35 * Hash(key, 7, 0, seed));
            return true;
        }

        /// <summary>Every grain and stone (all size classes) whose place on the ground is within <paramref name="reachKm"/>
        /// of the unit direction (dx, dy, dz) on a planet of radius <paramref name="R"/>.</summary>
        public static void Near(double dx, double dy, double dz, double R, double reachKm, Mix m, ulong seed, List<Grain> into)
        {
            for (int c = 0; c < Classes(m); c++) NearClass(dx, dy, dz, R, reachKm, c, m, seed, into);
        }
        private static void NearClass(double dx, double dy, double dz, double R, double reachKm, int cls, Mix m, ulong seed, List<Grain> into)
        {
            double G = SizeOf(m, cls); long n = (long)Math.Ceiling(reachKm / G) + 1;
            double px = dx * R, py = dy * R, pz = dz * R;
            double ex, ey, ez, nx, ny, nz; Axes(dx, dy, dz, out ex, out ey, out ez, out nx, out ny, out nz);
            // step across the ground's plane (closer than a cube, so none is skipped) and take the cubes the shell passes
            // through there: the one at the step and those either side of it along the axis the shell crosses
            int ax = Math.Abs(dx) >= Math.Abs(dy) && Math.Abs(dx) >= Math.Abs(dz) ? 0 : Math.Abs(dy) >= Math.Abs(dz) ? 1 : 2;
            var seen = new HashSet<long>(); double st = 0.7; long ns = (long)Math.Ceiling(n / st);
            for (long a = -ns; a <= ns; a++)
                for (long b = -ns; b <= ns; b++)
                {
                    double qx = px + (ex * a + nx * b) * G * st, qy = py + (ey * a + ny * b) * G * st, qz = pz + (ez * a + nz * b) * G * st;
                    long i0 = (long)Math.Floor(qx / G), j0 = (long)Math.Floor(qy / G), k0 = (long)Math.Floor(qz / G);
                    for (int d = -1; d <= 1; d++)
                    {
                        long i = i0 + (ax == 0 ? d : 0), j = j0 + (ax == 1 ? d : 0), k = k0 + (ax == 2 ? d : 0), key = Key(i, j, k); if (!seen.Add(key)) continue;
                        for (int layer = 0; layer < 2; layer++)
                        {
                            Grain g; if (!At(i, j, k, layer, cls, R, m, seed, out g)) continue;
                            double hx = (g.Dx - dx) * R, hy = (g.Dy - dy) * R, hz = (g.Dz - dz) * R;
                            if (hx * hx + hy * hy + hz * hz > (reachKm + G) * (reachKm + G)) continue;
                            into.Add(g);
                        }
                    }
                }
        }

        /// <summary>The ground's own east and north at a unit direction up.</summary>
        public static void Axes(double ux, double uy, double uz, out double ex, out double ey, out double ez, out double nx, out double ny, out double nz)
        {
            double ax = Math.Abs(uy) < 0.99 ? 0 : 1, ay = Math.Abs(uy) < 0.99 ? 1 : 0, az = 0;
            ex = ay * uz - az * uy; ey = az * ux - ax * uz; ez = ax * uy - ay * ux; double l = Math.Sqrt(ex * ex + ey * ey + ez * ez); ex /= l; ey /= l; ez /= l;
            nx = uy * ez - uz * ey; ny = uz * ex - ux * ez; nz = ux * ey - uy * ex;
        }

        // value noise for a grain's rough face
        private static double VNoise(double x, double y, double z, long key)
        {
            long xi = (long)Math.Floor(x), yi = (long)Math.Floor(y), zi = (long)Math.Floor(z); double fx = x - xi, fy = y - yi, fz = z - zi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            Func<long, long, long, double> h = (a, b, c) => Hash(a, b, c, (ulong)key * 0x9E37UL + 11);
            double x00 = h(xi, yi, zi) + (h(xi + 1, yi, zi) - h(xi, yi, zi)) * fx, x10 = h(xi, yi + 1, zi) + (h(xi + 1, yi + 1, zi) - h(xi, yi + 1, zi)) * fx;
            double x01 = h(xi, yi, zi + 1) + (h(xi + 1, yi, zi + 1) - h(xi, yi, zi + 1)) * fx, x11 = h(xi, yi + 1, zi + 1) + (h(xi + 1, yi + 1, zi + 1) - h(xi, yi + 1, zi + 1)) * fx;
            double y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }

        /// <summary>How far a grain's face is from its middle along a direction in its own frame (east, up, north; unit),
        /// with detail down to <paramref name="cell"/> km: a lumpy egg, its roughness at every size, its atomic steps below
        /// about 600 nm. The ground and the matter view both use this, so a grain is the same shape from both.</summary>
        public static double Surface(long key, double rad, double dx, double dy, double dz, double cell, ulong seed)
        {
            double sx = 0.8 + 0.4 * Hash(key, 16, 0, seed), sy = 0.6 + 0.35 * Hash(key, 17, 0, seed), sz = 0.8 + 0.4 * Hash(key, 18, 0, seed), turn = Hash(key, 19, 0, seed) * Math.PI * 2;
            double ct = Math.Cos(turn), st = Math.Sin(turn), ux = dx * ct + dz * st, uz = -dx * st + dz * ct;
            double r = rad / Math.Sqrt(ux * ux / (sx * sx) + dy * dy / (sy * sy) + uz * uz / (sz * sz));
            // its roughness at every size: the waveform (stored CDF 9/7 levels, then mode-E finer), to the finest that can show
            // (a crystal face is not rough at every size: below about a micrometre it is flat terraces and steps, not hills)
            double rough = Waveform.Octaves(ux, dy, uz, key, 2.5, 0.16, 0.5, Math.Min(rad / cell * 2, rad / 1e-9));
            // its face is broken and grown in steps at every size (cleavage and growth terraces, curved fracture ripples):
            // each size of step a quarter the one above, coming in as the detail reaches it, so there is always more to see
            double steps = 0, sh = 0.03, sf = 3;
            for (int k = 0; k < 40; k++)
            {
                double size = rad / sf; if (size / cell < 0.7) break;
                double w = Math.Min(1, size / cell - 0.7);
                double field = VNoise(ux * sf * 0.7 + k * 3.1, dy * sf * 0.7, uz * sf * 0.7, key * 31 + k + 900) * 3 + dy * sf * 0.15;   // tilted, curved step lines
                double fl = Math.Floor(field), fr = field - fl, edge = fr < 0.85 ? 0 : (fr - 0.85) / 0.15;                         // flat treads, steep risers
                steps += w * sh * (fl + edge * edge) * 0.33;
                sh *= 0.25; sf *= 4;
            }
            r *= 1 + rough + steps * 0.5;
            if (cell < 6e-10)
            {
                double tq = Math.Max(0, Math.Min(1, Math.Log(6e-10 / cell) / Math.Log(2))), stepped = Math.Floor(r / 3e-13) * 3e-13;
                r = r + (stepped - r) * tq;
            }
            return r;
        }

        /// <summary>How a spot of a grain's face is coloured (multipliers on its mineral's colour): stains and coatings in
        /// patches at every size (iron oxide orange-red, pale clay), darker in its cracks; fixed on the grain, never moving.</summary>
        public static void Stain(long key, double dx, double dy, double dz, double rad, double cell, out float r, out float g, out float b)
        {
            // its stains and cracks at every size: the waveform too
            double lim = Math.Min(rad / cell * 1.6, rad / 2e-10); if (lim < 2) { r = g = b = 1; return; }   // stains no finer than a fifth of a micrometre
            double iron = Waveform.Octaves(dx, dy, dz, key * 7 + 300, 2.0, 1, 0.6, lim), clay = Waveform.Octaves(dx + 9, dy, dz - 4, key * 7 + 400, 2.0, 1, 0.6, lim);
            double crackv = Waveform.Octaves(dx * 1.7, dy * 1.7, dz * 1.7, key * 7 + 500, 2.0, 1, 0.6, lim), dark = Math.Max(0, 0.5 - Math.Abs(crackv) * 3), norm = 2.4;
            if (norm <= 0) { r = g = b = 1; return; }
            iron = Math.Max(0, iron / norm * 2.4 - 0.15); clay = Math.Max(0, clay / norm * 2.4 - 0.2); dark = Math.Min(0.6, dark / norm * 1.5);
            double ir = Math.Min(0.6, iron), cl = Math.Min(0.5, clay);
            r = (float)((1 + ir * 0.25 + cl * 0.3) * (1 - dark)); g = (float)((1 - ir * 0.35 + cl * 0.3) * (1 - dark)); b = (float)((1 - ir * 0.55 + cl * 0.25) * (1 - dark));
        }

        /// <summary>The top of the ground's loose bed at a unit direction (km above the ground's own level there): the sand's
        /// grains, and the stones of every size lying on it; each size class comes in as <paramref name="cell"/> (the finest
        /// detail drawn there) grows fine enough to show it, so the ground breaks into boulders, then cobbles, pebbles and
        /// sand as you come in. The grain or stone that is the top there; false in a gap between sand grains.</summary>
        public static bool Top(double dx, double dy, double dz, double R, Mix m, ulong seed, double cell, out double top, out Grain hit)
        {
            top = 0; hit = new Grain(); bool on = false; double best = double.MinValue;
            for (int c = Classes(m) - 1; c >= 0; c--)
            {
                double G = SizeOf(m, c), w = Math.Max(0, Math.Min(1, (G * 0.25 - cell) / (G * 0.15))); if (w <= 0) continue;
                double t; Grain g; bool o = TopClass(dx, dy, dz, R, m, c, seed, cell, out t, out g);
                if (c > 0 && !o) continue;                                              // no stone of this size here
                double v = t * w;
                if (v > best) { best = v; hit = g; on = o; top = v; }
            }
            if (best == double.MinValue) { top = 0; return false; }
            return on;
        }
        private static bool TopClass(double dx, double dy, double dz, double R, Mix m, int cls, ulong seed, double cell, out double top, out Grain hit)
        {
            double ex, ey, ez, nx, ny, nz; Axes(dx, dy, dz, out ex, out ey, out ez, out nx, out ny, out nz);
            double G = SizeOf(m, cls); top = -G * 0.45; hit = new Grain(); bool any = false;
            long i0 = (long)Math.Floor(dx * R / G), j0 = (long)Math.Floor(dy * R / G), k0 = (long)Math.Floor(dz * R / G);
            for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++) for (int dk = -1; dk <= 1; dk++)   // a grain near here has its cube next to this one
            for (int layer = 0; layer < 2; layer++)
            {
                Grain g; if (!At(i0 + di, j0 + dj, k0 + dk, layer, cls, R, m, seed, out g)) continue;
                double ox = (dx - g.Dx) * R, oy = (dy - g.Dy) * R, oz = (dz - g.Dz) * R;
                double a = ox * ex + oy * ey + oz * ez, b = ox * nx + oy * ny + oz * nz, d2 = a * a + b * b;
                if (d2 > g.Rad * g.Rad * 1.6) continue;
                double py = g.Rad * 0.6; bool ok = true;
                for (int it = 0; it < 2; it++)
                {
                    double l = Math.Sqrt(a * a + py * py + b * b); if (l <= 0) { ok = false; break; }
                    double rr = Surface(g.Key, g.Rad, a / l, py / l, b / l, cell, seed), hz2 = rr * rr - d2; if (hz2 <= 0) { ok = false; break; }
                    py = Math.Sqrt(hz2);
                }
                if (!ok) continue;
                double ty = py - g.Depth;
                if (ty > top) { top = ty; hit = g; any = true; }
            }
            return any;
        }
    }
}
