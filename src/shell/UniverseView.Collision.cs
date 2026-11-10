// Universe · shell · the universe view, its part: the collision (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── galaxies colliding, worked out only where you are (render by scale): the galaxy you are near and its nearest neighbour
        // within thirty of its widths fall together by their pulls from where they stand (each a softened point mass, its mass from its
        // size and its dark matter), a little off line by their seeds; their stars are test stars, each first on its circle in its own
        // galaxy's pull, stepped through the two moving pulls (Toomre and Toomre 1972), so the tidal tails, the bridge between them and
        // the merger come of the physics; drawn from outside them, a galaxy's own far stars (2,500 each) ──
        private sealed class Collide
        {
            public int A = -1, B = -1; public double T; public double[] Cx = new double[2], Cy = new double[2], Cz = new double[2], Vx = new double[2], Vy = new double[2], Vz = new double[2], Gm = new double[2], Eps = new double[2];
            public double[] X, Y, Z, PVx, PVy, PVz; public int[] Of; public StarVertex[] Base, Drawn; public UploadBuffer Buf;
        }
        private Collide _col;
        private double GU { get { double au = Units.Au(1); return 4 * Math.PI * Math.PI * au * au * au; } }   // (G in universe units cubed per solar mass per year squared)
        private double GalaxyMassSol(Galaxy g) { double k = g.Scale / (5e4 / Units.LightYearsPerUnit); return 1e11 / Math.Max(0.05, 1 - _darkMatter) * Math.Max(0.01, k * k); }
        // which pair, if any: the one you are near and its nearest within thirty of its widths
        private void ChooseCollision()
        {
            if (_near < 0 || _near >= _galaxies.Count) { DropCollision(); return; }
            var a = _galaxies[_near]; int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                if (i == _near || _galaxies[i].FarPts == null) continue; var b = _galaxies[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / Math.Max(1e-30, a.Scale + b.Scale);
                if (d < 15 && d < bd) { bd = d; best = i; }
            }
            if (best < 0 || a.FarPts == null) { DropCollision(); return; }
            if (_col != null && ((_col.A == _near && _col.B == best) || (_col.A == best && _col.B == _near))) return;
            DropCollision(); StartCollision(_near, best);
        }
        private void DropCollision() { if (_col != null && _col.Buf != null) _col.Buf.Dispose(); _col = null; }
        private void StartCollision(int ia, int ib)
        {
            var c = new Collide { A = ia, B = ib, T = 0 }; var gs = new[] { _galaxies[ia], _galaxies[ib] }; double G = GU;
            for (int k = 0; k < 2; k++) { c.Cx[k] = gs[k].X; c.Cy[k] = gs[k].Y; c.Cz[k] = gs[k].Z; c.Gm[k] = G * GalaxyMassSol(gs[k]); c.Eps[k] = 0.3 * gs[k].Scale; }
            {
                // from rest against each other, a little off line: a sideways speed a tenth of the pair's circular speed at that distance, by their seeds
                double dx = c.Cx[1] - c.Cx[0], dy = c.Cy[1] - c.Cy[0], dz = c.Cz[1] - c.Cz[0], d = Math.Sqrt(dx * dx + dy * dy + dz * dz), vc = Math.Sqrt((c.Gm[0] + c.Gm[1]) / Math.Max(1e-30, d)) * 0.1;
                double px = -dz, py = (gs[0].Seed % 7) / 7.0 - 0.5, pz = dx, pl = Math.Sqrt(px * px + py * py + pz * pz); px /= pl; py /= pl; pz /= pl;
                double m0 = c.Gm[0] / (c.Gm[0] + c.Gm[1]), m1 = 1 - m0;
                c.Vx[0] = -px * vc * m1; c.Vy[0] = -py * vc * m1; c.Vz[0] = -pz * vc * m1; c.Vx[1] = px * vc * m0; c.Vy[1] = py * vc * m0; c.Vz[1] = pz * vc * m0;
            }
            int n0 = gs[0].FarPts.Length, n1 = gs[1].FarPts.Length, n = n0 + n1; c.X = new double[n]; c.Y = new double[n]; c.Z = new double[n]; c.PVx = new double[n]; c.PVy = new double[n]; c.PVz = new double[n]; c.Of = new int[n]; c.Base = new StarVertex[n]; c.Drawn = new StarVertex[n];
            int q = 0;
            for (int k = 0; k < 2; k++)
            {
                var g = gs[k]; var pg = Placement(g);
                foreach (var s in g.FarPts)
                {
                    double ux = s.X * pg.M11 + s.Y * pg.M21 + s.Z * pg.M31 + g.X, uy = s.X * pg.M12 + s.Y * pg.M22 + s.Z * pg.M32 + g.Y, uz = s.X * pg.M13 + s.Y * pg.M23 + s.Z * pg.M33 + g.Z;
                    // its circle in its own galaxy's pull: the circular speed of a softened point mass at its distance, along its turn in the disc
                    double rx = ux - g.X, ry = uy - g.Y, rz = uz - g.Z, r2 = rx * rx + ry * ry + rz * rz, vcirc = Math.Sqrt(c.Gm[k] * r2 / Math.Pow(r2 + c.Eps[k] * c.Eps[k], 1.5));
                    double lr = Math.Sqrt((double)s.X * s.X + (double)s.Z * s.Z), tx = lr > 1e-9 ? -s.Z / lr : 0, tz = lr > 1e-9 ? s.X / lr : 0;
                    double dxu = tx * pg.M11 + tz * pg.M31, dyu = tx * pg.M12 + tz * pg.M32, dzu = tx * pg.M13 + tz * pg.M33, dl = Math.Sqrt(dxu * dxu + dyu * dyu + dzu * dzu);
                    if (dl > 0) { dxu /= dl; dyu /= dl; dzu /= dl; }
                    c.X[q] = ux; c.Y[q] = uy; c.Z[q] = uz; c.PVx[q] = dxu * vcirc + c.Vx[k]; c.PVy[q] = dyu * vcirc + c.Vy[k]; c.PVz[q] = dzu * vcirc + c.Vz[k]; c.Of[q] = k; c.Base[q] = s; q++;
                }
            }
            c.Buf = new UploadBuffer(_gpu.Device, (long)n * Stride); _col = c;
        }
        // carried on to the game's time: leapfrog steps of at most two million years (a galaxy turns in a couple of hundred), at most forty a frame
        private void StepCollision()
        {
            var c = _col; if (c == null) return; double t = Math.Max(0, _simYears); if (t < c.T) { int a = c.A, b = c.B; DropCollision(); StartCollision(a, b); c = _col; }
            double span = t - c.T; if (span <= 0) return; int steps = (int)Math.Min(40, Math.Ceiling(span / 2e6)); double dt = span / Math.Max(1, steps);
            for (int s = 0; s < steps; s++)
            {
                // the two galaxies on each other (kick, drift, kick)
                double dx = c.Cx[1] - c.Cx[0], dy = c.Cy[1] - c.Cy[0], dz = c.Cz[1] - c.Cz[0], e2 = c.Eps[0] * c.Eps[0] + c.Eps[1] * c.Eps[1], r2 = dx * dx + dy * dy + dz * dz + e2, inv = 1 / (r2 * Math.Sqrt(r2));
                c.Vx[0] += dx * c.Gm[1] * inv * dt; c.Vy[0] += dy * c.Gm[1] * inv * dt; c.Vz[0] += dz * c.Gm[1] * inv * dt; c.Vx[1] -= dx * c.Gm[0] * inv * dt; c.Vy[1] -= dy * c.Gm[0] * inv * dt; c.Vz[1] -= dz * c.Gm[0] * inv * dt;
                for (int k = 0; k < 2; k++) { c.Cx[k] += c.Vx[k] * dt; c.Cy[k] += c.Vy[k] * dt; c.Cz[k] += c.Vz[k] * dt; }
                // each star in both pulls
                for (int i = 0; i < c.X.Length; i++)
                {
                    double ax = 0, ay = 0, az = 0;
                    for (int k = 0; k < 2; k++) { double ex = c.Cx[k] - c.X[i], ey = c.Cy[k] - c.Y[i], ez = c.Cz[k] - c.Z[i], q2 = ex * ex + ey * ey + ez * ez + c.Eps[k] * c.Eps[k], f = c.Gm[k] / (q2 * Math.Sqrt(q2)); ax += ex * f; ay += ey * f; az += ez * f; }
                    c.PVx[i] += ax * dt; c.PVy[i] += ay * dt; c.PVz[i] += az * dt; c.X[i] += c.PVx[i] * dt; c.Y[i] += c.PVy[i] * dt; c.Z[i] += c.PVz[i] * dt;
                }
            }
            c.T = t;
        }
        /// <summary>True when galaxy i is drawn by the collision instead (you outside both).</summary>
        // drawn by the collision only once the two are within two of their widths together, where their pulls on each other's stars show
        // (the tails, the bridge); further apart each is drawn whole as it is, its own hundred thousand points, never its 2,500 test stars
        private bool Close()
        {
            var c = _col; if (c == null || c.A < 0 || c.B < 0 || c.A >= _galaxies.Count || c.B >= _galaxies.Count) return false;
            double dx = c.Cx[1] - c.Cx[0], dy = c.Cy[1] - c.Cy[0], dz = c.Cz[1] - c.Cz[0];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) < 2 * (_galaxies[c.A].Scale + _galaxies[c.B].Scale);
        }
        private bool CollisionDraws(int i) { return _col != null && (i == _col.A || i == _col.B) && InsideShare() < 0.5 && _col.T > 0 && Close(); }
        private void DrawCollision(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            var c = _col; if (c == null || InsideShare() >= 0.5 || c.T <= 0 || !Close()) return;
            for (int i = 0; i < c.X.Length; i++) { var v = c.Base[i]; v.X = (float)((c.X[i] - _ax) - _ox); v.Y = (float)((c.Y[i] - _ay) - _oy); v.Z = (float)((c.Z[i] - _az) - _oz); c.Drawn[i] = v; }
            c.Buf.Write(c.Drawn, c.Drawn.Length);
            var g = _galaxies[c.A]; double gd = Math.Max(1e-9, Math.Sqrt((g.X - _cx) * (g.X - _cx) + (g.Y - _cy) * (g.Y - _cy) + (g.Z - _cz) * (g.Z - _cz))), gpx = g.Scale / gd * h / (2 * Math.Tan(0.45)), dotPx = Math.Max(1.5, 0.0016 * h);
            float crowd = (float)Math.Max(0.015, Math.Min(1, Math.PI * gpx * gpx * 2 / (c.X.Length * dotPx * dotPx) * 0.35));   // (its light as a galaxy's, never more than it has)
            DrawPoints(c.Buf, c.X.Length, vp, right, up, 0.006f * g.Scale, 0.0012f, w, h, time, 0.012f, crowd, 0f);
        }
        // the card: falling together, how far apart now, how fast, and whether they have met
        private void CollisionLines(List<string> L, int i)
        {
            var c = _col; if (c == null || (i != c.A && i != c.B)) return; int other = i == c.A ? c.B : c.A; if (other < 0 || other >= _galaxies.Count) return;
            double dx = c.Cx[1] - c.Cx[0], dy = c.Cy[1] - c.Cy[0], dz = c.Cz[1] - c.Cz[0], d = Math.Sqrt(dx * dx + dy * dy + dz * dz), vr = ((c.Vx[1] - c.Vx[0]) * dx + (c.Vy[1] - c.Vy[0]) * dy + (c.Vz[1] - c.Vz[0]) * dz) / Math.Max(1e-30, d);
            double kms = vr * Units.LightYearsPerUnit * 9.4607e12 / 31557600.0, sumR = _galaxies[c.A].Scale + _galaxies[c.B].Scale;
            L.Add((d < sumR ? "merging with " : "falling toward ") + _galaxies[other].Name + ": " + Units.Describe(d) + " apart, " + (kms < 0 ? "closing at " + (-kms).ToString("N0") + " km/s" : "drawing apart at " + kms.ToString("N0") + " km/s") + " (their pulls on each other)");
        }
    }
}
