// DNA-AND · shell · the start of the journey: a mix of galaxies spread through space, each built live from content/galaxies.dnat.
// Click a galaxy (or Tab for the next one) to centre on it, then roll the wheel in to fly into it.
// Drag to turn the view. Esc closes.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DnaAnd.Core;
using DnaAnd.Gen;
using DnaAnd.Platform;
using DnaAnd.Render;

namespace DnaAnd.Shell
{
    internal sealed class GalaxiesView : IDisposable
    {
        public sealed class Galaxy { public string Type, Name; public ulong Seed; public double X, Y, Z; public float Tilt, Turn, Scale; }
        public readonly List<Galaxy> Galaxies = new List<Galaxy>();
        private readonly BillboardDrawer _dots;
        private readonly UploadBuffer _buf;
        private readonly StarVertex[] _all;       // every galaxy's stars, placed in the universe
        private StarVertex[] _rel;
        private int _focus = 0;
        private double _dist = 45, _yaw = 0.4, _pitch = 0.35;
        private double _fx, _fy, _fz;             // where the camera is centred
        /// <summary>Set when the camera is close enough to a galaxy to fly into it: its index (else -1).</summary>
        public int HandOff = -1;
        private const int StarsEach = 2500;
        private const double Spread = 120;        // the universe patch, in galaxy units (one unit is 50,000 light-years)

        public GalaxiesView(Gpu gpu, BillboardPipeline pipe, ContentLibrary content, ulong seed)
        {
            _dots = new BillboardDrawer(gpu.CommandList, pipe);
            var types = new List<string>(); var names = new List<string>();
            foreach (var e in content.OfKind("galaxies.dnat", "galaxy")) { types.Add(e.Id); names.Add(e.Name); }
            if (types.Count == 0) { types.Add("spiral"); names.Add("Spiral galaxy"); }
            var rng = new Rng(seed ^ 0x6A1AUL);
            var kinds = GalaxyGen.StarKinds(content);
            var pts = new List<StarVertex>();
            for (int i = 0; i < 36; i++)
            {
                int t = i == 0 ? Math.Max(0, types.IndexOf("spiral")) : rng.Below(types.Count);
                var g = new Galaxy { Type = types[t], Name = names[t], Seed = seed + (ulong)i * 104729UL,
                    X = i == 0 ? 0 : rng.Range(-1, 1) * Spread, Y = i == 0 ? 0 : rng.Range(-0.35f, 0.35f) * Spread, Z = i == 0 ? 0 : rng.Range(-1, 1) * Spread,
                    Tilt = rng.Range(-1.2f, 1.2f), Turn = rng.Range(0, 6.28f), Scale = types[t].StartsWith("dwarf") ? 0.35f : types[t].StartsWith("giant") ? 1.6f : rng.Range(0.8f, 1.2f) };
                Galaxies.Add(g);
                var stars = GalaxyGen.Generate(g.Type, g.Seed, StarsEach, kinds);
                float ct = (float)Math.Cos(g.Tilt), st = (float)Math.Sin(g.Tilt), cu = (float)Math.Cos(g.Turn), su = (float)Math.Sin(g.Turn);
                foreach (var s0 in stars)
                {
                    var s = s0;
                    float x = s.X * g.Scale, y = s.Y * g.Scale, z = s.Z * g.Scale;
                    float y2 = y * ct - z * st, z2 = y * st + z * ct;              // tilt
                    float x3 = x * cu - z2 * su, z3 = x * su + z2 * cu;           // turn
                    s.X = (float)(x3 + g.X); s.Y = (float)(y2 + g.Y); s.Z = (float)(z3 + g.Z);
                    pts.Add(s);
                }
            }
            _all = pts.ToArray(); _rel = new StarVertex[_all.Length];
            _buf = new UploadBuffer(gpu.Device, (long)_all.Length * Marshal.SizeOf(typeof(StarVertex)));
            Centre(0);
        }

        private void Centre(int i)
        {
            _focus = i; var g = Galaxies[i]; _fx = g.X; _fy = g.Y; _fz = g.Z;
            for (int k = 0; k < _all.Length; k++) { var v = _all[k]; v.X = (float)(_all[k].X - _fx); v.Y = (float)(_all[k].Y - _fy); v.Z = (float)(_all[k].Z - _fz); _rel[k] = v; }
            _buf.Write(_rel, _rel.Length);
        }

        public Galaxy Focus { get { return Galaxies[_focus]; } }

        /// <summary>The camera's angle as seen from the focused galaxy's own frame (its tilt and turn undone), so flying
        /// into it carries on at the same angle instead of turning flat.</summary>
        public void AngleInFocus(out float yaw, out float pitch)
        {
            var g = Galaxies[_focus];
            double ex = Math.Cos(_yaw) * Math.Cos(_pitch), ey = Math.Sin(_pitch), ez = Math.Sin(_yaw) * Math.Cos(_pitch);
            double ct = Math.Cos(g.Tilt), st = Math.Sin(g.Tilt), cu = Math.Cos(g.Turn), su = Math.Sin(g.Turn);
            double x1 = ex * cu + ez * su, z1 = -ex * su + ez * cu;                   // undo the turn
            double y2 = ey * ct + z1 * st, z2 = -ey * st + z1 * ct;                   // undo the tilt
            yaw = (float)Math.Atan2(z2, x1); pitch = (float)Math.Asin(Math.Max(-1, Math.Min(1, y2)));
        }

        /// <summary>Back out of a galaxy: hover just outside it.</summary>
        public void ReturnFromGalaxy() { HandOff = -1; _dist = 5; }

        public string Title
        {
            get
            {
                var g = Galaxies[_focus];
                return "DNA-AND · the universe · " + Galaxies.Count + " galaxies in view · centred on: " + g.Name + " · view " + Units.Describe(_dist) +
                       " across · click a galaxy or Tab to pick it · wheel in to fly into it · drag to turn · Esc: close";
            }
        }

        public bool Update(Input input, double dt, int w, int h)
        {
            if (input.WasPressed(Win32.VK_ESCAPE)) return false;
            if (input.LeftDown) { _yaw += input.MouseDX * 0.005; _pitch = Math.Max(-1.4, Math.Min(1.4, _pitch + input.MouseDY * 0.005)); }
            if (input.WasPressed(0x09)) { Centre((_focus + 1) % Galaxies.Count); _dist = Math.Max(_dist, 12); }
            if (input.LeftClicked) Click(input.MouseX, input.MouseY, w, h);
            if (input.Wheel > 0) ZoomToward(input.MouseX, input.MouseY, w, h);      // zoom into the galaxy under the mouse
            if (input.Wheel != 0) _dist = Math.Max(3.5, Math.Min(400, _dist * Math.Pow(0.87, input.Wheel)));
            if (input.Wheel > 0 && _dist <= 4.0) HandOff = _focus;            // close enough: fly into it
            return true;
        }

        private Mat4 _vp;

        private void ZoomToward(int mx, int my, int w, int h)
        {
            int keep = _focus; double keepDist = _dist;
            Click(mx, my, w, h);
            if (_focus != keep) _dist = Math.Min(keepDist, Math.Max(12, keepDist));   // a new galaxy: keep the distance, carry on in toward it
        }

        private void Click(int mx, int my, int w, int h)
        {
            float x = mx / (float)Math.Max(1, w) * 2 - 1, y = 1 - my / (float)Math.Max(1, h) * 2;
            int best = -1; float bd = 0.08f;
            for (int i = 0; i < Galaxies.Count; i++)
            {
                var g = Galaxies[i];
                float gx = (float)(g.X - _fx), gy = (float)(g.Y - _fy), gz = (float)(g.Z - _fz);
                float cx = gx * _vp.M11 + gy * _vp.M21 + gz * _vp.M31 + _vp.M41, cy = gx * _vp.M12 + gy * _vp.M22 + gz * _vp.M32 + _vp.M42, cw = gx * _vp.M14 + gy * _vp.M24 + gz * _vp.M34 + _vp.M44;
                if (cw <= 0) continue;
                float d = (float)Math.Sqrt((cx / cw - x) * (cx / cw - x) + (cy / cw - y) * (cy / cw - y));
                if (d < bd) { bd = d; best = i; }
            }
            if (best >= 0) { Centre(best); _dist = Math.Min(_dist, 30); }
        }

        public void Draw(int w, int h, double time)
        {
            var eye = new Vec3((float)(Math.Cos(_yaw) * Math.Cos(_pitch) * _dist), (float)(Math.Sin(_pitch) * _dist), (float)(Math.Sin(_yaw) * Math.Cos(_pitch) * _dist));
            Mat4 view = Mat4.LookAt(eye, new Vec3(0, 0, 0), new Vec3(0, 1, 0));
            _vp = view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)(_dist * 1e-3), (float)(_dist * 20 + Spread * 4));
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = _vp.M11; c[1] = _vp.M12; c[2] = _vp.M13; c[3] = _vp.M14; c[4] = _vp.M21; c[5] = _vp.M22; c[6] = _vp.M23; c[7] = _vp.M24;
            c[8] = _vp.M31; c[9] = _vp.M32; c[10] = _vp.M33; c[11] = _vp.M34; c[12] = _vp.M41; c[13] = _vp.M42; c[14] = _vp.M43; c[15] = _vp.M44;
            c[16] = view.M11; c[17] = view.M21; c[18] = view.M31; c[20] = view.M12; c[21] = view.M22; c[22] = view.M32;
            c[24] = (float)time; c[25] = 0.006f; c[26] = 2; c[27] = 4; c[28] = w; c[29] = h; c[30] = 0.0016f;
            _dots.Draw(_buf, _all.Length, c, 0, 0, w, h);
        }

        public void Dispose() { _buf.Dispose(); }
    }
}
