// Universe Â· shell Â· the matter view, its part: crystals (one class in several files: src/shell/MatterView.cs is its start)
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
        // ── real crystal structures, where they are in: rock salt (halite) and the silica network (quartz) ──
        private sealed class CAtom { public Element E; public double X, Y, Z; }
        // a crystal from its data (content/minerals.unt): its unit cell (edges in angstroms, angles in degrees) and every atom
        // site in it (fractional positions; a site shared by several kinds of atom holds each as often as the data says)
        private sealed class Lattice
        {
            public double[] A, B, C;                 // the cell's edges in km, world x, height, z (c points up, out of the face)
            public double[][] Pos; public string[][] Els; public double[][] Occ; public double Area;
            public static Lattice From(ContentEntry e)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
                var c = e.Get("cell", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); var sites = e.All("site");
                if (c.Length != 6 || sites.Count == 0) return null;
                var v = new double[6]; for (int q = 0; q < 6; q++) if (!double.TryParse(c[q], fs, ci, out v[q])) return null;
                double a = v[0] * 1e-13, b = v[1] * 1e-13, cc = v[2] * 1e-13, al = v[3] * Math.PI / 180, be = v[4] * Math.PI / 180, ga = v[5] * Math.PI / 180;
                double cx = cc * Math.Cos(be), cy = cc * (Math.Cos(al) - Math.Cos(be) * Math.Cos(ga)) / Math.Sin(ga), cz = Math.Sqrt(Math.Max(0, cc * cc - cx * cx - cy * cy));
                var L = new Lattice { A = new[] { a, 0, 0 }, B = new[] { b * Math.Cos(ga), 0, b * Math.Sin(ga) }, C = new[] { cx, cz, cy } };
                L.Area = Math.Abs(L.A[0] * L.B[2] - L.A[2] * L.B[0]);
                var pos = new List<double[]>(); var els = new List<string[]>(); var occ = new List<double[]>();
                foreach (var line in sites)
                {
                    var w = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length < 4) continue;
                    double x, y, z; if (!double.TryParse(w[0], fs, ci, out x) || !double.TryParse(w[1], fs, ci, out y) || !double.TryParse(w[2], fs, ci, out z)) continue;
                    var el = new List<string>(); var oc = new List<double>();
                    for (int q = 3; q < w.Length; q++)
                    {
                        double o; if (double.TryParse(w[q], fs, ci, out o)) { if (oc.Count > 0) oc[oc.Count - 1] = o; continue; }
                        el.Add(w[q]); oc.Add(1);
                    }
                    if (el.Count == 0) continue;
                    pos.Add(new[] { x, y, z }); els.Add(el.ToArray()); occ.Add(oc.ToArray());
                }
                if (pos.Count == 0) return null;
                L.Pos = pos.ToArray(); L.Els = els.ToArray(); L.Occ = occ.ToArray(); return L;
            }
        }
        // a molecule from the data ("C 0 0 0, H 0.063 0.063 0.063, ..." in nm, from its first atom)
        private static KeyValuePair<string, double[]>[] MoleculeFrom(string t)
        {
            if (string.IsNullOrEmpty(t)) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var l = new List<KeyValuePair<string, double[]>>();
            foreach (var part in t.Split(','))
            {
                var w = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length != 4) continue;
                l.Add(P(w[0], double.Parse(w[1], ci) * 1e-12, double.Parse(w[2], ci) * 1e-12, double.Parse(w[3], ci) * 1e-12));
            }
            return l.Count > 0 ? l.ToArray() : null;
        }
        private bool Structured(Mineral m) { return m != null && m.Lat != null; }
        private string StructureName(Mineral m) { return m == null || m.Structure.Length == 0 ? "" : ", " + m.Structure; }

        // the atoms of a crystal within reach of (cx, cz): its face at height 0, down to 'depth' below it, at most 'budget' atoms
        // (the reach shrinks to fit). Each site's atom from its cell and site (a shared site picks by the data's shares); a
        // molecule's other atoms round each of its first atoms, turned its own way.
        private List<CAtom> Crystal(Mineral m, double cx, double cz, double reach, double depth, int budget)
        {
            var list = new List<CAtom>(); var L = m.Lat; if (L == null) return list;
            int per = L.Pos.Length * (m.Molecule != null ? m.Molecule.Length : 1);
            long nk = (long)Math.Max(1, Math.Ceiling(depth / Math.Max(1e-20, L.C[1])));
            double most = Math.Sqrt(budget * L.Area / Math.Max(1, per * nk)) * 0.5; reach = Math.Min(reach, most);
            // cells covering the square round (cx, cz): solve x = i*A + j*B in the face's plane
            double det = L.A[0] * L.B[2] - L.A[2] * L.B[0];
            for (long k = -1; k >= -nk; k--)
            {
                double ox = cx - k * L.C[0], oz = cz - k * L.C[2];
                double i0 = double.MaxValue, i1 = double.MinValue, j0 = double.MaxValue, j1 = double.MinValue;
                for (int q = 0; q < 4; q++)
                {
                    double px = ox + ((q & 1) == 0 ? -reach : reach), pz = oz + ((q & 2) == 0 ? -reach : reach);
                    double fi = (px * L.B[2] - pz * L.B[0]) / det, fj = (pz * L.A[0] - px * L.A[2]) / det;
                    i0 = Math.Min(i0, fi); i1 = Math.Max(i1, fi); j0 = Math.Min(j0, fj); j1 = Math.Max(j1, fj);
                }
                for (long i = (long)Math.Floor(i0) - 1; i <= (long)Math.Ceiling(i1); i++)
                    for (long j = (long)Math.Floor(j0) - 1; j <= (long)Math.Ceiling(j1); j++)
                        for (int q = 0; q < L.Pos.Length; q++)
                        {
                            var f = L.Pos[q]; double u = i + f[0], w = j + f[1], h = k + f[2];
                            double X = u * L.A[0] + w * L.B[0] + h * L.C[0], Y = h * L.C[1], Z = u * L.A[2] + w * L.B[2] + h * L.C[2];
                            if (Y > 0 || Y < -depth || Math.Abs(X - cx) > reach || Math.Abs(Z - cz) > reach) continue;
                            // which atom is on this site here (a shared site: each as often as the data gives; maybe none)
                            var els = L.Els[q]; var oc = L.Occ[q]; string sym = null; double r = Hash(i * 977 + q, j, k + 90, _seed), acc = 0;
                            for (int t = 0; t < els.Length; t++) { acc += oc[t]; if (r < acc) { sym = els[t]; break; } }
                            if (sym == null) continue;
                            sym = Traced(m, sym, i * 977 + q, j, k);                                   // (its trace elements take the host's place on their share of its sites)
                            var e = El(sym); if (e == null) continue;
                            list.Add(new CAtom { E = e, X = X, Y = Y, Z = Z });
                            if (m.Molecule != null && m.Molecule[0].Key == sym)
                                for (int t = 1; t < m.Molecule.Length; t++)
                                {
                                    var me = El(m.Molecule[t].Key); if (me == null) continue; double dx, dy, dz; Orient(i * 31 + q, j, k, m.Molecule[t].Value, out dx, out dy, out dz);
                                    if (Y + dy <= 0) list.Add(new CAtom { E = me, X = X + dx, Y = Y + dy, Z = Z + dz });
                                }
                        }
            }
            return list;
        }

        // the crystal round you, worked out once and kept while you stay about where you are (not every frame)
        private List<CAtom> _kept; private Mineral _keptM; private double _keptX, _keptZ, _keptReach, _keptDepth;
        private List<CAtom> CrystalKept(Mineral m, double reach, double depth, int budget)
        {
            if (_kept != null && _keptM == m && Math.Abs(_keptDepth - depth) < 1e-20 && reach <= _keptReach && reach > _keptReach * 0.4
                && Math.Abs(_fx - _keptX) < _keptReach * 0.25 && Math.Abs(_fz - _keptZ) < _keptReach * 0.25) return _kept;
            _keptReach = reach * 1.6; _keptX = _fx; _keptZ = _fz; _keptM = m; _keptDepth = depth;
            _kept = Crystal(m, _fx, _fz, _keptReach, depth, budget); return _kept;
        }

        private static KeyValuePair<string, double[]> P(string el, double x, double y, double z) { return new KeyValuePair<string, double[]>(el, new[] { x, y, z }); }

        // turns a molecule's offset by its site's own orientation (from the site's hash)
        private void Orient(long i, long j, long k, double[] v, out double x, out double y, out double z)
        {
            double a = Hash(i, j, k + 40, _seed) * Math.PI * 2, b = Math.Acos(1 - 2 * Hash(i, j, k + 41, _seed)), c = Hash(i, j, k + 42, _seed) * Math.PI * 2;
            double x1 = v[0] * Math.Cos(a) - v[1] * Math.Sin(a), y1 = v[0] * Math.Sin(a) + v[1] * Math.Cos(a), z1 = v[2];
            double y2 = y1 * Math.Cos(b) - z1 * Math.Sin(b), z2 = y1 * Math.Sin(b) + z1 * Math.Cos(b);
            x = x1 * Math.Cos(c) - z2 * Math.Sin(c); z = x1 * Math.Sin(c) + z2 * Math.Cos(c); y = y2;
        }

        // a big count in plain words
        private static string Big(double n)
        {
            string[] names = { "", " thousand", " million", " billion", " trillion", " thousand trillion", " million trillion", " billion trillion" };
            int k = 0; while (n >= 1000 && k < names.Length - 1) { n /= 1000; k++; }
            return n.ToString(n < 10 ? "0.#" : "0") + names[k];
        }

        private static double AtomRadius(Element e)
        {
            if (e == null) return 1e-13;
            if (e.Radius > 0) return e.Radius;
            return (0.7 + 0.5 * Math.Min(1, e.Z / 30.0)) * 1e-13;                  // the rest: a rough size from its number
        }
    }
}
