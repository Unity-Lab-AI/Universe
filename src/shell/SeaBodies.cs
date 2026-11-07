// DNA-AND · shell · the planet's own sea animals, swimming around you at their true size.
// They are the water-going species of this region of the planet's life (jellies and finned animals), each built from its
// own evolved genes, so the sea changes as its life evolves. Only a few of each swim near you; each stands for a crowd.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DnaAnd.Core;
using DnaAnd.Gen;
using DnaAnd.Render;
using DnaAnd.Sim;

namespace DnaAnd.Shell
{
    internal sealed class SeaBodies : IDisposable
    {
        private sealed class Swimmer { public Species Sp; public double X, Y, Z; public Vec3 Dir; public double Phase; }
        private readonly IntPtr _device;
        private UploadBuffer _buf; private int _count; private StarVertex[] _verts = new StarVertex[0];
        private readonly Dictionary<int, StarVertex[]> _meshes = new Dictionary<int, StarVertex[]>();
        private readonly Dictionary<int, float> _span = new Dictionary<int, float>();
        private readonly List<Swimmer> _swim = new List<Swimmer>();
        private readonly Rng _rng = new Rng(4242);
        private List<Species> _here = new List<Species>();
        private double _nextCheck = -1;
        /// <summary>Names of the kinds swimming here, for the title.</summary>
        public string Here = "";
        /// <summary>True when the planet's own sea animals are shown (so the stock ones are not).</summary>
        public bool Showing { get { return _swim.Count > 0; } }

        public SeaBodies(IntPtr device) { _device = device; }

        /// <summary>Lives in water: jellies, and animals with fins.</summary>
        public static bool Swims(Genome g) { return g.Stage == 3 || (g.Stage >= 4 && g.Fins > 0.35f); }

        private StarVertex[] Mesh(Species sp, out float span)
        {
            StarVertex[] m;
            if (_meshes.TryGetValue(sp.Id, out m)) { span = _span[sp.Id]; return m; }
            m = BodyGen.Build(GeneBody.FromGenome(sp.G, (ulong)sp.Id * 7919UL + 1), 0, 3, false);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in m) { lo = Math.Min(lo, Math.Min(v.X, Math.Min(v.Y, v.Z))); hi = Math.Max(hi, Math.Max(v.X, Math.Max(v.Y, v.Z))); }
            span = Math.Max(1e-3f, hi - lo);
            _meshes[sp.Id] = m; _span[sp.Id] = span;
            return m;
        }

        public void Update(PlanetLife life, double cx, double cy, double cz, Vec3 up, Vec3 ahead, double viewKm, double time, double dt, float light)
        {
            if (life == null) { _swim.Clear(); _count = 0; Here = ""; return; }
            if (time >= _nextCheck)
            {
                _nextCheck = time + 3;                                                    // species evolve: look again every few seconds
                double r0 = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                _here = new List<Species>();
                foreach (var kv in life.At(cx / r0, cy / r0, cz / r0)) if (Swims(kv.Key.G)) { _here.Add(kv.Key); if (_here.Count >= 6) break; }
                _swim.RemoveAll(s => !_here.Contains(s.Sp));
            }
            // keep a few of each kind near you, if it is near the size you are looking at
            var names = new List<string>();
            foreach (var sp in _here)
            {
                double size = LandLife.RealSize(sp.G);
                bool fits = size >= viewKm * 0.02 && size <= viewKm * 60;
                if (!fits) { _swim.RemoveAll(s => s.Sp == sp); continue; }
                string nm = sp.G.Stage == 3 ? "jellies" : PlanetLife.StageNames[sp.G.Stage] + " (swimmers)";
                if (!names.Contains(nm)) names.Add(nm);
                int have = 0; foreach (var s in _swim) if (s.Sp == sp) have++;
                int want = size > viewKm * 5 ? 2 : 8;
                for (; have < want; have++)
                {
                    double r = size * _rng.Range(2.5f, 10f);
                    var side = Vec3.Cross(ahead, up).Normalized; var lift = Vec3.Cross(side, ahead).Normalized;
                    var at = ahead * (float)r + side * (float)(r * _rng.Range(-0.6f, 0.6f)) + lift * (float)(r * _rng.Range(-0.3f, 0.3f));
                    var d = (side * _rng.Range(-1f, 1f) + ahead * _rng.Range(-1f, 1f) + up * _rng.Range(-0.15f, 0.15f)).Normalized;
                    _swim.Add(new Swimmer { Sp = sp, X = cx + at.X, Y = cy + at.Y, Z = cz + at.Z, Dir = d, Phase = _rng.NextDouble() * 6.28 });
                }
            }
            Here = string.Join(", ", names.ToArray());
            // swim; those left behind or far off go, so new ones come in ahead
            _swim.RemoveAll(s =>
            {
                double size = LandLife.RealSize(s.Sp.G), dx = s.X - cx, dy = s.Y - cy, dz = s.Z - cz;
                if (dx * ahead.X + dy * ahead.Y + dz * ahead.Z < -size) return true;
                return dx * dx + dy * dy + dz * dz > size * size * 400;
            });
            var outl = new List<StarVertex>();
            foreach (var s in _swim)
            {
                double size = LandLife.RealSize(s.Sp.G);
                bool jelly = s.Sp.G.Stage == 3;
                double speed = size * (jelly ? 0.15 : 0.6) * (0.5 + s.Sp.G.Speed);           // body lengths a second
                s.X += s.Dir.X * speed * dt; s.Y += s.Dir.Y * speed * dt; s.Z += s.Dir.Z * speed * dt; s.Phase += dt * (jelly ? 1.5 : 5);
                if (_rng.NextDouble() < dt * 0.2) s.Dir = (s.Dir + new Vec3(_rng.Range(-0.5f, 0.5f), _rng.Range(-0.1f, 0.1f), _rng.Range(-0.5f, 0.5f))).Normalized;
                float span; var mesh = Mesh(s.Sp, out span);
                float scale = (float)(size / span);
                var fx = s.Dir; var fz = Vec3.Cross(fx, up).Normalized; var fy = Vec3.Cross(fz, fx).Normalized;
                var c = new Vec3((float)(s.X - cx), (float)(s.Y - cy), (float)(s.Z - cz));
                float sway = (float)Math.Sin(s.Phase) * 0.12f, pulse = jelly ? 1 + (float)Math.Sin(s.Phase) * 0.08f : 1;
                foreach (var v in mesh)
                {
                    float bx = v.X * scale, by = v.Y * scale * pulse, bz = (v.Z + (jelly ? 0 : sway * Math.Max(0, -v.X) * Math.Max(0, -v.X))) * scale * pulse;   // the tail sweeps side to side
                    var p = c + fx * bx + fy * by + fz * bz;
                    outl.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = v.Size, R = v.R * light, G = v.G * light, B = v.B * light, A = 1 });
                }
            }
            _count = outl.Count;
            if (_count == 0) return;
            if (_verts.Length < _count) _verts = new StarVertex[_count + 4096];
            outl.CopyTo(_verts);
            if (_buf == null || _buf.Bytes < (long)_count * Marshal.SizeOf(typeof(StarVertex)))
            {
                if (_buf != null) _buf.Dispose();
                _buf = new UploadBuffer(_device, (long)(_count + 4096) * Marshal.SizeOf(typeof(StarVertex)) * 2);
            }
            _buf.Write(_verts, _count);
        }

        public void Draw(MeshPipes pipes, Mat4 view, Mat4 proj, double[] starDir, int width, int height)
        {
            if (_count == 0 || _buf == null) return;
            Mat4 vp = view * proj;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = (float)starDir[0]; c[17] = (float)starDir[1]; c[18] = (float)starDir[2];
            pipes.Solid.Draw(_buf, _count, c, 0, 0, width, height);
        }

        public void Dispose() { if (_buf != null) _buf.Dispose(); }
    }
}
