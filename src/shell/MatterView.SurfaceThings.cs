// Universe Â· shell Â· the matter view, its part: surfacethings (one class in several files: src/shell/MatterView.cs is its start)
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
        // ── what sits on a grain's surface, each at its real size, fixed in place (from its cell), each pickable ──
        // (all of them from content/minerals.unt @surface: what it is made of, its sizes, how common, its shape, what it is)
        private sealed class SurfThing { public string Id, Name; public Mineral M; public double Lo, Hi, Chance, Bond, Angle; public bool Flat, Molecule, FromGround, Life; public double Thick = 0.05, Cell = 4, Aspect = 1; public string[] About; }
        private readonly List<SurfThing> _surf = new List<SurfThing>();
        private double _clock;                                                             // the matter's time, for the electrons of things drawn here
        private void SurfaceThings(List<StarVertex> solid, List<StarVertex> glow, List<StarVertex> mesh, double s)
        {
            long fgi, fgj; double fgx, fgy, fgz, frad; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
            for (int t = 0; t < _surf.Count; t++)
            {
                var st = _surf[t]; if (st.Life && !_lifeHere) continue;                     // living things only on a living world
                if (st.Hi / s < 0.004 || st.Lo / s > 3) continue;                           // only at sizes where it shows
                double cell = st.Hi * st.Cell; long n = (long)Math.Ceiling(2 * s / cell);
                if (n > 20) continue;   // (bounded: forty a side was a million glows of molecules before the crystal's own atoms were reached)
                long ci0 = (long)Math.Floor(_fx / cell), cj0 = (long)Math.Floor(_fz / cell);
                for (long i = ci0 - n; i <= ci0 + n; i++)
                    for (long j = cj0 - n; j <= cj0 + n; j++)
                    {
                        if (Hash(i, j, 100 + t, _seed) > st.Chance) continue;
                        double x = (i + Hash(i, j, 101 + t, _seed)) * cell, z = (j + Hash(i, j, 102 + t, _seed)) * cell, size = st.Lo + (st.Hi - st.Lo) * Hash(i, j, 103 + t, _seed), top;
                        if (!GrainTop(fgi, fgj, fgx, fgy, fgz, frad, x, z, Math.Max(size * 0.5, AtomSpacing), out top)) continue;
                        double y = top + size * 0.5; var tm = ThingMat(st, i, j); if (tm == null) continue;
                        if (st.Flat)
                        {
                            // a plate or skin lies on the face: it rests on the highest point under it, clear of the face, never in it
                            for (int q = 0; q < 6; q++) { double cq, a6 = q * Math.PI / 3; if (GrainTop(fgi, fgj, fgx, fgy, fgz, frad, x + Math.Cos(a6) * size * 0.5, z + Math.Sin(a6) * size * 0.5, Math.Max(size * 0.25, AtomSpacing), out cq)) top = Math.Max(top, cq); }
                            y = top + size * st.Thick * 0.5 + Math.Max(AtomSpacing, s * 0.003);
                        }
                        if (s < size * 0.4 && (x - _fx) * (x - _fx) + (z - _fz) * (z - _fz) < size * size * 0.25) continue;   // you are at it: its own atoms show instead (by where you are)
                        if (st.Molecule && st.M.Atoms.Length >= 3)
                        {
                            // a molecule drawn atom by atom from its mineral's formula: the first atom in the middle, the next two at the
                            // bond length and angle the data gives, each molecule turned its own way
                            var o = El(st.M.Atoms[0]); var hh = El(st.M.Atoms[1]); if (o == null || hh == null) continue;
                            double oh = st.Bond, half = st.Angle / 2 * Math.PI / 180, turn = Hash(i, j, 104, _seed) * Math.PI * 2;
                            double ball = 1 - Fade(s, AtomSpacing * 4, AtomSpacing * 2);               // close in, each atom becomes its own system
                            float cf = (float)Fade(s, AtomSpacing * 4, AtomSpacing * 2);
                            // a molecule away from where you look: each atom its cloud at its size (as the lattice's atoms are drawn), its own system
                            // only near the look (every molecule in reach had been its three atoms' full systems: a million glows before the crystal was reached)
                            if (!NearLook(x, y, z, s) && (x - _fx) * (x - _fx) + (z - _fz) * (z - _fz) > s * s * 0.36)
                            {
                                float cw = (float)Math.Max(ball, cf) * 0.8f; LatticeCloud(glow, o, x, y, z, s, cw);
                                for (int q = -1; q <= 1; q += 2) { double hx = q * oh * Math.Sin(half), hy = oh * Math.Cos(half); LatticeCloud(glow, hh, x + hx * Math.Cos(turn), y + hy * 0.5, z + hx * Math.Sin(turn), s, cw); }
                                continue;
                            }
                            if (ball > 0.03) AtomSystem(glow, o, x, y, z, s, _clock, i * 31 + j, (float)ball);
                            if (cf > 0.01 && NearLook(x, y, z, s)) Cloud(glow, o, x, y, z, s, _clock, i * 31 + j, cf);
                            for (int q = -1; q <= 1; q += 2)
                            {
                                double hx = q * oh * Math.Sin(half), hy = oh * Math.Cos(half), px = x + hx * Math.Cos(turn), py = y + hy * 0.5, pz = z + hx * Math.Sin(turn);
                                if (ball > 0.03) AtomSystem(glow, hh, px, py, pz, s, _clock, i * 31 + j + q + 7, (float)ball);
                                if (cf > 0.01 && NearLook(px, py, pz, s)) Cloud(glow, hh, px, py, pz, s, _clock, i * 31 + j + q + 7, cf);
                            }
                        }
                        else
                        {
                            if (st.Flat) { if (mesh != null) Plate(mesh, st, tm, x, y, z, size, i, j, s); }   // a six-sided plate (clay) or a thin skin (coating), with real thickness
                            else Stone(mesh, solid, tm, x, y, z, size, i, j, s, st.Aspect);
                        }
                        int nb = _bodies.Count; AddBody(8, i, j, x, y, z, size * 0.5, s); if (_bodies.Count > nb) _bodies[nb].Sym = st.Id;
                    }
            }
        }

        private static double Cov(Element e) { return e.Cov > 0 ? e.Cov : 1.5e-13; }

        // a thin six-sided plate lying on the face, as wide as 'size' and as thick as its data gives, turned its own way;
        // its top and bottom shaded smooth, its rim in facets
        private void Plate(List<StarVertex> mesh, SurfThing st, Mineral m, double x, double y, double z, double size, long i, long j, double s)
        {
            double R = size * 0.5, t = size * st.Thick, turn = Hash(i, j, 105, _seed) * Math.PI;
            float sh = (float)(0.85 + 0.25 * Hash(i, j, 106, _seed)), r = m.R * sh, g = m.G * sh, b = m.B * sh, lu = m.Lustre;
            Func<double, double, double, StarVertex> V = (px, py, pz) => new StarVertex { X = (float)((px - _fx) / s + _ex), Y = (float)((py + _yOff - _fy) / s + _ey), Z = (float)((pz - _fz) / s + _ez), R = r, G = g, B = b, A = lu };
            if (Math.Abs((x - _fx) / s) > 60 || Math.Abs((z - _fz) / s) > 60 || R / s < 0.002) return;
            float up = PackNormal(0, 1, 0), down = PackNormal(0, -1, 0);
            var cx = new double[6]; var cz = new double[6];
            for (int q = 0; q < 6; q++) { double a = turn + q * Math.PI / 3; cx[q] = x + Math.Cos(a) * R; cz[q] = z + Math.Sin(a) * R; }
            for (int q = 0; q < 6; q++)
            {
                int n = (q + 1) % 6;
                var c0 = V(x, y + t * 0.5, z); var c1 = V(cx[q], y + t * 0.5, cz[q]); var c2 = V(cx[n], y + t * 0.5, cz[n]); c0.Size = c1.Size = c2.Size = up;
                mesh.Add(c0); mesh.Add(c1); mesh.Add(c2);
                var d0 = V(x, y - t * 0.5, z); var d1 = V(cx[n], y - t * 0.5, cz[n]); var d2 = V(cx[q], y - t * 0.5, cz[q]); d0.Size = d1.Size = d2.Size = down;
                mesh.Add(d0); mesh.Add(d1); mesh.Add(d2);
                var a0 = V(cx[q], y + t * 0.5, cz[q]); var a1 = V(cx[n], y + t * 0.5, cz[n]); var a2 = V(cx[n], y - t * 0.5, cz[n]); var a3 = V(cx[q], y - t * 0.5, cz[q]);
                mesh.Add(a0); mesh.Add(a1); mesh.Add(a2); mesh.Add(a0); mesh.Add(a2); mesh.Add(a3);
            }
        }
        // a small rounded speck (a silt grain, a nanoparticle): a lumpy ball of its own mineral, shaded smooth, sitting on the face
        private void Stone(List<StarVertex> mesh, List<StarVertex> solid, Mineral m, double x, double y, double z, double size, long i, long j, double s, double aspect = 1)
        {
            double turn = Hash(i, j, 109, _seed) * Math.PI, ca = Math.Cos(turn), sa = Math.Sin(turn), wide = 1 / Math.Sqrt(aspect);
            if (mesh == null || _ico == null) { Add(solid, x, y, z, size * 0.5 / 0.75, m.R, m.G, m.B, s); return; }
            double R = size * 0.5; if (R / s < 0.004 || Math.Abs((x - _fx) / s) > 60 || Math.Abs((z - _fz) / s) > 60) return;
            float sh = (float)(0.8 + 0.3 * Hash(i, j, 107, _seed)), r = m.R * sh, g = m.G * sh, b = m.B * sh;
            foreach (int k in _icoTri)
            {
                var d = _ico[k]; double lump = 1 + 0.18 * (Hash(i * 64 + k, j, 108, _seed) - 0.5) * 2 * 0 + 0.12 * Math.Sin(d.X * 3.1 + i) * Math.Cos(d.Z * 2.7 + j);
                double lx = d.X * aspect * wide, lz = d.Z * wide, rx = lx * ca - lz * sa, rz = lx * sa + lz * ca;   // long along its own way (a rod)
                mesh.Add(new StarVertex { X = (float)((x + rx * R * lump - _fx) / s + _ex), Y = (float)((y + d.Y * R * 0.8 * wide * lump + _yOff - _fy) / s + _ey), Z = (float)((z + rz * R * lump - _fz) / s + _ez),
                    Size = PackNormal(d.X, d.Y / 0.8, d.Z), R = r, G = g, B = b, A = m.Lustre });
            }
        }

        // a bond as it really is: not a stick but the two atoms' outer electrons shared between them. A soft cloud of electron
        // density along the line between them, densest between, with the shared pair moving in it; pulled toward the atom that
        // pulls harder (electronegativity, content/elements.unt). Atoms that differ by 1.7 or more share nothing: one has given
        // its electron to the other and they hold as ions (salt), so nothing is drawn between them.
        private void SharedPair(List<StarVertex> glow, CAtom a, CAtom b, double s, double time, int n, float f)
        {
            double ca = a.E.Chi, cb = b.E.Chi, diff = ca > 0 && cb > 0 ? Math.Abs(ca - cb) : 0;
            if (diff >= 1.7) return;                                                    // ionic: no shared electrons
            double ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, len = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (len < 1e-20) return;
            double toward = ca > 0 && cb > 0 ? (cb > ca ? 1 : -1) * diff / 1.7 * 0.25 : 0, mid = 0.5 + toward;   // nearer the one that pulls harder
            for (int q = 0; q <= 8; q++)
            {
                double t = q / 8.0, bell = Math.Exp(-Math.Pow((t - mid) / 0.28, 2));
                Add(glow, a.X + ex * t, a.Y + ey * t, a.Z + ez * t, len * (0.1 + 0.18 * bell), 0.07f * f * (float)bell, 0.055f * f * (float)bell, 0.025f * f * (float)bell, s);
            }
            // the shared pair: two electrons on opposite sides, circling the line between the atoms and sliding along it
            double ux = ex / len, uy = ey / len, uz = ez / len, px = Math.Abs(uy) < 0.9 ? -uz : 1, py = Math.Abs(uy) < 0.9 ? 0 : 0, pz = Math.Abs(uy) < 0.9 ? ux : 0;
            double pl = Math.Sqrt(px * px + py * py + pz * pz); px /= pl; py /= pl; pz /= pl;
            double qx = uy * pz - uz * py, qy = uz * px - ux * pz, qz = ux * py - uy * px;
            double ph = time * 3.1 + n * 1.7, slide = mid + 0.14 * Math.Sin(time * 1.9 + n), r = len * 0.13, dot = s * 0.0035;
            for (int k = 0; k < 2; k++)
            {
                double ang = ph + k * Math.PI, cx = a.X + ex * slide, cy = a.Y + ey * slide, cz = a.Z + ez * slide;
                Add(glow, cx + (px * Math.Cos(ang) + qx * Math.Sin(ang)) * r, cy + (py * Math.Cos(ang) + qy * Math.Sin(ang)) * r, cz + (pz * Math.Cos(ang) + qz * Math.Sin(ang)) * r, dot, 1f * f, 0.8f * f, 0.25f * f, s);
            }
        }

        // the bonds between the atoms drawn, each as what it is: the two atoms' outer electrons shared between them
        private void Bonds(List<StarVertex> glow, List<CAtom> atoms, double s, double time, float f)
        {
            if (f <= 0.02 || atoms.Count < 2) return;
            double cell = 3.2e-13; var grid = new Dictionary<long, List<int>>();
            Func<double, long> c = v => (long)Math.Floor(v / cell);
            for (int i = 0; i < atoms.Count; i++) { long key = (c(atoms[i].X) * 73856093L) ^ (c(atoms[i].Y) * 19349663L) ^ (c(atoms[i].Z) * 83492791L); List<int> l; if (!grid.TryGetValue(key, out l)) grid[key] = l = new List<int>(); l.Add(i); }
            int bonds = 0;
            for (int i = 0; i < atoms.Count && bonds < 4000; i++)
            {
                var a = atoms[i]; long cx = c(a.X), cy = c(a.Y), cz = c(a.Z);
                for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) for (long dz = -1; dz <= 1; dz++)
                {
                    List<int> l; if (!grid.TryGetValue(((cx + dx) * 73856093L) ^ ((cy + dy) * 19349663L) ^ ((cz + dz) * 83492791L), out l)) continue;
                    foreach (int j in l)
                    {
                        if (j <= i) continue; var b = atoms[j];
                        double ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, d = Math.Sqrt(ex * ex + ey * ey + ez * ez), lim = (Cov(a.E) + Cov(b.E)) * 1.2;
                        if (d < 1e-16 || d > lim) continue;
                        bonds++;
                        SharedPair(glow, a, b, s, time, bonds, f);
                    }
                }
            }
        }

        // a surface direction packed into a vertex's glow (below -1), so the mesh is shaded smooth, not in facets
        private static float PackNormal(double x, double y, double z)
        {
            double l = Math.Abs(x) + Math.Abs(y) + Math.Abs(z); if (l <= 0) return 0; x /= l; y /= l; z /= l;
            double ex = x, ey = z;
            if (y < 0) { double tx = (1 - Math.Abs(z)) * (x >= 0 ? 1 : -1), tz = (1 - Math.Abs(x)) * (z >= 0 ? 1 : -1); ex = tx; ey = tz; }
            int a = (int)Math.Round((ex * 0.5 + 0.5) * 4095), b = (int)Math.Round((ey * 0.5 + 0.5) * 4095);
            return -(2 + (a | (b << 12)));
        }

        private static double Fade(double s, double from, double to) { return Math.Max(0, Math.Min(1, Math.Log(from / s) / Math.Log(from / to))); }

        // the lattice's level: an atom as its electron fields, each subshell a soft field at its own distance (Slater's, the same
        // distances the atom's level puts its electrons at), the inner ones small and dense, the valence shell outermost, faint and
        // in gold, so from the lattice down the atom reads as its shells, the same electron fields as close in, only smaller and
        // sooner; its nucleus a spark at the middle. (One soft glow at the atom's size had stood for it, and read as a ball.)
        private void LatticeCloud(List<StarVertex> glow, Element e, double x, double y, double z, double s, float w)
        {
            if (e == null || w <= 0.02f) return; double ar = AtomRadius(e);
            var els = ElectronsOf(e); int shells = e.Shells.Length, last = -1, count = 0; double rr = 0; bool val = false;
            for (int k = 0; k <= els.Length; k++)
            {
                if (k < els.Length && els[k].Sub == last) { count++; continue; }
                if (last >= 0 && count > 0)
                {
                    double rad = Math.Max(rr * ar, s * 0.004); float dens = (float)Math.Min(1, count / (rr * rr * 18 + 0.5));   // (its electrons over the shell's area: the 1s pair densest, the valence shell thinnest)
                    float bq = w * (0.25f + 0.75f * dens) * 0.55f;
                    Add(glow, x, y, z, rad * 1.15, (val ? 0.95f : 0.5f) * bq, (val ? 0.8f : 0.6f) * bq, (val ? 0.35f : 0.9f) * bq, s);
                }
                if (k < els.Length) { last = els[k].Sub; count = 1; rr = els[k].R; val = els[k].N == shells; }
            }
            Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.002), 1f * w, 0.95f * w, 0.9f * w, s);
        }

        // a grain as a lit, lumpy stone: a once-split icosahedron, squashed and turned its own way, each corner pushed in or out
        private static Vec3[] _ico; private static int[] _icoTri;
        private static void MakeIco()
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            var v = new List<Vec3> { new Vec3(-1, (float)t, 0), new Vec3(1, (float)t, 0), new Vec3(-1, (float)-t, 0), new Vec3(1, (float)-t, 0), new Vec3(0, -1, (float)t), new Vec3(0, 1, (float)t),
                new Vec3(0, -1, (float)-t), new Vec3(0, 1, (float)-t), new Vec3((float)t, 0, -1), new Vec3((float)t, 0, 1), new Vec3((float)-t, 0, -1), new Vec3((float)-t, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var mid = new Dictionary<long, int>(); var tri = new List<int>();
            Func<int, int, int> m = (a, b) => { long key = Math.Min(a, b) * 1000L + Math.Max(a, b); int idx; if (mid.TryGetValue(key, out idx)) return idx; var p = (v[a] + v[b]) * 0.5f; v.Add(p); mid[key] = v.Count - 1; return v.Count - 1; };
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = m(a, b), bc = m(b, c), ca = m(c, a);
                tri.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            _ico = new Vec3[v.Count]; for (int i = 0; i < v.Count; i++) _ico[i] = v[i].Normalized; _icoTri = tri.ToArray();
        }

        private void Stone(List<StarVertex> l, long gi, long gj, double cx, double cy, double cz, double rad, float r, float g, float b, double s)
        {
            if (_ico == null) MakeIco();
            var pts = new double[_ico.Length * 3];
            for (int k = 0; k < _ico.Length; k++)
            {
                double rr = GrainSurface(gi, gj, rad, _ico[k].X, _ico[k].Y, _ico[k].Z, rad * 0.2);   // the same surface as close in, coarse
                pts[k * 3] = (cx + _ico[k].X * rr - _fx) / s + _ex; pts[k * 3 + 1] = (cy + _ico[k].Y * rr + _yOff - _fy) / s + _ey; pts[k * 3 + 2] = (cz + _ico[k].Z * rr - _fz) / s + _ez;
            }
            foreach (int k in _icoTri)
                l.Add(new StarVertex { X = (float)pts[k * 3], Y = (float)pts[k * 3 + 1], Z = (float)pts[k * 3 + 2], Size = PackNormal(_ico[k].X, _ico[k].Y, _ico[k].Z), R = r, G = g, B = b, A = 1 });   // shaded smooth; its stains and fine detail on the card
        }

        // a far grain: eight faces, squashed and turned its own way
        private void Octa(List<StarVertex> l, long gi, long gj, double cx, double cy, double cz, double rad, float r, float g, float b, double s)
        {
            double sx = (0.8 + 0.4 * Universe.Gen.GrainField.Hash(gi, 16, 0, _gseed)) * rad, sy = (0.6 + 0.35 * Universe.Gen.GrainField.Hash(gi, 17, 0, _gseed)) * rad, sz = (0.8 + 0.4 * Universe.Gen.GrainField.Hash(gi, 18, 0, _gseed)) * rad, turn = Universe.Gen.GrainField.Hash(gi, 19, 0, _gseed) * Math.PI * 2;   // the field's own shape
            double ct = Math.Cos(turn), st = Math.Sin(turn);
            double[][] p = { new[] { sx, 0, 0.0 }, new[] { -sx, 0, 0.0 }, new[] { 0, sy, 0.0 }, new[] { 0, -sy, 0.0 }, new[] { 0, 0, sz }, new[] { 0, 0, -sz } };
            int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
            foreach (int k in f)
            {
                double x = p[k][0] * ct - p[k][2] * st, z = p[k][0] * st + p[k][2] * ct;
                l.Add(new StarVertex { X = (float)((cx + x - _fx) / s + _ex), Y = (float)((cy + p[k][1] + _yOff - _fy) / s + _ey), Z = (float)((cz + z - _fz) / s + _ez), Size = 0, R = r, G = g, B = b, A = 1 });
            }
        }

        // every solid point (the grain's face, atoms, protons and neutrons) as a lit shape: a round ball when it is big on screen,
        // an eight-sided piece when small; a face tile (flat on the ground) stays flat
        private void Solidify(List<StarVertex> solid, List<StarVertex> mesh, int h)
        {
            if (_ico == null) MakeIco();
            int balls = 0;
            // nearest first, so the round balls (and the room in the mesh) go to the atoms closest to you
            var order = new int[solid.Count]; var dd = new float[solid.Count];
            for (int q = 0; q < order.Length; q++) { var w = solid[q]; order[q] = q; dd[q] = w.X * w.X + w.Y * w.Y + w.Z * w.Z; }
            Array.Sort(dd, order);
            foreach (int oi in order)
            {
                var v = solid[oi];
                double rad = v.Size * 0.75, dist = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z), px = rad / Math.Max(1e-9, dist) * h / (2 * Math.Tan(0.45));
                if (px < 0.6 || mesh.Count > 1500000) continue;
                if (v.A < 0.9f)
                {
                    // a tile of the grain's face: flat, side by side with its neighbours, so the face is one surface
                    double hw = v.Size / 0.72 * 0.5;
                    var a0 = new StarVertex { X = (float)(v.X - hw), Y = v.Y, Z = (float)(v.Z - hw), R = v.R, G = v.G, B = v.B, A = 1 };
                    var a1 = a0; a1.X = (float)(v.X + hw); var a2 = a0; a2.X = (float)(v.X + hw); a2.Z = (float)(v.Z + hw); var a3 = a0; a3.Z = (float)(v.Z + hw);
                    mesh.Add(a0); mesh.Add(a1); mesh.Add(a2); mesh.Add(a0); mesh.Add(a2); mesh.Add(a3);
                    continue;
                }
                if (px > 6 && balls < 6000)
                {
                    balls++;
                    foreach (int k in _icoTri) mesh.Add(new StarVertex { X = (float)(v.X + _ico[k].X * rad), Y = (float)(v.Y + _ico[k].Y * rad), Z = (float)(v.Z + _ico[k].Z * rad), Size = PackNormal(_ico[k].X, _ico[k].Y, _ico[k].Z), R = v.R, G = v.G, B = v.B, A = 1 });
                }
                else
                {
                    double[][] p = { new[] { rad, 0, 0.0 }, new[] { -rad, 0, 0.0 }, new[] { 0, rad, 0.0 }, new[] { 0, -rad, 0.0 }, new[] { 0, 0, rad }, new[] { 0, 0, -rad } };
                    int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
                    foreach (int k in f) mesh.Add(new StarVertex { X = (float)(v.X + p[k][0]), Y = (float)(v.Y + p[k][1]), Z = (float)(v.Z + p[k][2]), Size = PackNormal(p[k][0], p[k][1], p[k][2]), R = v.R, G = v.G, B = v.B, A = 1 });
                }
            }
        }

        /// <summary>The starlight's way in the ground's own frame (x east, y up, z north), for lighting the grains.</summary>
        public float[] Light = { 0.4f, 0.8f, 0.3f };

    }
}
