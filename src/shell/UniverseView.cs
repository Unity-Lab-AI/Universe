// Universe · shell · the universe: a single camera that flies through every scale at any angle, with no views to switch.
// The universe of galaxies and each galaxy's own stars are drawn together; as you come closer to a galaxy its far glow
// fades out while its real stars fade in, so it grows into its stars without a seam. Zooming moves the camera along the
// line under the mouse by a share of the distance to whatever is nearest, so it slows by itself as things come close
// and never runs out. Positions are kept in a chain of frames (the universe, then a galaxy), each with its own origin,
// so nothing loses precision however deep you go. (Star systems, planets and the ground join the same camera next.)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class UniverseView : IDisposable
    {
        private sealed class Galaxy { public volatile StarVertex[] Pending; public int[] PendingKinds; public long Region; public string Type, Name; public ulong Seed; public double X, Y, Z; public float Tilt, Turn, Scale; public UploadBuffer Far; public int FarCount; public StarVertex[] FarPts; public int[] FarKinds; public UploadBuffer Struct; public int StructCount; public List<double[]> Features; }   // Struct and Features: its clusters and nebulae, made once it is near enough to see them
        private readonly List<Galaxy> _galaxies = new List<Galaxy>();
        private readonly Gpu _gpu; private readonly BillboardDrawer _dots; private readonly ContentLibrary _content;
        private readonly List<StarKind> _kinds;
        private readonly MeshPipes _meshes;
        private readonly ScaleHud _hud;
        private readonly MatterView _matter; private double _matterScale;     // below the ground's finest piece: how big the view is (km), 0 = not in the matter
        // the planet or moon you have come close to: its real ground, drawn in the same camera (-1: none)
        private int _planet = -1; private PlanetTerrain _ground; private double _px, _py, _pz; private Extra _groundExtra;   // _planet -2: the ground is a small body's (_groundExtra), made by the same ground system a planet's is   // its centre from the star, in universe units
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
        public int TimeStep = 2; private int _timeBeforePause = 2;   // (starts at a minute a second: a day of 10 to 40 hours passes in 10 to 40 minutes, the worlds and their stars moving calmly)
        /// <summary>Pauses, or carries on at the speed it had.</summary>
        public void TogglePause() { if (TimeStep == 0) TimeStep = _timeBeforePause; else { _timeBeforePause = TimeStep; TimeStep = 0; } MenuChanged = true; }
        public void SetTime(int step) { TimeStep = Math.Max(0, Math.Min(TimeSpeeds.Length - 1, step)); if (TimeStep > 0) _timeBeforePause = TimeStep; MenuChanged = true; }

        // a body's day in hours, one turn of its spin: a world's from its own template (content/terrain.unt day-hours, its own
        // value in the kind's range, the same one its ground is lit by); moons turn once each orbit (one face to their planet)
        private double DayHours(int i)
        {
            if (i < 0 || _system == null || i >= _system.Bodies.Count) return 8;   // (a small body: a few hours)
            var b = _system.Bodies[i];
            if (b.Parent >= 0 || b.Terrain == null) return Math.Max(1, b.Parent >= 0 ? b.Period * 8766 : 24);
            if (b.Locked) return Math.Max(1, b.Period * 8766);                          // tidally locked: it turns once each orbit, one face always to its star
            return Math.Max(1, PlanetTerrain.Instance(b.Terrain, WorldSeed(i)).DayHours);
        }
        // the seed a world of this system is made from (its ground, its own traits)
        private uint WorldSeed(int i) { return (uint)((_near >= 0 ? _galaxies[_near].Seed : 0UL) * 977UL + (ulong)_sysStar * 31UL + (ulong)i); }

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

        public UniverseView(Gpu gpu, BillboardPipeline pipe, MeshPipes meshes, ContentLibrary content, ulong seed)
        {
            _meshes = meshes; _gpu = gpu; _hud = new ScaleHud(gpu.Device); _matter = new MatterView(gpu, pipe, content); MatterView.Log = ZoomLog; _dots = new BillboardDrawer(gpu.CommandList, pipe); _content = content; Physics.Load(content);   // (the laws of nature, content/physics.unt)
            var types = new List<string>(); var names = new List<string>();
            foreach (var e in content.OfKind("galaxies.unt", "galaxy")) { types.Add(e.Id); names.Add(e.Name); }
            if (types.Count == 0) { types.Add("spiral"); names.Add("Spiral galaxy"); }
            _kinds = GalaxyGen.StarKinds(content); _types = types; _typeNames = names; _seed = seed;
            foreach (var e in content.OfKind("universe.unt", "universe"))
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float; double v;
                if (double.TryParse(e.Get("across-ly", "1.34e10"), fs, ci, out v)) _uRadius = v * 0.5 / Units.LightYearsPerUnit;
                foreach (var law in _content.OfKind("physics.unt", "law")) if (law.Id == "cosmos") { if (double.TryParse(law.Get("dark-matter-share", "0"), fs, ci, out v)) _darkMatter = Math.Max(0, Math.Min(0.99, v)); if (double.TryParse(law.Get("rim-speed-km-s", "220"), fs, ci, out v)) _rimSpeed = v; if (double.TryParse(law.Get("dark-energy-share", "0"), fs, ci, out v)) _darkEnergy = v; if (double.TryParse(law.Get("hubble-km-s-mpc", "70"), fs, ci, out v)) _hubble = v; }
                foreach (var law in _content.OfKind("physics.unt", "law")) { if (law.Id == "frame-dragging") { double lo, hi; if (law.TryRange("spin-range", out lo, out hi)) { _spinLo = lo; _spinHi = hi; } } if (law.Id == "gravity-off" && double.TryParse(law.Get("swell-over-freefall", "2"), fs, ci, out v)) _swellOverFreefall = v; if (law.Id == "hole-inside") { if (double.TryParse(law.Get("way-in-ly", "3e6"), fs, ci, out v)) _wayInLy = v; if (double.TryParse(law.Get("way-in-mass-sol", "10"), fs, ci, out v)) _wayInMass = Math.Max(1e-6, v); if (double.TryParse(law.Get("way-in-power", "1"), fs, ci, out v)) _wayInPower = v; } }
                if (double.TryParse(e.Get("galaxies", "1e11"), fs, ci, out v)) _uGalaxies = v;
                if (double.TryParse(e.Get("web-ly", "2e8"), fs, ci, out v)) _webUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("web-contrast", "6"), fs, ci, out v)) _webContrast = v;
                if (double.TryParse(e.Get("web-wall-ly", "1.2e7"), fs, ci, out v)) _wallUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("region-ly", "1e6"), fs, ci, out v)) _regionUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("near-ly", "2e7"), fs, ci, out v)) _nearUnits = v / Units.LightYearsPerUnit;
                if (double.TryParse(e.Get("glow-points", "300000"), fs, ci, out v)) _glowPoints = (int)v;
                if (double.TryParse(e.Get("real-stars-cube-ly", "25"), fs, ci, out v)) LocalCell = Math.Max(1, v) / 50000.0;
                if (double.TryParse(e.Get("real-stars-reach-ly", "60"), fs, ci, out v)) LocalReach = Math.Max(1, v) / 50000.0;
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

        // ── every other kind of body, made round you from content/bodies.unt, drawn and clickable: a supermassive black hole at
        // each galaxy's heart; stellar black holes, neutron stars and rogue planets among a galaxy's stars; stars and rogue
        // planets between the galaxies; asteroids, Kuiper-belt bodies and Oort-cloud comets round a star ──
        private sealed class Place { public string Id, Name, Where, Entry, Says, Shape; public double PerLy3, SizeKm, SizeLo, SizeHi, SizePow = 1, RoundKm, FeedShare, JetShare, ComaLo, ComaHi, TailLo, TailHi, DiscLo = 3, DiscHi = 60, SwarmLo = 12, SwarmHi = 120, EatShare, OrbitSeconds = 8, EatLo = 1, EatHi = 1; public int Shown, Swarm; public float R, G, B; }   // EatLo..EatHi: how many stars a feeding hole tears apart at once   // DiscLo..DiscHi: a black hole's disc, in horizon radii; Swarm stars on orbits of SwarmLo..SwarmHi radii; EatShare: how many feeding holes are tearing a star apart now; OrbitSeconds: the inner edge's turn, as watched
        private sealed class Extra { public double X, Y, Z, R, Mass, ComaKm, TailKm; public string Name; public List<string> Lines; public float Cr, Cg, Cb; public bool Hole; public int Feeding; public float Tilt, Turn; public string Shape = ""; public Place Pl; public bool Eating; public string Mix, KindName; public double Density, Albedo; public int EatN; public double Spin; public double SpinHold = double.NaN; public double OffX, OffY, OffZ, LOffX, LOffY, LOffZ; public bool Orbits; public double OrbE, OrbQ, OrbPeriod, OrbCx, OrbCy, OrbCz, LX, LY, LZ; public double[] OrbE1, OrbE2; public int ShapeIx; }   // ShapeIx: which of the rock shapes is its (by its seed; its band by its size)   // Spin: a share of the most a hole could have (content/physics.unt @law frame-dragging). EatN: the stars being torn apart at once; Mix: the ground its matter is (content/minerals.unt @mix); Density g/cc; Albedo its share of light reflected   // Feeding: 0 quiet, 1 a glowing disc, 2 a disc and jets
        private List<Place> _places; private readonly List<Extra> _extras = new List<Extra>(); private string _extraKey = "";
        // a feeding black hole's accretion disc and its jets, made once in its own units (Schwarzschild radii), drawn for each,
        // tilted its own way, turning, sized to it

        // a feeding black hole's parts, made once for each kind of hole (content/bodies.unt: its disc's reach in horizon radii, its
        // swarm of stars, how fast its inner edge turns as watched), in its own frame (its horizon's radius 1, its disc level); every
        // motion in them is worked out on the card each frame (Pipeline: the gas on Kepler orbits, the stars on their ellipses, the
        // torn star's stream flowing)
        private sealed class HoleParts { public UploadBuffer Disc, Jets, Swarm, Stream; public int DiscN, JetsN, SwarmN, StreamN; public float K; }
        private readonly Dictionary<string, HoleParts> _holeParts = new Dictionary<string, HoleParts>();
        /// <summary>Every black hole's shadow: a black cone of the view round it, its half-angle by the law for where the camera
        /// stands (sin^2 = (27/4)(rs/r)^2 (1 - rs/r)): 2.6 horizon radii across from afar, half the sky at the photon sphere, nearly
        /// all of it just outside the horizon. Drawn over the stars and galaxies behind the hole; the disc, swarm and streams are drawn
        /// after it, their own captured light dropped on the card.</summary>
        private void DrawShadows(int w, int h)
        {
            if (!_haveAxes) return; var lk = Look;
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz), r = d / Math.Max(1e-300, rs);
                if (r <= 1.0005) continue;
                double s2 = 6.75 / (r * r) * Math.Max(0, 1 - 1 / r), th = r >= 1.5 ? Math.Asin(Math.Sqrt(Math.Min(1, s2))) : Math.PI - Math.Asin(Math.Sqrt(Math.Min(1, s2)));
                if (th < Math.PI / 2 && Math.Tan(th) * h / (2 * Math.Tan(0.45)) < 0.6) continue;   // (smaller than a pixel: nothing to draw)
                double ux = dx / d, uy = dy / d, uz = dz / d;
                _hud.DrawCone(_meshes, w, h, ux * _viewRight.X + uy * _viewRight.Y + uz * _viewRight.Z, ux * _viewUp.X + uy * _viewUp.Y + uz * _viewUp.Z, ux * lk.X + uy * lk.Y + uz * lk.Z, th);
                // its photon ring: the thin circle of bent light at the shadow's edge, where light from everything behind and around
                // it has looped the hole on its way to you (the ring the Event Horizon Telescope photographed): faint, there on every
                // hole, feeding or quiet, so a quiet hole on an empty sky is still there to be seen, a black disc in a ring of light
                if (th < Math.PI / 2)
                {
                    var u = new Vec3((float)ux, (float)uy, (float)uz); var e1 = Vec3.Cross(u, Math.Abs(u.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(u, e1).Normalized;
                    double thr = th * 1.04; float ct = (float)Math.Cos(thr), st = (float)Math.Sin(thr); const int N = 240; float dist = (float)Math.Min(d, 1e-3);
                    if (_photonV.Length < N) _photonV = new StarVertex[N];
                    for (int q = 0; q < N; q++) { double a = q * Math.PI * 2 / N; var dir = u * ct + (e1 * (float)Math.Cos(a) + e2 * (float)Math.Sin(a)) * st; _photonV[q] = new StarVertex { X = dir.X * dist, Y = dir.Y * dist, Z = dir.Z * dist, Size = 0, R = 0.55f, G = 0.6f, B = 0.7f, A = 1 }; }
                    if (_photonBuf == null) _photonBuf = new UploadBuffer(_gpu.Device, (long)N * Stride); _photonBuf.Write(_photonV, N);
                    var vp0 = _lastVp; DrawPoints(_photonBuf, N, vp0, _viewRight, _viewUp, 1f, 0.0016f, w, h, 0, 0.0016f, 0.45f);
                }
            }
        }
        private UploadBuffer _photonBuf; private StarVertex[] _photonV = new StarVertex[0];
        private Vec3 _fallAxis = new Vec3(0, 0, 1); private UploadBuffer _fallBuf; private int _fallCap; private Extra _fallHole; private double _fallTau, _fallRate = 1; private double _fallShare, _fallScale0;   // the way in, 0 at the horizon to 1 at the point (the fall's own measure: the view held at the horizon's scale till the walls meet, then the zoom into the point)   // the fall's clock: proper time since the horizon, its rate against real time; deep: past a thousandth of a radius, the zoom carries on to the quarks   // the hole you are falling into: the camera itself travels down the axis toward its middle
        /// <summary>Inside the horizon, the fall's one reference: the gravity well itself, in the world. The sheet of space round the
        /// hole as a square grid (the rubber sheet), bent into Flamm's paraboloid outside the horizon (its depth 2 root(rs (r - rs)),
        /// the exact shape of a still slice of Schwarzschild space, flat far out, vertical at the horizon's throat) and, inside the
        /// throat, into the surface the same equation gives there with its sign flipped (space and time swapped: a timelike depth),
        /// 2 root(rs (rs - r)), from the throat down to the point, the singularity, two radii below it.
        /// You are on the axis at your depth, the walls round you, every line of the grid running down to the point ahead; the axis is
        /// the way you came in, fixed in space, so turning the view turns round the well. (A reference, not light: never bent by the
        /// hole's lens.) Flamm 1916, from the Schwarzschild metric; the free-faller's own slices are flat, so this is the still slice's shape.</summary>
        private void DrawFallGrid(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            var ax = _fallAxis; var e1 = Vec3.Cross(ax, Math.Abs(ax.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(ax, e1).Normalized;
            // where you are down the well: the fall runs from the horizon (r = rs) to the quarks' size, 25 tenfolds; the cone inside
            // the throat narrows to the point over that run
            // the surface, from the metric itself (rs = 1): outside the horizon Flamm's, its height above the throat 2 root(r - 1)
            // (z'^2 = g_rr - 1 = 1/(r - 1)), a bowl opening upward, flat far out, vertical at the throat; inside the horizon the same
            // equation with its sign flipped (there g_rr is negative: space and time have swapped roles, and the depth is timelike),
            // 2 root(1 - r), running from the throat down to the point at r = 0, two radii below it. Down is toward the point: the
            // far sheet is above and behind you, the throat's ring at depth 0, the point at depth 2. You are at your real r
            double rhoCam = Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds);   // your depth: the way in, in the fall's own measure
            // the well inside the horizon is the dilation itself, not the hole's size: the funnel is drawn in the fall's own measure,
            // each tenfold of the radius two views of length, forty tenfolds from the horizon to the point for every hole (the way
            // in is not the hole's size: inside there is near-infinite space and no space at once), so it is far longer than the space the hole takes
            // up, and it is complete at every depth: an inward spike, fixed in your view, that you travel down. The mouth (the
            // horizon's ring, the big opening) is a view and a half wide; the cone squeezes straight down from it, linear, to the
            // point eighty views below, where it ends in no space at all. As you fall the walls beside you close in and get tighter,
            // and the point is a pin point below you the whole way, nearer and nearer, until the walls meet at it. Looking down you
            // see the whole run to the point; looking up, the whole run back to the opening
            const double PerTenfold = 2.0;
            double T = MatterView.FallTenfolds;                                                         // tenfolds from the horizon to the end: the same immense run for every hole
            double tCam = Math.Min(T - 0.01, -Math.Log10(Math.Max(1e-300, rhoCam)));                   // your own depth in tenfolds below the horizon
            Func<double, double> radiusAt = t => 1.5 * Math.Max(0, T - t) / T;                          // the cone's radius at a depth, in views: a view and a half at the mouth, nothing at the point; at your own depth tighter the deeper you are
            double Smouth = radiusAt(0);                                                                 // the mouth's radius in views: the sheet outside the horizon is drawn at that scale (one horizon radius = Smouth)
            Func<double, double> tw = t => 0;                                                            // a gravity well is rings and straight spokes: no twist, no turning (the drag's twist read as a trippy effect, not a well)
            Func<double, double> along = rho => rho >= 1 ? -tCam * PerTenfold - 2 * Math.Sqrt(rho - 1) * Smouth : (-Math.Log10(Math.Max(1e-300, rho)) - tCam) * PerTenfold;   // how far ahead of you (down) a ring of that radius lies; negative: above
            Func<double, double> width = rho => rho >= 1 ? rho * Smouth : radiusAt(-Math.Log10(Math.Max(1e-300, rho)));   // its radius in views
            var pts = new List<StarVertex>();
            Action<double, double, float, float, float, float> put = (u, v, br, cr, cg, cb) =>
            {
                double rho = Math.Sqrt(u * u + v * v), wv = rho > 0 ? width(rho) / rho : 0;
                var p = e1 * (float)(u * wv) + e2 * (float)(v * wv) + ax * (float)along(rho);
                pts.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0, R = cr * br, G = cg * br, B = cb * br, A = 1 });   // (no size of its own: a point of the line, the same few pixels at any distance)
            };
            // the sheet outside the horizon: its square grid every half radius out to eight, bent into Flamm's bowl down to the throat,
            // at the mouth's own scale (a view and a half), always: the opening you look back up at from any depth
            {
                // rings of the sheet at every radius out to eight, and spokes from the throat out: a gravity well, round from the top
                // (a square grid read as a square at the mouth)
                double[] sheetRings = { 1.08, 1.2, 1.4, 1.7, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8 };
                foreach (double rho in sheetRings)
                {
                    float br = (float)(0.45 * Math.Max(0.25, 1 - Math.Abs(rho - 1) * 0.08)); int n = (int)Math.Max(96, Math.Min(2400, rho * Smouth * 2 * Math.PI / 0.02));
                    for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n; put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
                }
                for (int sp = 0; sp < 36; sp++)
                {
                    double a = sp * Math.PI * 2 / 36; int n = (int)Math.Max(100, Math.Min(1200, 7 * Smouth / 0.02));
                    for (int q = 0; q <= n; q++) { double rho = 1 + 7.0 * q / n; float br = (float)(0.4 * Math.Max(0.25, 1 - Math.Abs(rho - 1) * 0.08)); put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
                }
            }
            // inside: the whole cone, a ring every sixth of a tenfold from the mouth to the point and spokes the whole way down
            // between them, every one ending at the point. Every line carries a point about every fiftieth of a view of its own
            // length, as the sheet's lines do, so it reads as a line right beside you (a ring of a few dozen points, a spoke of a
            // point every eighth of a view, was a scatter of single pixels at arm's length: the walls were not there to be seen,
            // only the mouth's ring and the sheet)
            const double Along = 0.02;                                                      // a point every fiftieth of a view along a line
            int kEnd = (int)Math.Ceiling(T * 6);
            for (int k = 0; k <= kEnd; k++)
            {
                double t = Math.Min(T, k / 6.0), rho = Math.Pow(10, -t), rw = radiusAt(t); if (rw <= 0) break; if (rw > 200) continue;   // (a ring wider than two hundred views is beyond the picture)
                float br = (float)(0.55 * Math.Max(0.45, 1 - Math.Abs(t - tCam) * 0.15));
                int n = (int)Math.Max(96, Math.Min(1500, rw * 2 * Math.PI / Along));
                for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n + tw(t); put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
            }
            {
                int qn = (int)Math.Min(6000, Math.Max(600, T * PerTenfold / Along));
                for (int sp = 0; sp < 36; sp++)
                {
                    double a = sp * Math.PI * 2 / 36;
                    for (int q = 0; q <= qn; q++) { double t = T * q / (double)qn, rho = Math.Pow(10, -t), a2 = a + tw(t); if (radiusAt(t) > 200) continue; put(Math.Cos(a2) * rho, Math.Sin(a2) * rho, 0.45f, 0.35f, 0.9f, 0.6f); }
                }
            }
            { int n = (int)Math.Max(480, Math.Min(3000, Smouth * 2 * Math.PI / Along)); if (Smouth <= 200) for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n; put(Math.Cos(a), Math.Sin(a), 0.9f, 0.7f, 0.95f, 1f); } }   // the mouth: the horizon's ring, bright
            { var p = ax * (float)((T - tCam) * PerTenfold); pts.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0.04f, R = 1f, G = 1f, B = 1f, A = 1 }); }   // the point it all squeezes down to, at the cone's end
            if (_fallBuf == null || _fallCap < pts.Count) { if (_fallBuf != null) _fallBuf.Dispose(); _fallCap = pts.Count + 64; _fallBuf = new UploadBuffer(_gpu.Device, (long)_fallCap * Stride); }
            var arr = pts.ToArray(); _fallBuf.Write(arr, arr.Length);
            float lz = _lens[2]; _lens[2] = 0; float sky0 = _sky; _sky = 1;                      // (a reference, not light: never bent by the hole's lens, and never faded with the sky at the very small: the sky's fade below ten microns had taken the grid with it, gone before the atoms' scales)
            // drawn through the matter's own projection (positions in units of your scale, the near plane a thousandth of it): the
            // universe's projection cut the grid off at its near plane once the fall was deep
            var fv = Mat4.LookAt(new Vec3(0, 0, 0), Look, ViewUp()); var fvp = fv * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), 0.001f, 1e9f);
            DrawPoints(_fallBuf, arr.Length, fvp, new Vec3(fv.M11, fv.M21, fv.M31), new Vec3(fv.M12, fv.M22, fv.M32), 1f, 0.0012f, w, h, time, 0, 1f, 2f);
            _lens[2] = lz; _sky = sky0;
        }
        private UploadBuffer _wellBuf; private int _wellN;
        private void MakeWell()
        {
            var l = new List<StarVertex>(); double[] rings = { 1, 1.15, 1.35, 1.6, 2, 2.5, 3.2, 4, 5, 6.5, 8, 10, 13, 16, 20, 25, 32, 40 };
            Func<double, double> depth = r => -2 * Math.Sqrt(Math.Max(0, r - 1));
            foreach (var r in rings) for (int k = 0; k < 160; k++) { double a = k * Math.PI * 2 / 160; float br = (float)(r <= 1 ? 0.8 : 0.45); l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)depth(r), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.5f * br, G = 0.72f * br, B = 0.9f * br, A = 1 }); }
            for (int sp = 0; sp < 36; sp++)
            {
                double a = sp * Math.PI * 2 / 36;
                for (int k = 0; k < 72; k++) { double r = Math.Pow(40, k / 71.0); l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)depth(r), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.17f, G = 0.26f, B = 0.33f, A = 1 }); }
                for (int k = 1; k <= 40; k++) { double t = k / 40.0, r = 1 - t; l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)(-14 * t), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.5f, G = 0.6f, B = 0.75f, A = 1 }); }   // the step: from the horizon's rim straight down to the point
            }
            l.Add(new StarVertex { X = 0, Y = -14, Z = 0, Size = 0.5f, R = 1f, G = 1f, B = 1f, A = 1 });
            _wellN = l.Count; _wellBuf = new UploadBuffer(_gpu.Device, (long)l.Count * Stride); _wellBuf.Write(l.ToArray(), l.Count);
        }
        // what a body is made of, on its card: its kind, its make-up, its minerals and their elements, its density and mass, its albedo
        private void Compose(Extra ex, ContentEntry made)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            ex.KindName = made.Name; ex.Mix = made.Get("mix", null); if (ex.Mix != null && ex.Mix.Length == 0) ex.Mix = null;
            if (ex.Mix != null) ex.Mix = _matter.VaryMix(ex.Mix, (ulong)(Math.Abs(Math.Sin(ex.X * 7.1 + ex.Y * 13.3 + ex.Z * 3.7) * 1e6) % 1e6));   // its own shares of its kind's minerals, by its seed (content/minerals.unt vary): no two bodies of a kind the same
            ex.Lines.Add(made.Name);   // (its kind; its words are in the content and the docs: the card stays short)
            { var parts = made.Get("makeup", "").Split(';'); if (parts.Length > 0 && parts[0].Trim().Length > 0) ex.Lines.Add("made of: " + Short(parts[0].Trim(), 70)); }   // (the first clause of its make-up: the minerals and elements below say the rest)
            if (ex.Mix != null) foreach (var ln in _matter.MakeUpWords(ex.Mix)) ex.Lines.Add(ln);
            double dens; if (double.TryParse(made.Get("density", ""), fs, ci, out dens) && dens > 0)
            {
                ex.Density = dens; double rM = ex.R / Units.Km(1) * 1000, kg = dens * 1000 * 4.0 / 3 * Math.PI * rM * rM * rM; ex.Mass = kg / 1.989e30;
                ex.Lines.Add("density " + dens.ToString("0.#") + " g/cc, mass " + kg.ToString("0.#E+0") + " kg, " + ScaleHud.Words(ex.R / Units.Km(1) * 2) + " across");
            }
            double alb; if (double.TryParse(made.Get("albedo", ""), fs, ci, out alb)) { ex.Albedo = alb; ex.Lines.Add("albedo " + alb.ToString("0.##") + ": reflects " + (alb * 100).ToString("0") + "% of the light" + (alb < 0.06 ? ", darker than coal" : "")); }
            if (made.Props.ContainsKey("linked-meteorite")) ex.Lines.Add("its meteorites: " + made.Get("linked-meteorite", ""));
        }
        // a card's line cut short at a clause (a comma, a colon, a semicolon) before the limit: the whole sentence lives in the content
        private static string Short(string t, int limit)
        {
            if (t == null || t.Length <= limit) return t; int cut = -1;
            foreach (var sep in new[] { "; ", ": ", ", " }) { int k = t.LastIndexOf(sep, limit); if (k > cut) cut = k; }
            return cut > limit / 2 ? t.Substring(0, cut) : t.Substring(0, limit).TrimEnd();
        }
        private static string Secs(double t)
        {
            if (t < 1e-3) return (t * 1e6).ToString("0.#") + " microseconds";
            if (t < 1) return (t * 1e3).ToString("0.#") + " milliseconds";
            if (t < 90) return t.ToString("0.#") + " seconds";
            if (t < 5400) return (t / 60).ToString("0.#") + " minutes";
            return (t / 3600).ToString("0.#") + " hours";
        }
        // a star's own make-up (content/stars.unt makeup, by count of atoms): its kind's shares, its share of everything past helium
        // drawn for the star itself (a third to three times its kind's: stars differ), the rest renormalised
        private string StarMakeUp(ContentEntry kind, double x, double y, double z, out double metals)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            double u = Math.Abs(Math.Sin(x * 2.3 + y * 9.1 + z * 4.7) * 7331.113) % 1;
            double lifeLo, lifeHi; bool longLived = kind.TryRange("life-years", out lifeLo, out lifeHi) && lifeHi >= 5e9;   // the long-lived kinds (K and M dwarfs): many are old, born when the galaxy was poorer in everything past helium
            metals = longLived ? Math.Exp((u - 0.7) * 3.0) : Math.Exp((u - 0.5) * 2.2);
            var names = new List<string>(); var shares = new List<double>(); double total = 0;
            foreach (var bit in kind.Get("makeup", "").Split(','))
            {
                string t = bit.Trim(); int pc = t.IndexOf('%'), sp = pc > 0 ? t.LastIndexOf(' ', pc) : -1; if (sp < 0) continue;
                double share; if (!double.TryParse(t.Substring(sp + 1, pc - sp - 1), fs, ci, out share)) continue;
                string nm = t.Substring(0, sp).Trim(); if (nm != "hydrogen" && nm != "helium") share *= metals;
                names.Add(nm); shares.Add(share); total += share;
            }
            if (total <= 0) return null;
            var parts = new List<string>();
            for (int q = 0; q < names.Count; q++) { double v = shares[q] / total * 100; parts.Add(names[q] + " " + (v >= 1 ? v.ToString("0.#") : v >= 0.01 ? v.ToString("0.00#") : v >= 0.0001 ? v.ToString("0.00000") : v.ToString("0.#E+0")) + "%"); }
            return string.Join(", ", parts.ToArray());
        }
        private ContentEntry StarKindEntry(string kindName) { foreach (var e in _content.OfKind("stars.unt", "star")) if (e.Name == kindName) return e; return null; }
        private ContentEntry StarKindEntryById(string id) { foreach (var e in _content.OfKind("stars.unt", "star")) if (e.Id == id) return e; return null; }
        // the star of the system you are in: its own plasma's make-up, for the dive into its surface
        private string SystemStarMakeUp()
        {
            if (_sysStar < 0 || _sysStar >= _localCount) return null;
            var e = StarKindEntryById(_kinds[_localKind[_sysStar]].Id); if (e == null) return null;
            double m; return StarMakeUp(e, _ax, _ay, _az, out m);
        }
        // the picked hole's radius from you in horizon radii (outside, within sixty of them; inside, the fall), for the well's graph; 0 for none
        private double HoleRadii()
        {
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds);
            if (_tKind != 6 || _tIndex < 0 || _tIndex >= _extras.Count || !_extras[_tIndex].Hole || _extras[_tIndex].Mass <= 0) return 0;
            var ex = _extras[_tIndex]; double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / rs;
            return d < 60 ? d : 0;
        }
        // ── supernovae: every galaxy's, from its seed and the game's time (content/physics.unt @law supernova): when, at which of its
        // stars, which kind (the collapse of a heavy star's core, or a white dwarf blown apart), how heavy the star was; each one's
        // light by its real light curve, its shell of flung matter growing for a hundred thousand years, and the neutron star or
        // black hole born at its middle (one of the bodies round you, picked like any) ──
        private sealed class Sn { public double T, X, Y, Z, Ux, Uy, Uz, Mass; public int Kind; public Galaxy G; public SnKind K; public bool Ia { get { return K.Leaves == "nothing"; } } }   // T: when, in years of the game's time; X Y Z in its galaxy's frame; U: in the universe
        private sealed class SnKind { public string Id, Name, Leaves, Says; public double Share, Peak, Rise, Plateau, Halving, Tail, V, PeakK = 6000; public float R = 1, G = 1, B = 1; }
        private readonly List<SnKind> _snKinds = new List<SnKind>();
        private sealed class SnCache { public int From = int.MaxValue, To = int.MinValue; public bool Full; public readonly List<Sn> Events = new List<Sn>(); }
        private readonly Dictionary<ulong, SnCache> _snCache = new Dictionary<ulong, SnCache>();
        private double _snRate = 3, _snCc = 0.7, _snCcPeak = 5e8, _snIaPeak = 4e9, _snCcRise = 10, _snIaRise = 18, _snPlateau = 100, _snCcV = 5000, _snIaV = 10000, _snFreeYears = 300, _snRemnantYears = 1e5, _snNsMax = 20; private bool _snLoaded;
        private Sn _tSn;                                                                   // the supernova picked (its card, the tour's words)
        private void LoadSupernovaLaw()
        {
            _snLoaded = true; var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("physics.unt", "law"))
            {
                if (e.Id != "supernova") continue;
                Func<string, double, double> num = (k, d) => { double v; return double.TryParse(e.Get(k, ""), fs, ci, out v) ? v : d; };
                _snRate = num("rate-per-century", _snRate); _snCc = num("core-collapse-share", _snCc); _snCcPeak = num("cc-peak-sol", _snCcPeak); _snIaPeak = num("ia-peak-sol", _snIaPeak);
                _snCcRise = num("cc-rise-days", _snCcRise); _snIaRise = num("ia-rise-days", _snIaRise); _snPlateau = num("plateau-days", _snPlateau); _snCcV = num("cc-ejecta-km-s", _snCcV); _snIaV = num("ia-ejecta-km-s", _snIaV);
                _snFreeYears = num("free-years", _snFreeYears); _snRemnantYears = num("remnant-years", _snRemnantYears); _snNsMax = num("neutron-star-max-sol", _snNsMax);
            }
            // the kinds (content/physics.unt @supernova): each with its share, its light curve, how fast its matter flies, what it leaves
            foreach (var e in _content.OfKind("physics.unt", "supernova"))
            {
                Func<string, double, double> num = (k, d) => { double v; return double.TryParse(e.Get(k, ""), fs, ci, out v) ? v : d; };
                var k2 = new SnKind { Id = e.Id, Name = e.Name, Leaves = e.Get("leaves", "core"), Says = e.Get("says", ""), Share = num("share", 0), Peak = num("peak-sol", 5e8), Rise = num("rise-days", 10), Plateau = num("plateau-days", 0), Halving = num("halving-days", 15), Tail = num("tail-share", 0.05), V = num("ejecta-km-s", 5000), PeakK = num("peak-k", 6000) };
                var cw = e.Get("colour", "1 1 1").Split(' '); if (cw.Length == 3) { float.TryParse(cw[0], fs, ci, out k2.R); float.TryParse(cw[1], fs, ci, out k2.G); float.TryParse(cw[2], fs, ci, out k2.B); }
                if (k2.Share > 0) _snKinds.Add(k2);
            }
            if (_snKinds.Count == 0) _snKinds.Add(new SnKind { Id = "ii-p", Name = "Type II-P supernova", Leaves = "core", Share = 1, Peak = _snCcPeak, Rise = _snCcRise, Plateau = _snPlateau, Halving = 8, Tail = 0.05, V = _snCcV });
        }
        // a galaxy's rate a year: the law's rate for a galaxy of a hundred billion stars (about fifty thousand light-years across), by its size squared
        private double SnRate(Galaxy g) { double k = g.Scale / (5e4 / Units.LightYearsPerUnit); return _snRate / 100.0 * Math.Max(0.1, Math.Min(4, k * k)); }
        private void SnYear(Galaxy g, SnCache c, int year)
        {
            var rng = new Rng((ulong)year * 0x9E3779B97F4A7C15UL ^ g.Seed ^ 0xA5A5UL);
            double lam = SnRate(g); int n = (int)Math.Floor(lam) + (rng.NextDouble() < lam - Math.Floor(lam) ? 1 : 0);
            for (int q = 0; q < n; q++)
            {
                var sn = new Sn { G = g, T = year + rng.NextDouble() };
                { double tot = 0; foreach (var k2 in _snKinds) tot += k2.Share; double pick = rng.NextDouble() * tot; sn.Kind = _snKinds.Count - 1; for (int qk = 0; qk < _snKinds.Count; qk++) { pick -= _snKinds[qk].Share; if (pick <= 0) { sn.Kind = qk; break; } } sn.K = _snKinds[sn.Kind]; }
                if (g.FarPts != null && g.FarPts.Length > 0) { var p = g.FarPts[rng.Below(g.FarPts.Length)]; sn.X = p.X; sn.Y = p.Y; sn.Z = p.Z; }
                else { double r = Math.Sqrt(rng.NextDouble()) * 0.5, a = rng.NextDouble() * Math.PI * 2; sn.X = Math.Cos(a) * r; sn.Y = rng.Gaussian() * 0.02; sn.Z = Math.Sin(a) * r; }
                sn.Mass = sn.K.Leaves == "nothing" ? 1.4 : sn.K.Leaves == "magnetar" ? 40 * Math.Pow(3, rng.NextDouble()) : 8 * Math.Pow(5, rng.NextDouble());   // a white dwarf at its limit; a star of 8 to 40 solar masses; a very heavy one, 40 to 120
                ToUniverse(g, sn.X, sn.Y, sn.Z, out sn.Ux, out sn.Uy, out sn.Uz);
                c.Events.Add(sn);
            }
        }
        /// <summary>A galaxy's supernovae up to now: the last few years' (their light), or with its whole history back a hundred thousand
        /// years (their shells and the remnants at their middles) for the galaxy you are in.</summary>
        private SnCache Supernovae(Galaxy g, bool full)
        {
            if (!_snLoaded) LoadSupernovaLaw();
            SnCache c; if (!_snCache.TryGetValue(g.Seed, out c)) { c = new SnCache(); _snCache[g.Seed] = c; }
            int now = (int)Math.Floor(_simYears), from = full ? now - (int)_snRemnantYears : now - 2, to = now + 1;
            if (c.From == int.MaxValue) { for (int y = from; y <= to; y++) SnYear(g, c, y); c.From = from; c.To = to; c.Full = full; }
            else
            {
                if (full && !c.Full) { for (int y = from; y < c.From; y++) SnYear(g, c, y); c.From = Math.Min(c.From, from); c.Full = true; }
                while (c.To < to) { c.To++; SnYear(g, c, c.To); }
            }
            return c;
        }
        // its light, in solar luminosities, by its age in days: the real light curves (content/physics.unt @law supernova)
        private double SnLum(Sn sn, double days)
        {
            if (days < 0) return 0; var k = sn.K;
            double early;
            if (days < k.Rise) early = k.Peak * Math.Pow(days / k.Rise, 2);                                              // the rise: the fireball's growing face
            else if (days < k.Rise + k.Plateau) early = k.Peak * (1 - 0.3 * (days - k.Rise) / k.Plateau);              // a plateau (a thick hydrogen envelope recombining), dimming a little
            else early = k.Peak * (k.Plateau > 0 ? 0.7 : 1) * Math.Pow(0.5, (days - k.Rise - k.Plateau) / k.Halving);   // the fade: halving every so many days
            double tail = k.Peak * k.Tail * Math.Pow(0.5, Math.Max(0, days - k.Rise) / 77);                               // the radioactive tail: cobalt-56, halving every 77 days
            return Math.Max(early, tail);
        }
        // its shell's radius in light-years by its age in years: free expansion, then slowed as it sweeps up the gas between the stars
        private double SnShellLy(Sn sn, double years)
        {
            double v = sn.K.V, free = v * 3.15576e7 / 9.4607e12 * Math.Min(years, _snFreeYears);
            return years <= _snFreeYears ? free : free * Math.Pow(years / _snFreeYears, 0.4);
        }
        private double SnShellKmS(Sn sn, double years) { double v = sn.K.V; return years <= _snFreeYears ? v : v * 0.4 * Math.Pow(_snFreeYears / years, 0.6); }
        private static string Years(double y) { return y < 1 ? (y * 365.25).ToString("0") + " days" : y < 1000 ? y.ToString("0") + " years" : y < 1e6 ? (y / 1000).ToString("0.#") + " thousand years" : (y / 1e6).ToString("0.#") + " million years"; }
        private int _snLogTick = -1;   // (the log's clock for the tour's supernova: what its fireball is doing, twice a second)
        private UploadBuffer _snPointBuf, _shellYoungBuf, _shellOldBuf, _fireBuf, _shellCrabBuf, _shellCasBuf, _ejectaBuf; private int _ejectaN; private int _snPointCap, _shellN; private StarSurface _fireSurf; private StarVertex[] _fireVerts;   // (_fireSurf: a fireball's face, drawn as a star's, coloured by its heat each frame)
        private void MakeShells()
        {
            // a shell of flung matter: a thin sphere of points, bunched into filaments (knots and streamers, as the real ones are), the
            // young one yellow-white and red (hot, glowing in hydrogen and oxygen), the old one faint and blue-green
            // in the real colours of their glowing gas: hydrogen's red, oxygen's green and teal, sulfur's orange, the blue-white of electrons
            // spiralling in a magnetic field; each remnant one of the real looks (the Crab's red filaments round a blue-white heart, Cas A's
            // green oxygen and orange sulfur knots, the Veil's red and teal lace), the old ones faint red and blue
            var rng = new Rng(0x5E11UL); var young = new List<StarVertex>(); var old = new List<StarVertex>(); var crab = new List<StarVertex>(); var cas = new List<StarVertex>();
            for (int k = 0; k < 2400; k++)
            {
                double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, sq = Math.Sqrt(1 - u * u);
                double fil = 0.5 + 0.5 * Math.Sin(a * 7 + u * 11) * Math.Sin(u * 13 - a * 3);      // the filaments: bunched, not even
                double r = 1 + (rng.NextDouble() - 0.5) * 0.12 - 0.06 * fil;
                float br = (float)(0.25 + 0.75 * fil * fil); bool lace = fil > 0.55;
                float x = (float)(sq * Math.Cos(a) * r), y = (float)(u * r), z = (float)(sq * Math.Sin(a) * r);
                young.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.05f, R = (lace ? 1f : 0.3f) * br, G = (lace ? 0.32f : 0.85f) * br, B = (lace ? 0.3f : 0.8f) * br, A = 1 });          // the Veil: red hydrogen lace, teal oxygen
                crab.Add(new StarVertex { X = x * (lace ? 1f : 0.6f), Y = y * (lace ? 1f : 0.6f), Z = z * (lace ? 1f : 0.6f), Size = lace ? 0.05f : 0.09f, R = (lace ? 1f : 0.6f) * br, G = (lace ? 0.35f : 0.7f) * br, B = (lace ? 0.3f : 1f) * br, A = 1 });   // the Crab: red filaments round a blue-white glow of spiralling electrons
                cas.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.05f, R = (lace ? 0.3f : 1f) * br, G = (lace ? 0.95f : 0.5f) * br, B = (lace ? 0.55f : 0.2f) * br, A = 1 });            // Cas A: green oxygen knots, orange sulfur
                old.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.06f, R = (lace ? 0.6f : 0.35f) * br, G = (lace ? 0.3f : 0.4f) * br, B = (lace ? 0.35f : 0.7f) * br, A = 1 });
            }
            _shellN = young.Count;
            _shellYoungBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellYoungBuf.Write(young.ToArray(), _shellN);
            _shellOldBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellOldBuf.Write(old.ToArray(), _shellN);
            _shellCrabBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellCrabBuf.Write(crab.ToArray(), _shellN);
            _shellCasBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellCasBuf.Write(cas.ToArray(), _shellN);
            // the ejecta's cloud: the flung matter filling the shell, soft points through its volume (denser toward the edge, as matter
            // swept up is), in hot gas's colours, so a young supernova is a ballooning cloud round its glowing face, never a bare star
            var cloud = new List<StarVertex>(); var rngC = new Rng(0xC10DUL);
            for (int k = 0; k < 2400; k++)
            {
                double u = rngC.NextDouble() * 2 - 1, a = rngC.NextDouble() * Math.PI * 2, sq = Math.Sqrt(1 - u * u), r = 0.35 + 0.65 * Math.Pow(rngC.NextDouble(), 0.4); float br = (float)(0.3 + 0.5 * rngC.NextDouble());
                cloud.Add(new StarVertex { X = (float)(sq * Math.Cos(a) * r), Y = (float)(u * r), Z = (float)(sq * Math.Sin(a) * r), Size = 0.16f, R = 1f * br, G = 0.55f * br, B = 0.35f * br, A = 1 });
            }
            _ejectaN = cloud.Count; _ejectaBuf = new UploadBuffer(_gpu.Device, (long)_ejectaN * Stride); _ejectaBuf.Write(cloud.ToArray(), _ejectaN);
        }
        // the colour of a glowing surface by its heat (the usual blackbody look: orange at 3,000 K, white near 6,500, blue-white past 10,000)
        private static void Blackbody(double tK, out float r, out float g, out float b)
        {
            double t = Math.Max(1500, Math.Min(60000, tK)) / 100;
            double rr = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592), gg = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492), bb = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            r = (float)Math.Max(0, Math.Min(1, rr / 255)); g = (float)Math.Max(0, Math.Min(1, gg / 255)); b = (float)Math.Max(0, Math.Min(1, bb / 255));
        }
        // its glowing face's heat by its age: a hundred thousand degrees at the shock's breakout, cooling to its kind's heat by the
        // peak, about 5,500 on a plateau, settling near 4,500 as it fades (the real run of every kind's photosphere)
        private double SnHeatK(Sn sn, double days)
        {
            var k = sn.K;
            if (days < k.Rise) return Math.Min(100000, k.PeakK * Math.Pow(k.Rise / Math.Max(0.02, days), 0.7));
            if (days < k.Rise + k.Plateau) return k.PeakK - (k.PeakK - 5000) * (days - k.Rise) / k.Plateau;
            return Math.Max(4500, k.PeakK - (k.PeakK - 4500) * Math.Min(1, (days - k.Rise - k.Plateau) / 60));
        }
        // the radius (km) of its glowing face: out at about six tenths of the ejecta's speed, to about ten billion km, holding while
        // it shines, then drawing back in as the fireball thins to a cloud (the nebular phase); and of the ejecta's own edge
        private double SnPhotKm(Sn sn, double days)
        {
            var k = sn.K; double end = k.Rise + k.Plateau + 3 * k.Halving, cap = 1e10 * Math.Sqrt(k.Peak / 5e8);
            double r = Math.Min(cap, 0.6 * k.V * 86400 * Math.Max(0, days));
            return days <= end ? r : r * Math.Exp(-(days - end) / 40);
        }
        private double SnEjectaKm(Sn sn, double days) { return sn.K.V * 86400 * Math.Max(0, days); }
        /// <summary>The supernovae going now in the galaxies round you, each as bright as physics says from where you are (its light
        /// over the square of its distance, against a star of one solar luminosity ten light-years off: a glow that grows with the logarithm of that,
        /// so in its own galaxy it outshines every star, as a real one does, and from another galaxy it is one point among that
        /// galaxy's); close in, the fireball itself at its real size and the colour of its heat, then the thinning cloud of its
        /// ejecta with its remnant inside; and in the galaxy you are in, the shells of the old ones, each at its own size by its age.</summary>
        private void DrawSupernovae(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (!_snLoaded) LoadSupernovaLaw();
            if (_shellYoungBuf == null) MakeShells();
            double now = _simYears; var pts = new List<StarVertex>();
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double gx = (g.X - _ax) - _ox, gy = (g.Y - _ay) - _oy, gz = (g.Z - _az) - _oz, gd = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                if (gd > 400 * g.Scale) continue;
                bool full = i == _near; var c = Supernovae(g, full); if (c.Events.Count == 0) continue;
                foreach (var sn in c.Events)
                {
                    double years = now - sn.T; if (years < 0) continue; double days = years * 365.25;
                    if (_tSn != null && Math.Abs(sn.T - _tSn.T) < 1e-9 && Math.Abs(sn.Ux - _tSn.Ux) < 1e-6 && Math.Abs(sn.Uz - _tSn.Uz) < 1e-6 && (int)(time * 2) != _snLogTick)
                    {
                        // the supernova the tour is at, in the log: its age, its light, its face and its ejecta now, how far off, so a blank
                        // stop can be read from the log instead of guessed at
                        _snLogTick = (int)(time * 2); double uxl = (sn.Ux - _ax) - _ox, uyl = (sn.Uy - _ay) - _oy, uzl = (sn.Uz - _az) - _oz;
                        ZoomLog("SN at the tour's pick: " + sn.K.Name + ", " + days.ToString("0") + " days, light " + SnLum(sn, days).ToString("0.#E+0") + " sol, face " + ScaleHud.Words(SnPhotKm(sn, days)) + " across, ejecta " + ScaleHud.Words(SnEjectaKm(sn, days)) + ", " + Units.Describe(Math.Sqrt(uxl * uxl + uyl * uyl + uzl * uzl)) + " away, galaxy " + (full ? "near" : "far") + ", leaves " + sn.K.Leaves + ", remnant bodies " + _extras.Count);
                    }
                    double ux = (sn.Ux - _ax) - _ox, uy = (sn.Uy - _ay) - _oy, uz = (sn.Uz - _az) - _oz, d = Math.Sqrt(ux * ux + uy * uy + uz * uz), dLy = Math.Max(1e-9, d * Units.LightYearsPerUnit);
                    if (days < 700)
                    {
                        double L = SnLum(sn, days); if (L < 1e4) continue;
                        double rPhot = Units.Km(SnPhotKm(sn, days)), rEj = Units.Km(SnEjectaKm(sn, days)); float cr, cg, cb; Blackbody(SnHeatK(sn, days), out cr, out cg, out cb);
                        bool closeIn = rPhot > 0 && rPhot / Math.Max(1e-30, d) * h > 6;
                        if (!closeIn)
                        {
                            double ratio = L / (dLy * dLy) * 100;                                                // against one solar luminosity at ten light-years
                            double frac = 0.0022 * Math.Max(0.25, 1 + 0.5 * Math.Log10(Math.Max(1e-6, ratio)));     // its glow's share of the view
                            float size = (float)(frac * d);                                                         // (the glow is that share of the view at its distance)
                            float fr = sn.K.R * 0.5f + cr * 0.5f, fg = sn.K.G * 0.5f + cg * 0.5f, fb = sn.K.B * 0.5f + cb * 0.5f;
                            pts.Add(new StarVertex { X = (float)ux, Y = (float)uy, Z = (float)uz, Size = size, R = fr, G = fg, B = fb, A = 1 });
                            if (ratio > 1e3) pts.Add(new StarVertex { X = (float)ux, Y = (float)uy, Z = (float)uz, Size = size * 3.5f, R = fr * 0.12f, G = fg * 0.12f, B = fb * 0.12f, A = 1 });   // its halo, once it is brilliant
                        }
                        else
                        {
                            // the fireball: its glowing face, a ball of plasma at its real size in the colour of its heat, drawn as a star's
                            // face is (one sheet of boiling cells, darker to the limb), full-bright while it shines (a photosphere's face is
                            // as bright as its heat says, whatever its light is against its peak: a thin dim puff of points had stood for it,
                            // and at two hundred days there was nothing to see in the ring), fading only as it thins to a cloud on the tail
                            double end = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving; float bright = (float)(L > 1e6 ? 1 : Math.Max(0.25, days <= end ? 1 : Math.Exp(-(days - end) / 40)));   // (full while it blazes at over a million suns, whatever its day; on the tail a quarter at the least: the thinning cloud still glows, the remnant seen through it)
                            if (_fireSurf == null) { string nm0; StarGen.Build(_content, "supernova-fireball", 0xF1AEUL, out nm0, out _fireSurf); _fireSurf.Giant = true; _fireSurf.Cell = 1.6f; _fireVerts = new StarVertex[_fireSurf.Normals.Length]; _fireBuf = new UploadBuffer(_gpu.Device, (long)_fireVerts.Length * Stride); }
                            _fireSurf.R = cr; _fireSurf.G = cg; _fireSurf.B = cb; _fireSurf.Hot = SnHeatK(sn, days) > 7000;
                            var toEye = new Vec3((float)(-ux / d), (float)(-uy / d), (float)(-uz / d));
                            int n = _fireSurf.Draw(toEye, time, _fireVerts, 0); _fireBuf.Write(_fireVerts, n);
                            var mf = Mat4.Identity; mf.M11 = mf.M22 = mf.M33 = (float)rPhot; mf.M41 = (float)ux; mf.M42 = (float)uy; mf.M43 = (float)uz;
                            DrawPoints(_fireBuf, n, mf * vp, right, up, 1f, 0f, w, h, time, 0, bright);
                        }
                        if (rEj > rPhot * 1.2 && rEj / Math.Max(1e-30, d) * h > 2.5)
                        {
                            // the ejecta: the flung matter's cloud ballooning out round the glowing face, bright while the supernova shines
                            // whatever its light is against its peak (its thin edge at a tenth had left a bare star with no cloud at every
                            // stop), and its glowing edge racing out ahead; what is left once the fireball has thinned
                            float fade = (float)Math.Max(L > 1e6 ? 0.85 : 0.2, Math.Min(0.9, L / sn.K.Peak + 0.1));
                            var m = Mat4.Identity; m.M11 = (float)rEj; m.M22 = (float)rEj; m.M33 = (float)rEj; m.M41 = (float)ux; m.M42 = (float)uy; m.M43 = (float)uz;
                            DrawPoints(_ejectaBuf, _ejectaN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade * 0.55f);
                            DrawPoints(_shellYoungBuf, _shellN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade);
                        }
                    }
                    else if (full && years < _snRemnantYears)
                    {
                        // a remnant's shell: a faint nebula, seen only from near (within about two thousand light-years, the youngest from
                        // farther), never a galaxy-wide crop of gas balls; a going supernova is the thing seen across the galaxy
                        double reachLy = years < 2000 ? 6000 : years < 20000 ? 2500 : 1200; if (dLy > reachLy) continue;
                        double rly = SnShellLy(sn, years), r = rly / Units.LightYearsPerUnit;
                        if (r / Math.Max(1e-30, d) * h < 2.5) continue;                                          // (too small on screen to show)
                        float fade = (float)(Math.Max(0.04, Math.Min(1, Math.Pow(300 / Math.Max(300, years), 0.4))) * Math.Min(1, (reachLy - dLy) / (reachLy * 0.5)));
                        var m = Mat4.Identity; m.M11 = (float)r; m.M22 = (float)r; m.M33 = (float)r; m.M41 = (float)ux; m.M42 = (float)uy; m.M43 = (float)uz;
                        int look = (int)(Math.Abs(sn.T * 1000) % 3);
                        DrawPoints(years > 30000 ? _shellOldBuf : look == 0 ? _shellCrabBuf : look == 1 ? _shellCasBuf : _shellYoungBuf, _shellN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade);
                    }
                }
            }
            if (pts.Count > 0)
            {
                if (_snPointBuf == null || _snPointCap < pts.Count) { if (_snPointBuf != null) _snPointBuf.Dispose(); _snPointCap = pts.Count + 64; _snPointBuf = new UploadBuffer(_gpu.Device, (long)_snPointCap * Stride); }
                var arr = pts.ToArray(); _snPointBuf.Write(arr, arr.Length);
                DrawPoints(_snPointBuf, arr.Length, vp, right, up, 1f, 0.0005f, w, h, time, 0, 1f, 2f);
            }
        }
        // the nearest supernova going now (within about a year of its blast), in any galaxy made round you; null for none
        private Sn NearestSupernova()
        {
            Sn best = null; double bd = double.MaxValue;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var c = Supernovae(_galaxies[i], i == _near);
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > 1.2) continue; double days = years * 365.25, end = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving;
                    // the nearest, but a shining one (its face still blazing) far before one on its tail, and one that leaves a neutron star
                    // or a black hole before a white dwarf blown to nothing (the tour had kept landing on white dwarfs a hundred and seventy
                    // days in, their faces shrunk and dimmed: empty space at the stop)
                    double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) * (days > end ? 50 : 1) * (sn.K.Leaves == "nothing" ? 8 : 1);
                    if (d < bd) { bd = d; best = sn; }
                }
            }
            return best;
        }
        // the youngest supernova remnant in the galaxy you are in (a few years to a few thousand old): its shell still bright, the
        // neutron star or black hole born at its middle; the nearest of the young ones
        private Sn YoungRemnant()
        {
            if (_near < 0 || _near >= _galaxies.Count) return null; var c = Supernovae(_galaxies[_near], true); Sn best = null; double bs = double.MaxValue;
            foreach (var sn in c.Events)
            {
                double years = _simYears - sn.T; if (years < 1.2 || years > 3000) continue;
                double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, score = Math.Sqrt(dx * dx + dy * dy + dz * dz) * (1 + years / 300);
                if (score < bs) { bs = score; best = sn; }
            }
            return best;
        }
        private void PickSupernova(Sn sn)
        {
            double years = _simYears - sn.T, days = Math.Max(1, years * 365.25);
            _tKind = 1; _tx = sn.Ux; _ty = sn.Uy; _tz = sn.Uz; _tName = "a supernova"; _tSn = sn;
            // the shots' measure: the glowing face while it shines; once it has thinned (the tail), or for a remnant, the neutron star
            // or black hole at its middle, so the tour flies in to what is there (it had gone to ten times the face's size at its
            // biggest, a thousand AU from a face shrunk to nothing and a remnant not yet made: empty space); a white dwarf blown apart
            // leaves nothing, and its shots stay at its thinning cloud
            double endD = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving; bool tail = days > endD;
            _tRadius = sn.K.Leaves != "nothing" && (tail || years > 2) ? Units.Km(sn.K.Leaves == "magnetar" || sn.Mass < _snNsMax ? 12 : Physics.HorizonKm(sn.Mass) * 3)
                : years > 2 ? Units.Km(SnShellLy(sn, years) * 9.4607e12)
                : Units.Km(Math.Max(1e8, Math.Max(SnPhotKm(sn, days), SnEjectaKm(sn, days) * 0.5)));   // (the shots framed on the ballooning cloud, not only the face inside it)
        }
        // a click on one: the supernova going now nearest the line under the mouse, within about two degrees
        private bool ClickSupernova(Vec3 dir)
        {
            Sn best = null; double bestAng = 0.035;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double gx = (g.X - _ax) - _ox, gy = (g.Y - _ay) - _oy, gz = (g.Z - _az) - _oz; if (gx * gx + gy * gy + gz * gz > Math.Pow(400 * g.Scale, 2)) continue;
                var c = Supernovae(g, i == _near);
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > 1.5) continue;
                    double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0) continue;
                    double ang = Math.Acos(Math.Max(-1, Math.Min(1, (dx * dir.X + dy * dir.Y + dz * dir.Z) / l))); if (ang < bestAng) { bestAng = ang; best = sn; }
                }
            }
            if (best == null) return false; PickSupernova(best); return true;
        }
        private string SupernovaWords()
        {
            var sn = _tSn; if (sn == null) return "a supernova";
            if (_simYears - sn.T > 2)
            {
                double yrs = _simYears - sn.T;
                return "A " + sn.K.Name.ToLowerInvariant() + " " + Years(yrs) + " ago, mid-process: its shell " + SnShellLy(sn, yrs).ToString("0.#") + " light-years out, moving at " + SnShellKmS(sn, yrs).ToString("N0") + " km/s, " + (yrs < 300 ? "flying free" : "slowed as it sweeps up the gas between the stars") + "; " + (sn.K.Leaves == "nothing" ? "nothing at its middle but the shell" : (sn.K.Leaves == "magnetar" ? "a magnetar" : sn.Mass < _snNsMax ? "a neutron star" : "a black hole") + " at its middle, born in it; the star that died is gone, its worlds with it");
            }
            double days = (_simYears - sn.T) * 365.25, L = SnLum(sn, days), v = sn.K.V; string stage = days < sn.K.Rise ? "still rising" : days < sn.K.Rise + sn.K.Plateau ? "on its plateau" : days < sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? "fading" : "on its long radioactive tail";
            return sn.K.Name + ", " + days.ToString("0") + " days after its blast, " + stage + ": " + (sn.Ia ? "a white dwarf blown apart, " : "the core of a star of " + sn.Mass.ToString("0") + " solar masses collapsed, ") + "shining at " + (L >= 1e6 ? (L / 1e6).ToString("0.#") + " million" : L.ToString("N0")) + " solar luminosities, its matter flung out at " + v.ToString("N0") + " km/s" + (sn.K.Leaves == "nothing" ? "; it leaves nothing but its shell" : sn.K.Leaves == "magnetar" ? "; a magnetar is born at its middle" : sn.Mass < _snNsMax ? "; a neutron star is born at its middle" : "; a black hole is born at its middle");
        }
        private void SupernovaCard(List<string> L, double dist)
        {
            var sn = _tSn; double years = _simYears - sn.T, days = years * 365.25, L0 = SnLum(sn, days), peak = sn.K.Peak, v = sn.K.V;
            if (years > 2)
            {
                // a remnant, mid-process: the shell by its age, what is at its middle, what became of the star and its worlds, what comes next
                L.Add(sn.K.Name + ", " + Years(years) + " ago"); L.Add("id: SN-" + ((long)(sn.T * 1000) & 0xFFFFFF).ToString("X6") + " in " + sn.G.Name);
                L.Add("its shell: " + (SnShellLy(sn, years) * 2).ToString("0.#") + " light-years across, moving at " + SnShellKmS(sn, years).ToString("N0") + " km/s" + (years < _snFreeYears ? " (flying free: it has not yet swept up its own mass of gas)" : " (slowed as it sweeps up the gas between the stars, its radius growing as the two-fifths power of its age)") + (years < 5000 ? "; hot, glowing in hydrogen and oxygen" : "; cooling, faint, blue-green"));
                L.Add(sn.K.Leaves == "nothing" ? "at its middle: nothing; the white dwarf was blown apart entirely" : sn.K.Leaves == "magnetar" ? "at its middle: a magnetar, born in it, lighting the blast from inside as it spins down" : sn.Mass < _snNsMax ? "at its middle: a neutron star, born in it, spinning about " + Math.Min(700, 30 * Math.Sqrt(970 / Math.Max(1, years))).ToString("0") + " times a second; among the bodies round you, picked like any" : "at its middle: a black hole, born in it; among the bodies round you, picked like any");
                L.Add("the star that died: " + (sn.Ia ? "a white dwarf of 1.4 solar masses" : "a star of " + sn.Mass.ToString("0") + " solar masses") + ", gone; its worlds with it: the inner ones vaporised, the outer flung loose");
                L.Add("distance: " + Units.Describe(dist));
                return;
            }
            string stage = days < sn.K.Rise ? "still rising to its peak" : days < sn.K.Rise + sn.K.Plateau ? "on its plateau" : days < sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? "fading, halving every " + sn.K.Halving.ToString("0") + " days" : "on its long radioactive tail (cobalt-56, halving every 77 days)";
            L.Add(sn.K.Name); L.Add("id: SN-" + ((long)(sn.T * 1000) & 0xFFFFFF).ToString("X6") + " in " + sn.G.Name);
            L.Add(sn.Ia ? "a white dwarf of 1.4 solar masses, blown apart entirely" : "a star of " + sn.Mass.ToString("0") + " solar masses, its fuel spent");
            L.Add(days.ToString("0") + " days since its blast, " + stage + "; shining now at " + (L0 >= 1e6 ? (L0 / 1e6).ToString("0.#") + " million" : L0.ToString("N0")) + " solar luminosities (its peak " + (peak / 1e6).ToString("0") + " million: " + (peak >= 2e9 ? "brighter than most galaxies" : "a hundred million stars' light") + ")");
            L.Add("its matter flung out at " + v.ToString("N0") + " km/s (" + (v / 299792.458 * 100).ToString("0.#") + "% of light's speed, the outermost up to a tenth of it): its edge now " + ScaleHud.Words(SnEjectaKm(sn, Math.Max(1, days)) * 2) + " across");
            { double rp = SnPhotKm(sn, days), tk = SnHeatK(sn, days); L.Add(rp > 1e6 ? "its glowing face: " + ScaleHud.Words(rp * 2) + " across, " + tk.ToString("N0") + " K (" + (tk > 9000 ? "blue-white" : tk > 6500 ? "white" : tk > 5000 ? "yellow-white" : "orange") + ")" + (days > sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? ", drawing back in as the fireball thins to a cloud" : "") : "the fireball has thinned to a cloud: its remnant shows at the middle"); }
            L.Add("its worlds: the inner ones vaporised, the outer flung loose; its neighbours scorched");
            L.Add(sn.K.Leaves == "nothing" ? "what it leaves: nothing but its shell, seen for a hundred thousand years as it sweeps up the gas between the stars" : sn.K.Leaves == "magnetar" ? "what it leaves: a magnetar at its middle, a neutron star with a field a thousand times a pulsar's, spinning down and lighting the blast from inside; its shell seen for a hundred thousand years" : sn.Mass < _snNsMax ? "what it leaves: a neutron star at its middle, revealed as the ejecta thin, spinning tens of times a second; its shell seen for a hundred thousand years" : "what it leaves: a black hole at its middle, revealed as the ejecta thin; its shell seen for a hundred thousand years");
            L.Add("this galaxy: about " + (SnRate(sn.G) * 100).ToString("0.#") + " supernovae a century");
            L.Add("distance: " + Units.Describe(dist));
        }
        // a supernova whose shock has reached here: the nearest, and what it did (by how near: within a couple of light-years the worlds'
        // air is stripped and their faces scorched; within ten the air thins and its ozone goes, life on the exposed sides dies; out to
        // about thirty the sky is lit as by a second star for months and the particles harm life)
        private string ScorchWords(double ux, double uy, double uz)
        {
            if (_near < 0 || _near >= _galaxies.Count) return null; var c = Supernovae(_galaxies[_near], true); string best = null; double bd = double.MaxValue;
            foreach (var sn in c.Events)
            {
                double years = _simYears - sn.T; if (years < 0 || years > _snRemnantYears) continue;
                double dx = sn.Ux - ux, dy = sn.Uy - uy, dz = sn.Uz - uz, dLy = Math.Sqrt(dx * dx + dy * dy + dz * dz) * Units.LightYearsPerUnit;
                if (dLy > 30 || dLy > SnShellLy(sn, years) || dLy >= bd || dLy < 1e-6) continue;
                bd = dLy; best = "a " + sn.K.Name.ToLowerInvariant() + " " + dLy.ToString("0.#") + " light-years away, " + Years(years) + " ago: its shock has passed here" + (dLy < 2 ? "; the worlds' air stripped and their faces scorched, their life gone" : dLy < 10 ? "; the air thinned and its ozone gone, life on the exposed sides killed" : "; the sky lit as by a second star for months, its particles harming life");
            }
            return best;
        }
        private HoleParts PartsFor(Place pl, double spin)
        {
            HoleParts hp; string id = (pl != null ? pl.Id : "") + ":" + Math.Round(spin, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); if (_holeParts.TryGetValue(id, out hp)) return hp;
            // its disc's inner edge by its spin: the innermost stable orbit comes in from three horizon radii at rest (content/physics.unt @law frame-dragging)
            double lo = Math.Min(pl != null ? pl.DiscLo : 3, Math.Max(0.6, IscoRs(spin))), hi = pl != null ? pl.DiscHi : 60, secs = pl != null ? pl.OrbitSeconds : 8;
            var rng = new Rng(0xB1AC4UL); var disc = new List<StarVertex>(); var jets = new List<StarVertex>(); var swarm = new List<StarVertex>(); var stream = new List<StarVertex>();
            for (int k = 0; k < 28000; k++)
            {
                // the disc's gas from the innermost stable orbit out to its rim, more of it close in; hottest and brightest inside
                // (blue-white), cooling outward (yellow, orange, red), thin, with denser streams through it (sheared into spirals
                // by the rotation, since each point goes round at its own rate)
                double r = lo * Math.Pow(hi / lo, Math.Pow(rng.NextDouble(), 1.6)), a = rng.NextDouble() * Math.PI * 2, t = Math.Pow(lo / r, 0.75);
                double dens = 0.82 + 0.18 * Math.Sin(Math.Log(r / lo) * 21 + rng.NextDouble() * 0.6) + 0.12 * (rng.NextDouble() - 0.5);   // fine turbulent bands, never arms (the Kepler shear on the card is all the spiral there is)
                float br = (float)(Math.Min(1, 1.4 * Math.Pow(lo / r, 1.1) + 0.06) * dens);
                float cr = (float)(1.0 * br), cg = (float)((0.45 + 0.55 * t) * br), cb = (float)((0.15 + 0.85 * t * t) * br);
                disc.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)(rng.Gaussian() * r * 0.02), Z = (float)(Math.Sin(a) * r), Size = (float)(0.14 + r * 0.012), R = cr, G = cg, B = cb, A = 1 });
            }
            for (int k = 0; k < 2400; k++)
            {
                // two beams from the poles, narrow, fading as they go, blue-violet
                double y = 3 + Math.Pow(rng.NextDouble(), 1.8) * 600, w = 0.6 + y * 0.03, a = rng.NextDouble() * Math.PI * 2, f = Math.Exp(-y / 300);
                float br = (float)(0.25 + 0.75 * f); int side = (k & 1) == 0 ? 1 : -1;
                jets.Add(new StarVertex { X = (float)(Math.Cos(a) * w * rng.NextDouble()), Y = (float)(side * y), Z = (float)(Math.Sin(a) * w * rng.NextDouble()), Size = (float)(1 + y * 0.01), R = 0.55f * br, G = 0.6f * br, B = 1f * br, A = 1 });
            }
            int nsw = pl != null ? pl.Swarm : 0;
            for (int k = 0; k < nsw; k++)
            {
                // a star of the swarm round the hole: its ellipse (size, shape, timing) and its orientation in its record, its path
                // worked out on the card; in the colours of stars
                double sa = pl.SwarmLo * Math.Pow(pl.SwarmHi / pl.SwarmLo, rng.NextDouble()), ecc = 0.2 + 0.7 * rng.NextDouble(), ph = rng.NextDouble() * Math.PI * 2;
                uint seed = (uint)rng.Below(0x7F000000); float heat = (float)rng.NextDouble();
                swarm.Add(new StarVertex { X = (float)sa, Y = (float)ecc, Z = (float)ph, Size = BitConverter.ToSingle(BitConverter.GetBytes(seed), 0), R = 1f, G = 0.75f + 0.25f * heat, B = 0.55f + 0.45f * heat, A = 1 });
            }
            for (int k = 0; k < 1800; k++)
            {
                // the torn star: each point a share of the way along its stream (denser at the head, the star's remains), with its
                // own small offset across the stream; its brightness in R
                double u = Math.Pow(rng.NextDouble(), 1.4);
                stream.Add(new StarVertex { X = (float)u, Y = (float)(rng.Gaussian() * 0.22), Z = (float)(rng.Gaussian() * 0.22), Size = (float)(0.25 + 0.4 * rng.NextDouble()), R = (float)(0.6 + 0.4 * rng.NextDouble()), G = 1, B = 1, A = 1 });
            }
            hp = new HoleParts { DiscN = disc.Count, JetsN = jets.Count, SwarmN = swarm.Count, StreamN = stream.Count, K = (float)(2 * Math.PI * Math.Pow(lo, 1.5) / secs) };
            hp.Disc = new UploadBuffer(_gpu.Device, (long)disc.Count * Stride); hp.Disc.Write(disc.ToArray(), disc.Count);
            hp.Jets = new UploadBuffer(_gpu.Device, (long)jets.Count * Stride); hp.Jets.Write(jets.ToArray(), jets.Count);
            if (swarm.Count > 0) { hp.Swarm = new UploadBuffer(_gpu.Device, (long)swarm.Count * Stride); hp.Swarm.Write(swarm.ToArray(), swarm.Count); }
            hp.Stream = new UploadBuffer(_gpu.Device, (long)stream.Count * Stride); hp.Stream.Write(stream.ToArray(), stream.Count);
            _holeParts[id] = hp; return hp;
        }
        private void DrawBlackHoles(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (rs * 600 / Math.Max(1e-30, d) * h < 2) continue;                      // (too small on screen to show yet)
                var hp = PartsFor(ex.Pl, ex.Spin);
                // its frame: tilted its own way, its horizon's radius 1; nothing turns as a plate: every point moves on its own
                // orbit on the card (the hole's own turn is only a phase of the time)
                float sc = (float)rs, ct = (float)Math.Cos(ex.Tilt), st = (float)Math.Sin(ex.Tilt);
                var tilt = Mat4.Identity; tilt.M22 = ct; tilt.M23 = st; tilt.M32 = -st; tilt.M33 = ct;
                var scale = Mat4.Identity; scale.M11 = sc; scale.M22 = sc; scale.M33 = sc;
                var place = tilt * scale * Mat4.Translation((float)dx, (float)dy, (float)dz);
                // where the camera is in that frame (for the beaming: which side of the disc comes toward you)
                double cx = -dx / rs, cy0 = -dy / rs, cz0 = -dz / rs; float hx = (float)cx, hy = (float)(cy0 * ct + cz0 * st), hz = (float)(-cy0 * st + cz0 * ct);
                double tt = time + ex.Turn * 10;
                // its gravity well: the dip of space round it (Flamm's paraboloid: the depth 2 root(rs (r - rs)), the real shape of space's
                // stretch round a mass), rings and spokes from the horizon out to forty radii, and at the horizon a sharp step straight down to
                // a point: the singularity. Round every hole, feeding or quiet; the disc, swarm and jets only round a feeding one
                if (_wellBuf == null) MakeWell();
                // (no well drawn in space: the shadow, the disc and the bent light are what a hole shows from outside; the well is the fall's own, inside)
                if (ex.Feeding > 0) DrawPoints(hp.Disc, hp.DiscN, place * vp, right, up, 1f, 0.0012f / sc, w, h, tt, 0.02f / sc, 1f, 2f, hp.K, 1, hx, hy, hz);
                if (ex.Feeding == 2) DrawPoints(hp.Jets, hp.JetsN, place * vp, right, up, 1f, 0.0012f / sc, w, h, time, 0.02f / sc, 1f, 2f);
                if (ex.Feeding > 0 && hp.SwarmN > 0) DrawPoints(hp.Swarm, hp.SwarmN, place * vp, right, up, 1f, 0.0018f / sc, w, h, tt, 0.03f / sc, 1f, 2f, hp.K, 2, hx, hy, hz);
                if (ex.Eating) for (int q = 0; q < Math.Max(1, ex.EatN); q++)
                {
                    // each star being eaten: its own stream, turned its own way round the axis and at its own moment (the stars came from different directions, at different times)
                    double ra = q * Math.PI * 2 / Math.Max(1, ex.EatN) + ex.Turn * 3; float cq = (float)Math.Cos(ra), sq = (float)Math.Sin(ra);
                    var roty = Mat4.Identity; roty.M11 = cq; roty.M13 = sq; roty.M31 = -sq; roty.M33 = cq;
                    float hx2 = hx * cq - hz * sq, hz2 = hx * sq + hz * cq;
                    DrawPoints(hp.Stream, hp.StreamN, roty * place * vp, right, up, 1f, 0.0012f / sc, w, h, tt + q * 37, 0.02f / sc, 1f, 2f, hp.K, 3, hx2, hy, hz2);
                }
            }
        }
        // -- asteroids and comets close in: a lumpy cratered rock lit from its star's side, made once (radius about 1) and drawn for
        // each, turned to its star and spinning on that line (so its lit side stays lit); a comet's dark nucleus in its coma, with
        // its ion tail straight away from the star and its wider curved dust tail --
        private UploadBuffer _cometBuf; private int _cometN;
        private bool ShapeShown(Extra ex, int h)
        {
            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double reach = ex.Shape == "comet" ? Units.Km(ex.TailKm) / Math.Max(1e-30, ex.R) : 1;   // (a comet shows by its tail, millions of km long: across its whole system)
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
        // a solid body: a sphere of triangles (an icosahedron cut four times), pushed in and out by broad lumps and pitted by craters,
        // drawn as lit stone with flat facets (never points of light); its star along +x
        private UploadBuffer _rockSolid, _roundSolid, _nucSolid; private int _rockSolidN, _roundSolidN, _nucSolidN; private Func<double[], double[]> _rockShape, _roundShape, _nucShape; private const int RockBands = 4, RockSeeds = 3; private static readonly double[] RockLumps = { 1.3, 0.9, 0.55, 0.25 }; private Func<double[], double[]>[] _rockShapes; private UploadBuffer[] _rockSolids; private int[] _rockSolidNs; private double _lastDrawTime; private int _diveLogTick = -1; private double[] _tSpot;   // _tSpot: a spot picked on the small body you are at (unit, from its middle): the zoom flies to it, as to a spot of a world's land
        // a small body's shape: its radius and shade along any direction from its middle (its bumps, craters, stretch and grit), the
        // one function the mesh is built from and a dive lands on, so the ground you stand on is the stone that is drawn
        private static Func<double[], double[]> RockShape(Rng rng, double lump)
        {
            var bumps = new double[14][]; for (int q = 0; q < 14; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); bumps[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), (rng.NextDouble() - 0.5) * 0.55 * lump, 1 + rng.NextDouble() * 3 }; }
            var craters = new double[40][]; for (int q = 0; q < 40; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); craters[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), Math.Cos(0.05 + rng.NextDouble() * 0.28), rng.NextDouble() }; }
            Func<double[], double[]> place = v =>
            {
                double x = v[0], y = v[1], z = v[2], rad = 1, shade = 1;
                foreach (var bq in bumps) rad += bq[3] * Math.Pow(Math.Max(0, x * bq[0] + y * bq[1] + z * bq[2]), bq[4]);
                foreach (var cq in craters)
                {
                    double c = x * cq[0] + y * cq[1] + z * cq[2]; if (c < cq[3] - 0.03) continue;
                    double t = (c - cq[3]) / (1 - cq[3]);
                    if (t > 0) { rad -= 0.06 * (1 - cq[3]) * 12 * Math.Sqrt(Math.Max(0, 1 - (1 - t) * (1 - t))) * 0.25; shade *= 0.8 + 0.2 * t; } else rad += 0.008;   // a bowl, and its raised rim
                }
                double grit = 0.94 + 0.12 * (Math.Abs(Math.Sin(x * 91.7 + y * 37.3 + z * 63.1) * 4375.5) % 1);
                return new[] { x * (1 + 0.5 * lump) * rad, y * (1 - 0.15 * lump) * rad, z * rad, shade * grit };
            };
            return place;
        }
        private static List<StarVertex> SolidRock(Rng rng, double lump, float r0, float g0, float b0)
        {
            var place = RockShape(rng, lump);
            // an icosahedron, each face cut in four, four times over, each corner put out to the sphere
            double t0 = (1 + Math.Sqrt(5)) / 2; var vs = new List<double[]>();
            double[][] iv = { new[] { -1, t0, 0 }, new[] { 1, t0, 0 }, new[] { -1, -t0, 0 }, new[] { 1, -t0, 0 }, new[] { 0, -1, t0 }, new[] { 0, 1, t0 }, new[] { 0, -1, -t0 }, new[] { 0, 1, -t0 }, new[] { t0, 0, -1 }, new[] { t0, 0, 1 }, new[] { -t0, 0, -1 }, new[] { -t0, 0, 1 } };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            Func<double[], double[]> unit = v => { double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); return new[] { v[0] / l, v[1] / l, v[2] / l }; };
            var tris = new List<double[][]>(); for (int i = 0; i < f.Length; i += 3) tris.Add(new[] { unit(iv[f[i]]), unit(iv[f[i + 1]]), unit(iv[f[i + 2]]) });
            for (int lv = 0; lv < 4; lv++)
            {
                var nt = new List<double[][]>();
                foreach (var tr in tris)
                {
                    Func<double[], double[], double[]> mid = (a, b) => unit(new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2 });
                    double[] ab = mid(tr[0], tr[1]), bc = mid(tr[1], tr[2]), ca = mid(tr[2], tr[0]);
                    nt.Add(new[] { tr[0], ab, ca }); nt.Add(new[] { ab, tr[1], bc }); nt.Add(new[] { ca, bc, tr[2] }); nt.Add(new[] { ab, bc, ca });
                }
                tris = nt;
            }
            var l2 = new List<StarVertex>(tris.Count * 3);
            foreach (var tr in tris)
                foreach (var c in tr)
                {
                    var p = place(c); float sh = (float)p[3];
                    l2.Add(new StarVertex { X = (float)p[0], Y = (float)p[1], Z = (float)p[2], Size = 0, R = r0 * sh, G = g0 * sh, B = b0 * sh, A = 1 });
                }
            return l2;
        }
        private void MakeSmallBodies()
        {
            {
                _rockShape = RockShape(new Rng(0x5A57E1UL), 1); _roundShape = RockShape(new Rng(0x60BE5UL), 0.08); _nucShape = RockShape(new Rng(0xC0CE7UL), 0.9);   // (the same shapes the meshes are built from, for the dive)
                var a = SolidRock(new Rng(0x5A57E1UL), 1, 0.52f, 0.47f, 0.42f); _rockSolidN = a.Count; _rockSolid = new UploadBuffer(_gpu.Device, (long)a.Count * Stride); _rockSolid.Write(a.ToArray(), a.Count);
                // every rock its own shape: four bands of lumpiness (the smallest the most irregular, the largest near round, as gravity
                // pulls a body round as it grows) by three seeds each; a body's by its size and its own seed, so no two beside each other are alike
                _rockShapes = new Func<double[], double[]>[RockBands * RockSeeds]; _rockSolids = new UploadBuffer[RockBands * RockSeeds]; _rockSolidNs = new int[RockBands * RockSeeds];
                for (int bq = 0; bq < RockBands; bq++) for (int sq = 0; sq < RockSeeds; sq++)
                {
                    int ix = bq * RockSeeds + sq; ulong seed = 0x5A57E1UL + (ulong)(bq * 7 + sq * 131);
                    _rockShapes[ix] = RockShape(new Rng(seed), RockLumps[bq]);
                    var rv = SolidRock(new Rng(seed), RockLumps[bq], 0.52f, 0.47f, 0.42f); _rockSolidNs[ix] = rv.Count; _rockSolids[ix] = new UploadBuffer(_gpu.Device, (long)rv.Count * Stride); _rockSolids[ix].Write(rv.ToArray(), rv.Count);
                }
                var b = SolidRock(new Rng(0x60BE5UL), 0.08, 0.55f, 0.52f, 0.5f); _roundSolidN = b.Count; _roundSolid = new UploadBuffer(_gpu.Device, (long)b.Count * Stride); _roundSolid.Write(b.ToArray(), b.Count);
                var c = SolidRock(new Rng(0xC0CE7UL), 0.9, 0.42f, 0.40f, 0.38f); _nucSolidN = c.Count; _nucSolid = new UploadBuffer(_gpu.Device, (long)c.Count * Stride); _nucSolid.Write(c.ToArray(), c.Count);   // a comet's nucleus: among the darkest bodies there are (albedo 0.04), shown as a camera exposing for it sees it, mid-grey, as Rosetta saw 67P (at its true darkness it was black against the stars) things known
            }
            var cm = new List<StarVertex>(); var rng = new Rng(0xC0CE7UL);   // (the coma and the tails round the solid nucleus)
            for (int k = 0; k < 9000; k++)
            {
                // the coma: gas and dust round the nucleus, densest close in, out to about 2000 times its size
                double rr = Math.Exp(Math.Log(0.0007) + rng.NextDouble() * Math.Log(1 / 0.0007)), u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, s2 = Math.Sqrt(1 - u * u);   // (in the coma's own size: 1 its edge)
                float br = (float)(0.5 * Math.Pow(0.0007 / rr, 0.35));
                cm.Add(new StarVertex { X = (float)(rr * s2 * Math.Cos(a) + rr * 0.3), Y = (float)(rr * u), Z = (float)(rr * s2 * Math.Sin(a)), Size = (float)(rr * 0.35), R = 0.55f * br, G = 0.75f * br, B = 0.8f * br, A = 1 });
            }
            for (int k = 0; k < 9000; k++)
            {
                // the ion tail: straight away from the star (along -x), thin, blue; the dust tail: wider, curved, yellow-white
                bool ion = (k & 1) == 0; double t = Math.Pow(rng.NextDouble(), 0.7), w = (ion ? 0.02 : 0.08) * t + 0.0033, a = rng.NextDouble() * Math.PI * 2, q = rng.Gaussian() * 0.4;   // (in the tail's own length: 1 its end)
                float br = (float)((ion ? 0.35 : 0.3) * Math.Exp(-t / 0.5));
                double bend = ion ? 0 : t * t * 0.25;
                cm.Add(new StarVertex { X = (float)(-t), Y = (float)(Math.Cos(a) * w * q), Z = (float)(Math.Sin(a) * w * q + bend), Size = (float)(w * 0.8), R = (ion ? 0.35f : 0.9f) * br, G = (ion ? 0.55f : 0.85f) * br, B = (ion ? 1f : 0.7f) * br, A = 1 });
            }
            // the coma (the first 9,000) and the tails (the rest) each on its own, each drawn to its own size
            _comaN = 9000; var tl = cm.GetRange(_comaN, cm.Count - _comaN); _tailN = tl.Count;
            _cometN = _comaN; _cometBuf = new UploadBuffer(_gpu.Device, (long)_comaN * Stride); _cometBuf.Write(cm.GetRange(0, _comaN).ToArray(), _comaN);
            _tailBuf = new UploadBuffer(_gpu.Device, (long)_tailN * Stride); _tailBuf.Write(tl.ToArray(), _tailN);
        }
        // ── rings: round any world whose template gives it them (content/terrain.unt rings-chance and its rings), made once
        // for each world of the system you are in, in the world's own radii, placed and scaled to it where it is now ──
        private readonly Dictionary<int, UploadBuffer> _ringBufs = new Dictionary<int, UploadBuffer>(); private readonly Dictionary<int, int> _ringCounts = new Dictionary<int, int>(); private object _ringSystem;
        private void DrawRings(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (_system == null) return;
            if (_ringSystem != _system) { foreach (var b0 in _ringBufs.Values) b0.Dispose(); _ringBufs.Clear(); _ringCounts.Clear(); _ringSystem = _system; }
            for (int i = 0; i < _system.Bodies.Count && i + 1 < _bodyCount; i++)
            {
                var b = _system.Bodies[i]; if (!b.Rings || b.Terrain == null) continue;
                double dx = _bodyVerts[i + 1].X, dy = _bodyVerts[i + 1].Y, dz = _bodyVerts[i + 1].Z, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);   // (already from the camera: drawn after the system has placed its bodies)
                if (dl <= 0 || b.RadiusUnits / dl * h < 3) continue;                       // (too small on screen to show)
                UploadBuffer buf;
                if (!_ringBufs.TryGetValue(i, out buf))
                {
                    string nm; var rings = PlanetGen.Build(_content, b.Terrain, WorldSeed(i), out nm);
                    float[] col = PlanetGen.RingColour(_content, b.Terrain);
                    var v = new StarVertex[Math.Max(1, rings.Points.Count)];
                    for (int q = 0; q < rings.Points.Count; q++) { var p = rings.Points[q]; float s = rings.Shade[q] * 2; v[q] = new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0.012f, R = col[0] * s, G = col[1] * s, B = col[2] * s, A = 1 }; }
                    buf = new UploadBuffer(_gpu.Device, (long)v.Length * Stride); buf.Write(v, rings.Points.Count);
                    _ringBufs[i] = buf; _ringCounts[i] = rings.Points.Count;
                }
                if (_ringCounts[i] == 0) continue;
                float sc = (float)b.RadiusUnits; var m = Mat4.Identity; m.M11 = sc; m.M22 = sc; m.M33 = sc; m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                DrawPoints(buf, _ringCounts[i], m * vp, right, up, 1f, 0.0008f, w, h, time, 0f, 1f, 0f);
            }
        }
        // a small body's spin: it turns about its star line, except while you are on it (a dive holds it, so the ground under you is
        // the stone that is drawn and does not sweep away at a kilometre a second)
        private static double BodySpin(Extra ex, double time) { return double.IsNaN(ex.SpinHold) ? time * 0.15 + ex.Turn : ex.SpinHold; }
        // the body's real surface along a direction from its middle (unit, in the universe's frame): the mesh's own shape in the body's
        // frame at this moment, so a dive lands on the stone that is drawn, not on a sphere kilometres inside or above it (a rock is
        // half again as long as it is wide, with bumps and craters besides: "a metre above it" had been nothing but stars)
        // which of the rock shapes a body has: its band by its size against the size gravity rounds a body at (its place's, else
        // 300 km), its seed its own; at or over that size it is the round shape
        private int RockIx(Extra ex)
        {
            double roundKm = ex.Pl != null && ex.Pl.RoundKm > 0 ? ex.Pl.RoundKm : 300, share = ex.R / Units.Km(1) / roundKm;
            int band = share < 0.08 ? 0 : share < 0.3 ? 1 : share < 0.65 ? 2 : 3;
            return band * RockSeeds + ((ex.ShapeIx % RockSeeds) + RockSeeds) % RockSeeds;
        }
        private bool RockRound(Extra ex) { double roundKm = ex.Pl != null && ex.Pl.RoundKm > 0 ? ex.Pl.RoundKm : 300; return ex.R / Units.Km(1) >= roundKm; }
        private double BodyRadiusToward(Extra ex, double time, double dx, double dy, double dz)
        {
            if (_rockSolid == null) MakeSmallBodies();
            double bx = RelX(ex), by = RelY(ex), bz = RelZ(ex), bl = Math.Sqrt(bx * bx + by * by + bz * bz);
            var xs = bl > 0 ? new Vec3((float)(-bx / bl), (float)(-by / bl), (float)(-bz / bl)) : new Vec3(1, 0, 0);
            var ys = Vec3.Cross(xs, Math.Abs(xs.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var zs = Vec3.Cross(xs, ys).Normalized;
            double spin = BodySpin(ex, time); float c = (float)Math.Cos(spin), sn = (float)Math.Sin(spin);
            var y2 = new Vec3(ys.X * c + zs.X * sn, ys.Y * c + zs.Y * sn, ys.Z * c + zs.Z * sn); var z2 = Vec3.Cross(xs, y2);
            if (ex.Shape == "comet") { y2 = ys; z2 = zs; }
            double lx = dx * xs.X + dy * xs.Y + dz * xs.Z, ly = dx * y2.X + dy * y2.Y + dz * y2.Z, lz = dx * z2.X + dy * z2.Y + dz * z2.Z, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); if (ll <= 0) return ex.R;
            bool comet = ex.Shape == "comet", round = !comet && RockRound(ex);
            var shape = comet ? _nucShape : round ? _roundShape : _rockShapes != null ? _rockShapes[RockIx(ex)] : _rockShape; if (shape == null) return ex.R;
            var p = shape(new[] { lx / ll, ly / ll, lz / ll });
            return ex.R * Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
        }
        // a small body's place against the anchor (the star you are at), precise: its star's offset from the anchor (nothing, in its
        // own system) and its own precise offset from its star. Its absolute coordinates are doubles at billions of light-years,
        // where a double's step is about a million kilometres: taken against the anchor they snapped every asteroid to a
        // million-kilometre grid, the rows of bodies set out in straight lines seen in every belt
        private double RelX(Extra ex) { return (ex.OrbCx - _ax) + ex.OffX; }
        private double RelY(Extra ex) { return (ex.OrbCy - _ay) + ex.OffY; }
        private double RelZ(Extra ex) { return (ex.OrbCz - _az) + ex.OffZ; }
        private void DrawSmallBodies(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 || !ShapeShown(ex, h)) continue;
                if (_ground != null && _planet == -2 && ex == _groundExtra) continue;   // (its ground is made and drawn as a world's: the mesh would be a second stone over it)
                if (_rockSolid == null) MakeSmallBodies();
                double bx = RelX(ex), by = RelY(ex), bz = RelZ(ex), dx = bx - _ox, dy = by - _oy, dz = bz - _oz, bl = Math.Sqrt(bx * bx + by * by + bz * bz);
                // its frame: +x toward its star (the anchor of the system you are in), spinning about that line
                var xs = bl > 0 ? new Vec3((float)(-bx / bl), (float)(-by / bl), (float)(-bz / bl)) : new Vec3(1, 0, 0);
                var ys = Vec3.Cross(xs, Math.Abs(xs.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var zs = Vec3.Cross(xs, ys).Normalized;
                double spin = BodySpin(ex, time); float c = (float)Math.Cos(spin), sn = (float)Math.Sin(spin);   // (held still while you are on it)
                var y2 = new Vec3(ys.X * c + zs.X * sn, ys.Y * c + zs.Y * sn, ys.Z * c + zs.Z * sn); var z2 = Vec3.Cross(xs, y2);
                if (ex.Shape == "comet") { y2 = ys; z2 = zs; }                                  // (its tails do not spin)
                float sc = (float)ex.R;
                var m = Mat4.Identity;
                m.M11 = xs.X * sc; m.M12 = xs.Y * sc; m.M13 = xs.Z * sc;
                m.M21 = y2.X * sc; m.M22 = y2.Y * sc; m.M23 = y2.Z * sc;
                m.M31 = z2.X * sc; m.M32 = z2.Y * sc; m.M33 = z2.Z * sc;
                m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                // a comet's coma first: thin gas, bright from afar, only a faint haze once you are inside it (its points stacked
                // into a white blaze that hid the nucleus); its dark nucleus is drawn over it
                if (ex.Shape == "comet")
                {
                    // the coma and the tails, each scaled to its own real size round the nucleus, the same frame (the tails straight
                    // away from the star); the coma thins to a faint haze as you come into it
                    double dc = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Func<double, Mat4> sized = km => { float k = (float)Units.Km(km); var mm = m; mm.M11 = xs.X * k; mm.M12 = xs.Y * k; mm.M13 = xs.Z * k; mm.M21 = y2.X * k; mm.M22 = y2.Y * k; mm.M23 = y2.Z * k; mm.M31 = z2.X * k; mm.M32 = z2.Y * k; mm.M33 = z2.Z * k; return mm; };
                    float inC = (float)Math.Max(0.06, Math.Min(1, Math.Sqrt(dc / Math.Max(1e-30, Units.Km(ex.ComaKm) * 1.5))));
                    // (the smallest a point may be is a share of the view's distance, in the points' own units: divided by their scale)
                    DrawPoints(_cometBuf, _comaN, sized(ex.ComaKm) * vp, right, up, 1f, 0.0008f / (float)Units.Km(ex.ComaKm), w, h, time, 0f, inC, 0f);
                    DrawPoints(_tailBuf, _tailN, sized(ex.TailKm) * vp, right, up, 1f, 0.0008f / (float)Units.Km(ex.TailKm), w, h, time, 0f, 1f, 0f);
                }
                // the body itself, solid stone lit from its star (its camera's place in its own frame, for which faces show)
                {
                    var mvp = m * vp; var mc = new float[BillboardPipeline.ConstantCount];
                    mc[0] = mvp.M11; mc[1] = mvp.M12; mc[2] = mvp.M13; mc[3] = mvp.M14; mc[4] = mvp.M21; mc[5] = mvp.M22; mc[6] = mvp.M23; mc[7] = mvp.M24;
                    mc[8] = mvp.M31; mc[9] = mvp.M32; mc[10] = mvp.M33; mc[11] = mvp.M34; mc[12] = mvp.M41; mc[13] = mvp.M42; mc[14] = mvp.M43; mc[15] = mvp.M44;
                    mc[16] = 1; mc[17] = 0; mc[18] = 0;                                           // its star along its own +x
                    double ix = -dx, iy = -dy, iz = -dz;                                           // the camera from the body, into its frame
                    mc[20] = (float)((ix * xs.X + iy * xs.Y + iz * xs.Z) / sc); mc[21] = (float)((ix * y2.X + iy * y2.Y + iz * y2.Z) / sc); mc[22] = (float)((ix * z2.X + iy * z2.Y + iz * z2.Z) / sc);
                    bool comet = ex.Shape == "comet", round = !comet && RockRound(ex);
                    _gpu.ClearDepth();
                    _meshes.Solid.Draw(comet ? _nucSolid : round ? _roundSolid : _rockSolids[RockIx(ex)], comet ? _nucSolidN : round ? _roundSolidN : _rockSolidNs[RockIx(ex)], mc, 0, 0, w, h);
                }

            }
        }
        private UploadBuffer _extraBuf; private StarVertex[] _extraV = new StarVertex[0]; private readonly float[] _lens = new float[4], _web = new float[5];
        private void LoadPlaces()
        {
            _places = new List<Place>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("bodies.unt", "place"))
            {
                double v; var pl = new Place { Id = e.Id, Name = e.Name, Where = e.Get("where", ""), Entry = e.Get("entry", ""), Says = e.Get("says", ""), Shape = e.Get("shape", "") };
                { double lo, hi; if (e.TryRange("coma-km", out lo, out hi)) { pl.ComaLo = lo; pl.ComaHi = hi; } if (e.TryRange("tail-km", out lo, out hi)) { pl.TailLo = lo; pl.TailHi = hi; } }   // (a comet's coma and tail at their real sizes)
                if (double.TryParse(e.Get("per-cubic-ly", "0"), fs, ci, out v)) pl.PerLy3 = v; if (double.TryParse(e.Get("feeding-share", "0"), fs, ci, out v)) pl.FeedShare = v; if (double.TryParse(e.Get("jets-share", "0"), fs, ci, out v)) pl.JetShare = v; if (double.TryParse(e.Get("size-km", "1000"), fs, ci, out v)) pl.SizeKm = v;
                { double lo, hi; if (e.TryRange("size-km", out lo, out hi)) { pl.SizeKm = lo; pl.SizeLo = lo; pl.SizeHi = hi; } if (double.TryParse(e.Get("size-power", "1"), fs, ci, out v)) pl.SizePow = Math.Max(0.1, v); if (double.TryParse(e.Get("round-above-km", "0"), fs, ci, out v)) pl.RoundKm = v; }   // (a size range: each body its own size between them, by the power; round-above-km: the size gravity has pulled a body round at)
                int n; if (int.TryParse(e.Get("shown", "0"), out n)) pl.Shown = n;
                { double lo, hi; if (e.TryRange("disc-rs", out lo, out hi)) { pl.DiscLo = Math.Max(1.5, lo); pl.DiscHi = Math.Max(pl.DiscLo + 1, hi); } if (e.TryRange("swarm-rs", out lo, out hi)) { pl.SwarmLo = Math.Max(2, lo); pl.SwarmHi = Math.Max(pl.SwarmLo + 1, hi); } }
                if (int.TryParse(e.Get("swarm-stars", "0"), out n)) pl.Swarm = n; { double lo, hi; if (e.TryRange("eating-stars", out lo, out hi)) { pl.EatLo = Math.Max(1, lo); pl.EatHi = Math.Max(pl.EatLo, hi); } } if (double.TryParse(e.Get("eating-share", "0"), fs, ci, out v)) pl.EatShare = v; if (double.TryParse(e.Get("inner-orbit-seconds", "8"), fs, ci, out v)) pl.OrbitSeconds = Math.Max(0.5, v);
                var c = e.Get("colour", "1 1 1").Split(' '); float r = 1, g = 1, b = 1; if (c.Length == 3) { float.TryParse(c[0], fs, ci, out r); float.TryParse(c[1], fs, ci, out g); float.TryParse(c[2], fs, ci, out b); }
                pl.R = r; pl.G = g; pl.B = b; _places.Add(pl);
            }
        }
        // a small body's size (km) from its place's range, by a power law on its position's seed: many small, few large (a belt is
        // mostly boulders, its largest a few hundred km across); a place with one size keeps it (a rock with the old spread round it)
        private static double SizeOf(Place pl, double x, double y, double z)
        {
            double su = Math.Abs(Math.Sin(x * 3.1 + y * 5.7 + z * 1.9) * 7919.1) % 1;
            if (pl.SizeHi > pl.SizeLo && pl.SizeLo > 0) return Math.Exp(Math.Log(pl.SizeLo) + Math.Pow(su, pl.SizePow) * (Math.Log(pl.SizeHi) - Math.Log(pl.SizeLo)));
            return pl.SizeKm * (pl.Shape == "rock" ? Math.Exp(-1.6 + 3.2 * Math.Pow(su, 2.5)) : 1);
        }
        private Extra MakeExtra(Place pl, double x, double y, double z)
        {
            var ex = new Extra { X = x, Y = y, Z = z, R = Units.Km(SizeOf(pl, x, y, z)), ShapeIx = (int)(Math.Abs(Math.Sin(x * 9.3 + y * 2.9 + z * 7.7) * 3313.7) % 1 * RockSeeds), Name = pl.Name, Shape = pl.Shape ?? "", Turn = (float)(Math.Abs(Math.Sin(x * 7.1 + z * 3.3) * 9187.3) % (Math.PI * 2)), Cr = pl.R, Cg = pl.G, Cb = pl.B, Lines = new List<string>(), Pl = pl };
            ex.OrbCx = _ax; ex.OrbCy = _ay; ex.OrbCz = _az; ex.OffX = x - _ax; ex.OffY = y - _ay; ex.OffZ = z - _az;   // (its place from the anchor; a body on an orbit gets its precise offset below)
            ex.Lines.Add(pl.Name);
            {
                // its id, as a star's: a code for its kind, then six digits from where it is (the same body, the same id, every time)
                string kid = pl.Id.Contains("comet") ? "CM" : pl.Id.Contains("asteroid") ? "AS" : pl.Id.Contains("neutron") || pl.Id.Contains("pulsar") ? "NS" : pl.Id.Contains("rogue") ? "RP" : pl.Id.Contains("hole") || pl.Id.Contains("black") ? "BH" : pl.Id.Contains("kuiper") ? "KO" : pl.Id.Contains("intergalactic") ? "IS" : "B";
                long hid = ((long)(x / Units.Km(1e6)) * 73856093L ^ (long)(y / Units.Km(1e6)) * 19349663L ^ (long)(z / Units.Km(1e6)) * 83492791L) & 0xFFFFFF;
                ex.Lines.Add("id: " + kid + "-" + hid.ToString("X6"));
            }
            if (pl.Says.Length > 0) ex.Lines.Add(Short(pl.Says, 70));                        // (a short line: the card is a card, not a page)
            if (pl.Shape == "comet")
            {
                // its coma and tail, each comet its own size in its kind's ranges (from where it is): real comets' comas a hundred
                // thousand km to over a million, their tails tens of millions of km
                double uc = Math.Abs(Math.Sin(x * 1.7 + y * 2.9 + z * 4.3) * 4321.7) % 1, ut = Math.Abs(Math.Sin(x * 3.3 + y * 1.1 + z * 2.7) * 8765.3) % 1;
                ex.ComaKm = pl.ComaLo > 0 ? pl.ComaLo + (pl.ComaHi - pl.ComaLo) * uc : 1e4; ex.TailKm = pl.TailLo > 0 ? pl.TailLo + (pl.TailHi - pl.TailLo) * ut : 3e5;
                ex.Lines.Add("coma about " + ex.ComaKm.ToString("N0") + " km across, tail about " + (ex.TailKm / 1e6).ToString("0.#") + " million km");
            }
            // what it is made of: an asteroid of the belt draws its kind (content/small-bodies.unt @asteroid) by the kinds' belt-share at
            // its place in the belt (stony inside, carbon-rich outside, metal and basalt rare, dark icy ones at the outer edge); any
            // other body is its entry's kind; each kind's mix of minerals, its make-up, its density (so its mass) and its albedo go on
            // the card, with the elements the minerals hold down to the traces, and its matter is dived into at its surface
            {
                ContentEntry made = null;
                if (pl.Where == "belt" && _system != null && _system.BeltOutAu > _system.BeltInAu)
                {
                    double rr = Math.Sqrt((x - _ax) * (x - _ax) + (y - _ay) * (y - _ay) + (z - _az) * (z - _az)) / Units.Au(1), t = Math.Max(0, Math.Min(1, (rr - _system.BeltInAu) / (_system.BeltOutAu - _system.BeltInAu)));
                    double u = Math.Abs(Math.Sin(x * 5.3 + y * 17.9 + z * 7.7) * 31415.9265) % 1, sum = 0; var cands = new List<KeyValuePair<ContentEntry, double>>();
                    foreach (var e in _content.OfKind("small-bodies.unt", "asteroid")) { double lo, hi; if (!e.TryRange("belt-share", out lo, out hi)) continue; double wgt = lo + (hi - lo) * t; if (wgt <= 0) continue; cands.Add(new KeyValuePair<ContentEntry, double>(e, wgt)); sum += wgt; }
                    double pick = u * sum; foreach (var c in cands) { pick -= c.Value; if (pick <= 0) { made = c.Key; break; } }
                    if (made == null && cands.Count > 0) made = cands[cands.Count - 1].Key;
                }
                if (made == null) { var en0 = pl.Entry.Split(' '); if (en0.Length == 2) foreach (var e in _content.Entries) if (e.Kind == en0[0] && e.Id == en0[1] && e.Props.ContainsKey("mix")) { made = e; break; } }
                if (made == null && pl.Shape == "rock")
                {
                    // a kind by the kinds' shares at mid-belt, from where the body is: a rock always has a kind, its minerals and a ground
                    double u2 = Math.Abs(Math.Sin(x * 3.7 + y * 11.3 + z * 5.9) * 27182.8) % 1, sum2 = 0; var c2 = new List<KeyValuePair<ContentEntry, double>>();
                    foreach (var e in _content.OfKind("small-bodies.unt", "asteroid")) { double lo, hi; if (!e.TryRange("belt-share", out lo, out hi) || !e.Props.ContainsKey("mix")) continue; double wgt = (lo + hi) / 2; if (wgt <= 0) continue; c2.Add(new KeyValuePair<ContentEntry, double>(e, wgt)); sum2 += wgt; }
                    double pick2 = u2 * sum2; foreach (var c in c2) { pick2 -= c.Value; if (pick2 <= 0) { made = c.Key; break; } }
                    if (made == null && c2.Count > 0) made = c2[c2.Count - 1].Key;
                }
                if (made != null) Compose(ex, made);
            }
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
                    ex.Lines.Add("mass: " + (ex.Mass > 0 ? ex.Mass.ToString(ex.Mass >= 1e4 ? "0.##E+0" : "0.#") : e.Get("mass-sol", "")) + " solar masses");
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
                    // a black hole is its mass: its event horizon the Schwarzschild radius 2GM/c^2 (2.95 km a solar mass), the shadow round it
                    // about 2.6 times that, the photon ring where light circles it
                    ex.Hole = true; double rs = 2.953 * ex.Mass; ex.R = Units.Km(rs * 2.6);
                    ex.Lines.Add("event horizon: " + ScaleHud.Words(rs * 2) + " across");
                    ex.Lines.Add("shadow: " + ScaleHud.Words(rs * 5.2) + " across");
                    // feeding or quiet, by its kind's shares (content/bodies.unt), from its place
                    double u2 = Math.Abs(Math.Sin(x * 3.7 + y * 91.1 + z * 13.3) * 24634.6345) % 1, u3 = Math.Abs(Math.Sin(x * 17.9 + y * 5.3 + z * 47.1) * 15731.743) % 1;
                    ex.Feeding = u2 < pl.FeedShare ? (u3 < pl.JetShare ? 2 : 1) : 0; ex.Pl = pl;
                    { double u4 = Math.Abs(Math.Sin(x * 7.3 + y * 29.7 + z * 61.1) * 9173.513) % 1, u5 = Math.Abs(Math.Sin(x * 11.3 + y * 3.7 + z * 41.9) * 6571.771) % 1; ex.Eating = ex.Feeding > 0 && u4 < pl.EatShare; ex.EatN = ex.Eating ? (int)Math.Round(pl.EatLo + (pl.EatHi - pl.EatLo) * u5) : 0; }   // (a star being torn apart and eaten now, by its kind's share, from its place; a supermassive one may have several in its disc at once)
                    ex.Tilt = (float)(u3 * Math.PI); ex.Turn = (float)(u2 * 37 % (Math.PI * 2));
                    {
                        // its spin, from its place (content/physics.unt @law frame-dragging): its horizon pulled in, its ergosphere, space
                        // swept round at its horizon, and how far in its disc reaches (the innermost stable orbit, by the spin)
                        double u6 = Math.Abs(Math.Sin(x * 19.3 + y * 7.7 + z * 23.9) * 4391.317) % 1; ex.Spin = _spinLo + u6 * (_spinHi - _spinLo);
                        double rsM = rs * 1000, M = rsM / 2, rp = M * (1 + Math.Sqrt(Math.Max(0, 1 - ex.Spin * ex.Spin))), omega = ex.Spin * Physics.C / (2 * rp);
                        ex.Lines.Add("spin " + ex.Spin.ToString("0.00") + " of the most; horizon " + ScaleHud.Words(rp * 2 / 1000) + " across; ergosphere " + ScaleHud.Words(rs * 2) + " at the equator");
                        ex.Lines.Add("frame dragging: space at the horizon round " + TurnsWords(omega / (2 * Math.PI)) + "; disc's inner edge " + IscoRs(ex.Spin).ToString("0.##") + " horizon radii");
                    }
                    ex.Lines.Add(ex.Feeding == 0 ? "quiet: not feeding, only its shadow and the light it bends" : ex.Feeding == 1 ? "feeding: a disc of gas spiralling in, heated to millions of degrees" : "feeding, and firing jets from its poles at nearly the speed of light");
                    if (ex.Eating) ex.Lines.Add(ex.EatN > 1 ? "stars being eaten now: " + ex.EatN + ", each stretched along its orbit into a stream of gas winding into the disc" : "a star being eaten now: stretched along its orbit into a stream of gas winding into the disc");
                    { double gm = Physics.GmSol * ex.Mass, rsM = rs * 1000; ex.Lines.Add("escape speed: light's at the horizon; the fall from there to the middle: " + Secs(2.0 / 3 * Math.Pow(rsM, 1.5) / Math.Sqrt(2 * gm)) + " of your own time"); }
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
            if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count && _tExtra != _extras[_tIndex]) _tExtra = _extras[_tIndex];   // (a fresh pick: its body held, before the list may change)
            bool inGalaxy = _near >= 0 && NearShare() > 0.5, between = !inGalaxy && (_near < 0 || NearShare() < 0.05);
            double lyU = 1.0 / Units.LightYearsPerUnit;
            string key = (inGalaxy ? "g" : between ? "b" : "-") + (_system != null ? "s" + _system.StarName : "") + ":" + (long)Math.Floor(_cx / 0.02) + "," + (long)Math.Floor(_cy / 0.02) + "," + (long)Math.Floor(_cz / 0.02) + ":" + _galaxies.Count;
            // every body on an orbit moves on it every frame (the list itself is remade only when you move to a new cell); the one
            // you are at moves you with it, so you stay beside it, or on its ground, as it goes round (at a minute a second an
            // asteroid at three AU would leave you in half a second)
            if (_tKind != 6 || _tExtra == null) _tSpot = null;
            // the one body the camera rides with: the body picked, else the body whose ground you are on (never both: were they two
            // objects for one body the camera would move twice its step a frame and run off it)
            var rider = _tExtra != null && _tExtra.Orbits && _extras.Contains(_tExtra) ? _tExtra : _groundExtra;
            foreach (var ex in _extras) { if (!double.IsNaN(ex.SpinHold) && ((ex != _tExtra && ex != _groundExtra) || _spaceScale <= 0)) ex.SpinHold = double.NaN; if (ex.Orbits) { Orbit(ex); if (ex == rider) { _ox += ex.OffX - ex.LOffX; _oy += ex.OffY - ex.LOffY; _oz += ex.OffZ - ex.LOffZ; } } }   // (a held spin let go once you are off the body)
            // the small body's ground has its centre where the body is NOW, after it has moved on its orbit and you with it: taken
            // once a frame before the move, the ground was drawn one orbital step behind the stone (at a minute a second tens of km,
            // and a different step every frame), so it flapped about by tens of km, the height above it jumped, and the zoom down to
            // it chased a point that was never there and froze
            if (_planet == -2 && _groundExtra != null) { _px = RelX(_groundExtra); _py = RelY(_groundExtra); _pz = RelZ(_groundExtra); }
            if (key == _extraKey) return; _extraKey = key; _extras.Clear();
            // the compact remnants of this galaxy's past supernovae: a neutron star (from a star of eight to about twenty solar masses) or a
            // stellar black hole (heavier) born at each one's middle, from the blast; the ones near you, picked like any body
            if (inGalaxy && _near >= 0 && _near < _galaxies.Count)
            {
                var g = _galaxies[_near]; var c = Supernovae(g, true); Place pN = null, pB = null;
                foreach (var pl in _places) { if (pl.Id == "neutron-star") pN = pl; if (pl.Id == "stellar-black-hole") pB = pl; }
                double reach = g.Scale * 0.15; int made = 0;
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (sn.K.Leaves == "nothing" || years < 0 || made >= 64) continue;   // (from the blast: it is born in the collapse, inside the fireball until the ejecta thin)
                    double dx = sn.Ux - _cx, dy = sn.Uy - _cy, dz = sn.Uz - _cz; if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
                    bool mag = sn.K.Leaves == "magnetar"; var pl = mag || sn.Mass < _snNsMax ? pN : pB; if (pl == null) continue;
                    var ex = MakeExtra(pl, sn.Ux, sn.Uy, sn.Uz); if (mag) { ex.Lines[0] = "A magnetar"; ex.Name = "A magnetar"; }
                    ex.Lines.Insert(2, "born in the " + sn.K.Name.ToLowerInvariant() + " here " + Years(years) + " ago, from a star of " + sn.Mass.ToString("0") + " solar masses" + (mag ? "; its magnetic field a thousand times a pulsar's" : "") + (mag || sn.Mass < _snNsMax ? "; spinning about " + Math.Min(700, 30 * Math.Sqrt(970 / Math.Max(1, years))).ToString("0") + " times a second (the youngest spin fastest, slowing as they age)" : "") + "; its shell now " + SnShellLy(sn, years).ToString("0.#") + " light-years out, moving at " + SnShellKmS(sn, years).ToString("N0") + " km/s");
                    _extras.Add(ex); made++;
                }
            }
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
                        // on its own orbit round the star (Kepler's law: its period from its distance and the star's mass), made at its
                        // closest point at the clock's zero, so the same body is the same body every time (its id, its size and its
                        // make-up from that place); the asteroids and Kuiper bodies on near-circles, the comets on long ellipses that
                        // sweep in close and far out (they had all stood still, placed once and never moved)
                        var ex = MakeExtra(pl, _ax + Math.Cos(ang) * rr * d, _ay + u * d, _az + Math.Sin(ang) * rr * d);
                        double ecc = pl.Shape == "comet" ? 0.5 + 0.45 * rng.NextDouble() : pl.Where == "kuiper" ? 0.3 * rng.NextDouble() : 0.15 * rng.NextDouble();
                        ex.Orbits = true; ex.OrbE = ecc; ex.OrbQ = d; ex.OrbCx = _ax; ex.OrbCy = _ay; ex.OrbCz = _az;
                        ex.OrbPeriod = Math.Sqrt(Math.Pow(au / (1 - ecc), 3) / Math.Max(0.05, _system.MassSol));
                        { double hx = Math.Cos(ang) * rr, hy = u, hz = Math.Sin(ang) * rr; ex.OrbE1 = new[] { hx, hy, hz }; double sx = -hz, sz = hx, sl = Math.Sqrt(sx * sx + sz * sz); if (sl < 1e-9) { sx = 1; sz = 0; sl = 1; } ex.OrbE2 = new[] { sx / sl, 0, sz / sl }; }   // (e1: from the star to its closest point; e2: along the orbit from there, level with the system)
                        ex.Lines.Add("its orbit: closest " + au.ToString("0.##") + " AU, farthest " + (au * (1 + ecc) / (1 - ecc)).ToString("0.##") + " AU, round the star in " + Years(ex.OrbPeriod));
                        Orbit(ex); ex.LX = ex.X; ex.LY = ex.Y; ex.LZ = ex.Z; ex.LOffX = ex.OffX; ex.LOffY = ex.OffY; ex.LOffZ = ex.OffZ; _extras.Add(ex);
                    }
                }
            }
            // the body picked, found again in the new list by its place (the same body is made at the same place every time); if it
            // is not among them (you have moved on), it is kept in the list itself, so the pick, its card and its dive stay its own
            if (_tKind == 6 && _tExtra != null)
            {
                int found = -1;
                for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (ex == _tExtra || (Math.Abs(ex.X - _tExtra.X) <= 1e-9 * Math.Max(1, Math.Abs(ex.X)) && Math.Abs(ex.Y - _tExtra.Y) <= 1e-9 * Math.Max(1, Math.Abs(ex.Y)) && Math.Abs(ex.Z - _tExtra.Z) <= 1e-9 * Math.Max(1, Math.Abs(ex.Z)) && ex.Hole == _tExtra.Hole)) { found = q; break; } }
                if (found < 0) { _extras.Add(_tExtra); found = _extras.Count - 1; }
                _tIndex = found; _tExtra = _extras[found];
                if (_groundExtra != null && !_extras.Contains(_groundExtra))
                {
                    // the body whose ground you are on, in the list made afresh: the same body (its name and make-up, the nearest of them),
                    // so its ground rides on with it; else the ground is let go by distance, as a planet's is
                    Extra gb = null; double gd = double.MaxValue; double gx = RelX(_groundExtra), gy = RelY(_groundExtra), gz = RelZ(_groundExtra);
                    foreach (var ex in _extras) { if (ex.Name != _groundExtra.Name || ex.Mix != _groundExtra.Mix) continue; double dx = RelX(ex) - gx, dy = RelY(ex) - gy, dz = RelZ(ex) - gz, d = dx * dx + dy * dy + dz * dz; if (d < gd) { gd = d; gb = ex; } }
                    if (gb != null) _groundExtra = gb;
                }
            }
        }

        // where a body on its orbit is now (Kepler's equation from its mean anomaly, as the planets), and where it was last frame;
        // with gravity switched off (content/physics.unt @law gravity-off) it carries straight on from where it was when it went off
        private void Orbit(Extra ex)
        {
            if (!ex.Orbits || ex.OrbPeriod <= 0) return;
            ex.LX = ex.X; ex.LY = ex.Y; ex.LZ = ex.Z; ex.LOffX = ex.OffX; ex.LOffY = ex.OffY; ex.LOffZ = ex.OffZ;
            double off = SpaceGen.GravityOffYear, t = _simYears, lx, ly, lz;
            if (!double.IsNaN(off) && t > off)
            {
                double x0, y0, z0, x1, y1, z1; OrbitAt(ex, off - 1e-4, out x0, out y0, out z0); OrbitAt(ex, off, out x1, out y1, out z1);
                lx = x1 + (x1 - x0) / 1e-4 * (t - off); ly = y1 + (y1 - y0) / 1e-4 * (t - off); lz = z1 + (z1 - z0) / 1e-4 * (t - off);
            }
            else OrbitAt(ex, t, out lx, out ly, out lz);
            ex.OffX = lx; ex.OffY = ly; ex.OffZ = lz; ex.X = ex.OrbCx + lx; ex.Y = ex.OrbCy + ly; ex.Z = ex.OrbCz + lz;   // (the precise offset kept beside the absolute place)
        }
        private static void OrbitAt(Extra ex, double t, out double x, out double y, out double z)
        {
            double e = ex.OrbE, M = (2 * Math.PI * t / ex.OrbPeriod) % (2 * Math.PI), E = M + e * Math.Sin(M);
            for (int it = 0; it < 12; it++) { double dE = (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E)); E -= dE; if (Math.Abs(dE) < 1e-12) break; }
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2)), a = ex.OrbQ / (1 - e), r = a * (1 - e * Math.Cos(E));
            double cn = Math.Cos(nu), sn = Math.Sin(nu);
            x = r * (cn * ex.OrbE1[0] + sn * ex.OrbE2[0]); y = r * (cn * ex.OrbE1[1] + sn * ex.OrbE2[1]); z = r * (cn * ex.OrbE1[2] + sn * ex.OrbE2[2]);   // (its offset from its star, precise)
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
        // the nearest thing drawn round you: the nearest galaxy, star, world or ground, and the bodies drawn close (asteroids, comets,
        // black holes' discs), so a shot's departure is paced from the one you were beside
        private double NearestDrawn()
        {
            double best = NearestDistance();
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 && !ex.Hole) continue;
                double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - ex.R * 1.6;
                if (d <= 0) d = ex.R * 0.01;
                if (d < best) best = d;
            }
            return best;
        }
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
        private double LocalCell = 25 / 50000.0, LocalReach = 60 / 50000.0;   // the cubes (25 light-years) and how far round you they are made (60), in galaxy units: content/universe.unt real-stars-cube-ly and real-stars-reach-ly
        private readonly Dictionary<long, StarVertex[]> _localCells = new Dictionary<long, StarVertex[]>();
        private readonly List<StarVertex> _localList = new List<StarVertex>();   // in the near galaxy's own frame
        private readonly List<int> _localKind = new List<int>(); private readonly List<long> _localId = new List<long>();
        private UploadBuffer _localBuf; private StarVertex[] _localDrawn = new StarVertex[0]; private int _localCount; private readonly HashSet<int> _localDead = new HashSet<int>();   // (the stars round you that died in supernovae: gone)
        private long _localKey = long.MinValue; private long _localCX, _localCY, _localCZ;
        private double _localOX, _localOY, _localOZ;   // the list's origin (the corner of the cube it was built round), galaxy frame: every star an offset from it, so the float positions are exact to a fraction of an AU and nothing snaps to a lattice

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
            _localKey = key; _localOX = cx * LocalCell; _localOY = cy * LocalCell; _localOZ = cz * LocalCell;
            _localList.Clear(); _localKind.Clear(); _localId.Clear(); _localVersion++;   // (remade: put on the card again)
            var keep = new Dictionary<long, StarVertex[]>();
            // every cube within the reach of where you are (not of your cube: the sky is as deep in stars on every side), the ones
            // already made kept, so moving a cube on makes only the new face
            int nr = (int)Math.Ceiling(LocalReach / LocalCell); double r2 = Math.Pow(LocalReach + LocalCell * 0.87, 2);
            for (long ox = -nr; ox <= nr; ox++) for (long oy = -nr; oy <= nr; oy++) for (long oz = -nr; oz <= nr; oz++)
            {
                long x = cx + ox, y = cy + oy, z = cz + oz, id = (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
                double mx = (x + 0.5) * LocalCell - lx, my = (y + 0.5) * LocalCell - ly, mz = (z + 0.5) * LocalCell - lz;
                if (mx * mx + my * my + mz * mz > r2 || _localList.Count > 600000) continue;
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
                        cell[k] = new StarVertex { X = (float)(rng.NextDouble() * LocalCell), Y = (float)(rng.NextDouble() * LocalCell), Z = (float)(rng.NextDouble() * LocalCell),   // (within its cube)
                            Size = (float)Units.Km(696000), R = kc.R, G = kc.G, B = kc.B, A = kind };      // A carries the kind's index
                    }
                }
                keep[id] = cell;
                float ox2 = (float)((x - cx) * LocalCell), oy2 = (float)((y - cy) * LocalCell), oz2 = (float)((z - cz) * LocalCell);   // its cube's corner from the origin
                for (int k = 0; k < cell.Length; k++) { var v = cell[k]; v.X += ox2; v.Y += oy2; v.Z += oz2; _localList.Add(v); _localKind.Add((int)cell[k].A); _localId.Add(id * 4096 + k); }
            }
            _localCells.Clear(); foreach (var kv in keep) _localCells[kv.Key] = kv.Value;
            _localCount = _localList.Count;
            // the stars that have died in supernovae (this galaxy's, back a hundred thousand years): each blast is at the star nearest
            // it here (within a few light-years), and that star is gone: not drawn, no system opens at it; what stands there is the
            // neutron star or black hole it left, among the bodies round you, its worlds gone with it
            _localDead.Clear();
            if (_near >= 0 && _near < _galaxies.Count && _localCount > 0)
            {
                var gq = _galaxies[_near]; var c = Supernovae(gq, true); double reach = 8 / (gq.Scale * Units.LightYearsPerUnit), near = LocalCell * 4;
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > _snRemnantYears) continue;
                    if (Math.Abs(sn.X - _localOX) > near || Math.Abs(sn.Y - _localOY) > near || Math.Abs(sn.Z - _localOZ) > near) continue;
                    int bi = -1; double bd = reach * reach;
                    for (int k = 0; k < _localCount; k++) { var s0 = _localList[k]; double dx = s0.X + _localOX - sn.X, dy = s0.Y + _localOY - sn.Y, dz = s0.Z + _localOZ - sn.Z, dd = dx * dx + dy * dy + dz * dz; if (dd < bd) { bd = dd; bi = k; } }
                    if (bi < 0) continue;
                    var sv = _localList[bi]; _localDead.Add(bi); sn.X = sv.X + _localOX; sn.Y = sv.Y + _localOY; sn.Z = sv.Z + _localOZ; ToUniverse(gq, sn.X, sn.Y, sn.Z, out sn.Ux, out sn.Uy, out sn.Uz);
                }
            }
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
            for (int k = 0; k < _localCount; k++) { var s0 = _localList[k]; double dx = s0.X + _localOX - lx, dy = s0.Y + _localOY - ly, dz = s0.Z + _localOZ - lz, d = dx * dx + dy * dy + dz * dz; if (d < bl) { bl = d; bestL = k; } }
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
            if (k >= 1000000) { var sl = _localList[k - 1000000]; ToUniverse(_galaxies[_near], sl.X + _localOX, sl.Y + _localOY, sl.Z + _localOZ, out x, out y, out z); return; }   // (a real star: its offset from the list's origin)
            var s = _nearLocal[k]; ToUniverse(_galaxies[_near], s.X, s.Y, s.Z, out x, out y, out z);
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
            if (_system == null && k >= 0 && d < SystemRange && !(k >= 1000000 && _localDead.Contains(k - 1000000)))   // (never at a star that died in a supernova: its remnant stands there, its worlds gone)
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
            // a small body you are at (an asteroid, a comet's nucleus, a Kuiper body, a rogue world) is a world like any: within forty
            // of its radii its ground is made by the same ground system a planet's is, with its own shape, size, spin, minerals and
            // elements, and everything from there down (the landing, the grains, a grain's face, the crystal, the atoms, the picks on
            // its surface, the way out) is the one code a world uses: nothing special for rocks
            var sb = _tKind == 6 && _tExtra != null && _tExtra.Shape.Length > 0 && !_tExtra.Hole && _tExtra.Mix != null && _tExtra.R > 0 ? _tExtra : _groundExtra;   // (or the body whose ground you are on: its ground lives by your distance from it, as a planet's does, picked or not; the tour's stand stop clears the pick while you stand on the ground, and the ground had gone with the pick, the camera left behind in empty space, every stop after it zooming into nothing)
            if (_system == null && sb == null) { DropPlanet(); return; }
            int best = -1; double bd = double.MaxValue;
            if (_system != null) for (int i = 0; i < _system.Bodies.Count; i++)
            {
                double x, y, z; BodyPos(i, out x, out y, out z);
                double d = Math.Sqrt((x - _ox) * (x - _ox) + (y - _oy) * (y - _oy) + (z - _oz) * (z - _oz)) / _system.Bodies[i].RadiusUnits;
                if (d < bd) { bd = d; best = i; }
            }
            if (sb != null) { double sx = RelX(sb) - _ox, sy = RelY(sb) - _oy, sz = RelZ(sb) - _oz, sd = Math.Sqrt(sx * sx + sy * sy + sz * sz) / sb.R; if (sd < bd) { bd = sd; best = -2; } }
            _cpBest = best; _cpBd = bd;
            if (bd > PlanetRange * 1.2) { DropPlanet(); return; }
            if ((best != _planet || (best == -2 && _groundExtra != sb)) && bd < PlanetRange)
            {
                DropPlanet();
                if (best == -2)
                {
                    _planet = -2; _groundExtra = sb; _spinRef = Spin(-2);
                    uint sbSeed = (uint)(sb.Lines.Count > 1 ? sb.Lines[1].GetHashCode() : sb.Name.GetHashCode());
                    _ground = new PlanetTerrain(_gpu.Device, "asteroid", sb.Name, sbSeed, false, sb.R / Units.Km(1)); _ground.MixId = sb.Mix;   // (its own radius, its own mix of minerals)
                    ZoomLog("GROUND made for " + sb.Name + " (" + ScaleHud.Words(sb.R / Units.Km(1) * 2) + " across, mix " + sb.Mix + "), as a world's");
                }
                else
                {
                var b = _system.Bodies[best];
                _planet = best;
                _spinRef = Spin(best);                                                     // (its day as it is now: the light where its star really is)
                _ground = new PlanetTerrain(_gpu.Device, b.Terrain ?? DeepView.TerrainKind(b.Kind), b.Kind, WorldSeed(best), false, b.RadiusUnits / Units.Km(1)) { Locked = b.Locked || (b.Parent < 0 && PlanetTerrain.TerrainKind(b.Terrain).Locked) };   // (a moon: its own regolith grains)
            }
                }
            if (_planet == -2 && _groundExtra != null) { _px = RelX(_groundExtra); _py = RelY(_groundExtra); _pz = RelZ(_groundExtra); }
            else if (_planet >= 0) BodyPos(_planet, out _px, out _py, out _pz);
        }

        // the camera rides along with the planet or moon you picked (or are at) as it goes round its star
        private void FollowBody()
        {
            int follow = _tKind == 2 && _system != null && _tIndex < _system.Bodies.Count ? _tIndex : _tKind == 6 ? -1 : _planet;   // (an asteroid, comet or black hole picked: they do not move, and riding with the planet you left shifted the camera off them every frame)
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
        private double _spinRef;   // the world's spin when you came to it: its ground is held still and its sky turns by how far it has spun since
        private double SpunSince { get { return _ground != null && (_planet >= 0 || _planet == -2) ? Spin(_planet) - _spinRef : 0; } }   // (a small body's world too: its day of a few hours)
        private void DropPlanet() { if (_ground != null) { _ground.Dispose(); _ground = null; } _planet = -1; _groundExtra = null; }
        // the ground body's name for a card (a world's kind, or a small body's own name)
        private string GroundKind() { return _planet == -2 && _groundExtra != null ? _groundExtra.Name.ToLowerInvariant() : _system != null && _planet >= 0 && _planet < _system.Bodies.Count ? _system.Bodies[_planet].Kind : "the ground"; }
        // whether the pick is the body whose ground is made: the picked world, or the picked small body
        private bool AtGroundBody() { return _ground != null && ((_tKind == 2 && _tIndex == _planet) || (_tKind == 6 && _planet == -2 && _tExtra != null && _tExtra == _groundExtra)); }

        // the top of what is under a direction, in km from the planet's radius: the sea's surface where there is sea and you are
        // above it; the sea bed once you are at the water (the dive: on down through it to the bed); else the real ground (a dry
        // basin below sea level is ground, not water)
        private bool _underSea;                                                           // at or under the sea's surface: the floor under you is the sea bed
        private double Surface(double x, double y, double z, double altKm = double.NaN)
        {
            double h = _ground.GroundAt(x, y, z, _surfaceCell);                           // the grains only as finely as your height needs
            if (h >= 0 || !_ground.HasWater) { if (!double.IsNaN(altKm)) _underSea = false; return h; }
            if (!double.IsNaN(altKm)) _underSea = altKm < 1e-3;                            // (within a metre of the water: on down through it)
            return _underSea ? h : 0;
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
            // under the sea the matter is the sea bed (the ocean mix: silt, sand, the sea floor's minerals), with the water above it
            _matter.Set(molten ? "melt" : sea ? "ocean" : _ground.MixId ?? _ground.Kind, false, ice && !sea, sea ? 0.2f : 0.58f, sea ? 0.4f : 0.5f, sea ? 0.6f : 0.4f, spot);
            // the planet's own grains, on the land's slope here: the same ones its ground is the top of
            {
                double ux = x / r, uy = y / r, uz = z / r, ex, ey, ez, nx, ny, nz; GrainField.Axes(ux, uy, uz, out ex, out ey, out ez, out nx, out ny, out nz);
                double d = 5e-4, R = _ground.Radius;
                Func<double, double, double, double> H = (a1, b1, c1) => { double l = Math.Sqrt(a1 * a1 + b1 * b1 + c1 * c1); return _ground.BaseAt(a1 / l, b1 / l, c1 / l); };
                double he = (H(ux + ex * d / R, uy + ey * d / R, uz + ez * d / R) - H(ux - ex * d / R, uy - ey * d / R, uz - ez * d / R)) / (2 * d);
                double hn = (H(ux + nx * d / R, uy + ny * d / R, uz + nz * d / R) - H(ux - nx * d / R, uy - ny * d / R, uz - nz * d / R)) / (2 * d);
                if (double.IsNaN(he) || Math.Abs(he) > 20) he = 0; if (double.IsNaN(hn) || Math.Abs(hn) > 20) hn = 0;
                var gm = _ground.GrainMix(uy);
                _matter.SetGround(R, ux, uy, uz, molten ? null : sea ? "ocean" : gm != null ? gm.Id : null, _ground.GrainSeed, he, hn);
                _matter.SetLife(_ground.HasWater && _ground.Traits.Life);   // microbes on a living world's grains
                string airHere;
                if (sea) _matter.SetAirAbove("water 100%", 3.3e22);                      // the sea above the bed: water molecules, packed as a liquid is (about 0.31 nm apart)
                else _matter.SetAirAbove(_ground.AirKm <= 0 || !PlanetTerrain.Air.TryGetValue(_ground.Kind, out airHere) ? null : airHere);   // its own air (a moon: none)
            }
        }

        // how far above the planet's ground the camera is (universe units)
        private double _surfaceCell, _lastAbove = 1;
        // a giant's layers (content/gas-giants.unt @layer: from-r, to-r, matter), and where its core begins
        private sealed class Layer { public string Id, Name, Matter, Says; public double From, To; public List<string> In; }   // In: the kinds of world that have it (none: every giant)
        private List<Layer> _layers;
        private List<Layer> Layers()
        {
            if (_layers != null) return _layers; _layers = new List<Layer>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("gas-giants.unt", "layer")) { double a, b; if (!double.TryParse(e.Get("from-r", ""), fs, ci, out a) || !double.TryParse(e.Get("to-r", ""), fs, ci, out b)) continue; _layers.Add(new Layer { Id = e.Id, Name = e.Name, Matter = e.Get("matter", "air"), Says = e.Get("says", ""), From = a, To = b, In = e.Props.ContainsKey("in") ? new List<string>(System.Linq.Enumerable.Select(e.Get("in", "").Split(','), x => x.Trim())) : null }); }
            return _layers;
        }
        private double CoreShare { get { foreach (var l in Layers()) if (l.Matter == "core") return l.To; return 0.15; } }
        // the layer of a giant you are in now (by your share of its radius from its middle); null above its clouds
        private Layer LayerHere()
        {
            if (_ground == null || _ground.Solid) return null; double x, y, z; CamFromPlanet(out x, out y, out z); double share = Math.Sqrt(x * x + y * y + z * z) / _ground.Radius;
            Layer best = null;
            foreach (var l in Layers())
            {
                if (share < l.From || share > l.To) continue;
                if (l.In != null && !l.In.Contains(_ground.Kind)) continue;              // only the layers its kind has (gas-giants.unt in:)
                if (best == null || l.To - l.From < best.To - best.From) best = l;
            }
            return best;
        }
        // where the ground hands you on into the matter of the grain under you: a third of a grain above it (about 30 micrometres
        // over sand), before the ground's ever finer grain tops would rise to meet the camera and hold it hovering
        private double HandoffKm()
        {
            if (_ground == null || !_ground.Solid) return 2e-9;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); var gm = r > 0 ? (_underSea ? GrainField.For("ocean") ?? _ground.GrainMix(y / r) : _ground.GrainMix(y / r)) : null;   // (under the sea: the bed's own grains)
            return Math.Max(gm != null && gm.Size > 0 ? Math.Max(2e-9, gm.Size * 0.3) : 2e-9, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 1.6 : 0);   // (and never later than the drawn pieces' own cell: their flat faces are the floor until then)
        }
        private double _agX = double.NaN, _agY, _agZ, _agCell, _agVal; private object _agGround;
        // for the log: the camera's height above the bare rock (the ground without its grains, at a millimetre cell), beside its height above the grains
        private string BaseHeightWords()
        {
            if (_ground == null) return "-";
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return "-";
            double h = r - _ground.Radius - (_ground.Solid ? _ground.GroundAt(x / r, y / r, z / r, 1e-6) : 0);
            return (h < 0 ? "-" : "") + ScaleHud.Words(Math.Abs(h));
        }
        private double AboveGround()
        {
            if (_ground == null) return double.MaxValue;
            _surfaceCell = Math.Max(1e-11, _lastAbove / Units.Km(1) / 30);                   // (from the height last asked: a thirtieth of it)
            double x, y, z; CamFromPlanet(out x, out y, out z);
            if (x == _agX && y == _agY && z == _agZ && _surfaceCell == _agCell && _agGround == _ground) return _agVal;   // (asked dozens of times a frame, and low down each answer is the ground worked out at a fine cell: once for a place)
            double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return 0;
            _lastAbove = Math.Max(1e-9, (r - _ground.Radius - (_ground.Solid ? Surface(x / r, y / r, z / r, r - _ground.Radius) : -_ground.Radius * (1 - CoreShare)))) * Units.Km(1);   // a giant: down through its clouds and layers to its core
            _agX = x; _agY = y; _agZ = z; _agCell = _surfaceCell; _agGround = _ground; _agVal = _lastAbove; return _lastAbove;
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
                double r = i == 0 ? StarRadiusNow : _system.Bodies[i - 1].RadiusUnits;
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

        // ── a tour: a flight of stops read from content/tours/<name>.unt, flown with the real controls (the wheel's zoom at one
        // steady rate, the drag's circling and looking, the same picks a click makes) so it shows the universe as it is ──
        private sealed class TourStop { public string Name, Do, Target, Caption; public double Rate, UntilKm, Turn, Tilt, Seconds, Pan, ToRadii, ToKm, Sweep, Rise, MaxRate, FarLo, FarHi; public bool Through, Out; }   // Out: a zoom out that ends once back out of the matter (a body's ground, a hole) into space   // Through: flown on through without slowing or stopping (into the next zoom); FarLo..FarHi: a far-galaxy pick's crossing, light-years
        private List<TourStop> _tour; private int _tourAt; private double _tourClock, _tourStall, _tourLastD, _tourT, _tourRate;
        /// <summary>Starts the tour of that name (content/tours/name.unt); false if there is none.</summary>
        public bool StartTour(string name)
        { _tourVisited = 0;
            var list = new List<TourStop>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            Func<string, double, double> num = (t, f) => { double v; return double.TryParse(t, fs, ci, out v) ? v : f; };
            foreach (var e in _content.OfKind("tours/" + name + ".unt", "stop"))
            {
                list.Add(new TourStop { Name = e.Id, Do = e.Get("do", "orbit"), Target = e.Get("target", "none"), Caption = e.Get("caption", ""), Rate = num(e.Get("rate", "0.3"), 0.3),
                    UntilKm = num(e.Get("until-km", "0"), 0), Turn = num(e.Get("turn", "6"), 6), Tilt = num(e.Get("tilt", "0"), 0), Seconds = num(e.Get("seconds", "10"), 10), Through = e.Get("through", "no") == "yes", Out = e.Get("out", "no") == "yes", Pan = num(e.Get("pan", "0"), 0),
                    ToRadii = num(e.Get("to-radii", "0"), 0), ToKm = num(e.Get("to-km", "0"), 0), Sweep = num(e.Get("sweep", "0"), 0), Rise = num(e.Get("rise", "0"), 0), MaxRate = num(e.Get("max-rate", "0.35"), 0.35) });
                { double lo, hi; if (e.TryRange("far-ly", out lo, out hi)) { list[list.Count - 1].FarLo = lo; list[list.Count - 1].FarHi = hi; } }   // (a far-galaxy pick: how far the crossing is, light-years)
            }
            if (list.Count == 0) return false;
            _tour = list; _tourName = name; _pausedTour = null; _pausedWhy = null; MenuChanged = true; _tourAt = 0; _tourT = 0; _tourRate = 0; _tourClock = 0; _shotRng = new Rng((ulong)Environment.TickCount ^ _seed); NewShot();
            if (TimeStep == 0) SetTime(1);                                              // time runs on the tour (the planets turn, the electrons move)
            return true;
        }
        public bool Touring { get { return _tour != null; } }
        /// <summary>Stops the tour; the controls are yours again.</summary>
        public void StopTour() { _tour = null; _pressed = false; _pausedTour = null; _pausedWhy = null; }
        // a tour you took the controls from is paused, not lost: it says so, and why, and carries on (T) or starts again (R)
        // from where it was; Backspace lets it go
        private string _tourName = "grand-tour"; public string TourName { get { return _tourName; } }
        private List<TourStop> _pausedTour; private int _pausedAt; private string _pausedWhy;
        public bool TourPaused { get { return _pausedTour != null; } }
        /// <summary>The tour ran to its end (it offers itself again).</summary>
        public bool TourFinished { get { return _pausedTour != null && _pausedWhy == null; } }
        private void PauseTour(string why) { _pausedTour = _tour; _pausedAt = _tourAt; _pausedWhy = why; _tour = null; _pressed = false; _tourRate = 0; _shotT = -1; MenuChanged = true; }
        /// <summary>Carries a paused tour on from the stop it was at (the shot under way taken again from where the camera is now).</summary>
        public void ResumeTour()
        {
            if (_pausedTour == null) return;
            _tour = _pausedTour; _tourAt = _pausedAt >= _pausedTour.Count ? 0 : _pausedAt; _tourT = 0; _tourClock = 0; _tourRate = 0; _shotT = -1; _pausedTour = null; _pausedWhy = null; NewShot();
            if (TimeStep == 0) SetTime(1); MenuChanged = true;
        }
        private static string KeyName(int k)
        {
            if ((k >= 0x30 && k <= 0x39) || (k >= 0x41 && k <= 0x5A)) return "the " + (char)k + " key";
            if (k >= 0x70 && k <= 0x87) return "F" + (k - 0x6F);
            switch (k) { case 0x20: return "Space"; case 0x0D: return "Enter"; case 0x1B: return "Esc"; case 0x08: return "Backspace"; case 0x09: return "Tab"; case 0x25: case 0x26: case 0x27: case 0x28: return "an arrow key"; case 0xBC: return "the , key"; case 0xBE: return "the . key"; }
            return "a key";
        }
        private double _tourH0, _tourLookFrom; private bool _tourWaiting, _pickFailed;   // (a pick that found nothing: the stops that depend on it are skipped, never shown with the wrong thing in view)
        // the director's choices for the shot under way, made afresh for each shot of each viewing (so no two tours are the same):
        // which way it circles, how fast (a little either side of the script's own), and a slow crane up or down as it goes;
        // the last galaxy's kind, so the next is another kind
        private Rng _shotRng = new Rng(1); private double _shotDir = 1, _shotMul = 1, _shotCrane; private string _lastGalaxyType = "";
        private double[] _shotE1, _shotE2, _shotUp, _shotLook0; private double _waitSpin, _shotTurnT = 2, _shotSweep;   // (_shotSweep: how far round the shot sweeps, radians, signed: the stop's sweep, or round to a small body's lit side)
        private double[] _shotP0; private double _shotR0;   // where the shot began (universe units) and the size of what was in view there: the departure is paced from it
        private float _localLf = -1; private int _localVersion, _localWritten = -1, _localWrittenN = -1; private double _shotD0, _shotD1, _shotEl0, _shotEl1, _shotLen, _shotT = -1, _shotELast, _shotAllowLog;   // the shot under way: its frame round the subject, its distances and heights, its length
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
                if (_ground != null && _matterScale <= 0 && _ground.Backlog > 80 && AboveGround() > Units.Km(MatterView.OverlayKm)) want *= Math.Max(0.6, 80.0 / _ground.Backlog);   // (below a centimetre the grains cover the ground: nothing is waited for, on down to the hand-over)
                if (WebNotMade) want = 0.05;                                            // out among the web before its light is made: wait for it
                if (_matterScale > 0 || _spaceScale > 0) want = 1;                     // in the matter nothing is waited for (the ground's building behind you never slows it)
                _tourPace += (want - _tourPace) * 0.05; return _tourPace;
            }
        }
        // the tour is looking round (up at the sky, round the view, gliding): the view is its own, never turned back to the pick
        private bool TourLooking { get { if (_tour == null || _tourAt >= _tour.Count) return false; var d = _tour[_tourAt].Do; return d == "shot" || (_ground != null && (d == "look" || d == "glide" || (d == "orbit" && _tKind != 2))); } }
        // the caption: the stop's own (a pick, over in a moment, shows the next stop's, so every caption stays on through its flight)
        // what is really up in the sky from where you stand (for a caption written as "{sky}"): the star only if it is above the
        // horizon and in view, the other worlds and this world's moons only if they are, the galaxy's stars by night, the day sky by day
        private string SkyWords()
        {
            if (_ground == null || _planet < 0 || _system == null) return "the sky";
            double cx, cy, cz; CamFromPlanet(out cx, out cy, out cz); double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz); if (cl <= 0) return "the sky";
            double ux = cx / cl, uy = cy / cl, uz = cz / cl;
            Func<double, double, double, bool> seen = (dx, dy, dz) => { double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0 || (dx * ux + dy * uy + dz * uz) / l < 0.02) return false; double sx, sy; return ToScreen(dx, dy, dz, out sx, out sy) && sx >= 0 && sx <= _lastW && sy >= 0 && sy <= _lastH; };
            bool star = seen(-_ox, -_oy, -_oz), day = (-_ox * ux - _oy * uy - _oz * uz) > 0;
            int worlds = 0, moons = 0;
            for (int i = 0; i < _system.Bodies.Count && i + 1 < _bodyCount; i++)
            {
                if (i == _planet) continue; var b = _system.Bodies[i];
                if (!seen(_bodyVerts[i + 1].X, _bodyVerts[i + 1].Y, _bodyVerts[i + 1].Z)) continue;   // (from the camera: the system has placed them)
                if (b.Parent == _planet) moons++; else if (b.Parent < 0) worlds++;
            }
            var parts = new List<string>();
            if (star) parts.Add("its star");
            if (moons > 0) parts.Add(moons == 1 ? "its moon" : "its " + moons + " moons");
            if (worlds > 0) parts.Add(worlds == 1 ? "another of its star's worlds" : worlds + " of its star's other worlds");
            parts.Add(day ? "the day sky, its star's light through the air" : "the galaxy's stars beyond");
            string w = string.Join(", ", parts.ToArray()); w = char.ToUpperInvariant(w[0]) + w.Substring(1);
            // the air, and what it does to the light: its make-up (the world's own, from its kind), the sky's colour (the air's own
            // colour, as the game draws it), and the star's colour from its surface heat, seen through that air or not at all
            string airHere = null; bool hasAir = _ground.AirKm > 0 && PlanetTerrain.Air.TryGetValue(_ground.Kind, out airHere) && !string.IsNullOrEmpty(airHere);
            if (!hasAir) airHere = null;
            string starCol = StarColourWords(_system.TempK), starKind = _system.StarName != null ? _system.StarName.ToLowerInvariant() : "its star";
            if (!hasAir) return w + ". No air here: the sky is black by day, the stars shine beside its star, and its light comes unchanged, " + starCol + " (a " + starKind + ", " + _system.TempK.ToString("N0") + " K)";
            string gases = AirGasesWords(airHere); string skyCol = ColourWords(_ground.AirR, _ground.AirG, _ground.AirB);
            bool clear = airHere.ToLowerInvariant().StartsWith("nitrogen") || airHere.ToLowerInvariant().StartsWith("oxygen");
            return w + ". The air, " + gases + ", " + (clear ? "scatters the blue of its star's light, so the sky is " + skyCol + " by day and reddens toward the horizon, where the light crosses the most air" : "tints the sky " + skyCol + " by day, its haze and dust scattering the light") + "; its star, a " + starKind + " at " + _system.TempK.ToString("N0") + " K, is " + starCol + ", and shows " + (clear ? "a little yellower" : "dimmer and redder") + " through the air than from space";
        }
        // the colour of a star from its surface heat (a black body: blue-white when hottest, red when coolest)
        private static string StarColourWords(double k) { return k >= 10000 ? "blue-white" : k >= 7500 ? "white" : k >= 6000 ? "yellow-white" : k >= 5000 ? "yellow-orange" : k >= 3700 ? "orange" : k >= 2500 ? "red" : "a dim deep red"; }
        // a colour in words, from its red, green and blue
        private static string ColourWords(float r, float g, float b)
        {
            float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)); if (mx <= 0.08f) return "nearly black";
            if (mx - mn < 0.08f) return mx > 0.7f ? "white" : "grey";
            if (b >= r && b >= g) return g > b * 0.8f ? "pale blue" : r > b * 0.6f ? "violet" : "blue";
            if (r >= g && r >= b) return g > r * 0.75f ? "yellow" : g > r * 0.45f ? "orange, butterscotch" : "red";
            return b > g * 0.7f ? "blue-green" : "green";
        }
        // an air's main gases in words, from its make-up line ("nitrogen 78%, oxygen 21%, ...": the first three, with their shares)
        private static string AirGasesWords(string air)
        {
            var names = new List<string>(); foreach (var bit in air.Split(',')) { string t = bit.Trim(); int pc = t.IndexOf('%'); if (pc < 0) { int sp = t.IndexOf("  ("); if (sp > 0) t = t.Substring(0, sp); if (t.Length > 0 && names.Count < 3) names.Add(t); continue; } int sp2 = t.LastIndexOf(' ', pc); string nm = sp2 > 0 ? t.Substring(0, sp2).Trim() : t; string share = sp2 > 0 ? t.Substring(sp2 + 1, pc - sp2) : ""; if (names.Count < 3) names.Add(nm + (share.Length > 0 ? " " + share : "")); }
            return names.Count == 0 ? "thin" : string.Join(", ", names.ToArray());
        }
        // a galaxy far across the universe (the stop's far-ly: a crossing of billions of light-years): a destination ahead of you,
        // inside the visible universe; the regions round it hold the same galaxies they always will (each from its own place), so
        // they are made now, whole, and held until you arrive. One whose heart is feeding first, and another kind than the last
        private long _heldRegion; private double _heldX, _heldY, _heldZ;
        private bool FarGalaxy(TourStop st)
        {
            var rng = new Rng(_seed ^ (ulong)(_tourAt * 7919 + Environment.TickCount)); var lk = Look; double R = _regionUnits;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                double d = (st.FarLo + (Math.Max(st.FarLo, st.FarHi) - st.FarLo) * rng.NextDouble()) / Units.LightYearsPerUnit;
                // ahead of you, within about 25 degrees of where you look
                double ax = lk.X + (rng.NextDouble() - 0.5) * 0.9, ay = lk.Y + (rng.NextDouble() - 0.5) * 0.9, az = lk.Z + (rng.NextDouble() - 0.5) * 0.9, al = Math.Sqrt(ax * ax + ay * ay + az * az);
                double px = _cx + ax / al * d, py = _cy + ay / al * d, pz = _cz + az / al * d;
                if (px * px + py * py + pz * pz > Math.Pow(_uRadius * 0.9, 2)) continue;    // (inside the visible universe, well in from its edge)
                long ci = (long)Math.Floor(px / R), cj = (long)Math.Floor(py / R), ck = (long)Math.Floor(pz / R);
                int best = -1; double bs = double.MinValue; var keys = new HashSet<long>();
                for (long i = ci - 1; i <= ci + 1; i++) for (long j = cj - 1; j <= cj + 1; j++) for (long k = ck - 1; k <= ck + 1; k++) { SpawnRegion(i, j, k); keys.Add(i * 73856093L ^ j * 19349663L ^ k * 83492791L); }   // (the regions' own galaxies, made if they are not yet)
                // the tour flies by a feeding hole's disc: a galaxy whose heart is feeding is required (a quiet one only if sixteen
                // tries round the aim find none, which with a third of hearts feeding they do not); every galaxy of the regions
                // counts, not only the ones made this moment
                for (int g = 0; g < _galaxies.Count; g++) { var gg = _galaxies[g]; if (!keys.Contains(gg.Region)) continue; if (!HeartFeeds(gg) && attempt < 15) continue; double score = rng.NextDouble() + (HeartFeeds(gg) ? 10 : 0) + (gg.Type != _lastGalaxyType ? 1 : 0); if (score > bs) { bs = score; best = g; } }
                if (best < 0) continue;
                var gb = _galaxies[best]; _tKind = 0; _tIndex = best; _tName = gb.Name; _lastGalaxyType = gb.Type; _heldRegion = gb.Region; _heldX = gb.X; _heldY = gb.Y; _heldZ = gb.Z;
                ZoomLog("far galaxy " + gb.Name + " at " + Units.Describe(Math.Sqrt((gb.X - _cx) * (gb.X - _cx) + (gb.Y - _cy) * (gb.Y - _cy) + (gb.Z - _cz) * (gb.Z - _cz))) + (HeartFeeds(gb) ? " (its heart feeding)" : ""));
                return true;
            }
            return false;
        }
        // whether a galaxy's central black hole is feeding: the same place-hash and share the hole itself is made with (MakeExtra, from
        // content/bodies.unt), so the tour can choose a galaxy with a feeding heart before it is made
        private bool HeartFeeds(Galaxy g)
        {
            foreach (var pl in _places) if (pl.Where == "galaxy-heart") { double u2 = Math.Abs(Math.Sin(g.X * 3.7 + g.Y * 91.1 + g.Z * 13.3) * 24634.6345) % 1; return u2 < pl.FeedShare; }
            return false;
        }
        // the black hole you picked, for a caption written as "{hole}": its jets if it fires them, its disc, or quiet; its horizon and its mass
        private string HoleWords()
        {
            if (_tKind != 6 || _tIndex < 0 || _tIndex >= _extras.Count || !_extras[_tIndex].Hole) return "A black hole";
            var ex = _extras[_tIndex]; string rs = ScaleHud.Words(Physics.HorizonKm(ex.Mass) * 2), mass = (ex.Mass >= 1e4 ? ex.Mass.ToString("0.#E+0") : ex.Mass.ToString("0.#")) + " solar masses";
            string eat = ex.Eating ? "; a star torn into a stream and eaten" : "";
            return ex.Feeding == 2 ? "Two jets from its poles, flung out at nearly the speed of light, reach far beyond the galaxy's heart: a horizon " + rs + " across, " + mass + eat
                 : ex.Feeding == 1 ? "Feeding, without jets: a disc of gas going down into a horizon " + rs + " across, " + mass + eat
                 : "Quiet: no disc, only its shadow and the light it bends; a horizon " + rs + " across, " + mass;
        }
        // your horizon, for a caption written as "{horizon}": how far you see each way, and why it ends there (the expansion of space, sped by dark energy)
        private string HorizonWords() { return "It ends only at your horizon, " + Units.Describe(_uRadius) + " each way, which moves with you: past it space stretches away faster than light can cross it, sped by dark energy (" + (_darkEnergy * 100).ToString("0") + "% of everything, " + _hubble.ToString("0") + " km/s a megaparsec)"; }
        // a stop that only means anything inside a body's matter (its crystal, lattice, atoms, electrons, nucleus, nucleons, quarks)
        private static bool MatterStop(string name)
        {
            if (name == null) return false; string n = name.ToLowerInvariant();
            foreach (var w in new[] { "crystal", "lattice", "atom", "electron", "shell", "bond", "nucleus", "proton", "quark", "let-go" }) if (n.Contains(w)) return true;
            return false;
        }
        private string TourCaption { get { int at = _tourAt; while (_tour != null && at < _tour.Count - 1 && _tour[at].Do == "pick") at++; return _tour != null && at < _tour.Count ? _tour[at].Caption.Replace("{sky}", SkyWords()).Replace("{hole}", HoleWords()).Replace("{horizon}", HorizonWords()).Replace("{supernova}", SupernovaWords()).Replace("{seen}", SeenWords()).Replace("{body}", BodyWords()) + (_tourWaiting ? "   (making what is ahead...)" : "") + "   (the view: " + ScaleHud.Words(ViewDistanceKm()) + ")" : ""; } }   // the scale is what the tour shows
        // one frame of the tour: what the stop asks, given to the controls; on to the next when it is done
        private void TourStep(Input input, double dt, int w, int h)
        {
            _tourClock += dt;
            if (_tourClock > 2)
            {
                // you take the controls: a click, the wheel or a key pauses it (it says so, and why); never in its first moments
                // (only on purpose: the click that brings the window back to the front, Shift, Ctrl, Alt and the Windows keys, and
                // shortcuts such as Alt+Tab or Win+Shift+S never stop it; nor do screenshots and recording)
                if (input.LeftClicked || input.RightClicked) { PauseTour("you clicked"); return; }   // (the click that brings the window to the front too: a click is a click, it stops the tour and picks as any other; it had picked nothing, and nothing seemed clickable)
                if (input.Wheel != 0 && !input.JustActivated) { PauseTour("you turned the mouse wheel"); input.Wheel = 0; return; }
                for (int k = 8; k < 256; k++) if (k != 0x78 && k != 0x7B && input.WasPressed(k) && input.Deliberate(k)) { PauseTour("you pressed " + KeyName(k)); return; }
            }
            if (_tourAt >= _tour.Count) { PauseTour(null); return; }   // (the end: it says so, and offers it again)
            var st = _tour[_tourAt]; _tourT += dt; bool done = false;
            if (_pickFailed && st.Do != "pick") { ZoomLog("TOUR stop " + st.Name + " skipped: its pick found nothing"); _tourAt++; _tourT = 0; NewShot(); return; }   // (never a shot of the wrong thing with the caption of the right one)
            if (MatterStop(st.Name) && _matterScale <= 0 && _spaceScale <= 0) { ZoomLog("TOUR stop " + st.Name + " skipped: not in any body's matter (the dive did not come)"); _tourAt++; _tourT = 0; NewShot(); return; }   // (a lattice's caption over open sky, a fly across six million km: never)
            input.MouseX = w / 2; input.MouseY = h / 2;                                    // the controls act at the middle of the view
            switch (st.Do)
            {
                case "zoom":
                    {
                        // one steady rate in scale (each tenfold the same time), eased in and out
                        double d = ViewDistanceKm(), want = st.UntilKm, sign = st.Rate >= 0 ? 1 : -1;
                        // a zoom out of a body's matter ends where the view pops out to space (its written until is a stand-in): that is
                        // its real end, and it is eased to, as every end is (it had run at full rate until the pop, and stopped dead)
                        if (st.Out && want >= 1e30) want = _spaceScale > 0 ? _spaceTop * 0.6 : _matterScale > 0 ? _matterTop * 0.6 : want;
                        double left = Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, want)));
                        // in a black hole the way is the fall's own measure (forty tenfolds of the way in, then the zoom): what is left
                        // of a climb out is the share of the way still to climb and the zoom back out, not the view's scale against the
                        // top (the scale sits just under the top the whole way up, so the climb had crawled at a sixth of its rate for
                        // minutes while the fall ran at full rate)
                        if (_spaceScale > 0 && _matter.SpaceKind == 12 && _fallHole != null) left = (sign < 0 ? Math.Max(0, _fallShare) : Math.Max(0, 1 - _fallShare)) * MatterView.FallTenfolds + Math.Abs(Math.Log10(Math.Max(1e-40, _spaceScale) / Math.Max(1e-40, sign < 0 ? _fallScale0 : d)));
                        // one movement, however many stops it is written as: a zoom followed by another the same way runs straight on
                        // (no easing out at the join, no fresh start after it); the last of them slows to a crawl over its final tenfold
                        // and a half, a twentieth of its rate at the end, and ends at that crawl, as every move does (it had dropped
                        // to an eighth and then stopped dead at its until)
                        var nx = _tourAt + 1 < _tour.Count ? _tour[_tourAt + 1] : null; bool chain = !st.Out && nx != null && nx.Do == "zoom" && (nx.Rate >= 0 ? 1 : -1) == sign;
                        double target = Math.Abs(st.Rate) * (st.Through || chain ? 1 : Math.Max(0.05, Math.Sqrt(Math.Min(1, left / 1.5)))) * TourPace;   // (never faster than the world is made)
                        double ramp = Math.Min(1, _tourT / 2.5); ramp = ramp * ramp * (3 - 2 * ramp);                     // brought up from rest over the first two and a half seconds (an exponential ease-in was fastest in its first frame: a jerk away from the last thing)
                        double aim = Math.Max(target * ramp, Math.Min(target, _tourRate));                                 // (a rate carried in from the stop before is kept, never dropped to ramp again)
                        _tourRate += (aim - _tourRate) * (1 - Math.Exp(-dt * 1.5));
                        double notches = _tourRate * Math.Log(10) / Math.Log(1 / 0.87) * dt;       // a wheel notch is 13% of the way
                        input.Wheel = OnCourse() * OverPick() * sign * notches;   // never moving one way while looking another; in the matter, never past what is picked (only once over it)
                        if (_spaceScale > 0 && _matter.SpaceKind == 12)
                        {
                            // the fall, on the tour: looking up at the sky through the mouth as you enter (from the first frame, no easing
                            // into it), held for the first seconds, then turned down along the funnel to its absolute point, and held
                            // there for the rest of the fall, the walls closing in round the view and meeting at it; a slow wheel, so
                            // the grid turns past; the fall itself about a minute
                            double tf = Math.Max(0, Math.Min(1, _fallShare));
                            var ax = _fallAxis; var e1 = Vec3.Cross(ax, Math.Abs(ax.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(ax, e1).Normalized;
                            double cyc = _tourT, thAim = sign < 0 ? 0.2 : cyc < 6 ? 0.2 : Math.PI - 0.08;   // (0: up, the mouth; pi: down, the point; climbing out: up at the mouth the whole way, never down at the point)
                            if (_fallTh < 0)
                            {
                                // from the way you were looking as you came in (at the hole: down its axis), the turn's speed brought up from
                                // rest over its first second and a half: the look swings up to the mouth, never snaps to it
                                var l0 = Look; _fallTh = Math.Acos(Math.Max(-1, Math.Min(1, -(l0.X * ax.X + l0.Y * ax.Y + l0.Z * ax.Z))));
                                _fallPh0 = Math.Atan2(l0.X * e2.X + l0.Y * e2.Y + l0.Z * e2.Z, l0.X * e1.X + l0.Y * e1.Y + l0.Z * e1.Z); _fallTurnT = 0;
                            }
                            _fallTurnT += dt; double fr = Math.Min(1, _fallTurnT / 1.5);
                            _fallTh += (thAim - _fallTh) * (1 - Math.Exp(-dt * (cyc < 6 || sign < 0 ? 1.1 : 0.6) * fr));   // (up to the mouth in the first seconds, then down the funnel, and held; up again for the climb out)
                            double th = Math.Max(0.06, Math.Min(Math.PI - 0.06, _fallTh)), ph = _fallPh0 + _tourT * 0.12;
                            var side = e1 * (float)Math.Cos(ph) + e2 * (float)Math.Sin(ph);
                            var lk2 = (ax * (float)(-Math.Cos(th)) + side * (float)Math.Sin(th)).Normalized; _fx = lk2.X; _fy = lk2.Y; _fz = lk2.Z;
                        }
                        else if (st.Pan != 0 && (_tKind >= 0 || _matterScale > 0 || _spaceScale > 0)) TourTurn(st.Pan * Math.PI / 180 * dt * Math.Min(1, _tourT / 2), 0);   // circling what it flies to as it goes (in the matter too, where the eye goes round the point you dive into)
                        // going nowhere (nothing nearer to fly at) for six seconds: on to the next stop
                        if (Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, _tourLastD))) > 0.05 || (_spaceScale > 0 && _matter.SpaceKind == 12 && _fallShare < 1) || (_ground != null && _ground.Backlog > 0 && AboveGround() > Units.Km(MatterView.OverlayKm)) || WebNotMade) { _tourLastD = d; _tourStall = 0; } else _tourStall += dt;   // (below the overlay the ground's queue means nothing: the grains cover it)   // (held back by ground still being built, or the web's light: waiting, never given up on)
                        if ((sign > 0 && d <= want * 1.02) || (sign < 0 && !st.Out && d >= want * 0.98) || _tourT > 240 || (_tourStall > 20 && _tourT > 20) || (st.Out && _tourT > 0.5 && _spaceScale <= 0 && _matterScale <= 0)) { done = true; if (!chain) _tourRate = 0; input.Wheel = 0; _tourStall = 0; _tourLastD = 0; }
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
                            var up = ShotUp(); double ux = up.X, uy = up.Y, uz = up.Z;
                            double cx = -tdx / tl, cy = -tdy / tl, cz = -tdz / tl, ce = cx * ux + cy * uy + cz * uz;
                            double hx = cx - ux * ce, hy = cy - uy * ce, hz = cz - uz * ce, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                            if (hl < 1e-6) { hx = Math.Abs(ux) < 0.9 ? 1 : 0; hy = 0; hz = Math.Abs(ux) < 0.9 ? 0 : 1; double d0 = hx * ux + hz * uz; hx -= ux * d0; hy -= uy * d0; hz -= uz * d0; hl = Math.Sqrt(hx * hx + hy * hy + hz * hz); }
                            _shotE1 = new[] { hx / hl, hy / hl, hz / hl }; _shotUp = new[] { ux, uy, uz };
                            _shotE2 = new[] { uy * _shotE1[2] - uz * _shotE1[1], uz * _shotE1[0] - ux * _shotE1[2], ux * _shotE1[1] - uy * _shotE1[0] };
                            _shotSweep = st.Sweep * _shotDir * Math.PI / 180;
                            if (_tKind == 6 && _system != null && _tIndex >= 0 && _tIndex < _extras.Count && !_extras[_tIndex].Hole)
                            {
                                // a small body lit by its star: the shot sweeps round from where the camera is to the body's lit side (the
                                // short way round), so it comes to rest with the star behind the camera, never on a dark rock against the stars
                                double sx = _ax - _tx, sy = _ay - _ty, sz = _az - _tz, se = sx * ux + sy * uy + sz * uz; sx -= ux * se; sy -= uy * se; sz -= uz * se; double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                                if (sl > 1e-9) _shotSweep = Math.Atan2((sx * _shotE2[0] + sy * _shotE2[1] + sz * _shotE2[2]) / sl, (sx * _shotE1[0] + sy * _shotE1[1] + sz * _shotE1[2]) / sl);
                            }
                            _shotD0 = tl; _shotEl0 = Math.Asin(Math.Max(-1, Math.Min(1, ce)));
                            _shotD1 = st.ToKm > 0 ? Units.Km(st.ToKm) : st.ToRadii > 0 && trad > 0 ? trad * st.ToRadii : tl;
                            _shotEl1 = Math.Max(-1.3, Math.Min(1.3, _shotEl0 + st.Rise * Math.PI / 180));
                            double decades = Math.Abs(Math.Log10(_shotD1 / _shotD0));
                            _shotLen = Math.Max(st.Seconds, decades / Math.Max(0.05, st.MaxRate) + 2);
                            _shotT = 0; _shotLook0 = new[] { _fx, _fy, _fz }; _shotAllowLog = 0;
                            // the departure is paced from what was in view where the shot began: the nearest thing drawn (the asteroid or comet
                            // you were beside, a world's ground, a star), never the distance to the new subject (its pick is made before the
                            // shot starts, so that was an AU wide while the asteroid filled the view, and the asteroid was gone in a frame)
                            // (the start kept in the subject's own frame: a world moves on its orbit as the shot runs, and a start fixed in space drifted
                            // out of the pace's allowance, so the camera snapped back to it on the frames the glide crossed the allowance; and the pace
                            // from what is in view, never from a thing drawn a few hundred km off that the view never held)
                            _shotP0 = new[] { -tdx, -tdy, -tdz }; _shotR0 = Math.Max(Math.Min(Units.Km(ViewDistanceKm()), Math.Max(NearestDrawn(), Units.Km(ViewDistanceKm()) * 0.05)), Units.Km(1e-6)); _shotELast = 0;
                            // a turn takes as long as it needs at no more than about 20 degrees a second (eased in and out), and the shot
                            // is long enough to hold it: a pick far behind you is turned to calmly, never whipped round to
                            double turnDeg = Math.Acos(Math.Max(-1, Math.Min(1, -(_fx * tdx + _fy * tdy + _fz * tdz) / tl * -1))) * 180 / Math.PI;
                            _shotTurnT = Math.Max(1.5, turnDeg / 20 * 1.5); _shotLen = Math.Max(_shotLen, _shotTurnT + 3);   // (where it looks as the shot begins: it turns from there to the subject, never snapping)
                        }
                        if (!WebNotMade) _shotT += dt;                                          // (held while the web's light is made)
                        // the pan before the travel: the camera holds where it is while the look turns to the new subject (seven tenths of
                        // the turn's time), and only then sets off, so it is never pulled away from the last thing while still facing it
                        double hold = _shotLook0 != null ? Math.Max(1.5, _shotTurnT) * 0.7 : 0;
                        double u = Math.Min(1, Math.Max(0, _shotT - hold) / Math.Max(0.1, _shotLen - hold)), e = u * u * (3 - 2 * u);
                        // where the shot has the camera at a point e of its glide: the way from the subject and the distance
                        Func<double, double[]> frame = ee =>
                        {
                            double Dd = Math.Exp(Math.Log(_shotD0) + (Math.Log(_shotD1) - Math.Log(_shotD0)) * ee);
                            double az2 = _shotSweep * ee, el2 = _shotEl0 + (_shotEl1 - _shotEl0) * ee, ca2 = Math.Cos(az2), sa2 = Math.Sin(az2), ce3 = Math.Cos(el2), se3 = Math.Sin(el2);
                            return new[] { ce3 * (ca2 * _shotE1[0] + sa2 * _shotE2[0]) + se3 * _shotUp[0], ce3 * (ca2 * _shotE1[1] + sa2 * _shotE2[1]) + se3 * _shotUp[1], ce3 * (ca2 * _shotE1[2] + sa2 * _shotE2[2]) + se3 * _shotUp[2], Dd };
                        };
                        Func<double[], double> fromStart = f => { double px = f[0] * f[3] - _shotP0[0], py = f[1] * f[3] - _shotP0[1], pz = f[2] * f[3] - _shotP0[2]; return Math.Sqrt(px * px + py * py + pz * pz); };   // (from the start, in the subject's frame: zero at the start itself, growing along the glide)
                        // the departure paced from what was in view where the shot began: the camera is never farther from there than
                        // its pace allows (max-rate tenfolds a second, from that size), so an asteroid you were beside pulls away and
                        // shrinks smoothly instead of being left in one frame on the way to a comet across the system; once the
                        // shot's own glide is within that, it goes on as written, and it is not over until the camera has arrived
                        // the pace itself eased: brought up from rest over two and a half seconds once the pan is done, and slowed to a crawl
                        // over the last tenfold and a half of the way, a twentieth of its rate at the end (it had run at the stop's full rate
                        // from the first frame and stopped dead on arrival, so every long crossing jerked away and slammed to a halt: the
                        // black hole in one frame, another system in the next)
                        {
                            double tm = Math.Max(0, _shotT - hold), rampIn = Math.Min(1, tm / 2.5); rampIn = rampIn * rampIn * (3 - 2 * rampIn);
                            double endFrom = _shotP0 != null ? fromStart(frame(1)) : 0, allowNow = _shotR0 * Math.Pow(10, _shotAllowLog);
                            double leftDec = endFrom > 0 ? Math.Log10(Math.Max(1e-300, endFrom) / Math.Max(1e-300, allowNow)) : 0;
                            double easeOut = Math.Max(0.05, Math.Min(1, Math.Sqrt(Math.Max(0, leftDec) / 1.5)));
                            if (!WebNotMade && tm > 0) _shotAllowLog += Math.Max(0.05, st.MaxRate) * rampIn * easeOut * dt;
                        }
                        double allow = _shotP0 != null ? _shotR0 * Math.Pow(10, _shotAllowLog) : double.MaxValue, eUse = e;
                        var fr0 = frame(e);
                        if (_shotP0 != null && fromStart(fr0) > allow)
                        {
                            double lo = 0, hi = e;
                            for (int it = 0; it < 28; it++) { double mid = (lo + hi) * 0.5; if (fromStart(frame(mid)) > allow) hi = mid; else lo = mid; }
                            eUse = Math.Max(lo, _shotELast); fr0 = frame(eUse);                  // (never back along the glide: a shot only goes forward)
                        }
                        _shotELast = eUse;
                        double vx = fr0[0], vy = fr0[1], vz = fr0[2], D = fr0[3];
                        // the camera to where the shot has it now (the subject where it is now: worlds move on their orbits)
                        _ox += tdx + vx * D; _oy += tdy + vy * D; _oz += tdz + vz * D;
                        // the look glides from where it was to the subject over the shot's first part (eased both ends), never a snap
                        double lb = Math.Min(1, _shotT / Math.Max(1.5, _shotTurnT)); lb = lb * lb * (3 - 2 * lb);
                        if (_shotLook0 != null && lb < 1)
                        {
                            double ax = _shotLook0[0], ay = _shotLook0[1], az0 = _shotLook0[2], bx = -vx, by = -vy, bz = -vz;
                            if (ax * bx + ay * by + az0 * bz < -0.9) { ax += _shotUp[0] * 0.3; ay += _shotUp[1] * 0.3; az0 += _shotUp[2] * 0.3; }   // (turning right round: by way of up, never through nothing)
                            SetLook(ax + (bx - ax) * lb, ay + (by - ay) * lb, az0 + (bz - az0) * lb);
                        }
                        else SetLook(-vx, -vy, -vz);
                        if (u >= 1 && eUse >= e - 1e-9) { done = true; _shotT = -1; }     // (arrived: the glide done, and the departure's pace no longer holding it back)
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
                        if (!there) input.Wheel = OnCourse() * OverPick() * sign * Math.Abs(st.Rate) * ease * TourPace * Math.Log(10) / Math.Log(1 / 0.87) * dt;   // only moving where it looks
                        { double lp = LocalPitch(); if (Math.Abs(lp) > 0.9) TourPitchToward(Math.Sign(lp) * 0.9, 1 - Math.Exp(-dt * 0.8)); }
                        TourTurn(st.Turn * _shotDir * _shotMul * Math.PI / 180 * dt * Math.Max(ease, 0.4 * Math.Min(1, _tourT / 3)), (_ground == null && _matterScale <= 0 ? _shotCrane : 0) * Math.PI / 180 * dt * ease);
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
                        int k0 = _tKind, i0 = _tIndex, p0 = _matter.PickCount; double x0 = _tx; TourPick(st.Target, st);
                        bool got = st.Target == "none" || (st.Target == "planet" && _tKind == 2 && _system != null) || (st.Target == "land" && _tKind == 3) || (_tKind >= 0 && (_tKind != k0 || _tIndex != i0 || _tx != x0)) || _matter.PickCount != p0;
                        // while it looks (what it wants still being made round you, or none there at all) it never sits still: it looks
                        // round slowly, eased in, and goes on after a few seconds at most
                        if (!got && _ground == null) TourTurn(4 * _shotDir * Math.PI / 180 * dt * Math.Min(1, _tourT / 1.5), 0);   // (out in space only: by a world, among its stones and grains, it holds still)
                        bool matterPick = st.Target == "grain" || st.Target == "spot" || st.Target == "atom" || st.Target == "bonded-atom" || st.Target == "electron" || st.Target == "nucleus" || st.Target == "proton" || st.Target == "quark";
                        if (got || _tourT > (matterPick ? 0.6 : st.Target == "asteroid" || st.Target == "comet" ? 2 : 2.5))   // (a pick that has not come in a couple of seconds is not coming: no five-second waits; a matter pick is answered at once from what is drawn, so one that is not there (a bond in a gas, an electron in a plasma) is given up in under a second)
                        {
                            done = true; _pickFailed = !got && st.Target != "none"; if (got) _tourVisited++;
                            ZoomLog("TOUR pick " + st.Target + (got ? ": " + _tName + " (kind " + _tKind + ")" : " FAILED after " + _tourT.ToString("0.0") + " s: " + _extras.Count + " bodies round you, " + _localCount + " stars, system " + (_system != null ? "open" : "none") + ", galaxy " + _near + "; its stops are skipped"));
                        }
                    }
                    break;
                default: done = true; break;
            }
            // no pauses: each stop goes straight on into the next (its caption stays on through the flight); the looking round is
            // done by the circling stops at the points that matter
            if (done && WebNotMade) { done = false; input.Wheel = 0; }               // (never on past the web before it shows)
            // held back while what is ahead is still being made (the web's light, the ground): never still, so it never looks frozen;
            // it keeps circling what it shows, and the caption says what it is waiting for
            _tourWaiting = WebNotMade || (st.Do == "zoom" && _ground != null && _matterScale <= 0 && _ground.Backlog > 80 && AboveGround() > Units.Km(MatterView.OverlayKm));   // (in the matter the ground is not being made: its old count means nothing)
            _waitSpin += ((WebNotMade ? 1 : 0) - _waitSpin) * (1 - Math.Exp(-dt * 0.8));   // (eased in and out, never a sudden start or stop)
            if (_waitSpin > 0.002) TourTurn(6 * _waitSpin * Math.PI / 180 * dt, 0);                   // (out among the galaxies only: by a world it never circles while it waits)
            if (done) { _tourAt++; _tourT = 0; NewShot(); if (_tourAt < _tour.Count) ZoomLog("TOUR stop " + _tour[_tourAt].Name + " (" + _tour[_tourAt].Do + ") begins at " + Units.Describe(Units.Km(ViewDistanceKm())) + " pick " + _tKind + ":" + _tIndex + " " + _tName + (_matterScale > 0 ? " matter " + _matter.Card()[0] + " pick-off " + _matter.PickOff.ToString("0.00") : "")); }
        }
        // what a stop picks, as a click would
        private void TourPick(string what, TourStop st = null)
        {
            switch (what)
            {
                case "none": _tKind = -1; _tName = ""; _matter.Unpick(); break;
                case "black-hole":
                case "asteroid":
                case "comet":
                    {
                        // one of the bodies round you (content/bodies.unt): the nearest black hole, asteroid of the belt, or comet of
                        // the Oort cloud
                        int bi = -1; double bd = double.MaxValue, nearestHole = double.MaxValue;
                        // a black hole: this galaxy's own heart (or one within a few galaxy-widths), a feeding one before a quiet one; never a
                        // feeding heart ten million light-years off outscoring the quiet one you are at (the shot would leave the galaxy
                        // with the caption saying it was going down to its heart)
                        double holeReach = what == "black-hole" && _near >= 0 && _near < _galaxies.Count ? _galaxies[_near].Scale * 4 : double.MaxValue;
                        if (what == "black-hole" && _near >= 0 && _near < _galaxies.Count)
                        {
                            // this galaxy's own heart, first and surest: every galaxy has one at its middle; if the bodies round you do not
                            // hold it yet (they are made again as you move, and a far galaxy's may lag a frame), it is made here from its
                            // place, the same one the galaxy would get
                            var g = _galaxies[_near]; double tol = g.Scale * 0.02;
                            for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (!ex.Hole) continue; double hx = ex.X - g.X, hy = ex.Y - g.Y, hz = ex.Z - g.Z; if (hx * hx + hy * hy + hz * hz < tol * tol) { bi = q; break; } }
                            if (bi < 0 && _places != null) foreach (var pl in _places) if (pl.Where == "galaxy-heart") { _extras.Add(MakeExtra(pl, g.X, g.Y, g.Z)); bi = _extras.Count - 1; ZoomLog("TOUR the heart of " + g.Name + " made for the pick (it was not among the " + (_extras.Count - 1) + " bodies round you)"); break; }
                            if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; break; }
                        }
                        for (int q = 0; q < _extras.Count; q++)
                        {
                            var ex = _extras[q]; string nm = ex.Name.ToLowerInvariant();
                            bool fits = what == "black-hole" ? ex.Hole : what == "asteroid" ? ex.Shape == "rock" : ex.Shape == "comet" || nm.Contains("comet");   // (an asteroid: any rocky body here, of a belt, the Kuiper belt or the Oort cloud, as the system has)
                            if (!fits) continue;
                            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = dx * dx + dy * dy + dz * dz;
                            if (what == "black-hole" && d > holeReach * holeReach) { if (d < bd * 1e12) nearestHole = Math.Min(nearestHole, Math.Sqrt(d)); continue; }
                            if (what == "black-hole" && ex.Feeding > 0) d *= 1e-6;
                            if (what == "asteroid" && nm.Contains("asteroid")) d *= 1e-4;          // (the belt's own, before a far icy body)             // (a feeding one, its disc glowing, before a quiet one)
                            if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; }
                        else if (what == "black-hole") ZoomLog("TOUR no black hole within " + Units.Describe(holeReach) + " (the nearest " + (nearestHole < double.MaxValue ? Units.Describe(nearestHole) : "none") + " away; galaxy " + _near + " scale " + (_near >= 0 && _near < _galaxies.Count ? Units.Describe(_galaxies[_near].Scale) : "-") + ")");
                    }
                    break;
                case "supernova":
                    { var sn = NearestSupernova() ?? YoungRemnant(); if (sn != null) PickSupernova(sn); else ZoomLog("TOUR no supernova, going or young, in the " + _galaxies.Count + " galaxies made round you"); }
                    break;
                case "giant":
                    if (_system != null)
                    {
                        // a giant of this star's worlds (gas or ice), by chance among them, weighted as the planets are
                        var rng = new Rng(_seed ^ (ulong)(Environment.TickCount + 47)); var opts = new List<int>();
                        for (int i = 0; i < _system.Bodies.Count; i++) { var b = _system.Bodies[i]; if (b.Parent < 0 && b.Giant) opts.Add(i); }
                        double total = 0; var wts = new double[opts.Count];
                        for (int q = 0; q < opts.Count; q++) { wts[q] = Math.Max(0.01, _system.Bodies[opts[q]].Interest); total += wts[q]; }
                        double pickAt = rng.NextDouble() * total; int pick = opts.Count > 0 ? opts[opts.Count - 1] : -1;
                        for (int q = 0; q < opts.Count; q++) { pickAt -= wts[q]; if (pickAt <= 0) { pick = opts[q]; break; } }
                        if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; } else ZoomLog("TOUR no giant among the " + _system.Bodies.Count + " bodies of this system");
                    }
                    break;
                case "neutron":
                    {
                        // the nearest neutron star, pulsar or magnetar among the bodies round you (placed among the stars, or left at a
                        // supernova's middle): a body like any other, picked and dived into
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < _extras.Count; q++)
                        {
                            var ex = _extras[q]; if (ex.Pl == null) continue; string id = ex.Pl.Id;
                            if (!(id.Contains("neutron") || id.Contains("pulsar") || id.Contains("magnetar"))) continue;
                            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; }
                        else ZoomLog("TOUR no neutron star among the " + _extras.Count + " bodies round you");
                    }
                    break;
                case "nebula":
                    if (_near >= 0 && _near < _galaxies.Count && _features != null)
                    {
                        // the nearest nebula of the galaxy you are in (emission, reflection, planetary, a supernova's remnant or a dark one)
                        var g = _galaxies[_near]; string[] names = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < _features.Count; q++)
                        {
                            var fq = _features[q]; int fk = (int)fq[4]; if (fk < 2 || fk > 6) continue;
                            double ux, uy, uz; ToUniverse(g, fq[0], fq[1], fq[2], out ux, out uy, out uz);
                            double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { double ux, uy, uz; ToUniverse(g, _features[bi][0], _features[bi][1], _features[bi][2], out ux, out uy, out uz); _tGal = _near; _tKind = 5; _tIndex = bi; _tx = ux; _ty = uy; _tz = uz; _tRadius = _features[bi][3] * g.Scale; int fk = (int)_features[bi][4]; _tName = fk >= 0 && fk < names.Length ? names[fk] : "a nebula"; }
                        else ZoomLog("TOUR no nebula among the " + _features.Count + " features of " + g.Name);
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
                    if (st != null && st.FarLo > 0 && FarGalaxy(st)) break;                // (a true crossing: billions of light-years, to a galaxy made ahead from its own region)
                    {
                        // across the universe: the farthest galaxy made round you that lies ahead (in view), never a near one
                        int bi = -1; double bs = double.MinValue; var lk = Look; var rng = new Rng(_seed ^ (ulong)Environment.TickCount);
                        for (int i = 0; i < _galaxies.Count; i++)
                        {
                            var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (d < 3 || d > _nearUnits * 1.7) continue;
                            double ahead = (dx * lk.X + dy * lk.Y + dz * lk.Z) / d; if (ahead < 0.5) continue;
                            double score = d * (0.85 + 0.3 * rng.NextDouble()) * (g.Type == _lastGalaxyType ? 0.6 : 1);   // (another kind than the last, where it can)
                            if (HeartFeeds(g)) score += 1e6;                                       // (one whose heart is feeding: the tour flies by its disc; a quiet one only if none in view feeds)
                            if (score > bs) { bs = score; bi = i; }
                        }
                        if (bi >= 0 && !HeartFeeds(_galaxies[bi])) { int fb = -1; double fd = double.MaxValue; for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; if (!HeartFeeds(g)) continue; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = dx * dx + dy * dy + dz * dz; if (d > 9 && d < fd) { fd = d; fb = i; } } if (fb >= 0) bi = fb; }   // (none feeding ahead: the nearest feeding one anywhere round you, before a quiet one)
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
                case "land":
                    if (_ground != null && _ground.Solid)
                    {
                        // a spot of dry land to come down on, the nearest to under you (the seas are not dived into yet): rings of
                        // directions round the point under you, wider and wider, out to the far side if need be
                        double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) break;
                        double ux = x / r, uy = y / r, uz = z / r;
                        Func<double, double, double, bool> dry = (vx, vy, vz) => !_ground.HasWater || _ground.GroundAt(vx, vy, vz) > 0.02;
                        if (dry(ux, uy, uz)) { _tKind = 3; _tx = ux; _ty = uy; _tz = uz; _tName = "land"; break; }
                        double ex = Math.Abs(uy) < 0.9 ? 0 : 1, ey = Math.Abs(uy) < 0.9 ? 1 : 0, ez = 0;                     // a frame round the point under you
                        double e1x = ey * uz - ez * uy, e1y = ez * ux - ex * uz, e1z = ex * uy - ey * ux, e1l = Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z); e1x /= e1l; e1y /= e1l; e1z /= e1l;
                        double e2x = uy * e1z - uz * e1y, e2y = uz * e1x - ux * e1z, e2z = ux * e1y - uy * e1x;
                        bool found = false;
                        for (double ang = 0.01; ang < Math.PI && !found; ang *= 1.4)
                            for (int q = 0; q < 16 && !found; q++)
                            {
                                double th = q * Math.PI * 2 / 16 + ang, ca = Math.Cos(ang), sa = Math.Sin(ang), ct = Math.Cos(th), st2 = Math.Sin(th);
                                double vx = ux * ca + (e1x * ct + e2x * st2) * sa, vy = uy * ca + (e1y * ct + e2y * st2) * sa, vz = uz * ca + (e1z * ct + e2z * st2) * sa;
                                if (dry(vx, vy, vz)) { _tKind = 3; _tx = vx; _ty = vy; _tz = vz; _tName = "land"; found = true; }
                            }
                    }
                    break;
                case "planet":
                    if (_system != null)
                    {
                        // exploring: one of the star's worlds, chosen afresh (a solid one where there is one, to go down to; never a
                        // world that is all sea, until the dive under the sea is built)
                        var rng = new Rng(_seed ^ (ulong)(Environment.TickCount + 31)); var opts = new List<int>();
                        for (int i = 0; i < _system.Bodies.Count; i++)
                        {
                            var b = _system.Bodies[i]; if (b.Parent >= 0 || b.Giant) continue;
                            var tk = b.Terrain != null ? PlanetTerrain.TerrainKind(b.Terrain) : null;
                            if (tk != null && tk.Water && tk.LandShare <= 0) continue;
                            opts.Add(i);
                        }
                        if (opts.Count == 0) for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0) opts.Add(i);
                        // the director's eye: a world with seas and air most of all, then any solid world, by chance among them
                        double total = 0; var wts = new double[opts.Count];
                        for (int q = 0; q < opts.Count; q++) { wts[q] = _system.Bodies[opts[q]].Interest; total += wts[q]; }   // (each kind's interest, from its template)
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
        /// <summary>How wide the view is where you are looking (km): the distance to the nearest thing.</summary>
        public double ViewKm { get { return ViewDistanceKm(); } }
        /// <summary>The scale in the matter (km), or 0 above it: for the test runs, so a wheel can end on the last landmark.</summary>
        public double MatterKm { get { return _matterScale > 0 ? _matterScale : _spaceScale > 0 ? _spaceScale : 0; } }
        /// <summary>How many ground pieces are waiting (0 when not at a planet).</summary>
        public int GroundBacklog { get { return _ground != null ? _ground.Backlog : 0; } } private double _autoLogT;
        // ── what you clicked: the camera turns to face it and the wheel flies to it ──
        private Extra _tExtra;                   // the body picked (kind 6): the body itself, found again by its place whenever the bodies round you are made again
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
                int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Interest > _system.Bodies[pick].Interest)) pick = i;   // (the world of most interest, by its template)
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
            if (_tKind == 6 && _tSpot != null && _tExtra != null)
            {
                // the spot picked on the body's stone: the point itself on its drawn surface, a small mark to fly down to
                double rr = BodyRadiusToward(_tExtra, _lastDrawTime, _tSpot[0], _tSpot[1], _tSpot[2]);
                dx = RelX(_tExtra) - _ox + _tSpot[0] * rr; dy = RelY(_tExtra) - _oy + _tSpot[1] * rr; dz = RelZ(_tExtra) - _oz + _tSpot[2] * rr; radius = _tExtra.R * 0.05; return true;
            }
            // a small body: from its precise offset, the same place it is drawn at (its absolute coordinate, taken against the anchor,
            // is off by up to a million kilometres: the zoom had flown to a point beside the body and stalled there, the dive never
            // within reach of the stone it was aimed at)
            if (_tKind == 6 && _tExtra != null) { dx = RelX(_tExtra) - _ox; dy = RelY(_tExtra) - _oy; dz = RelZ(_tExtra) - _oz; radius = _tRadius; return true; }
            if (_tKind == 1 && _tSn != null && _tName == "a supernova") { _tx = _tSn.Ux; _ty = _tSn.Uy; _tz = _tSn.Uz; }
            if (_tKind == 1 && _system != null && _tName == "a star" && Math.Abs(_tx - _ax) + Math.Abs(_ty - _ay) + Math.Abs(_tz - _az) < StarRadiusNow * 0.01) { dx = -_ox; dy = -_oy; dz = -_oz; radius = StarRadiusNow; return true; }   // (the system's own star: its real radius, so a zoom onto its face ends at its face; one solar radius had stood for every star)   // (where it is now: a supernova is moved onto a real star of its galaxy once that galaxy's stars are made, after the pick from afar had copied its far place; the shots had flown to the old point, light-years from the dying star, and seen it as a point of light with no cloud)
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
            _tSn = null;
            if (ClickSupernova(dir)) return;                                                // a supernova going now, under the mouse: picked before any star
            double reachPx = 24 * Math.Max(1, _lastH / 1080.0), best = reachPx * reachPx; int kind = -1, idx = 0; double tx = 0, ty = 0, tz = 0, trad = 0; string name = "";
            _tSpot = null;
            // a spot on the small body you are at: where the line under the mouse meets its drawn stone (its real shape, as a world's
            // land is met), once it stands a few dozen pixels wide; picked, the zoom flies down to that spot, as to a spot of land
            if (_tKind == 6 && _tExtra != null && _tExtra.Shape.Length > 0 && !_tExtra.Hole && _ground == null)
            {
                var exs = _tExtra; double cx0 = RelX(exs) - _ox, cy0 = RelY(exs) - _oy, cz0 = RelZ(exs) - _oz, cl0 = Math.Sqrt(cx0 * cx0 + cy0 * cy0 + cz0 * cz0);
                if (cl0 > 0 && exs.R / cl0 * _lastH > 40)
                {
                    Func<double, double> over = t => { double hx = dir.X * t - cx0, hy = dir.Y * t - cy0, hz = dir.Z * t - cz0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz); return hl - BodyRadiusToward(exs, _lastDrawTime, hx / hl, hy / hl, hz / hl); };
                    double t0 = Math.Max(0, cl0 - exs.R * 2), t1 = cl0 + exs.R * 2, hitT = -1, prev = t0, prevV = over(t0);
                    for (int q = 1; q <= 240; q++) { double t = t0 + (t1 - t0) * q / 240.0, v = over(t); if (prevV > 0 && v <= 0) { double lo = prev, hi = t; for (int b2 = 0; b2 < 24; b2++) { double mid = (lo + hi) / 2; if (over(mid) > 0) lo = mid; else hi = mid; } hitT = (lo + hi) / 2; break; } prev = t; prevV = v; }
                    if (hitT > 0)
                    {
                        double hx = dir.X * hitT - cx0, hy = dir.Y * hitT - cy0, hz = dir.Z * hitT - cz0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                        _tSpot = new[] { hx / hl, hy / hl, hz / hl }; _tName = "a spot on " + exs.Name.ToLowerInvariant(); return;
                    }
                }
            }
            Func<double, double, double, double> near = (dx, dy, dz) => { double sx, sy; if (!ToScreen(dx, dy, dz, out sx, out sy)) return double.MaxValue; return (sx - mx) * (sx - mx) + (sy - my) * (sy - my); };
            for (int q = 0; q < _extras.Count && _ground == null; q++)
            {
                // the other bodies round you (black holes, neutron stars, rogue planets, asteroids, comets...): clickable as any star
                var ex = _extras[q]; double d2 = near(RelX(ex) - _ox, RelY(ex) - _oy, RelZ(ex) - _oz);
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
                        var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
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
                    _tKind = 3; _tx = hx / l; _ty = hy / l; _tz = hz / l; _tName = "a spot on " + GroundKind(); return;
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
                    var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
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
            var layHere = LayerHere(); var starHere = StarLayerHere();
            if (layHere == null && starHere != null && (kind < 0 || kind == 1))
            {
                // inside a star, a click is a point in the layer you are in, just ahead: the wheel flies to it and on into that layer's plasma
                double reachS = Units.Km(1000); kind = 7; idx = 0; tx = _ax + _ox + dir.X * reachS; ty = _ay + _oy + dir.Y * reachS; tz = _az + _oz + dir.Z * reachS; trad = 0; name = "a point in its " + starHere.Name.ToLowerInvariant();
            }
            else if (layHere != null && layHere.Matter != "core" && (kind < 0 || kind == 3))
            {
                // inside a giant, a click is a point in the layer you are in, just ahead where you clicked (its gas, its plasma, its
                // hot dense water): the wheel flies to it and on into that layer's own matter
                double reachU = Units.Km(1000); kind = 7; idx = 0; tx = _ax + _ox + dir.X * reachU; ty = _ay + _oy + dir.Y * reachU; tz = _az + _oz + dir.Z * reachU; trad = 0; name = "a point in its " + layHere.Name.ToLowerInvariant();
            }
            _tKind = kind; _tIndex = idx; _tx = tx; _ty = ty; _tz = tz; _tRadius = trad; _tName = name; if (kind == 5) _tGal = pickGal;
            if (kind >= 0 && !_hud.Visible) { _hud.Visible = true; MenuChanged = true; }   // (a click that picks something shows its card: cards hidden with H come back for it)
        }

        public bool Update(Input input, double dt, int w, int h)
        {
            Mark();
            LoadProgress(dt);
            if (Loading) input.Hold();                                                  // (the controls wait while the universe is made)
            if (_pausedTour != null)
            {
                // a paused tour: T carries it on (the T key and the menu, as they start it), R starts it again, Backspace lets it go
                if (input.WasPressed('R')) StartTour(_tourName);
                else if (input.WasPressed(0x08)) { _pausedTour = null; _pausedWhy = null; MenuChanged = true; }
            }
            if (_tour != null && !Loading) TourStep(input, dt, w, h);   // (the tour begins once the universe is made)
            _matter.Glide = _tour != null ? 1.2 : 4;                                     // on the tour the matter glides as a film camera would                                  // a tour gives the controls what each stop asks
            // a click (pressed and let go without dragging) picks what is under the mouse
            if (input.LeftClicked) { _pressed = true; _dragged = false; _pressX = input.MouseX; _pressY = input.MouseY; }   // (the click that brings the window to the front picks too: it stops the tour first, so it never sets a spot off to the side as the tour's target)
            if (_pressed && input.LeftDown && Math.Abs(input.MouseX - _pressX) + Math.Abs(input.MouseY - _pressY) > 6) _dragged = true;   // dragging circles round the pick (or looks around if nothing is picked)
            if (_pressed && !input.LeftDown) { _pressed = false; if (!_dragged)
                {
                    if (_matterScale > 0 || _spaceScale > 0) { if (_matter.Pick(input.MouseX, input.MouseY) && !_hud.Visible) { _hud.Visible = true; MenuChanged = true; } }   // in the matter or the gas: what is there (its card shown, even if hidden with H)
                    else if (_ground != null && AboveGround() < Units.Km(MatterView.OverlayKm) && _matter.Pick(input.MouseX, input.MouseY))
                    { if (!_hud.Visible) { _hud.Visible = true; MenuChanged = true; } }   // a grain over the ground: picked, its card shown; the ground itself carries you on down to it (never a second layer of grains)   // a grain over the ground: on into the matter, the same picture, flying to it
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
                if (l > 0 && AtGroundBody())
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
                if (l > 0 && _tour != null)
                {
                    // on the tour, a slow eased turn, as a camera operator would: eased in as well as out (the turn's speed itself
                    // brought up from rest over its first second and a half whenever a new pick lies off the view, so the last thing
                    // never flies out of view in a snap) and never over about twenty degrees a second, so a pick behind you is
                    // turned to calmly, never whipped round to
                    var lk = Look; double ang = Math.Acos(Math.Max(-1, Math.Min(1, (lk.X * tdx + lk.Y * tdy + lk.Z * tdz) / l)));
                    if (ang < 0.05) _faceT = 0; else _faceT += dt;
                    double fr = Math.Min(1, _faceT / 1.5); fr = fr * fr * (3 - 2 * fr);
                    double rate = 0.6 * (fr + (1 - fr) * Math.Min(1, 0.05 / Math.Max(1e-6, ang)));   // (a small offset is kept closed at full rate; a big one starts slow)
                    double k = 1 - Math.Exp(-dt * rate); if (ang > 1e-6) k = Math.Min(k, 20 * Math.PI / 180 * dt / ang);
                    EaseLook(tdx, tdy, tdz, k);
                }
                else if (l > 0) EaseLook(tdx, tdy, tdz, 1 - Math.Exp(-dt * 5));
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
                    int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Interest > _system.Bodies[pick].Interest)) pick = i;   // (the world of most interest, by its template)
                    if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; }
                }
                if (_tKind != 2) AimNext();
                input.Wheel += 1;
                _autoLogT += dt;
                if (_autoLogT > 1) { _autoLogT = 0; try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), PreviewText.AppName.ToLowerInvariant().Replace("-", "") + "_autopath.log"), Title + Environment.NewLine); } catch { } }
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
                                  && !(_spaceScale > 0 && _matter.SpaceKind == 12)   // (in the fall a drag looks round the well: up at the mouth, down the spike to the point, out at the walls; never a circle round the hole, which snapped the view back to its middle)
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
                bool falling = _matter.SpaceKind == 12 && _fallHole != null;
                if (falling)
                {
                    // the fall is a travel first and a zoom last: the way in from the horizon to the point (millions of light-years, the
                    // fall's own measure) is run with the view held at the horizon's own scale, the walls of the well closing in round you
                    // as it goes, each notch of the wheel a step of the way; only once the walls have met at the point does the wheel
                    // zoom the view on into it, down to the quarks' size, where the singularity shows. Back out the same way
                    const double NotchTenfolds = 0.060480747;                                     // a notch: 13% of the scale, in tenfolds
                    if (input.Wheel != 0)
                    {
                        bool zooming = _fallShare >= 1 && (input.Wheel > 0 || _spaceScale < _fallScale0 * 0.999);
                        if (zooming) _spaceScale = Math.Min(_fallScale0, Math.Max(MatterView.BottomKm, _spaceScale * Math.Pow(0.87, input.Wheel * 3)));
                        else _fallShare = Math.Max(-0.001, Math.Min(1, _fallShare + input.Wheel * NotchTenfolds / MatterView.FallTenfolds));
                        if (_fallShare < 1) _spaceScale = _fallScale0;
                    }
                    if (_fallShare < 0) { _spaceScale = 0; _fallHole = null; falling = false; }   // climbed back out past the horizon
                }
                else
                {
                    if (input.Wheel != 0) _spaceScale *= Math.Pow(0.87, input.Wheel);
                    if (input.Wheel > 0) _matter.Landmarks(_spaceScale);                      // (on a body's ground: the wheel comes down onto an atom, its nucleus, a nucleon, a quark, as on a world's)
                    if (_spaceScale > _spaceTop) _spaceScale = 0; else _spaceScale = Math.Max(MatterView.BottomKm, _spaceScale);
                }
                if (falling)
                {
                    // the fall's clock: the whole way in takes two thirds of rs over c of your own time, run with the way
                    double rsKm0 = _matter.HoleRs, tauEnd = 2.0 / 3 * rsKm0 / 299792.458;
                    _fallTau = tauEnd * Math.Max(0, _fallShare);
                    { double rK = rsKm0 * Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds); _matter.SetFallClock(_fallTau, _fallRate, Math.Sqrt(rsKm0 / Math.Max(1e-300, rK))); _matter.SetFallShare(Math.Max(0, _fallShare)); }
                    // the fall is travel as well as a zoom: from the horizon to the middle the camera itself goes down the axis it came in
                    // on, always at the view's own distance from the middle, the scale shrinking with it (both at once); climbing out it
                    // comes back up the same way, and leaves the dive just outside the horizon
                    double rr = Units.Km(Math.Max(_spaceScale, MatterView.BottomKm) * 0.6);
                    if (_spaceScale <= 0) rr = Units.Km(_spaceTop * 0.6);
                    _ox = RelX(_fallHole) - _fallAxis.X * rr; _oy = RelY(_fallHole) - _fallAxis.Y * rr; _oz = RelZ(_fallHole) - _fallAxis.Z * rr;
                    if (_spaceScale <= 0) _fallHole = null;
                }
                if (input.WasPressed(0x20)) TogglePause();
                if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);
                if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);
                _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);
                if (TimeStep > 0) _matterClock += dt;
                return true;
            }
            if (_matterScale > 0)
            {
                // below a millimetre or so: the same zoom, with your height kept finely enough for atoms (the wheel moves
                // you the same share of the way each notch; W A S D Q E fly at the same speed for the scale; time runs on)
                if (input.Wheel != 0) _matterScale *= Math.Pow(0.87, input.Wheel);
                if (input.Wheel > 0) _matter.Landmarks(_matterScale);                     // (the wheel goes down onto the landmark at each size: an atom, its nucleus, a nucleon, a quark; never between them into nothing)
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
                _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);
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
                    var slh = StarLayerHere();
                    if (slh != null) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), shr = sdK / Math.Max(1e-30, StarRadiusNow); _matter.SetPlasma(lspot, slh.MakeUp ?? SystemStarMakeUp(), LayerDensity(slh, shr), slh.Name.ToLowerInvariant(), LayerTempK(slh, shr)); }
                    else if (lay != null && lay.Matter == "plasma") _matter.SetPlasma(lspot);
                    else if (lay != null && lay.Matter == "ice") { _matter.SetAir("water vapour 70%, ammonia 15%, methane 15%", lspot); }   // hot dense water, ammonia and methane
                    else _matter.SetAir(air, lspot);
                    _spaceScale = Math.Max(MatterView.BottomKm, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;
                    return true;
                }
            }
            if (_system != null && _ground == null && input.Wheel > 0 && _spaceScale <= 0 && Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) < StarRadiusNow * 1.002 && Math.Abs(_ax) + Math.Abs(_ay) + Math.Abs(_az) >= 0)
            {
                // into the star's surface: its plasma, the same zoom carrying on
                ulong sspot = (ulong)(long)(_ox / Units.Km(1e3)) * 73856093UL ^ (ulong)(long)(_oy / Units.Km(1e3)) * 19349663UL ^ (ulong)(long)(_oz / Units.Km(1e3)) * 83492791UL;
                {
                    // its layer at this depth (content/stars.unt @layer): its own make-up, heat and density there, the photosphere at the face
                    double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), shr = Math.Min(1, sdK / Math.Max(1e-30, StarRadiusNow)); var sl = StarLayerAt(shr);
                    if (sl != null) _matter.SetPlasma(sspot, sl.MakeUp ?? SystemStarMakeUp(), LayerDensity(sl, shr), sl.Name.ToLowerInvariant(), LayerTempK(sl, shr));
                    else _matter.SetPlasma(sspot, SystemStarMakeUp());                      // (its own make-up: content/stars.unt)
                }
                _spaceScale = 1.0 / 0.6; _spaceTop = _spaceScale * 1.1;                   // from a kilometre: a fog of plasma, down to its nuclei
                return true;
            }
            if (_tKind == 6 && _spaceScale <= 0 && _matterScale <= 0 && _tIndex >= 0 && _tIndex < _extras.Count && (int)(_lastDrawTime * 2) != _diveLogTick)
            {
                // the dive's state, twice a second, so a dive that does not come says why in the log: the body, whether it is the one
                // picked, how far its middle is against its radius and its stone's radius here, the wheel, the course, whether it is drawn as its stone
                _diveLogTick = (int)(_lastDrawTime * 2); var exl = _extras[_tIndex]; double lx = RelX(exl) - _ox, ly = RelY(exl) - _oy, lz = RelZ(exl) - _oz, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                double lrD = exl.Hole || exl.Shape.Length == 0 ? exl.R : BodyRadiusToward(exl, _lastDrawTime, -lx / Math.Max(1e-300, ll), -ly / Math.Max(1e-300, ll), -lz / Math.Max(1e-300, ll));
                ZoomLog("DIVE check: " + exl.Name + ", same as the pick " + (exl == _tExtra) + ", middle " + ScaleHud.Words(ll / Units.Km(1)) + " off, nominal R " + ScaleHud.Words(exl.R / Units.Km(1)) + " (pick R " + ScaleHud.Words(_tRadius / Units.Km(1)) + "), stone R here " + ScaleHud.Words(lrD / Units.Km(1)) + ", wheel " + input.Wheel.ToString("0.00") + ", on course " + OnCourse().ToString("0.00") + ", over pick " + OverPick().ToString("0.00") + ", drawn as stone " + ShapeShown(exl, _lastH) + ", bodies " + _extras.Count);
            }
            if (_tKind == 6 && input.Wheel > 0 && _spaceScale <= 0 && _matterScale <= 0 && _tIndex >= 0 && _tIndex < _extras.Count)
            {
                // at a picked body: on in to what it is made of, the same zoom. A small body (an asteroid, a comet's nucleus, a Kuiper
                // body, a rogue planet): onto its surface, its own grains, as a world's ground is dived into (from a centimetre, where
                // the grains show). A neutron star: into its crust. A black hole: through the horizon, the fall to the singularity
                var exd = _extras[_tIndex]; double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && qr > 0)
                {
                    double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                    double cxv = RelX(exd) - _ox, cyv = RelY(exd) - _oy, czv = RelZ(exd) - _oz, cl = Math.Max(1e-300, Math.Sqrt(cxv * cxv + cyv * cyv + czv * czv));   // (from the body's middle: a spot picked on it only steers the way down; the ground is what is under you)
                    double qrD = exd.Hole || exd.Shape.Length == 0 ? qr : BodyRadiusToward(exd, _lastDrawTime, -cxv / cl, -cyv / cl, -czv / cl);   // the stone's real surface under you, not its nominal sphere
                    ulong dspot = (ulong)(long)(_ox / Units.Km(1)) * 73856093UL ^ (ulong)(long)(_oy / Units.Km(1)) * 19349663UL ^ (ulong)(long)(_oz / Units.Km(1)) * 83492791UL;
                    if (ql < (exd.Hole && exd.Mass > 0 ? Units.Km(Physics.HorizonKm(exd.Mass)) * 1.25 : double.MaxValue) && (exd.Hole || cl < Math.Max(qr, qrD) * 1.3))   // (within 1.3 of its nominal radius, where the zoom's own pacing, which stops at 1.2 of it, can reach: measured against the drawn stone alone, a rock squashed along this side was never reached, and the dive never fired)
                    {
                        if (exd.Hole && exd.Mass > 0)
                        {
                            double rsKm = Physics.HorizonKm(exd.Mass);
                            _matter.SetHole(dspot, rsKm, exd.Mass, exd.Spin, _wayInLy * Math.Pow(Math.Max(1e-6, exd.Mass) / _wayInMass, _wayInPower) * 9.4607e12); _bodyUp = null; _spaceScale = rsKm / 0.6; _spaceTop = _spaceScale * 1.1; _fallHole = exd;
                            // the fall's clock: from the horizon to the middle takes two thirds of rs over c of your own time (26 seconds for a
                            // four-million-solar-mass hole, 70 microseconds for a stellar one, eleven minutes for a hundred-million-solar-mass one); it is
                            // shown over about thirty seconds, slowed or sped as that needs, and the card says by how much
                            { double tauEnd = 2.0 / 3 * rsKm / 299792.458; _fallTau = 0; _fallRate = Math.Max(1, tauEnd / 50.0); _fallShare = 0; _fallScale0 = _spaceScale; }   // (the stretch on the hole's own clock, from the horizon to a thousandth of it, shown in about eight seconds whatever the hole: a big hole's seven hundred seconds took a minute; never slower than real)
                            { double ql2 = Math.Max(1e-300, ql); _fallAxis = new Vec3((float)(qx / ql2), (float)(qy / ql2), (float)(qz / ql2)); _fallTh = -1; }   // the axis you came in on: the well's grid is drawn down it; the look is left as it was (at the hole: down the axis), never snapped round (the hole had flown out of view in a snap)
                            ZoomLog("DIVE through the horizon of " + exd.Name + " (" + ScaleHud.Words(rsKm * 2) + " across)"); return true;
                        }
                        if (exd.Pl != null && (exd.Pl.Id.Contains("neutron") || exd.Pl.Id.Contains("pulsar") || exd.Pl.Id.Contains("magnetar")))
                        {
                            _matter.SetNeutronCrust(dspot); _bodyUp = null; _spaceScale = 1e-3 / 0.6; _spaceTop = _spaceScale * 1.1;
                            ZoomLog("DIVE into the crust of " + exd.Name); return true;
                        }
                        if (exd.Mix != null && (exd.Shape.Length == 0 || _ground == null || _planet != -2))
                        {
                            double ux = -cxv / cl, uy = -cyv / cl, uz = -czv / cl;                 // from the body's middle to you: its surface here, under you
                            double sx = _ax - exd.X, sy = _ay - exd.Y, sz = _az - exd.Z, sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);   // its star, for the light on the grains
                            _matter.Set(exd.Mix, false, false, exd.Cr, exd.Cg, exd.Cb, dspot);
                            _matter.SetGround(qrD / Units.Km(1), ux, uy, uz, exd.Mix, dspot * 0x9E3779B97F4A7C15UL + 0x51UL, 0, 0); exd.SpinHold = BodySpin(exd, _lastDrawTime);   // (its ground at the drawn stone's own height here; its spin held while you are on it)
                            _matter.SetAirAbove(null);
                            _bodyUp = new[] { ux, uy, uz }; _bodyStar = sl > 0 ? new[] { sx / sl, sy / sl, sz / sl } : new[] { ux, uy, uz };
                            double hgt = Math.Max(cl - qrD, Units.Km(MatterView.OverlayKm * 2)); _ox = RelX(exd) + ux * (qrD + hgt); _oy = RelY(exd) + uy * (qrD + hgt); _oz = RelZ(exd) + uz * (qrD + hgt);   // (the camera set just over the drawn stone under it, never inside a stone that reaches past its nominal sphere)
                            _spaceScale = Math.Max(MatterView.OverlayKm / 0.6, hgt / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;   // (from your real height over its skin, the rock still drawn behind: the zoom goes on down to the grains at its own pace, no jump to a centimetre)
                            ZoomLog("DIVE onto " + exd.Name + ": its ground " + exd.Mix + " from " + ScaleHud.Words(_spaceScale * 0.6) + " up"); return true;
                        }
                    }
                }
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
                        bool atIt = AtGroundBody();
                        reach = atIt ? AboveGround() : Math.Max(fl - (_tKind == 5 ? 0 : _tKind == 6 && _tExtra != null && _tExtra.Hole && _tExtra.Mass > 0 ? Units.Km(Physics.HorizonKm(_tExtra.Mass)) * 1.08 : frad * 1.2), fl * 1e-6);   // clusters and nebulae are clouds: flown into, not stopped at; a black hole: down to just outside its horizon
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
                ZoomLog("wheel " + input.Wheel.ToString("0") + " step " + Units.Describe(Math.Abs(step)) + " reach " + Units.Describe(reach) + " pick " + _tKind + ":" + _tIndex + " planet " + _planet + " nearest-body " + _cpBest + " at " + _cpBd.ToString("0.##") + " radii follow " + _followBody + (_ground != null ? " ground-ready " + (_ground.ReadyStep < 1e30 ? ScaleHud.Words(_ground.ReadyStep) : "none") + " height " + ScaleHud.Words(AboveGround() / Units.Km(1)) + " base " + BaseHeightWords() + " piece " + (_ground.CamPieceEdge > 0 ? ScaleHud.Words(_ground.CamPieceEdge) + " L" + _ground.CamPieceLevel : "none") + " [" + _ground.BuildStats + "; " + _ground.FrameStats + "]" : ""));
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
                    if (altBefore < MatterView.OverlayKm) floorKm = Math.Max(1e-9, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 1.2 : 1e-9);   // below a centimetre the grains cover the ground, but the drawn pieces are flat facets a cell across that can stand above the true ground: never under them (the sky showed through the planet), the matter takes over first                 // below a centimetre the grains cover the ground: no waiting, on down past the 30 micrometres where the matter takes over
                    {
                        // over the sea: no lower than a metre above the water, until the dive under the sea is in the universe view
                        double cx0, cy0, cz0; CamFromPlanet(out cx0, out cy0, out cz0); double r0 = Math.Sqrt(cx0 * cx0 + cy0 * cy0 + cz0 * cz0);
                        // over the sea: the water's surface is the floor until you are a metre from it, then the sea bed is (the dive)
                        if (_ground.HasWater && !_underSea && _ground.GroundAt(cx0 / r0, cy0 / r0, cz0 / r0) < 0) floorKm = Math.Max(floorKm, 5e-7);
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
            _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]); SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);                                    // time runs at the speed chosen
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
                    double r = i == 0 ? StarRadiusNow : _system.Bodies[i - 1].RadiusUnits;
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
                var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
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
            bool overIt = AtGroundBody() && AboveGround() / Units.Km(1) < _ground.Radius * 0.5;
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
        // how far on course the tour's flight is (1 looking at what it flies to, easing to 0 by 0.12 radians off): it slows as
        // it turns and speeds as it comes round, never starting or stopping all at once
        // in the matter, how far over what is picked the view is: the tour slows as it lines up (to a quarter, never stopped,
        // so it never looks frozen) and speeds again once over it
        // below a giant's cloud tops (it has no ground: inside, only its gas)
        // ── the start: held on a loading screen until what you see first is made (the web's light, the galaxies made in full
        // round you with their stars), so the universe never opens half made and filling in; never more than a minute ──
        private int _glowMade, _glowWant = 1; private bool _loadDone; private double _loadT, _loadShare; private string[] _loadLines;
        /// <summary>Still making what is seen first, at the start.</summary>
        public bool Loading { get { return !_loadDone; } }
        /// <summary>The recording notice over the picture (drawn after the frame is taken for the clip, so it is not in it).</summary>
        public void DrawRec(int w, int h, double time, double seconds) { _gpu.ClearDepth(); _hud.DrawRec(_meshes, w, h, time, seconds); }
        private void LoadProgress(double dt)
        {
            if (_loadDone) return; _loadT += dt;
            double web = _glowAll != null ? 1 : Math.Min(0.99, _glowMade / (double)Math.Max(1, _glowWant));
            int near = 0, made = 0;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz;
                if (dx * dx + dy * dy + dz * dz > _nearUnits * _nearUnits * 2.25) continue;
                near++; if (g.Far != null) made++;
            }
            double gal = near == 0 ? (_loadT > 3 ? 1 : 0) : made / (double)near;
            _loadShare = web * 0.65 + gal * 0.35;
            _loadLines = new[] { "Making the universe", web < 1 ? "the cosmic web: " + ((int)(web * 100)) + "% of its light" : "the cosmic web: ready", "the galaxies round you: " + made + " of " + Math.Max(near, made) };
            if ((web >= 1 && gal >= 1 && _loadT > 1.5) || _loadT > 60) { _loadDone = true; MenuChanged = true; }   // (held a moment and a half at the least: a bar that flashes by on a fast machine is a bar never seen)
        }
        private bool InsideGiant { get { if (_ground == null || _ground.Solid || _planet < 0) return false; double x, y, z; CamFromPlanet(out x, out y, out z); return Math.Sqrt(x * x + y * y + z * z) < _ground.Radius + _ground.CloudKm * 0.9; } }
        private double OverPick() { if (_matterScale <= 0 && _spaceScale <= 0) return 1; double u = Math.Max(0, Math.Min(1, (0.6 - _matter.PickOff) / 0.4)); u = u * u * (3 - 2 * u); return 0.25 + 0.75 * u; }
        private double OnCourse() { double u = Math.Max(0, Math.Min(1, (0.12 - OffTarget()) / 0.08)); return u * u * (3 - 2 * u); }
        private double OffTarget()
        {
            if (AtGroundBody()) return 0;          // (coming down to the picked world the view looks ahead to its horizon, not at its middle: that is on course)
            if (_spaceScale > 0) return 0;                                               // (dived into a body or a hole: the zoom goes on wherever you look; the fall is watched looking back at the horizon)
            double dx, dy, dz, r; if (_tKind < 0 || !TargetFromCamera(out dx, out dy, out dz, out r)) return 0;
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0) return 0; var lk = Look;
            double off = Math.Acos(Math.Max(-1, Math.Min(1, (dx * lk.X + dy * lk.Y + dz * lk.Z) / l)));
            if (_tKind == 3 && _ground != null) off = Math.Max(0, off - 0.5);           // a spot on the ground: within about thirty degrees of the look is on course (coming down onto it the view looks down and the spot is under you a little to one side, and that angle grows as the height shrinks: it stood still at a centimetre, the spot a few degrees past the limit)
            return off;
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
        // the frame a shot is set in: level up, or at a feeding black hole its disc's axis, so a shot's rise is measured from the
        // disc (rise 0 skims it, a small rise grazes over it, a dive below sees its underside)
        private Vec3 ShotUp()
        {
            if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count && _extras[_tIndex].Hole) { var ex = _extras[_tIndex]; return new Vec3(0, (float)Math.Cos(ex.Tilt), (float)Math.Sin(ex.Tilt)); }
            return LevelUp();
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
        private double _cstarCX, _cstarCY, _cstarCZ, _cstarR;   // the cluster's middle (universe units) and size, for its light on screen
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
            _cstarFeature = inF; _cgx = px; _cgy = py; _cgz = pz; _cstarHalf = half; _cstars.Clear(); _cstarCX = cx; _cstarCY = cy; _cstarCZ = cz; _cstarR = Rc;
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
        // -- the nebulae close up: a nebula had been only its far marker (eight soft points scattered over it, for the galaxy's sky seen from
        // outside), so flown to, or dived into, there was nothing on the screen; each kind is a cloud of its own gas now, in the colours of
        // what it is made of (its make-up, the same its card and its far marker use), made once when you come within forty of its radii
        private readonly Dictionary<long, UploadBuffer> _nebBufs = new Dictionary<long, UploadBuffer>(); private readonly Dictionary<long, int> _nebNs = new Dictionary<long, int>();
        private void DrawNebulae(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (_near < 0 || _near >= _galaxies.Count || _features == null) return;
            var g = _galaxies[_near];
            for (int q = 0; q < _features.Count; q++)
            {
                var fq = _features[q]; int fk = (int)fq[4]; if (fk < 2 || fk > 6) continue;
                double ux, uy, uz; ToUniverse(g, fq[0], fq[1], fq[2], out ux, out uy, out uz);
                double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, R = fq[3] * g.Scale, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (R <= 0 || d > R * 40) continue;
                long key = (long)(g.Seed % 1000003UL) * 100003L + q; UploadBuffer buf; int n;
                if (!_nebBufs.TryGetValue(key, out buf))
                {
                    if (_nebBufs.Count >= 12) { foreach (var kv in _nebBufs) kv.Value.Dispose(); _nebBufs.Clear(); _nebNs.Clear(); }   // (a few at a time: the ones round you)
                    var cloud = NebulaCloud(fk, fq.Length > 5 ? fq[5] : 0.5, g.Seed ^ (ulong)(q * 2654435761L));
                    n = cloud.Count; buf = new UploadBuffer(_gpu.Device, (long)Math.Max(1, n) * Stride); if (n > 0) buf.Write(cloud.ToArray(), n);
                    _nebBufs[key] = buf; _nebNs[key] = n;
                }
                n = _nebNs[key]; if (n == 0) continue;
                float fade = (float)Math.Max(0, Math.Min(1, (40 - d / R) / 20));   // (coming in from forty of its radii to twenty; the far marker's points stand for it beyond)
                var m = Mat4.Identity; m.M11 = (float)R; m.M22 = (float)R; m.M33 = (float)R; m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                DrawPoints(buf, n, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade * (fk == 6 ? 0.35f : 0.75f));
            }
        }
        // a nebula's cloud in its own units (its radius 1): a planetary nebula a thin shell puffed off its dying star (oxygen's green-blue
        // within, hydrogen's and nitrogen's red at the rim, the white dwarf at its middle); a supernova's remnant a torn shell of filaments;
        // an emission nebula knots and pillars of glowing gas with its hot young stars inside, a reflection nebula smaller and blue round
        // its stars, a dark nebula dim dust; every point's colour from a part of what the nebula is made of, by its share
        private static List<StarVertex> NebulaCloud(int kind, double mix, ulong seed)
        {
            var rng = new Rng(seed); var parts = SpaceGen.Composition(kind, mix); var l = new List<StarVertex>();
            Func<SpaceGen.Part> part = () => { double u = rng.NextDouble(); foreach (var p in parts) { u -= p.Share; if (u <= 0) return p; } return parts[parts.Length - 1]; };
            Func<Vec3> dir = () => { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, s = Math.Sqrt(1 - u * u); return new Vec3((float)(s * Math.Cos(a)), (float)u, (float)(s * Math.Sin(a))); };
            if (kind == 4)
            {
                float squash = (float)(0.6 + 0.4 * rng.NextDouble());
                for (int k = 0; k < 2600; k++)
                {
                    var dv = dir(); double rr = 0.75 + 0.22 * rng.NextDouble() + 0.05 * rng.Gaussian(); var p = part(); bool rim = rr > 0.9;
                    float br = (float)(0.35 + 0.45 * rng.NextDouble()) * (rim ? 1f : 0.7f), cr = rim ? 1f : p.R, cg = rim ? 0.3f : p.G, cb = rim ? 0.3f : p.B;
                    l.Add(new StarVertex { X = (float)(dv.X * rr), Y = (float)(dv.Y * rr * squash), Z = (float)(dv.Z * rr), Size = 0.09f, R = cr * br, G = cg * br, B = cb * br, A = 1 });
                }
                for (int k = 0; k < 600; k++) { var dv = dir(); double rr = Math.Pow(rng.NextDouble(), 0.5) * 0.7; const float br = 0.18f; l.Add(new StarVertex { X = (float)(dv.X * rr), Y = (float)(dv.Y * rr * squash), Z = (float)(dv.Z * rr), Size = 0.14f, R = 0.2f * br, G = 1f * br, B = 0.75f * br, A = 1 }); }
                l.Add(new StarVertex { X = 0, Y = 0, Z = 0, Size = 0.012f, R = 0.8f, G = 0.9f, B = 1f, A = 1 });   // the white dwarf that puffed it off
                return l;
            }
            if (kind == 5)
            {
                int fil = 40; var fd = new Vec3[fil]; for (int f = 0; f < fil; f++) fd[f] = dir();
                for (int k = 0; k < 3200; k++)
                {
                    var f0 = fd[rng.Below(fil)]; var jt = dir(); double rr = 0.85 + 0.15 * rng.NextDouble(); var dv = (f0 * 0.75f + jt * 0.35f).Normalized; var p = part(); float br = (float)(0.3 + 0.5 * rng.NextDouble());
                    l.Add(new StarVertex { X = dv.X * (float)rr, Y = dv.Y * (float)rr, Z = dv.Z * (float)rr, Size = 0.07f, R = p.R * br, G = p.G * br, B = p.B * br, A = 1 });
                }
                for (int k = 0; k < 500; k++) { var dv = dir(); double rr = Math.Pow(rng.NextDouble(), 0.4); const float br = 0.12f; l.Add(new StarVertex { X = dv.X * (float)rr, Y = dv.Y * (float)rr, Z = dv.Z * (float)rr, Size = 0.16f, R = 0.9f * br, G = 0.4f * br, B = 0.35f * br, A = 1 }); }
                return l;
            }
            int knots = 28; var kc = new Vec3[knots]; var ks = new float[knots];
            for (int q = 0; q < knots; q++) { kc[q] = new Vec3((float)rng.Gaussian() * 0.45f, (float)rng.Gaussian() * 0.3f, (float)rng.Gaussian() * 0.45f); ks[q] = (float)(0.12 + 0.3 * rng.NextDouble()); }
            int pts = kind == 6 ? 1800 : 3000;
            for (int k = 0; k < pts; k++)
            {
                int q = rng.Below(knots); var c = kc[q] + new Vec3((float)rng.Gaussian(), (float)rng.Gaussian(), (float)rng.Gaussian()) * ks[q];
                double cl = Math.Sqrt(c.X * c.X + c.Y * c.Y + c.Z * c.Z); if (cl > 1) continue;
                var p = part(); if (p.El == "O" && cl > 0.5) continue;                                   // (oxygen's teal toward the middle, hydrogen's red throughout)
                float br = (float)((0.25 + 0.45 * rng.NextDouble()) * (1 - cl * 0.5)) * (kind == 6 ? 0.45f : 1f);
                l.Add(new StarVertex { X = c.X, Y = c.Y, Z = c.Z, Size = kind == 3 ? 0.11f : 0.15f, R = p.R * br, G = p.G * br, B = p.B * br, A = 1 });
            }
            if (kind == 2 || kind == 3) for (int k = 0; k < (kind == 2 ? 24 : 6); k++) { int q = rng.Below(knots); var c = kc[q] + new Vec3((float)rng.Gaussian(), (float)rng.Gaussian(), (float)rng.Gaussian()) * ks[q] * 0.5f; l.Add(new StarVertex { X = c.X, Y = c.Y, Z = c.Z, Size = 0.01f, R = 0.75f, G = 0.85f, B = 1f, A = 1 }); }   // the hot young stars that light it
            return l;
        }
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
            _lastDrawTime = time;
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
                double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - ex.R * 1.6;
                if (d <= 0) d = ex.R * 0.01;                                        // (beside it or over it: the near plane a hundredth of its radius, never leaping out past the stone; within two radii it had dropped out of the reckoning, the near plane leapt out to the next thing, and the stone was clipped away as you came in)
                if (d < reach) reach = d;
            }
            Mat4 proj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)(reach * 1e-3), (float)(_uRadius * 2.2));   // out to the far side of the visible universe
            Mat4 vp = view * proj;
            if (_ground != null)
            {
                // at a world its ground is held still under you and the sky turns round it, by its spin (the same turn as its
                // starlight on the ground): its star rises and sets, the stars wheel overhead, about the world's middle
                double sp = -SpunSince; float c0 = (float)Math.Cos(sp), s0 = (float)Math.Sin(sp);
                var rot = Mat4.Identity; rot.M11 = c0; rot.M13 = s0; rot.M31 = -s0; rot.M33 = c0;
                float pcx = (float)(_px - _ox), pcy = (float)(_py - _oy), pcz = (float)(_pz - _oz);
                vp = Mat4.Translation(-pcx, -pcy, -pcz) * rot * Mat4.Translation(pcx, pcy, pcz) * vp;
            }
            _lastVp = vp; _lastW = w; _lastH = h; _haveVp = true;
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            _viewRight = right; _viewUp = up; _haveAxes = true;
            double share = NearShare(), within = InsideShare();
            // the world you are falling to fills the whole view: everything behind it (the web, the galaxies, their clusters and
            // nebulae, the stars round you) cannot be seen, so none of it is drawn; what lies in front (its moons, its rings) still is
            bool skyHidden = InsideGiant;                                               // (inside a giant's gas nothing past it shows)
            if (!skyHidden && _ground != null)
            {
                double qx = _px - _ox, qy = _py - _oy, qz = _pz - _oz, qd = Math.Sqrt(qx * qx + qy * qy + qz * qz), pr = _ground.Radius * Units.Km(1) * (_ground.Solid ? 0.999 : 0.97);
                if (qd > pr)
                {
                    var lk = Look; double off = Math.Acos(Math.Max(-1, Math.Min(1, (qx * lk.X + qy * lk.Y + qz * lk.Z) / qd)));
                    double aspect = w / (double)Math.Max(1, h), halfDiag = Math.Atan(Math.Tan(0.45) * Math.Sqrt(1 + aspect * aspect));
                    skyHidden = off + halfDiag < Math.Asin(Math.Min(1, pr / qd));
                }
            }
            // inside a galaxy its own stars fill the sky and the other galaxies show fainter behind them (as Andromeda
            // is barely seen with the eye): the others and the web's glow fade to a sixth as you go in
            float others = (float)(1 - 0.45 * Math.Max(0, Math.Min(1, within)));   // (dimmed a little: the other galaxies still show in its sky)
            // the web's points, each a far galaxy, dimmer and smaller the nearer a galaxy you are, so from a galaxy's edge its own
            // stars (sharp, in their colours) and the web's galaxies behind (soft, faint) are never taken for each other
            float webNear = (float)(1 - 0.75 * Math.Max(0, Math.Min(1, within)));
            {
                // the rest of the universe's galaxies, a glow along the cosmic web (remade without the near ones as you move far)
                if (double.IsNaN(_glowAtX) || _glowReady != null || (Math.Abs(_cx - _glowAtX) + Math.Abs(_cy - _glowAtY) + Math.Abs(_cz - _glowAtZ) > _nearUnits * 0.25 && time - _glowAtT > 0.5)) { MakeGlow(); _glowAtT = time; }   // (only to start it, take it in when made, or make it again far away: the card sifts it each frame)
                var gm = Mat4.Identity; gm.M41 = (float)-_cx; gm.M42 = (float)-_cy; gm.M43 = (float)-_cz;
                float webFade = (float)Math.Max(0, Math.Min(1, (0.98 - within) / 0.45)); webFade = webFade * webFade * (3 - 2 * webFade);   // (inside a galaxy the web's points are not drawn: its sky shows the other galaxies themselves, made in full round you)
                webFade *= (float)Math.Min(1, (System.Diagnostics.Stopwatch.GetTimestamp() - _glowCameAt) / (2.0 * System.Diagnostics.Stopwatch.Frequency));   // (newly made: faded in over two seconds)
            Render.GpuLog.Pass = "the web's light";
                if (webFade > 0.01 && !skyHidden)   // (deep inside a galaxy its stars and dust hide the web, and it is not drawn; it fades in as you rise out of it, never popping in)
                {
                _web[0] = (float)(2.0e9 / Units.LightYearsPerUnit); _web[1] = (float)_cx; _web[2] = (float)_cy; _web[3] = (float)_cz; _web[4] = (float)_nearUnits;   // (from where the galaxies made in full begin to fade out, the web fades in over the same span: they hand over, never both at once, never a gap)
                DrawPoints(_glowBuf, _glowCount, gm * vp, right, up, 1f, 0.0035f * (0.5f + 0.5f * webNear), w, h, time, 0.007f * (0.5f + 0.5f * webNear), others * webFade * webNear, 0f);   // as bright as the galaxies it stands for where they hand over (never a black wall past the galaxies made in full)
                _web[0] = 0;
                }   // each a galaxy at its true size (a pinpoint from afar), faint, so the web shows where they crowd
            }
            Render.GpuLog.Pass = "galaxies";
            for (int i = 0; i < _galaxies.Count && !skyHidden; i++)
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
                // (its light at a third of a full point a pixel on the average: its dense heart glows, its thin rim fades to nothing, so
                // from afar it is a soft shape in its own colours with a fine edge, never a solid white disc or a white spiral icon)
                float crowd = (float)Math.Max(0.015, Math.Min(1, Math.PI * gpx * gpx / (count * dotPx * dotPx) * 0.35)) * nearF * (i == _near ? 1f : others);
                var pg = Placement(g); float drift = GalaxyDrift(g, ref pg);
                DrawPoints(buf, count, pg * vp, right, up, 0.006f * g.Scale * shrink * glow, i == _near ? 0.0016f : 0.0012f, w, h, time, 0.012f, crowd, InsideShare() > 0.5 && i == _near ? 2f : 0f, 0, 0, 0, 0, 0, drift);   // a galaxy's stars from afar are soft glows that add up to its light (hard discs would merge into a white outline); among them, points
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
                    if (drawn >= 12 || skyHidden) break; var g = _galaxies[kv.Value];
                    if (g.Features == null) { if (made) continue; EnsureStruct(g); made = true; }
                    if (g.StructCount > 0) { DrawPoints(g.Struct, g.StructCount, Placement(g) * vp, right, up, 1f, 0.0016f, w, h, time, 0, (float)Math.Max(0, Math.Min(1, (60 - kv.Key) / 20))); drawn++; }   // fading in from 60 to 40 of its sizes
                }
            }
            Render.GpuLog.Pass = "supernovae";
            DrawSupernovae(vp, right, up, w, h, time);
            Render.GpuLog.Pass = "black holes and bodies";
            UpdateExtras();
            DrawShadows(w, h);                                                          // every hole's shadow, black, at its real size from where you stand
            DrawBlackHoles(vp, right, up, w, h, time);                               // the feeding ones' discs and jets
                              // asteroids and comets close in: their own bodies
            {
                // the black hole that bends the most light on screen: its Einstein ring, sqrt(2 Rs / distance) radians (light from far
                // behind it), set for every point drawn
                _lens[2] = 0; double bestE = 0;
                foreach (var ex in _extras)
                {
                    if (!ex.Hole) continue; double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double rs = Units.Km(Physics.HorizonKm(ex.Mass)), thE = Math.Sqrt(2 * rs / Math.Max(d, rs)); if (thE < bestE) continue;
                    var cl = new float[4]; float px0 = (float)dx, py0 = (float)dy, pz0 = (float)dz;
                    float cx0 = px0 * vp.M11 + py0 * vp.M21 + pz0 * vp.M31 + vp.M41, cy0 = px0 * vp.M12 + py0 * vp.M22 + pz0 * vp.M32 + vp.M42, cw0 = px0 * vp.M14 + py0 * vp.M24 + pz0 * vp.M34 + vp.M44;
                    if (cw0 <= 0) continue;
                    bestE = thE; _lens[0] = cx0 / cw0; _lens[1] = cy0 / cw0; _lens[2] = (float)(thE / Math.Tan(0.45)); _lens[3] = cw0;
                }
            }
            if (_extras.Count > 0)
            {
                if (_extraV.Length < _extras.Count) _extraV = new StarVertex[_extras.Count + 256];
                int nv = 0;   // (only the bodies not drawn as their own stone: an empty vertex had stood in for each drawn one, size nothing at the camera itself, and the card projected it into a white speckled disc on the screen, the ghosts seen beside a rock)
                for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (ex.Shape.Length > 0 && ShapeShown(ex, h)) continue;   // (drawn as its own body: no point of light over it)
                    if (ex.Hole)
                    {
                        // its glow from afar (so it can be found and picked), fading out within a hundred radii as its real disc, shadow and lens take over
                        double hdx = RelX(ex) - _ox, hdy = RelY(ex) - _oy, hdz = RelZ(ex) - _oz, hrs = ex.Mass > 0 ? Units.Km(Physics.HorizonKm(ex.Mass)) : ex.R / 2.6;
                        float farF = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(hdx * hdx + hdy * hdy + hdz * hdz) / Math.Max(1e-300, hrs) - 100) / 100)) * (ex.Feeding > 0 ? 0.7f : 0.25f);
                        _extraV[nv++] = new StarVertex { X = (float)hdx, Y = (float)hdy, Z = (float)hdz, Size = (float)(ex.R * 3), R = ex.Cr * farF, G = ex.Cg * farF, B = ex.Cb * farF, A = 1 }; continue;
                    }
                    _extraV[nv++] = new StarVertex { X = (float)(RelX(ex) - _ox), Y = (float)(RelY(ex) - _oy), Z = (float)(RelZ(ex) - _oz), Size = (float)ex.R, R = ex.Cr, G = ex.Cg, B = ex.Cb, A = 1 }; }
                if (_extraBuf == null || _extraBuf.Bytes < (long)_extras.Count * Stride) { if (_extraBuf != null) _extraBuf.Dispose(); _extraBuf = new UploadBuffer(_gpu.Device, (long)(_extras.Count + 256) * Stride); }
                if (nv > 0) { _extraBuf.Write(_extraV, nv); DrawPoints(_extraBuf, nv, vp, right, up, 1f, 0.0024f, w, h, time); }
            }
            Render.GpuLog.Pass = "a cluster's stars";
            if (_cstars.Count > 0 && !skyHidden)
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
                // its light, never more than it has: from afar its thousands of stars would stack into a solid white ball with a hard
                // rim, so each is dimmed by how many share the cluster's patch of screen (as the galaxies' stars are); among them, points
                double ccx = (_cstarCX - _ax) - _ox, ccy = (_cstarCY - _ay) - _oy, ccz = (_cstarCZ - _az) - _oz, ccd = Math.Max(1e-12, Math.Sqrt(ccx * ccx + ccy * ccy + ccz * ccz));
                double cpx = _cstarR / ccd * h / (2 * Math.Tan(0.45)), cdot = Math.Max(1.5, 0.0016 * h);
                float ccrowd = (float)Math.Max(0.03, Math.Min(1, Math.PI * cpx * cpx / (_cstars.Count * cdot * cdot) * 0.35));
                DrawPoints(_cstarBuf, _cstars.Count, vp, right, up, 1f, 0.0016f, w, h, time, 0f, ccrowd);
            }
            Render.GpuLog.Pass = "the stars round you";
            if (_near >= 0 && _localCount > 0 && inside > 0 && !skyHidden)
            {
                // the real stars of this space at their real sizes (a small point from afar, never smaller than a dot on screen)
                // and only while the view is near their own size: wider than a few times the reach they are made in, the galaxy's
                // own field stands for them (a block of thousands round the eye, each a dot, would be a fuzz of light at those views)
                double viewLy = ViewDistanceKm() / 9.4607e12, reachLy = LocalReach * 50000.0, far = Math.Max(0, Math.Min(1, (viewLy / reachLy - 3) / 12));
                float lf = (float)(inside * inside * (3 - 2 * inside) * (1 - far * far * (3 - 2 * far)));
                if (lf > 0.003f)
                {
                    if (Math.Abs(lf - _localLf) > 0.002f || _localVersion != _localWritten || _localCount != _localWrittenN)   // (put on the card again only when they change)
                    {
                        for (int k = 0; k < _localCount; k++) { var v = _localList[k]; v.A = 1; v.R *= lf; v.G *= lf; v.B *= lf; if (_localDead.Contains(k)) { v.R = 0; v.G = 0; v.B = 0; } _localDrawn[k] = v; }   // (a star that died in a supernova: gone)
                        _localBuf.Write(_localDrawn, _localCount); _localLf = lf; _localWritten = _localVersion; _localWrittenN = _localCount;
                    }
                    // placed from the list's origin: its place in the universe less the camera's worked out in double, so the stars
                    // round you are exact on the card (the galaxy's own frame in float snapped them to a lattice 190 AU wide)
                    var pm = Placement(_galaxies[_near]); double oux, ouy, ouz; ToUniverse(_galaxies[_near], _localOX, _localOY, _localOZ, out oux, out ouy, out ouz);
                    pm.M41 = (float)((oux - _ax) - _ox); pm.M42 = (float)((ouy - _ay) - _oy); pm.M43 = (float)((ouz - _az) - _oz);
                    DrawPoints(_localBuf, _localCount, pm * vp, right, up, 1f, 0.0022f, w, h, time);
                }
            }
            Render.GpuLog.Pass = "the star system";
            if (_system != null)
            {
                // the system around its star (the anchor): orbits and belts, then the star, planets and moons as glows at their real sizes
                _gpu.ClearDepth();
                PlaceSystem();
                var st = _system.Static;
                float stFade = (float)Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));   // orbits and belts fade in with the planets
                // the belts' haze is rubble on its orbits: each point turns about the star at Kepler's rate for its distance (the
                // orbit lines and the Oort cloud stand); the whole haze had stood still
                var sr = _system.StaticRate; double yrs = _simYears;
                for (int i = 0; i < st.Length; i++)
                {
                    var v = st[i]; double px0 = st[i].X, pz0 = st[i].Z; float rate = sr != null && i < sr.Length ? sr[i] : 0;
                    if (rate != 0) { double th = (rate * yrs) % (2 * Math.PI), ct = Math.Cos(th), snt = Math.Sin(th); px0 = st[i].X * ct - st[i].Z * snt; pz0 = st[i].X * snt + st[i].Z * ct; }
                    v.X = (float)(px0 - _ox); v.Y = (float)(st[i].Y - _oy); v.Z = (float)(pz0 - _oz); v.R *= stFade; v.G *= stFade; v.B *= stFade; _staticShifted[i] = v;
                }
                // the belt's points dimmed by how many share its patch of screen (as a galaxy's stars are): from afar three thousand
                // dots in a thin ring added up to a solid white annulus, a machined pattern round the star; a belt is a haze
                double belt = 1;
                if (_system.BeltOutAu > 0)
                {
                    double dsys = Math.Max(1e-30, DistToSysStar()), pxPer = h / (2 * Math.Tan(0.45)) / dsys, ro = Units.Au(_system.BeltOutAu) * pxPer, ri = Units.Au(_system.BeltInAu) * pxPer, dotPx = Math.Max(1.5, 0.0012 * h);
                    belt = Math.Max(0.08, Math.Min(1, Math.PI * (ro * ro - ri * ri) / (3000 * dotPx * dotPx) * 0.5));
                }
                if (st.Length > 0) { _sysStatic.Write(_staticShifted, st.Length); DrawPoints(_sysStatic, st.Length, vp, right, up, 0f, 0.0012f, w, h, time, 0f, (float)belt); }
                for (int i = 0; i < _bodyCount; i++) { _bodyVerts[i].X = (float)(_bodyVerts[i].X - _ox); _bodyVerts[i].Y = (float)(_bodyVerts[i].Y - _oy); _bodyVerts[i].Z = (float)(_bodyVerts[i].Z - _oz); }
                // the planet whose real ground is showing is not drawn as a glow too (it showed through its own see-through sea and clouds as a frosted ghost)
                // the system's planets and moons fade in as you close in on its star (from 2,000 AU to 500 AU), never all at once
                double sysFade = Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));
                for (int i = 1; i < _bodyCount; i++) { _bodyVerts[i].R *= (float)sysFade; _bodyVerts[i].G *= (float)sysFade; _bodyVerts[i].B *= (float)sysFade; }
                if (_ground != null && _planet >= 0 && _planet + 1 < _bodyCount)   // (a small body's world has no glow among the system's bodies: its slot is not a planet's; indexed at slot minus one it had crashed the first frame its ground was made)
                {
                    // its glow fades into its real ground as you come in (from 40 to 20 of its widths), never swapped
                    double gx0, gy0, gz0; CamFromPlanet(out gx0, out gy0, out gz0);
                    float gk = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(gx0 * gx0 + gy0 * gy0 + gz0 * gz0) / _ground.Radius - 20) / 20));
                    _bodyVerts[_planet + 1].R *= gk; _bodyVerts[_planet + 1].G *= gk; _bodyVerts[_planet + 1].B *= gk; if (gk <= 0) _bodyVerts[_planet + 1].Size = 0;
                }
                _sysBodies.Write(_bodyVerts, _bodyCount);
                DrawPoints(_sysBodies, _bodyCount, vp, right, up, 1f, 0.004f, w, h, time);
                // close enough to see its disc: the star's real body at its real size
                double R = StarRadiusNow, sd = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz); float swellDim = (float)(1 / (StarSwell * StarSwell));   // (blown apart: thinner, dimmer)
                if (_starSurf != null && R / Math.Max(1e-30, sd) > 0.004)
                {
                    var toEye = new Vec3((float)(_ox / sd), (float)(_oy / sd), (float)(_oz / sd));
                    int n = _starSurf.Draw(toEye, time, _starSurfVerts, 0);
                    _surfBuf.Write(_starSurfVerts, n);
                    var m = Mat4.Identity; m.M11 = m.M22 = m.M33 = (float)R; m.M41 = (float)-_ox; m.M42 = (float)-_oy; m.M43 = (float)-_oz;
                    DrawPoints(_surfBuf, n, m * vp, right, up, 1f, 0f, w, h, time, 0, swellDim);
                    DrawPoints(_coronaBuf, _starCorona.Length, m * vp, right, up, 1f, 0f, w, h, time, 0, swellDim);
                }
                {
                    // inside the star: nothing is seen but its own glow, in the colour of the heat of the layer you are in (the plasma's own
                    // picture takes over when you dive into it)
                    var sl = StarLayerHere();
                    if (sl != null && _spaceScale <= 0) { float gr, gg, gb; StarGlow(LayerTempK(sl, sd / Math.Max(1e-30, R)), out gr, out gg, out gb); _hud.DrawFill(_meshes, w, h, gr, gg, gb); _gpu.ClearDepth(); }
                }
            }
            // the solid small bodies (asteroids, comets' nuclei) and worlds' rings last of everything in space: after every star,
            // belt and orbit behind them, so nothing shows through a solid body (the glows are drawn without looking at what is in front)
            Render.GpuLog.Pass = "asteroids and comets";
            DrawNebulae(vp, right, up, w, h, time);                                   // the nebulae near you, each a cloud of its own gas (its far marker stands for it beyond forty of its radii)
            DrawSmallBodies(vp, right, up, w, h, time);
            DrawRings(vp, right, up, w, h, time);                                       // the rings of the worlds that have them
            Render.GpuLog.Pass = "a world's ground";
            if (_ground != null && (_matterScale <= 0 || _matterScale > 1e-7))   // (the ground still drawn behind the matter down to a tenth of a millimetre, so the hand-over to the grains is no cut; below that the grain's own face fills the view)
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
                var sd = _system != null ? _system.StarRadius : 0; double lx = -_px, ly = -_py, lz = -_pz, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                if (ll > 0)
                {
                    // the starlight's way, turned back by the planet's own spin, so its days pass over the ground
                    double sp = -SpunSince, c0 = Math.Cos(sp), s0 = Math.Sin(sp), sx = lx / ll, sz = lz / ll;   // (its day where its star really is: lit on the side facing it, turning by its spin)
                    _ground.StarDir[0] = sx * c0 - sz * s0; _ground.StarDir[1] = ly / ll; _ground.StarDir[2] = sx * s0 + sz * c0;
                    if (Settings.Get("always-day", true))
                    {
                        // always day (Time menu): the star high over wherever you are (sixty degrees up, so the ground still shows its
                        // shape), turned back by the spin as the real starlight is
                        double cr = Math.Sqrt(x * x + y * y + z * z), ux = x / cr, uy = y / cr, uz = z / cr;
                        double ex = -uz, ez = ux, el = Math.Sqrt(ex * ex + ez * ez); if (el < 1e-6) { ex = 1; ez = 0; el = 1; }
                        double dx = ux * 0.87 + ex / el * 0.5, dy = uy * 0.87, dz = uz * 0.87 + ez / el * 0.5, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        dx /= dl; dy /= dl; dz /= dl;
                        _ground.StarDir[0] = dx * c0 - dz * s0; _ground.StarDir[1] = dy; _ground.StarDir[2] = dx * s0 + dz * c0;
                    }
                }
                GroundLight();
                // what the ground needs to detail: only what is in view (the view's half-angle, corner to corner)
                var lk = Look; if (_ground.ViewDir == null) _ground.ViewDir = new double[3];
                _ground.ViewDir[0] = lk.X; _ground.ViewDir[1] = lk.Y; _ground.ViewDir[2] = lk.Z;
                _ground.ViewHalfAngle = Math.Atan(Math.Tan(0.45) * Math.Sqrt(1 + (w / (double)Math.Max(1, h)) * (w / (double)Math.Max(1, h))));
                PlanetTerrain.Power = Window.Detail;
                _ground.SandOnCard = true;                                                // (the grains worked out per pixel at every height: on the raised pieces close in too, looked up at the bed's level, so their domes shade smooth, never in steps)
                if (InsideGiant)
                {
                    // inside a giant, below its cloud tops: no shell to see from in here, only its own gas all round, its air's
                    // colour, dimming as you go deeper (sunlight dies away within a few hundred km), lit on its day side
                    double cr = Math.Sqrt(x * x + y * y + z * z), depth = Math.Max(0, _ground.Radius + _ground.CloudKm - cr);
                    double day = cr > 0 ? Math.Max(0.15, Math.Min(1, (x * _ground.StarDir[0] + y * _ground.StarDir[1] + z * _ground.StarDir[2]) / cr * 1.5 + 0.4)) : 1;
                    float lit = (float)(Math.Exp(-depth / 300.0) * day), deep = 0.05f;
                    _hud.DrawFill(_meshes, w, h, _ground.AirR * (deep + 0.55f * lit), _ground.AirG * (deep + 0.55f * lit), _ground.AirB * (deep + 0.55f * lit));
                }
                else
                {
                    // under the sea: the water all round in its own colour, the light dying away with depth (sunlight is gone
                    // within a few hundred metres), everything beyond about thirty metres lost in it; the sea bed drawn through it
                    double cr = Math.Sqrt(x * x + y * y + z * z), seaAlt = cr - _ground.Radius; float fogKm = 0, fr = 0, fg = 0, fb = 0;
                    if (_underSea && seaAlt < 0)
                    {
                        double lit = Math.Exp(-(-seaAlt * 1000) / 80.0) * 0.9 + 0.03;
                        fr = (float)(0.08 * lit); fg = (float)(0.28 * lit); fb = (float)(0.42 * lit); fogKm = 0.03f;
                        _hud.DrawFill(_meshes, w, h, fr, fg, fb); _gpu.ClearDepth(); _ground.Dim = (float)lit;
                    }
                    else _ground.Dim = 1;
                    {
                        // the ground's pieces split no finer than four grains to an edge (a cell a quarter of a grain: the finest the land's
                        // shape goes, the grains themselves take over below that, drawn over the ground from a centimetre up and as the matter
                        // below): it split on down to microbe size under the overlay, a queue of a thousand pieces in flight for detail the
                        // grains already covered, and the frame rate went with it
                        var gmx = _ground.Solid && cr > 0 ? _ground.GrainMix(y / cr) : null;
                        _ground.FinestEdgeKm = gmx != null && gmx.Size > 0 ? Math.Max(1e-7, gmx.Size * 2) : 0;
                    }
                    _ground.Draw(_meshes, x, y, z, view, kproj, time, 3.0 * Math.Max(1, h / 600.0) * Math.Max(0.3, Math.Min(3, Math.Sqrt(Window.Detail))), fogKm, fr, fg, fb, 0, 0, w, h, 400);   // detail scales with the GPU use
                }
            }
            _matterCardDrawn = false;
            _hud.CardTop = _tour != null ? (float)(h * 0.1 + 4) : 4; _hud.CardBottom = _tour != null ? (float)(h * 0.9 - 4) : (float)(h * 0.66);   // (never more than two thirds of the screen: a long card is cut short)   // (every card between the tour's bars: a tall one is cut short, never pushed up under the top bar)
            double overAlt = _ground != null && _matterScale <= 0 ? AboveGround() / Units.Km(1) : 1e30;
            if (overAlt < MatterView.OverlayKm)
            {
                // a centimetre over the ground: its grains begin to show, lying over it, specks that grow as you come down
                SetMatter(); _gpu.ClearDepth();
                Mat4 ov = MatterFrame(look);
                _matter.Floor = false; MatterDrawTimed(ov, Math.Max(MatterView.BottomKm, overAlt / 0.6), w, h, _matterClock, _meshes, true);   // (the planet's ground is the floor here)
                {
                    // a grain picked over the ground: ringed, with its card, as anything picked anywhere
                    _gpu.ClearDepth(); double gqx, gqy, gqr;
                    if (_matter.PickedOnScreen(out gqx, out gqy, out gqr)) { _hud.DrawRing(_meshes, gqx, gqy, Math.Min(gqr, h * 0.45), w, h); if (_hud.Visible) { _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; } }   // (the card at the left edge, as every card: beside the ring it ran into the other cards)
                }
            }
            if (_spaceScale > 0)
            {
                // in a nebula's or cluster's gas: its atoms in front of space (its glow round you still drawn behind), and below a tenth
                // of a millimetre only the matter itself, no stars or galaxies behind (a draw distance for the very small)
                _gpu.ClearDepth();
                Mat4 sv;
                if (!_matter.InSpace && _bodyUp != null) sv = MatterFrameFor(new Vec3((float)_bodyUp[0], (float)_bodyUp[1], (float)_bodyUp[2]), new Vec3((float)_bodyStar[0], (float)_bodyStar[1], (float)_bodyStar[2]), look);   // on a small body's ground: its own up and its star's light, as on a world's
                else
                {
                    if (_matter.SpaceKind == 12) { _hud.DrawFill(_meshes, w, h, 0, 0, 0); _gpu.ClearDepth(); }   // inside a black hole: pitch black, all the way down, the well's grid the one reference
                    _matter.Light[0] = 0.4f; _matter.Light[1] = 0.7f; _matter.Light[2] = 0.6f;
                    var slk = Look; _matter.SetEye(slk.X, slk.Y, slk.Z);                     // in the gas too: your eye goes round the point you dive into
                    sv = Mat4.LookAt(new Vec3(0, 0, 0), slk, ViewUp());                // (the camera's own up: never flipping)
                }
                _matter.Floor = _matter.InSpace ? false : _spaceScale <= 1e-7;             // (a body's ground: its face is the floor only once you are down at it; in space or a hole, none: it had been drawn over the fall's grid)
                MatterDrawTimed(sv, _spaceScale, w, h, _matterClock, _meshes, false);
                if (_matter.SpaceKind == 12) { _gpu.ClearDepth(); DrawFallGrid(vp, right, up, w, h, time); }   // the fall's well, over everything but the ring and the card
                _gpu.ClearDepth();
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);
                if (_hud.Visible) { _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; }   // (on the tour below its top bar, which had hidden the card's first lines)
            }
            if (_matterScale > 0)
            {
                // the matter, in front of everything; above a tenth of a millimetre the ground still shows through behind it
                _gpu.ClearDepth();
                Mat4 mview = MatterFrame(look);
                _matter.Floor = _matterScale <= 1e-7;                                      // (its own floor only once the planet's ground is no longer drawn behind)
                MatterDrawTimed(mview, _matterScale, w, h, _matterClock, _meshes, false);
                _gpu.ClearDepth();                                                     // its ring and card on top of the grains
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);   // what you picked, ringed
                if (_hud.Visible) { _hud.DrawCard(_meshes, _matter.Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; }   // (on the tour below its top bar, which had hidden the card's first lines)   // what you are in, and what you picked
            }
            _gpu.ClearDepth();                                                         // the scale, cards and rings always on top of everything
            DrawScale(w, h);
            // (no graph of the well on screen: the well itself is drawn round you inside, and the card carries the numbers)   // a black hole picked and near, or the fall into it: its gravity well graphed, and where you are on it
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
                    _hud.CardTop = 4; _hud.CardBottom = (float)(h * 0.66); _hud.DrawCard(_meshes, lines, w * 0.3, h * 0.9, w, h, cap);   // to the right of the scale (the caption alone sits in the lower bar)
                }
                _gpu.ClearDepth();                                                     // the cards and the frames a second on top of the bars
                // what is in view and in focus, written as you go, just below the top bar
                // (the matter's card is drawn once, at the left edge, on the tour as off it: a second drawing here drew over the first, a garble)
            }
            else if (_pausedTour != null)
            {
                // a tour you took the controls from, or one that has ended: it says so, why, and how to go on
                var lines = new List<string>();
                if (_pausedWhy == null) { lines.Add("The tour is over"); lines.Add("R: watch it again    Backspace: close this"); }
                else
                {
                    string at = _pausedAt < _pausedTour.Count ? _pausedTour[_pausedAt].Caption : "";
                    lines.Add("Tour paused: " + _pausedWhy);
                    if (at.Length > 0) lines.Add("at: " + at);
                    lines.Add("T: carry on    R: start again    Backspace: close this");
                }
                _hud.DrawCard(_meshes, lines, w * 0.3, h * 0.86, w, h);
            }
            if (Loading) { _gpu.ClearDepth(); _hud.DrawLoading(_meshes, w, h, _loadShare, _loadLines ?? new[] { "Making the universe" }); }   // (the start's loading screen over everything)
            if (Settings.Get("show-fps", true))                                       // frames a second, top right (View > Show FPS)
            {
                // and with it, when asked (View > Show GPU and CPU stats): the processor's and the card's time a frame, the
                // processor's share of every core, the memory, the card's own memory and how busy it is; the card sized to its
                // longest line and flush with the right edge
                bool stats = Settings.Get("show-stats", true);
                var sl = new List<string> { Window.Fps.ToString("0") + " FPS" + (stats && Window.Fps > 0 ? ", " + (1000 / Window.Fps).ToString("0.0") + " ms a frame" : "") };
                if (stats) sl.AddRange(Stats.Lines);
                int longest = 0; foreach (var l in sl) longest = Math.Max(longest, l.Length); longest = Math.Min(longest, _hud.WrapAt);   // (the card wraps its lines: never wider than a wrapped line, else a long line pushed it left over the pick's card)
                double spx = Math.Max(1, h / 540.0), scw = longest * 6.6 * spx + 12 * spx;
                double sy0 = _tour != null ? h * 0.11 : 12 * Math.Max(1, h / 1080.0);
                _hud.DrawCard(_meshes, sl, w - scw - 8 * spx, sy0, w, h);
                _statsLeft = w - scw - 8 * spx; _statsBottom = sy0 + sl.Count * 11 * spx + 12 * spx;   // (where the stats sit: a pick's card never lands on them)
            }
            else { _statsLeft = w; _statsBottom = 0; }
            double mdx, mdy, mdz, mrad, msx, msy;
            if (_tKind >= 0 && _matterScale <= 0 && _spaceScale <= 0 && TargetFromCamera(out mdx, out mdy, out mdz, out mrad) && ToScreen(mdx, mdy, mdz, out msx, out msy))   // (in a dive, the matter's card alone: the two drawn together garbled each other)
            {
                double l = Math.Sqrt(mdx * mdx + mdy * mdy + mdz * mdz);
                double rpx = Math.Max(14 * Math.Max(1, h / 1080.0), mrad / Math.Max(1e-30, l) / Math.Tan(0.45) * h * 0.5 * 1.25);
                bool fills = rpx > h * 0.45;                                           // bigger than the view: no ring, the card sits top left
                if (!fills) _hud.DrawRing(_meshes, msx, msy, rpx, w, h);
                // its card at the left edge, under the top, never over what you look at (beside the ring it sat across the middle of the
                // view, a wall of text over the hole); on the tour too, where the caption at the foot says what the shot is and the card what the thing is
                if (_hud.Visible && !_matterCardDrawn) _hud.DrawCard(_meshes, Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 72 * Math.Max(1, h / 1080.0), w, h);   // (one card at a time: when the matter's is up, the world's waits; on the tour below its top bar)
            }
        }

        // the scale on screen: how many km one pixel spans at the distance of the nearest thing, in the middle of the view
        // how far away what you are looking at is (km), for the scale: in the gas or the matter, your own fine distance; flying to
        // a point in a nebula, the distance to that point (not to some star nearby); else the nearest thing
        // the matter's draw, timed: its part of the frame's processor time (on the stats card with the ground's)
        private double _matterMs;
        private void MatterDrawTimed(Mat4 view, double scaleKm, int w, int h, double time, MeshPipes pipes, bool overlay) { var sw = System.Diagnostics.Stopwatch.StartNew(); _matter.Draw(view, scaleKm, w, h, time, pipes, overlay); _matterMs = sw.Elapsed.TotalMilliseconds; }
        /// <summary>The processor's part of the frame by phase: the ground's pieces chosen, drawn and taken in, the matter; for the stats card.</summary>
        public string CpuPhases { get { return (_ground != null && _ground.FrameStats != null ? _ground.FrameStats + ", " : "") + "matter " + _matterMs.ToString("0.0") + " ms, bodies " + _extras.Count; } }
        private double ViewDistanceKm()
        {
            if (_spaceScale > 0) return _spaceScale * 0.6;
            if (_matterScale > 0) return _matterScale * 0.6;
            double qx, qy, qz, qr;
            if (_tKind == 5 && TargetFromCamera(out qx, out qy, out qz, out qr)) return Math.Max(1e-30, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1));
            // on the tour, how far it is to what the tour goes to (the picked world's ground; anything else picked, itself), never
            // just the nearest thing (beside a black hole every other zoom would think itself already there)
            if (_tour != null && AtGroundBody()) return Math.Max(1e-30, AboveGround()) / Units.Km(1);
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
        private int _comaN, _tailN; private UploadBuffer _tailBuf;
        private void DrawPoints(UploadBuffer buf, int count, Mat4 vp, Vec3 right, Vec3 up, float sizeScale, float minSize, int w, int h, double time, float maxSize = 0, float bright = 1, float style = 2, float holeK = 0, float holeMode = 0, float hx = 0, float hy = 0, float hz = 0, float drift = 0)
        {
            if (buf == null || count <= 0) return;
            float b = bright * _sky; if (b <= 0.003f) return;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = sizeScale; c[26] = style; c[27] = 4; c[28] = w; c[29] = h; c[30] = minSize; c[31] = maxSize; c[32] = 1 - Math.Min(1, b); c[34] = drift;   /* (gravity off: how far a galaxy's stars have flown, in their own frame) */
            c[36] = _lens[0]; c[37] = _lens[1]; c[38] = _lens[2]; c[39] = _lens[3]; c[40] = w / (float)Math.Max(1, h);
            c[43] = _web[0]; c[44] = _web[1]; c[45] = _web[2]; c[46] = _web[3]; c[47] = _web[4];   // the web's light, sifted on the card (0: not the web)
            if (holeK > 0) { c[33] = holeK; c[35] = holeMode; c[43] = 0; c[44] = hx; c[45] = hy; c[46] = hz; c[47] = 0; }   // a feeding black hole's parts: moved on the card in its own frame (the pace, which part, where the camera is in that frame)
            _dots.Draw(buf, count, c, 0, 0, w, h);
        }

        // ── the information card of what you picked: what it is, its id, and the facts that matter about it ──
        private static string Au(double units) { double au = units / Units.Au(1); return au >= 10 ? au.ToString("0") + " AU" : au.ToString("0.##") + " AU"; }
        private static string Km(double units) { double km = units / Units.Km(1); return km >= 10 ? km.ToString("N0") + " km" : km.ToString("0.##") + " km"; }

        // what a kind of planet or moon is made of
        private static string[] MakeUp(SpaceGen.Body b, bool water)
        {
            // from its kind's template (content/terrain.unt makeup, and makeup-dry for one with no seas); a moon is rock and dust
            if (b.MakeUp != null) return !water && b.MakeUpDry != null ? b.MakeUpDry : b.MakeUp;
            if (b.Parent >= 0) return new[] { "rock and dust, iron-poor", "no air", "craters from strikes" };
            return new[] { "rock: silicate crust, iron core", "dust and stone", "air: thin carbon dioxide" };
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
                string f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), PreviewText.AppName.ToLowerInvariant().Replace("-", "") + "_zoom.log");   // (the program's own name: universe_zoom.log)
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 4000000) System.IO.File.Delete(f);   // (a whole tour fits: it is the record of what the tour did)
                System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.f") + "  " + what + "  |  " + Title.Replace(PreviewText.AppName + " · ", "").Split(new[] { " · wheel:" }, StringSplitOptions.None)[0] + Environment.NewLine);
            }
            catch { }
        }

        // the light on the ground: by day the star's own colour (from its temperature) through the air, which takes a little of
        // its own colour out of the direct light the thicker it is (a blue sky leaves the light warmer); by night half as bright,
        // in the colour of the air's glow and scattered starlight (airless worlds: the starlight alone)
        private void GroundLight()
        {
            float sr = _system != null ? _system.SR : 1, sg = _system != null ? _system.SG : 1, sb = _system != null ? _system.SB : 1, m = Math.Max(1e-3f, Math.Max(sr, Math.Max(sg, sb)));   // (a rogue body with no star of its own: lit white, by the galaxy's light)
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
        private double[] _bodyUp, _bodyStar;                                              // on a small body's ground: its up (from its middle to you) and the way to its star
        private Mat4 MatterFrame(Vec3 look)
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            return MatterFrameFor(new Vec3((float)(x / r), (float)(y / r), (float)(z / r)), new Vec3((float)_ground.StarDir[0], (float)_ground.StarDir[1], (float)_ground.StarDir[2]), look);
        }
        // the matter's frame on any ground (a world's, a small body's): up from the middle to you, the starlight from sd
        private Mat4 MatterFrameFor(Vec3 upR, Vec3 sd, Vec3 look)
        {
            var east0 = Vec3.Cross(Math.Abs(upR.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upR).Normalized; var north0 = Vec3.Cross(upR, east0).Normalized;
            var upL = upR;                                                              // (the grains themselves lie along the land's slope)
            var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
            var lk = new Vec3(Vec3.Dot(look, east), Vec3.Dot(look, upL), Vec3.Dot(look, north));
            // in the matter you look down onto the grain's face (it fills the whole view, never its edge and the dark past it); over
            // the ground, never from below it
            float lowest = _matterScale > 0 || _spaceScale > 0 ? -0.7f : -0.15f;
            if (lk.Y > lowest) { float hl = (float)Math.Sqrt(lk.X * lk.X + lk.Z * lk.Z); float keep = (float)Math.Sqrt(1 - lowest * lowest) / Math.Max(1e-6f, hl); lk = hl > 1e-6f ? new Vec3(lk.X * keep, lowest, lk.Z * keep) : new Vec3(0, -1, 0); }
            _matter.Light[0] = Vec3.Dot(sd, east); _matter.Light[1] = Vec3.Dot(sd, upL); _matter.Light[2] = Vec3.Dot(sd, north);
            var lkn = lk.Normalized;
            if (_matterScale > 0 || _spaceScale > 0) _matter.SetEye(lkn.X, lkn.Y, lkn.Z); else _matter.SetEye(0, -1, 0);   // in the matter your eye goes round the point you dive into
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
                { var sc = Supernovae(g, _tIndex == _near); int going = 0; foreach (var sn in sc.Events) { double yrs = _simYears - sn.T; if (yrs >= 0 && yrs * 365.25 < 150) going++; } L.Add("supernovae: about " + (SnRate(g) * 100).ToString("0.#") + " a century here" + (going > 0 ? "; " + going + " going now" : "; none going now")); }
                if (_darkMatter > 0) { L.Add("mass: about " + (1e11 / (1 - _darkMatter)).ToString("0.#E+0") + " solar masses, " + (_darkMatter * 100).ToString("0") + "% of it dark matter"); L.Add("its stars orbit at a flat " + _rimSpeed.ToString("0") + " km/s out to its rim, held by the dark halo (without it they would slow outward like planets)"); }
            }
            else if (_tKind == 1 && _tSn != null && _tName == "a supernova") SupernovaCard(L, dist);
            else if (_tKind == 1)
            {
                bool open = _system != null && Math.Abs(_tx - _ax) + Math.Abs(_ty - _ay) + Math.Abs(_tz - _az) < Units.Km(1e6);
                L.Add(_tName.StartsWith("a ") ? _tName.Substring(2) : _tName);
                L.Add("id: S-" + (((long)(_tx / Units.Km(1e9)) * 73856093L ^ (long)(_ty / Units.Km(1e9)) * 19349663L ^ (long)(_tz / Units.Km(1e9)) * 83492791L) & 0xFFFFFF).ToString("X6"));
                {
                    // what it is made of, by count of atoms, its own share of the heavier elements (content/stars.unt makeup)
                    var ke = StarKindEntry(_tName.StartsWith("a ") ? _tName.Substring(2) : _tName);
                    if (ke != null) { double metals; string mk = StarMakeUp(ke, _tx, _ty, _tz, out metals); if (mk != null) { var mp = mk.Split(','); L.Add("made of (by atoms): " + string.Join(",", mp, 0, Math.Min(6, mp.Length)) + (mp.Length > 6 ? ", and " + (mp.Length - 6) + " more" : "")); if (ke.Get("makeup", "").StartsWith("hydrogen")) L.Add("past helium " + metals.ToString("0.##") + " times its kind's share; its colour and heat from its mass, not its make-up"); } if (ke.Props.ContainsKey("makeup-note")) L.Add(ke.Get("makeup-note", "")); }
                }
                if (open)
                {
                    var sy = _system;
                    L.Add("surface: " + sy.TempK.ToString("N0") + " K"); L.Add("mass: " + sy.MassSol.ToString("0.##") + " solar masses, width " + sy.RadiusSol.ToString("0.##") + " solar radii");
                    L.Add("brightness: " + sy.Luminosity.ToString(sy.Luminosity < 0.1 ? "0.###" : "0.##") + " solar luminosities");
                    {
                        // its light climbing out of its own well (gravitational redshift, GM/Rc^2 for a star), and its layers from the surface to the core
                        double gmS = Physics.GmSol * sy.MassSol, RS = Math.Max(1, sy.StarRadius / Units.Km(1) * 1000), zS = 1 / Math.Sqrt(Math.Max(1e-30, 1 - 2 * gmS / (RS * Physics.C * Physics.C))) - 1;
                        L.Add("relativity: its light redshifted " + ShiftWords(zS) + " climbing out; surface clocks slower by the same");
                        var lays = StarLayersFor(SystemStarId()); if (lays.Count > 0) { var nm = new List<string>(); foreach (var l in lays) nm.Add(l.Name.ToLowerInvariant()); L.Add("layers: " + string.Join(", ", nm.ToArray())); }
                        var sl = StarLayerHere(); if (sl != null) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) / Units.Km(1), RK = StarRadiusNow / Units.Km(1); L.Add("inside: " + ScaleHud.Words(RK - sdK) + " down, its " + sl.Name.ToLowerInvariant() + ", about " + LayerTempK(sl, sdK / RK).ToString("N0") + " K, " + DensityWords(LayerDensity(sl, sdK / RK))); }
                        if (!Window.GravityOn) L.Add("gravity off " + Years(Math.Max(0, _simYears - _gravityOffYear)) + ": " + (StarSwell >= 100 ? StarSwell.ToString("N0") : StarSwell.ToString("0.#")) + " times its size, growing " + (StarSwellSpeedKmS()).ToString("N0") + " km/s");
                    }
                    L.Add("planets and moons: " + sy.Bodies.Count + " (" + sy.Planets + " planets)");
                    L.Add("habitable zone: " + (sy.HabitableAu * 0.75).ToString("0.##") + " to " + (sy.HabitableAu * 1.4).ToString("0.##") + " AU");
                    L.Add("frost line: " + sy.FrostLineAu.ToString("0.##") + " AU");
                    { string sc = ScorchWords(_tx, _ty, _tz); if (sc != null) L.Add("scorched: " + sc); }
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
                {
                    // its mass (from its size and its kind's density), its pull at its ground and the speed to get away from it
                    double rwM = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), gmw = Physics.G * b.MassKg, gS = gmw / (rwM * rwM), vE = Math.Sqrt(2 * gmw / rwM);
                    L.Add("mass: " + MassWords(b.MassKg));
                    L.Add("ground pull " + gS.ToString("0.##") + " m/s2 (" + (gS / Physics.EarthG).ToString("0.##") + " of Earth's); escape " + (vE >= 1000 ? (vE / 1000).ToString("0.#") + " km/s" : vE.ToString("0") + " m/s"));
                }
                var top = b.Parent >= 0 ? sy.Bodies[b.Parent] : b;
                if (b.Parent < 0)
                {
                    double au = b.OrbitUnits / Units.Au(1);
                    L.Add("orbit: " + Au(b.OrbitUnits) + ", year " + (b.Period >= 1 ? b.Period.ToString("0.#") + " years" : (b.Period * 365.25).ToString("0") + " days"));
                    L.Add("its orbit: " + (b.Ecc < 0.05 ? "near round" : b.Ecc < 0.25 ? "oval" : "a long ellipse") + " (eccentricity " + b.Ecc.ToString("0.00") + "), from " + Au(b.OrbitUnits * (1 - b.Ecc)) + " to " + Au(b.OrbitUnits * (1 + b.Ecc)));
                    if (_tIndex + 1 < _bodyCount)
                    {
                        // its speed, by the laws of motion (vis-viva): now, from how far it is from its star this moment, and at its
                        // closest point; and relativity: its closest point turning round the star, and clocks slowed by the star's pull
                        double rx = _bodyVerts[_tIndex + 1].X - _bodyVerts[0].X, ry = _bodyVerts[_tIndex + 1].Y - _bodyVerts[0].Y, rz = _bodyVerts[_tIndex + 1].Z - _bodyVerts[0].Z;
                        double rM = Math.Max(1, Math.Sqrt(rx * rx + ry * ry + rz * rz) / Units.Km(1) * 1000), aM = b.OrbitUnits / Units.Km(1) * 1000, gm = Physics.GmSol * sy.MassSol;
                        double vNow = Math.Sqrt(Math.Max(0, gm * (2 / rM - 1 / aM))), vPeri = Math.Sqrt(gm * (1 + b.Ecc) / (aM * (1 - b.Ecc))), vApo = Math.Sqrt(gm * (1 - b.Ecc) / (aM * (1 + b.Ecc)));
                        L.Add("speed now: " + (vNow / 1000).ToString("0.#") + " km/s; " + (vPeri / 1000).ToString("0.#") + " at its closest, " + (vApo / 1000).ToString("0.#") + " at its farthest");
                        double arcsecCentury = b.PeriRate * (100 / Math.Max(1e-9, b.Period)) * 206264.8, slow = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gm / (rM * Physics.C * Physics.C)));
                        L.Add("relativity: its closest point turns " + (arcsecCentury < 3600 ? arcsecCentury.ToString("0.#") + " arcseconds" : (arcsecCentury / 3600).ToString("0.#") + " degrees") + " a century; clocks here run slower by 1 part in " + (slow > 0 ? (1 / slow).ToString("0.##E+0") : "(none)"));
                        {
                            // its own pull on clocks: slower on its ground than far away, and faster higher up, so a satellite's clock runs
                            // ahead of the ground's by so many microseconds a day (the correction every satellite clock carries)
                            double gmw = WorldGm(b), rw = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), c2 = Physics.C * Physics.C;
                            double sGround = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gmw / (rw * c2))), sSat = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gmw / (4.2 * rw * c2)));
                            double usDay = sGround * 86400e6, gain = (sGround - sSat) * 86400e6;
                            L.Add("its pull: ground clocks " + (usDay >= 10 ? usDay.ToString("0") : usDay.ToString("0.##")) + " us a day slow; a satellite 4 radii up " + (gain >= 10 ? gain.ToString("0") : gain.ToString("0.##")) + " us a day faster");
                        }
                        {
                            // the rest of it: its light climbing out of its own well and its star's reaches far away redshifted; its light swinging
                            // blue and red as it comes and goes round its star (Doppler); at its speed its length along its path shorter (special relativity)
                            double gmw = WorldGm(b), rw = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), c2 = Physics.C * Physics.C;
                            double z = 1 / Math.Sqrt(Math.Max(1e-30, 1 - 2 * gm / (rM * c2) - 2 * gmw / (rw * c2))) - 1, dop = vNow / Physics.C, gam = 1 / Math.Sqrt(Math.Max(1e-30, 1 - dop * dop)) - 1;
                            L.Add("redshift " + ShiftWords(z) + "; Doppler swing " + ShiftWords(dop) + "; contraction " + ShiftWords(gam));
                        }
                    }
                    L.Add(au >= sy.HabitableAu * 0.75 && au <= sy.HabitableAu * 1.4 ? "in the habitable zone" : au < sy.HabitableAu * 0.75 ? "inside the habitable zone: too hot" : "outside the habitable zone: too cold");
                    L.Add("moons: " + moons);
                    if (b.Locked) L.Add("tidally locked: one face always toward its star, its day as long as its year");
                }
                else L.Add("orbit: " + Km(b.OrbitUnits) + " from its planet" + (b.Ecc >= 0.05 ? ", " + (b.Ecc < 0.25 ? "oval" : "a long ellipse") + " (eccentricity " + b.Ecc.ToString("0.00") + ")" : ""));
                bool here = _ground != null && _planet == _tIndex;
                foreach (var m in MakeUp(b, here ? _ground.HasWater : PlanetTerrain.TerrainKind(b.Terrain).Water)) L.Add(m);
                { string sc = ScorchWords(_ax, _ay, _az); if (sc != null) L.Add("scorched: " + sc); }
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
                double massSol = exk.Mass > 0 ? exk.Mass : exk.Density > 0 && exk.R > 0 ? SpaceGen.MassOf(exk.R, exk.Density) / 1.989e30 : 0;   // (a small body's mass from its size and its density)
                if (massSol > 0)
                {
                    if (exk.Mass <= 0) L.Add("mass: " + (massSol * 1.989e30).ToString("0.##E+0") + " kg");
                    // its pull where you are, falling with the square of the distance; the speed you would need to get away
                    double rM = Math.Max(1, (dist + exk.R) / Units.Km(1) * 1000);                // metres
                    double g = Physics.Pull(massSol, rM), vesc = Physics.Escape(massSol, rM);     // (Newton's gravity, content/physics.unt)
                    L.Add("its pull here: " + (g >= 0.01 ? g.ToString("0.###") : g.ToString("0.##E+0")) + " m/s2 (" + (g / Physics.EarthG).ToString(g / Physics.EarthG >= 0.01 ? "0.###" : "0.##E+0") + " of Earth's)");
                    L.Add("escape speed here: " + (vesc >= Physics.C ? "faster than light: inside, nothing gets out" : vesc >= 1000 ? (vesc / 1000).ToString("N0") + " km/s (" + (vesc / Physics.C * 100).ToString("0.###") + "% of light)" : vesc.ToString("0.#") + " m/s"));
                    { double gmx = Physics.GmSol * massSol, q = 1 - 2 * gmx / (rM * Physics.C * Physics.C); L.Add("relativity: clocks here run " + (q <= 0 ? "to a stop at its horizon" : "slower by " + ((1 - Math.Sqrt(q)) * 100 >= 0.01 ? ((1 - Math.Sqrt(q)) * 100).ToString("0.##") + "%" : "1 part in " + (1 / (1 - Math.Sqrt(q))).ToString("0.##E+0")))); }
                    if (exk.Hole) { double gmx2 = Physics.GmSol * massSol, q2 = 1 - 2 * gmx2 / (rM * Physics.C * Physics.C); L.Add(q2 <= 0 ? "redshift from here: without limit at the horizon" : "redshift from here: " + ShiftWords(1 / Math.Sqrt(q2) - 1)); }
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
                L.Add("on: " + GroundKind());
                L.Add("place: " + (Math.Asin(_ty) * 180 / Math.PI).ToString("0.#") + " lat, " + (Math.Atan2(_tz, _tx) * 180 / Math.PI).ToString("0.#") + " long");
                L.Add(sea ? "depth: " + (-gh * 1000).ToString("N0") + " m of water" : "height: " + (gh * 1000).ToString("N0") + " m above the sea");
                L.Add(sea ? "water over sand and mud" : ice ? "ice and packed snow" : gh > 3 ? "bare rock and scree" : gh > 1 ? "rock and thin soil" : "soil, sand and stone");
                L.Add("distance: " + Units.Describe(dist));
            }
            return L;
        }

        // gravitational time dilation, where you are: your own clock against the clocks far from every mass (the star, the world you
        // are at, the holes near you): dtau/dt = root(1 - sum 2GM/(r c^2)); slower the deeper in a well, stopped at a horizon against
        // far clocks, and past it no far clock sees you at all. The world's own clock (its days, its orbits) runs on far time
        private double ClockFactorHere()
        {
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return 0;
            double sum = 0, c2 = Physics.C * Physics.C;
            if (_system != null) { double d = Math.Max(1, DistToSysStar() / Units.Km(1) * 1000); sum += 2 * Physics.GmSol * _system.MassSol / (d * c2); }
            if (_system != null && _planet >= 0 && _planet < _system.Bodies.Count) { double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Max(1, Math.Sqrt(x * x + y * y + z * z) * 1000); sum += 2 * WorldGm(_system.Bodies[_planet]) / (r * c2); }
            foreach (var ex in _extras) { if (!ex.Hole || ex.Mass <= 0) continue; double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy + dz * dz) / Units.Km(1) * 1000); sum += 2 * Physics.GmSol * ex.Mass / (d * c2); }
            return Math.Sqrt(Math.Max(0, 1 - sum));
        }
        // a world's GM from its own mass (its size and its kind's density, content/terrain.unt density-g-cc)
        private static double WorldGm(SpaceGen.Body b) { return Physics.G * b.MassKg; }
        private static string MassWords(double kg) { double e = kg / 5.972e24; return (e >= 100 ? e.ToString("N0") : e >= 1 ? e.ToString("0.#") : e >= 0.01 ? e.ToString("0.##") : e.ToString("0.#E+0")) + " Earths (" + kg.ToString("0.##E+0") + " kg)"; }
        private static string TurnsWords(double perSec)
        {
            if (perSec >= 1) return (perSec >= 100 ? perSec.ToString("N0") : perSec.ToString("0.#")) + " times a second";
            if (perSec * 60 >= 1) return (perSec * 60).ToString("0.#") + " times a minute";
            if (perSec * 3600 >= 1) return (perSec * 3600).ToString("0.#") + " times an hour";
            return "once every " + (perSec * 86400 >= 1 ? (1 / (perSec * 3600)).ToString("0.#") + " hours" : (1 / (perSec * 86400)).ToString("0.#") + " days");
        }
        // the innermost stable orbit round a spinning hole, going its way, in horizon radii (Bardeen, Press and Teukolsky 1972): 3 at rest, down to 0.62 near the most
        private static double IscoRs(double a)
        {
            a = Math.Max(0, Math.Min(0.9999, a));
            double z1 = 1 + Math.Pow(1 - a * a, 1 / 3.0) * (Math.Pow(1 + a, 1 / 3.0) + Math.Pow(1 - a, 1 / 3.0)), z2 = Math.Sqrt(3 * a * a + z1 * z1);
            return (3 + z2 - Math.Sqrt((3 - z1) * (3 + z1 + 2 * z2))) / 2;
        }
        private static string ShiftWords(double z) { return z <= 0 ? "not at all" : z >= 0.01 ? "a share of " + z.ToString("0.###") : "1 part in " + (1 / z).ToString("0.##E+0"); }
        private static string SlowWords(double f)
        {
            double slow = 1 - f; if (slow <= 0) return "the same as far away";
            return slow * 100 >= 0.01 ? "slower than far away by " + (slow * 100).ToString("0.##") + "%" : "slower than far away by 1 part in " + (1 / slow).ToString("0.##E+0");
        }
        private string ClockWords()
        {
            double f = ClockFactorHere();
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return " · your clock here: past the horizon, where no far clock sees it";
            if (f <= 0) return " · your clock here: stopped against far clocks";
            return 1 - f < 1e-7 ? "" : " · your clock here: " + SlowWords(f);
        }
        // ── gravity switched off (the Time menu; content/physics.unt @law gravity-off): the year it went, the star's swelling ──
        private double _gravityOffYear = double.NaN;
        public void ToggleGravity() { Window.GravityOn = !Window.GravityOn; _gravityOffYear = Window.GravityOn ? double.NaN : _simYears; SpaceGen.GravityOffYear = _gravityOffYear; MenuChanged = true; }
        private string GravityWords() { return Window.GravityOn || double.IsNaN(_gravityOffYear) ? "" : " · gravity off for " + Years(Math.Max(0, _simYears - _gravityOffYear)) + ": every body straight on, every star blown apart"; }
        // how much the system's star has swollen: its own pressure unopposed grows it at about its sound speed, doubling it in about its
        // sound-crossing time (swell-over-freefall times its free-fall time root(R^3 / GM): about an hour for a star of one solar mass)
        private double StarSwell
        {
            get
            {
                if (Window.GravityOn || double.IsNaN(_gravityOffYear) || _system == null) return 1;
                double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol), tff = Math.Sqrt(R * R * R / gm);
                return Math.Min(1e6, 1 + Math.Max(0, _simYears - _gravityOffYear) * 31557600.0 / (_swellOverFreefall * tff));
            }
        }
        private double StarSwellSpeedKmS() { if (_system == null) return 0; double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol); return R / (_swellOverFreefall * Math.Sqrt(R * R * R / gm)) / 1000; }
        private double StarRadiusNow { get { return _system != null ? _system.StarRadius * StarSwell : 0; } }
        // a galaxy's stars flung off with gravity off: how far each has flown in the galaxy's own frame (the disc's on their tangents at the
        // rim speed, a round galaxy's every way: negative), and the camera's own star's flight taken out of the placing, so you ride with it
        private readonly Dictionary<string, bool> _ballType = new Dictionary<string, bool>();
        private float GalaxyDrift(Galaxy g, ref Mat4 pg)
        {
            if (Window.GravityOn || double.IsNaN(_gravityOffYear)) return 0;
            double years = Math.Max(0, _simYears - _gravityOffYear), kmPerUnit = g.Scale / Units.Km(1);
            double drift = _rimSpeed * years * 31557600.0 / Math.Max(1e-30, kmPerUnit);
            bool ball; if (!_ballType.TryGetValue(g.Type, out ball)) { ball = false; foreach (var e in _content.OfKind("galaxies.unt", "galaxy")) if (e.Id == g.Type) ball = e.Get("bodies-spread", "disc") == "ball"; _ballType[g.Type] = ball; }
            if (!ball)
            {
                // the camera in the galaxy's frame (the placing's rows are the galaxy's axes, scaled): its own tangent taken out when it is within the galaxy
                double rx = _cx - g.X, ry = _cy - g.Y, rz = _cz - g.Z, s2 = (double)g.Scale * g.Scale;
                double lx = (rx * pg.M11 + ry * pg.M12 + rz * pg.M13) / s2, lz = (rx * pg.M31 + ry * pg.M32 + rz * pg.M33) / s2, lr = Math.Sqrt(lx * lx + lz * lz);
                if (lr > 1e-6 && lr < 1.5) { double tx = -lz / lr * drift, tz = lx / lr * drift; pg.M41 -= (float)(tx * pg.M11 + tz * pg.M31); pg.M42 -= (float)(tx * pg.M12 + tz * pg.M32); pg.M43 -= (float)(tx * pg.M13 + tz * pg.M33); }
            }
            return (float)(ball ? -drift : drift);
        }
        // ── a star's layers from its surface down to its core (content/stars.unt @layer): which the system's star has, which you are in ──
        private sealed class StarLayer { public string Id, Name, Says, MakeUp; public double From, To, TLo, THi, DLo, DHi; public List<string> In; }
        private List<StarLayer> _starLayers;
        private List<StarLayer> StarLayers()
        {
            if (_starLayers != null) return _starLayers; _starLayers = new List<StarLayer>();
            foreach (var e in _content.OfKind("stars.unt", "layer"))
            {
                double a, b, tl, th, dl, dh; if (!e.TryRange("from-r", out a, out b)) continue; double f = a; if (!e.TryRange("to-r", out a, out b)) continue;
                var l = new StarLayer { Id = e.Id, Name = e.Name, Says = e.Get("says", ""), From = f, To = a, MakeUp = e.Props.ContainsKey("makeup") ? e.Get("makeup", "") : null };
                if (e.TryRange("temp-k", out tl, out th)) { l.TLo = tl; l.THi = th; } if (e.TryRange("density-g-cc", out dl, out dh)) { l.DLo = dl; l.DHi = dh; }
                if (e.Props.ContainsKey("in")) { l.In = new List<string>(); foreach (var q in e.Get("in", "").Split(',')) if (q.Trim().Length > 0) l.In.Add(q.Trim()); }
                _starLayers.Add(l);
            }
            return _starLayers;
        }
        private string SystemStarId() { return _sysStar >= 0 && _sysStar < _localCount ? _kinds[_localKind[_sysStar]].Id : ""; }
        private List<StarLayer> StarLayersFor(string starId)
        {
            var l = new List<StarLayer>(); foreach (var q in StarLayers()) if (q.In == null || q.In.Contains(starId)) l.Add(q);
            l.Sort((p, q) => q.To.CompareTo(p.To)); return l;
        }
        private StarLayer StarLayerAt(double share)
        {
            StarLayer best = null; foreach (var l in StarLayersFor(SystemStarId())) { if (share < l.From || share > l.To) continue; if (best == null || l.To - l.From < best.To - best.From) best = l; }
            return best;
        }
        // the layer of the system's star you are in now, by your share of its radius from its middle; null outside it (or at its very face)
        private StarLayer StarLayerHere()
        {
            if (_system == null || _ground != null || _sysStar < 0) return null;
            double sd = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), R = StarRadiusNow; if (R <= 0 || sd >= R * 0.9985) return null;
            return StarLayerAt(sd / R);
        }
        // a layer's temperature and density at a depth: each runs from the layer's top to its bottom (the density on a log scale)
        private static double LayerTempK(StarLayer l, double share) { double f = Math.Max(0, Math.Min(1, (l.To - share) / Math.Max(1e-9, l.To - l.From))); return l.TLo > 0 && l.THi > 0 ? l.TLo * Math.Pow(l.THi / l.TLo, f) : 6000; }
        private static double LayerDensity(StarLayer l, double share) { double f = Math.Max(0, Math.Min(1, (l.To - share) / Math.Max(1e-9, l.To - l.From))); return l.DLo > 0 && l.DHi > 0 ? l.DLo * Math.Pow(l.DHi / l.DLo, f) : 2e-7; }
        private static string DensityWords(double gcc) { return gcc >= 1 ? gcc.ToString(gcc >= 100 ? "N0" : "0.#") + " g/cc" : gcc >= 1e-3 ? (gcc * 1000).ToString("0.#") + " mg/cc" : gcc.ToString("0.#E+0") + " g/cc"; }
        // the glow inside a star, by its heat: orange at a few thousand K, white-yellow at a star's face, blue-white in the millions
        private static void StarGlow(double tempK, out float r, out float g, out float b)
        {
            double t = Math.Max(0, Math.Min(1, Math.Log10(Math.Max(1000, tempK) / 3000.0) / Math.Log10(1e7 / 3000.0)));
            r = 1f; g = (float)(0.45 + 0.55 * Math.Min(1, t * 2.2)); b = (float)(0.15 + 0.85 * Math.Min(1, t * 1.6));
        }
        // ── the tour's reach: the places it went to against every galaxy, star, world and moon there is ──
        private int _tourVisited; private double _bodiesPerStar = -1; private double _fallTh = Math.PI, _fallPh0, _fallTurnT, _faceT;   // the tour's look down the fall, eased (set from the look as you come in); its facing turn's time since it began
        private double BodiesPerStar()
        {
            if (_bodiesPerStar >= 0) return _bodiesPerStar; if (_kinds == null || _kinds.Count == 0) return _bodiesPerStar = 10;
            double sum = 0; int n = 0;
            for (int k = 0; k < 24; k++) { try { var sy = SpaceGen.System(_content, _kinds[(k * 7) % _kinds.Count], 0x9E3779B97F4A7C15UL * (ulong)(k + 1)); sum += sy.Bodies.Count; n++; } catch { } }
            return _bodiesPerStar = n > 0 ? sum / n : 10;
        }
        // the body the tour is at, for a caption: its kind and its own minerals (an S-type asteroid: forsterite 31%, enstatite 24%...),
        // never a stock word for it (the caption had said "rock left over from when the worlds were made" over every one)
        private string BodyWords()
        {
            var ex = _tKind == 6 ? _tExtra : null; if (ex == null) return "a small body";
            string kind = ex.KindName ?? ex.Name; string mins = null;
            foreach (var ln in ex.Lines) if (ln.StartsWith("minerals: ")) { mins = ln.Substring(10); break; }
            string a = kind.Length > 0 && "AEIOU".IndexOf(char.ToUpperInvariant(kind[0])) >= 0 ? "An " : "A ";
            return a + kind.ToLowerInvariant() + (mins != null ? ": " + mins : "");
        }
        private string SeenWords()
        {
            double stars = _uGalaxies * RealStars, places = _uGalaxies + stars + stars * BodiesPerStar();
            double pct = Math.Max(1, _tourVisited) / places * 100; string pw;
            if (pct >= 0.01) pw = pct.ToString("0.##");
            else { int e = (int)Math.Floor(Math.Log10(pct)); pw = pct.ToString("0." + new string('0', Math.Min(60, -e + 1))); }
            return "You have now been to " + Math.Max(1, _tourVisited) + " of its " + places.ToString("0.#E+0") + " galaxies, stars, worlds and moons: " + pw + "% of the universe";
        }
        public string Title
        {
            get
            {
                double pdx, pdy, pdz, prad; string picked = _tKind >= 0 && TargetFromCamera(out pdx, out pdy, out pdz, out prad) ? " · going to " + _tName + ", " + Units.Describe(Math.Max(0, Math.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz) - prad)) + " away" : " · click something to go to it";
                string where = _spaceScale > 0 ? "in " + (_tName.Length > 0 ? _tName : "the matter") + ", " + ScaleHud.Words(_spaceScale * 0.6) + " from the point you dive into: " + _matter.Stage(_spaceScale)
                    : _matterScale > 0 ? "at " + GroundKind() + ", " + ScaleHud.Words(_matterScale * 0.6) + " above the ground: " + _matter.Stage(_matterScale)
                    : _ground != null ? "at " + GroundKind() + ", " + Units.Describe(AboveGround()) + " above the ground"
                    : _system != null ? "in the system of a " + _system.StarName + " (" + _system.Bodies.Count + " planets and moons)"
                    : _near >= 0 ? (NearShare() > 0.5 ? "among the stars of " : "near ") + _galaxies[_near].Name : "among the galaxies";
                double hr = LocalHour(); string when = " · time: " + TimeNames[TimeStep] + (hr >= 0 ? ", " + ((int)hr).ToString("00") + ":" + ((int)(hr % 1 * 60)).ToString("00") + " here" : "") + ClockWords() + GravityWords();
                var lh = LayerHere(); if (lh != null) where = "inside " + (_system != null && _planet >= 0 ? GroundKind() : "the giant") + ", in its " + lh.Name.ToLowerInvariant() + " (" + lh.Says + ")";
                { var sl = StarLayerHere(); if (sl != null && _spaceScale <= 0) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) / Units.Km(1), RK = StarRadiusNow / Units.Km(1); where = "inside the " + _system.StarName + ", " + ScaleHud.Words(RK - sdK) + " below its surface, in its " + sl.Name.ToLowerInvariant() + " (about " + LayerTempK(sl, sdK / RK).ToString("N0") + " K: " + sl.Says + ")"; } }
                return PreviewText.AppName + " · " + where + when + picked + (_tour != null && _tourAt < _tour.Count ? " · tour: " + _tour[_tourAt].Name : "") + " · " + (_spaceScale > 0 || _matterScale > 0 || _tKind == 5 ? "looking at " + ScaleHud.Words(ViewDistanceKm()) + " away" : "nearest thing " + Units.Describe(NearestDistance()) + " away") + " · wheel: fly in or out toward the mouse · drag: circle the pick or look around · right click: clear the pick · W A S D Q E: fly · H: scale on or off · Esc: close · card " + (int)Math.Round(Window.GpuBusy * 100) + "% busy (GPU use " + (int)Math.Round(Window.GpuUse * 100) + "%)";
            }
        }

        public void Dispose()
        {
            foreach (var g in _galaxies) if (g.Far != null) g.Far.Dispose(); if (_glowBuf != null) _glowBuf.Dispose();
            if (_nearBuf != null) _nearBuf.Dispose(); foreach (var g in _galaxies) if (g.Struct != null) g.Struct.Dispose(); foreach (var kv in _nebBufs) kv.Value.Dispose(); _nebBufs.Clear();
            CloseSystem();
            DropPlanet();
            _hud.Dispose(); _matter.Dispose();
            if (_localBuf != null) _localBuf.Dispose();
        }
    }
}
