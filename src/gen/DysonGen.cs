// Universe · generators · Dyson structures around a star, from content/space-tech.dnat.
// Shells and rings are hollow worlds (PlanetTerrain in shell mode): a hull outside, land inside, held on by the spin.
// Swarms, bubbles, mirrors and engines are many pieces around the star. Docks sit on the hulls and ships fly
// between them, because spaceships, docks and economies are built around Dyson spheres.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    internal sealed class DysonStructure
    {
        public string Id, Name;
        public double Outer;                                   // km: the outermost part, for framing the view
        public readonly List<PlanetTerrain> Shells = new List<PlanetTerrain>();
        public StarVertex[] Pieces;                           // collectors, sails, mirrors, docks, ships, the engine's jet (km, star at 0)
        public float StarBright = 1f;                         // how much of the star still shows through
        public PlanetTerrain Enterable { get { foreach (var s in Shells) if (s.Habitable) return s; return null; } }
    }

    internal static class DysonGen
    {
        public const double Au = 1.496e8;                     // km
        /// <summary>The builds in order (content/space-tech.dnat @build), the first steps first.</summary>
        public static List<string> Ids(ContentLibrary content) { var l = new List<string>(); foreach (var e in content.OfKind("space-tech.dnat", "build")) l.Add(e.Id); return l; }

        public static DysonStructure Build(IntPtr device, ContentLibrary content, string id, ulong seed)
        {
            var d = new DysonStructure { Id = id, Name = id, Outer = Au };
            ContentEntry be = null; foreach (var e in content.OfKind("space-tech.dnat", "build")) if (e.Id == id) be = e;
            if (be == null) return d;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            Func<string, double, double> num = (t, f) => { double v; return double.TryParse(t, fs, ci, out v) ? v : f; };
            // its name: its own, or the megastructure it is a step of, with what is added
            d.Name = be.Get("name", id);
            string of = be.Get("of", ""); if (of.Length > 0) foreach (var e in content.OfKind("space-tech.dnat", "mega")) if (e.Id == of) d.Name = e.Name + be.Get("suffix", "");
            d.StarBright = (float)num(be.Get("star-bright", "1"), 1); d.Outer = Au * num(be.Get("outer-au", "1"), 1);
            var rng = new Rng(seed);
            var pts = new List<StarVertex>();
            uint s32 = (uint)(seed & 0xFFFFFFFF); int shells = 0;
            foreach (var part in be.All("part"))
            {
                var w = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length == 0) continue;
                Func<int, double, double> arg = (k, f) => k < w.Length ? num(w[k], f) : f;
                switch (w[0])
                {
                    case "mirrors":
                        for (int i = 0, n = (int)arg(1, 500); i < n; i++) pts.Add(Orbiting(rng, Au * rng.Range((float)arg(2, 0.5), (float)arg(3, 0.9)), 0.3, 900000, 0.95f, 0.95f, 1f, 0.9f));
                        break;
                    case "swarm":
                        {
                            int n = (int)arg(1, 30000), rings = Math.Max(1, (int)arg(2, 40)); float bright = (float)arg(3, 0.7);
                            for (int i = 0; i < n; i++)
                            {
                                // collectors on many tilted rings: dark panels that glint blue in the light
                                int ring = i % rings; var rr = new Rng(seed + (ulong)ring * 31UL);
                                double tilt = rr.NextDouble() * Math.PI, node = rr.NextDouble() * Math.PI * 2, rad = Au * (0.9 + 0.2 * rr.NextDouble());
                                double a = rng.NextDouble() * Math.PI * 2;
                                pts.Add(OnRing(rad, tilt, node, a, 700000, 0.25f, 0.35f, 0.6f, bright));
                            }
                        }
                        break;
                    case "bubble":
                        for (int i = 0, n = (int)arg(1, 24000); i < n; i++)
                        {
                            // light sails held still by the star's own light: a thin even sphere, hovering, not orbiting
                            double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - u * u), rad = Au * rng.Range(0.98f, 1.02f);
                            pts.Add(SpaceGen.Star(new Vec3((float)(Math.Cos(a) * r * rad), (float)(u * rad), (float)(Math.Sin(a) * r * rad)), 500000, 0.8f, 0.82f, 0.9f, 0.35f));
                        }
                        break;
                    case "shell":
                        {
                            var sh = PlanetTerrain.MakeShell(device, d.Name, s32 + (uint)shells, Au * arg(1, 1), w.Length > 2 && w[2] == "yes", (int)arg(3, 0));
                            if (w.Length > 7) { sh.HullR = (float)arg(4, 0.5); sh.HullG = (float)arg(5, 0.5); sh.HullB = (float)arg(6, 0.5); sh.HullGlow = (float)arg(7, 0); }
                            d.Shells.Add(sh); shells++;
                        }
                        break;
                    case "jet":
                        for (int i = 0, n = (int)arg(1, 3000); i < n; i++)
                        {
                            // the light pushed out one way: the whole star slowly moves the other
                            float t = (float)rng.NextDouble();
                            double spread = Au * 0.05 * t;
                            var p = new Vec3((float)(rng.Range(-1, 1) * spread), (float)(-Au * 1.4 * t - 800000), (float)(rng.Range(-1, 1) * spread));
                            pts.Add(SpaceGen.Star(p, (float)(1500000 + spread * 0.3), 1f, 0.75f, 0.45f, 0.25f * (1 - t)));
                        }
                        break;
                }
            }
            // docks on every hull, and ship traffic between the docks and inward toward the star
            foreach (var sh in d.Shells)
            {
                if (!sh.Habitable) continue;
                var docks = new List<Vec3>();
                for (int k = 0; k < 60; k++)
                {
                    double u = sh.Mask == 1 ? rng.Range(-0.03f, 0.03f) : rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - u * u);
                    double rad = sh.Radius + sh.HullKm + 200;
                    var dp = new Vec3((float)(Math.Cos(a) * r * rad), (float)(u * rad), (float)(Math.Sin(a) * r * rad));
                    docks.Add(dp);
                    for (int q = 0; q < 12; q++)                                                        // a dock: a cluster of lights
                        pts.Add(SpaceGen.Star(dp + new Vec3(rng.Range(-1, 1) * 20000, rng.Range(-1, 1) * 20000, rng.Range(-1, 1) * 20000), 9000, 1f, 0.85f, 0.5f, 0.9f));
                }
                for (int k = 0; k < 4000; k++)
                {
                    // ships strung along lanes from one dock to another, out away from the hull
                    var a0 = docks[rng.Below(docks.Count)]; var a1 = docks[rng.Below(docks.Count)];
                    float t = (float)rng.NextDouble();
                    var mid = (a0 + a1) * 0.5f; float ml = (float)Math.Sqrt(Vec3.Dot(mid, mid));
                    var lift = mid * (float)((sh.Radius * 1.15) / Math.Max(1, ml));
                    var p = a0 * ((1 - t) * (1 - t)) + lift * (2 * t * (1 - t)) + a1 * (t * t);
                    pts.Add(SpaceGen.Star(p, 6000, 0.6f, 0.9f, 1f, 0.8f));
                }
            }
            d.Pieces = pts.ToArray();
            return d;
        }

        private static StarVertex Orbiting(Rng rng, double rad, double tiltRange, float size, float r, float g, float b, float bright)
        {
            double tilt = (rng.NextDouble() - 0.5) * tiltRange * 2, node = rng.NextDouble() * Math.PI * 2, a = rng.NextDouble() * Math.PI * 2;
            return OnRing(rad, tilt, node, a, size, r, g, b, bright);
        }

        private static StarVertex OnRing(double rad, double tilt, double node, double a, float size, float r, float g, float b, float bright)
        {
            double x = Math.Cos(a) * rad, z = Math.Sin(a) * rad, y = 0;
            double y2 = y * Math.Cos(tilt) - z * Math.Sin(tilt), z2 = y * Math.Sin(tilt) + z * Math.Cos(tilt);
            double x3 = x * Math.Cos(node) - z2 * Math.Sin(node), z3 = x * Math.Sin(node) + z2 * Math.Cos(node);
            return SpaceGen.Star(new Vec3((float)x3, (float)y2, (float)z3), size, r, g, b, bright);
        }
    }
}
