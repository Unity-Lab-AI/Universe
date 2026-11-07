// Universe · shell · deep zoom: one galaxy full-window, zoomable from the whole galaxy down to the core's
// supermassive black hole, its clusters and nebulas, and any star's full system. Everything is generated live.
// Precision: positions are kept relative to the current focus (a moving origin), so tiny things stay exact.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class DeepView : IDisposable
    {
        private const int GalaxyStars = 150000;
        private readonly Gpu _gpu; private readonly BillboardDrawer _drawer; private readonly ContentLibrary _content;
        private readonly string _name; private readonly ulong _seed;
        private readonly StarVertex[] _stars, _structures; private readonly int[] _kinds;
        private readonly StarVertex[] _starsRel, _structRel;
        private readonly List<StarKind> _kindList;
        private readonly SpaceGen.BlackHole _bh;
        private readonly int _clusters, _nebulas;
        private UploadBuffer _starBuf, _structBuf, _diskBuf, _jetBuf, _coreBuf, _sysStatic, _sysBodies;
        private SpaceGen.StarSystem _system; private int _focusStar = -1;
        private int _focusBody = -1;
        private double _atSystemSince = -1, _clock;   // when the zoom reached the whole-system view (a short rest before going on to a planet)               // in a solar system: the planet or moon the camera is centred on (-1 = the star)
        /// <summary>Set when the camera has come close enough to a planet to fly down to it: the body's index (else -1).</summary>
        public int HandOff = -1;
        /// <summary>Set when the player zooms out past the whole galaxy: back to the mix of galaxies.</summary>
        public bool Ascend;
        /// <summary>For drawing the planet being approached as the real 3D planet (set by the journey).</summary>
        public MeshPipes Meshes;
        /// <summary>The 3D planet being approached (made when a planet is picked; the planet view carries on with this same one).</summary>
        public PlanetTerrain FocusTerrain, FocusFields;
        private StarSurface _starSurface; private StarVertex[] _starCorona, _starSurfVerts; private UploadBuffer _starCoronaBuf, _starSurfBuf, _shineBuf;
        // the target star is reached one wheel notch at a time (see Update)
        private int _pendingStar = -1;              // the journey: the star being flown toward, opened once close enough

        /// <summary>The journey: aim at a one-solar-mass star out in the disc, so rolling the wheel in flies to it.</summary>
        public void AimForJourney()
        {
            int best = -1; double bestScore = 1e9;
            for (int i = 0; i < _stars.Length; i += 7)
            {
                double r = Math.Sqrt(_stars[i].X * _stars[i].X + _stars[i].Z * _stars[i].Z);
                string k = _kindList[Math.Max(0, _kinds[i])].Id;
                double score = Math.Abs(r - 0.55) + (k == "g-type" ? 0 : 1);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) return;
            _pendingStar = best;
            // start on the whole galaxy; each notch in moves toward the star
            _dist = 3.5;
        }

        // keep the point under the mouse fixed on screen while zooming by factor z
        private void ZoomAtMouse(int mx, int my, int w, int h, double z)
        {
            double nx = mx / (double)Math.Max(1, w) * 2 - 1, ny = 1 - my / (double)Math.Max(1, h) * 2;
            Mat4 view = OrbitView();
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            double t = Math.Tan(0.45), aspect = w / (double)Math.Max(1, h);
            double px = right.X * nx * t * aspect * _dist + up.X * ny * t * _dist, py = right.Y * nx * t * aspect * _dist + up.Y * ny * t * _dist, pz = right.Z * nx * t * aspect * _dist + up.Z * ny * t * _dist;
            // the focus moves toward that point by the same share the distance shrinks
            double k = 1 - z;
            SetFocus(_fx + px * k, _fy + py * k, _fz + pz * k);
        }

        private int NearestStarToFocus()
        {
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _stars.Length; i++)
            {
                double dx = _stars[i].X - _fx, dy = _stars[i].Y - _fy, dz = _stars[i].Z - _fz, d = dx * dx + dy * dy + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>In the galaxy: the star nearest the mouse becomes the one you fly to; the view glides toward it as you zoom in.</summary>
        private void ClickStar(int mx, int my, int w, int h)
        {
            Mat4 vp = ViewProj(w, h);
            float x = mx / (float)Math.Max(1, w) * 2 - 1, y = 1 - my / (float)Math.Max(1, h) * 2;
            int best = -1; float bd = 0.12f * 0.12f;           // the nearest star to the mouse, within about a tenth of the screen
            for (int i = 0; i < _starsRel.Length; i++)
            {
                StarVertex v = _starsRel[i];
                float cx = v.X * vp.M11 + v.Y * vp.M21 + v.Z * vp.M31 + vp.M41, cy = v.X * vp.M12 + v.Y * vp.M22 + v.Z * vp.M32 + vp.M42, cw = v.X * vp.M14 + v.Y * vp.M24 + v.Z * vp.M34 + vp.M44;
                if (cw <= 0) continue;
                float d = (cx / cw - x) * (cx / cw - x) + (cy / cw - y) * (cy / cw - y);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            if (_system != null)
            {
                // leaving the open solar system for another star: back out to galaxy scale, centred where we were
                _system = null; _focusStar = -1; _focusBody = -1; FocusTerrain = null; FocusFields = null;
                if (_sysStatic != null) { _sysStatic.Dispose(); _sysStatic = null; }
                if (_sysBodies != null) { _sysBodies.Dispose(); _sysBodies = null; }
                _dist = Math.Max(_dist, 0.002);
            }
            _pendingStar = best;
        }

        private void OpenStar(int best)
        {
            _focusStar = best; _pendingStar = -1;
            StarKind kind = _kindList[Math.Max(0, _kinds[best])];
            string sname; _starCorona = StarGen.Build(_content, kind.Id, _seed * 77UL + (ulong)best, out sname, out _starSurface);
            _starSurfVerts = new StarVertex[_starSurface.Normals.Length];
            if (_starCoronaBuf != null) _starCoronaBuf.Dispose(); if (_starSurfBuf != null) _starSurfBuf.Dispose();
            _starCoronaBuf = Upload(_starCorona); _starSurfBuf = new UploadBuffer(_gpu.Device, (long)_starSurfVerts.Length * Stride);
            _system = SpaceGen.System(_content, kind, _seed * 31UL + (ulong)best); _focusBody = -1;
            SetFocus(_stars[best].X, _stars[best].Y, _stars[best].Z);
            if (_sysStatic != null) _sysStatic.Dispose();
            _sysStatic = Upload(_system.Static);
            if (_sysBodies != null) _sysBodies.Dispose();
            _bodyVerts = new StarVertex[(_system.Bodies.Count + 1) * (1 + TrailPoints)];
            _staticShifted = new StarVertex[_system.Static.Length];
            _sysBodies = new UploadBuffer(_gpu.Device, (long)_bodyVerts.Length * Stride);
            // show the whole system: nothing is picked for you; click a planet or moon (or Tab) to choose where to go
            _focusBody = -1; FocusTerrain = null; FocusFields = null;
            double outer = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outer = Math.Max(outer, b.OrbitUnits);
            _dist = outer * 2.2;
        }
        public SpaceGen.StarSystem System { get { return _system; } }
        public ulong SystemSeed { get { return _seed * 31UL + (ulong)Math.Max(0, _focusStar); } }
        private double _fx, _fy, _fz;          // focus position in galaxy units (double)
        private float _yaw = 0.6f, _pitch = 0.9f; private double _dist = 2.4;
        private StarVertex[] _bodyVerts;
        private double _simYears;                 // simulation time in years
        private double _speed = 1; private bool _paused;
        private static readonly double[] Speeds = { 1, 10, 100, 1000, 100000 };
        private const double SecondsPerYearAt1x = 20.0;   // at 1x, one year takes 20 s
        private const double MaxVisualTurnPerSecond = 0.05; // radians: nothing on screen turns faster than this
        private static readonly int Stride = Marshal.SizeOf(typeof(StarVertex));

        public DeepView(Gpu gpu, BillboardPipeline pipe, ContentLibrary content, string type, string name, ulong seed)
        {
            _gpu = gpu; _drawer = new BillboardDrawer(gpu.CommandList, pipe); _content = content; _name = name; _seed = seed;
            _kindList = GalaxyGen.StarKinds(content);
            _kinds = new int[GalaxyStars];
            _stars = GalaxyGen.Generate(type, seed, GalaxyStars, _kindList, _kinds);
            _structures = SpaceGen.Structures(type, seed, _kindList, out _clusters, out _nebulas);
            _starsRel = new StarVertex[_stars.Length]; _structRel = new StarVertex[_structures.Length];
            _starBuf = new UploadBuffer(gpu.Device, (long)_stars.Length * Stride);
            _structBuf = new UploadBuffer(gpu.Device, (long)Math.Max(1, _structures.Length) * Stride);
            _bh = SpaceGen.CoreBlackHole(content, seed);
            _diskBuf = Upload(_bh.Disk); _jetBuf = Upload(_bh.Jets); _coreBuf = Upload(_bh.Glow);
            SetFocus(0, 0, 0);
        }

        private UploadBuffer Upload(StarVertex[] v)
        {
            var b = new UploadBuffer(_gpu.Device, (long)Math.Max(1, v.Length) * Stride);
            if (v.Length > 0) b.Write(v, v.Length);
            return b;
        }

        private void SetFocus(double x, double y, double z)
        {
            _fx = x; _fy = y; _fz = z;
            Relative(_stars, _starsRel); _starBuf.Write(_starsRel, _starsRel.Length);
            if (_structures.Length > 0) { Relative(_structures, _structRel); _structBuf.Write(_structRel, _structRel.Length); }
        }

        private void Relative(StarVertex[] src, StarVertex[] dst)
        {
            for (int i = 0; i < src.Length; i++)
            {
                StarVertex v = src[i];
                v.X = (float)(src[i].X - _fx); v.Y = (float)(src[i].Y - _fy); v.Z = (float)(src[i].Z - _fz);
                dst[i] = v;
            }
        }

        /// <summary>Returns false when the player wants to go back to the sample grid.</summary>
        public bool Update(Input input, double dt, int w, int h)
        {
            if (input.WasPressed(0x08) || input.WasPressed(0x1B)) return false;                    // Backspace or Esc: back
            // in a solar system: click a planet or moon (or Tab for the next one) to centre on it; the wheel then flies to it
            if (_system != null && input.WasPressed(0x09)) PickBody((_focusBody + 2) % (_system.Bodies.Count + 1) - 1);
            // a click picks a planet in the open system if one is under the mouse, otherwise any star of the galaxy
            if (input.LeftClicked && !(_system != null && ClickBody(input.MouseX, input.MouseY, w, h))) ClickStar(input.MouseX, input.MouseY, w, h);
            if (input.LeftDown) { _yaw += input.MouseDX * 0.006f; _pitch = Math.Max(-1.5f, Math.Min(1.5f, _pitch + input.MouseDY * 0.006f)); }
            if (input.Wheel < 0 && _focusStar < 0 && _dist >= 3.99) { Ascend = true; return false; }     // out past the galaxy: back to the universe
            // zooming out lets go step by step: the planet, then the solar system, then the galaxy
            if (input.Wheel < 0 && _system != null)
            {
                double outer = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outer = Math.Max(outer, b.OrbitUnits);
                if (_focusBody >= 0 && _dist > outer * 1.5) { _focusBody = -1; }
                else if (_focusBody < 0 && _dist > outer * 40) { GoCore(); _dist = Math.Max(_dist, outer * 40); return true; }
            }
            if (_pendingStar >= 0 && input.Wheel < 0 && _dist > 3.0) _pendingStar = -1;      // backed right out to the whole galaxy: let the target go
            // the wheel zooms toward where the mouse points: the spot under the mouse stays under it (unless a star was clicked)
            // each notch zooms a little; through a solar system's own range the zoom brakes, so the whole system stays in view
            double step = 0.87;
            if (_system != null && _focusBody < 0)
            {
                double outerS = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outerS = Math.Max(outerS, b.OrbitUnits);
                if (_dist < outerS * 8 && _dist > outerS * 0.05) step = 0.95;
            }
            if (input.Wheel != 0 && _system == null && _pendingStar < 0) ZoomAtMouse(input.MouseX, input.MouseY, w, h, Math.Pow(step, input.Wheel));
            if (input.Wheel != 0) _dist *= Math.Pow(step, input.Wheel);
            if (_pendingStar >= 0 && input.Wheel > 0)
            {
                // each notch in moves the view's centre a share of the way to the target, so you fly toward where you pointed
                var s0 = _stars[_pendingStar];
                double k = 1 - Math.Pow(0.75, input.Wheel);
                double nx = _fx + (s0.X - _fx) * k, ny = _fy + (s0.Y - _fy) * k, nz = _fz + (s0.Z - _fz) * k;
                double gap = Math.Sqrt((s0.X - nx) * (s0.X - nx) + (s0.Y - ny) * (s0.Y - ny) + (s0.Z - nz) * (s0.Z - nz));
                if (gap < _dist * 0.05) { nx = s0.X; ny = s0.Y; nz = s0.Z; }
                SetFocus(nx, ny, nz);
            }
            if (_pendingStar < 0 && _system == null && _dist < 2e-6) _pendingStar = NearestStarToFocus();   // arrived among the stars: the nearest one opens
            if (_pendingStar >= 0 && _dist < 2e-6)
            {
                var s1 = _stars[_pendingStar];
                if (Math.Abs(s1.X - _fx) + Math.Abs(s1.Y - _fy) + Math.Abs(s1.Z - _fz) < 1e-6) OpenStar(_pendingStar); else _dist = 2e-6;
            }                 // close enough to the star: open its solar system
            double minDist = _focusStar >= 0 ? _system.StarRadius * 1.5 : _pendingStar >= 0 ? 1e-6 : _bh.HorizonUnits * 3;
            if (_system != null && _focusBody < 0)
            {
                // the zoom stops at the whole solar system; rolling in again picks the planet nearest the mouse and goes on to it
                double outerF = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outerF = Math.Max(outerF, b.OrbitUnits);
                minDist = outerF * 1.3;
                if (input.Wheel > 0 && _dist <= minDist * 1.02 && _atSystemSince >= 0 && _clock - _atSystemSince > 0.4) PickNearestBody(input.MouseX, input.MouseY, w, h);
                if (_dist <= minDist * 1.02) { if (_atSystemSince < 0) _atSystemSince = _clock; } else _atSystemSince = -1;
            }
            else _atSystemSince = -1;
            _clock += dt;
            if (_focusBody >= 0) minDist = _system.Bodies[_focusBody].RadiusUnits * 1.05;
            _dist = Math.Max(minDist, Math.Min(4.0, _dist));
            // close enough to a planet: hand over to the planet view, at the same distance
            if (_focusBody >= 0 && input.Wheel > 0 && _dist < _system.Bodies[_focusBody].RadiusUnits * 3.3) HandOff = _focusBody;   // the planet view starts 2.3 radii above the ground: 3.3 from the middle, so the zoom carries straight on
            if (input.WasPressed(0x20)) _paused = !_paused;                          // Space: pause
            for (int k = 0; k < 5; k++) if (input.WasPressed(0x31 + k)) { _speed = Speeds[k]; _paused = false; } // 1-5: speeds
            if (!_paused) _simYears += dt / SecondsPerYearAt1x * _speed;
            if (input.WasPressed(0x48)) GoCore();                                    // H: the core black hole
            if (input.WasPressed(0x0D)) VisitCenterStar(w, h);                       // Enter: visit the star in the middle
            return true;
        }

        public double BodyRadiusUnits(int i) { return _system.Bodies[i].RadiusUnits; }
        public double Distance { get { return _dist; } }

        /// <summary>Back from the planet view: hover just above the planet, where the planet view let go.</summary>
        public void ReturnFromPlanet() { HandOff = -1; if (_focusBody >= 0) _dist = _system.Bodies[_focusBody].RadiusUnits * 3.4; }

        /// <summary>Test runs: go to the middle star's system and centre on its first planet.</summary>
        public void TestJourney(int w, int h)
        {
            TestVisit(w, h);
            if (_system == null) return;
            int pick = 0;
            for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && _system.Bodies[i].Interest > _system.Bodies[pick].Interest) pick = i;
            PickBody(pick);
        }

        private void PickBody(int i)
        {
            if (_system == null) return;
            _focusBody = i < 0 || i >= _system.Bodies.Count ? -1 : i;
            FocusTerrain = null; FocusFields = null;
            if (_focusBody >= 0)
            {
                var body = _system.Bodies[_focusBody];
                string kind = body.Terrain ?? TerrainKind(body.Kind);
                uint tseed = (uint)(SystemSeed * 977UL + (ulong)_focusBody);
                // its own real size, from its own solar system
                double km = body.RadiusUnits / Units.Km(1);
                FocusTerrain = new PlanetTerrain(_gpu.Device, kind, body.Kind, tseed, false, km);
                FocusFields = new PlanetTerrain(_gpu.Device, kind, body.Kind, tseed, true, km);
            }
            double outer = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outer = Math.Max(outer, b.OrbitUnits);
            // picking keeps the distance you were at (no jump straight to the planet): you fly in with the wheel through every scale
            _dist = _focusBody < 0 ? outer * 2.2 : Math.Min(_dist, outer * 2.2);
        }

        // the planet nearest the mouse on screen, however far from it (the star is never picked this way)
        private void PickNearestBody(int mx, int my, int w, int h)
        {
            Mat4 vp = ViewProj(w, h);
            float x = mx / (float)Math.Max(1, w) * 2 - 1, y = 1 - my / (float)Math.Max(1, h) * 2;
            int best = -1; float bd = float.MaxValue;
            for (int i = 1; i < _bodyCount; i++)
            {
                if (i - 1 >= _system.Bodies.Count || _system.Bodies[i - 1].Parent >= 0) continue;   // planets only, not moons
                var v = _bodyVerts[i];
                float cx = v.X * vp.M11 + v.Y * vp.M21 + v.Z * vp.M31 + vp.M41, cy = v.X * vp.M12 + v.Y * vp.M22 + v.Z * vp.M32 + vp.M42, cw = v.X * vp.M14 + v.Y * vp.M24 + v.Z * vp.M34 + vp.M44;
                if (cw <= 0) continue;
                float d = (cx / cw - x) * (cx / cw - x) + (cy / cw - y) * (cy / cw - y);
                if (d < bd) { bd = d; best = i - 1; }
            }
            if (best >= 0) PickBody(best);
        }

        private bool ClickBody(int mx, int my, int w, int h)
        {
            Mat4 vp = ViewProj(w, h);
            float x = mx / (float)Math.Max(1, w) * 2 - 1, y = 1 - my / (float)Math.Max(1, h) * 2;
            int best = -2; float bd = 0.05f;
            for (int i = 0; i < _bodyCount; i++)
            {
                var v = _bodyVerts[i];
                float cx = v.X * vp.M11 + v.Y * vp.M21 + v.Z * vp.M31 + vp.M41, cy = v.X * vp.M12 + v.Y * vp.M22 + v.Z * vp.M32 + vp.M42, cw = v.X * vp.M14 + v.Y * vp.M24 + v.Z * vp.M34 + vp.M44;
                if (cw <= 0) continue;
                float d = (float)Math.Sqrt((cx / cw - x) * (cx / cw - x) + (cy / cw - y) * (cy / cw - y));
                if (d < bd) { bd = d; best = i - 1; }       // vertex 0 is the star
            }
            if (best > -2) { PickBody(best); return true; }
            return false;
        }

        public void TestCore() { GoCore(); _dist = _bh.HorizonUnits * 60; }
        public void TestVisit(int w, int h) { _pitch = 1.45f; _dist = 0.6; VisitCenterStar(w, h); if (_focusStar < 0) { _dist = 0.05; VisitCenterStar(w, h); } _pitch = 0.7f; }
        public void TestZoom(double d) { _dist = d; }
        /// <summary>Starts the view at an angle (from the universe view), so the zoom into the galaxy keeps your angle.</summary>
        public void SetAngle(float yaw, float pitch) { _yaw = yaw; _pitch = Math.Max(-1.5f, Math.Min(1.5f, pitch)); }
        public void TestSpeed(int index) { _speed = Speeds[Math.Max(0, Math.Min(4, index))]; }

        private void GoCore()
        {
            _focusStar = -1; _system = null;
            if (_sysStatic != null) { _sysStatic.Dispose(); _sysStatic = null; }
            if (_sysBodies != null) { _sysBodies.Dispose(); _sysBodies = null; }
            SetFocus(0, 0, 0);
            _dist = Math.Max(_bh.HorizonUnits * 40, Math.Min(_dist, 0.05));
        }

        private void VisitCenterStar(int w, int h)
        {
            Mat4 vp = ViewProj(w, h);
            int best = -1; double bestD = 40 * 40; // within 40 pixels of the middle
            for (int i = 0; i < _starsRel.Length; i++)
            {
                StarVertex s = _starsRel[i];
                float cx = s.X * vp.M11 + s.Y * vp.M21 + s.Z * vp.M31 + vp.M41;
                float cy = s.X * vp.M12 + s.Y * vp.M22 + s.Z * vp.M32 + vp.M42;
                float cw = s.X * vp.M14 + s.Y * vp.M24 + s.Z * vp.M34 + vp.M44;
                if (cw <= 0) continue;
                double px = cx / cw * w * 0.5, py = cy / cw * h * 0.5, d = px * px + py * py;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            _focusStar = best;
            StarKind kind = _kindList[Math.Max(0, _kinds[best])];
            _system = SpaceGen.System(_content, kind, _seed * 31UL + (ulong)best); _focusBody = -1;
            SetFocus(_stars[best].X, _stars[best].Y, _stars[best].Z);
            if (_sysStatic != null) _sysStatic.Dispose();
            _sysStatic = Upload(_system.Static);
            if (_sysBodies != null) _sysBodies.Dispose();
            _bodyVerts = new StarVertex[(_system.Bodies.Count + 1) * (1 + TrailPoints)];
            _staticShifted = new StarVertex[_system.Static.Length];
            _sysBodies = new UploadBuffer(_gpu.Device, (long)_bodyVerts.Length * Stride);
            double outer = 0; foreach (var b in _system.Bodies) if (b.Parent < 0) outer = Math.Max(outer, b.OrbitUnits);
            _dist = outer * 2.2;
        }

        public static string TerrainKind(string bodyKind)
        {
            switch (bodyKind)
            {
                case "Temperate world": return "temperate";
                case "Lava planet": return "lava";
                case "Ice world": return "snowball";
                case "Gas giant": return "gas-giant";
                case "Ice giant": return "ice-giant";
                case "Hot Jupiter": return "hot-jupiter";
                default: return "desert";
            }
        }

        /// <summary>The camera, built from its direction (always unit length) and then moved out by the distance,
        /// so it stays exact even a trillionth of a galaxy unit from a planet.</summary>
        private Mat4 OrbitView()
        {
            var dir = new Vec3((float)(Math.Cos(_yaw) * Math.Cos(_pitch)), (float)Math.Sin(_pitch), (float)(Math.Sin(_yaw) * Math.Cos(_pitch)));
            Mat4 v = Mat4.LookAt(dir, new Vec3(0, 0, 0), new Vec3(0, 1, 0));
            v.M41 = (float)(v.M41 * _dist); v.M42 = (float)(v.M42 * _dist); v.M43 = (float)(v.M43 * _dist);
            return v;
        }

        private Mat4 ViewProj(int w, int h)
        {
            var eye = new Vec3((float)(Math.Cos(_yaw) * Math.Cos(_pitch) * _dist), (float)(Math.Sin(_pitch) * _dist), (float)(Math.Sin(_yaw) * Math.Cos(_pitch) * _dist));
            Mat4 view = OrbitView();
            float near = (float)(_dist * 1e-3), far = (float)Math.Max(_dist * 1e5, 12);
            return view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), near, far);
        }

        public void Draw(int w, int h, double time)
        {
            var eye = new Vec3((float)(Math.Cos(_yaw) * Math.Cos(_pitch) * _dist), (float)(Math.Sin(_pitch) * _dist), (float)(Math.Sin(_yaw) * Math.Cos(_pitch) * _dist));
            Mat4 view = OrbitView();
            Mat4 vp = ViewProj(w, h);
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);

            // the galaxy's stars, clusters and nebulas, with their own near and far range so they stay in view however close you are to a planet
            Mat4 bgVp = view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)Math.Max(_dist * 1e-3, 1e-5), (float)Math.Max(_dist * 1e5, 12));
            Draw(_starBuf, _stars.Length, bgVp, right, up, 0f, 0.0028f, 2f, w, h, time);            // galaxy stars: Populous-like (the chosen galaxy style)
            if (_structures.Length > 0) Draw(_structBuf, _structures.Length, bgVp, right, up, 1f, 0.0018f, 0f, w, h, time); // clusters, nebulas
            _gpu.ClearDepth();
            if (_pendingStar >= 0) DrawTargetRing(view, right, up, w, h, time);
            if (_focusStar < 0)
            {
                // the core sits at the galaxy's real centre, wherever the view is centred
                Mat4 atCore = Mat4.Translation((float)-_fx, (float)-_fy, (float)-_fz) * vp;
                Draw(_coreBuf, _bh.Glow.Length, atCore, right, up, 0f, 0.0016f, 0f, w, h, time);
                Mat4 spin = Mat4.RotationY((float)(time * 0.3)) * atCore;                         // the disk turns, gently (calm motion)
                Draw(_diskBuf, _bh.Disk.Length, spin, right, up, 1f, 0.0012f, 0f, w, h, time);
                // the core's jets belong to the core: shown only when you are at the core itself, never over the stars you fly to
                double toCore = Math.Sqrt(_fx * _fx + _fy * _fy + _fz * _fz);
                if (_bh.Jets.Length > 0 && _pendingStar < 0 && toCore < 0.05) Draw(_jetBuf, _bh.Jets.Length, atCore, right, up, 1f, 0.0012f, 0f, w, h, time);
            }
            else
            {
                UpdateBodies(time);
                Draw(_sysStatic, _system.Static.Length, vp, right, up, 0f, 0.002f, 1f, w, h, time); // orbits, belts, Oort cloud
                Draw(_sysBodies, _bodyCount, vp, right, up, 1f, 0.004f, 0f, w, h, time);    // the planets and moons: glows in their own colours
                DrawStar(vp, view, eye, right, up, w, h, time);
                DrawFocusPlanet(eye, w, h, time);
            }
        }

        // the system's star: a wide shine in its own colour from afar, and close up its moving plasma and corona (the set 3 look)
        private void DrawStar(Mat4 vp, Mat4 view, Vec3 eye, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (_starSurface == null) return;
            var sp = _bodyVerts[0];                                  // the star's place, relative to what the camera is centred on
            double R = _system.StarRadius;
            // the shine: three soft layers in the star's colour, never smaller than a little blur on screen
            var shine = new StarVertex[3];
            for (int k = 0; k < 3; k++) shine[k] = SpaceGen.Star(new Vec3(sp.X, sp.Y, sp.Z), (float)(R * (2 + k * 4)), _system.SR, _system.SG, _system.SB, 0.5f / (k + 1));
            if (_shineBuf == null) _shineBuf = new UploadBuffer(_gpu.Device, 3L * Stride);
            _shineBuf.Write(shine, 3);
            Draw(_shineBuf, 3, vp, right, up, 1f, 0.02f, 0f, w, h, time);
            // close enough to see its disc: the plasma surface and corona, scaled to the star's real size
            var toStar = new Vec3(sp.X - eye.X, sp.Y - eye.Y, sp.Z - eye.Z);
            double d = Math.Sqrt(Vec3.Dot(toStar, toStar));
            if (R / Math.Max(1e-30, d) < 0.004) return;
            var toEye = toStar * (float)(-1 / Math.Max(1e-30, d));
            int n = _starSurface.Draw(toEye, time, _starSurfVerts, 0);
            _starSurfBuf.Write(_starSurfVerts, n);
            var m = Mat4.Identity; m.M11 = m.M22 = m.M33 = (float)R; m.M41 = sp.X; m.M42 = sp.Y; m.M43 = sp.Z;
            Mat4 svp = m * vp;
            Draw(_starSurfBuf, n, svp, right, up, (float)R, 0f, 2f, w, h, time, true);
            Draw(_starCoronaBuf, _starCorona.Length, svp, right, up, (float)R, 0f, 2f, w, h, time, true);
        }

        // the planet being approached, as the real 3D planet, once it is more than a speck on screen
        private void DrawFocusPlanet(Vec3 eye, int w, int h, double time)
        {
            if (FocusTerrain == null || Meshes == null || _focusBody < 0) return;
            double rU = _system.Bodies[_focusBody].RadiusUnits, eyeD = Math.Sqrt(Vec3.Dot(eye, eye));
            if (rU / eyeD < 0.002) return;
            double kmPerUnit = FocusTerrain.Radius / rU;
            double ex = eye.X / eyeD, ey = eye.Y / eyeD, ez = eye.Z / eyeD, camKm = eyeD * kmPerUnit;
            Mat4 viewRot = Mat4.LookAt(new Vec3(0, 0, 0), new Vec3((float)-ex, (float)-ey, (float)-ez), new Vec3(0, 1, 0));
            Mat4 proj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)(camKm * 0.05), (float)(camKm * 4));
            _gpu.ClearDepth();
            FocusTerrain.Draw(Meshes, ex * camKm, ey * camKm, ez * camKm, viewRot, proj, time, 2.6, 0, 0, 0, 0, 0, 0, w, h, 24);
        }

        // a bright ring on screen around the star you picked, so you can see what you are flying to
        private UploadBuffer _ringBuf;
        private void DrawTargetRing(Mat4 view, Vec3 right, Vec3 up, int w, int h, double time)
        {
            var t = _starsRel[_pendingStar]; var ring = new StarVertex[32];
            var c = new Vec3(t.X, t.Y, t.Z);
            var eye = new Vec3((float)(Math.Cos(_yaw) * Math.Cos(_pitch) * _dist), (float)(Math.Sin(_pitch) * _dist), (float)(Math.Sin(_yaw) * Math.Cos(_pitch) * _dist));
            float d = (float)Math.Sqrt(Vec3.Dot(c - eye, c - eye)) * 0.03f;           // about the same size on screen at any distance
            for (int i = 0; i < 32; i++)
            {
                double a = i * Math.PI * 2 / 32 + time * 0.5;
                ring[i] = SpaceGen.Star(c + right * (float)(Math.Cos(a) * d) + up * (float)(Math.Sin(a) * d), d * 0.12f, 0.4f, 0.95f, 1f, 1f);
            }
            if (_ringBuf == null) _ringBuf = new UploadBuffer(_gpu.Device, 32L * Stride);
            _ringBuf.Write(ring, 32);
            Mat4 vp = view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)Math.Max(_dist * 1e-3, 1e-5), (float)Math.Max(_dist * 1e5, 12));
            Draw(_ringBuf, 32, vp, right, up, 1f, 0.004f, 2f, w, h, time);
        }

        private const int TrailPoints = 72;
        private StarVertex[] _staticShifted = new StarVertex[0];
        private int _bodyCount;

        private void UpdateBodies(double time)
        {
            // everything is placed relative to the planet in view (worked out in double precision), so it never shakes up close
            double fx, fy, fz;
            _bodyCount = SpaceGen.BodyVerts(_system, _simYears, _paused ? 0 : _speed / SecondsPerYearAt1x, MaxVisualTurnPerSecond, _bodyVerts, _focusBody, 1.0, out fx, out fy, out fz);
            _sysBodies.Write(_bodyVerts, _bodyCount);
            var st = _system.Static;
            for (int i = 0; i < st.Length; i++) { var v = st[i]; v.X = (float)(st[i].X - fx); v.Y = (float)(st[i].Y - fy); v.Z = (float)(st[i].Z - fz); _staticShifted[i] = v; }
            if (st.Length > 0) _sysStatic.Write(_staticShifted, st.Length);
        }

        private void Draw(UploadBuffer buf, int count, Mat4 vp, Vec3 right, Vec3 up, float sizeScale, float minSize, float style, int w, int h, double time, bool scaled)
        {
            // a scaled group (the star's surface): the size of each point is already in the group's own units
            Draw(buf, count, vp, right, up, scaled ? 1f : sizeScale, minSize, style, w, h, time);
        }

        private void Draw(UploadBuffer buf, int count, Mat4 vp, Vec3 right, Vec3 up, float sizeScale, float minSize, float style, int w, int h, double time)
        {
            if (buf == null || count <= 0) return;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = sizeScale; c[26] = style; c[27] = 4f; c[28] = w; c[29] = h; c[30] = minSize;
            _drawer.Draw(buf, count, c, 0, 0, w, h);
        }

        public string Title
        {
            get
            {
                double fromCore = Math.Sqrt(_fx * _fx + _fy * _fy + _fz * _fz);
                string focus = _focusStar < 0 && fromCore > _dist * 0.5
                    ? "looking at a part of the galaxy " + Units.Describe(fromCore) + " from its centre" + (_pendingStar >= 0 ? " · flying to the star in the ring" : " · roll in to go there, or click a star")
                    : _focusStar < 0
                    ? string.Format("focus: the core's supermassive black hole, {0:0.0e0} solar masses{1} · {2} star clusters, {3} nebulas",
                        _bh.MassSol, _bh.Active ? ", feeding (jets on)" : ", quiet", _clusters, _nebulas)
                    : _focusBody >= 0 ? "focus: " + _system.Bodies[_focusBody].Kind + " (keep rolling the wheel in to fly down to it)"
                    : string.Format("focus: {0} star · {1} planets · frost line {2:0.0} au · habitable zone {3:0.00} au",
                        _system.StarName, _system.Planets, _system.FrostLineAu, _system.HabitableAu);
                return string.Format(Universe.Shell.PreviewText.AppName + " deep zoom · {0} · view {1} across · {2} · time {3} · wheel zoom, drag rotate, Enter visit middle star, click a star in the galaxy or a planet in a system to pick it, then wheel in to fly to it, H core, 1-5 speed, Space pause, Esc = back",
                    _name, Units.Describe(_dist * 1.0), focus, _paused ? "paused" : _speed.ToString("N0") + "x");
            }
        }

        public void Dispose()
        {
            foreach (var b in new[] { _starBuf, _structBuf, _diskBuf, _jetBuf, _coreBuf, _sysStatic, _sysBodies, _starCoronaBuf, _starSurfBuf, _shineBuf, _ringBuf }) if (b != null) b.Dispose();
        }
    }
}
