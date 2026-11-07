// DNA-AND · shell · the one world: a single camera that flies through every scale at any angle, with no views to switch.
// The universe of galaxies and each galaxy's own stars are drawn together; as you come closer to a galaxy its far glow
// fades out while its real stars fade in, so it grows into its stars without a seam. Zooming moves the camera along the
// line under the mouse by a share of the distance to whatever is nearest, so it slows by itself as things come close
// and never runs out. Positions are kept in a chain of frames (the universe, then a galaxy), each with its own origin,
// so nothing loses precision however deep you go. (Star systems, planets and the ground join the same camera next.)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DnaAnd.Core;
using DnaAnd.Gen;
using DnaAnd.Platform;
using DnaAnd.Render;

namespace DnaAnd.Shell
{
    internal sealed class WorldView : IDisposable
    {
        private sealed class Galaxy { public volatile StarVertex[] Pending; public int[] PendingKinds; public long Region; public string Type, Name; public ulong Seed; public double X, Y, Z; public float Tilt, Turn, Scale; public UploadBuffer Far; public int FarCount; public StarVertex[] FarPts; public int[] FarKinds; public UploadBuffer Struct; public int StructCount; public List<double[]> Features; }   // Struct and Features: its clusters and nebulae, made once it is near enough to see them
        private readonly List<Galaxy> _galaxies = new List<Galaxy>();
        private readonly Gpu _gpu; private readonly BillboardDrawer _dots; private readonly ContentLibrary _content;
        private readonly List<StarKind> _kinds;
        private readonly MeshPipes _meshes;
        private readonly ScaleHud _hud;
        private readonly MatterView _matter; private double _matterScale;     // below the ground's finest piece: how big the view is (km), 0 = not in the matter
        // the planet or moon you have come close to: its real ground, drawn in the same camera (-1: none)
        private int _planet = -1; private PlanetTerrain _ground; private double _px, _py, _pz;   // its centre from the star, in universe units
        private const double PlanetRange = 40;                // within 40 of its radii, the ground is made and drawn
        private static readonly int Stride = Marshal.SizeOf(typeof(StarVertex));
        private const int FarStars = 2500, NearStars = 150000;
        private const double Spread = 120;                 // the universe patch, in galaxy units (one unit is 50,000 light-years)

        // the camera: an anchor (a galaxy's centre, or the star of the system you are in, in universe units) plus how far the camera
        // is from it. Distances near the camera stay exact because they are worked out from the small offset, not the huge anchor.
        private double _ax, _ay, _az, _ox, _oy, _oz;
        // the camera's way: the direction it looks and its own up, never angles (angles lock at straight up and down: gimbal lock)
        private double _fx = -0.8652, _fy = -0.3429, _fz = -0.3658; private Vec3 _camUp = new Vec3(0, 1, 0);
        private double _cx { get { return _ax + _ox; } }
        private double _cy { get { return _ay + _oy; } }
        private double _cz { get { return _az + _oz; } }
        private void Anchor(double x, double y, double z)
        {
            // move the anchor without moving the camera: the offset takes up the difference (worked out before adding)
            _ox = (_ax - x) + _ox; _oy = (_ay - y) + _oy; _oz = (_az - z) + _oz; _ax = x; _ay = y; _az = z;
        }

        // the star system around you, once you are within about 2,000 AU of a star of the near galaxy
        private int _sysStar = -1; private SpaceGen.StarSystem _system; private int[] _nearKinds;
        private UploadBuffer _sysStatic, _sysBodies; private StarVertex[] _bodyVerts, _staticShifted; private int _bodyCount;
        private double _simYears;
        // ── time: how fast it runs (years a second of yours), from paused to ten years a second; an hour a second at first ──
        public static readonly double[] TimeSpeeds = { 0, 1 / 31557600.0, 60 / 31557600.0, 3600 / 31557600.0, 86400 / 31557600.0, 7 * 86400 / 31557600.0, 30.44 * 86400 / 31557600.0, 1 / 20.0, 1, 10 };
        public static readonly string[] TimeNames = { "paused", "real time", "a minute a second", "an hour a second", "a day a second", "a week a second", "a month a second", "a year in 20 seconds", "a year a second", "ten years a second" };
        public int TimeStep = 3; private int _timeBeforePause = 3;
        /// <summary>Pauses, or carries on at the speed it had.</summary>
        public void TogglePause() { if (TimeStep == 0) TimeStep = _timeBeforePause; else { _timeBeforePause = TimeStep; TimeStep = 0; } MenuChanged = true; }
        public void SetTime(int step) { TimeStep = Math.Max(0, Math.Min(TimeSpeeds.Length - 1, step)); if (TimeStep > 0) _timeBeforePause = TimeStep; MenuChanged = true; }

        // a body's day in hours: rocky worlds 10 to 40, giants 9 to 17, moons turn once each orbit (one face to their planet)
        private double DayHours(int i)
        {
            var b = _system.Bodies[i];
            if (b.Parent >= 0) return Math.Max(1, b.Period * 8766);
            double u = ((ulong)(_sysStar * 7919L + i * 104729L) * 0x9E3779B97F4A7C15UL >> 40) / (double)(1UL << 24);
            bool giant = b.Kind.Contains("giant") || b.Kind.Contains("Jupiter");
            return giant ? 9 + 8 * u : 10 + 30 * u;
        }

        // the planet's own turn now (radians): the starlight turns the other way across its ground
        private double Spin(int i) { return 2 * Math.PI * (_simYears * 8766.0 / DayHours(i)) % (2 * Math.PI); }

        /// <summary>The local time of day where you are on the ground (hours, 0 to 24), or -1 when not at a planet.</summary>
        public double LocalHour()
        {
            if (_ground == null) return -1;
            double x, y, z; CamFromPlanet(out x, out y, out z);
            double sx = _ground.StarDir[0], sz = _ground.StarDir[2];
            double a = Math.Atan2(z, x) - Math.Atan2(sz, sx);                               // noon where the star is overhead
            double h = 12 + a / (2 * Math.PI) * 24; h %= 24; if (h < 0) h += 24; return h;
        }
        private StarSurface _starSurf; private StarVertex[] _starCorona, _starSurfVerts; private UploadBuffer _coronaBuf, _surfBuf;
        private static readonly double SystemRange = Units.Au(2000);
        // the galaxy whose own stars are being shown (or -1), and those stars in its own frame
        private int _near = -1; private StarVertex[] _nearLocal, _nearDrawn; private UploadBuffer _nearBuf;
        private int[] _gridHead, _gridNext; private const int GridN = 64;     // a grid over the near galaxy's stars, to find the nearest one fast

        public WorldView(Gpu gpu, BillboardPipeline pipe, MeshPipes meshes, ContentLibrary content, ulong seed)
        {
            _meshes = meshes; _gpu = gpu; _hud = new ScaleHud(gpu.Device); _matter = new MatterView(gpu, pipe, content); _dots = new BillboardDrawer(gpu.CommandList, pipe); _content = content;
            var types = new List<string>(); var names = new List<string>();
            foreach (var e in content.OfKind("galaxies.dnat", "galaxy")) { types.Add(e.Id); names.Add(e.Name); }
            if (types.Count == 0) { types.Add("spiral"); names.Add("Spiral galaxy"); }
            _kinds = GalaxyGen.StarKinds(content); _types = types; _typeNames = names; _seed = seed;
            foreach (var e in content.OfKind("universe.dnat", "universe"))
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float; double v;
                if (double.TryParse(e.Get("across-ly", "1.34e10"), fs, ci, out v)) _uRadius = v * 0.5 / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("galaxies", "1e11"), fs, ci, out v)) _uGalaxies = v;
                if (double.TryParse(e.Get("web-ly", "2e8"), fs, ci, out v)) _webUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("web-contrast", "6"), fs, ci, out v)) _webContrast = v;
                if (double.TryParse(e.Get("web-wall-ly", "1.2e7"), fs, ci, out v)) _wallUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("region-ly", "1e6"), fs, ci, out v)) _regionUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("near-ly", "2e7"), fs, ci, out v)) _nearUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("glow-points", "300000"), fs, ci, out v)) _glowPoints = (int)v;
            }
            {
                // the web's mean over space, measured, so the regions hold the real total of galaxies; and home placed on a
                // filament (as the Local Group lies on a filament of the Laniakea supercluster), not in a void
                var mr = new Rng(seed ^ 0x3EA5UL); double sum = 0; int nS = 6000;
                for (int q = 0; q < nS; q++) sum += Web((mr.NextDouble() * 2 - 1) * _webUnits * 20, (mr.NextDouble() * 2 - 1) * _webUnits * 20, (mr.NextDouble() * 2 - 1) * _webUnits * 20);
                _webMean = Math.Max(1e-6, sum / nS);
                double best = -1, bx = 0, by = 0, bz = 0;
                for (int q = 0; q < 4000; q++)
                {
                    double x = (mr.NextDouble() * 2 - 1) * _webUnits, y = (mr.NextDouble() * 2 - 1) * _webUnits, z = (mr.NextDouble() * 2 - 1) * _webUnits, wv = Web(x, y, z);
                    if (wv > best && wv < _webContrast * 3 / _webMean) { best = wv; bx = x; by = y; bz = z; }   // a filament (not the very heart of a cluster)
                }
                _webOffX = bx; _webOffY = by; _webOffZ = bz;
            }
            // home: a spiral at the middle, then every galaxy of the regions round you
            AddGalaxy(Math.Max(0, types.IndexOf("spiral")), seed, 0, 0, 0, 0.5f, 0.3f, 1f);
            SpawnRegions();
            // start out among the galaxies, looking at the first one
            _ox = 30; _oy = 14; _oz = 22;
            LookAt(0, 0, 0);
        }

        // ── the visible universe (content/universe.dnat): about 100 billion galaxies in a sphere 13.4 billion light-years
        // across. They are made by regions round you (each region always holds the same ones, from its own place), at the real
        // average density, more of them in the cosmic web's filaments and walls and fewer in its voids; beyond, the rest show
        // as a glow of points along the same web; the edge cannot be passed ──
        private List<string> _types, _typeNames; private ulong _seed;
        private double _farScale;                                                         // how far out the wheel has zoomed (universe units): out grows it, in brings it back
        private double _uRadius = 134000, _uGalaxies = 1e11, _webUnits = 4000, _webContrast = 6, _regionUnits = 20, _nearUnits = 400; private int _glowPoints = 300000;
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
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz;
                bool held = i == 0 || i == _near || (_tKind == 0 && i == _tIndex) || i == _tGal || g.Pending != null || (g.Far == null && g.FarPts == null && g.Region != 0 && dx * dx + dy * dy + dz * dz < far * far);
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
            long here = ci * 73856093L ^ cj * 19349663L ^ ck * 83492791L; if (here == _lastRegionCell) return; _lastRegionCell = here;
            DropFarGalaxies();
            int n = (int)Math.Ceiling(_nearUnits / R), made = 0;
            for (long i = ci - n; i <= ci + n; i++)
                for (long j = cj - n; j <= cj + n; j++)
                    for (long k = ck - n; k <= ck + n; k++)
                    {
                        double mx = (i + 0.5) * R, my = (j + 0.5) * R, mz = (k + 0.5) * R;
                        if ((mx - cx) * (mx - cx) + (my - cy) * (my - cy) + (mz - cz) * (mz - cz) > _nearUnits * _nearUnits) continue;
                        if (mx * mx + my * my + mz * mz > _uRadius * _uRadius) continue;            // past the edge: nothing
                        long key = i * 73856093L ^ j * 19349663L ^ k * 83492791L; if (!_regions.Add(key)) continue; _addingRegion = key;
                        // how many galaxies this region holds: the mean for its volume, crowded or thinned by the web, the rest by chance
                        var rng = new Rng((ulong)key * 0x9E3779B97F4A7C15UL ^ _seed);
                        double lambda = MeanPerUnit3 * R * R * R * Web(mx, my, mz);
                        int count = (int)Math.Floor(lambda) + (rng.NextDouble() < lambda - Math.Floor(lambda) ? 1 : 0);
                        for (int q = 0; q < count && q < 40; q++)
                        {
                            double gx = (i + rng.NextDouble()) * R, gy = (j + rng.NextDouble()) * R, gz = (k + rng.NextDouble()) * R;
                            if (gx * gx + gy * gy + gz * gz < 4) continue;                   // the first galaxy's own space (the one you begin in)
                            int t = rng.Below(_types.Count);
                            float scale = _types[t].StartsWith("dwarf") ? 0.35f : _types[t].StartsWith("giant") ? 1.6f : rng.Range(0.8f, 1.2f);
                            AddGalaxy(t, _seed + (ulong)key * 104729UL + (ulong)q, gx, gy, gz, rng.Range(-1.2f, 1.2f), rng.Range(0, 6.28f), scale);
                            made++;
                        }
                    }
            if (made > 0) _glowAtX = double.NaN;                                           // the glow is remade without the near ones
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
                        int want = Math.Min(_glowPoints, 900000); double R = _uRadius, gcx = _cx, gcy = _cy, gcz = _cz, gr = reachU * 1.5; _glowCX = gcx; _glowCY = gcy; _glowCZ = gcz; double top = (0.6 + 1.5) / _webMean; ulong sd = _seed;   // (top: a wall's density and a little more keeps every point (filaments and clusters show by crowding, voids two hundred times thinner): a quarter of the tries are kept, so it is made in a few seconds)
                        _glowMaker = new System.Threading.Thread(() =>
                        {
                            var rng = new Rng(sd ^ 0x6107UL); var list = new List<StarVertex>(want); long tries = 0;
                            while (list.Count < want && tries++ < (long)want * 60)
                            {
                                double x = gcx + (rng.NextDouble() * 2 - 1) * gr, y = gcy + (rng.NextDouble() * 2 - 1) * gr, z = gcz + (rng.NextDouble() * 2 - 1) * gr;
                                if (x * x + y * y + z * z > R * R) continue;                         // inside the visible universe
                                if ((x - gcx) * (x - gcx) + (y - gcy) * (y - gcy) + (z - gcz) * (z - gcz) > gr * gr) continue;   // within reach of where you are
                                double wv = Web(x, y, z); if (rng.NextDouble() * top * top > wv * wv) continue;   // kept by the web's density squared: its filaments and clusters stand out bright, its voids go dark (by the density alone it reads as an even dust)
                                float warm = (float)rng.NextDouble();
                                list.Add(new StarVertex { X = (float)x, Y = (float)y, Z = (float)z, Size = 1.2f, R = 0.75f + 0.25f * warm, G = 0.72f + 0.1f * warm, B = 1f - 0.3f * warm, A = 1 });
                            }
                            _glowReady = list.ToArray();
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
        // the edge of the visible universe cannot be passed: you are held just inside it
        private void HoldInside()
        {
            double x = _cx, y = _cy, z = _cz, l = Math.Sqrt(x * x + y * y + z * z), lim = _uRadius * 0.985;
            if (l <= lim) return;
            double k = lim / l; _ox = x * k - _ax; _oy = y * k - _ay; _oz = z * k - _az;
        }

        // ── every other kind of body, made round you from content/bodies.dnat, drawn and clickable: a supermassive black hole at
        // each galaxy's heart; stellar black holes, neutron stars and rogue planets among a galaxy's stars; stars and rogue
        // planets between the galaxies; asteroids, Kuiper-belt bodies and Oort-cloud comets round a star ──
        private sealed class Place { public string Id, Name, Where, Entry, Says, Shape; public double PerLy3, SizeKm, FeedShare, JetShare; public int Shown; public float R, G, B; }
        private sealed class Extra { public double X, Y, Z, R, Mass; public string Name; public List<string> Lines; public float Cr, Cg, Cb; public bool Hole; public int Feeding; public float Tilt, Turn; public string Shape = ""; }   // Feeding: 0 quiet, 1 a glowing disc, 2 a disc and jets
        private List<Place> _places; private readonly List<Extra> _extras = new List<Extra>(); private string _extraKey = "";
        // a feeding black hole's accretion disc and its jets, made once in its own units (Schwarzschild radii), drawn for each,
        // tilted its own way, turning, sized to it
        private UploadBuffer _bhDisc, _bhJets; private int _bhDiscN, _bhJetsN;
        private void MakeBlackHoleParts()
        {
            var rng = new Rng(0xB1AC4UL); var disc = new List<StarVertex>(); var jets = new List<StarVertex>();
            for (int k = 0; k < 6000; k++)
            {
                // from the innermost stable orbit (3 radii) out to about 60, more of it close in; hottest and brightest inside
                // (blue-white), cooling outward (yellow, orange, red), thin
                double r = 3 * Math.Pow(20, Math.Pow(rng.NextDouble(), 1.6)), a = rng.NextDouble() * Math.PI * 2, t = Math.Pow(3 / r, 0.75);
                float br = (float)Math.Min(1, 1.4 * Math.Pow(3 / r, 1.1) + 0.06);
                float cr = (float)(1.0 * br), cg = (float)((0.45 + 0.55 * t) * br), cb = (float)((0.15 + 0.85 * t * t) * br);
                disc.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)(rng.Gaussian() * r * 0.02), Z = (float)(Math.Sin(a) * r), Size = (float)(0.6 + r * 0.03), R = cr, G = cg, B = cb, A = 1 });
            }
            for (int k = 0; k < 2400; k++)
            {
                // two beams from the poles, narrow, fading as they go, blue-violet
                double y = 3 + Math.Pow(rng.NextDouble(), 1.8) * 600, w = 0.6 + y * 0.03, a = rng.NextDouble() * Math.PI * 2, f = Math.Exp(-y / 300);
                float br = (float)(0.25 + 0.75 * f); int side = (k & 1) == 0 ? 1 : -1;
                jets.Add(new StarVertex { X = (float)(Math.Cos(a) * w * rng.NextDouble()), Y = (float)(side * y), Z = (float)(Math.Sin(a) * w * rng.NextDouble()), Size = (float)(1 + y * 0.01), R = 0.55f * br, G = 0.6f * br, B = 1f * br, A = 1 });
            }
            _bhDiscN = disc.Count; _bhDisc = new UploadBuffer(_gpu.Device, (long)disc.Count * Stride); _bhDisc.Write(disc.ToArray(), disc.Count);
            _bhJetsN = jets.Count; _bhJets = new UploadBuffer(_gpu.Device, (long)jets.Count * Stride); _bhJets.Write(jets.ToArray(), jets.Count);
        }
        private void DrawBlackHoles(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Feeding == 0 || ex.Mass <= 0) continue;
                double rs = Units.Km(2.953 * ex.Mass), dx = (ex.X - _ax) - _ox, dy = (ex.Y - _ay) - _oy, dz = (ex.Z - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (rs * 600 / Math.Max(1e-30, d) * h < 2) continue;                      // (too small on screen to show yet)
                if (_bhDisc == null) MakeBlackHoleParts();
                float sc = (float)rs, spin = (float)(time * 0.6 + ex.Turn), ct = (float)Math.Cos(ex.Tilt), st = (float)Math.Sin(ex.Tilt);
                var m = Mat4.RotationY(spin); var tilt = Mat4.Identity; tilt.M22 = ct; tilt.M23 = st; tilt.M32 = -st; tilt.M33 = ct;
                var scale = Mat4.Identity; scale.M11 = sc; scale.M22 = sc; scale.M33 = sc;
                var place = m * tilt * scale * Mat4.Translation((float)dx, (float)dy, (float)dz);
                DrawPoints(_bhDisc, _bhDiscN, place * vp, right, up, 1f, 0.0012f / sc, w, h, time, 0.02f / sc, 1f, 2f);   // (sizes in its own units: the placing scales them, and the view-distance limits are in its units too)
                if (ex.Feeding == 2) DrawPoints(_bhJets, _bhJetsN, tilt * scale * Mat4.Translation((float)dx, (float)dy, (float)dz) * vp, right, up, 1f, 0.0012f / sc, w, h, time, 0.02f / sc, 1f, 2f);
            }
        }
        // -- asteroids and comets close in: a lumpy cratered rock lit from its star's side, made once (radius about 1) and drawn for
        // each, turned to its star and spinning on that line (so its lit side stays lit); a comet's dark nucleus in its coma, with
        // its ion tail straight away from the star and its wider curved dust tail --
        private UploadBuffer _rockBuf, _cometBuf, _nucBuf, _roundBuf; private int _rockN, _cometN, _nucN, _roundN;
        private bool ShapeShown(Extra ex, int h)
        {
            double dx = (ex.X - _ax) - _ox, dy = (ex.Y - _ay) - _oy, dz = (ex.Z - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double reach = ex.Shape == "comet" ? 2000 : 1;                                  // (a comet's coma shows from much farther)
            return ex.R * reach / Math.Max(1e-30, d) * h > 3;
        }
        private static void RockPoints(List<StarVertex> l, Rng rng, int n, float r0, float g0, float b0, double albedo, double lump = 1)
        {
            var bumps = new double[12][]; for (int q = 0; q < 12; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); bumps[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), (rng.NextDouble() - 0.5) * 0.5 * lump, 1 + rng.NextDouble() * 2.5 }; }
            var craters = new double[30][]; for (int q = 0; q < 30; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); craters[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), Math.Cos(0.08 + rng.NextDouble() * 0.3) }; }
            for (int k = 0; k < n; k++)
            {
                double u = (k + 0.5) / n * 2 - 1, a = k * 2.399963229728653, rr = Math.Sqrt(1 - u * u), x = rr * Math.Cos(a), y = u, z = rr * Math.Sin(a);
                double rad = 1; foreach (var bq in bumps) rad += bq[3] * Math.Pow(Math.Max(0, x * bq[0] + y * bq[1] + z * bq[2]), bq[4]);
                double shade = 1;
                foreach (var cq in craters) { double c = x * cq[0] + y * cq[1] + z * cq[2]; if (c > cq[3]) { double t = (c - cq[3]) / (1 - cq[3]); rad -= 0.05 * (1 - t); shade *= 0.75 + 0.25 * t; } else if (c > cq[3] - 0.02) shade *= 1.15; }
                double sx = x * (1 + 0.5 * lump), sy = y * (1 - 0.15 * lump), sz = z;          // (elongated, as small bodies are: too little gravity to pull them round)
                double lit = Math.Max(0, x * 0.9 + 0.1) * shade * albedo + 0.015;              // its star is along +x
                l.Add(new StarVertex { X = (float)(sx * rad), Y = (float)(sy * rad), Z = (float)(sz * rad), Size = 0.045f, R = (float)(r0 * lit), G = (float)(g0 * lit), B = (float)(b0 * lit), A = 1 });
            }
        }
        private void MakeSmallBodies()
        {
            var rock = new List<StarVertex>(); RockPoints(rock, new Rng(0x5A57E1UL), 30000, 0.75f, 0.68f, 0.6f, 1.0);
            _rockN = rock.Count; _rockBuf = new UploadBuffer(_gpu.Device, (long)_rockN * Stride); _rockBuf.Write(rock.ToArray(), _rockN);
            // a body big enough (a few hundred km across) for its own gravity to pull it round: a cratered globe
            var round = new List<StarVertex>(); RockPoints(round, new Rng(0x60BE5UL), 40000, 0.75f, 0.7f, 0.66f, 1.0, 0.08);
            _roundN = round.Count; _roundBuf = new UploadBuffer(_gpu.Device, (long)_roundN * Stride); _roundBuf.Write(round.ToArray(), _roundN);
            var cm = new List<StarVertex>(); var rng = new Rng(0xC0CE7UL); RockPoints(cm, rng, 20000, 0.5f, 0.48f, 0.46f, 0.35);   // a comet's nucleus: among the darkest things known
            _nucN = cm.Count; _nucBuf = new UploadBuffer(_gpu.Device, (long)_nucN * Stride); _nucBuf.Write(cm.ToArray(), _nucN); cm.Clear();
            for (int k = 0; k < 9000; k++)
            {
                // the coma: gas and dust round the nucleus, densest close in, out to about 2000 times its size
                double rr = Math.Exp(Math.Log(1.5) + rng.NextDouble() * Math.Log(2000 / 1.5)), u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, s2 = Math.Sqrt(1 - u * u);
                float br = (float)(0.5 * Math.Pow(1.5 / rr, 0.35));
                cm.Add(new StarVertex { X = (float)(rr * s2 * Math.Cos(a) + rr * 0.3), Y = (float)(rr * u), Z = (float)(rr * s2 * Math.Sin(a)), Size = (float)(rr * 0.35), R = 0.55f * br, G = 0.75f * br, B = 0.8f * br, A = 1 });
            }
            for (int k = 0; k < 9000; k++)
            {
                // the ion tail: straight away from the star (along -x), thin, blue; the dust tail: wider, curved, yellow-white
                bool ion = (k & 1) == 0; double t = Math.Pow(rng.NextDouble(), 0.7) * 60000, w = (ion ? 0.02 : 0.08) * t + 200, a = rng.NextDouble() * Math.PI * 2, q = rng.Gaussian() * 0.4;
                float br = (float)((ion ? 0.35 : 0.3) * Math.Exp(-t / 30000));
                double bend = ion ? 0 : t * t / 60000 * 0.25;
                cm.Add(new StarVertex { X = (float)(-t), Y = (float)(Math.Cos(a) * w * q), Z = (float)(Math.Sin(a) * w * q + bend), Size = (float)(w * 0.8), R = (ion ? 0.35f : 0.9f) * br, G = (ion ? 0.55f : 0.85f) * br, B = (ion ? 1f : 0.7f) * br, A = 1 });
            }
            _cometN = cm.Count; _cometBuf = new UploadBuffer(_gpu.Device, (long)_cometN * Stride); _cometBuf.Write(cm.ToArray(), _cometN);
        }
        private void DrawSmallBodies(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 || !ShapeShown(ex, h)) continue;
                if (_rockBuf == null) MakeSmallBodies();
                double bx = ex.X - _ax, by = ex.Y - _ay, bz = ex.Z - _az, dx = bx - _ox, dy = by - _oy, dz = bz - _oz, bl = Math.Sqrt(bx * bx + by * by + bz * bz);
                // its frame: +x toward its star (the anchor of the system you are in), spinning about that line
                var xs = bl > 0 ? new Vec3((float)(-bx / bl), (float)(-by / bl), (float)(-bz / bl)) : new Vec3(1, 0, 0);
                var ys = Vec3.Cross(xs, Math.Abs(xs.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var zs = Vec3.Cross(xs, ys).Normalized;
                double spin = time * 0.15 + ex.Turn; float c = (float)Math.Cos(spin), sn = (float)Math.Sin(spin);
                var y2 = new Vec3(ys.X * c + zs.X * sn, ys.Y * c + zs.Y * sn, ys.Z * c + zs.Z * sn); var z2 = Vec3.Cross(xs, y2);
                if (ex.Shape == "comet") { y2 = ys; z2 = zs; }                                  // (its tails do not spin)
                float sc = (float)ex.R;
                var m = Mat4.Identity;
                m.M11 = xs.X * sc; m.M12 = xs.Y * sc; m.M13 = xs.Z * sc;
                m.M21 = y2.X * sc; m.M22 = y2.Y * sc; m.M23 = y2.Z * sc;
                m.M31 = z2.X * sc; m.M32 = z2.Y * sc; m.M33 = z2.Z * sc;
                m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                if (ex.Shape == "comet") DrawPoints(_nucBuf, _nucN, m * vp, right, up, 1f, 0.0008f / sc, w, h, time, 0f, 1f, 2f);
                if (ex.Shape == "comet") DrawPoints(_cometBuf, _cometN, m * vp, right, up, 1f, 0.0008f / sc, w, h, time, 0f, 1f, 0f);   // (its own units: the placing scales them; the coma and tails soft glows)
                else DrawPoints(Units.Km(1) > 0 && ex.R / Units.Km(1) > 200 ? _roundBuf : _rockBuf, ex.R / Units.Km(1) > 200 ? _roundN : _rockN, m * vp, right, up, 1f, 0.0008f / sc, w, h, time, 0f, 1f, 2f);
            }
        }
        private UploadBuffer _extraBuf; private StarVertex[] _extraV = new StarVertex[0]; private readonly float[] _lens = new float[4], _web = new float[5];
        private void LoadPlaces()
        {
            _places = new List<Place>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("bodies.dnat", "place"))
            {
                double v; var pl = new Place { Id = e.Id, Name = e.Name, Where = e.Get("where", ""), Entry = e.Get("entry", ""), Says = e.Get("says", ""), Shape = e.Get("shape", "") };
                if (double.TryParse(e.Get("per-cubic-ly", "0"), fs, ci, out v)) pl.PerLy3 = v; if (double.TryParse(e.Get("feeding-share", "0"), fs, ci, out v)) pl.FeedShare = v; if (double.TryParse(e.Get("jets-share", "0"), fs, ci, out v)) pl.JetShare = v; if (double.TryParse(e.Get("size-km", "1000"), fs, ci, out v)) pl.SizeKm = v;
                int n; if (int.TryParse(e.Get("shown", "0"), out n)) pl.Shown = n;
                var c = e.Get("colour", "1 1 1").Split(' '); float r = 1, g = 1, b = 1; if (c.Length == 3) { float.TryParse(c[0], fs, ci, out r); float.TryParse(c[1], fs, ci, out g); float.TryParse(c[2], fs, ci, out b); }
                pl.R = r; pl.G = g; pl.B = b; _places.Add(pl);
            }
        }
        private Extra MakeExtra(Place pl, double x, double y, double z)
        {
            var ex = new Extra { X = x, Y = y, Z = z, R = Units.Km(pl.SizeKm * (pl.Shape == "rock" ? Math.Exp(-1.6 + 3.2 * Math.Pow(Math.Abs(Math.Sin(x * 3.1 + y * 5.7 + z * 1.9) * 7919.1) % 1, 2.5)) : 1)), Name = pl.Name, Shape = pl.Shape ?? "", Turn = (float)(Math.Abs(Math.Sin(x * 7.1 + z * 3.3) * 9187.3) % (Math.PI * 2)), Cr = pl.R, Cg = pl.G, Cb = pl.B, Lines = new List<string>() };
            ex.Lines.Add(pl.Name); if (pl.Says.Length > 0) ex.Lines.Add(pl.Says);
            var en = pl.Entry.Split(' ');
            if (en.Length == 2) foreach (var e in _content.Entries) if (e.Kind == en[0] && e.Id == en[1])
            {
                if (e.Props.ContainsKey("mass-sol"))
                {
                    // its own mass, from the range its kind has (spread evenly in scale), from its place
                    var rr = e.Get("mass-sol", "").Split(new[] { ".." }, StringSplitOptions.None); double lo, hi; var ci2 = System.Globalization.CultureInfo.InvariantCulture;
                    if (rr.Length >= 1 && double.TryParse(rr[0], System.Globalization.NumberStyles.Float, ci2, out lo))
                    {
                        if (rr.Length < 2 || !double.TryParse(rr[1], System.Globalization.NumberStyles.Float, ci2, out hi)) hi = lo * 10;
                        double u = Math.Abs(Math.Sin(x * 12.9898 + y * 78.233 + z * 37.719) * 43758.5453) % 1;
                        ex.Mass = Math.Exp(Math.Log(lo) + (Math.Log(hi) - Math.Log(lo)) * u);
                    }
                    ex.Lines.Add("mass: " + (ex.Mass > 0 ? ex.Mass.ToString(ex.Mass >= 1e4 ? "0.##E+0" : "0.#") : e.Get("mass-sol", "")) + " times the Sun's");
                }
                if (e.Kind == "star" && e.Id == "neutron-star" && ex.Mass > 0)
                {
                    // a neutron star is an atom's nucleus the size of a city: its mass in a ball about 10 km across its middle, as dense
                    // as nuclear matter (about 2.3e17 kg a cubic metre), made almost all of neutrons pressed together
                    double rm = pl.SizeKm * 500, kg = ex.Mass * 1.989e30, rho = kg / (4.0 / 3 * Math.PI * rm * rm * rm);
                    ex.Lines.Add("density: " + rho.ToString("0.#E+0") + " kg a cubic metre, " + (rho / 2.3e17).ToString("0.#") + " times an atom's nucleus");
                    ex.Lines.Add("made of: neutrons pressed together, as in a nucleus; a teaspoon would weigh " + (rho * 5e-6 / 1e12).ToString("0.#") + " billion tonnes");
                }
                if (e.Kind == "blackhole" && ex.Mass > 0)
                {
                    // a black hole is its mass: its event horizon the Schwarzschild radius 2GM/c^2 (2.95 km a Sun), the shadow round it
                    // about 2.6 times that, the photon ring where light circles it
                    ex.Hole = true; double rs = 2.953 * ex.Mass; ex.R = Units.Km(rs * 2.6);
                    ex.Lines.Add("event horizon: " + ScaleHud.Words(rs * 2) + " across");
                    ex.Lines.Add("shadow: " + ScaleHud.Words(rs * 5.2) + " across");
                    // feeding or quiet, by its kind's shares (content/bodies.dnat), from its place
                    double u2 = Math.Abs(Math.Sin(x * 3.7 + y * 91.1 + z * 13.3) * 24634.6345) % 1, u3 = Math.Abs(Math.Sin(x * 17.9 + y * 5.3 + z * 47.1) * 15731.743) % 1;
                    ex.Feeding = u2 < pl.FeedShare ? (u3 < pl.JetShare ? 2 : 1) : 0;
                    ex.Tilt = (float)(u3 * Math.PI); ex.Turn = (float)(u2 * 37 % (Math.PI * 2));
                    ex.Lines.Add(ex.Feeding == 0 ? "quiet: not feeding, only its shadow and the light it bends" : ex.Feeding == 1 ? "feeding: a disc of gas spiralling in, heated to millions of degrees" : "feeding, and firing jets from its poles at nearly the speed of light");
                }
                if (e.Props.ContainsKey("made-by")) ex.Lines.Add("made by: " + e.Get("made-by", ""));
                if (e.Props.ContainsKey("example")) ex.Lines.Add("for example: " + e.Get("example", ""));
                break;
            }
            return ex;
        }
        // the bodies round you, made again when where you are changes enough (each always the same, from its own place)
        private void UpdateExtras()
        {
            if (_places == null) LoadPlaces();
            bool inGalaxy = _near >= 0 && NearShare() > 0.5, between = !inGalaxy && (_near < 0 || NearShare() < 0.05);
            double lyU = 1.0 / Units.LightYearsPerUnit;
            string key = (inGalaxy ? "g" : between ? "b" : "-") + (_system != null ? "s" + _system.StarName : "") + ":" + (long)Math.Floor(_cx / 0.02) + "," + (long)Math.Floor(_cy / 0.02) + "," + (long)Math.Floor(_cz / 0.02) + ":" + _galaxies.Count;
            if (key == _extraKey) return; _extraKey = key; _extras.Clear();
            foreach (var pl in _places)
            {
                if (pl.Where == "galaxy-heart")
                {
                    foreach (var g in _galaxies) { double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz; if (dx * dx + dy * dy + dz * dz < Math.Pow(60 * g.Scale, 2)) _extras.Add(MakeExtra(pl, g.X, g.Y, g.Z)); }
                }
                else if ((pl.Where == "galaxy" && inGalaxy) || (pl.Where == "between-galaxies" && between))
                {
                    if (pl.PerLy3 <= 0) continue;
                    // about two dozen of each round you: the reach that holds them at this density, in cells each from its own place
                    double reachLy = Math.Pow(24 / (4.0 / 3 * Math.PI * pl.PerLy3), 1 / 3.0), cellLy = reachLy / 2, cell = cellLy * lyU;
                    long ci0 = (long)Math.Floor(_cx / cell), cj0 = (long)Math.Floor(_cy / cell), ck0 = (long)Math.Floor(_cz / cell);
                    for (long i = ci0 - 2; i <= ci0 + 2; i++) for (long j = cj0 - 2; j <= cj0 + 2; j++) for (long k = ck0 - 2; k <= ck0 + 2; k++)
                    {
                        var rng = new Rng((ulong)(i * 73856093L ^ j * 19349663L ^ k * 83492791L) ^ _seed ^ (ulong)pl.Id.GetHashCode());
                        double lambda = pl.PerLy3 * cellLy * cellLy * cellLy; int n = (int)Math.Floor(lambda) + (rng.NextDouble() < lambda - Math.Floor(lambda) ? 1 : 0);
                        for (int q = 0; q < n && q < 8; q++) _extras.Add(MakeExtra(pl, (i + rng.NextDouble()) * cell, (j + rng.NextDouble()) * cell, (k + rng.NextDouble()) * cell));
                    }
                }
                else if (_system != null && (pl.Where == "belt" || pl.Where == "kuiper" || pl.Where == "oort" || pl.Where == "inner"))
                {
                    double a0 = pl.Where == "belt" ? _system.BeltInAu : pl.Where == "kuiper" ? _system.KuiperInAu : pl.Where == "inner" ? 0.8 : _system.OortInAu, a1 = pl.Where == "belt" ? _system.BeltOutAu : pl.Where == "kuiper" ? _system.KuiperOutAu : pl.Where == "inner" ? 3.0 : _system.OortOutAu;
                    if (a1 <= 0) continue;
                    var rng = new Rng(_seed ^ (ulong)pl.Id.GetHashCode() ^ (ulong)_system.StarName.GetHashCode());
                    for (int q = 0; q < pl.Shown; q++)
                    {
                        double au = a0 + (a1 - a0) * rng.NextDouble(), ang = rng.NextDouble() * Math.PI * 2, u = pl.Where == "oort" ? rng.NextDouble() * 2 - 1 : (rng.NextDouble() - 0.5) * 0.1, rr = Math.Sqrt(1 - u * u);
                        double d = Units.Km(au * Units.KmPerAu);
                        _extras.Add(MakeExtra(pl, _ax + Math.Cos(ang) * rr * d, _ay + u * d, _az + Math.Sin(ang) * rr * d));
                    }
                }
            }
        }

        private void LookAt(double x, double y, double z)
        {
            double dx = x - _cx, dy = y - _cy, dz = z - _cz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l > 0) SetLook(dx, dy, dz);
        }
        private void SetLook(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z); if (l <= 0 || double.IsNaN(l)) return;
            _fx = x / l; _fy = y / l; _fz = z / l;
        }
        private void SetLook(Vec3 f) { SetLook(f.X, f.Y, f.Z); }
        // turns the view a share k of the way to a direction, round the shortest way (never through angles)
        private void EaseLook(double wx, double wy, double wz, double k)
        {
            double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz); if (wl <= 0) return; wx /= wl; wy /= wl; wz /= wl;
            var f = Look; double d = Math.Max(-1, Math.Min(1, f.X * wx + f.Y * wy + f.Z * wz)), a = Math.Acos(d);
            if (a < 1e-7) { SetLook(wx, wy, wz); return; }
            var axis = Vec3.Cross(f, new Vec3((float)wx, (float)wy, (float)wz)); if (axis.Length < 1e-6f) axis = _camUp;
            SetLook(Turn(f, axis.Normalized, a * k));
        }

        private Vec3 Look { get { return new Vec3((float)_fx, (float)_fy, (float)_fz); } }

        // a galaxy's own frame placed in the universe: its size, tilt and turn (as the universe always placed them)
        private static void ToUniverse(Galaxy g, double x, double y, double z, out double ux, out double uy, out double uz)
        {
            x *= g.Scale; y *= g.Scale; z *= g.Scale;
            double ct = Math.Cos(g.Tilt), st = Math.Sin(g.Tilt), cu = Math.Cos(g.Turn), su = Math.Sin(g.Turn);
            double y2 = y * ct - z * st, z2 = y * st + z * ct;
            ux = x * cu - z2 * su + g.X; uy = y2 + g.Y; uz = x * su + z2 * cu + g.Z;
        }

        // the same placing as a matrix, relative to the camera (the translation is the galaxy's centre minus the camera, in double first)
        private Mat4 Placement(Galaxy g)
        {
            float ct = (float)Math.Cos(g.Tilt), st = (float)Math.Sin(g.Tilt), cu = (float)Math.Cos(g.Turn), su = (float)Math.Sin(g.Turn), s = g.Scale;
            var m = Mat4.Identity;
            // rows: where the galaxy's x, y and z axes go (row vectors, as the rest of the renderer uses)
            // worked from ToUniverse: x' = x·cu − (y·st + z·ct)·su, y' = y·ct − z·st, z' = x·su + (y·st + z·ct)·cu
            m.M11 = cu * s; m.M12 = 0; m.M13 = su * s;
            m.M21 = -st * su * s; m.M22 = ct * s; m.M23 = st * cu * s;
            m.M31 = -ct * su * s; m.M32 = -st * s; m.M33 = ct * cu * s;
            m.M41 = (float)((g.X - _ax) - _ox); m.M42 = (float)((g.Y - _ay) - _oy); m.M43 = (float)((g.Z - _az) - _oz);
            return m;
        }

        // how close the nearest thing is (universe units): a star of the near galaxy, or the edge of the nearest galaxy
        private double NearestDistance()
        {
            if (_matterScale > 0) return Units.Km(_matterScale) * 0.5;
            double best = double.MaxValue;
            foreach (var g in _galaxies)
            {
                double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - g.Scale * 1.0;
                best = Math.Min(best, Math.Max(d, g.Scale * 0.02));
            }
            if (_system != null) best = Math.Min(best, PlaceSystem());
            if (_ground != null) best = Math.Min(best, AboveGround());
            if (_near >= 0 && _nearLocal != null) { double ds; if (NearestStar(out ds) >= 0) best = Math.Min(best, ds); }
            return best;
        }

        private static int Cell(double v) { return (int)Math.Floor((v + 1.6) / 3.2 * GridN); }

        // ── the stars of the space you are in ──
        // A real galaxy holds about a hundred billion stars; its sample shows 150,000. Around the camera, the real ones are made:
        // space is cut into cubes about 25 light-years across, and each cube near you gets the stars the galaxy's density gives it
        // there (from how many sample stars share its big grid cell), placed from the cube's own position, so they are always the
        // same stars when you come back.
        private const double RealStars = 1e11;
        private static readonly double LocalCell = 25 / 50000.0;           // 25 light-years, in galaxy units
        private readonly Dictionary<long, StarVertex[]> _localCells = new Dictionary<long, StarVertex[]>();
        private readonly List<StarVertex> _localList = new List<StarVertex>();   // in the near galaxy's own frame
        private readonly List<int> _localKind = new List<int>(); private readonly List<long> _localId = new List<long>();
        private UploadBuffer _localBuf; private StarVertex[] _localDrawn = new StarVertex[0]; private int _localCount;
        private long _localKey = long.MinValue; private long _localCX, _localCY, _localCZ;

        // the galaxy's density of real stars at a point of its own frame (stars per galaxy unit cubed)
        private double Density(double lx, double ly, double lz)
        {
            int x = Cell(lx), y = Cell(ly), z = Cell(lz);
            if (x < 0 || y < 0 || z < 0 || x >= GridN || y >= GridN || z >= GridN) return 0;
            int n = 0; for (int k = _gridHead[(x * GridN + y) * GridN + z]; k >= 0; k = _gridNext[k]) n++;
            double cellSize = 3.2 / GridN;
            return n / (double)NearStars * RealStars / (cellSize * cellSize * cellSize);
        }

        // refresh the local stars when the camera moves into another cube
        private void LocalStars()
        {
            if (_near < 0 || _nearLocal == null) { _localCount = 0; return; }
            double lx, ly, lz; ToGalaxy(_galaxies[_near], _cx, _cy, _cz, out lx, out ly, out lz);
            long cx = (long)Math.Floor(lx / LocalCell), cy = (long)Math.Floor(ly / LocalCell), cz = (long)Math.Floor(lz / LocalCell);
            long key = (cx * 73856093L) ^ (cy * 19349663L) ^ (cz * 83492791L);
            if (key == _localKey) return;
            // flying through faster than a cube or two a frame (zooming out of the galaxy, crossing it): its real stars are not made
            // (they would be gone before they were seen, and making them every frame stalls it); the galaxy's own stars show it
            bool fast = _localKey != long.MinValue && (Math.Abs(cx - _localCX) > 2 || Math.Abs(cy - _localCY) > 2 || Math.Abs(cz - _localCZ) > 2);
            _localCX = cx; _localCY = cy; _localCZ = cz;
            if (fast) { _localKey = long.MinValue + 1; _localCount = 0; _localCells.Clear(); return; }
            _localKey = key;
            _localList.Clear(); _localKind.Clear(); _localId.Clear();
            var keep = new Dictionary<long, StarVertex[]>();
            for (long ox = -1; ox <= 1; ox++) for (long oy = -1; oy <= 1; oy++) for (long oz = -1; oz <= 1; oz++)
            {
                long x = cx + ox, y = cy + oy, z = cz + oz, id = (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
                StarVertex[] cell;
                if (!_localCells.TryGetValue(id, out cell))
                {
                    double vol = LocalCell * LocalCell * LocalCell, expect = Density((x + 0.5) * LocalCell, (y + 0.5) * LocalCell, (z + 0.5) * LocalCell) * vol;
                    var rng = new Rng((ulong)id ^ _galaxies[_near].Seed);
                    int count = (int)Math.Min(4000, Math.Floor(expect + rng.NextDouble()));
                    cell = new StarVertex[count];
                    for (int k = 0; k < count; k++)
                    {
                        var kind = PickKind(rng);
                        var kc = _kinds[kind];
                        cell[k] = new StarVertex { X = (float)((x + rng.NextDouble()) * LocalCell), Y = (float)((y + rng.NextDouble()) * LocalCell), Z = (float)((z + rng.NextDouble()) * LocalCell),
                            Size = (float)Units.Km(696000), R = kc.R, G = kc.G, B = kc.B, A = kind };      // A carries the kind's index
                    }
                }
                keep[id] = cell;
                for (int k = 0; k < cell.Length; k++) { _localList.Add(cell[k]); _localKind.Add((int)cell[k].A); _localId.Add(id * 4096 + k); }
            }
            _localCells.Clear(); foreach (var kv in keep) _localCells[kv.Key] = kv.Value;
            _localCount = _localList.Count;
            if (_localDrawn.Length < _localCount) _localDrawn = new StarVertex[_localCount + 1024];
            if (_localBuf == null || _localBuf.Bytes < (long)_localCount * Stride) { if (_localBuf != null) _localBuf.Dispose(); _localBuf = new UploadBuffer(_gpu.Device, (long)(_localCount + 1024) * Stride * 2); }
        }

        // a star kind by how common each is (the same shares the galaxy generator uses)
        private int PickKind(Rng rng)
        {
            double total = 0; foreach (var k in _kinds) total += k.Weight;
            double r = rng.NextDouble() * total;
            for (int i = 0; i < _kinds.Count; i++) { r -= _kinds[i].Weight; if (r <= 0) return i; }
            return 0;
        }

        // the near galaxy's star nearest the camera (index, and distance in universe units), or -1
        private int NearestStar(out double dist)
        {
            dist = double.MaxValue; if (_near < 0 || _nearLocal == null) return -1;
            double lx, ly, lz; ToGalaxy(_galaxies[_near], _cx, _cy, _cz, out lx, out ly, out lz);
            // the real stars of this space (index from 1,000,000 up), then the galaxy's sample stars
            int bestL = -1; double bl = double.MaxValue;
            for (int k = 0; k < _localCount; k++) { var s0 = _localList[k]; double dx = s0.X - lx, dy = s0.Y - ly, dz = s0.Z - lz, d = dx * dx + dy * dy + dz * dz; if (d < bl) { bl = d; bestL = k; } }
            int gx = Cell(lx), gy = Cell(ly), gz = Cell(lz), best = -1; double bd = double.MaxValue;
            for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) for (int oz = -1; oz <= 1; oz++)
            {
                int x = gx + ox, y = gy + oy, z = gz + oz;
                if (x < 0 || y < 0 || z < 0 || x >= GridN || y >= GridN || z >= GridN) continue;
                for (int k = _gridHead[(x * GridN + y) * GridN + z]; k >= 0; k = _gridNext[k])
                {
                    var s = _nearLocal[k]; double dx = s.X - lx, dy = s.Y - ly, dz = s.Z - lz, d = dx * dx + dy * dy + dz * dz;
                    if (d < bd) { bd = d; best = k; }
                }
            }
            if (bestL >= 0 && bl < bd) { dist = Math.Sqrt(bl) * _galaxies[_near].Scale; return 1000000 + bestL; }
            if (best >= 0) dist = Math.Sqrt(bd) * _galaxies[_near].Scale;
            // a star cluster's own stars round you (index from 2,000,000 up), so each can open into its solar system too
            for (int q = 0; q < _cstars.Count; q++)
            {
                var cs = _cstars[q]; double dx = cs[0] - _cx, dy = cs[1] - _cy, dz = cs[2] - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < dist) { dist = d; best = 2000000 + q; }
            }
            return best;
        }

        // where a star of the near galaxy sits in the universe
        private void StarPos(int k, out double x, out double y, out double z)
        {
            if (k >= 2000000) { var cs = _cstars[k - 2000000]; x = cs[0]; y = cs[1]; z = cs[2]; return; }   // a cluster's star: already in universe units
            var s = k >= 1000000 ? _localList[k - 1000000] : _nearLocal[k]; ToUniverse(_galaxies[_near], s.X, s.Y, s.Z, out x, out y, out z);
        }
        // a cluster star's kind: in a globular, red giants, orange K and yellow G stars; in an open cluster, blue-white B and white A stars
        private int ClusterKind(double[] cs)
        {
            string id = cs[3] == 0 ? (cs[4] < 0.1 ? "red-giant" : cs[4] < 0.4 ? "k-type" : "g-type") : (cs[4] < 0.3 ? "b-type" : "a-type");
            for (int i = 0; i < _kinds.Count; i++) if (_kinds[i].Id == id) return i;
            return 0;
        }
        private int StarKindOf(int k) { return k >= 2000000 ? ClusterKind(_cstars[k - 2000000]) : k >= 1000000 ? _localKind[k - 1000000] : _nearKinds[k]; }
        private ulong StarSeed(int k) { if (k >= 2000000) { var cs = _cstars[k - 2000000]; return (ulong)(long)(cs[0] / Units.Km(1e6)) * 73856093UL ^ (ulong)(long)(cs[2] / Units.Km(1e6)) * 19349663UL ^ 0xC1C1UL; } return k >= 1000000 ? (ulong)_localId[k - 1000000] : (ulong)k; }

        // open the system of the star you have come close to (and anchor the camera on it), or close it when you leave
        private void ChooseSystem()
        {
            double d; int k = NearestStar(out d);
            if (_system != null && k >= 0 && k != _sysStar)
            {
                // the same star can come back under a new number as the stars around you are made again: it is another
                // star only if it sits somewhere else (more than an AU from this system's star, the anchor)
                double sx0, sy0, sz0; StarPos(k, out sx0, out sy0, out sz0);
                if (Math.Abs(sx0 - _ax) + Math.Abs(sy0 - _ay) + Math.Abs(sz0 - _az) < Units.Au(1)) _sysStar = k;
            }
            if (_system != null && (k != _sysStar && d < SystemRange * 0.5 || DistToSysStar() > SystemRange * 1.5))
            {
                ZoomLog("SYSTEM CLOSED: " + (DistToSysStar() > SystemRange * 1.5 ? "left it, " + Units.Describe(DistToSysStar()) + " from its star" : "another star " + k + " (was " + _sysStar + ") within " + Units.Describe(d)));
                CloseSystem();
            }
            if (_system == null && k >= 0 && d < SystemRange)
            {
                _sysStar = k; ZoomLog("SYSTEM OPENED: star " + k + " at " + Units.Describe(d));
                var kind = _kinds[Math.Max(0, Math.Min(_kinds.Count - 1, StarKindOf(k)))];
                _system = SpaceGen.System(_content, kind, _galaxies[_near].Seed * 31UL + StarSeed(k));
                _bodyVerts = new StarVertex[_system.Bodies.Count + 1];
                _sysBodies = new UploadBuffer(_gpu.Device, (long)_bodyVerts.Length * Stride);
                _staticShifted = new StarVertex[_system.Static.Length];
                _sysStatic = new UploadBuffer(_gpu.Device, (long)Math.Max(1, _system.Static.Length) * Stride);
                double sx, sy, sz; StarPos(k, out sx, out sy, out sz); Anchor(sx, sy, sz);
                // the star's own body: its plasma surface and corona, in its kind's colours (as the star close-ups are made)
                string sname; _starCorona = StarGen.Build(_content, kind.Id, _galaxies[_near].Seed * 77UL + StarSeed(k), out sname, out _starSurf);
                _starSurfVerts = new StarVertex[_starSurf.Normals.Length];
                _coronaBuf = new UploadBuffer(_gpu.Device, (long)Math.Max(1, _starCorona.Length) * Stride); if (_starCorona.Length > 0) _coronaBuf.Write(_starCorona, _starCorona.Length);
                _surfBuf = new UploadBuffer(_gpu.Device, (long)_starSurfVerts.Length * Stride);
            }
            if (_system == null && _near >= 0)
            {
                // between the stars the camera is measured from the galaxy's middle, except when flying to a point in a nebula or a
                // cluster: then from that point itself, as from a star or planet (from tens of thousands of light-years away the
                // finest step a number can hold is tens of kilometres, and the zoom would stall)
                if (_tKind == 5) { if (_ax != _tx || _ay != _ty || _az != _tz) Anchor(_tx, _ty, _tz); }
                else { var g = _galaxies[_near]; if (Math.Abs(_ax - g.X) + Math.Abs(_ay - g.Y) + Math.Abs(_az - g.Z) > 0) Anchor(g.X, g.Y, g.Z); }
            }
        }

        private double DistToSysStar() { return Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz); }    // anchored on the star: the offset is the distance

        private void CloseSystem()
        {
            DropPlanet(); _hadPos = false; _followBody = -1;                       // a new system's bodies are new: ride along afresh
            _system = null; _sysStar = -1;
            if (_sysStatic != null) { _sysStatic.Dispose(); _sysStatic = null; }
            if (_sysBodies != null) { _sysBodies.Dispose(); _sysBodies = null; }
            if (_coronaBuf != null) { _coronaBuf.Dispose(); _coronaBuf = null; }
            if (_surfBuf != null) { _surfBuf.Dispose(); _surfBuf = null; }
            _starSurf = null;
        }

        // a planet or moon's centre from its star, in double precision
        private void BodyPos(int i, out double x, out double y, out double z)
        {
            var tmp = new StarVertex[_system.Bodies.Count + 1]; double fx, fy, fz;
            SpaceGen.BodyVerts(_system, _simYears, 1 / 20.0, 0.05, tmp, i, 1.0, out fx, out fy, out fz);
            x = fx; y = fy; z = fz;
        }

        // the camera from the planet's centre, in km
        private void CamFromPlanet(out double x, out double y, out double z)
        {
            double k = Units.Km(1); x = (_ox - _px) / k; y = (_oy - _py) / k; z = (_oz - _pz) / k;
        }

        // make the ground of the body you have come close to, or let it go once you leave
        private void ChoosePlanet()
        {
            if (_system == null) { DropPlanet(); return; }
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _system.Bodies.Count; i++)
            {
                double x, y, z; BodyPos(i, out x, out y, out z);
                double d = Math.Sqrt((x - _ox) * (x - _ox) + (y - _oy) * (y - _oy) + (z - _oz) * (z - _oz)) / _system.Bodies[i].RadiusUnits;
                if (d < bd) { bd = d; best = i; }
            }
            _cpBest = best; _cpBd = bd;
            if (bd > PlanetRange * 1.2) { DropPlanet(); return; }
            if (best != _planet && bd < PlanetRange)
            {
                DropPlanet();
                var b = _system.Bodies[best];
                _planet = best;
                _ground = new PlanetTerrain(_gpu.Device, DeepView.TerrainKind(b.Kind), b.Kind, (uint)(_galaxies[_near].Seed * 977UL + (ulong)_sysStar * 31UL + (ulong)best), false, b.RadiusUnits / Units.Km(1)) { MixId = b.Kind == "Moon" ? "moon" : null };   // (a moon: its own regolith grains)
            }
            if (_planet >= 0) BodyPos(_planet, out _px, out _py, out _pz);
        }

        // the camera rides along with the planet or moon you picked (or are at) as it goes round its star
        private void FollowBody()
        {
            int follow = _tKind == 2 && _system != null && _tIndex < _system.Bodies.Count ? _tIndex : _planet;
            if (follow < 0 && _system != null)
            {
                // nothing picked: the nearest planet or moon is matched once you are within about 300 of its radii
                double bd = double.MaxValue;
                for (int i = 0; i < _system.Bodies.Count; i++)
                {
                    double x, y, z; BodyPos(i, out x, out y, out z);
                    double d = Math.Sqrt((x - _ox) * (x - _ox) + (y - _oy) * (y - _oy) + (z - _oz) * (z - _oz)) / _system.Bodies[i].RadiusUnits;
                    if (d < bd) { bd = d; follow = i; }
                }
                if (bd > 300) follow = -1;
            }
            if (follow >= 0)
            {
                double fx, fy, fz; BodyPos(follow, out fx, out fy, out fz);
                if (_hadPos && follow == _followBody) { _ox += fx - _fbx; _oy += fy - _fby; _oz += fz - _fbz; }
                _fbx = fx; _fby = fy; _fbz = fz; _followBody = follow; _hadPos = true;
            }
            else { _hadPos = false; _followBody = -1; }
        }
        private int _followBody = -1; private double _fbx, _fby, _fbz;

        private bool _hadPos;
        private int _cpBest = -1; private double _cpBd;                         // for the log: the nearest body in its own radii
        private void DropPlanet() { if (_ground != null) { _ground.Dispose(); _ground = null; } _planet = -1; }

        // the top of what is under a direction, in km from the planet's radius: the sea's surface where there is sea, else the
        // real ground (a dry basin below sea level is ground, not water)
        private double Surface(double x, double y, double z)
        {
            double h = _ground.GroundAt(x, y, z, _surfaceCell);                           // the grains only as finely as your height needs
            return h < 0 && _ground.HasWater ? 0 : h;
        }

        // what the matter under you is made of, set once for each spot (about a metre across), so its grains stay put
        private double _matterTop = MatterView.TopKm;
        private double _spaceScale, _spaceTop;
        private double _matterClock;                                                    // the matter's own clock: the game's time, so pausing freezes every electron and quark                                          // into a nebula's or cluster's gas: the fine height, and where it began                                 // zooming out past this goes back up to the ground
        private ulong _matterSpot = ulong.MaxValue; private double _mox, _moy, _moz; private int _matterPlanet = -1;
        // where you are from the spot the matter began at (km east and north there)
        private void MatterOffset(out double e, out double n)
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            var up = new Vec3((float)_mox, (float)_moy, (float)_moz);
            var east = Vec3.Cross(Math.Abs(up.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), up).Normalized; var north = Vec3.Cross(up, east).Normalized;
            double dx = (x / r - _mox) * _ground.Radius, dy = (y / r - _moy) * _ground.Radius, dz = (z / r - _moz) * _ground.Radius;
            e = dx * east.X + dy * east.Y + dz * east.Z; n = dx * north.X + dy * north.Y + dz * north.Z;
        }
        private void SetMatter()
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            double e, n;
            if (_matterSpot != ulong.MaxValue && _matterPlanet == _planet) { MatterOffset(out e, out n); if (e * e + n * n < 5e-4 * 5e-4) { _matter.Follow(e, n); return; } }   // within half a metre: the same grains, passing by
            if (!_ground.Solid)
            {
                // a world with no ground (a giant): what is there is its air, its own gases at their real density
                ulong gspot = (ulong)(long)(x / r * 1e7) * 73856093UL ^ (ulong)(long)(y / r * 1e7) * 19349663UL ^ (ulong)(long)(z / r * 1e7) * 83492791UL;
                if (_matterSpot == gspot && _matterPlanet == _planet) return;
                _matterSpot = gspot; _matterPlanet = _planet; _mox = x / r; _moy = y / r; _moz = z / r;
                string air; if (!PlanetTerrain.Air.TryGetValue(_ground.Kind, out air)) air = "hydrogen 90%, helium 10%";
                var lay = LayerHere();
                if (lay != null && lay.Matter == "core") { _matter.Set("core", false, false, 0.5f, 0.45f, 0.4f, gspot); _matter.SetGround(_ground.Radius * CoreShare, x / r, y / r, z / r, "core", _ground.GrainSeed, 0, 0); _matter.SetAirAbove(null); return; }   // its core: rock, iron and ice
                if (lay != null && lay.Matter == "plasma") { _matter.SetPlasma(gspot); return; }      // metallic hydrogen: protons in a sea of free electrons
                if (lay != null && lay.Matter == "ice") { _matter.Set("ice", false, true, 0.7f, 0.8f, 0.9f, gspot); return; }   // the ice mantle: hot dense water, ammonia, methane
                _matter.SetAir(air, gspot); return;
            }
            bool sea = _ground.HasWater && _ground.GroundAt(x / r, y / r, z / r) < 0, ice = Math.Abs(y / r) > 0.86;
            ulong spot = (ulong)(long)(x / r * 1e7) * 73856093UL ^ (ulong)(long)(y / r * 1e7) * 19349663UL ^ (ulong)(long)(z / r * 1e7) * 83492791UL;   // the same place, the same grains
            _matterSpot = spot; _matterPlanet = _planet; _mox = x / r; _moy = y / r; _moz = z / r;
            float mr0, mg0, mb0, mglow; _ground.Height(x / r, y / r, z / r, 30, out mr0, out mg0, out mb0, out mglow);
            bool molten = mglow > 0.5;                                                    // glowing molten ground: the matter there is melt
            _matter.Set(molten ? "melt" : _system.Bodies[_planet].Kind == "Moon" ? "moon" : _ground.Kind, sea, ice, sea ? 0.2f : 0.58f, sea ? 0.4f : 0.5f, sea ? 0.6f : 0.4f, spot);
            // the planet's own grains, on the land's slope here: the same ones its ground is the top of
            {
                double ux = x / r, uy = y / r, uz = z / r, ex, ey, ez, nx, ny, nz; GrainField.Axes(ux, uy, uz, out ex, out ey, out ez, out nx, out ny, out nz);
                double d = 5e-4, R = _ground.Radius;
                Func<double, double, double, double> H = (a1, b1, c1) => { double l = Math.Sqrt(a1 * a1 + b1 * b1 + c1 * c1); return _ground.BaseAt(a1 / l, b1 / l, c1 / l); };
                double he = (H(ux + ex * d / R, uy + ey * d / R, uz + ez * d / R) - H(ux - ex * d / R, uy - ey * d / R, uz - ez * d / R)) / (2 * d);
                double hn = (H(ux + nx * d / R, uy + ny * d / R, uz + nz * d / R) - H(ux - nx * d / R, uy - ny * d / R, uz - nz * d / R)) / (2 * d);
                if (double.IsNaN(he) || Math.Abs(he) > 20) he = 0; if (double.IsNaN(hn) || Math.Abs(hn) > 20) hn = 0;
                var gm = _ground.GrainMix(uy);
                _matter.SetGround(R, ux, uy, uz, sea || molten ? null : gm != null ? gm.Id : null, _ground.GrainSeed, he, hn);
                _matter.SetLife(_ground.HasWater && PlanetTerrain.TerrainKind(_ground.Kind).Life && _system.Bodies[_planet].Kind != "Moon");   // microbes on a living world's grains
                string airHere; _matter.SetAirAbove(_system.Bodies[_planet].Kind == "Moon" || !PlanetTerrain.Air.TryGetValue(_ground.Kind, out airHere) ? null : airHere);   // its own air (a moon: none)
            }
        }

        // how far above the planet's ground the camera is (universe units)
        private double _surfaceCell, _lastAbove = 1;
        // a giant's layers (content/gas-giants.dnat @layer: from-r, to-r, matter), and where its core begins
        private sealed class Layer { public string Id, Name, Matter, Says; public double From, To; }
        private List<Layer> _layers;
        private List<Layer> Layers()
        {
            if (_layers != null) return _layers; _layers = new List<Layer>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("gas-giants.dnat", "layer")) { double a, b; if (!double.TryParse(e.Get("from-r", ""), fs, ci, out a) || !double.TryParse(e.Get("to-r", ""), fs, ci, out b)) continue; _layers.Add(new Layer { Id = e.Id, Name = e.Name, Matter = e.Get("matter", "air"), Says = e.Get("says", ""), From = a, To = b }); }
            return _layers;
        }
        private double CoreShare { get { foreach (var l in Layers()) if (l.Matter == "core") return l.To; return 0.15; } }
        // the layer of a giant you are in now (by your share of its radius from its middle); null above its clouds
        private Layer LayerHere()
        {
            if (_ground == null || _ground.Solid) return null; double x, y, z; CamFromPlanet(out x, out y, out z); double share = Math.Sqrt(x * x + y * y + z * z) / _ground.Radius;
            bool ice = _ground.Kind == "ice-giant"; Layer best = null;
            foreach (var l in Layers())
            {
                if (share < l.From || share > l.To) continue;
                if (ice && l.Id == "metallic-hydrogen") continue; if (!ice && l.Id == "ice-mantle") continue;   // an ice giant has an ice mantle; a gas giant metallic hydrogen
                if (best == null || l.To - l.From < best.To - best.From) best = l;
            }
            return best;
        }
        // where the ground hands you on into the matter of the grain under you: a third of a grain above it (about 30 micrometres
        // over sand), before the ground's ever finer grain tops would rise to meet the camera and hold it hovering
        private double HandoffKm()
        {
            if (_ground == null || !_ground.Solid) return 2e-9;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); var gm = r > 0 ? _ground.GrainMix(y / r) : null;
            return gm != null && gm.Size > 0 ? Math.Max(2e-9, gm.Size * 0.3) : 2e-9;
        }
        private double AboveGround()
        {
            if (_ground == null) return double.MaxValue;
            _surfaceCell = Math.Max(1e-11, _lastAbove / Units.Km(1) / 30);                   // (from the height last asked: a thirtieth of it)
            double x, y, z; CamFromPlanet(out x, out y, out z);
            double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return 0;
            return _lastAbove = Math.Max(1e-9, (r - _ground.Radius - (_ground.Solid ? Surface(x / r, y / r, z / r) : -_ground.Radius * (1 - CoreShare)))) * Units.Km(1);   // a giant: down through its clouds and layers to its core
        }

        // the system's planets, moons and star placed around the star (anchor), and the nearest surface to the camera
        private double PlaceSystem()
        {
            if (_system == null) return double.MaxValue;
            double fx, fy, fz;
            _bodyCount = SpaceGen.BodyVerts(_system, _simYears, 1 / 20.0, 0.05, _bodyVerts, -1, 1.0, out fx, out fy, out fz);
            double best = double.MaxValue;
            for (int i = 0; i < _bodyCount; i++)
            {
                double r = i == 0 ? _system.StarRadius : _system.Bodies[i - 1].RadiusUnits;
                double dx = _bodyVerts[i].X - _ox, dy = _bodyVerts[i].Y - _oy, dz = _bodyVerts[i].Z - _oz;
                best = Math.Min(best, Math.Max(r * 0.05, Math.Sqrt(dx * dx + dy * dy + dz * dz) - r));
            }
            return best;
        }

        private static void ToGalaxy(Galaxy g, double ux, double uy, double uz, out double x, out double y, out double z)
        {
            double px = ux - g.X, py = uy - g.Y, pz = uz - g.Z;
            double ct = Math.Cos(g.Tilt), st = Math.Sin(g.Tilt), cu = Math.Cos(g.Turn), su = Math.Sin(g.Turn);
            double x1 = px * cu + pz * su, z1 = -px * su + pz * cu;                  // undo the turn
            double y2 = py * ct + z1 * st, z2 = -py * st + z1 * ct;                  // undo the tilt
            x = x1 / g.Scale; y = y2 / g.Scale; z = z2 / g.Scale;
        }

        /// <summary>Test runs: the camera turns to the next thing on the way down (the galaxy, a star, a planet, its ground) each frame.</summary>
        public bool AutoPath, AutoNebula;

        // ── a tour: a flight of stops read from content/tours/<name>.dnat, flown with the real controls (the wheel's zoom at one
        // steady rate, the drag's circling and looking, the same picks a click makes) so it shows the one world as it is ──
        private sealed class TourStop { public string Name, Do, Target, Caption; public double Rate, UntilKm, Turn, Tilt, Seconds, Pan, ToRadii, ToKm, Sweep, Rise, MaxRate; public bool Through; }   // Through: flown on through without slowing or stopping (into the next zoom)
        private List<TourStop> _tour; private int _tourAt; private double _tourClock, _tourStall, _tourLastD, _tourT, _tourRate;
        /// <summary>Starts the tour of that name (content/tours/name.dnat); false if there is none.</summary>
        public bool StartTour(string name)
        {
            var list = new List<TourStop>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            Func<string, double, double> num = (t, f) => { double v; return double.TryParse(t, fs, ci, out v) ? v : f; };
            foreach (var e in _content.OfKind("tours/" + name + ".dnat", "stop"))
                list.Add(new TourStop { Name = e.Id, Do = e.Get("do", "orbit"), Target = e.Get("target", "none"), Caption = e.Get("caption", ""), Rate = num(e.Get("rate", "0.3"), 0.3),
                    UntilKm = num(e.Get("until-km", "0"), 0), Turn = num(e.Get("turn", "6"), 6), Tilt = num(e.Get("tilt", "0"), 0), Seconds = num(e.Get("seconds", "10"), 10), Through = e.Get("through", "no") == "yes", Pan = num(e.Get("pan", "0"), 0),
                    ToRadii = num(e.Get("to-radii", "0"), 0), ToKm = num(e.Get("to-km", "0"), 0), Sweep = num(e.Get("sweep", "0"), 0), Rise = num(e.Get("rise", "0"), 0), MaxRate = num(e.Get("max-rate", "0.35"), 0.35) });
            if (list.Count == 0) return false;
            _tour = list; _tourAt = 0; _tourT = 0; _tourRate = 0; _tourClock = 0; _shotRng = new Rng((ulong)Environment.TickCount ^ _seed); NewShot();
            if (TimeStep == 0) SetTime(1);                                              // time runs on the tour (the planets turn, the electrons move)
            return true;
        }
        public bool Touring { get { return _tour != null; } }
        /// <summary>Stops the tour; the controls are yours again.</summary>
        public void StopTour() { _tour = null; _pressed = false; }
        private double _tourH0, _tourLookFrom; private bool _tourWaiting;
        // the director's choices for the shot under way, made afresh for each shot of each viewing (so no two tours are the same):
        // which way it circles, how fast (a little either side of the script's own), and a slow crane up or down as it goes;
        // the last galaxy's kind, so the next is another kind
        private Rng _shotRng = new Rng(1); private double _shotDir = 1, _shotMul = 1, _shotCrane; private string _lastGalaxyType = "";
        private double[] _shotE1, _shotE2, _shotUp; private double _shotD0, _shotD1, _shotEl0, _shotEl1, _shotLen, _shotT = -1;   // the shot under way: its frame round the subject, its distances and heights, its length
        private void NewShot()
        {
            _shotDir = _shotRng.NextDouble() < 0.5 ? -1 : 1; _shotMul = 0.75 + 0.5 * _shotRng.NextDouble(); _shotCrane = (_shotRng.NextDouble() * 2 - 1) * 1.5;
        }
        // how fast the tour may go now: never faster than the world round you is made (slower when the frames a second drop below
        // about 45, and while many pieces of ground are still waiting to be built), eased so it never jerks
        private double _tourPace = 1;
        // out at the cosmic web's scale (beyond the galaxies made in full round you) while its light is still being made
        private bool WebNotMade { get { return _glowAll == null && _farScale > _nearUnits * 0.5; } }
        private double TourPace
        {
            get
            {
                double want = Math.Max(0.5, Math.Min(1, Window.Fps / 40.0));
                if (_ground != null && _ground.Backlog > 80) want *= Math.Max(0.6, 80.0 / _ground.Backlog);
                if (WebNotMade) want = 0.05;                                            // out among the web before its light is made: wait for it
                if (_matterScale > 0 || _spaceScale > 0) want = 1;                     // in the matter nothing is waited for (the ground's building behind you never slows it)
                _tourPace += (want - _tourPace) * 0.05; return _tourPace;
            }
        }
        // the tour is looking round (up at the sky, round the view, gliding): the view is its own, never turned back to the pick
        private bool TourLooking { get { if (_tour == null || _tourAt >= _tour.Count) return false; var d = _tour[_tourAt].Do; return d == "shot" || (_ground != null && (d == "look" || d == "glide" || (d == "orbit" && _tKind != 2))); } }
        // the caption: the stop's own (a pick, over in a moment, shows the next stop's, so every caption stays on through its flight)
        private string TourCaption { get { int at = _tourAt; while (_tour != null && at < _tour.Count - 1 && _tour[at].Do == "pick") at++; return _tour != null && at < _tour.Count ? _tour[at].Caption + (_tourWaiting ? "   (making what is ahead...)" : "") + "   (the view: " + ScaleHud.Words(ViewDistanceKm()) + ")" : ""; } }   // the scale is what the tour shows
        // one frame of the tour: what the stop asks, given to the controls; on to the next when it is done
        private void TourStep(Input input, double dt, int w, int h)
        {
            _tourClock += dt;
            if (_tourClock > 2)
            {
                // you take the controls back: a click, or a key (not the mouse's own buttons); never in its first moments
                if (input.LeftClicked || input.RightClicked) { _tour = null; _pressed = false; return; }
                for (int k = 8; k < 256; k++) if (k != 0x78 && k != 0x7B && input.WasPressed(k)) { _tour = null; _pressed = false; return; }   // (screenshots and recording never stop it)
            }
            if (_tourAt >= _tour.Count) { _tour = null; _pressed = false; return; }
            var st = _tour[_tourAt]; _tourT += dt; bool done = false;
            input.MouseX = w / 2; input.MouseY = h / 2;                                    // the controls act at the middle of the view
            switch (st.Do)
            {
                case "zoom":
                    {
                        // one steady rate in scale (each tenfold the same time), eased in and out
                        double d = ViewDistanceKm(), want = st.UntilKm, sign = st.Rate >= 0 ? 1 : -1;
                        double left = Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, want)));
                        double target = Math.Abs(st.Rate) * (st.Through ? 1 : Math.Min(1, left / 0.6)) * TourPace;    // easing out over the last few tenths of a tenfold; never faster than the world is made
                        _tourRate += (target - _tourRate) * (1 - Math.Exp(-dt * 1.5));            // and easing in
                        double notches = _tourRate * Math.Log(10) / Math.Log(1 / 0.87) * dt;       // a wheel notch is 13% of the way
                        input.Wheel = OffTarget() < 0.07 && (_matterScale <= 0 && _spaceScale <= 0 || _matter.PickOff < 0.35) ? sign * notches : 0;   // never moving one way while looking another; in the matter, never past what is picked (only once over it)
                        if (st.Pan != 0 && _tKind >= 0) TourTurn(st.Pan * Math.PI / 180 * dt * Math.Min(1, _tourT / 2), 0);   // circling what it flies to as it goes
                        // going nowhere (nothing nearer to fly at) for six seconds: on to the next stop
                        if (Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, _tourLastD))) > 0.05 || (_ground != null && _ground.Backlog > 0) || WebNotMade) { _tourLastD = d; _tourStall = 0; } else _tourStall += dt;   // (held back by ground still being built, or the web's light: waiting, never given up on)
                        if ((sign > 0 && d <= want * 1.02) || (sign < 0 && d >= want * 0.98) || _tourT > 240 || (_tourStall > 20 && _tourT > 20)) { done = true; _tourRate = 0; input.Wheel = 0; _tourStall = 0; _tourLastD = 0; }
                    }
                    break;
                case "shot":
                    {
                        // a film's shot of what is picked: the camera moves in its own frame round the subject (how far, in the
                        // subject's own sizes or in km; how far round it sweeps; how far it rises or sinks), always facing it, the
                        // distance gliding evenly in scale and every move eased in and out; long enough that the scale never changes
                        // faster than max-rate tenfolds a second, so nothing streams past
                        double tdx, tdy, tdz, trad;
                        if (!TargetFromCamera(out tdx, out tdy, out tdz, out trad)) { done = true; break; }
                        double tl = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz); if (tl <= 0) { done = true; break; }
                        if (_tourT <= dt * 1.5 || _shotT < 0)
                        {
                            // its frame, from where the camera is: up the level up, round from where it stands now
                            var up = LevelUp(); double ux = up.X, uy = up.Y, uz = up.Z;
                            double cx = -tdx / tl, cy = -tdy / tl, cz = -tdz / tl, ce = cx * ux + cy * uy + cz * uz;
                            double hx = cx - ux * ce, hy = cy - uy * ce, hz = cz - uz * ce, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                            if (hl < 1e-6) { hx = Math.Abs(ux) < 0.9 ? 1 : 0; hy = 0; hz = Math.Abs(ux) < 0.9 ? 0 : 1; double d0 = hx * ux + hz * uz; hx -= ux * d0; hy -= uy * d0; hz -= uz * d0; hl = Math.Sqrt(hx * hx + hy * hy + hz * hz); }
                            _shotE1 = new[] { hx / hl, hy / hl, hz / hl }; _shotUp = new[] { ux, uy, uz };
                            _shotE2 = new[] { uy * _shotE1[2] - uz * _shotE1[1], uz * _shotE1[0] - ux * _shotE1[2], ux * _shotE1[1] - uy * _shotE1[0] };
                            _shotD0 = tl; _shotEl0 = Math.Asin(Math.Max(-1, Math.Min(1, ce)));
                            _shotD1 = st.ToKm > 0 ? Units.Km(st.ToKm) : st.ToRadii > 0 && trad > 0 ? trad * st.ToRadii : tl;
                            _shotEl1 = Math.Max(-1.3, Math.Min(1.3, _shotEl0 + st.Rise * Math.PI / 180));
                            double decades = Math.Abs(Math.Log10(_shotD1 / _shotD0));
                            _shotLen = Math.Max(st.Seconds, decades / Math.Max(0.05, st.MaxRate) + 2);
                            _shotT = 0;
                        }
                        if (!WebNotMade) _shotT += dt;                                          // (held while the web's light is made)
                        double u = Math.Min(1, _shotT / _shotLen), e = u * u * (3 - 2 * u);
                        double D = Math.Exp(Math.Log(_shotD0) + (Math.Log(_shotD1) - Math.Log(_shotD0)) * e);
                        double az = st.Sweep * _shotDir * Math.PI / 180 * e, el = _shotEl0 + (_shotEl1 - _shotEl0) * e, ca = Math.Cos(az), sa = Math.Sin(az), ce2 = Math.Cos(el), se2 = Math.Sin(el);
                        double vx = ce2 * (ca * _shotE1[0] + sa * _shotE2[0]) + se2 * _shotUp[0], vy = ce2 * (ca * _shotE1[1] + sa * _shotE2[1]) + se2 * _shotUp[1], vz = ce2 * (ca * _shotE1[2] + sa * _shotE2[2]) + se2 * _shotUp[2];
                        // the camera to where the shot has it now (the subject where it is now: worlds move on their orbits)
                        _ox += tdx + vx * D; _oy += tdy + vy * D; _oz += tdz + vz * D;
                        SetLook(-vx, -vy, -vz);
                        if (u >= 1) { done = true; _shotT = -1; }
                    }
                    break;
                case "orbit":
                case "look":
                    {
                        // never circling while looking near straight up or down (the turn would spin round the pole): ease the view
                        // toward level first, gently
                        { double lp = LocalPitch(); if (st.Do == "orbit" && Math.Abs(lp) > 0.9) TourPitchToward(Math.Sign(lp) * 0.9, 1 - Math.Exp(-dt * 0.8)); }
                        double ease = Math.Min(1, _tourT / 2.5) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 2.5); ease = ease * ease * (3 - 2 * ease);
                        double yaw = st.Turn * _shotDir * _shotMul * Math.PI / 180 * dt * ease;
                        if (st.Do == "look")
                        {
                            // a look: up to its tilt above the horizon (on a world, the sky with its stars and galaxies) or down below it,
                            // a definite aim, eased in over the stop
                            if (_tourT <= dt * 1.5) _tourLookFrom = LocalPitch();
                            double t01 = Math.Min(1, _tourT / Math.Max(0.5, st.Seconds * 0.8)), ee = t01 * t01 * (3 - 2 * t01);
                            double aim = Math.Max(-1.45, Math.Min(1.45, (_ground != null ? 0 : _tourLookFrom) + st.Tilt * Math.PI / 180));
                            TourTurn(yaw, 0); TourPitchToward(_tourLookFrom + (aim - _tourLookFrom) * ee, 0.5);
                        }
                        else TourTurn(yaw, (_ground == null ? _shotCrane : Math.Sin(_tourClock * 0.2) * 0.25) * Math.PI / 180 * dt * ease);   // out in space a slow crane up or down; on a world its gaze drifts barely
                        if (_tourT >= st.Seconds) done = true;
                    }
                    break;
                case "arc":
                    {
                        // the cinematic move: a slow dolly in (or out) while circling the subject, both eased in and out, turned smoothly
                        // with a distance to reach it circles on till it is there (easing out over the last few tenths of a tenfold),
                        // else for its seconds
                        double sign = st.Rate >= 0 ? 1 : -1, d = ViewDistanceKm();
                        bool there = st.UntilKm > 0 && ((sign > 0 && d <= st.UntilKm) || (sign < 0 && d >= st.UntilKm));
                        double left = st.UntilKm > 0 && Math.Abs(st.Rate) > 0 ? Math.Abs(Math.Log10(Math.Max(1e-40, d) / st.UntilKm)) / 0.6 : Math.Max(0, st.Seconds - _tourT) / 3;
                        double ease = Math.Min(1, _tourT / 3) * Math.Min(1, Math.Max(0.15, left)); ease = ease * ease * (3 - 2 * ease);
                        if (!there && OffTarget() < 0.07 && (_matterScale <= 0 && _spaceScale <= 0 || _matter.PickOff < 0.35)) input.Wheel = sign * Math.Abs(st.Rate) * ease * TourPace * Math.Log(10) / Math.Log(1 / 0.87) * dt;   // only moving where it looks
                        { double lp = LocalPitch(); if (Math.Abs(lp) > 0.9) TourPitchToward(Math.Sign(lp) * 0.9, 1 - Math.Exp(-dt * 0.8)); }
                        TourTurn(st.Turn * _shotDir * _shotMul * Math.PI / 180 * dt * Math.Max(ease, 0.4), (_ground == null && _matterScale <= 0 ? _shotCrane : 0) * Math.PI / 180 * dt * ease);
                        if (_tourT >= st.Seconds && (there || st.UntilKm <= 0 || Math.Abs(st.Rate) <= 0 || _tourT >= st.Seconds * 2)) done = true;   // (circling on to its distance for at most twice its seconds: never held there; the next stop carries on)
                    }
                    break;
                case "fly":
                    {
                        // a steady flight forward, past what is ahead (a star, a world): the same share of the distance to the
                        // nearest thing each second, eased in and out, never a sudden speed-up; turning slowly as it goes
                        double ease = Math.Min(1, _tourT / 2) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 2);
                        var f = Look;
                        if (_matterScale > 0)
                        {
                            // in the matter: on through it, the way you face, at the height you are (among the crystal's atoms, past
                            // them, rate heights a second), as W A S D fly you
                            double msp = _matterScale * 0.6 * st.Rate * dt * ease;
                            double x0, y0, z0; CamFromPlanet(out x0, out y0, out z0); double r0 = Math.Sqrt(x0 * x0 + y0 * y0 + z0 * z0);
                            var upL = new Vec3((float)(x0 / r0), (float)(y0 / r0), (float)(z0 / r0));
                            var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
                            double fe = Vec3.Dot(f, east), fn = Vec3.Dot(f, north), fl = Math.Sqrt(fe * fe + fn * fn); if (fl < 1e-6) { fe = 1; fn = 0; fl = 1; }
                            _matter.Unpick(); _matter.Move(fe / fl * msp, fn / fl * msp);
                        }
                        else { double sp = Math.Max(NearestDistance(), _farScale) * st.Rate * dt * ease; _ox += f.X * sp; _oy += f.Y * sp; _oz += f.Z * sp; }
                        SetLook(Turn(Look, LevelUp(), st.Turn * Math.PI / 180 * dt * ease));
                        if (_tourT >= st.Seconds) done = true;
                    }
                    break;
                case "glide":
                    {
                        // a flight low over the land: level, at the height it began at, rising and falling with the ground, rate
                        // heights a second (a walk at head height, a glide over hills from higher), looking a little ahead and
                        // down, turning slowly; eased in and out
                        if (_ground == null) { done = true; break; }
                        double ease = Math.Min(1, _tourT / 3) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 3); ease = ease * ease * (3 - 2 * ease);
                        double hNow = AboveGround(); if (_tourT <= dt * 1.5 || _tourH0 <= 0) _tourH0 = hNow;
                        double cx, cy, cz; CamFromPlanet(out cx, out cy, out cz); double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz); if (cl <= 0) { done = true; break; }
                        double ux = cx / cl, uy = cy / cl, uz = cz / cl; var f = Look; double fu = f.X * ux + f.Y * uy + f.Z * uz;
                        double tx = f.X - ux * fu, ty = f.Y - uy * fu, tz = f.Z - uz * fu, tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        if (tl > 1e-6)
                        {
                            double sp = _tourH0 * st.Rate * dt * ease; _ox += tx / tl * sp; _oy += ty / tl * sp; _oz += tz / tl * sp;
                        }
                        double dh = (_tourH0 - AboveGround()) * Math.Min(1, dt * 3); _ox += ux * dh; _oy += uy * dh; _oz += uz * dh;   // the same height over the ground
                        TourPitchToward(-0.22, 1 - Math.Exp(-dt * 0.6));                         // looking a little ahead and down (against the world's own horizon)
                        TourTurn(st.Turn * Math.PI / 180 * dt * ease, 0);
                        if (_tourT >= st.Seconds) { done = true; _tourH0 = 0; }
                    }
                    break;
                case "pick":
                    {
                        // a pick that finds nothing yet (the stars round you, the star's worlds still being made) tries again each
                        // frame for up to twenty seconds, never going on with nothing chosen (every later stop would be lost)
                        int k0 = _tKind, i0 = _tIndex, p0 = _matter.PickCount; double x0 = _tx; TourPick(st.Target);
                        bool got = st.Target == "none" || (st.Target == "planet" && _tKind == 2 && _system != null) || (_tKind >= 0 && (_tKind != k0 || _tIndex != i0 || _tx != x0)) || _matter.PickCount != p0;
                        if (got || _tourT > 20) done = true;
                    }
                    break;
                default: done = true; break;
            }
            // no pauses: each stop goes straight on into the next (its caption stays on through the flight); the looking round is
            // done by the circling stops at the points that matter
            if (done && WebNotMade) { done = false; input.Wheel = 0; }               // (never on past the web before it shows)
            // held back while what is ahead is still being made (the web's light, the ground): never still, so it never looks frozen;
            // it keeps circling what it shows, and the caption says what it is waiting for
            _tourWaiting = WebNotMade || (st.Do == "zoom" && _ground != null && _ground.Backlog > 80);
            if (WebNotMade) TourTurn(10 * Math.PI / 180 * dt, 0);                   // (out among the galaxies only: by a world it never circles while it waits)
            if (done) { _tourAt++; _tourT = 0; NewShot(); }
        }
        // what a stop picks, as a click would
        private void TourPick(string what)
        {
            switch (what)
            {
                case "none": _tKind = -1; _tName = ""; _matter.Unpick(); break;
                case "black-hole":
                case "asteroid":
                case "comet":
                    {
                        // one of the bodies round you (content/bodies.dnat): the nearest black hole, asteroid of the belt, or comet of
                        // the Oort cloud
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < _extras.Count; q++)
                        {
                            var ex = _extras[q]; string nm = ex.Name.ToLowerInvariant();
                            bool fits = what == "black-hole" ? ex.Hole : what == "asteroid" ? nm.Contains("asteroid") : nm.Contains("comet");
                            if (!fits) continue;
                            double dx = (ex.X - _ax) - _ox, dy = (ex.Y - _ay) - _oy, dz = (ex.Z - _az) - _oz, d = dx * dx + dy * dy + dz * dz;
                            if (what == "black-hole" && ex.Feeding > 0) d *= 1e-6;             // (a feeding one, its disc glowing, before a quiet one)
                            if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; }
                    }
                    break;
                case "start-galaxy":
                    {
                        // the galaxy you begin in (a galaxy of this universe, like any other), to open on
                        int bi = -1; double bd = double.MaxValue;
                        for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = i; } }
                        if (_near >= 0 && _near < _galaxies.Count) bi = _near;                // (the one you are in, whatever else is made round it)
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; }
                    }
                    break;
                case "far-galaxy":
                    {
                        // across the universe: the farthest galaxy made round you that lies ahead (in view), never a near one
                        int bi = -1; double bs = double.MinValue; var lk = Look; var rng = new Rng(_seed ^ (ulong)Environment.TickCount);
                        for (int i = 0; i < _galaxies.Count; i++)
                        {
                            var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (d < 3 || d > _nearUnits * 1.7) continue;
                            double ahead = (dx * lk.X + dy * lk.Y + dz * lk.Z) / d; if (ahead < 0.5) continue;
                            double score = d * (0.85 + 0.3 * rng.NextDouble()) * (g.Type == _lastGalaxyType ? 0.6 : 1);   // (another kind than the last, where it can)
                            if (score > bs) { bs = score; bi = i; }
                        }
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; _lastGalaxyType = _galaxies[bi].Type; break; }
                    }
                    goto case "galaxy";                                                    // (none far ahead yet: one some way off)
                case "galaxy":
                case "next-galaxy":
                    {
                        // exploring: a galaxy chosen afresh each time (any seed, any run), some way off rather than the nearest,
                        // ahead of you where it can be, never the one just visited
                        var rng = new Rng(_seed ^ (ulong)(_tourAt * 7919 + Environment.TickCount));
                        int bi = -1; double bs = double.MinValue; int skip = _tKind == 0 ? _tIndex : -1; var lk = Look;
                        for (int i = 0; i < _galaxies.Count; i++)
                        {
                            if (i == skip) continue; var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (d < 3 || d > _nearUnits * 1.4) continue;
                            double ahead = (dx * lk.X + dy * lk.Y + dz * lk.Z) / d, score = ahead + rng.NextDouble() * 1.5 + Math.Min(1, d / 60);
                            if (score > bs) { bs = score; bi = i; }
                        }
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; }
                    }
                    break;
                case "star":
                    {
                        // exploring: one of the stars round you, chosen afresh, not simply the nearest
                        var rng = new Rng(_seed ^ (ulong)(_tourAt * 104729 + Environment.TickCount));
                        if (_localCount > 0)
                        {
                            int k = rng.Below(_localCount); double ux, uy, uz; StarPos(1000000 + k, out ux, out uy, out uz);
                            _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star";
                        }
                        else { double d; int k = NearestStar(out d); if (k >= 0) { double ux, uy, uz; StarPos(k, out ux, out uy, out uz); _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star"; } }
                        if (_tKind != 1 && _near >= 0 && _nearLocal != null && _nearLocal.Length > 0)
                        {
                            // still among the galaxy's far stars (the ones close round you not made yet): one of the stars you can
                            // see and click, the same ones a click would take
                            var s0 = _nearLocal[rng.Below(_nearLocal.Length)]; double ux, uy, uz; ToUniverse(_galaxies[_near], s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                            _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star";
                        }
                    }
                    break;
                case "next-planet":
                    if (_system != null)
                    {
                        // the next of the star's planets in turn (for a fly-by of each)
                        int start = _tKind == 2 ? _tIndex : -1;
                        for (int q = 1; q <= _system.Bodies.Count; q++) { int i = (start + q + _system.Bodies.Count) % _system.Bodies.Count; if (_system.Bodies[i].Parent < 0) { _tKind = 2; _tIndex = i; _tName = _system.Bodies[i].Kind; break; } }
                    }
                    break;
                case "planet":
                    if (_system != null)
                    {
                        // exploring: one of the star's worlds, chosen afresh (a solid one where there is one, to go down to)
                        var rng = new Rng(_seed ^ (ulong)(Environment.TickCount + 31)); var opts = new List<int>();
                        for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && !_system.Bodies[i].Kind.Contains("giant") && !_system.Bodies[i].Kind.Contains("Jupiter")) opts.Add(i);
                        if (opts.Count == 0) for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0) opts.Add(i);
                        // the director's eye: a world with seas and air most of all, then any solid world, by chance among them
                        double total = 0; var wts = new double[opts.Count];
                        for (int q = 0; q < opts.Count; q++) { string k = _system.Bodies[opts[q]].Kind; wts[q] = k == "Temperate world" ? 6 : k == "Ice world" || k == "Lava planet" ? 2.5 : 2; total += wts[q]; }
                        double pickAt = rng.NextDouble() * total; int pick = opts.Count > 0 ? opts[opts.Count - 1] : -1;
                        for (int q = 0; q < opts.Count; q++) { pickAt -= wts[q]; if (pickAt <= 0) { pick = opts[q]; break; } }
                        if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; }
                    }
                    break;
                default: _matter.PickNearest(what); break;                                     // a grain, a spot on its face, an atom, electron, nucleus, proton, quark
            }
        }
        /// <summary>True when a planet's ground is being drawn (what the GPU use tunes, so far).</summary>
        public bool AtGround { get { return _ground != null; } }
        /// <summary>How many ground pieces are waiting (0 when not at a planet).</summary>
        public int GroundBacklog { get { return _ground != null ? _ground.Backlog : 0; } } private double _autoLogT;
        // ── what you clicked: the camera turns to face it and the wheel flies to it ──
        private int _tKind = -1;                 // 0 a galaxy, 1 a star, 2 a planet or moon, 3 a spot on the planet's ground
        private int _tIndex; private double _tx, _ty, _tz, _tRadius;   // galaxy or body index; a star's place (universe units) or a ground spot (unit direction)
        private string _tName = "";
        private bool _pressed, _dragged; private int _pressX, _pressY;
        private Mat4 _lastVp; private int _lastW = 1, _lastH = 1; private bool _haveVp;
        /// <summary>Set when something the menu shows has changed (the scale's tick), so the menu is rebuilt.</summary>
        public bool MenuChanged;
        /// <summary>Whether the scale shows on screen (H, or the View menu).</summary>
        public bool ScaleOn { get { return _hud.Visible; } set { _hud.Visible = value; } }
        private void AimNext()
        {
            double tx, ty, tz;
            if (_ground != null) { tx = _px; ty = _py; tz = _pz; }                                  // straight down to the ground
            else if (_system != null)
            {
                int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Kind == "Temperate world")) pick = i;
                if (pick < 0) return;
                BodyPos(pick, out tx, out ty, out tz);
                // come in over its day side: a point a few radii out toward the star
                double l0 = Math.Sqrt(tx * tx + ty * ty + tz * tz), rr = _system.Bodies[pick].RadiusUnits * 4;
                if (l0 > 0) { tx -= tx / l0 * rr; ty -= ty / l0 * rr; tz -= tz / l0 * rr; }
            }
            else if (_near >= 0 && _localCount > 0 && NearShare() > 0.9)
            {
                double d; int k = NearestStar(out d); if (k < 0) return;
                double ux, uy, uz; StarPos(k, out ux, out uy, out uz); tx = (ux - _ax); ty = (uy - _ay); tz = (uz - _az);
            }
            else { var g = _galaxies[0]; tx = g.X - _ax; ty = g.Y - _ay; tz = g.Z - _az; }
            double dx = tx - _ox, dy = ty - _oy, dz = tz - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l > 0) SetLook(dx, dy, dz);
        }

        // where the clicked thing is now, from the camera (universe units), and how big it is; false if it is gone
        private bool TargetFromCamera(out double dx, out double dy, out double dz, out double radius)
        {
            dx = dy = dz = 0; radius = 0;
            if (_tKind == 8)
                for (int i = 0; i < _galaxies.Count; i++)
                {
                    // the far galaxy you picked, now made in full near where its light was: the pick goes on to it
                    var g8 = _galaxies[i]; double ex = g8.X - _tx, ey = g8.Y - _ty, ez = g8.Z - _tz;
                    if (ex * ex + ey * ey + ez * ez < Math.Pow(3e6 / Units.LightYearsPerUnit, 2)) { _tKind = 0; _tIndex = i; _tName = g8.Name; break; }
                }
            if (_tKind == 0) { var g = _galaxies[_tIndex]; dx = (g.X - _ax) - _ox; dy = (g.Y - _ay) - _oy; dz = (g.Z - _az) - _oz; radius = g.Scale * 0.3; return true; }
            if (_tKind == 1 || _tKind == 4 || _tKind == 5 || _tKind == 6 || _tKind == 7 || _tKind == 8) { dx = (_tx - _ax) - _ox; dy = (_ty - _ay) - _oy; dz = (_tz - _az) - _oz; radius = _tRadius; return true; }   // a star, or a spot in a belt
            if (_tKind == 2 && _system != null && _tIndex < _system.Bodies.Count)
            { double x, y, z; BodyPos(_tIndex, out x, out y, out z); dx = x - _ox; dy = y - _oy; dz = z - _oz; radius = _system.Bodies[_tIndex].RadiusUnits; return true; }
            if (_tKind == 3 && _ground != null)
            {
                double g = _ground.Solid ? Surface(_tx, _ty, _tz) : 0, r = (_ground.Radius + g) * Units.Km(1);
                dx = _px + _tx * r - _ox; dy = _py + _ty * r - _oy; dz = _pz + _tz * r - _oz; radius = 0; return true;
            }
            return false;
        }

        // where a point (relative to the camera, universe units) lands on screen, in pixels; false if behind you
        private bool ToScreen(double dx, double dy, double dz, out double sx, out double sy)
        {
            sx = sy = 0; if (!_haveVp) return false;
            var m = _lastVp; float x = (float)dx, y = (float)dy, z = (float)dz;
            float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw <= 0) return false;
            sx = (cx / cw * 0.5 + 0.5) * _lastW; sy = (0.5 - cy / cw * 0.5) * _lastH; return true;
        }

        // pick what is under the mouse: the planet's ground if you are at one, else the nearest planet or moon, star or galaxy to the line
        private void Pick(Vec3 dir, int mx, int my)
        {
            double reachPx = 24 * Math.Max(1, _lastH / 1080.0), best = reachPx * reachPx; int kind = -1, idx = 0; double tx = 0, ty = 0, tz = 0, trad = 0; string name = "";
            Func<double, double, double, double> near = (dx, dy, dz) => { double sx, sy; if (!ToScreen(dx, dy, dz, out sx, out sy)) return double.MaxValue; return (sx - mx) * (sx - mx) + (sy - my) * (sy - my); };
            for (int q = 0; q < _extras.Count && _ground == null; q++)
            {
                // the other bodies round you (black holes, neutron stars, rogue planets, asteroids, comets...): clickable as any star
                var ex = _extras[q]; double d2 = near((ex.X - _ax) - _ox, (ex.Y - _ay) - _oy, (ex.Z - _az) - _oz);
                if (d2 < best) { best = d2; kind = 6; idx = q; tx = ex.X; ty = ex.Y; tz = ex.Z; trad = ex.R; name = ex.Name; }
            }
            if (_tKind == 5 && _near >= 0 && _tGal == _near && _tIndex >= 0 && _tIndex < _features.Count && _ground == null)
            {
                // inside a picked nebula or cluster: a click is a point in its gas, right where you click, at the depth you are at
                // (a star only if you click right on it), so the wheel flies there and on into its atoms
                var g = _galaxies[_near]; var ft = _features[_tIndex]; double cux, cuy, cuz; ToUniverse(g, ft[0], ft[1], ft[2], out cux, out cuy, out cuz);
                double ex = (cux - _ax) - _ox, ey = (cuy - _ay) - _oy, ez = (cuz - _az) - _oz, toCentre = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                if (toCentre < ft[3] * g.Scale * 1.2)
                {
                    double tight = Math.Pow(16 * Math.Max(1, _lastH / 1080.0), 2);
                    for (int k = 0; k < _localCount; k++)
                    {
                        var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                        if (near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz) < tight) { _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a " + _kinds[_localKind[k]].Name; return; }
                    }
                    double cdx, cdy, cdz, crad; TargetFromCamera(out cdx, out cdy, out cdz, out crad);
                    double depth = Math.Max(Units.Km(10), Math.Sqrt(cdx * cdx + cdy * cdy + cdz * cdz));
                    _tx = _ax + _ox + dir.X * depth; _ty = _ay + _oy + dir.Y * depth; _tz = _az + _oz + dir.Z * depth; _tRadius = 0;
                    _tName = "a point in the gas of " + _tName.Replace("a point in the gas of ", ""); return;
                }
            }
            if (_ground != null)
            {
                // the ground: where the line under the mouse meets the planet
                double x, y, z; CamFromPlanet(out x, out y, out z);
                // follow the line of sight down to the real ground (its hills and basins, the sea where there is sea): step out
                // along it, growing with the distance, until it passes below the ground, then close in on the crossing
                double alt0 = Math.Max(1e-9, AboveGround() / Units.Km(1)), t0 = 0, t1 = alt0 * 0.05, far = Math.Sqrt(Math.Max(0, (x * x + y * y + z * z) - _ground.Radius * _ground.Radius)) * 1.2 + 50;
                Func<double, double> above = t =>
                {
                    double hx = x + dir.X * t, hy = y + dir.Y * t, hz = z + dir.Z * t, l = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    return l - _ground.Radius - (_ground.Solid ? Surface(hx / l, hy / l, hz / l) : 0);
                };
                bool hit = false;
                for (int step = 0; step < 400 && t1 < far; step++)
                {
                    if (above(t1) <= 0) { hit = true; break; }
                    t0 = t1; t1 += Math.Max(alt0 * 0.02, t1 * 0.04);
                }
                if (hit)
                {
                    for (int q = 0; q < 40; q++) { double tm = (t0 + t1) * 0.5; if (above(tm) > 0) t0 = tm; else t1 = tm; }
                    double hx = x + dir.X * t1, hy = y + dir.Y * t1, hz = z + dir.Z * t1, l = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    _tKind = 3; _tx = hx / l; _ty = hy / l; _tz = hz / l; _tName = "a spot on " + _system.Bodies[_planet].Kind; return;
                }
            }
            if (_system != null)
            {
                PlaceSystem();
                for (int i = 1; i < _bodyCount; i++)
                {
                    double d2 = near(_bodyVerts[i].X - _ox, _bodyVerts[i].Y - _oy, _bodyVerts[i].Z - _oz);
                    if (d2 < best) { best = d2; kind = 2; idx = i - 1; name = _system.Bodies[i - 1].Kind; }
                }
            }
            if (_system != null && kind < 0)
            {
                // a belt: where the line under the mouse crosses the plane of the system, if it falls in a belt (the asteroid belt,
                // the Kuiper belt, or the Oort cloud's inner reaches)
                if (Math.Abs(dir.Y) > 1e-6)
                {
                    double t = -_oy / dir.Y;
                    if (t > 0)
                    {
                        double hx = _ox + dir.X * t, hz = _oz + dir.Z * t, au = Math.Sqrt(hx * hx + hz * hz) / Units.Au(1);
                        var sy = _system; string bn = null;
                        if (sy.BeltOutAu > 0 && au >= sy.BeltInAu && au <= sy.BeltOutAu) bn = "the asteroid belt";
                        else if (au >= sy.KuiperInAu && au <= sy.KuiperOutAu) bn = "the Kuiper belt";
                        else if (au >= sy.OortInAu && au <= sy.OortOutAu) bn = "the Oort cloud";
                        if (bn != null)
                        {
                            double sx2, sy2; bool onScreen = ToScreen(hx - _ox, -_oy, hz - _oz, out sx2, out sy2);
                            if (onScreen) { _tKind = 4; _tx = _ax + hx; _ty = _ay; _tz = _az + hz; _tRadius = 0; _tName = bn; return; }
                        }
                    }
                }
            }
            if (_near >= 0 && kind < 0)
            {
                var g = _galaxies[_near];
                for (int k = 0; k < _localCount; k++)
                {
                    var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                    double d2 = near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz);
                    if (d2 < best) { best = d2; kind = 1; tx = ux; ty = uy; tz = uz; trad = Units.Km(696000); name = "a " + _kinds[_localKind[k]].Name; }
                }
                if (kind < 0 && NearShare() > 0.5)
                    for (int k = 0; k < _nearLocal.Length; k++)
                    {
                        var s0 = _nearLocal[k]; double ux, uy, uz; ToUniverse(g, s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                        double d2 = near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz);
                        if (d2 < best) { best = d2; kind = 1; tx = ux; ty = uy; tz = uz; trad = Units.Km(696000); name = "a " + _kinds[_nearKinds[k]].Name; }
                    }
            }
            int pickGal = -1;
            for (int gi = 0; gi < _galaxies.Count && kind != 5; gi++)
            {
                // a cluster or nebula of another galaxy near enough to see them
                var g = _galaxies[gi]; if (gi == _near || g.Features == null) continue;
                double gdx = g.X - _cx, gdy = g.Y - _cy, gdz = g.Z - _cz; if (Math.Sqrt(gdx * gdx + gdy * gdy + gdz * gdz) / g.Scale > 60) continue;
                string[] nm = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                for (int q = 0; q < g.Features.Count; q++)
                {
                    var ft = g.Features[q]; double ux, uy, uz; ToUniverse(g, ft[0], ft[1], ft[2], out ux, out uy, out uz);
                    double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = ft[3] * g.Scale / Math.Max(1e-12, l) / Math.Tan(0.45) * _lastH * 0.5, d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && kind != 1 && (kind < 0 || d2 < best)) { best = d2; kind = 5; idx = q; tx = ux; ty = uy; tz = uz; trad = ft[3] * g.Scale; name = nm[(int)ft[4]] + " of " + g.Name; pickGal = gi; }
                }
            }
            if (_near >= 0 && (kind < 0 || kind == 1) && NearShare() > 0.2)
            {
                // a star cluster or a nebula of the near galaxy: by its place on screen, or anywhere on it
                var g = _galaxies[_near]; string[] names = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                for (int q = 0; q < _features.Count; q++)
                {
                    var ft = _features[q]; double ux, uy, uz; ToUniverse(g, ft[0], ft[1], ft[2], out ux, out uy, out uz);
                    double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = ft[3] * g.Scale / Math.Max(1e-12, l) / Math.Tan(0.45) * _lastH * 0.5, d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && kind != 1 && (kind < 0 || d2 < best)) { best = d2; kind = 5; idx = q; tx = ux; ty = uy; tz = uz; trad = ft[3] * g.Scale; name = names[(int)ft[4]]; pickGal = _near; }
                }
            }
            if (kind < 0)
                for (int i = 0; i < _galaxies.Count; i++)
                {
                    var g = _galaxies[i]; double dx = (g.X - _ax) - _ox, dy = (g.Y - _ay) - _oy, dz = (g.Z - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = g.Scale / Math.Max(1e-9, l) / Math.Tan(0.45) * _lastH * 0.5;   // its size on screen
                    double d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && (kind < 0 || d2 < best)) { best = d2; kind = 0; idx = i; name = g.Name; }
                }
            if (kind < 0 && _glowShown.Length > 0)
            {
                // every point of the web's light far off is a galaxy: a click on one picks it, to fly to; there its galaxies are
                // made in full and the pick goes on to the one at that spot
                double gb = reachPx * reachPx; int gq = -1;
                for (int q = 0; q < _glowShown.Length; q++) { var v = _glowShown[q]; double d2 = near((v.X - _ax) - _ox, (v.Y - _ay) - _oy, (v.Z - _az) - _oz); if (d2 < gb) { gb = d2; gq = q; } }
                if (gq >= 0)
                {
                    var v = _glowShown[gq]; double ly = Math.Sqrt(Math.Pow(v.X - _ax - _ox, 2) + Math.Pow(v.Y - _ay - _oy, 2) + Math.Pow(v.Z - _az - _oz, 2)) * Units.LightYearsPerUnit;
                    kind = 8; idx = gq; tx = v.X; ty = v.Y; tz = v.Z; trad = 50000 / Units.LightYearsPerUnit;
                    name = "a galaxy, seen by its light from " + (ly >= 1e9 ? (ly / 1e9).ToString("0.#") + " billion" : (ly / 1e6).ToString("N0") + " million") + " light-years away";
                }
            }
            if (_cstars.Count > 0)
            {
                // a star of the cluster round you: it takes the click ahead of the cluster itself
                double tight = Math.Pow(12 * Math.Max(1, _lastH / 1080.0), 2), cb = tight; int ci = -1;
                for (int q = 0; q < _cstars.Count; q++) { var cs = _cstars[q]; double d2 = near((cs[0] - _ax) - _ox, (cs[1] - _ay) - _oy, (cs[2] - _az) - _oz); if (d2 < cb) { cb = d2; ci = q; } }
                if (ci >= 0) { var cs = _cstars[ci]; bool old = cs[3] == 0; kind = 1; tx = cs[0]; ty = cs[1]; tz = cs[2]; trad = Units.Km(696000 * (old && cs[4] < 0.1 ? 20 : 1)); name = old ? (cs[4] < 0.1 ? "a red giant of the cluster" : cs[4] < 0.4 ? "an old orange star of the cluster" : "an old yellow star of the cluster") : "a young blue-white star of the cluster"; }
            }
            if (kind < 0 && _near >= 0 && NearShare() > 0.5)
            {
                // nothing there but the space between the stars: that point, at the depth of the nearest thing, to fly to and into its gas
                double depth = Math.Max(Units.Km(1000), NearestDistance());
                _tKind = 5; _tIndex = -1; _tGal = _near; _tx = _ax + _ox + dir.X * depth; _ty = _ay + _oy + dir.Y * depth; _tz = _az + _oz + dir.Z * depth; _tRadius = 0;
                _tName = "a point in the space between the stars"; return;
            }
            if (kind < 0) return;
            var layHere = LayerHere();
            if (layHere != null && layHere.Matter != "core" && (kind < 0 || kind == 3))
            {
                // inside a giant, a click is a point in the layer you are in, just ahead where you clicked (its gas, its plasma, its
                // hot dense water): the wheel flies to it and on into that layer's own matter
                double reachU = Units.Km(1000); kind = 7; idx = 0; tx = _ax + _ox + dir.X * reachU; ty = _ay + _oy + dir.Y * reachU; tz = _az + _oz + dir.Z * reachU; trad = 0; name = "a point in its " + layHere.Name.ToLowerInvariant();
            }
            _tKind = kind; _tIndex = idx; _tx = tx; _ty = ty; _tz = tz; _tRadius = trad; _tName = name; if (kind == 5) _tGal = pickGal;
        }

        public bool Update(Input input, double dt, int w, int h)
        {
            Mark();
            if (_tour != null) TourStep(input, dt, w, h);
            _matter.Glide = _tour != null ? 1.2 : 4;                                     // on the tour the matter glides as a film camera would                                  // a tour gives the controls what each stop asks
            // a click (pressed and let go without dragging) picks what is under the mouse
            if (input.LeftClicked) { _pressed = true; _dragged = false; _pressX = input.MouseX; _pressY = input.MouseY; }
            if (_pressed && input.LeftDown && Math.Abs(input.MouseX - _pressX) + Math.Abs(input.MouseY - _pressY) > 6) _dragged = true;   // dragging circles round the pick (or looks around if nothing is picked)
            if (_pressed && !input.LeftDown) { _pressed = false; if (!_dragged)
                {
                    if (_matterScale > 0 || _spaceScale > 0) _matter.Pick(input.MouseX, input.MouseY);    // in the matter or the gas: what is there
                    else if (_ground != null && AboveGround() < Units.Km(MatterView.OverlayKm) && _matter.Pick(input.MouseX, input.MouseY))
                    { }   // a grain over the ground: picked, its card shown; the ground itself carries you on down to it (never a second layer of grains)   // a grain over the ground: on into the matter, the same picture, flying to it
                    else Pick(MouseRay(input.MouseX, input.MouseY, w, h), input.MouseX, input.MouseY);
                } }
            LevelCamera(dt);
            // the camera turns smoothly to face what you picked
            double tdx, tdy, tdz, trad;
            bool amongGrains = _ground != null && _matterScale <= 0 && AboveGround() < Units.Km(MatterView.OverlayKm);
            if (amongGrains && !(_pressed && _dragged) && !TourLooking)
            {
                // among the grains: the view looks down onto the point you dive into (the way you face kept), so it, and what you
                // pick there (which eases to it), sits in the middle of the screen
                double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                double ux = x / r, uy = y / r, uz = z / r; var lk0 = Look; double d0 = lk0.X * ux + lk0.Y * uy + lk0.Z * uz;
                double hx = lk0.X - ux * d0, hy = lk0.Y - uy * d0, hz = lk0.Z - uz * d0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                if (hl < 1e-6) { hx = -uz; hy = 0; hz = ux; hl = Math.Sqrt(hx * hx + hz * hz); if (hl < 1e-6) { hx = 1; hl = 1; } }
                double dn = Math.Sin(88 * Math.PI / 180), fl = Math.Cos(88 * Math.PI / 180);
                double wx = hx / hl * fl - ux * dn, wy = hy / hl * fl - uy * dn, wz = hz / hl * fl - uz * dn;
                EaseLook(wx, wy, wz, 1 - Math.Exp(-dt * 4));
            }
            else if (_tKind >= 0 && _matterScale <= 0 && _spaceScale <= 0 && !(_pressed && _dragged) && !TourLooking && TargetFromCamera(out tdx, out tdy, out tdz, out trad))   // (in the matter or the gas, an earlier pick out in the world does not turn you)
            {
                double l = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
                if (l > 0 && _tKind == 2 && _tIndex == _planet && _ground != null)
                {
                    // coming down to the picked planet: from a tenth of its radius the view levels out from its middle toward
                    // its horizon (about 15 degrees below it), keeping the way you face, so you see the land ahead, not your feet
                    double alt = AboveGround() / Units.Km(1), lev = Math.Max(0, Math.Min(1, 1.2 - alt / (_ground.Radius * 0.5)));   // (from about half its radius up: the world's curve and the land ahead, never straight down)
                    lev = lev * lev * (3 - 2 * lev);
                    double ux = -tdx / l, uy = -tdy / l, uz = -tdz / l;                 // up: from the planet's middle to you
                    var lk0 = Look; double dot = lk0.X * ux + lk0.Y * uy + lk0.Z * uz;
                    double hx = lk0.X - ux * dot, hy = lk0.Y - uy * dot, hz = lk0.Z - uz * dot, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    if (hl < 1e-6) { hx = -uz; hy = 0; hz = ux; hl = Math.Sqrt(hx * hx + hz * hz); if (hl < 1e-6) { hx = 1; hl = 1; } }
                    double down = Math.Sin(20 * Math.PI / 180), flat = Math.Cos(20 * Math.PI / 180);
                    double wx = hx / hl * flat - ux * down, wy = hy / hl * flat - uy * down, wz = hz / hl * flat - uz * down;
                    tdx = tdx / l * (1 - lev) + wx * lev; tdy = tdy / l * (1 - lev) + wy * lev; tdz = tdz / l * (1 - lev) + wz * lev;
                    l = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
                }
                if (l > 0)
                {
                    EaseLook(tdx, tdy, tdz, 1 - Math.Exp(-dt * (_tour != null ? 0.6 : 5)));   // (on the tour, a slow eased turn, as a camera operator would)
                }
            }
            if (AutoNebula)
            {
                // the test path into a nebula: to the galaxy, pick its nearest emission nebula, and roll the wheel on in
                if (_tKind != 5 && _near >= 0 && _features.Count > 0 && NearShare() > 0.3)
                {
                    var g = _galaxies[_near]; int bi = -1; double bd = double.MaxValue;
                    for (int q = 0; q < _features.Count; q++)
                    {
                        if (_features[q][4] != 2) continue;
                        double ux, uy, uz; ToUniverse(g, _features[q][0], _features[q][1], _features[q][2], out ux, out uy, out uz);
                        double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, d = dx * dx + dy * dy + dz * dz;
                        if (d < bd) { bd = d; bi = q; }
                    }
                    if (bi >= 0) { double ux, uy, uz; ToUniverse(g, _features[bi][0], _features[bi][1], _features[bi][2], out ux, out uy, out uz); _tGal = _near; _tKind = 5; _tIndex = bi; _tx = ux; _ty = uy; _tz = uz; _tRadius = _features[bi][3] * g.Scale; _tName = "an emission nebula"; }
                }
                if (_tKind != 5) AimNext();
                input.Wheel += 2;
                _autoLogT += dt;
                if (_autoLogT > 0.5) { _autoLogT = 0; ZoomLog("auto: space " + ScaleHud.Words(_spaceScale * 0.6) + " pick " + _tKind); }
            }
            if (AutoPath)
            {
                // the test path: pick a planet as a click would, roll the wheel in, and write where the camera is each second
                if (_system != null && _tKind != 2 && _ground == null)
                {
                    int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Kind == "Temperate world")) pick = i;
                    if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; }
                }
                if (_tKind != 2) AimNext();
                input.Wheel += 1;
                _autoLogT += dt;
                if (_autoLogT > 1) { _autoLogT = 0; try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dna_autopath.log"), Title + Environment.NewLine); } catch { } }
            }
            if (input.RightClicked) { _matter.Unpick(); if (_spaceScale <= 0) { _tKind = -1; _tName = ""; } }   // lets go of everything picked (in a nebula's gas the nebula stays where you are)                    // right click: let go of what you picked
            if (input.WasPressed(0x48)) { _hud.Visible = !_hud.Visible; MenuChanged = true; }   // H: the scale on screen, on or off
            if (input.WasPressed(0x42) && (_matterScale > 0 || _spaceScale > 0)) _matter.PickNearest("bonded-atom");   // B: glide across the bond to the next atom
            if (input.WasPressed(Win32.VK_ESCAPE)) return false;
            if (input.LeftDown && (input.MouseDX != 0 || input.MouseDY != 0))
            {
                // one rule for dragging at every scale: the camera goes the way the mouse goes (drag down, the view pans down;
                // drag left, it turns left), by as much as the mouse moved across the view; with something picked, the camera
                // circles round it the same way. The View menu can flip either way.
                int dxm = Settings.Get("invert-left-right", false) ? -input.MouseDX : input.MouseDX, dym = Settings.Get("invert-up-down", false) ? -input.MouseDY : input.MouseDY;
                var d1 = MouseRay(input.MouseX, input.MouseY, w, h); var d0 = MouseRay(input.MouseX - dxm, input.MouseY - dym, w, h);
                var axis = Vec3.Cross(d1, d0); float al = axis.Length;
                if (al > 1e-9f)
                {
                    axis = new Vec3(axis.X / al, axis.Y / al, axis.Z / al);
                    double ang = -Math.Atan2(al, Vec3.Dot(d1, d0)) * Window.DragSpeed;   // the camera goes the way the mouse goes, as fast as View > Drag speed sets
                    var f = Turn(Look, axis, ang);
                    double odx, ody, odz, orad;
                    bool circle = _tKind >= 0 && _tKind != 3 && _dragged && !(_ground != null && (_matterScale > 0 || AboveGround() < Units.Km(MatterView.OverlayKm)))
                                  && TargetFromCamera(out odx, out ody, out odz, out orad) && odx * odx + ody * ody + odz * odz > 0;
                    if (circle)
                    {
                        // round the pick: the camera's place from it turned the same way, still facing it
                        TargetFromCamera(out odx, out ody, out odz, out orad);
                        var o = new Vec3((float)-odx, (float)-ody, (float)-odz); double ol = Math.Sqrt(odx * odx + ody * ody + odz * odz);
                        var on = Turn(new Vec3((float)(o.X / ol), (float)(o.Y / ol), (float)(o.Z / ol)), axis, ang);
                        _ox += on.X * ol + odx; _oy += on.Y * ol + ody; _oz += on.Z * ol + odz;
                        f = new Vec3(-on.X, -on.Y, -on.Z);
                    }
                    SetLook(f);
                }
            }
            Jumped("dragging");
            SpawnRegions(); TakeGalaxies(); HoldInside();                                   // the galaxies round you (their stars made in the background, a few taken to the card each frame), and the edge of the visible universe
            double reach = NearestDistance();
            if (_matterScale <= 0)
            {
                // W A S D, Q and E: fly like a first-person game, at a speed scaled to the nearest thing (half its distance a
                // second, Shift four times faster), so it feels the same among galaxies as a metre above the ground
                double mf = (input.IsDown(0x57) ? 1 : 0) - (input.IsDown(0x53) ? 1 : 0), mr = (input.IsDown(0x44) ? 1 : 0) - (input.IsDown(0x41) ? 1 : 0), mu = (input.IsDown(0x45) ? 1 : 0) - (input.IsDown(0x51) ? 1 : 0);
                if (mf != 0 || mr != 0 || mu != 0)
                {
                    _tKind = -1;                                                       // flying yourself lets go of the pick
                    var f = Look; var rt = _haveAxes ? _viewRight : Vec3.Cross(f, ViewUp()).Normalized; var upv = _haveAxes ? _viewUp : Vec3.Cross(rt, f).Normalized;
                    double sp = Math.Max(reach, _farScale) * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1);   // at the scale you are zoomed out to
                    _ox += (f.X * mf + rt.X * mr + upv.X * mu) * sp; _oy += (f.Y * mf + rt.Y * mr + upv.Y * mu) * sp; _oz += (f.Z * mf + rt.Z * mr + upv.Z * mu) * sp;
                    double fk = _ground != null ? Math.Max(5e-7, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 0.6 : 0) : 0;
                    if (_ground != null && AboveGround() < Units.Km(MatterView.OverlayKm)) fk = 5e-7;
                    if (_ground != null && AboveGround() < Units.Km(fk))
                    {
                        // never through the ground, nor into its drawn pieces: back up above them
                        double x, y, z; CamFromPlanet(out x, out y, out z);
                        double r = Math.Sqrt(x * x + y * y + z * z), g = _ground.Solid ? Surface(x / r, y / r, z / r) : 0;
                        double want = _ground.Radius + g + fk, k = Units.Km(1);
                        _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    }
                }
            }
            Jumped("W A S D Q E");
            if (_spaceScale > 0)
            {
                // in a nebula's or cluster's gas: the same zoom, kept finely enough for atoms; out past where it began, back to space
                if (input.Wheel != 0) _spaceScale *= Math.Pow(0.87, input.Wheel);
                if (_spaceScale > _spaceTop) _spaceScale = 0; else _spaceScale = Math.Max(MatterView.BottomKm, _spaceScale);
                if (input.WasPressed(0x20)) TogglePause();
                if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);
                if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);
                _simYears += dt * TimeSpeeds[TimeStep];
                if (TimeStep > 0) _matterClock += dt;
                return true;
            }
            if (_matterScale > 0)
            {
                // below a millimetre or so: the same zoom, with your height kept finely enough for atoms (the wheel moves
                // you the same share of the way each notch; W A S D Q E fly at the same speed for the scale; time runs on)
                if (input.Wheel != 0) _matterScale *= Math.Pow(0.87, input.Wheel);
                {
                    double mf = (input.IsDown(0x57) ? 1 : 0) - (input.IsDown(0x53) ? 1 : 0), mr = (input.IsDown(0x44) ? 1 : 0) - (input.IsDown(0x41) ? 1 : 0), mu = (input.IsDown(0x45) ? 1 : 0) - (input.IsDown(0x51) ? 1 : 0);
                    if (mf != 0 || mr != 0 || mu != 0)
                    {
                        double sp = _matterScale * 0.6 * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1);   // half your height a second, as above the ground
                        double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                        var upL = new Vec3((float)(x / r), (float)(y / r), (float)(z / r));
                        var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
                        var lk = Look; double fe = Vec3.Dot(lk, east), fn = Vec3.Dot(lk, north), fl = Math.Sqrt(fe * fe + fn * fn);
                        if (fl < 1e-6) { fe = 1; fn = 0; fl = 1; }
                        fe /= fl; fn /= fl;
                        _matter.Move((fe * mf + fn * mr) * sp, (fn * mf - fe * mr) * sp);
                        if (mu != 0) _matterScale *= Math.Exp(mu * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1));
                    }
                }
                if (input.WasPressed(0x20)) TogglePause();
                if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);
                if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);
                _simYears += dt * TimeSpeeds[TimeStep];
                if (TimeStep > 0) _matterClock += dt;
                if (_matterScale > _matterTop)
                {
                    // high enough again for the plain height: the camera goes on from exactly the height the fine one had
                    double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z), k = Units.Km(1);
                    double want = _ground.Radius + (_ground.Solid ? Surface(x / r, y / r, z / r) : 0) + _matterScale * 0.6;
                    _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    _matterScale = 0;
                }
                else _matterScale = Math.Max(MatterView.BottomKm, _matterScale);
                ChooseNear(); LocalStars(); ChooseSystem(); FollowBody(); ChoosePlanet();
                return true;
            }
            if (_ground != null && input.Wheel > 0 && AboveGround() < Units.Km(HandoffKm()))   // (the ground itself, its grains its own, carries you down to about 2 micrometres: on one grain's face)
            {
                // at the finest ground: on down into what it is made of
                double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                bool sea = _ground.HasWater && _ground.GroundAt(x / r, y / r, z / r) < 0, ice = Math.Abs(y / r) > 0.86;
                SetMatter();
                _matterScale = Math.Max(MatterView.BottomKm, AboveGround() / Units.Km(1) / 0.6);   // the same picture carries on: you are 0.6 of the scale above it
                _matterTop = Math.Max(MatterView.TopKm, _matterScale * 1.1);
                return true;
            }
            if (_tKind == 7 && input.Wheel > 0 && _spaceScale <= 0 && _matterScale <= 0)
            {
                double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && Math.Sqrt(qx * qx + qy * qy + qz * qz) < Units.Km(1))
                {
                    var lay = LayerHere(); ulong lspot = (ulong)(_tx / Units.Km(1)) ^ (ulong)(_tz / Units.Km(1)) * 31UL;
                    string air; if (!PlanetTerrain.Air.TryGetValue(_ground != null ? _ground.Kind : "", out air)) air = "hydrogen 90%, helium 10%";
                    if (lay != null && lay.Matter == "plasma") _matter.SetPlasma(lspot);
                    else if (lay != null && lay.Matter == "ice") { _matter.SetAir("water vapour 70%, ammonia 15%, methane 15%", lspot); }   // hot dense water, ammonia and methane
                    else _matter.SetAir(air, lspot);
                    _spaceScale = Math.Max(MatterView.BottomKm, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;
                    return true;
                }
            }
            if (_system != null && _ground == null && input.Wheel > 0 && _spaceScale <= 0 && Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) < _system.StarRadius * 1.002 && Math.Abs(_ax) + Math.Abs(_ay) + Math.Abs(_az) >= 0)
            {
                // into the star's surface: its plasma, the same zoom carrying on
                ulong sspot = (ulong)(long)(_ox / Units.Km(1e3)) * 73856093UL ^ (ulong)(long)(_oy / Units.Km(1e3)) * 19349663UL ^ (ulong)(long)(_oz / Units.Km(1e3)) * 83492791UL;
                _matter.SetPlasma(sspot);
                _spaceScale = 1.0 / 0.6; _spaceTop = _spaceScale * 1.1;                   // from a kilometre: a fog of plasma, down to its nuclei
                return true;
            }
            if (_tKind == 5 && input.Wheel > 0)
            {
                // into a picked nebula or cluster: once within a kilometre of the point you fly to, on into its gas, the same zoom
                double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && Math.Sqrt(qx * qx + qy * qy + qz * qz) < Units.Km(1))
                {
                    var gq = _galaxies[_near];
                    if (_tIndex < 0 || _tGal != _near) _matter.SetSpace(9, (ulong)(_tx / Units.Km(1e6)) ^ gq.Seed, 1);   // the space between the stars
                    else
                    {
                        int fk = (int)_features[_tIndex][4];
                        var fq = _features[_tIndex]; double cux, cuy, cuz; ToUniverse(gq, fq[0], fq[1], fq[2], out cux, out cuy, out cuz);
                        double fromC = Math.Sqrt((_tx - cux) * (_tx - cux) + (_ty - cuy) * (_ty - cuy) + (_tz - cuz) * (_tz - cuz)) / Math.Max(1e-30, fq[3] * gq.Scale);   // how far from its middle you dive in
                        _matter.SetSpace(fk, (ulong)(_tIndex * 7919 + 1) ^ gq.Seed, Math.Min(1, fromC), fq.Length > 5 ? SpaceGen.Composition(fk, fq[5]) : null);
                    }
                    _spaceScale = Math.Max(MatterView.BottomKm, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;
                    return true;
                }
            }
            if (input.Wheel != 0)
            {
                // fly along the line under the mouse, by a share of the distance to the nearest thing: slower as it comes close, never ending
                var dir = MouseRay(input.MouseX, input.MouseY, w, h);
                double fdx, fdy, fdz, frad;
                if (_tKind >= 0 && TargetFromCamera(out fdx, out fdy, out fdz, out frad))
                {
                    // flying to what you picked: straight at it, a share of the way to its surface each notch
                    double fl = Math.Sqrt(fdx * fdx + fdy * fdy + fdz * fdz);
                    // the steps go by the distance to what you picked only: stars you fly past no longer hold them back
                    if (fl > 0)
                    {
                        dir = new Vec3((float)(fdx / fl), (float)(fdy / fl), (float)(fdz / fl));
                        // at the picked planet's ground, the steps go by the height above the ground, all the way down;
                        // from afar, by the distance to just outside it
                        bool atIt = _tKind == 2 && _tIndex == _planet && _ground != null;
                        reach = atIt ? AboveGround() : Math.Max(fl - (_tKind == 5 ? 0 : frad * 1.2), fl * 1e-6);   // clusters and nebulae are clouds: flown into, not stopped at
                    }
                }
                // a star close to the line you point along (within about 4 degrees) is flown at straight, not past
                if (_tKind < 0) { double tdist; var tdir = AimStar(dir, out tdist); if (input.Wheel > 0 && tdist > 0) { dir = tdir; reach = Math.Min(reach, tdist); } }
                double step = reach * (1 - Math.Pow(0.87, Math.Abs(input.Wheel))) * Math.Sign(input.Wheel);
                // out: each notch a share of how far out you have already zoomed (not only of the nearest thing), so going on out
                // grows without end, to the whole universe; in: that scale comes back down toward the nearest thing
                if (input.Wheel < 0)
                {
                    double basis = Math.Max(Math.Max(reach, Units.Km(1e-9)), _farScale);
                    double most = _uRadius * 0.1;                                                  // never so far out that the universe is one ball: always inside it, among the web
                    if (basis >= most) { step = 0; _farScale = most; }
                    else { double nb = Math.Min(most, basis * Math.Pow(1 / 0.87, -input.Wheel)); step = -(nb - basis); _farScale = nb; }
                }
                else { _farScale *= Math.Pow(0.87, input.Wheel); if (_farScale < reach) _farScale = 0; }
                double altBefore = _ground != null ? AboveGround() / Units.Km(1) : 0;
                ZoomLog("wheel " + input.Wheel.ToString("0") + " step " + Units.Describe(Math.Abs(step)) + " reach " + Units.Describe(reach) + " pick " + _tKind + ":" + _tIndex + " planet " + _planet + " nearest-body " + _cpBest + " at " + _cpBd.ToString("0.##") + " radii follow " + _followBody + (_ground != null ? " ground-ready " + (_ground.ReadyStep < 1e30 ? ScaleHud.Words(_ground.ReadyStep) : "none") + " height " + ScaleHud.Words(AboveGround() / Units.Km(1)) + " [" + _ground.BuildStats + "]" : ""));
                _ox += dir.X * step; _oy += dir.Y * step; _oz += dir.Z * step;
                if (_ground != null)
                {
                    // slow down near the finest ground already made under you (so the rough first pieces are not stretched
                    // across the screen), but never push you back and always let each notch close in a tenth, so the finer
                    // ground is asked for and catches up; never below half a millimetre
                    double ready = _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 3 : 0;
                    // ...but never inside the drawn ground: its pieces are flat triangles about ReadyStep across that can stand above
                    // the true ground, so stay at least 0.6 of a step above it until finer pieces are made (close enough to ask for them)
                    double floorKm = Math.Max(5e-7, Math.Min(ready, Math.Max(altBefore * 0.9, ready / 3 * 0.6)));
                    if (altBefore < MatterView.OverlayKm) floorKm = 1e-9;                 // below a centimetre the grains cover the ground: no waiting, on down past the 30 micrometres where the matter takes over
                    {
                        // over the sea: no lower than a metre above the water, until the dive under the sea is in the one world
                        double cx0, cy0, cz0; CamFromPlanet(out cx0, out cy0, out cz0); double r0 = Math.Sqrt(cx0 * cx0 + cy0 * cy0 + cz0 * cz0);
                        // over the sea the floor is the water itself, not the sea bed far below (whose pieces stay coarse): down to a millimetre
                        if (_ground.HasWater && _ground.GroundAt(cx0 / r0, cy0 / r0, cz0 / r0) < 0) floorKm = 1e-9;
                    }
                    if (AboveGround() < Units.Km(floorKm))
                    {
                        double x, y, z; CamFromPlanet(out x, out y, out z);
                        double r = Math.Sqrt(x * x + y * y + z * z), g = _ground.Solid ? Surface(x / r, y / r, z / r) : 0;
                        double want = _ground.Radius + g + floorKm, k = Units.Km(1);
                        _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    }
                }
            }
            Jumped("the wheel");
            if (input.WasPressed(0x20)) TogglePause();                                 // Space: pause, or carry on
            if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);                        // , slower
            if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);                        // . faster
            _simYears += dt * TimeSpeeds[TimeStep];                                    // time runs at the speed chosen
            if (TimeStep > 0) _matterClock += dt;
            ChooseNear(); Jumped("ChooseNear"); ClusterStars();
            LocalStars(); Jumped("LocalStars");
            ChooseSystem(); Jumped("ChooseSystem");
            // ride along with the body first, then see which one you are at: planets move about a hundred thousand km a frame,
            // so choosing before riding along saw you many radii behind (a moon never got its ground, and flickered)
            FollowBody(); Mark();                                                // riding along is meant to move you far: not logged as a jump
            ChoosePlanet(); Jumped("ChoosePlanet");
            return true;
        }

        // the star nearest the line along dir (local stars, or the open system's star): its direction and distance (else 0)
        private Vec3 AimStar(Vec3 dir, out double dist)
        {
            dist = 0; var best = dir; double bestCos = Math.Cos(4 * Math.PI / 180);
            if (_system != null)
            {
                // in a system: the star or any planet or moon near the line you point along (the one nearest the line wins)
                PlaceSystem();
                for (int i = 0; i < _bodyCount; i++)
                {
                    double r = i == 0 ? _system.StarRadius : _system.Bodies[i - 1].RadiusUnits;
                    double dx = _bodyVerts[i].X - _ox, dy = _bodyVerts[i].Y - _oy, dz = _bodyVerts[i].Z - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (l <= 0) continue;
                    double c = (dx * dir.X + dy * dir.Y + dz * dir.Z) / l;
                    if (c > bestCos) { bestCos = c; best = new Vec3((float)(dx / l), (float)(dy / l), (float)(dz / l)); dist = Math.Max(l - r * 1.5, r * 0.05); }
                }
                return best;
            }
            if (_near < 0) return best;
            var g = _galaxies[_near];
            for (int k = 0; k < _localCount; k++)
            {
                var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (l <= 0) continue;
                double c = (dx * dir.X + dy * dir.Y + dz * dir.Z) / l;
                if (c > bestCos) { bestCos = c; best = new Vec3((float)(dx / l), (float)(dy / l), (float)(dz / l)); dist = l; }
            }
            return best;
        }

        // v turned by angle a (radians) round the unit axis k
        // the tour's own turn: by exact fractions of a degree each frame, smooth (the mouse moves in whole pixels, which at a
        // slow turn would jump a little every few frames: a judder that makes people sick); round the pick as a drag circles it
        private void TourTurn(double yawRad, double pitchRad)
        {
            var lk = Look; var upv = LevelUp(); var rt = Vec3.Cross(lk, ViewUp()).Normalized;
            // close over the picked world (under half its radius up) the view turns where you are, never circling the whole world
            bool overIt = _ground != null && _tKind == 2 && _tIndex == _planet && AboveGround() / Units.Km(1) < _ground.Radius * 0.5;
            for (int pass = 0; pass < 2; pass++)
            {
                double ang = pass == 0 ? yawRad : pitchRad; if (Math.Abs(ang) < 1e-12) continue;
                double odx, ody, odz, orad;
                bool circle = _tKind >= 0 && _tKind != 3 && !overIt && !(_ground != null && (_matterScale > 0 || AboveGround() < Units.Km(MatterView.OverlayKm)))
                              && TargetFromCamera(out odx, out ody, out odz, out orad) && odx * odx + ody * ody + odz * odz > 0
                              && Math.Sqrt(odx * odx + ody * ody + odz * odz) <= Math.Max(orad * 12, NearestDistance() * 3);   // (a film circles only what is close and fills the view; far off, circling would sweep the camera across whole stretches of space, everything near streaming past: there it turns where it is)
                // circling round a pick: about the view's own up (always square to the line to it, so it never rolls in place when
                // seen from straight above); turning where you are: about the level up (the horizon stays level), or the view's own up
                // when looking nearly along it
                var axis = pass == 1 ? rt : circle || Math.Abs(Vec3.Dot(lk, upv.Normalized)) > 0.95f ? ViewUp() : upv.Normalized;
                var f = Turn(Look, axis, ang);
                if (circle)
                {
                    TargetFromCamera(out odx, out ody, out odz, out orad);
                    var o = new Vec3((float)-odx, (float)-ody, (float)-odz); double ol = Math.Sqrt(odx * odx + ody * ody + odz * odz);
                    var on = Turn(new Vec3((float)(o.X / ol), (float)(o.Y / ol), (float)(o.Z / ol)), axis, ang);
                    _ox += on.X * ol + odx; _oy += on.Y * ol + ody; _oz += on.Z * ol + odz;
                    f = new Vec3(-on.X, -on.Y, -on.Z);
                }
                SetLook(f);
            }
        }
        // how far (radians) the view is from facing what is picked; 0 with nothing picked
        private double OffTarget()
        {
            if (_tKind == 2 && _ground != null && _tIndex == _planet) return 0;          // (coming down to the picked world the view looks ahead to its horizon, not at its middle: that is on course)
            double dx, dy, dz, r; if (_tKind < 0 || !TargetFromCamera(out dx, out dy, out dz, out r)) return 0;
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0) return 0; var lk = Look;
            return Math.Acos(Math.Max(-1, Math.Min(1, (dx * lk.X + dy * lk.Y + dz * lk.Z) / l)));
        }

        private static Vec3 Turn(Vec3 v, Vec3 k, double a)
        {
            float c = (float)Math.Cos(a), sn = (float)Math.Sin(a), d = Vec3.Dot(k, v); var x = Vec3.Cross(k, v);
            return new Vec3(v.X * c + x.X * sn + k.X * d * (1 - c), v.Y * c + x.Y * sn + k.Y * d * (1 - c), v.Z * c + x.Z * sn + k.Z * d * (1 - c)).Normalized;
        }

        // the view's up: the universe's up in space; near a planet's ground, the planet's own up (so its horizon is level)
        // on a world, how far above its horizon you look (radians, up from the planet's middle through you); off a world, the
        // view's own pitch
        private double LocalPitch()
        {
            if (_ground == null) return Math.Asin(Math.Max(-1, Math.Min(1, _fy)));
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return Math.Asin(Math.Max(-1, Math.Min(1, _fy)));
            var lk = Look; return Math.Asin(Math.Max(-1, Math.Min(1, (lk.X * x + lk.Y * y + lk.Z * z) / r)));
        }
        // turns the view up or down toward a pitch above the horizon (on a world, its own horizon), eased by k
        private void TourPitchToward(double want, double k) { TourTurn(0, (want - LocalPitch()) * k); }
        // the camera's own up, kept from frame to frame and square to the way it looks: looking straight up or down it simply
        // holds, never flipping (no gimbal lock)
        private Vec3 ViewUp()
        {
            var f = Look; var u = _camUp; float d = Vec3.Dot(u, f);
            var p = new Vec3(u.X - f.X * d, u.Y - f.Y * d, u.Z - f.Z * d);
            if (p.Length < 1e-4f) { var a = Math.Abs(f.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0); float e = Vec3.Dot(a, f); p = new Vec3(a.X - f.X * e, a.Y - f.Y * e, a.Z - f.Z * e); }
            return p.Normalized;
        }
        // each frame the camera's up eases toward level (the universe's up; near a world, its own up), except while looking
        // nearly along it, where it holds as it is
        private void LevelCamera(double dt)
        {
            var f = Look; var want = LevelUp(); float d = Vec3.Dot(want, f);
            var p = new Vec3(want.X - f.X * d, want.Y - f.Y * d, want.Z - f.Z * d); float pl = p.Length;
            var u = ViewUp();
            if (pl > 1e-4f)
            {
                double w = Math.Max(0, Math.Min(1, (pl - 0.05) / 0.25)) * (1 - Math.Exp(-dt * 10)); p = new Vec3(p.X / pl, p.Y / pl, p.Z / pl);
                u = new Vec3(u.X + (p.X - u.X) * (float)w, u.Y + (p.Y - u.Y) * (float)w, u.Z + (p.Z - u.Z) * (float)w);
            }
            _camUp = u; _camUp = ViewUp();
        }
        private Vec3 LevelUp()
        {
            var wu = new Vec3(0, 1, 0);
            if (_ground == null) return wu;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return wu;
            double alt = r - _ground.Radius, k = Math.Max(0, Math.Min(1, 1 - alt / (_ground.Radius * 0.1)));
            var lu = new Vec3((float)(x / r), (float)(y / r), (float)(z / r));
            var u = new Vec3(wu.X * (float)(1 - k) + lu.X * (float)k, wu.Y * (float)(1 - k) + lu.Y * (float)k, wu.Z * (float)(1 - k) + lu.Z * (float)k);
            return u.Normalized;
        }

        private Vec3 _viewRight, _viewUp; private bool _haveAxes;
        private Vec3 MouseRay(int mx, int my, int w, int h)
        {
            double nx = mx / (double)Math.Max(1, w) * 2 - 1, ny = 1 - my / (double)Math.Max(1, h) * 2, t = Math.Tan(0.45), aspect = w / (double)Math.Max(1, h);
            // the screen's own right and up, as the view was last drawn (so the line goes exactly through the pixel clicked)
            var f = Look; var right = _haveAxes ? _viewRight : Vec3.Cross(f, ViewUp()).Normalized; var up = _haveAxes ? _viewUp : Vec3.Cross(right, f).Normalized;
            return (f + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
        }

        // the galaxy whose real stars are shown: the nearest one, once the camera is within a few of its sizes
        private void ChooseNear()
        {
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale;
                if (d < bd) { bd = d; best = i; }
            }
            if (bd > 8) best = -1;
            EnsureMade(best);                                                          // (the galaxy you come near is made at once)
            if (best != _near)
            {
                _near = best; CloseSystem(); _localCells.Clear(); _localKey = long.MinValue; _localCount = 0;
                if (_nearBuf != null) { _nearBuf.Dispose(); _nearBuf = null; }
                _nearLocal = null;
                if (best >= 0)
                {
                    var g = _galaxies[best];
                    // the same stars as its far glow, at once; the rest of its stars are made in the background and added after them
                    _nearKinds = (int[])g.FarKinds.Clone();
                    _nearLocal = (StarVertex[])g.FarPts.Clone();
                    _nearDrawn = _nearLocal;
                    _nearBuf = new UploadBuffer(_gpu.Device, (long)NearStars * Stride); _nearBuf.Write(_nearLocal, _nearLocal.Length);
                    int forGalaxy = best; var gt = g.Type; var gs = g.Seed ^ 0xE47A5UL;
                    Jobs.Run(() =>
                    {
                        var ek = new int[NearStars - FarStars];
                        var extra = GalaxyGen.Generate(gt, gs, NearStars - FarStars, _kinds, ek);
                        lock (_extraLock) { _extraFor = forGalaxy; _extra = extra; _extraKinds = ek; }
                    });
                    BuildGrid();
                    // its star clusters and nebulae, in place among its stars
                    EnsureStruct(g); _features = g.Features; _structBuf = g.Struct; _structCount = g.StructCount;
                }
            }
            // the rest of the near galaxy's stars, when they are ready: added after the ones already shown (nothing changes on screen)
            StarVertex[] ex = null; int[] exk = null;
            lock (_extraLock) { if (_extra != null && _extraFor == _near) { ex = _extra; exk = _extraKinds; } if (_extra != null) { _extra = null; _extraKinds = null; } }
            if (ex != null && _nearLocal != null && _nearLocal.Length == FarStars)
            {
                var all = new StarVertex[FarStars + ex.Length]; Array.Copy(_nearLocal, all, FarStars); Array.Copy(ex, 0, all, FarStars, ex.Length);
                var allK = new int[all.Length]; Array.Copy(_nearKinds, allK, FarStars); Array.Copy(exk, 0, allK, FarStars, exk.Length);
                _nearLocal = all; _nearKinds = allK; _nearDrawn = all; _nearBuf.Write(all, all.Length);
                BuildGrid();
            }
        }

        // inside a star cluster: its own stars made round you at its real density (a globular cluster's core about 1,000 stars a
        // cubic light-year, thinning outward; an open cluster's a few), stable (each from its own cell), in universe units
        private readonly List<double[]> _cstars = new List<double[]>(); private UploadBuffer _cstarBuf; private StarVertex[] _cstarV = new StarVertex[0];
        private int _cstarFeature = -1; private double _cgx = double.NaN, _cgy, _cgz, _cstarHalf;
        private void ClusterStars()
        {
            int inF = -1; double ly = Units.Km(9.4607e12);
            if (_near >= 0)
            {
                var g = _galaxies[_near];
                for (int q = 0; q < _features.Count; q++)
                {
                    if (_features[q][4] > 1) continue;                                         // clusters only
                    double ux, uy, uz; ToUniverse(g, _features[q][0], _features[q][1], _features[q][2], out ux, out uy, out uz);
                    double dx = ux - (_ax + _ox), dy = uy - (_ay + _oy), dz = uz - (_az + _oz), R = _features[q][3] * g.Scale;
                    if (dx * dx + dy * dy + dz * dz < R * R * 2.25) { inF = q; break; }
                }
            }
            if (inF < 0) { _cstars.Clear(); _cstarFeature = -1; return; }
            var gg = _galaxies[_near]; var ft = _features[inF]; double cx, cy, cz; ToUniverse(gg, ft[0], ft[1], ft[2], out cx, out cy, out cz);
            double Rc = ft[3] * gg.Scale, core = Rc * (ft[4] == 0 ? 0.08 : 0.3), n0 = ft[4] == 0 ? 1000 : 3;   // stars a cubic light-year at its middle
            double px = _ax + _ox - cx, py = _ay + _oy - cy, pz = _az + _oz - cz, r2 = px * px + py * py + pz * pz;
            double nHere = n0 * Math.Pow(1 + r2 / (core * core), -2.5);                       // thinning outward (a Plummer sphere)
            double half = Math.Max(0.05 * ly, Math.Pow(4000 / Math.Max(1e-6, nHere), 1 / 3.0) * 0.5 * ly);   // about 4,000 stars round you
            if (inF == _cstarFeature && !double.IsNaN(_cgx) && Math.Abs(px - _cgx) + Math.Abs(py - _cgy) + Math.Abs(pz - _cgz) < half * 0.25 && Math.Abs(half / _cstarHalf - 1) < 0.5) return;
            _cstarFeature = inF; _cgx = px; _cgy = py; _cgz = pz; _cstarHalf = half; _cstars.Clear();
            double cell = half / 4; long i0 = (long)Math.Floor(px / cell), j0 = (long)Math.Floor(py / cell), k0 = (long)Math.Floor(pz / cell);
            for (long i = i0 - 4; i <= i0 + 4; i++)
                for (long j = j0 - 4; j <= j0 + 4; j++)
                    for (long k = k0 - 4; k <= k0 + 4; k++)
                    {
                        double mx = (i + 0.5) * cell, my = (j + 0.5) * cell, mz = (k + 0.5) * cell;
                        double expect = n0 * Math.Pow(1 + (mx * mx + my * my + mz * mz) / (core * core), -2.5) * Math.Pow(cell / ly, 3);
                        ulong h = (ulong)(i * 73856093L ^ j * 19349663L ^ k * 83492791L ^ inF * 2654435761L) ^ gg.Seed;
                        var rng = new Rng(h); int count = (int)Math.Floor(expect) + (rng.NextDouble() < expect - Math.Floor(expect) ? 1 : 0);
                        for (int c = 0; c < count && _cstars.Count < 12000; c++)
                        {
                            double sx = cx + (i + rng.NextDouble()) * cell, sy = cy + (j + rng.NextDouble()) * cell, sz = cz + (k + rng.NextDouble()) * cell;
                            double t = rng.NextDouble(); _cstars.Add(new[] { sx, sy, sz, ft[4], t });
                        }
                    }
        }
        // a galaxy's clusters and nebulae, made once and kept with it
        private void EnsureStruct(Galaxy g)
        {
            if (g.Features != null) return;
            int cc, nc; g.Features = new List<double[]>();
            var st = SpaceGen.Structures(g.Type, g.Seed, _kinds, out cc, out nc, g.Features);
            g.StructCount = st.Length; g.Struct = new UploadBuffer(_gpu.Device, (long)Math.Max(1, st.Length) * Stride); if (st.Length > 0) g.Struct.Write(st, st.Length);
        }
        private int _tGal = -1;                                                           // the galaxy of a picked cluster or nebula
        private UploadBuffer _structBuf; private int _structCount; private List<double[]> _features = new List<double[]>();   // the near galaxy's clusters and nebulae
        private readonly object _extraLock = new object(); private StarVertex[] _extra; private int[] _extraKinds; private int _extraFor = -1;

        // the grid used to find the nearest star of the near galaxy
        private void BuildGrid()
        {
            {
                {
                    _gridHead = new int[GridN * GridN * GridN]; for (int k = 0; k < _gridHead.Length; k++) _gridHead[k] = -1;
                    _gridNext = new int[_nearLocal.Length];
                    for (int k = 0; k < _nearLocal.Length; k++)
                    {
                        int x = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].X))), y = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].Y))), z = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].Z)));
                        int c = (x * GridN + y) * GridN + z; _gridNext[k] = _gridHead[c]; _gridHead[c] = k;
                    }
                }
            }
        }

        // how far into its own stars the near galaxy is (0: only the stars of its far glow, 1: all of its stars), from how close you are
        private double NearShare()
        {
            if (_near < 0) return 0;
            var g = _galaxies[_near]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale;
            double t = (8 - d) / 6.5; t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t);
        }

        // how deep into the near galaxy you are (0 outside, 1 well inside), smoothly
        private double InsideShare()
        {
            if (_near < 0) return 0;
            var gn = _galaxies[_near]; double dx = gn.X - _cx, dy = gn.Y - _cy, dz = gn.Z - _cz, dg = Math.Sqrt(dx * dx + dy * dy + dz * dz) / gn.Scale;
            return Math.Max(0, Math.Min(1, 1.6 - dg));
        }

        // how many of a galaxy's stars to draw: more as it grows on screen, each smaller, so its glow stays the same overall
        private int StarsShown(int i)
        {
            if (i != _near || _nearLocal == null) return _galaxies[i].FarCount;
            return (int)Math.Min(_nearLocal.Length, FarStars + (NearStars - FarStars) * NearShare());
        }

        public void Draw(int w, int h, double time)
        {
            {
                double fine = _spaceScale > 0 ? _spaceScale : _matterScale > 0 ? _matterScale : 1;
                _sky = (float)Math.Max(0, Math.Min(1, Math.Log(fine / 1.7e-8) / Math.Log(1.7e-6 / 1.7e-8)));   // from a millimetre down to 10 microns
            }
            var look = Look;
            Mat4 view = Mat4.LookAt(new Vec3(0, 0, 0), look, ViewUp());
            double reach = Math.Max(1e-12, NearestDistance());
            // the bodies drawn close round you (asteroids, comets, black holes' discs) count too, so the nearest drawing distance never
            // cuts away one you are beside
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 && !ex.Hole) continue;
                double dx = (ex.X - _ax) - _ox, dy = (ex.Y - _ay) - _oy, dz = (ex.Z - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - ex.R * 2;
                if (d > 0 && d < reach) reach = d;
            }
            Mat4 proj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)(reach * 1e-3), (float)(_uRadius * 2.2));   // out to the far side of the visible universe
            Mat4 vp = view * proj;
            _lastVp = vp; _lastW = w; _lastH = h; _haveVp = true;
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            _viewRight = right; _viewUp = up; _haveAxes = true;
            double share = NearShare(), within = InsideShare();
            // inside a galaxy its own stars fill the sky and the other galaxies show fainter behind them (as Andromeda
            // is barely seen with the eye): the others and the web's glow fade to a sixth as you go in
            float others = (float)(1 - 0.45 * Math.Max(0, Math.Min(1, within)));   // (dimmed a little: the other galaxies still show in its sky)
            {
                // the rest of the universe's galaxies, a glow along the cosmic web (remade without the near ones as you move far)
                if (double.IsNaN(_glowAtX) || _glowReady != null || (Math.Abs(_cx - _glowAtX) + Math.Abs(_cy - _glowAtY) + Math.Abs(_cz - _glowAtZ) > _nearUnits * 0.25 && time - _glowAtT > 0.5)) { MakeGlow(); _glowAtT = time; }   // (only to start it, take it in when made, or make it again far away: the card sifts it each frame)
                var gm = Mat4.Identity; gm.M41 = (float)-_cx; gm.M42 = (float)-_cy; gm.M43 = (float)-_cz;
                float webFade = (float)Math.Max(0, Math.Min(1, (0.98 - within) / 0.45)); webFade = webFade * webFade * (3 - 2 * webFade); webFade = 0.5f + 0.5f * webFade;   // (from deep inside a galaxy the others still show in its sky, at half)
                webFade *= (float)Math.Min(1, (System.Diagnostics.Stopwatch.GetTimestamp() - _glowCameAt) / (2.0 * System.Diagnostics.Stopwatch.Frequency));   // (newly made: faded in over two seconds)
                if (webFade > 0.01)   // (deep inside a galaxy its stars and dust hide the web, and it is not drawn; it fades in as you rise out of it, never popping in)
                {
                _web[0] = (float)(2.0e9 / Units.LightYearsPerUnit); _web[1] = (float)_cx; _web[2] = (float)_cy; _web[3] = (float)_cz; _web[4] = (float)(_nearUnits * 0.4);   // (well inside where the galaxies made in full are drawn: the two overlap, so where they are still being made it is never dark)
                DrawPoints(_glowBuf, _glowCount, gm * vp, right, up, 1f, 0.0035f, w, h, time, 0.007f, others * webFade, 0f);   // as bright as the galaxies it stands for where they hand over (never a black wall past the galaxies made in full)
                _web[0] = 0;
                }   // each a galaxy at its true size (a pinpoint from afar), faint, so the web shows where they crowd
            }
            for (int i = 0; i < _galaxies.Count; i++)
            {
                // every galaxy is one field of its own stars: the near one shows more of them as it grows on screen, each point
                // a little smaller so the galaxy's glow stays the same; the first stars are always the same ones, so nothing swaps
                var g = _galaxies[i]; int count = StarsShown(i);
                if (i != _near)
                {
                    // a galaxy only a few pixels across needs only a few of its stars (the first ones are always the same, so nothing
                    // swaps): as many as its size on screen can show, from about a hundred to all of them; its glow stays its own
                    double qx = g.X - _cx, qy = g.Y - _cy, qz = g.Z - _cz, qd = Math.Max(1e-9, Math.Sqrt(qx * qx + qy * qy + qz * qz));
                    double qpx = g.Scale / qd * h / (2 * Math.Tan(0.45));
                    count = (int)Math.Max(Math.Min(count, 100), Math.Min(count, qpx * qpx * 1.5));
                }
                // made in full only near you: past that the web's glow shows them (it stands for the same galaxies); fading out
                // between the near reach and half again, so nothing pops
                double ndx = g.X - _cx, ndy = g.Y - _cy, ndz = g.Z - _cz, nd = Math.Sqrt(ndx * ndx + ndy * ndy + ndz * ndz);
                float nearF = i == _near || (_tKind == 0 && _tIndex == i) ? 1f : (float)Math.Max(0, Math.Min(1, (_nearUnits * 1.5 - nd) / (_nearUnits * 0.5)));
                if (nearF <= 0) continue;
                float shrink = (float)Math.Sqrt(FarStars / (double)Math.Max(FarStars, count));
                var buf = i == _near && _nearBuf != null ? _nearBuf : g.Far; if (buf == null) continue;   // (its stars still being made)
                // inside the galaxy its points ease down from glows to plain points of light, so no nearby one looms as a blob
                float glow = i == _near ? (float)Math.Pow(1 - InsideShare(), 3) : 1f;   // to nothing at all well inside: only points of light remain
                // a galaxy's points never cover more than a small angle on screen: from afar they glow, up close they stay points of light
                // its light, never more than it has: when its stars crowd a small patch of screen they would add up to a white blot,
                // so each is dimmed by how many share the galaxy's patch (its true glow and colour, at any distance)
                double gdx = g.X - _cx, gdy = g.Y - _cy, gdz = g.Z - _cz, gd = Math.Max(1e-9, Math.Sqrt(gdx * gdx + gdy * gdy + gdz * gdz));
                double gpx = g.Scale / gd * h / (2 * Math.Tan(0.45)), dotPx = Math.Max(1.5, 0.0016 * h);
                float crowd = (float)Math.Max(0.06, Math.Min(1, Math.PI * gpx * gpx / (count * dotPx * dotPx) * 1.2)) * nearF * (i == _near ? 1f : others);   // a visible soft smudge in its own colours from afar: never a white blot, never gone
                DrawPoints(buf, count, Placement(g) * vp, right, up, 0.006f * g.Scale * shrink * glow, 0.0016f, w, h, time, 0.012f, crowd, InsideShare() > 0.5 && i == _near ? 2f : 0f);   // a galaxy's stars from afar are soft glows that add up to its light (hard discs would merge into a white outline); among them, points
            }
            double inside = InsideShare();
            {
                // every galaxy's clusters and nebulae, for the 8 nearest within about 60 of their sizes (one made new each frame at most)
                int drawn = 0; bool made = false;
                var order = new List<KeyValuePair<double, int>>();
                for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale; if (d < 60) order.Add(new KeyValuePair<double, int>(d, i)); }
                order.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (var kv in order)
                {
                    if (drawn >= 12) break; var g = _galaxies[kv.Value];
                    if (g.Features == null) { if (made) continue; EnsureStruct(g); made = true; }
                    if (g.StructCount > 0) { DrawPoints(g.Struct, g.StructCount, Placement(g) * vp, right, up, 1f, 0.0016f, w, h, time, 0, (float)Math.Max(0, Math.Min(1, (60 - kv.Key) / 20))); drawn++; }   // fading in from 60 to 40 of its sizes
                }
            }
            UpdateExtras();
            DrawBlackHoles(vp, right, up, w, h, time);                               // the feeding ones' discs and jets
            DrawSmallBodies(vp, right, up, w, h, time);                              // asteroids and comets close in: their own bodies
            {
                // the black hole that bends the most light on screen: its Einstein ring, sqrt(2 Rs / distance) radians (light from far
                // behind it), set for every point drawn
                _lens[2] = 0; double bestE = 0;
                foreach (var ex in _extras)
                {
                    if (!ex.Hole) continue; double dx = (ex.X - _ax) - _ox, dy = (ex.Y - _ay) - _oy, dz = (ex.Z - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double rs = Units.Km(2.953 * ex.Mass), thE = Math.Sqrt(2 * rs / Math.Max(d, rs)); if (thE < bestE) continue;
                    var cl = new float[4]; float px0 = (float)dx, py0 = (float)dy, pz0 = (float)dz;
                    float cx0 = px0 * vp.M11 + py0 * vp.M21 + pz0 * vp.M31 + vp.M41, cy0 = px0 * vp.M12 + py0 * vp.M22 + pz0 * vp.M32 + vp.M42, cw0 = px0 * vp.M14 + py0 * vp.M24 + pz0 * vp.M34 + vp.M44;
                    if (cw0 <= 0) continue;
                    bestE = thE; _lens[0] = cx0 / cw0; _lens[1] = cy0 / cw0; _lens[2] = (float)(thE / Math.Tan(0.45)); _lens[3] = cw0;
                }
            }
            if (_extras.Count > 0)
            {
                if (_extraV.Length < _extras.Count) _extraV = new StarVertex[_extras.Count + 256];
                for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (ex.Shape.Length > 0 && ShapeShown(ex, h)) { _extraV[q] = new StarVertex(); continue; }   // (drawn as its own body: no point of light over it)
                    if (ex.Hole) { _extraV[q] = new StarVertex { X = (float)((ex.X - _ax) - _ox), Y = (float)((ex.Y - _ay) - _oy), Z = (float)((ex.Z - _az) - _oz), Size = (float)(ex.R * 3), R = ex.Cr * 0.7f, G = ex.Cg * 0.7f, B = ex.Cb * 0.7f, A = 1 }; continue; }   // its glowing disc and photon ring; the middle stays dark (bent light)
                    _extraV[q] = new StarVertex { X = (float)((ex.X - _ax) - _ox), Y = (float)((ex.Y - _ay) - _oy), Z = (float)((ex.Z - _az) - _oz), Size = (float)ex.R, R = ex.Cr, G = ex.Cg, B = ex.Cb, A = 1 }; }
                if (_extraBuf == null || _extraBuf.Bytes < (long)_extras.Count * Stride) { if (_extraBuf != null) _extraBuf.Dispose(); _extraBuf = new UploadBuffer(_gpu.Device, (long)(_extras.Count + 256) * Stride); }
                _extraBuf.Write(_extraV, _extras.Count);
                DrawPoints(_extraBuf, _extras.Count, vp, right, up, 1f, 0.0024f, w, h, time);
            }
            if (_cstars.Count > 0)
            {
                // the cluster's own stars round you: old red and orange giants and yellow stars in a globular, young blue-white in an open one
                if (_cstarV.Length < _cstars.Count) _cstarV = new StarVertex[_cstars.Count + 1024];
                for (int q = 0; q < _cstars.Count; q++)
                {
                    var cs = _cstars[q]; bool old = cs[3] == 0; double t = cs[4];
                    float r = old ? (t < 0.4 ? 1f : 1f) : 0.7f, g2 = old ? (t < 0.4 ? 0.55f : 0.85f) : 0.82f, b2 = old ? (t < 0.4 ? 0.3f : 0.6f) : 1f;
                    _cstarV[q] = new StarVertex { X = (float)((cs[0] - _ax) - _ox), Y = (float)((cs[1] - _ay) - _oy), Z = (float)((cs[2] - _az) - _oz), Size = (float)Units.Km(696000 * (old && t < 0.1 ? 20 : 1)), R = r, G = g2, B = b2, A = 1 };
                }
                if (_cstarBuf == null || _cstarBuf.Bytes < (long)_cstars.Count * Stride) { if (_cstarBuf != null) _cstarBuf.Dispose(); _cstarBuf = new UploadBuffer(_gpu.Device, (long)(_cstars.Count + 1024) * Stride); }
                _cstarBuf.Write(_cstarV, _cstars.Count);
                DrawPoints(_cstarBuf, _cstars.Count, vp, right, up, 1f, 0.0022f, w, h, time);
            }
            if (_near >= 0 && _localCount > 0 && inside > 0)
            {
                // the real stars of this space at their real sizes (a small point from afar, never smaller than a dot on screen)
                float lf = (float)(inside * inside * (3 - 2 * inside));
                for (int k = 0; k < _localCount; k++) { var v = _localList[k]; v.A = 1; v.R *= lf; v.G *= lf; v.B *= lf; _localDrawn[k] = v; }
                _localBuf.Write(_localDrawn, _localCount);
                DrawPoints(_localBuf, _localCount, Placement(_galaxies[_near]) * vp, right, up, 1f, 0.0022f, w, h, time);
            }
            if (_system != null)
            {
                // the system around its star (the anchor): orbits and belts, then the star, planets and moons as glows at their real sizes
                _gpu.ClearDepth();
                PlaceSystem();
                var st = _system.Static;
                float stFade = (float)Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));   // orbits and belts fade in with the planets
                for (int i = 0; i < st.Length; i++) { var v = st[i]; v.X = (float)(st[i].X - _ox); v.Y = (float)(st[i].Y - _oy); v.Z = (float)(st[i].Z - _oz); v.R *= stFade; v.G *= stFade; v.B *= stFade; _staticShifted[i] = v; }
                if (st.Length > 0) { _sysStatic.Write(_staticShifted, st.Length); DrawPoints(_sysStatic, st.Length, vp, right, up, 0f, 0.002f, w, h, time); }
                for (int i = 0; i < _bodyCount; i++) { _bodyVerts[i].X = (float)(_bodyVerts[i].X - _ox); _bodyVerts[i].Y = (float)(_bodyVerts[i].Y - _oy); _bodyVerts[i].Z = (float)(_bodyVerts[i].Z - _oz); }
                // the planet whose real ground is showing is not drawn as a glow too (it showed through its own see-through sea and clouds as a frosted ghost)
                // the system's planets and moons fade in as you close in on its star (from 2,000 AU to 500 AU), never all at once
                double sysFade = Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));
                for (int i = 1; i < _bodyCount; i++) { _bodyVerts[i].R *= (float)sysFade; _bodyVerts[i].G *= (float)sysFade; _bodyVerts[i].B *= (float)sysFade; }
                if (_ground != null && _planet + 1 < _bodyCount)
                {
                    // its glow fades into its real ground as you come in (from 40 to 20 of its widths), never swapped
                    double gx0, gy0, gz0; CamFromPlanet(out gx0, out gy0, out gz0);
                    float gk = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(gx0 * gx0 + gy0 * gy0 + gz0 * gz0) / _ground.Radius - 20) / 20));
                    _bodyVerts[_planet + 1].R *= gk; _bodyVerts[_planet + 1].G *= gk; _bodyVerts[_planet + 1].B *= gk; if (gk <= 0) _bodyVerts[_planet + 1].Size = 0;
                }
                _sysBodies.Write(_bodyVerts, _bodyCount);
                DrawPoints(_sysBodies, _bodyCount, vp, right, up, 1f, 0.004f, w, h, time);
                // close enough to see its disc: the star's real body at its real size
                double R = _system.StarRadius, sd = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz);
                if (_starSurf != null && R / Math.Max(1e-30, sd) > 0.004)
                {
                    var toEye = new Vec3((float)(_ox / sd), (float)(_oy / sd), (float)(_oz / sd));
                    int n = _starSurf.Draw(toEye, time, _starSurfVerts, 0);
                    _surfBuf.Write(_starSurfVerts, n);
                    var m = Mat4.Identity; m.M11 = m.M22 = m.M33 = (float)R; m.M41 = (float)-_ox; m.M42 = (float)-_oy; m.M43 = (float)-_oz;
                    DrawPoints(_surfBuf, n, m * vp, right, up, 1f, 0f, w, h, time);
                    DrawPoints(_coronaBuf, _starCorona.Length, m * vp, right, up, 1f, 0f, w, h, time);
                }
            }
            if (_ground != null && _matterScale <= 0)   // (in the matter the grain's own face fills the view: the planet's ground mesh is not drawn behind it, one picture only)
            {
                // the planet's real ground, in km around its centre, with the same view turn; near and far follow the height
                _gpu.ClearDepth();
                double x, y, z; CamFromPlanet(out x, out y, out z);
                // the height above the real ground (not sea level: on high land that was kilometres, and the nearest drawing
                // distance cut away the ground right under you)
                double alt = Math.Max(1e-9, AboveGround() / Units.Km(1));
                // the depth range starts a little in front of the ground and ends just past the horizon, so near things (the sea
                // and the shore just under it) never fight for the same depth and shimmer
                double altSea = Math.Max(alt, Math.Sqrt(x * x + y * y + z * z) - _ground.Radius);   // the horizon goes by the height above sea level
                double horizon = Math.Sqrt(altSea * (2 * _ground.Radius + altSea)) + Math.Sqrt(2 * _ground.Radius * 30);
                Mat4 kproj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)Math.Max(alt * 0.15, 1e-9), (float)(horizon * 1.3 + 100));
                var sd = _system.StarRadius; double lx = -_px, ly = -_py, lz = -_pz, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                if (ll > 0)
                {
                    // the starlight's way, turned back by the planet's own spin, so its days pass over the ground
                    double sp = -Spin(_planet), c0 = Math.Cos(sp), s0 = Math.Sin(sp), sx = lx / ll, sz = lz / ll;
                    _ground.StarDir[0] = sx * c0 - sz * s0; _ground.StarDir[1] = ly / ll; _ground.StarDir[2] = sx * s0 + sz * c0;
                }
                GroundLight();
                // what the ground needs to detail: only what is in view (the view's half-angle, corner to corner)
                var lk = Look; if (_ground.ViewDir == null) _ground.ViewDir = new double[3];
                _ground.ViewDir[0] = lk.X; _ground.ViewDir[1] = lk.Y; _ground.ViewDir[2] = lk.Z;
                _ground.ViewHalfAngle = Math.Atan(Math.Tan(0.45) * Math.Sqrt(1 + (w / (double)Math.Max(1, h)) * (w / (double)Math.Max(1, h))));
                PlanetTerrain.Power = Window.Detail;
                _ground.Draw(_meshes, x, y, z, view, kproj, time, 3.0 * Math.Max(1, h / 600.0) * Math.Max(0.3, Math.Min(3, Math.Sqrt(Window.Detail))), 0, 0, 0, 0, 0, 0, w, h, 400);   // detail scales with the GPU use
            }
            double overAlt = _ground != null && _matterScale <= 0 ? AboveGround() / Units.Km(1) : 1e30;
            if (overAlt < MatterView.OverlayKm)
            {
                // a centimetre over the ground: its grains begin to show, lying over it, specks that grow as you come down
                SetMatter(); _gpu.ClearDepth();
                Mat4 ov = MatterFrame(look);
                _matter.Draw(ov, Math.Max(MatterView.BottomKm, overAlt / 0.6), w, h, _matterClock, _meshes, true);
                {
                    // a grain picked over the ground: ringed, with its card, as anything picked anywhere
                    _gpu.ClearDepth(); double gqx, gqy, gqr;
                    if (_matter.PickedOnScreen(out gqx, out gqy, out gqr)) { _hud.DrawRing(_meshes, gqx, gqy, Math.Min(gqr, h * 0.45), w, h); if (_hud.Visible && _tour == null) _hud.DrawCard(_meshes, _matter.Card(), gqx + gqr + 12 * Math.Max(1, h / 1080.0), gqy - gqr, w, h); }
                }
            }
            if (_spaceScale > 0)
            {
                // in a nebula's or cluster's gas: its atoms in front of space (its glow round you still drawn behind), and below a tenth
                // of a millimetre only the matter itself, no stars or galaxies behind (a draw distance for the very small)
                _gpu.ClearDepth();
                _matter.Light[0] = 0.4f; _matter.Light[1] = 0.7f; _matter.Light[2] = 0.6f;
                var slk = Look; _matter.SetEye(slk.X, slk.Y, slk.Z);                     // in the gas too: your eye goes round the point you dive into
                Mat4 sv = Mat4.LookAt(new Vec3(0, 0, 0), slk, ViewUp());                // (the camera's own up: never flipping)
                _matter.Draw(sv, _spaceScale, w, h, _matterClock, _meshes, false);
                _gpu.ClearDepth();
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);
                if (_hud.Visible && _tour == null) _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), 24 * Math.Max(1, h / 1080.0), w, h);
            }
            if (_matterScale > 0)
            {
                // the matter, in front of everything; above a tenth of a millimetre the ground still shows through behind it
                _gpu.ClearDepth();
                Mat4 mview = MatterFrame(look);
                _matter.Draw(mview, _matterScale, w, h, _matterClock, _meshes, false);
                _gpu.ClearDepth();                                                     // its ring and card on top of the grains
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);   // what you picked, ringed
                if (_hud.Visible && _tour == null) _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), 24 * Math.Max(1, h / 1080.0), w, h);   // what you are in, and what you picked
            }
            _gpu.ClearDepth();                                                         // the scale, cards and rings always on top of everything
            DrawScale(w, h);
            if (_tour != null)
            {
                // the tour is a film: letterbox bars, the caption in the lower one, faded in after a moment and out before the cut
                _hud.DrawBars(_meshes, w, h, 0.1f);
                _gpu.ClearDepth();                                                     // the caption in front of the bar it sits in
                var cs = _tourAt < _tour.Count ? _tour[_tourAt] : null;
                float cap = cs == null ? 0 : (float)Math.Min(1, Math.Max(0, (_tourT - 0.8) / 1.5));
                if (cs != null && cs.Do != "pick" && cap > 0.05 && TourCaption.Length > 0)
                {
                    // the story, and under it what is really in view and in focus now, written from what is there (this galaxy's
                    // own name and kind, this star, this world, this mineral, this atom), so every run tells its own journey
                    var lines = new List<string> { TourCaption };
                    var now = _matterScale > 0 || _spaceScale > 0 ? _matter.Card() : (_tKind >= 0 ? Card() : null);
                    if (now != null && now.Count > 0) lines.Add("now: " + now[0] + (now.Count > 1 ? ", " + now[1] : ""));
                    _hud.DrawCard(_meshes, lines, w * 0.3, h * 0.9, w, h, cap);   // to the right of the scale
                }
                _gpu.ClearDepth();                                                     // the cards and the frames a second on top of the bars
                // what is in view and in focus, written as you go, just below the top bar
                if (_hud.Visible && (_matterScale > 0 || _spaceScale > 0)) _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), h * 0.11, w, h);
            }
            if (Settings.Get("show-fps", true))                                       // frames a second, top right (View > Show FPS)
                _hud.DrawCard(_meshes, new[] { Window.Fps.ToString("0") + " FPS" }, w - 150 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 12 * Math.Max(1, h / 1080.0), w, h);
            double mdx, mdy, mdz, mrad, msx, msy;
            if (_tKind >= 0 && _matterScale <= 0 && TargetFromCamera(out mdx, out mdy, out mdz, out mrad) && ToScreen(mdx, mdy, mdz, out msx, out msy))
            {
                double l = Math.Sqrt(mdx * mdx + mdy * mdy + mdz * mdz);
                double rpx = Math.Max(14 * Math.Max(1, h / 1080.0), mrad / Math.Max(1e-30, l) / Math.Tan(0.45) * h * 0.5 * 1.25);
                bool fills = rpx > h * 0.45;                                           // bigger than the view: no ring, the card sits top left
                if (!fills) _hud.DrawRing(_meshes, msx, msy, rpx, w, h);
                if (_hud.Visible) { if (fills) _hud.DrawCard(_meshes, Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); else _hud.DrawCard(_meshes, Card(), msx + rpx + 12 * Math.Max(1, h / 1080.0), msy - rpx, w, h); }
            }
        }

        // the scale on screen: how many km one pixel spans at the distance of the nearest thing, in the middle of the view
        // how far away what you are looking at is (km), for the scale: in the gas or the matter, your own fine distance; flying to
        // a point in a nebula, the distance to that point (not to some star nearby); else the nearest thing
        private double ViewDistanceKm()
        {
            if (_spaceScale > 0) return _spaceScale * 0.6;
            if (_matterScale > 0) return _matterScale * 0.6;
            double qx, qy, qz, qr;
            if (_tKind == 5 && TargetFromCamera(out qx, out qy, out qz, out qr)) return Math.Max(1e-30, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1));
            // on the tour, how far it is to what the tour goes to (the picked world's ground; anything else picked, itself), never
            // just the nearest thing (beside a black hole every other zoom would think itself already there)
            if (_tour != null && _tKind == 2 && _tIndex == _planet && _ground != null) return Math.Max(1e-30, AboveGround()) / Units.Km(1);
            if (_tour != null && _tKind >= 0 && _farScale <= 0 && TargetFromCamera(out qx, out qy, out qz, out qr)) return Math.Max(1e-30, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1));
            return Math.Max(1e-30, Math.Max(NearestDistance(), _farScale)) / Units.Km(1);   // zoomed out, the view is as wide as how far out you have pulled (out among the web a galaxy is always near)
        }

        private void DrawScale(int w, int h)
        {
            double d = ViewDistanceKm(), viewKm = 2 * d * Math.Tan(0.45) * w / (double)Math.Max(1, h);
            _gpu.ClearDepth();
            _hud.Draw(_meshes, viewKm / Math.Max(1, w), w, h);
        }

        private float _sky = 1;                                                           // how much of space shows behind (fades away at the very small)
        private void DrawPoints(UploadBuffer buf, int count, Mat4 vp, Vec3 right, Vec3 up, float sizeScale, float minSize, int w, int h, double time, float maxSize = 0, float bright = 1, float style = 2)
        {
            if (buf == null || count <= 0) return;
            float b = bright * _sky; if (b <= 0.003f) return;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = sizeScale; c[26] = style; c[27] = 4; c[28] = w; c[29] = h; c[30] = minSize; c[31] = maxSize; c[32] = 1 - Math.Min(1, b);
            c[36] = _lens[0]; c[37] = _lens[1]; c[38] = _lens[2]; c[39] = _lens[3]; c[40] = w / (float)Math.Max(1, h);
            c[43] = _web[0]; c[44] = _web[1]; c[45] = _web[2]; c[46] = _web[3]; c[47] = _web[4];   // the web's light, sifted on the card (0: not the web)
            _dots.Draw(buf, count, c, 0, 0, w, h);
        }

        // ── the information card of what you picked: what it is, its id, and the facts that matter about it ──
        private static string Au(double units) { double au = units / Units.Au(1); return au >= 10 ? au.ToString("0") + " AU" : au.ToString("0.##") + " AU"; }
        private static string Km(double units) { double km = units / Units.Km(1); return km >= 10 ? km.ToString("N0") + " km" : km.ToString("0.##") + " km"; }

        // what a kind of planet or moon is made of
        private static string[] MakeUp(string kind, bool water)
        {
            switch (kind)
            {
                case "Temperate world": return new[] { "rock: silicate crust, iron core", water ? "seas of liquid water" : "dry basins", "air: nitrogen, carbon dioxide, water vapour" };
                case "Lava planet": return new[] { "rock: molten silicate seas", "iron core", "air: thin rock vapour and sulfur" };
                case "Ice world": return new[] { "ice: frozen water, carbon dioxide, nitrogen", "rock and iron beneath", "air: thin, or none" };
                case "Gas giant": return new[] { "hydrogen and helium", "clouds: ammonia, water", "core: rock and ice" };
                case "Ice giant": return new[] { "water, ammonia and methane ices", "air: hydrogen, helium, methane", "core: rock" };
                case "Hot Jupiter": return new[] { "hydrogen and helium, swollen by heat", "clouds: silicates and iron", "core: rock" };
                case "Moon": return new[] { "rock and dust, iron-poor", "no air", "craters from strikes" };
                default: return new[] { "rock: silicate crust, iron core", "dust and stone", "air: thin carbon dioxide" };
            }
        }

        // a short log of each turn of the wheel (where you are and why the step is what it is), in the temp folder, to find
        // where a zoom stops; kept small (started over past about 200 KB)
        // for the log: a camera jump of over a million km in one step of the update says which step did it
        private double _mx, _my, _mz;
        private void Mark() { _mx = _cx; _my = _cy; _mz = _cz; }
        private void Jumped(string step)
        {
            double dx = _cx - _mx, dy = _cy - _my, dz = _cz - _mz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > Units.Km(1e6) || double.IsNaN(d)) ZoomLog("JUMP " + Units.Describe(d) + " in " + step);
            Mark();
        }

        private void ZoomLog(string what)
        {
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dna_zoom.log");
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 200000) System.IO.File.Delete(f);
                System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.f") + "  " + what + "  |  " + Title.Replace("DNA-AND · one world · ", "").Split(new[] { " · wheel:" }, StringSplitOptions.None)[0] + Environment.NewLine);
            }
            catch { }
        }

        // the light on the ground: by day the star's own colour (from its temperature) through the air, which takes a little of
        // its own colour out of the direct light the thicker it is (a blue sky leaves the light warmer); by night half as bright,
        // in the colour of the air's glow and scattered starlight (airless worlds: the starlight alone)
        private void GroundLight()
        {
            float sr = _system.SR, sg = _system.SG, sb = _system.SB, m = Math.Max(1e-3f, Math.Max(sr, Math.Max(sg, sb)));
            sr /= m; sg /= m; sb /= m;
            double air = _ground.AirKm > 0 ? Math.Min(1, _ground.AirKm / 100.0) : 0;
            float ar = _ground.AirR, ag = _ground.AirG, ab = _ground.AirB;
            float dr = sr * (float)(1 - 0.3 * air * ag * ab), dg = sg * (float)(1 - 0.3 * air * ar * ab), db = sb * (float)(1 - 0.3 * air * ar * ag);
            float dm = Math.Max(1e-3f, Math.Max(dr, Math.Max(dg, db))); dr /= dm; dg /= dm; db /= dm;
            float nr = (float)(sr * (1 - air * 0.6) + ar * air * 0.6), ng = (float)(sg * (1 - air * 0.6) + ag * air * 0.6), nb = (float)(sb * (1 - air * 0.6) + ab * air * 0.6);
            float nm = Math.Max(1e-3f, Math.Max(nr, Math.Max(ng, nb)));
            if (_ground.SunColour == null) { _ground.SunColour = new float[3]; _ground.NightColour = new float[3]; }
            _ground.SunColour[0] = dr; _ground.SunColour[1] = dg; _ground.SunColour[2] = db;
            _ground.NightColour[0] = nr / nm * 0.5f; _ground.NightColour[1] = ng / nm * 0.5f; _ground.NightColour[2] = nb / nm * 0.5f;
        }

        // the matter lies under the planet's own up and down: the view turned into that frame (up = away from the planet's
        // middle), and the starlight with it
        private Mat4 MatterFrame(Vec3 look)
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            var upR = new Vec3((float)(x / r), (float)(y / r), (float)(z / r));
            var east0 = Vec3.Cross(Math.Abs(upR.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upR).Normalized; var north0 = Vec3.Cross(upR, east0).Normalized;
            var upL = upR;                                                              // (the grains themselves lie along the land's slope)
            var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
            var lk = new Vec3(Vec3.Dot(look, east), Vec3.Dot(look, upL), Vec3.Dot(look, north));
            // in the matter you look down onto the grain's face (it fills the whole view, never its edge and the dark past it); over
            // the ground, never from below it
            float lowest = _matterScale > 0 ? -0.7f : -0.15f;
            if (lk.Y > lowest) { float hl = (float)Math.Sqrt(lk.X * lk.X + lk.Z * lk.Z); float keep = (float)Math.Sqrt(1 - lowest * lowest) / Math.Max(1e-6f, hl); lk = hl > 1e-6f ? new Vec3(lk.X * keep, lowest, lk.Z * keep) : new Vec3(0, -1, 0); }
            var sd = new Vec3((float)_ground.StarDir[0], (float)_ground.StarDir[1], (float)_ground.StarDir[2]);
            _matter.Light[0] = Vec3.Dot(sd, east); _matter.Light[1] = Vec3.Dot(sd, upL); _matter.Light[2] = Vec3.Dot(sd, north);
            var lkn = lk.Normalized;
            if (_matterScale > 0) _matter.SetEye(lkn.X, lkn.Y, lkn.Z); else _matter.SetEye(0, -1, 0);   // in the matter your eye goes round the point you dive into
            // the camera's own up, carried into the ground's frame (never switched when looking straight down: no flip, no judder)
            var cu = ViewUp(); var upF = new Vec3(Vec3.Dot(cu, east), Vec3.Dot(cu, upL), Vec3.Dot(cu, north));
            float ud = Vec3.Dot(upF, lkn); upF = new Vec3(upF.X - lkn.X * ud, upF.Y - lkn.Y * ud, upF.Z - lkn.Z * ud);
            if (upF.Length < 1e-3f) upF = Math.Abs(lkn.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1);
            return Mat4.LookAt(new Vec3(0, 0, 0), lkn, upF.Normalized);
        }

        private List<string> Card()
        {
            var L = new List<string>();
            double dx, dy, dz, rad; if (!TargetFromCamera(out dx, out dy, out dz, out rad)) return L;
            double dist = Math.Max(0, Math.Sqrt(dx * dx + dy * dy + dz * dz) - rad);
            if (_tKind == 0)
            {
                var g = _galaxies[_tIndex];
                L.Add(g.Name); L.Add("id: G-" + (g.Seed & 0xFFFFFF).ToString("X6"));
                L.Add("type: " + g.Type); L.Add("width: about " + Units.Describe(g.Scale));
                L.Add("stars: about 100 billion"); L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 1)
            {
                bool open = _system != null && Math.Abs(_tx - _ax) + Math.Abs(_ty - _ay) + Math.Abs(_tz - _az) < Units.Km(1e6);
                L.Add(_tName.StartsWith("a ") ? _tName.Substring(2) : _tName);
                L.Add("id: S-" + (((long)(_tx / Units.Km(1e9)) * 73856093L ^ (long)(_ty / Units.Km(1e9)) * 19349663L ^ (long)(_tz / Units.Km(1e9)) * 83492791L) & 0xFFFFFF).ToString("X6"));
                if (open)
                {
                    var sy = _system;
                    L.Add("surface: " + sy.TempK.ToString("N0") + " K"); L.Add("mass: " + sy.MassSol.ToString("0.##") + " times the Sun's, width " + sy.RadiusSol.ToString("0.##") + " times the Sun's");
                    L.Add("brightness: " + sy.Luminosity.ToString(sy.Luminosity < 0.1 ? "0.###" : "0.##") + " times the Sun's");
                    L.Add("planets and moons: " + sy.Bodies.Count + " (" + sy.Planets + " planets)");
                    L.Add("habitable zone: " + (sy.HabitableAu * 0.75).ToString("0.##") + " to " + (sy.HabitableAu * 1.4).ToString("0.##") + " AU");
                    L.Add("frost line: " + sy.FrostLineAu.ToString("0.##") + " AU");
                }
                else L.Add("its system opens within 2,000 AU");
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 2)
            {
                var sy = _system; var b = sy.Bodies[_tIndex];
                int moons = 0; for (int i = 0; i < sy.Bodies.Count; i++) if (sy.Bodies[i].Parent == _tIndex) moons++;
                L.Add(b.Kind + (b.Parent >= 0 ? " of " + sy.Bodies[b.Parent].Kind : ""));
                L.Add("id: S-" + _sysStar.ToString("X") + (b.Parent >= 0 ? "-P" + (b.Parent + 1) + "-M" : "-P") + (_tIndex + 1));
                L.Add("width: " + Km(b.RadiusUnits * 2));
                var top = b.Parent >= 0 ? sy.Bodies[b.Parent] : b;
                if (b.Parent < 0)
                {
                    double au = b.OrbitUnits / Units.Au(1);
                    L.Add("orbit: " + Au(b.OrbitUnits) + ", year " + (b.Period >= 1 ? b.Period.ToString("0.#") + " years" : (b.Period * 365.25).ToString("0") + " days"));
                    L.Add(au >= sy.HabitableAu * 0.75 && au <= sy.HabitableAu * 1.4 ? "in the habitable zone" : au < sy.HabitableAu * 0.75 ? "inside the habitable zone: too hot" : "outside the habitable zone: too cold");
                    L.Add("moons: " + moons);
                }
                else L.Add("orbit: " + Km(b.OrbitUnits) + " from its planet");
                bool here = _ground != null && _planet == _tIndex;
                foreach (var m in MakeUp(b.Kind, here ? _ground.HasWater : b.Kind == "Temperate world")) L.Add(m);
                if (here) { L.Add("air: " + (_ground.AirKm > 0 ? "about " + _ground.AirKm.ToString("0") + " km deep" : "none") + (_ground.HasClouds ? ", clouds" : "")); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 5 && _tIndex < 0)
            {
                L.Add("a point between the stars of " + (_near >= 0 ? _galaxies[_near].Name : "the galaxy"));
                L.Add("the gas here: about 1 atom a cubic centimetre");
                L.Add("mostly hydrogen, some helium, a little dust");
                L.Add("the nearest star: " + Units.Describe(NearestDistance()) + " away");
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 5 && _tGal >= 0 && _galaxies[_tGal].Features != null && _tIndex < _galaxies[_tGal].Features.Count)
            {
                int fk = (int)_galaxies[_tGal].Features[_tIndex][4]; double ly = _tRadius * 2 / Units.Km(9.4607e12);
                L.Add(_tName.Substring(_tName.IndexOf(' ') + 1, 1).ToUpperInvariant() + _tName.Substring(_tName.IndexOf(' ') + 2));
                L.Add("id: " + (fk < 2 ? "C-" : "N-") + (_galaxies[_tGal].Seed & 0xFFF).ToString("X3") + "-" + (_tIndex + 1)); L.Add("in " + _galaxies[_tGal].Name);
                L.Add("width: about " + ly.ToString("N0") + " light-years");
                var ftg = _galaxies[_tGal].Features[_tIndex];
                if (fk >= 2 && ftg.Length > 5)
                {
                    // what it is made of, its largest parts
                    var parts = SpaceGen.Composition(fk, ftg[5]); Array.Sort(parts, (a, b) => b.Share.CompareTo(a.Share));
                    for (int q = 0; q < Math.Min(parts.Length, fk == 6 ? 6 : 4); q++) L.Add((parts[q].Share * 100).ToString(parts[q].Share < 0.01 ? "0.0" : "0") + "% " + parts[q].Name);
                }
                if (fk == 0) { L.Add("hundreds of thousands of stars"); L.Add("ancient: among the oldest stars of the galaxy"); L.Add("in the halo, round the galaxy's middle"); }
                else if (fk == 1) { L.Add("a few hundred young stars, born together"); L.Add("in the arms, drifting apart over time"); }
                else if (fk == 2) { L.Add("glowing hydrogen, lit by hot young stars"); L.Add("where new stars are being born"); }
                else if (fk == 3) { L.Add("dust lit blue by nearby stars' light"); L.Add("it shines by reflection, not its own glow"); }
                else if (fk == 4) { L.Add("a shell of gas puffed off by a dying star"); L.Add("the star's hot core, a white dwarf, lights it"); L.Add("it fades in some tens of thousands of years"); }
                else if (fk == 5) { L.Add("the expanding shell of an exploded star"); L.Add("hot gas, still racing outward"); L.Add("it seeds space with heavy elements"); }
                else { L.Add("a cold cloud of dust and molecules"); L.Add("dark: it hides the stars behind it"); L.Add("stars are being born deep inside"); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count)
            {
                var exk = _extras[_tIndex]; foreach (var ln in exk.Lines) L.Add(ln);
                L.Add("distance: " + Units.Describe(dist));
                if (exk.Mass > 0)
                {
                    // its pull where you are, falling with the square of the distance; the speed you would need to get away
                    double rM = Math.Max(1, (dist + exk.R) / Units.Km(1) * 1000), gm = 1.32712e20 * exk.Mass;   // metres; G times its mass
                    double g = gm / (rM * rM), vesc = Math.Sqrt(2 * gm / rM);
                    L.Add("its pull here: " + (g >= 0.01 ? g.ToString("0.###") : g.ToString("0.##E+0")) + " m/s2 (" + (g / 9.81).ToString(g / 9.81 >= 0.01 ? "0.###" : "0.##E+0") + " of Earth's)");
                    L.Add("escape speed here: " + (vesc >= 2.998e8 ? "faster than light: inside, nothing gets out" : (vesc / 1000).ToString("N0") + " km/s (" + (vesc / 2.998e8 * 100).ToString("0.###") + "% of light)"));
                }
            }
            else if (_tKind == 4 && _system != null)
            {
                var sy = _system; bool ast = _tName == "the asteroid belt", kui = _tName == "the Kuiper belt";
                L.Add(_tName.Substring(4, 1).ToUpperInvariant() + _tName.Substring(5) + " of this star");
                if (ast) { L.Add("from " + sy.BeltInAu.ToString("0.##") + " to " + sy.BeltOutAu.ToString("0.##") + " AU"); L.Add("rocky and metal bodies, dust to dwarf planets"); L.Add("left where a giant's pull kept a planet from forming"); }
                else if (kui) { L.Add("from " + sy.KuiperInAu.ToString("0.#") + " to " + sy.KuiperOutAu.ToString("0.#") + " AU"); L.Add("icy bodies: frozen water, methane, ammonia"); L.Add("where short-period comets come from"); }
                else { L.Add("from about " + sy.OortInAu.ToString("N0") + " to " + sy.OortOutAu.ToString("N0") + " AU, all round"); L.Add("a shell of icy bodies, billions of them"); L.Add("where long-period comets come from"); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 3 && _ground != null)
            {
                double gh = _ground.Solid ? _ground.GroundAt(_tx, _ty, _tz) : 0;
                bool sea = _ground.HasWater && gh < 0, ice = Math.Abs(_ty) > 0.86;
                L.Add(sea ? "sea" : ice ? "ice" : "land"); 
                L.Add("on: " + _system.Bodies[_planet].Kind);
                L.Add("place: " + (Math.Asin(_ty) * 180 / Math.PI).ToString("0.#") + " lat, " + (Math.Atan2(_tz, _tx) * 180 / Math.PI).ToString("0.#") + " long");
                L.Add(sea ? "depth: " + (-gh * 1000).ToString("N0") + " m of water" : "height: " + (gh * 1000).ToString("N0") + " m above the sea");
                L.Add(sea ? "water over sand and mud" : ice ? "ice and packed snow" : gh > 3 ? "bare rock and scree" : gh > 1 ? "rock and thin soil" : "soil, sand and stone");
                L.Add("distance: " + Units.Describe(dist));
            }
            return L;
        }

        public string Title
        {
            get
            {
                double pdx, pdy, pdz, prad; string picked = _tKind >= 0 && TargetFromCamera(out pdx, out pdy, out pdz, out prad) ? " · going to " + _tName + ", " + Units.Describe(Math.Max(0, Math.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz) - prad)) + " away" : " · click something to go to it";
                string where = _spaceScale > 0 ? "in " + _tName + ", " + ScaleHud.Words(_spaceScale * 0.6) + " from the point you dive into: " + _matter.Stage(_spaceScale)
                    : _matterScale > 0 ? "at " + _system.Bodies[_planet].Kind + ", " + ScaleHud.Words(_matterScale * 0.6) + " above the ground: " + _matter.Stage(_matterScale)
                    : _ground != null ? "at " + _system.Bodies[_planet].Kind + ", " + Units.Describe(AboveGround()) + " above the ground"
                    : _system != null ? "in the system of a " + _system.StarName + " (" + _system.Bodies.Count + " planets and moons)"
                    : _near >= 0 ? (NearShare() > 0.5 ? "among the stars of " : "near ") + _galaxies[_near].Name : "among the galaxies";
                double hr = LocalHour(); string when = " · time: " + TimeNames[TimeStep] + (hr >= 0 ? ", " + ((int)hr).ToString("00") + ":" + ((int)(hr % 1 * 60)).ToString("00") + " here" : "");
                var lh = LayerHere(); if (lh != null) where = "inside " + (_system != null && _planet >= 0 ? _system.Bodies[_planet].Kind : "the giant") + ", in its " + lh.Name.ToLowerInvariant() + " (" + lh.Says + ")";
                return PreviewText.AppName + " · one world · " + where + when + picked + (_tour != null && _tourAt < _tour.Count ? " · tour: " + _tour[_tourAt].Name : "") + " · " + (_spaceScale > 0 || _matterScale > 0 || _tKind == 5 ? "looking at " + ScaleHud.Words(ViewDistanceKm()) + " away" : "nearest thing " + Units.Describe(NearestDistance()) + " away") + " · wheel: fly in or out toward the mouse · drag: circle the pick or look around · right click: clear the pick · W A S D Q E: fly · H: scale on or off · Esc: close · card " + (int)Math.Round(Window.GpuBusy * 100) + "% busy (GPU use " + (int)Math.Round(Window.GpuUse * 100) + "%)";
            }
        }

        public void Dispose()
        {
            foreach (var g in _galaxies) if (g.Far != null) g.Far.Dispose(); if (_glowBuf != null) _glowBuf.Dispose();
            if (_nearBuf != null) _nearBuf.Dispose(); foreach (var g in _galaxies) if (g.Struct != null) g.Struct.Dispose();
            CloseSystem();
            DropPlanet();
            _hud.Dispose(); _matter.Dispose();
            if (_localBuf != null) _localBuf.Dispose();
        }
    }
}
