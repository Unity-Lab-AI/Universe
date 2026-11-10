// Universe Â· shell Â· the matter view, its part: electrons (one class in several files: src/shell/MatterView.cs is its start)
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
        // ── each element's own electrons, as its real make-up gives them: every electron in its subshell (1s 2s 2p 3s ...,
        // filled in the Madelung order), each orbital holding two of opposite spin (filled one to each first, Hund's rule); how
        // far out each subshell sits from the charge its electrons feel (Slater's rules: the nucleus less the screening of the
        // electrons inside), its mean distance n^2 a0 / Zeff, so heavy atoms pull their inner shells in tight; and how fast each
        // goes round (as the charge it feels and its shell: inner electrons of a heavy atom far faster); each moves on a path in
        // its orbital's own shape (s round, p through its two lobes, d round its four leaves) ──
        private sealed class Electron { public int N, L, M, Spin, Sub; public double R, W; }
        private readonly Dictionary<int, Electron[]> _electrons = new Dictionary<int, Electron[]>();
        private Electron[] ElectronsOf(Element e)
        {
            Electron[] r; if (_electrons.TryGetValue(e.Z, out r)) return r;
            var subs = Subshells(e.Z); var list = new List<Electron>(); double rmax = 0;
            var rs = new double[subs.Count]; var ws = new double[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                int n = subs[i][0], l = subs[i][1];
                // Slater's groups: (1s)(2s 2p)(3s 3p)(3d)(4s 4p)(4d)(4f)(5s 5p)...: the same group screens 0.35 each (1s 0.30); for an
                // s or p electron the shell below screens 0.85 and all deeper 1.00; for a d or f electron everything inside 1.00
                double screen = 0;
                for (int j = 0; j < subs.Count; j++)
                {
                    int n2 = subs[j][0], l2 = subs[j][1], c = subs[j][2] - (j == i ? 1 : 0); if (c <= 0) continue;
                    bool sameGroup = l <= 1 ? (n2 == n && l2 <= 1) : (n2 == n && l2 == l);
                    bool inside = l <= 1 ? (n2 < n || (n2 == n && l2 > 1 && false)) : (n2 < n || (n2 == n && l2 < l));
                    if (sameGroup) screen += c * (n == 1 ? 0.30 : 0.35);
                    else if (l <= 1 && n2 == n - 1) screen += c * 0.85;
                    else if (l <= 1 && n2 < n - 1) screen += c * 1.00;
                    else if (l > 1 && inside) screen += c * 1.00;
                }
                double zeff = Math.Max(1, e.Z - screen);
                rs[i] = n * n / zeff; ws[i] = zeff * zeff / (n * n * n); rmax = Math.Max(rmax, rs[i]);
            }
            double wOut = 0; for (int i = 0; i < subs.Count; i++) if (rs[i] == rmax) wOut = ws[i];
            for (int i = 0; i < subs.Count; i++)
            {
                int n = subs[i][0], l = subs[i][1], count = subs[i][2], orbs = 2 * l + 1;
                for (int k = 0; k < count; k++)
                    list.Add(new Electron { N = n, L = l, M = k % orbs, Spin = k / orbs, Sub = i, R = rs[i] / rmax, W = Math.Min(8, ws[i] / Math.Max(1e-9, wOut)) });
            }
            r = list.ToArray(); _electrons[e.Z] = r; return r;
        }
        // where an electron is on its path (as a share of the atom's size, from its nucleus) at phase a, its orbital's shape
        // turned its own way for this atom (key)
        private void ElectronAt(Electron el, long key, double a, out double x, out double y, out double z, double drift = 0)
        {
            double r = el.R; a += el.Spin * Math.PI;                                       // (its partner of opposite spin: across from it)
            double u, v, w;
            switch (el.L)
            {
                case 0:
                    {
                        // s: round, a path on its sphere, tilted its own way
                        double tilt = Hash(key + el.Sub, el.Spin, 33, _seed) * Math.PI + drift * 0.5;   // (its plane turning slowly: a path never the same twice)
                        u = Math.Cos(a) * r; v = Math.Sin(a) * r * Math.Sin(tilt); w = Math.Sin(a) * r * Math.Cos(tilt); break;
                    }
                case 1:
                    {
                        // p: out through one lobe, back through the node at the nucleus, out through the other, along its axis
                        double t = Math.Sin(a), side = Math.Sin(2 * a) * 0.32;
                        double ax = el.M == 0 ? 1 : 0, ay = el.M == 1 ? 1 : 0, az = el.M == 2 ? 1 : 0;
                        double px = ay, py = az, pz = ax;                                      // (a direction across it)
                        u = (ax * t * 1.25 + px * side) * r; v = (ay * t * 1.25 + py * side) * r; w = (az * t * 1.25 + pz * side) * r; break;
                    }
                case 2:
                    {
                        // d: round its four leaves in their plane (or, for d z^2, through its two lobes and round its ring)
                        if (el.M == 4) { double t = Math.Sin(a); u = Math.Cos(3 * a) * r * 0.45; v = t * r * 1.2; w = Math.Sin(3 * a) * r * 0.45; break; }
                        double q = Math.Abs(Math.Cos(2 * a)) * r * 1.3, c = Math.Cos(a) * q, d2 = Math.Sin(a) * q;
                        if (el.M == 3) { double cc = (c - d2) * 0.7071, dd = (c + d2) * 0.7071; c = cc; d2 = dd; }
                        if (el.M == 0 || el.M == 3) { u = c; v = el.M == 0 ? d2 : 0; w = el.M == 3 ? d2 : 0; }
                        else if (el.M == 1) { u = 0; v = c; w = d2; } else { u = c; v = 0; w = d2; }
                        break;
                    }
                default:
                    {
                        // f: round its six leaves
                        double q = Math.Abs(Math.Cos(3 * a)) * r * 1.3, b = el.M * 0.9;
                        u = Math.Cos(a) * q; v = Math.Sin(b) * Math.Sin(a) * q; w = Math.Cos(b) * Math.Sin(a) * q; break;
                    }
            }
            // the whole atom's orbitals turned one way for this atom (its own place in the crystal), and turning on slowly with time
            // (drift: each subshell and orbital its own way), so every path keeps changing, as an electron's does
            // and a new path every time round: an electron has no path of its own, only a cloud of where it may be, so the loop it is
            // shown on is never the same twice. Each time round the loop's turn and tilt change (from the count of times round, so
            // every electron its own), eased in over the last quarter of the way round, never a jump
            double rev = Math.Floor(a / (2 * Math.PI)), fr = a / (2 * Math.PI) - rev, ease = Math.Max(0, (fr - 0.75) / 0.25); ease = ease * ease * (3 - 2 * ease);
            double h0 = Hash(key + el.Sub * 7, el.Spin + (long)rev * 13, 35 + el.M, _seed), h1 = Hash(key + el.Sub * 7, el.Spin + ((long)rev + 1) * 13, 35 + el.M, _seed);
            double revTurn = (h0 + (h1 - h0) * ease) * Math.PI * 2, revTilt = (Hash(key + el.Sub * 7, el.Spin + (long)rev * 13, 36 + el.M, _seed) * 2 - 1) * 0.9;
            double revTilt1 = (Hash(key + el.Sub * 7, el.Spin + ((long)rev + 1) * 13, 36 + el.M, _seed) * 2 - 1) * 0.9; revTilt += (revTilt1 - revTilt) * ease;
            double turn = Hash(key, 7, 34, _seed) * Math.PI * 2 + drift * (0.6 + 0.25 * el.Sub + 0.1 * el.M) + revTurn, ct = Math.Cos(turn), st = Math.Sin(turn);
            double x1 = u * ct - w * st, z1 = u * st + w * ct, tb = drift * 0.37 * (1 + 0.2 * el.Sub) + revTilt, cb = Math.Cos(tb), sb = Math.Sin(tb);
            x = x1; y = v * cb - z1 * sb; z = v * sb + z1 * cb;
        }
        // how far round its path an electron is at a time (inner and more tightly held electrons faster; slowed to be watched)
        private static double ElectronPhase(Electron el, int k, double time) { return time * 0.3 * Math.Sqrt(el.W) + k * 2.399963229728653; }   // (slowed to be watched: the outer shell a turn in about twenty seconds, the innermost in about seven; at a twentieth of that the outer ones looked frozen)
        private static double ElectronDrift(double time) { return time * 0.025; }   // how far the paths have turned on (radians): a slow precession, a whole turn in about four minutes

        private static int[] Shells(int z)
        {
            int[][] order = { new[] { 1, 0 }, new[] { 2, 0 }, new[] { 2, 1 }, new[] { 3, 0 }, new[] { 3, 1 }, new[] { 4, 0 }, new[] { 3, 2 }, new[] { 4, 1 }, new[] { 5, 0 }, new[] { 4, 2 }, new[] { 5, 1 },
                new[] { 6, 0 }, new[] { 4, 3 }, new[] { 5, 2 }, new[] { 6, 1 }, new[] { 7, 0 }, new[] { 5, 3 }, new[] { 6, 2 }, new[] { 7, 1 } };
            var shells = new int[7]; int left = z, top = 0;
            foreach (var o in order)
            {
                if (left <= 0) break;
                int take = Math.Min(left, 2 * (2 * o[1] + 1)); shells[o[0] - 1] += take; left -= take; top = Math.Max(top, o[0]);
            }
            var r = new int[top]; Array.Copy(shells, r, top); return r;
        }

        /// <summary>What the ground at this spot is made of: its kind, whether it is under water or ice, and its colour; and
        /// the spot (a seed), so the same place always holds the same grains.</summary>
        public void Set(string planetKind, bool sea, bool ice, float r, float g, float b, ulong spot)
        { Announce("Set", true, new object[] { planetKind, sea, ice, r, g, b, spot }); _recDepth++; try { _bornSec = double.NaN; Generation++;
            string id = sea ? "sea" : ice ? "ice" : planetKind;
            if (!_mixes.ContainsKey(id)) id = "desert";
            _space = false; _spaceKind = 0; _mixId = id; _mix = _mixes.ContainsKey(id) ? _mixes[id] : null; _seed = spot;
            _fx = _fz = _tfx = _tfz = _fy = _tfy = 0; _picked = null;
            double gk; Grain = _grainKm.TryGetValue(id, out gk) ? gk : 3e-7; _noGrains = Grain <= 0; if (_noGrains) Grain = 3e-7;   // the sea has no grains
        } finally { _recDepth--; } }

        private static double Hash(long i, long j, long k, ulong seed)
        {
            ulong h = (ulong)i * 0x9E3779B97F4A7C15UL ^ (ulong)j * 0xC2B2AE3D27D4EB4FUL ^ (ulong)k * 0x165667B19E3779F9UL ^ seed;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29; h *= 0x94D049BB133111EBUL; h ^= h >> 32;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }

    }
}
