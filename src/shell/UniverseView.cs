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
    internal sealed partial class UniverseView : IDisposable
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
        // ── time: how fast it runs (years a second of yours), from paused to a billion years a second (the universe's whole age in
        // fourteen seconds: what galaxies turning, stars flung off with gravity gone and space stretching need to be seen); an hour a
        // second at first ──
        public static readonly double[] TimeSpeeds = { 0, 1 / 31557600.0, 60 / 31557600.0, 3600 / 31557600.0, 86400 / 31557600.0, 7 * 86400 / 31557600.0, 30.44 * 86400 / 31557600.0, 1 / 20.0, 1, 10, 1e3, 1e6, 1e9 };
        public static readonly string[] TimeNames = { "paused", "real time", "a minute a second", "an hour a second", "a day a second", "a week a second", "a month a second", "a year in 20 seconds", "a year a second", "ten years a second", "a thousand years a second", "a million years a second", "a billion years a second" };
        public int TimeStep = 2; private int _timeBeforePause = 2;   // (starts at a minute a second: a day of 10 to 40 hours passes in 10 to 40 minutes, the worlds and their stars moving calmly)
        /// <summary>Pauses, or carries on at the speed it had.</summary>
        public void TogglePause() { if (TimeStep == 0) TimeStep = _timeBeforePause; else { _timeBeforePause = TimeStep; TimeStep = 0; } MenuChanged = true; }
        public void SetTime(int step) { TimeStep = Math.Max(0, Math.Min(TimeSpeeds.Length - 1, step)); if (TimeStep > 0) _timeBeforePause = TimeStep; MenuChanged = true; }

        // a body's day in hours, one turn of its spin: a world's from its own template (content/terrain.unt day-hours, its own
        // value in the kind's range, the same one its ground is lit by); moons turn once each orbit (one face to their planet)
        private double DayHours(int i)
        {
            if (i == -2 && _groundExtra != null && _groundExtra.DayH > 0) return _groundExtra.DayH;   // (a small body's ground: its own day, the one its stone turns by)
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
                if (ClipNote != null) return PreviewText.AppName + " · " + ClipNote;
                double pdx, pdy, pdz, prad; string picked = _tKind >= 0 && TargetFromCamera(out pdx, out pdy, out pdz, out prad) ? " · going to " + _tName + ", " + Units.Describe(Math.Max(0, Math.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz) - prad)) + " away" : " · click something to go to it";
                string where = _spaceScale > 0 ? "in " + (_tName.Length > 0 ? _tName : "the matter") + ", " + ScaleHud.Words(_spaceScale * 0.6) + " from the point you dive into: " + _matter.Stage(_spaceScale)
                    : _matterScale > 0 ? "at " + GroundKind() + ", " + ScaleHud.Words(_matterScale * 0.6) + " above the ground: " + _matter.Stage(_matterScale)
                    : _ground != null ? "at " + GroundKind() + ", " + Units.Describe(AboveGround()) + " above the ground"
                    : _system != null ? "in the system of a " + _system.StarName + " (" + _system.Bodies.Count + " planets and moons)"
                    : _near >= 0 ? (NearShare() > 0.5 ? "among the stars of " : "near ") + _galaxies[_near].Name : "among the galaxies";
                double hr = LocalHour(); string when = " · time: " + TimeNames[TimeStep] + (hr >= 0 ? ", " + ((int)hr).ToString("00") + ":" + ((int)(hr % 1 * 60)).ToString("00") + " here" : "") + ClockWords() + GravityWords();
                var lh = LayerHere(); if (lh != null) where = "inside " + (_system != null && _planet >= 0 ? GroundKind() : "the giant") + ", in its " + lh.Name.ToLowerInvariant() + " (" + lh.Says + ")";
                { var sl = StarLayerHere(); if (sl != null && _spaceScale <= 0) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) / Units.Km(1), RK = StarRadiusNow / Units.Km(1); where = "inside the " + _system.StarName + ", " + ScaleHud.Words(RK - sdK) + " below its surface, in its " + sl.Name.ToLowerInvariant() + " (about " + LayerTempK(sl, sdK / RK).ToString("N0") + " K: " + sl.Says + ")"; } }
                return PreviewText.AppName + " · " + where + " (" + CameraCoords() + ")" + when + picked + (_tour != null && _tourAt < _tour.Count ? " · tour: " + _tour[_tourAt].Name : "") + " · " + (_spaceScale > 0 || _matterScale > 0 || _tKind == 5 ? "looking at " + ScaleHud.Words(ViewDistanceKm()) + " away" : "nearest thing " + Units.Describe(NearestDistance()) + " away") + " · wheel: fly in or out toward the mouse · drag: circle the pick or look around · right click: clear the pick · W A S D Q E: fly · H: scale on or off · Esc: close · card " + (int)Math.Round(Window.GpuBusy * 100) + "% busy (GPU use " + (int)Math.Round(Window.GpuUse * 100) + "%)";
            }
        }

        public void Dispose()
        {
            foreach (var g in _galaxies) if (g.Far != null) g.Far.Dispose(); if (_glowBuf != null) _glowBuf.Dispose();
            if (_nearBuf != null) _nearBuf.Dispose(); foreach (var g in _galaxies) if (g.Struct != null) g.Struct.Dispose(); foreach (var kv in _nebBufs) kv.Value.Dispose(); _nebBufs.Clear(); if (_flareBuf != null) _flareBuf.Dispose(); foreach (var kv in _nebStarBufs) if (kv.Value != null) kv.Value.Dispose(); _nebStarBufs.Clear(); _nebNs.Clear(); _nebUsed.Clear();
            CloseSystem();
            DropPlanet();
            _hud.Dispose(); _matter.Dispose();
            if (_localBuf != null) _localBuf.Dispose();
        }
    }
}
