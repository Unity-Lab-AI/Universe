// Universe · shell · the universe view, its part: cosmos (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── the visible universe (content/universe.unt): about 100 billion galaxies in a sphere 13.4 billion light-years
        // across. They are made by regions round you (each region always holds the same ones, from its own place), at the real
        // average density, more of them in the cosmic web's filaments and walls and fewer in its voids; beyond, the rest show
        // as a glow of points along the same web; the edge cannot be passed ──
        private List<string> _types, _typeNames; private ulong _seed;
        private double _farScale;                                                         // how far out the wheel has zoomed (universe units): out grows it, in brings it back
        private bool _matterCardDrawn;                                                 // this frame: a card of the matter is up, so no other card is drawn over it
        private double _statsLeft = double.MaxValue, _statsBottom;   // the frame counter and stats card's left edge and bottom on screen, so no other card lands on it
        private double _uRadius = 134000, _uGalaxies = 1e11, _webUnits = 4000, _webContrast = 6, _regionUnits = 20, _nearUnits = 400; private int _glowPoints = 300000;
        private double _darkMatter, _rimSpeed = 220, _darkEnergy, _hubble = 70;
        private double _spinLo = 0.1, _spinHi = 0.998, _swellOverFreefall = 2, _wayInLy = 3e6, _wayInMass = 10, _wayInPower = 1;   // the way in from a hole's horizon to its singularity, by its mass (content/physics.unt @law hole-inside)   // every hole's spin, a share of the most (content/physics.unt @law frame-dragging); a star's swelling with gravity off (@law gravity-off)
        private readonly HashSet<long> _regions = new HashSet<long>(); private long _lastRegionCell = long.MinValue;
        private UploadBuffer _glowBuf; private int _glowCount; private StarVertex[] _glowAll, _glowShown = new StarVertex[0]; private long _glowCameAt; private double _glowAtT = -10, _glowAtX = double.NaN, _glowAtY, _glowAtZ;
        private void AddGalaxy(int t, ulong gseed, double x, double y, double z, float tilt, float turn, float scale)
        {
            var g = new Galaxy { Type = _types[t], Name = _typeNames[t], Seed = gseed, X = x, Y = y, Z = z, Tilt = tilt, Turn = turn, Scale = scale };
            g.Region = _addingRegion;
            if (_galaxies.Count == 0)
            {
                // home: made at once (you begin there)
                var fk = new int[FarStars];
                var stars = GalaxyGen.Generate(g.Type, g.Seed, FarStars, _kinds, fk);
                g.Far = new UploadBuffer(_gpu.Device, (long)stars.Length * Stride); g.Far.Write(stars, stars.Length); g.FarCount = stars.Length; g.FarPts = stars; g.FarKinds = fk;
            }
            else
            {
                // every other galaxy's stars made on the background workers, so the frame never waits; taken to the card a few each
                // frame when ready (until then it is not drawn: the web's glow stands for it)
                var kinds = _kinds; var gg = g;
                Jobs.Run(() => { var fk = new int[FarStars]; var st = GalaxyGen.Generate(gg.Type, gg.Seed, FarStars, kinds, fk); gg.PendingKinds = fk; gg.Pending = st; });
            }
            _galaxies.Add(g);
        }
        // how crowded the cosmic web is at a place: filaments and walls (ridges of a noise a web cell across) dense, voids empty
        // how crowded the cosmic web is at a place (its mean over space about one): the Voronoi foam of large-scale structure.
        // Space is packed with voids, one round each seed point of a grid a void across (jittered); galaxies lie on the thin
        // walls where two voids meet, more on the filaments where three meet, most in the clusters at the nodes where four
        // meet; the voids' insides hold very few (as the surveys and the Millennium simulation show)
        private double _wallUnits = 240;
        private void VoidSeed(long i, long j, long k, out double x, out double y, out double z)
        {
            ulong h = (ulong)(i * 73856093L ^ j * 19349663L ^ k * 83492791L) ^ _seed; var r = new Rng(h * 0x9E3779B97F4A7C15UL + 7);
            x = (i + 0.15 + 0.7 * r.NextDouble()) * _webUnits; y = (j + 0.15 + 0.7 * r.NextDouble()) * _webUnits; z = (k + 0.15 + 0.7 * r.NextDouble()) * _webUnits;
        }
        private double _webOffX, _webOffY, _webOffZ, _webMean = 1;
        private double Web(double x, double y, double z)
        {
            x += _webOffX; y += _webOffY; z += _webOffZ;
            long ci = (long)Math.Floor(x / _webUnits), cj = (long)Math.Floor(y / _webUnits), ck = (long)Math.Floor(z / _webUnits);
            double d1 = double.MaxValue, d2 = double.MaxValue, d3 = double.MaxValue, d4 = double.MaxValue;
            for (long i = ci - 1; i <= ci + 1; i++) for (long j = cj - 1; j <= cj + 1; j++) for (long k = ck - 1; k <= ck + 1; k++)
            {
                double sx, sy, sz; VoidSeed(i, j, k, out sx, out sy, out sz);
                double d = Math.Sqrt((sx - x) * (sx - x) + (sy - y) * (sy - y) + (sz - z) * (sz - z));
                if (d < d1) { d4 = d3; d3 = d2; d2 = d1; d1 = d; } else if (d < d2) { d4 = d3; d3 = d2; d2 = d; } else if (d < d3) { d4 = d3; d3 = d; } else if (d < d4) d4 = d;
            }
            // how near the point is to a wall (equal distance to its two nearest voids), a filament (three), a node (four)
            double w = _wallUnits, wall = Math.Exp(-Math.Pow((d2 - d1) / w, 2)), fil = Math.Exp(-Math.Pow((d3 - d1) / (w * 1.4), 2)), node = Math.Exp(-Math.Pow((d4 - d1) / (w * 2), 2));
            return (0.15 + wall * 0.6 + fil * _webContrast + node * _webContrast * 8) / _webMean;   // voids thin (about a fifth of the mean, as the surveys find: never empty, so no half of the sky is ever bare); walls; most on filaments and in clusters (its mean over space one)
        }
        private double MeanPerUnit3 { get { return _uGalaxies / (4.0 / 3.0 * Math.PI * _uRadius * _uRadius * _uRadius); } }
        // the galaxies of every region within reach of you that has none yet (each region's own from its place); at most a few a frame
        private long _addingRegion;
        // a galaxy you go to is always made: picked, by the tour or a click, or come near to; its stars made at once (a few
        // milliseconds) if the background workers have not got to it yet
        private void EnsureMade(int i)
        {
            if (i < 0 || i >= _galaxies.Count) return; var g = _galaxies[i]; if (g.Far != null) return;
            var st = g.Pending; int[] fk = g.PendingKinds;
            if (st == null) { fk = new int[FarStars]; st = GalaxyGen.Generate(g.Type, g.Seed, FarStars, _kinds, fk); }
            g.Far = new UploadBuffer(_gpu.Device, (long)st.Length * Stride); g.Far.Write(st, st.Length); g.FarCount = st.Length; g.FarPts = st; g.FarKinds = fk; g.Pending = null;
        }
        // galaxies whose stars are ready go to the card, a few each frame
        private void TakeGalaxies()
        {
            if (_tKind == 0) EnsureMade(_tIndex); EnsureMade(_tGal);
            int took = 0;
            foreach (var g in _galaxies)
            {
                var st = g.Pending; if (st == null || took >= 24) continue;   // (each only a few thousand points: two dozen a frame, so the galaxies round you are never long missing)
                if (g.Far != null) { g.Pending = null; continue; }                       // (already made at once when it was gone to)
                g.Far = new UploadBuffer(_gpu.Device, (long)st.Length * Stride); g.Far.Write(st, st.Length); g.FarCount = st.Length; g.FarPts = st; g.FarKinds = g.PendingKinds; g.Pending = null; took++;
            }
        }
        // galaxies far behind you let go (their regions make them again, the same ones, if you come back); the indices that point
        // into the list kept right
        private void DropFarGalaxies()
        {
            double far = _nearUnits * 1.8; var keep = new List<Galaxy>(_galaxies.Count); var map = new int[_galaxies.Count];
            // the far galaxy the tour is crossing to: its whole region is held until you are near it (then the usual rules hold it)
            if (_heldRegion != 0 && (_heldX - _cx) * (_heldX - _cx) + (_heldY - _cy) * (_heldY - _cy) + (_heldZ - _cz) * (_heldZ - _cz) < _nearUnits * _nearUnits) _heldRegion = 0;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz;
                bool held = i == 0 || i == _near || (_tKind == 0 && i == _tIndex) || i == _tGal || g.Pending != null || (_heldRegion != 0 && g.Region == _heldRegion) || (g.Far == null && g.FarPts == null && g.Region != 0 && dx * dx + dy * dy + dz * dz < far * far);
                if (held || dx * dx + dy * dy + dz * dz < far * far) { map[i] = keep.Count; keep.Add(g); continue; }
                map[i] = -1; if (g.Far != null) g.Far.Dispose(); if (g.Struct != null) g.Struct.Dispose(); _regions.Remove(g.Region);
            }
            if (keep.Count == _galaxies.Count) return;
            Func<int, int> m = k => k >= 0 && k < map.Length ? map[k] : k;
            _near = m(_near); _tGal = m(_tGal); if (_tKind == 0) { _tIndex = m(_tIndex); if (_tIndex < 0) _tKind = -1; }
            _galaxies.Clear(); _galaxies.AddRange(keep);
        }
        private void SpawnRegions()
        {
            double cx = _cx, cy = _cy, cz = _cz, R = _regionUnits; long ci = (long)Math.Floor(cx / R), cj = (long)Math.Floor(cy / R), ck = (long)Math.Floor(cz / R);
            long here = ci * 73856093L ^ cj * 19349663L ^ ck * 83492791L; if (here == _lastRegionCell) return;
            // crossing the universe faster than a couple of regions a frame (the tour's crossing to a far galaxy, a zoom out to
            // the web): nothing is made on the way (it would be gone before it was seen, and thousands of galaxies' stars would
            // be asked for at once); the web's glow stands for them, and the regions round you are made as you slow
            bool fast = _lastRegionCell != long.MinValue && (Math.Abs(ci - _lastRegionI) > 2 || Math.Abs(cj - _lastRegionJ) > 2 || Math.Abs(ck - _lastRegionK) > 2);
            _lastRegionCell = here; _lastRegionI = ci; _lastRegionJ = cj; _lastRegionK = ck;
            DropFarGalaxies();
            if (fast) return;
            int n = (int)Math.Ceiling(_nearUnits / R), made = 0;
            for (long i = ci - n; i <= ci + n; i++)
                for (long j = cj - n; j <= cj + n; j++)
                    for (long k = ck - n; k <= ck + n; k++)
                    {
                        double mx = (i + 0.5) * R, my = (j + 0.5) * R, mz = (k + 0.5) * R;
                        if ((mx - cx) * (mx - cx) + (my - cy) * (my - cy) + (mz - cz) * (mz - cz) > _nearUnits * _nearUnits) continue;
                        made += SpawnRegion(i, j, k);
                    }
            if (made > 0) _glowAtX = double.NaN;                                           // the glow is remade without the near ones
        }
        private long _lastRegionI, _lastRegionJ, _lastRegionK;
        // one region's galaxies, made from its own place (the same ones every time), if it is inside the universe and not made yet:
        // how many it holds is the mean for its volume, crowded or thinned by the web, the rest by chance
        private int SpawnRegion(long i, long j, long k)
        {
            double R = _regionUnits, mx = (i + 0.5) * R, my = (j + 0.5) * R, mz = (k + 0.5) * R;
            // (no edge: galaxies are made wherever you go; what you see of them ends only at your horizon, which moves with you)
            long key = i * 73856093L ^ j * 19349663L ^ k * 83492791L; if (!_regions.Add(key)) return 0; _addingRegion = key;
            var rng = new Rng((ulong)key * 0x9E3779B97F4A7C15UL ^ _seed); int made = 0;
            double lambda = MeanPerUnit3 * R * R * R * Web(mx, my, mz);
            int count = (int)Math.Floor(lambda) + (rng.NextDouble() < lambda - Math.Floor(lambda) ? 1 : 0);
            for (int q = 0; q < count && q < 40; q++)
            {
                double gx = (i + rng.NextDouble()) * R, gy = (j + rng.NextDouble()) * R, gz = (k + rng.NextDouble()) * R;
                if (gx * gx + gy * gy + gz * gz < 4) continue;                               // the first galaxy's own space (the one you begin in)
                int t = rng.Below(_types.Count);
                float scale = _types[t].StartsWith("dwarf") ? 0.35f : _types[t].StartsWith("giant") ? 1.6f : rng.Range(0.8f, 1.2f);
                AddGalaxy(t, _seed + (ulong)key * 104729UL + (ulong)q, gx, gy, gz, rng.Range(-1.2f, 1.2f), rng.Range(0, 6.28f), scale);
                made++;
            }
            return made;
        }
        // the glow of the rest of the universe's galaxies: points drawn from the same web, each standing for many galaxies,
        // left out round you where the galaxies are made in full
        private System.Threading.Thread _glowMaker; private volatile StarVertex[] _glowReady; private double _glowCX, _glowCY, _glowCZ;
        private void MakeGlow()
        {
            double reachU = 4.5e9 / Units.LightYearsPerUnit;
            // made round where you are, half again past the reach it is drawn to, and made again round you (on its own thread)
            // once you have gone an eighth of that reach; the old light stays drawn till the new is ready, so it is never dark
            // and never has an edge in view
            if (_glowReady != null && _glowMaker != null) { bool first = _glowAll == null; _glowAll = _glowReady; _glowReady = null; _glowMaker = null; if (first) _glowCameAt = System.Diagnostics.Stopwatch.GetTimestamp(); }
            bool moved = _glowAll != null && Math.Sqrt((_cx - _glowCX) * (_cx - _glowCX) + (_cy - _glowCY) * (_cy - _glowCY) + (_cz - _glowCZ) * (_cz - _glowCZ)) > reachU * 0.12;
            if (_glowAll == null || moved)
            {
                {
                    if (_glowMaker == null)
                    {
                        // points through the whole sphere, each kept as often as the web is crowded there: the walls, filaments and
                        // clusters show, the voids stay dark; made on a thread of its own so nothing waits
                        int want = Math.Min(_glowPoints, 900000); _glowWant = want; _glowMade = 0; double R = _uRadius, gcx = _cx, gcy = _cy, gcz = _cz, gr = reachU * 1.5; _glowCX = gcx; _glowCY = gcy; _glowCZ = gcz; double top = (0.6 + 1.5) / _webMean; ulong sd = _seed;   // (top: a wall's density and a little more keeps every point (filaments and clusters show by crowding, voids two hundred times thinner): a quarter of the tries are kept, so it is made in a few seconds)
                        _glowMaker = new System.Threading.Thread(() =>
                        {
                            // made on every core but one at once, each its own share from its own seed (the same points every time),
                            // so it shows a few seconds after you start, never only after you have been out among the galaxies
                            int parts = Math.Max(2, Environment.ProcessorCount - 1), each = (want + parts - 1) / parts;
                            var lists = new List<StarVertex>[parts]; var workers = new System.Threading.Thread[parts];
                            for (int pi = 0; pi < parts; pi++)
                            {
                                int me = pi;
                                workers[pi] = new System.Threading.Thread(() =>
                                {
                                    var rng = new Rng(sd ^ 0x6107UL ^ ((ulong)(me + 1) * 0x9E3779B97F4A7C15UL)); var list = new List<StarVertex>(each); long tries = 0;
                                    while (list.Count < each && tries++ < (long)each * 60)
                                    {
                                        double x = gcx + (rng.NextDouble() * 2 - 1) * gr, y = gcy + (rng.NextDouble() * 2 - 1) * gr, z = gcz + (rng.NextDouble() * 2 - 1) * gr;
                                        if (x * x + y * y + z * z > R * R) continue;                         // inside the visible universe
                                        if ((x - gcx) * (x - gcx) + (y - gcy) * (y - gcy) + (z - gcz) * (z - gcz) > gr * gr) continue;   // within reach of where you are
                                        double wv = Web(x, y, z); if (rng.NextDouble() * top * top > wv * wv) continue;   // kept by the web's density squared: its filaments and clusters stand out bright, its voids go dark
                                        float warm = (float)rng.NextDouble();
                                        list.Add(new StarVertex { X = (float)x, Y = (float)y, Z = (float)z, Size = 1.2f, R = 0.75f + 0.25f * warm, G = 0.72f + 0.1f * warm, B = 1f - 0.3f * warm, A = 1 });
                                        if ((list.Count & 1023) == 0) System.Threading.Interlocked.Add(ref _glowMade, 1024);   // (how far along, for the start's loading bar)
                                    }
                                    lists[me] = list;
                                }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
                                workers[pi].Start();
                            }
                            var all = new List<StarVertex>(want); for (int pi = 0; pi < parts; pi++) { workers[pi].Join(); all.AddRange(lists[pi]); }
                            _glowReady = all.ToArray();
                        }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
                        _glowMaker.Start();
                    }
                    if (_glowAll == null) return;
                }
            }
            // the whole web's light goes to the card once; the card leaves out the points near you (where the galaxies are made in
            // full) and fades the rest by distance, every frame, per point (nothing sifted on the processor)
            double cx = _cx, cy = _cy, cz = _cz;
            if (_glowShown != _glowAll)
            {
                _glowShown = _glowAll; _glowCount = _glowAll.Length;
                if (_glowBuf == null || _glowBuf.Bytes < (long)Math.Max(1, _glowCount) * Stride) { if (_glowBuf != null) _glowBuf.Dispose(); _glowBuf = new UploadBuffer(_gpu.Device, (long)(_glowAll.Length + 16) * Stride); }
                if (_glowCount > 0) _glowBuf.Write(_glowAll, _glowCount);
            }
            _glowAtX = cx; _glowAtY = cy; _glowAtZ = cz;
        }
        // the universe has no edge to be held inside: only a horizon, centred on you (move a light-year and you see a light-year
        // further); nothing holds you
        private void HoldInside() { }

    }
}
