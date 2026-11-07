// Universe · generators · the body builder: every living thing and built thing is put together from simple parts
// (round blobs, boxes and cones), and the same parts can be drawn in three looks:
//   0 smooth: finely cut, so it reads as soft rounded clay
//   1 chunky: few flat faces, the classic low-poly look
//   2 blocky: filled in with small cubes, like building blocks
// Every subject is made from a seed, so no two come out the same.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    /// <summary>One part of a body: a blob (0), box (1) or cone (2, base at -V, tip at +V), with its own axes and size.</summary>
    internal struct Part
    {
        public int Kind; public Vec3 C, U, V, W; public float SU, SV, SW; public float R, G, B, Glow;
        public bool IsGround;     // the patch of ground a sample stands on; left out when the body stands on a real world

        public bool Inside(Vec3 p)
        {
            var d = p - C;
            float x = Vec3.Dot(d, U) / SU, y = Vec3.Dot(d, V) / SV, z = Vec3.Dot(d, W) / SW;
            if (Kind == 0) return x * x + y * y + z * z <= 1;
            if (Kind == 1) return Math.Abs(x) <= 1 && Math.Abs(y) <= 1 && Math.Abs(z) <= 1;
            return y >= -1 && y <= 1 && Math.Sqrt(x * x + z * z) <= (1 - y) * 0.5;
        }
    }

    internal sealed class Body
    {
        public readonly List<Part> Parts = new List<Part>();
        public string Name;
        public float Radius = 1;          // roughly how big it is, for the camera
        public float GroundY = float.NaN; // where its feet, roots or foundations meet the ground (NaN: it floats)

        /// <summary>The patch of ground a sample stands on: a flat blob whose top is the ground level.</summary>
        public void GroundDisc(Vec3 c, float sx, float sy, float sz, float r, float g, float b)
        { Blob(c, sx, sy, sz, r, g, b); var p = Parts[Parts.Count - 1]; p.IsGround = true; Parts[Parts.Count - 1] = p; GroundY = c.Y + sy; }
        public void GroundSlab(Vec3 c, float sx, float sy, float sz, float r, float g, float b)
        { Box(c, sx, sy, sz, r, g, b); var p = Parts[Parts.Count - 1]; p.IsGround = true; Parts[Parts.Count - 1] = p; GroundY = c.Y + sy; }

        private static Vec3 Up = new Vec3(0, 1, 0);

        public void Blob(Vec3 c, float sx, float sy, float sz, float r, float g, float b, float glow = 0)
        { Parts.Add(new Part { Kind = 0, C = c, U = new Vec3(1, 0, 0), V = Up, W = new Vec3(0, 0, 1), SU = sx, SV = sy, SW = sz, R = r, G = g, B = b, Glow = glow }); }

        public void Box(Vec3 c, float sx, float sy, float sz, float r, float g, float b, float glow = 0, float turn = 0)
        {
            var u = new Vec3((float)Math.Cos(turn), 0, (float)Math.Sin(turn)); var w = Vec3.Cross(u, Up);
            Parts.Add(new Part { Kind = 1, C = c, U = u, V = Up, W = w, SU = sx, SV = sy, SW = sz, R = r, G = g, B = b, Glow = glow });
        }

        /// <summary>A cone standing on its base at <paramref name="base0"/>, pointing along <paramref name="dir"/>.</summary>
        public void Cone(Vec3 base0, Vec3 dir, float length, float radius, float r, float g, float b, float glow = 0)
        {
            var v = dir.Normalized; Vec3 u, w; Frame(v, out u, out w);
            Parts.Add(new Part { Kind = 2, C = base0 + v * (length * 0.5f), U = u, V = v, W = w, SU = radius, SV = length * 0.5f, SW = radius, R = r, G = g, B = b, Glow = glow });
        }

        /// <summary>A limb or stalk: a stretched blob from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void Limb(Vec3 a, Vec3 b, float radius, float r, float g, float bl, float glow = 0)
        {
            var d = b - a; float len = d.Length; if (len < 1e-6f) return;
            var v = d * (1f / len); Vec3 u, w; Frame(v, out u, out w);
            Parts.Add(new Part { Kind = 0, C = (a + b) * 0.5f, U = u, V = v, W = w, SU = radius, SV = len * 0.5f + radius * 0.4f, SW = radius, R = r, G = g, B = bl, Glow = glow });
        }

        private static void Frame(Vec3 v, out Vec3 u, out Vec3 w)
        {
            u = Vec3.Cross(v, Math.Abs(v.Y) < 0.9f ? Up : new Vec3(1, 0, 0)).Normalized; w = Vec3.Cross(u, v);
        }
    }

    internal static class BodyGen
    {
        public static readonly string[] LookNames = { "smooth (soft rounded clay)", "chunky (few flat faces)", "blocky (small cubes)" };

        // ── turning parts into triangles ──

        public static StarVertex[] Build(Body body, int look) { return Build(body, look, 7, true); }

        /// <summary><paramref name="split"/>: how finely smooth blobs are cut (lower for crowds in the world);
        /// <paramref name="withGround"/>: false leaves out the sample's patch of ground.</summary>
        public static StarVertex[] Build(Body body, int look, int split, bool withGround)
        {
            var l = new List<StarVertex>();
            if (look == 2) { Blocks(body, l); return l.ToArray(); }
            foreach (var p in body.Parts)
            {
                if (p.IsGround && !withGround) continue;
                if (p.Kind == 0) Ellipsoid(l, p, look == 0 ? split : 1, look == 1);
                else if (p.Kind == 1) BoxMesh(l, p, look == 1);
                else ConeMesh(l, p, look == 0 ? split * 5 : 6, look == 1);
            }
            return l.ToArray();
        }

        private static StarVertex V(Vec3 p, float r, float g, float b, float glow) { return new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = glow, R = r, G = g, B = b, A = 1 }; }

        private static void Tri(List<StarVertex> l, Vec3 a, Vec3 b, Vec3 c, Part p, float k)
        {
            float r = Math.Min(1, p.R * k), g = Math.Min(1, p.G * k), bl = Math.Min(1, p.B * k);
            l.Add(V(a, r, g, bl, p.Glow)); l.Add(V(b, r, g, bl, p.Glow)); l.Add(V(c, r, g, bl, p.Glow));
        }

        private static Vec3 Place(Part p, Vec3 local) { return p.C + p.U * (local.X * p.SU) + p.V * (local.Y * p.SV) + p.W * (local.Z * p.SW); }

        // a chunky face gets a slightly different shade from its neighbours, like hand-painted facets
        private static float Shade(Vec3 m, bool chunky) { return chunky ? 0.9f + 0.2f * Noise.Value(m.X * 37, m.Y * 37, m.Z * 37, 7) : 1; }

        private static readonly float T = 1.618034f;
        private static readonly Vec3[] IcoV = { new Vec3(-1, T, 0), new Vec3(1, T, 0), new Vec3(-1, -T, 0), new Vec3(1, -T, 0), new Vec3(0, -1, T), new Vec3(0, 1, T), new Vec3(0, -1, -T), new Vec3(0, 1, -T), new Vec3(T, 0, -1), new Vec3(T, 0, 1), new Vec3(-T, 0, -1), new Vec3(-T, 0, 1) };
        private static readonly int[] IcoF = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

        private static void Ellipsoid(List<StarVertex> l, Part p, int split, bool chunky)
        {
            for (int f = 0; f < 20; f++)
            {
                Vec3 A = IcoV[IcoF[f * 3]].Normalized, B = IcoV[IcoF[f * 3 + 1]].Normalized, C = IcoV[IcoF[f * 3 + 2]].Normalized;
                for (int i = 0; i < split; i++)
                    for (int j = 0; j < split - i; j++)
                    {
                        Vec3 p0 = L(A, B, C, i, j, split), p1 = L(A, B, C, i + 1, j, split), p2 = L(A, B, C, i, j + 1, split);
                        Tri(l, Place(p, p0), Place(p, p1), Place(p, p2), p, Shade(p0 + p1 + p2 + p.C, chunky));
                        if (j < split - i - 1)
                        {
                            Vec3 p3 = L(A, B, C, i + 1, j + 1, split);
                            Tri(l, Place(p, p1), Place(p, p3), Place(p, p2), p, Shade(p1 + p3 + p2 + p.C, chunky));
                        }
                    }
            }
        }
        private static Vec3 L(Vec3 A, Vec3 B, Vec3 C, int i, int j, int n) { float u = i / (float)n, v = j / (float)n; return (A * (1 - u - v) + B * u + C * v).Normalized; }

        private static void BoxMesh(List<StarVertex> l, Part p, bool chunky)
        {
            Vec3[] c = new Vec3[8];
            for (int i = 0; i < 8; i++) c[i] = Place(p, new Vec3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
            int[] q = { 0,1,3,2, 4,6,7,5, 0,4,5,1, 2,3,7,6, 0,2,6,4, 1,5,7,3 };
            for (int f = 0; f < 6; f++)
            {
                float k = Shade(c[q[f * 4]] + c[q[f * 4 + 2]], chunky);
                Tri(l, c[q[f * 4]], c[q[f * 4 + 1]], c[q[f * 4 + 2]], p, k); Tri(l, c[q[f * 4]], c[q[f * 4 + 2]], c[q[f * 4 + 3]], p, k);
            }
        }

        private static void ConeMesh(List<StarVertex> l, Part p, int seg, bool chunky)
        {
            var tip = Place(p, new Vec3(0, 1, 0)); var mid = Place(p, new Vec3(0, -1, 0));
            for (int i = 0; i < seg; i++)
            {
                double a0 = i * Math.PI * 2 / seg, a1 = (i + 1) * Math.PI * 2 / seg;
                var b0 = Place(p, new Vec3((float)Math.Cos(a0), -1, (float)Math.Sin(a0))); var b1 = Place(p, new Vec3((float)Math.Cos(a1), -1, (float)Math.Sin(a1)));
                Tri(l, b0, b1, tip, p, Shade(b0 + b1, chunky)); Tri(l, mid, b1, b0, p, 0.8f);
            }
        }

        // blocky: the whole body filled with small cubes, only the outside faces drawn
        private static void Blocks(Body body, List<StarVertex> l)
        {
            if (body.Parts.Count == 0) return;
            Vec3 lo = new Vec3(1e9f, 1e9f, 1e9f), hi = new Vec3(-1e9f, -1e9f, -1e9f);
            foreach (var p in body.Parts)
            {
                float e = Math.Max(p.SU, Math.Max(p.SV, p.SW));
                lo = new Vec3(Math.Min(lo.X, p.C.X - e), Math.Min(lo.Y, p.C.Y - e), Math.Min(lo.Z, p.C.Z - e));
                hi = new Vec3(Math.Max(hi.X, p.C.X + e), Math.Max(hi.Y, p.C.Y + e), Math.Max(hi.Z, p.C.Z + e));
            }
            var size = hi - lo; float cell = Math.Max(size.X, Math.Max(size.Y, size.Z)) / 44f;
            int nx = (int)(size.X / cell) + 2, ny = (int)(size.Y / cell) + 2, nz = (int)(size.Z / cell) + 2;
            var who = new int[nx, ny, nz];
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        var c = lo + new Vec3((x + 0.5f) * cell, (y + 0.5f) * cell, (z + 0.5f) * cell);
                        int w = 0;
                        for (int k = body.Parts.Count - 1; k >= 0; k--) if (body.Parts[k].Inside(c)) { w = k + 1; break; }   // later parts sit on top
                        who[x, y, z] = w;
                    }
            int[,] dirs = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        int w = who[x, y, z]; if (w == 0) continue;
                        var p = body.Parts[w - 1];
                        float k = 0.92f + 0.16f * Noise.Value(x * 1.7f, y * 1.7f, z * 1.7f, 11);   // each cube a touch different
                        var c = lo + new Vec3(x * cell, y * cell, z * cell);
                        for (int d = 0; d < 6; d++)
                        {
                            int ax = x + dirs[d, 0], ay = y + dirs[d, 1], az = z + dirs[d, 2];
                            if (ax >= 0 && ay >= 0 && az >= 0 && ax < nx && ay < ny && az < nz && who[ax, ay, az] != 0) continue;
                            CubeFace(l, c, cell, d, p, k);
                        }
                    }
        }

        private static void CubeFace(List<StarVertex> l, Vec3 o, float s, int d, Part p, float k)
        {
            Vec3 a, b, c, e;
            switch (d)
            {
                case 0: a = new Vec3(s, 0, 0); b = new Vec3(s, s, 0); c = new Vec3(s, s, s); e = new Vec3(s, 0, s); break;
                case 1: a = new Vec3(0, 0, 0); b = new Vec3(0, 0, s); c = new Vec3(0, s, s); e = new Vec3(0, s, 0); break;
                case 2: a = new Vec3(0, s, 0); b = new Vec3(0, s, s); c = new Vec3(s, s, s); e = new Vec3(s, s, 0); break;
                case 3: a = new Vec3(0, 0, 0); b = new Vec3(s, 0, 0); c = new Vec3(s, 0, s); e = new Vec3(0, 0, s); break;
                case 4: a = new Vec3(0, 0, s); b = new Vec3(s, 0, s); c = new Vec3(s, s, s); e = new Vec3(0, s, s); break;
                default: a = new Vec3(0, 0, 0); b = new Vec3(0, s, 0); c = new Vec3(s, s, 0); e = new Vec3(s, 0, 0); break;
            }
            Tri(l, o + a, o + b, o + c, p, k); Tri(l, o + a, o + c, o + e, p, k);
        }

        // ── the nine subjects of the style sample, each from a seed ──

        public static readonly string[] SubjectNames = { "virus", "bacterium", "archaeon", "amoeba (protist)", "mushroom (fungus)", "tree (plant)", "four-legged animal", "village", "spaceship" };

        public static Body Subject(int i, ulong seed)
        {
            var rng = new Rng(seed * 6364136223846793005UL + (ulong)i * 1442695040888963407UL + 1);
            var b = new Body { Name = SubjectNames[i] };
            switch (i)
            {
                case 0: Virus(b, rng); break;
                case 1: Bacterium(b, rng); break;
                case 2: Archaeon(b, rng); break;
                case 3: Amoeba(b, rng); break;
                case 4: Mushroom(b, rng); break;
                case 5: Tree(b, rng); break;
                case 6: Animal(b, rng); break;
                case 7: Village(b, rng); break;
                default: Ship(b, rng); break;
            }
            return b;
        }

        private static Vec3 Dir(Rng r) { var v = new Vec3(r.Gaussian(), r.Gaussian(), r.Gaussian()); return v.Normalized; }

        // a many-sided shell with spikes all round, each ending in a knob: the shape of many viruses
        private static void Virus(Body b, Rng r)
        {
            float hr = r.Range(0.6f, 1f), hg = r.Range(0.2f, 0.5f), hb = r.Range(0.3f, 0.8f);
            b.Blob(new Vec3(0, 0, 0), 0.6f, 0.6f, 0.6f, hr, hg, hb);
            int n = 14 + r.Below(10);
            for (int k = 0; k < n; k++)
            {
                // spread evenly round the shell
                double y = 1 - 2 * (k + 0.5) / n, rr = Math.Sqrt(1 - y * y), th = k * 2.399963;
                var d = new Vec3((float)(Math.Cos(th) * rr), (float)y, (float)(Math.Sin(th) * rr));
                b.Limb(d * 0.5f, d * 0.92f, 0.05f, 0.9f, 0.85f, 0.7f);
                b.Blob(d * 0.95f, 0.09f, 0.09f, 0.09f, 1f, 0.6f, 0.3f, 0.2f);
            }
            b.Radius = 1;
        }

        // a rod-shaped cell with a long whip-like tail it swims with
        private static void Bacterium(Body b, Rng r)
        {
            float len = r.Range(0.7f, 1.1f), cr = r.Range(0.2f, 0.4f), cg = r.Range(0.6f, 0.9f), cb = r.Range(0.3f, 0.6f);
            b.Limb(new Vec3(-len * 0.5f, 0, 0), new Vec3(len * 0.5f, 0, 0), 0.3f, cr, cg, cb);
            b.Blob(new Vec3(0.05f, 0.02f, 0), 0.12f, 0.12f, 0.12f, cr * 0.6f, cg * 0.6f, cb * 0.5f);     // the gene coil, showing through
            Vec3 prev = new Vec3(len * 0.5f + 0.25f, 0, 0);
            for (int k = 1; k <= 12; k++)
            {
                var p = new Vec3(len * 0.5f + 0.25f + k * 0.11f, (float)Math.Sin(k * 0.9) * 0.12f, (float)Math.Cos(k * 0.9) * 0.12f);
                b.Limb(prev, p, 0.03f, 0.85f, 0.85f, 0.6f); prev = p;
            }
            b.Radius = 1.3f;
        }

        // a lumpy round cell covered in fine hairs: archaea often live in boiling or salty water
        private static void Archaeon(Body b, Rng r)
        {
            float cr = r.Range(0.8f, 1f), cg = r.Range(0.4f, 0.7f), cb = r.Range(0.1f, 0.3f);
            b.Blob(new Vec3(0, 0, 0), 0.55f, 0.48f, 0.52f, cr, cg, cb);
            for (int k = 0; k < 5; k++) { var d = Dir(r); b.Blob(d * 0.3f, 0.32f, 0.3f, 0.3f, cr * 0.95f, cg * 0.95f, cb); }
            for (int k = 0; k < 22; k++) { var d = Dir(r); b.Limb(d * 0.45f, d * r.Range(0.8f, 1f), 0.018f, 0.95f, 0.9f, 0.7f); }
            b.Radius = 1;
        }

        // a soft cell stretching out arm-like lobes, with its nucleus and food bubbles inside
        private static void Amoeba(Body b, Rng r)
        {
            float cr = r.Range(0.5f, 0.7f), cg = r.Range(0.75f, 0.95f), cb = r.Range(0.7f, 0.9f);
            b.Blob(new Vec3(0, 0, 0), 0.6f, 0.3f, 0.55f, cr, cg, cb);
            int lobes = 4 + r.Below(3);
            for (int k = 0; k < lobes; k++)
            {
                double a = k * Math.PI * 2 / lobes + r.Range(-0.3f, 0.3f); float reach = r.Range(0.6f, 1f);
                var d = new Vec3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                b.Limb(d * 0.3f, d * reach + new Vec3(0, r.Range(-0.1f, 0.1f), 0), r.Range(0.12f, 0.2f), cr, cg, cb);
            }
            b.Blob(new Vec3(0.05f, 0.18f, 0.05f), 0.18f, 0.15f, 0.18f, 0.45f, 0.35f, 0.65f);          // nucleus
            for (int k = 0; k < 3; k++) b.Blob(new Vec3(r.Range(-0.3f, 0.3f), 0.2f, r.Range(-0.3f, 0.3f)), 0.07f, 0.07f, 0.07f, 0.85f, 0.75f, 0.35f);
            b.Radius = 1.1f;
        }

        // a stem and a wide cap with spots, on a little patch of ground
        private static void Mushroom(Body b, Rng r)
        {
            float h = r.Range(0.7f, 1f), capW = r.Range(0.55f, 0.8f);
            float cr = r.Range(0.7f, 0.95f), cg = r.Range(0.15f, 0.5f), cb = r.Range(0.05f, 0.25f);
            b.Blob(new Vec3(0, -0.55f, 0), 0.8f, 0.08f, 0.8f, 0.35f, 0.28f, 0.18f);                     // soil
            b.Limb(new Vec3(0, -0.5f, 0), new Vec3(0, -0.5f + h, 0), 0.11f, 0.92f, 0.88f, 0.78f);       // stem
            b.Blob(new Vec3(0, -0.5f + h, 0), capW, 0.28f, capW, cr, cg, cb);                         // cap
            b.Blob(new Vec3(0, -0.56f + h, 0), capW * 0.92f, 0.1f, capW * 0.92f, 0.85f, 0.78f, 0.65f);  // gills underneath
            for (int k = 0; k < 7; k++)
            {
                double a = r.Range(0, 6.28f); float rr = r.Range(0, capW * 0.7f);
                b.Blob(new Vec3((float)Math.Cos(a) * rr, -0.5f + h + 0.22f, (float)Math.Sin(a) * rr), 0.08f, 0.05f, 0.08f, 0.97f, 0.95f, 0.9f);
            }
            b.Radius = 1;
        }

        // a trunk that forks into branches, each ending in a round crown of leaves
        private static void Tree(Body b, Rng r)
        {
            b.Blob(new Vec3(0, -0.95f, 0), 0.9f, 0.08f, 0.9f, 0.3f, 0.5f, 0.2f);                        // grass
            float lg = r.Range(0.45f, 0.7f);
            Branch(b, r, new Vec3(0, -0.9f, 0), new Vec3(0, 1, 0), 0.75f, 0.12f, 0, lg);
            b.Radius = 1.25f;
        }
        private static void Branch(Body b, Rng r, Vec3 from, Vec3 dir, float len, float rad, int depth, float leafG)
        {
            var to = from + dir * len;
            b.Limb(from, to, rad, 0.45f, 0.3f, 0.18f);
            if (depth == 2) { float s = r.Range(0.25f, 0.35f); b.Blob(to, s, s * 0.85f, s, 0.2f, leafG, 0.18f); return; }
            int n = 2 + r.Below(2);
            for (int k = 0; k < n; k++)
            {
                var nd = (dir + Dir(r) * 0.75f).Normalized; if (nd.Y < 0.2f) nd = new Vec3(nd.X, 0.3f, nd.Z).Normalized;
                Branch(b, r, to, nd, len * 0.65f, rad * 0.6f, depth + 1, leafG);
            }
        }

        // a body, a head, four legs and a tail; long or short legs, long or short neck, spots or none
        private static void Animal(Body b, Rng r)
        {
            float cr = r.Range(0.5f, 0.9f), cg = r.Range(0.35f, 0.6f), cb = r.Range(0.2f, 0.4f);
            float legH = r.Range(0.35f, 0.6f), bodyL = r.Range(0.5f, 0.7f), neck = r.Range(0.15f, 0.4f);
            float y = -0.6f + legH + 0.2f;
            b.Blob(new Vec3(0, -0.62f, 0), 0.95f, 0.04f, 0.7f, 0.35f, 0.5f, 0.22f);                    // ground
            b.Blob(new Vec3(0, y, 0), bodyL, 0.24f, 0.24f, cr, cg, cb);                               // body
            var neckTop = new Vec3(bodyL * 0.85f, y + 0.15f + neck, 0);
            b.Limb(new Vec3(bodyL * 0.7f, y + 0.05f, 0), neckTop, 0.1f, cr, cg, cb);                  // neck
            b.Blob(neckTop + new Vec3(0.12f, 0, 0), 0.2f, 0.13f, 0.13f, cr, cg, cb);                  // head
            b.Blob(neckTop + new Vec3(0.3f, -0.03f, 0), 0.08f, 0.07f, 0.08f, cr * 0.8f, cg * 0.7f, cb * 0.7f);   // snout
            b.Blob(neckTop + new Vec3(0.17f, 0.06f, 0.1f), 0.035f, 0.035f, 0.035f, 0.05f, 0.05f, 0.05f);  // eyes
            b.Blob(neckTop + new Vec3(0.17f, 0.06f, -0.1f), 0.035f, 0.035f, 0.035f, 0.05f, 0.05f, 0.05f);
            b.Cone(neckTop + new Vec3(0.02f, 0.1f, 0.07f), new Vec3(-0.2f, 1, 0.3f), 0.14f, 0.05f, cr * 0.8f, cg * 0.7f, cb * 0.7f);   // ears
            b.Cone(neckTop + new Vec3(0.02f, 0.1f, -0.07f), new Vec3(-0.2f, 1, -0.3f), 0.14f, 0.05f, cr * 0.8f, cg * 0.7f, cb * 0.7f);
            for (int k = 0; k < 4; k++)
            {
                float lx = (k < 2 ? 0.6f : -0.6f) * bodyL, lz = (k % 2 == 0 ? 0.13f : -0.13f);
                b.Limb(new Vec3(lx, y - 0.05f, lz), new Vec3(lx + 0.03f, -0.58f, lz), 0.07f, cr * 0.9f, cg * 0.9f, cb * 0.9f);
            }
            b.Limb(new Vec3(-bodyL * 0.9f, y + 0.05f, 0), new Vec3(-bodyL - 0.35f, y + r.Range(-0.1f, 0.25f), 0), 0.05f, cr, cg, cb);   // tail
            if (r.NextDouble() < 0.6)
                for (int k = 0; k < 6; k++) b.Blob(new Vec3(r.Range(-bodyL * 0.7f, bodyL * 0.7f), y + r.Range(0.05f, 0.17f), r.Range(-0.18f, 0.18f)), 0.08f, 0.07f, 0.08f, cr * 0.5f, cg * 0.45f, cb * 0.4f);
            b.Radius = 1.2f;
        }

        // round huts with pointed roofs around a fire, a fence, and a field
        private static void Village(Body b, Rng r)
        {
            b.Box(new Vec3(0, -0.62f, 0), 1.3f, 0.04f, 1.3f, 0.4f, 0.55f, 0.25f);                      // ground
            b.Box(new Vec3(0.75f, -0.57f, -0.7f), 0.4f, 0.02f, 0.35f, 0.75f, 0.65f, 0.3f);              // a field of grain
            int huts = 5 + r.Below(3);
            for (int k = 0; k < huts; k++)
            {
                double a = k * Math.PI * 2 / huts + r.Range(-0.2f, 0.2f); float d = r.Range(0.55f, 0.8f), s = r.Range(0.14f, 0.2f);
                var c = new Vec3((float)Math.Cos(a) * d, -0.58f, (float)Math.Sin(a) * d);
                b.Blob(c + new Vec3(0, s * 0.6f, 0), s, s * 0.8f, s, 0.75f, 0.6f, 0.42f);               // walls
                b.Cone(c + new Vec3(0, s * 1.1f, 0), new Vec3(0, 1, 0), s * 1.4f, s * 1.25f, 0.85f, 0.72f, 0.3f);   // straw roof
                b.Box(c + new Vec3(0, s * 0.35f, 0) + new Vec3((float)-Math.Cos(a), 0, (float)-Math.Sin(a)) * (s * 0.95f), s * 0.18f, s * 0.3f, s * 0.18f, 0.25f, 0.15f, 0.1f, 0, (float)a);   // door
            }
            b.Blob(new Vec3(0, -0.55f, 0), 0.1f, 0.05f, 0.1f, 0.4f, 0.38f, 0.35f);                     // fire ring
            b.Cone(new Vec3(0, -0.55f, 0), new Vec3(0, 1, 0), 0.2f, 0.07f, 1f, 0.55f, 0.1f, 1f);       // the fire
            for (int k = 0; k < 36; k++)
            {
                double a = k * Math.PI * 2 / 36; var c = new Vec3((float)Math.Cos(a) * 1.15f, -0.5f, (float)Math.Sin(a) * 1.15f);
                b.Box(c, 0.025f, 0.08f, 0.025f, 0.5f, 0.35f, 0.2f);                                     // fence posts
            }
            b.Radius = 1.5f;
        }

        // a long hull, swept wings, a cockpit, and glowing engines
        private static void Ship(Body b, Rng r)
        {
            float hr = r.Range(0.6f, 0.85f), hg = r.Range(0.62f, 0.85f), hb = r.Range(0.65f, 0.9f);
            float ar = r.Range(0.6f, 1f), ag = r.Range(0.2f, 0.5f), ab = r.Range(0.1f, 0.3f);
            b.Blob(new Vec3(0, 0, 0), 0.95f, 0.18f, 0.24f, hr, hg, hb);                                // hull
            b.Cone(new Vec3(0.85f, 0, 0), new Vec3(1, 0, 0), 0.35f, 0.15f, hr, hg, hb);                 // nose
            b.Blob(new Vec3(0.45f, 0.14f, 0), 0.22f, 0.1f, 0.12f, 0.3f, 0.75f, 0.95f, 0.4f);           // cockpit glass
            float span = r.Range(0.7f, 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box(new Vec3(-0.25f, -0.02f, s * span * 0.55f), 0.32f, 0.03f, span * 0.4f, ar, ag, ab, 0, s * 0.35f);   // wing
                b.Limb(new Vec3(-0.5f, -0.03f, s * span * 0.85f), new Vec3(0.05f, -0.03f, s * span * 0.85f), 0.06f, hr * 0.8f, hg * 0.8f, hb * 0.8f);   // wing pod
                b.Limb(new Vec3(-0.95f, 0, s * 0.14f), new Vec3(-0.55f, 0, s * 0.14f), 0.1f, 0.4f, 0.42f, 0.45f);   // engine
                b.Blob(new Vec3(-1.0f, 0, s * 0.14f), 0.06f, 0.08f, 0.08f, 0.4f, 0.8f, 1f, 1f);         // engine glow
            }
            b.Box(new Vec3(-0.65f, 0.25f, 0), 0.18f, 0.14f, 0.02f, ar, ag, ab);                         // tail fin
            b.Radius = 1.3f;
        }
    }
}
