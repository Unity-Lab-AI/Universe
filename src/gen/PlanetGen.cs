// DNA-AND · generators · a planet's name and its rings, from content/planets.dnat and gas-giants.dnat.
// The planet itself (ground, sea, clouds, air) is built by PlanetTerrain.
using System;
using System.Collections.Generic;
using DnaAnd.Core;
using DnaAnd.Render;

namespace DnaAnd.Gen
{
    /// <summary>Ring points around a planet, in planet radii, with how bright each one is.</summary>
    internal sealed class PlanetRings
    {
        public readonly List<Vec3> Points = new List<Vec3>();
        public readonly List<float> Shade = new List<float>();
    }

    internal static class PlanetGen
    {
        public static PlanetRings Build(ContentLibrary content, string id, ulong seed, out string name)
        {
            name = id;
            foreach (var e in content.OfKind("planets.dnat", "planet")) if (e.Id == id) name = e.Name;
            foreach (var e in content.OfKind("gas-giants.dnat", "giant")) if (e.Id == id) name = e.Name;
            var rings = new PlanetRings();
            var rng = new Rng(seed);
            // rings: the gas giant gets bright ones, the ice giant faint dark ones
            if (id == "gas-giant" || id == "ice-giant")
            {
                bool bright = id == "gas-giant";
                int count = bright ? 5200 : 1400;
                for (int i = 0; i < count; i++)
                {
                    float rad = rng.Range(bright ? 1.35f : 1.6f, bright ? 2.3f : 2.0f);
                    if (bright && rad > 1.85f && rad < 1.92f) continue;           // a gap in the rings
                    double a = rng.NextDouble() * Math.PI * 2;
                    float shade = bright ? 0.6f + 0.4f * (float)Math.Sin(rad * 40) * 0.5f : 0.25f;
                    var p = new Vec3((float)Math.Cos(a) * rad, rng.Range(-0.005f, 0.005f), (float)Math.Sin(a) * rad);
                    p = new Vec3(p.X, p.Y * 0.95f + p.Z * 0.3f, p.Z * 0.95f - p.Y * 0.3f);   // tilt the rings
                    rings.Points.Add(p); rings.Shade.Add(shade * 0.5f);
                }
            }
            return rings;
        }
    }
}
