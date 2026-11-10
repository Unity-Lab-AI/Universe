// Universe Â· shell Â· the matter view, its part: surface (one class in several files: src/shell/MatterView.cs is its start)
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        // ── a grain's real surface: one shape at every size, so zooming into a grain never swaps it for anything else ──
        // a squashed, turned ellipsoid, roughened by detail on detail (each finer layer half as tall, without end), worked out from
        // the grain's own seed; drawn coarse as a stone from afar, and close in as a patch under you, finer the closer you come.
        // Below about 300 nm its height steps in single atomic layers (0.3 nm), so terraces form along its slopes as on real faces.
        private double VNoise(double x, double y, double z, long key)
        {
            long xi = (long)Math.Floor(x), yi = (long)Math.Floor(y), zi = (long)Math.Floor(z);
            double fx = x - xi, fy = y - yi, fz = z - zi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            Func<long, long, long, double> h = (a, b, c) => Hash(a * 7 + key, b, c + key * 31, _seed);
            double x00 = h(xi, yi, zi) + (h(xi + 1, yi, zi) - h(xi, yi, zi)) * fx, x10 = h(xi, yi + 1, zi) + (h(xi + 1, yi + 1, zi) - h(xi, yi + 1, zi)) * fx;
            double x01 = h(xi, yi, zi + 1) + (h(xi + 1, yi, zi + 1) - h(xi, yi, zi + 1)) * fx, x11 = h(xi, yi + 1, zi + 1) + (h(xi + 1, yi + 1, zi + 1) - h(xi, yi + 1, zi + 1)) * fx;
            double y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }

        // how far the grain's surface is from its middle, along the unit direction (dx, dy, dz), with detail down to 'cell' (km)
        private double GrainSurface(long gi, long gj, double rad, double dx, double dy, double dz, double cell)
        {
            return Universe.Gen.GrainField.Surface(gi, rad, dx, dy, dz, cell, _gseed);   // the same shape the planet's ground gives it
        }

        // the point on top of a grain straight above (x, z), and false if (x, z) is off the grain
        // the one cell the face's coarse shape is read at (the camera's height, the spots to pick, the face's rings): finer detail is the
        // card's, per pixel, so nothing worked out here stands off anything else
        private static double CoarseCell(double s, double frad) { return Math.Max(Math.Max(s / 40, AtomSpacing), s < frad * 1.5 ? frad * 0.004 : 0); }
        private bool GrainTop(long gi, long gj, double cx, double cy, double cz, double rad, double x, double z, double cell, out double y)
        {
            y = cy + rad; double dx = x - cx, dz = z - cz;
            if (dx * dx + dz * dz > rad * rad * 1.6) return false;
            double py = cy + rad * 0.6;
            for (int it = 0; it < 2; it++)
            {
                double ex = dx, ey = py - cy, ez = dz, l = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (l <= 0) return false;
                double rr = GrainSurface(gi, gj, rad, ex / l, ey / l, ez / l, cell);
                double hz2 = rr * rr - dx * dx - dz * dz; if (hz2 <= 0) return false;
                py = cy + Math.Sqrt(hz2);
            }
            y = py; return true;
        }

        // the grain you dive into: where it is and how big
        private void FocusGrain(out long gi, out long gj, out double gx, out double gy, out double gz, out double rad)
        {
            var g = FocusRec;
            if (g == null) { gi = 0; gj = 0; rad = Grain * 0.4; gx = _fx; gz = _fz; gy = -rad * 0.7; return; }
            gi = g.Key; gj = 0; rad = g.Rad; gx = g.X; gz = g.Z; gy = g.Y;
        }

        // the patch of the grain's surface under you, as a fine lit mesh
        private readonly Dictionary<long, double[]> _patchCache = new Dictionary<long, double[]>(); private long _patchGrain = long.MinValue; private int _patchBudget;
                private readonly List<StarVertex> _stoneMesh = new List<StarVertex>(); private readonly List<double[]> _stones = new List<double[]>(); private StarVertex[] _stoneArr = new StarVertex[0]; private UploadBuffer _stoneBuf;
        /// <summary>Whether the grain's face gets its own level floor far out all round (not when the planet's ground is drawn behind the matter).</summary>
        public bool Floor = true;
        /// <summary>The zoom log (the view's), for the lattice's probe line.</summary>
        public static Action<string> Log; private int _latticeLogTick = -1;
        private readonly List<StarVertex> _patchMesh = new List<StarVertex>(); private long _patchKey; private double _patchGX, _patchGY, _patchGZ, _patchRad;
        private void GrainPatch(List<StarVertex> mesh, double s)
        {
            long gi, gj; double gx, gy, gz, rad; FocusGrain(out gi, out gj, out gx, out gy, out gz, out rad);
            _patchKey = gi; _patchGX = gx; _patchGY = gy; _patchGZ = gz; _patchRad = rad;
            var m = GrainMineral(gi, gj); if (m == null) return;
            if (_patchGrain != gi || _patchCache.Count > 600000) { _patchCache.Clear(); _patchGrain = gi; }
            float dimF = s < AtomSpacing * 40 ? 0.45f : 1f, lusF = s < AtomSpacing * 40 ? 0.3f : m.Lustre;   // (at the nanometres: matte, dimmed, in greys: no glint blowing the face out to white)
            if (Floor)
            {
                double big = 400, y0 = 0, sxp = 0, szp = 0;
                bool near = s < rad * 0.05 && GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz, rad * 0.004, out y0);
                if (near)
                {
                    // close in (within a twentieth of the grain): a sheet in the face's own plane here, a hair under it, far out all
                    // round, in the face's colour and shading; a quad of the face not made yet, or thrown out as a spike, shows the
                    // sheet, never a black hole with the stars through it
                    double d = Math.Max(2.4 * s, rad * 0.008), ya, yb;
                    if (GrainTop(gi, gj, gx, gy, gz, rad, _fx + d, _fz, rad * 0.004, out ya) && GrainTop(gi, gj, gx, gy, gz, rad, _fx - d, _fz, rad * 0.004, out yb)) sxp = (ya - yb) / (2 * d);
                    if (GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz + d, rad * 0.004, out ya) && GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz - d, rad * 0.004, out yb)) szp = (ya - yb) / (2 * d);
                    double sink0 = Structured(m) ? AtomSpacing * 3 * Fade(s, AtomSpacing * 40, AtomSpacing * 22) : 0, under = Math.Max(AtomSpacing * 0.4, s * 0.004);
                    float n1 = PackNormal(-sxp, 1, -szp), fr1 = m.R * FocusShade * dimF, fg1 = m.G * FocusShade * dimF, fb1 = m.B * FocusShade * dimF;
                    Func<double, double, StarVertex> sv = (x, z) => new StarVertex { X = (float)(x + _ex), Y = (float)((y0 + sxp * x * s + szp * z * s - sink0 - under + _yOff - _fy) / s + _ey), Z = (float)(z + _ez), Size = n1, R = fr1, G = fg1, B = fb1, A = lusF };
                    _patchMesh.Add(sv(-big, -big)); _patchMesh.Add(sv(big, -big)); _patchMesh.Add(sv(big, big)); _patchMesh.Add(sv(-big, -big)); _patchMesh.Add(sv(big, big)); _patchMesh.Add(sv(-big, big));
                }
                else
                {
                    // farther out: a level floor of the face's own colour under its rings, far out all round, at the bed the grain lies
                    // on (never at its top, which buried the grains round it to their middles): never a hole or the dark past its edge
                    float fr = m.R * FocusShade * 0.85f * dimF, fg = m.G * FocusShade * 0.85f * dimF, fb = m.B * FocusShade * 0.85f * dimF, n0 = PackNormal(0, 1, 0);
                    double fy = (Math.Max(gy - rad * 0.9, -_yOff - 6 * s) + _yOff - _fy) / s + _ey;   // (deep in, just under the face: a backdrop past the rings, never black)
                    Func<double, double, StarVertex> fv = (x, z) => new StarVertex { X = (float)(x + _ex), Y = (float)fy, Z = (float)(z + _ez), Size = n0, R = fr, G = fg, B = fb, A = lusF };
                    _patchMesh.Add(fv(-big, -big)); _patchMesh.Add(fv(big, -big)); _patchMesh.Add(fv(big, big)); _patchMesh.Add(fv(-big, -big)); _patchMesh.Add(fv(big, big)); _patchMesh.Add(fv(-big, big));
                }
            }
            // the grain's face as rings of detail round you: the finest grid under you, then rings each four times coarser out
            // to the whole grain, each on a grid fixed on the grain (never on the camera, so nothing slides as you zoom and
            // move), each point worked out once; the finest eases between its two sizes so nothing pops
            const int N0 = 40; double want = 4.8 * s / N0, lv = Math.Max(0, Math.Log(want / AtomSpacing, 2)), L = Math.Floor(lv), frac = lv - L;   // never finer than the atoms (they are the face from there)
            _patchBudget = 36000;                                                          // new points a frame at most: the rest come in over the next frames, the frame never stalls
            double inner = 0;
            for (int ring = 0; ring < 12; ring++)
            {
                double Lr = L + ring * 2, step = AtomSpacing * Math.Pow(2, Lr);
                double half = (ring == 0 ? 2.4 * s : inner * 4) * 1.02;
                int N = (int)Math.Min(160, Math.Ceiling(2 * half / step / 2) * 2);
                PatchRing(mesh, s, m, gi, gj, gx, gy, gz, rad, Lr, step, N, ring == 0 ? frac : 0, ring == 0 ? 0 : inner * 0.98);
                inner = N / 2 * step;
                if (inner > rad * 1.3 || ring >= 6) break;                                // the grain round you as far as can be seen (never the whole of it at nanometres: it cannot show)
            }
        }
        // whether a point (in the view's units) is in view, with a margin of its own size: what is not is neither worked out nor drawn
        private bool InView(double x, double y, double z, double margin)
        {
            if (!_haveVp) return true; var m = _lastVp;
            double cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw < -margin) return false;
            double lim = Math.Max(0.05, cw) * 1.15 + margin * 2;
            return Math.Abs(cx) <= lim && Math.Abs(cy) <= lim;
        }
        // one ring: a grid of N x N steps round the point you dive into, its quads inside 'hole' (the finer ring's reach) left out
        private void PatchRing(List<StarVertex> mesh, double s, Mineral m, long gi, long gj, double gx, double gy, double gz, double rad, double L, double step, int N, double frac, double hole)
        {
            long ia0 = (long)Math.Round(_fx / step) - N / 2, ib0 = (long)Math.Round(_fz / step) - N / 2;
            Func<int, double> atA = q => (ia0 + q) * step - _fx; Func<int, double> atB = q => (ib0 + q) * step - _fz;
            var hy = new double[N + 1, N + 1]; var ok = new bool[N + 1, N + 1]; var st = new float[N + 1, N + 1, 3];
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    long key = ((long)L * 1000003L + (ia0 + a)) * 2000003L + (ib0 + b); double[] c;   // each point fixed on the grain: worked out once
                    if (!_patchCache.TryGetValue(key, out c) && !InView(atA(a) / s + _ex, _ey, atB(b) / s + _ez, step / s * 2 + 1.5)) { hy[a, b] = 0; ok[a, b] = false; continue; }   // (out of view, with a view and a half to spare for the face's own height: not worked out)
                    if (c == null)
                    {
                        if (_patchBudget-- <= 0) { hy[a, b] = 0; ok[a, b] = false; continue; }      // not worked out yet: next frame
                        double y; bool o = GrainTop(gi, gj, gx, gy, gz, rad, _fx + atA(a), _fz + atB(b), rad * 0.004, out y);   // every ring the same coarse shape (so no ring shows its edge); all finer detail per pixel on the card, the same everywhere   // the coarse shape here; the fine detail is the card's
                        float sr = 1, sg = 1, sb2 = 1;                                     // (its stains too: worked out per pixel on the card)
                        c = new[] { y, o ? 1 : 0, sr, sg, sb2 }; _patchCache[key] = c;
                    }
                    hy[a, b] = c[0]; ok[a, b] = c[1] > 0; st[a, b, 0] = (float)c[2]; st[a, b, 1] = (float)c[3]; st[a, b, 2] = (float)c[4];
                }
            for (int a = 1; a < N; a++)
                for (int b = 1; b < N; b++)
                {
                    // a point standing off its worked-out neighbours by far more than a terrace's step is a spike (a pillar or a pit
                    // on the face): thrown out, and the sheet under the face shows there instead
                    if (!ok[a, b]) continue; double sum = 0; int cnt = 0;
                    if (ok[a - 1, b]) { sum += hy[a - 1, b]; cnt++; } if (ok[a + 1, b]) { sum += hy[a + 1, b]; cnt++; } if (ok[a, b - 1]) { sum += hy[a, b - 1]; cnt++; } if (ok[a, b + 1]) { sum += hy[a, b + 1]; cnt++; }
                    if (cnt >= 2 && Math.Abs(hy[a, b] - sum / cnt) > step * 1.2 + AtomSpacing) ok[a, b] = false;   // (a step off its neighbours is a spike: a black wall across the face otherwise)
                }
            // never a hole with the stars through it: a point not worked out yet, out of view, or thrown out as a spike takes its
            // neighbours' height for the drawing (not for the spike test), so every quad in view is drawn
            var okd = (bool[,])ok.Clone();
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    if (ok[a, b]) continue; double sum = 0; int cnt = 0;
                    if (a > 0 && ok[a - 1, b]) { sum += hy[a - 1, b]; cnt++; } if (a < N && ok[a + 1, b]) { sum += hy[a + 1, b]; cnt++; } if (b > 0 && ok[a, b - 1]) { sum += hy[a, b - 1]; cnt++; } if (b < N && ok[a, b + 1]) { sum += hy[a, b + 1]; cnt++; }
                    if (cnt > 0) { hy[a, b] = sum / cnt; okd[a, b] = true; }
                }
            if (frac > 0)
            {
                // the points the next coarser grid does not have ease toward its shape as this size gives way to it
                var hm = (double[,])hy.Clone(); double ease = frac * frac * (3 - 2 * frac);
                for (int a = 1; a < N; a++)
                    for (int b = 1; b < N; b++)
                    {
                        bool oa = ((ia0 + a) & 1) != 0, ob = ((ib0 + b) & 1) != 0; if (!oa && !ob || !ok[a, b]) continue;
                        // only from neighbours that are worked out: one not yet (or out of view) stands at zero, and easing toward it
                        // pulled a point of the face up into a wall (a fence of grey pillars at the edge of what was made)
                        if (oa && ob ? !(ok[a - 1, b - 1] && ok[a + 1, b - 1] && ok[a - 1, b + 1] && ok[a + 1, b + 1]) : oa ? !(ok[a - 1, b] && ok[a + 1, b]) : !(ok[a, b - 1] && ok[a, b + 1])) continue;
                        double c = oa && ob ? (hy[a - 1, b - 1] + hy[a + 1, b - 1] + hy[a - 1, b + 1] + hy[a + 1, b + 1]) * 0.25 : oa ? (hy[a - 1, b] + hy[a + 1, b]) * 0.5 : (hy[a, b - 1] + hy[a, b + 1]) * 0.5;
                        hm[a, b] = hy[a, b] + (c - hy[a, b]) * ease;
                    }
                hy = hm;
            }
            float sh = FocusShade * (s < AtomSpacing * 40 ? 0.45f : 1f), lusR = s < AtomSpacing * 40 ? 0.3f : m.Lustre;   // (at the nanometres: matte, dimmed: no glint blowing the face out to white)
            double sink = Structured(m) ? AtomSpacing * 3 * Fade(s, AtomSpacing * 40, AtomSpacing * 22) : 0;   // under the atoms once they show
            var nrm = new float[N + 1, N + 1]; var occ = new float[N + 1, N + 1];
            mesh = _patchMesh;                                                           // drawn on its own, with the card working out its detail
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    // its slope and hollow from the neighbours that are worked out (a missing one stands at zero: never from it)
                    int a0 = a > 0 && ok[a - 1, b] ? a - 1 : a, a1 = a < N && ok[a + 1, b] ? a + 1 : a, b0 = b > 0 && ok[a, b - 1] ? b - 1 : b, b1 = b < N && ok[a, b + 1] ? b + 1 : b;
                    double dx = atA(a1) - atA(a0), dz = atB(b1) - atB(b0), sx = a1 > a0 ? (hy[a1, b] - hy[a0, b]) / Math.Max(1e-30, dx) : 0, sz = b1 > b0 ? (hy[a, b1] - hy[a, b0]) / Math.Max(1e-30, dz) : 0;
                    nrm[a, b] = PackNormal(-sx, 1, -sz);                                      // each corner's surface direction: shaded smooth
                    bool four = a1 > a0 && b1 > b0 && a1 - a0 == 2 && b1 - b0 == 2;
                    double hollow = four ? (hy[a0, b] + hy[a1, b] + hy[a, b0] + hy[a, b1]) * 0.25 - hy[a, b] : 0, span = Math.Max(1e-30, (dx + dz) * 0.5);
                    occ[a, b] = (float)Math.Max(0.55, Math.Min(1.08, 1 - hollow / span * 3));   // a hollow sees less of the sky
                }
            float r = m.R * sh, g = m.G * sh, bl = m.B * sh;
            Func<int, int, StarVertex> v = (aa, bb) => new StarVertex { X = (float)(atA(aa) / s + _ex), Y = (float)((hy[aa, bb] - sink + _yOff - _fy) / s + _ey), Z = (float)(atB(bb) / s + _ez), Size = nrm[aa, bb], R = r * occ[aa, bb] * st[aa, bb, 0], G = g * occ[aa, bb] * st[aa, bb, 1], B = bl * occ[aa, bb] * st[aa, bb, 2], A = lusR };
            for (int a = 0; a < N; a++)
                for (int b = 0; b < N; b++)
                {
                    if (!okd[a, b] || !okd[a + 1, b] || !okd[a, b + 1] || !okd[a + 1, b + 1]) continue;
                    if (!InView((atA(a) + step * 0.5) / s + _ex, (hy[a, b] - sink + _yOff - _fy) / s + _ey, (atB(b) + step * 0.5) / s + _ez, step / s * 1.5)) continue;   // only what is in view is drawn
                    // each ring round, never a square: the finer ring inside its hole, its own edge a circle, the grain's body beyond
                    double r00 = Math.Sqrt(atA(a) * atA(a) + atB(b) * atB(b)), r11 = Math.Sqrt(atA(a + 1) * atA(a + 1) + atB(b + 1) * atB(b + 1)), r10 = Math.Sqrt(atA(a + 1) * atA(a + 1) + atB(b) * atB(b)), r01 = Math.Sqrt(atA(a) * atA(a) + atB(b + 1) * atB(b + 1));
                    double rmax = Math.Max(Math.Max(r00, r11), Math.Max(r10, r01)), edge = N / 2 * step * 0.98;
                    if (hole > 0 && rmax < hole) continue;
                    if (s >= FocusRad * 0.05 && Math.Min(Math.Min(r00, r11), Math.Min(r10, r01)) > edge) continue;   // (closer in, with no body to blend into, the rings fill the view)
                    mesh.Add(v(a, b)); mesh.Add(v(a + 1, b)); mesh.Add(v(a + 1, b + 1)); mesh.Add(v(a, b)); mesh.Add(v(a + 1, b + 1)); mesh.Add(v(a, b + 1));
                }
        }

    }
}
