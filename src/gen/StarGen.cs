// Universe · generators · a single star seen up close, built from content/stars.unt.
// The star is a ball of faceted points (Populous-like) with surface detail that depends on the kind:
// granulation and spots, giant convection cells, bands on brown dwarfs, beams on pulsars.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    /// <summary>A star's living surface: redrawn every frame so the plasma moves and only the near side is drawn.</summary>
    internal sealed class StarSurface
    {
        public Vec3[] Normals; public float Radius, Size, Cell, R, G, B;
        public bool Giant, Brown, Hot, Spotty; public uint Seed;

        /// <summary>Writes the side facing <paramref name="toEye"/> (unit vector) into <paramref name="outV"/>; returns how many.</summary>
        public int Draw(Vec3 toEye, double time, StarVertex[] outV, int start)
        {
            int n = start;
            float t = (float)time * (Giant ? 0.02f : 0.05f);   // slow boiling
            for (int i = 0; i < Normals.Length; i++)
            {
                Vec3 q = Normals[i];
                float facing = Vec3.Dot(q, toEye);
                if (facing < -0.05f) continue;                  // back side: hidden, and would wash the colour out
                // its boiling surface from the waveform (stored CDF 9/7 octaves, mode-E past them): the granulation cells and finer
                float cells = (float)(0.5 + 0.5 * Waveform.Octaves(q.X * Cell + t, q.Y * Cell - t * 0.7f, q.Z * Cell + 5, Seed, 1, 1, 0.5, 4));
                float fine = (float)(0.5 + 0.5 * Waveform.Octaves(q.X * Cell * 4 - t * 2, q.Y * Cell * 4, q.Z * Cell * 4 + t, Seed + 77, 1, 1, 0.5, 2));
                float heat;
                if (Brown)
                {
                    // cloud bands like a gas giant, with a few round storms; dim, it does not burn like a true star
                    heat = 0.42f + 0.28f * (float)Math.Sin(q.Y * 11 + cells * 2.5f + t) + 0.12f * (fine - 0.5f);
                    if (Noise.Fractal(q.X * 2.2f + 9, q.Y * 6, q.Z * 2.2f, Seed + 31, 2) > 0.74f) heat += 0.25f;
                }
                else heat = 0.25f + 1.1f * (cells - 0.5f) + 0.35f * (fine - 0.5f) + 0.45f;
                if (Spotty && Noise.Fractal(q.X * 3 + 50, q.Y * 3, q.Z * 3, Seed + 9, 2) > 0.7f) heat *= 0.35f; // star spots
                heat *= 0.45f + 0.55f * Math.Max(0f, facing);   // darker toward the edge, like a real star
                heat = Math.Max(0.05f, Math.Min(1.15f, heat));
                // plasma colour: hot cells keep the star's colour, cool gaps go deeper and redder (never just white)
                float r = R * (0.25f + 0.75f * heat), g = G * (float)Math.Pow(heat, Hot ? 1.0 : 1.5), b = B * (float)Math.Pow(heat, Hot ? 0.9 : 2.0);
                outV[n++] = SpaceGen.Star(q * Radius, Size, r * 0.62f, g * 0.62f, b * 0.62f, 1f);
            }
            return n - start;
        }
    }

    internal static class StarGen
    {
        /// <summary>Points for one star, radius about 1, centred on the origin.</summary>
        public static StarVertex[] Build(ContentLibrary content, string id, ulong seed, out string name, out StarSurface surfaceOut)
        {
            name = id; float r = 1, g = 1, b = 1; string sk = "normal"; bool spotty = false;
            foreach (var e in content.OfKind("stars.unt", "star"))
                if (e.Id == id) { name = e.Name; GalaxyGen.ColorFor(e, out r, out g, out b); sk = e.Get("surface-kind", "normal"); spotty = e.Get("spots", "no") == "yes"; }   // its colour from its surface temperature; its kind of face from the data
            var rng = new Rng(seed);
            var list = new List<StarVertex>();
            uint nseed = (uint)(seed & 0xFFFFFFFF);

            bool giant = sk == "giant", brown = sk == "brown-dwarf", white = sk == "white-dwarf", compact = sk == "neutron-star";
            float radius = compact ? 0.28f : white ? 0.45f : 1f;
            int surface = compact ? 1200 : white ? 2000 : 6000;
            float cell = giant ? 1.6f : brown ? 1f : 5f;       // noise frequency: giants have huge convection cells

            var normals = new Vec3[surface]; double spacing = Math.Sqrt(4 * Math.PI / surface);
            for (int i = 0; i < surface; i++)
            {
                // even spread over the sphere, then each point jittered off its place by up to half the spacing, every way: the even
                // spread alone is a spiral lattice, and near a big star, where its points stand apart, it read as rows of evenly
                // spaced dots across the sky (the "patterning" seen in systems)
                double yv = 1 - 2 * (i + 0.5) / surface, rr = Math.Sqrt(1 - yv * yv), th = i * 2.399963229728653;
                var q = new Vec3((float)(Math.Cos(th) * rr), (float)yv, (float)(Math.Sin(th) * rr));
                var e1 = Vec3.Cross(q, Math.Abs(q.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(q, e1).Normalized;
                double ja = rng.NextDouble() * Math.PI * 2, jr = Math.Sqrt(rng.NextDouble()) * spacing * 0.5;
                normals[i] = (q + e1 * (float)(Math.Cos(ja) * jr) + e2 * (float)(Math.Sin(ja) * jr)).Normalized;
            }
            // points just touching, not piled up: spacing on a sphere of radius 1 is about sqrt(4 pi / count)
            float size = radius * (float)Math.Sqrt(4 * Math.PI / surface) * 0.9f;   // (wide enough to overlap its neighbours: one sheet of plasma close up, never separate dots)
            surface = 0;
            bool hot = r < g || b > 0.95f;
            var surf = new StarSurface { Normals = normals, Radius = radius, Size = size, Cell = cell, R = r, G = g, B = b,
                Giant = giant, Brown = brown, Hot = hot, Spotty = spotty, Seed = nseed };
            surfaceOut = surf;

            // the glow around it: a loose shell of dim points
            int corona = brown ? 140 : compact ? 160 : 420;   // brown dwarfs: only a faint warm haze
            for (int i = 0; i < corona; i++)
            {
                double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u);
                float h = radius * rng.Range(1.08f, giant ? 1.9f : 1.5f);
                var p = new Vec3((float)(Math.Cos(a) * rr) * h, (float)u * h, (float)(Math.Sin(a) * rr) * h);
                list.Add(SpaceGen.Star(p, radius * 0.18f, r, g, b, 0.10f * (1.6f - h / radius * 0.5f)));
            }

            // pulsars and magnetars: two narrow beams out of the magnetic poles, tilted from the spin axis
            if (compact && id != "neutron-star")
            {
                var axis = new Vec3(0.42f, 0.9f, 0.1f); float len = (float)Math.Sqrt(Vec3.Dot(axis, axis)); axis = axis * (1f / len);
                for (int s = -1; s <= 1; s += 2)
                    for (int k = 0; k < 260; k++)
                    {
                        float t = k / 260f, d = radius * 1.1f + t * 2.6f, spread = t * 0.25f;
                        var p = axis * (d * s) + new Vec3(rng.Range(-spread, spread), rng.Range(-spread, spread) * 0.3f, rng.Range(-spread, spread));
                        list.Add(SpaceGen.Star(p, 0.05f, 0.75f, 0.85f, 1f, 0.9f * (1 - t)));
                    }
            }
            return list.ToArray();
        }
    }
}
