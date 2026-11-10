// Universe Â· shell Â· the matter view, its part: grains (one class in several files: src/shell/MatterView.cs is its start)
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
        // ── the ground's grains: the planet's own (GrainField), the same ones its ground is the top of ──
        private Universe.Gen.GrainField.Mix _gm; private ulong _gseed; private double _R, _sx, _sy, _sz, _ax, _ay, _az, _nx, _ny, _nz, _he, _hn;
        private sealed class GrainRec { public long Key; public double X, Y, Z, Rad; public Mineral M; public float Shade; public int Layer; }
        private readonly Dictionary<long, GrainRec> _grains = new Dictionary<long, GrainRec>();
        private double _gcx = double.NaN, _gcz, _greach; private readonly List<GrainRec> _gnear = new List<GrainRec>();
        /// <summary>The planet the matter is on: its radius (km), the spot's unit direction, its grains (the mix and seed its
        /// ground uses) and the land's slope there (east and north, km per km), so the grains are the planet's own.</summary>
        public void SetGround(double radiusKm, double dx, double dy, double dz, string mixId, ulong grainSeed, double slopeE, double slopeN)
        { Announce("SetGround", false, new object[] { radiusKm, dx, dy, dz, mixId, grainSeed, slopeE, slopeN }); _recDepth++; try {
            _gm = Universe.Gen.GrainField.For(mixId); _gseed = grainSeed; _R = radiusKm; _sx = dx; _sy = dy; _sz = dz; _he = slopeE; _hn = slopeN;
            Universe.Gen.GrainField.Axes(dx, dy, dz, out _ax, out _ay, out _az, out _nx, out _ny, out _nz);
            _grains.Clear(); _gcx = double.NaN; _gcx2 = double.NaN;
            if (_gm != null && !_noGrains) { Grain = _gm.Size; }
        } finally { _recDepth--; } }
        // the grains within reach of (cx, cz), where they lie in the matter's frame (east, up, north from the spot), cached
        private double _gcx2 = double.NaN, _gcz2, _greach2; private readonly List<GrainRec> _gnear2 = new List<GrainRec>();
        private List<GrainRec> NearGrains(double cx, double cz, double reach)
        {
            bool small = reach <= Grain * 4.5;                                           // round the point you dive into, or all in view
            var outL = small ? _gnear : _gnear2;
            if (_gm == null) return outL;
            double ocx = small ? _gcx : _gcx2, ocz = small ? _gcz : _gcz2, ore = small ? _greach : _greach2;
            if (!double.IsNaN(ocx) && Math.Abs(cx - ocx) < Grain * 0.05 && Math.Abs(cz - ocz) < Grain * 0.05 && Math.Abs(reach - ore) < reach * 0.25) return outL;
            if (small) { _gcx = cx; _gcz = cz; _greach = reach; } else { _gcx2 = cx; _gcz2 = cz; _greach2 = reach; }
            outL.Clear();
            double px = _sx * _R + _ax * cx + _nx * cz, py = _sy * _R + _ay * cx + _ny * cz, pz = _sz * _R + _az * cx + _nz * cz, pl = Math.Sqrt(px * px + py * py + pz * pz);
            var list = new List<Universe.Gen.GrainField.Grain>(); Universe.Gen.GrainField.Near(px / pl, py / pl, pz / pl, _R, reach, _gm, _gseed, list);
            foreach (var g in list)
            {
                GrainRec rec;
                if (!_grains.TryGetValue(g.Key, out rec))
                {
                    double ox = (g.Dx - _sx) * _R, oy = (g.Dy - _sy) * _R, oz = (g.Dz - _sz) * _R;
                    double x = ox * _ax + oy * _ay + oz * _az, z = ox * _nx + oy * _ny + oz * _nz;
                    Mineral m; _minerals.TryGetValue(_gm.Minerals[g.Mineral], out m);
                    rec = new GrainRec { Key = g.Key, X = x, Z = z, Y = _he * x + _hn * z - g.Depth, Rad = g.Rad, M = m, Shade = g.Shade, Layer = (int)(g.Key & 1) };   // on the land's own slope
                    if (_grains.Count > 200000) _grains.Clear();
                    _grains[g.Key] = rec;
                }
                if (rec.M != null) outL.Add(rec);
            }
            return outL;
        }
        // the grain that is the top of the ground at (x, z): the one whose face is highest there
        private GrainRec TopGrain(double x, double z)
        {
            GrainRec best = null; double bestY = double.MinValue;
            foreach (var g in NearGrains(_fx, _fz, Grain * 4))
            {
                double y; if (!GrainTop(g.Key, 0, g.X, g.Y, g.Z, g.Rad, x, z, Math.Max(_scale / 40, AtomSpacing), out y)) continue;
                if (y > bestY) { bestY = y; best = g; }
            }
            if (best == null)                                                            // in a gap: the nearest grain
            { double bd = double.MaxValue; foreach (var g in _gnear) { double d = (g.X - x) * (g.X - x) + (g.Z - z) * (g.Z - z); if (d < bd) { bd = d; best = g; } } }
            return best;
        }
        private GrainRec _focusRec; private double _focusAtX = double.NaN, _focusAtZ;
        private GrainRec FocusRec { get { if (_focusRec == null || _focusAtX != _fx || _focusAtZ != _fz) { _focusRec = TopGrain(_fx, _fz); _focusAtX = _fx; _focusAtZ = _fz; } return _focusRec; } }

        // the mineral of a grain (by its key); a mix's mineral picked by (i, j) for a speck of the ground
        private Mineral GrainMineral(long i, long j)
        {
            GrainRec rec; if (j == 0 && _grains.TryGetValue(i, out rec)) return rec.M;
            return MixPick(i, j);
        }
        private Mineral MixPick(long i, long j)
        {
            if (_mix == null) return null;
            double total = 0; foreach (var kv in _mix) total += kv.Value;
            double t = Hash(i, j, 7, _seed) * total;
            foreach (var kv in _mix) { t -= kv.Value; if (t <= 0) return kv.Key; }
            return _mix[_mix.Count - 1].Key;
        }

        private Element El(string sym) { Element e; return _elements.TryGetValue(sym, out e) ? e : null; }
    }
}
