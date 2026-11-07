// Universe · shell · the planet's own living things on land, at their true size, around wherever you are.
// What grows and walks here is the species of this region of the planet's life: their bodies are built from their
// own evolved genes, so as they evolve, what you see changes. Where an intelligent species lives, its village stands here.
// Only a handful of each are drawn (each stands for a whole crowd), and only near the ground.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;
using Universe.Sim;

namespace Universe.Shell
{
    internal sealed class LandLife : IDisposable
    {
        private readonly IntPtr _device;
        private UploadBuffer _buf; private int _count; private StarVertex[] _verts = new StarVertex[0];
        private readonly Dictionary<int, StarVertex[]> _meshes = new Dictionary<int, StarVertex[]>();   // species id -> its body, in body units
        private readonly Dictionary<int, float[]> _fit = new Dictionary<int, float[]>();                // species id -> ground level, height, length
        private double _ax, _ay, _az, _aSize = -1; private string _key = "";
        private double _nextCheck;
        /// <summary>What is here, for the title: names of the kinds shown.</summary>
        public string Here = "";
        /// <summary>Test runs: show this step of an intelligent species' growth (0 wild to 5 space) instead of its own.</summary>
        public int ForceStep = -1;

        public LandLife(IntPtr device) { _device = device; }

        // how big a species really is, in km, from its stage and size gene
        internal static double RealSize(Genome g)
        {
            if (g.Stage == 2) return g.Photo > 0.5f ? 0.002 + g.Size * 0.006 : 0.00008 + g.Size * 0.0001;   // plants a few metres, fungi a hand's height
            return (g.Stage >= 6 ? 0.0012 : 0.0002 + g.Size * 0.0007) * (0.6 + g.Segs);                      // animals a hand to a few metres
        }

        private StarVertex[] Mesh(Species sp, out float[] fit)
        {
            StarVertex[] m;
            if (_meshes.TryGetValue(sp.Id, out m)) { fit = _fit[sp.Id]; return m; }
            m = MakeMesh(sp, out fit);
            _meshes[sp.Id] = m; _fit[sp.Id] = fit;
            return m;
        }

        // one species' body from its genes; touches nothing shared, so many can be built at once
        private static StarVertex[] MakeMesh(Species sp, out float[] fit)
        {
            StarVertex[] m;
            var body = GeneBody.FromGenome(sp.G, (ulong)sp.Id * 7919UL + 1);
            m = BodyGen.Build(body, 0, 3, false);
            float lo = float.MaxValue, hi = float.MinValue, x0 = float.MaxValue, x1 = float.MinValue;
            foreach (var v in m) { lo = Math.Min(lo, v.Y); hi = Math.Max(hi, v.Y); x0 = Math.Min(x0, v.X); x1 = Math.Max(x1, v.X); }
            fit = new[] { float.IsNaN(body.GroundY) ? lo : body.GroundY, Math.Max(1e-3f, hi - lo), Math.Max(1e-3f, x1 - x0) };
            return m;
        }

        /// <summary>Rebuilds the scene when you have moved far enough, the zoom has changed a lot, or the species here have changed.</summary>
        public void Update(PlanetLife life, PlanetTerrain terrain, double cx, double cy, double cz, double shown, double time)
        {
            if (life == null || shown > 0.5 || shown < 2e-6) { _count = 0; Here = ""; return; }
            double r = Math.Sqrt(cx * cx + cy * cy + cz * cz), ux = cx / r, uy = cy / r, uz = cz / r;
            double moved = Math.Sqrt((cx - _ax) * (cx - _ax) + (cy - _ay) * (cy - _ay) + (cz - _az) * (cz - _az));
            bool zoomed = _aSize < 0 || shown > _aSize * 3 || shown < _aSize / 3;
            if (!zoomed && moved < shown * 4 && time < _nextCheck) return;
            _nextCheck = time + 3;                                                    // species evolve: look again every few seconds
            var local = life.At(ux, uy, uz);
            var shownSp = new List<KeyValuePair<Species, double>>();
            foreach (var kv in local)
            {
                var g = kv.Key.G;
                if (g.Stage < 2 || (g.Stage == 2 && g.Photo <= 0.5f && !(g.Graze > 0.5f && g.Speed < 0.3f))) continue;   // land: plants, fungi, animals
                if (SeaBodies.Swims(g) && g.Legs < 0.3f) continue;                       // sea animals: drawn in the water, not on land
                double size = RealSize(g);
                if (size < shown * 0.01 || size > shown * 400) continue;             // too small to see from here, or too big to fit the view
                shownSp.Add(kv); if (shownSp.Count >= 8) break;
            }
            var keyB = new System.Text.StringBuilder(); foreach (var kv in shownSp) keyB.Append(kv.Key.Id).Append(',');
            int civ = -1; foreach (var kv in local) if (kv.Key.G.Stage >= 6) { civ = kv.Key.Id; break; }
            int step = civ > 0 ? (ForceStep >= 0 ? ForceStep : life.CivStep(life.All[civ])) : 0;
            keyB.Append(civ).Append(':').Append(step);
            if (!zoomed && moved < shown * 4 && keyB.ToString() == _key) return;
            _key = keyB.ToString(); _ax = cx; _ay = cy; _az = cz; _aSize = shown;

            // bodies not seen before are built on every core at once (each builds only its own), then kept
            var fresh = new List<Species>(); foreach (var kv in shownSp) if (!_meshes.ContainsKey(kv.Key.Id)) fresh.Add(kv.Key);
            if (fresh.Count > 0)
            {
                var built = new StarVertex[fresh.Count][]; var fits = new float[fresh.Count][];
                Jobs.For(fresh.Count, i => built[i] = MakeMesh(fresh[i], out fits[i]));
                for (int i = 0; i < fresh.Count; i++) { _meshes[fresh[i].Id] = built[i]; _fit[fresh[i].Id] = fits[i]; }
            }
            // a flat frame on the ground here: east and north along the surface
            var up = new Vec3((float)ux, (float)uy, (float)uz);
            var north = Vec3.Cross(Math.Abs(uy) < 0.99 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), up); north = Vec3.Cross(up, north).Normalized;
            var east = Vec3.Cross(north, up).Normalized;
            var outl = new List<StarVertex>();
            var names = new List<string>();
            var rng = new Rng((ulong)(Math.Abs(ux) * 1e6 + Math.Abs(uz) * 3e6) + 11);
            double R = terrain.Radius;
            foreach (var kv in shownSp)
            {
                float[] fit; var mesh = Mesh(kv.Key, out fit);
                double size = RealSize(kv.Key.G);
                float scale = (float)(size / Math.Max(fit[1], fit[2]));
                int n = 3 + Math.Min(9, (int)(Math.Log10(1 + kv.Value) * 4));                 // more of the common ones
                bool plant = kv.Key.G.Stage == 2 && kv.Key.G.Photo > 0.5f;
                if (plant) n *= 4;                                                             // plants grow thick; animals go about in smaller groups
                double spread = Math.Max(size * (plant ? 10 : 6), shown * 3);
                string nm = PlanetLife.StageNames[kv.Key.G.Stage] + (kv.Key.G.Stage == 2 ? (kv.Key.G.Photo > 0.5f ? " (plants)" : " (fungi)") : "");
                if (!names.Contains(nm)) names.Add(nm);
                for (int k = 0; k < n; k++)
                    Place(outl, mesh, fit[0], scale, terrain, R, ux, uy, uz, up, north, east, rng.Range(-1f, 1f) * spread, rng.Range(-0.2f, 1.6f) * spread, rng.Range(0, 6.283f), cx, cy, cz);
            }
            if (civ > 0 && step > 0 && shown < 0.3 && shown > 0.0005)
            {
                // where an intelligent species lives and has settled: its tribe's camp, village, town or city, in front of you;
                // once it has gone into space, a ship of its own hangs over its city
                var sp = life.All[civ];
                int what = Math.Min(4, step);                                           // CivGen: 1 camp, 2 village, 3 town, 4 city
                var body = CivGen.Build(sp.G, what, (ulong)civ);
                var m = BodyGen.Build(body, 0, 3, false);
                float lo = float.MaxValue, hi = float.MinValue; foreach (var v in m) { lo = Math.Min(lo, v.X); hi = Math.Max(hi, v.X); }
                float sc = (float)((0.12 + what * 0.05) / Math.Max(1e-3f, hi - lo));
                Place(outl, m, body.GroundY, sc, terrain, R, ux, uy, uz, up, north, east, 0, 0.35, 0.3, cx, cy, cz);
                if (step >= 5)
                {
                    var ship = BodyGen.Build(CivGen.Build(sp.G, 5, (ulong)civ), 0, 3, false);
                    Place(outl, ship, -1.6f, sc * 0.5f, terrain, R, ux, uy, uz, up, north, east, 0.05, 0.3, 1.2, cx, cy, cz);
                }
                names.Add(PlanetLife.CivSteps[step] + " of " + PlanetLife.StageNames[sp.G.Stage]);
            }
            else if (civ > 0) names.Add(PlanetLife.CivSteps[0] + ": " + PlanetLife.StageNames[life.All[civ].G.Stage]);
            Here = string.Join(", ", names.ToArray());
            _count = outl.Count;
            if (_verts.Length < _count) _verts = new StarVertex[_count + 1024];
            outl.CopyTo(_verts);
            if (_buf == null || _buf.Bytes < (long)_count * Marshal.SizeOf(typeof(StarVertex)))
            {
                if (_buf != null) _buf.Dispose();
                _buf = new UploadBuffer(_device, (long)(_count + 1024) * Marshal.SizeOf(typeof(StarVertex)) * 2);
            }
            _buf.Write(_verts, _count);
        }

        // one copy of a body standing on the real ground at (x east, y north) km from here, turned to face its own way
        private static void Place(List<StarVertex> outl, StarVertex[] mesh, float groundY, float scale, PlanetTerrain terrain, double R, double ux, double uy, double uz,
            Vec3 up, Vec3 north, Vec3 east, double x, double y, double turn, double cx, double cy, double cz)
        {
            double dx = ux + (east.X * x + north.X * y) / R, dy = uy + (east.Y * x + north.Y * y) / R, dz = uz + (east.Z * x + north.Z * y) / R;
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); dx /= l; dy /= l; dz /= l;
            double g = terrain.GroundAt(dx, dy, dz);
            if (g < 0 && terrain.HasWater) return;                                     // land life does not stand in the sea
            double wr = R + g;
            // the spot, relative to the camera (small numbers, so floats stay exact)
            float px = (float)(dx * wr - cx), py = (float)(dy * wr - cy), pz = (float)(dz * wr - cz);
            var u2 = new Vec3((float)dx, (float)dy, (float)dz);
            float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
            var fx = east * c + north * s; var fz = Vec3.Cross(fx, u2);
            foreach (var v in mesh)
            {
                float bx = v.X * scale, by = (v.Y - groundY) * scale, bz = v.Z * scale;
                var p = new Vec3(px, py, pz) + fx * bx + u2 * by + fz * bz;
                outl.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = v.Size, R = v.R, G = v.G, B = v.B, A = 1 });
            }
        }

        public void Draw(MeshPipes pipes, Mat4 view, Mat4 proj, double[] starDir, double cx, double cy, double cz, int width, int height)
        {
            if (_count == 0 || _buf == null) return;
            // the bodies were laid out around where you were when they were built; move them by how far you have come since
            Mat4 vp = Mat4.Translation((float)(_ax - cx), (float)(_ay - cy), (float)(_az - cz)) * view * proj;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = (float)starDir[0]; c[17] = (float)starDir[1]; c[18] = (float)starDir[2];
            c[20] = (float)(cx - _ax); c[21] = (float)(cy - _ay); c[22] = (float)(cz - _az);
            pipes.Solid.Draw(_buf, _count, c, 0, 0, width, height);
        }

        public void Dispose() { if (_buf != null) _buf.Dispose(); }
    }
}
