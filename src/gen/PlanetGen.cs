// Universe · generators · a planet's name and its rings, from content/planets.dnat and gas-giants.dnat.
// The planet itself (ground, sea, clouds, air) is built by PlanetTerrain.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    /// <summary>Ring points around a planet, in planet radii, with how bright each one is.</summary>
    internal sealed class PlanetRings
    {
        public readonly List<Vec3> Points = new List<Vec3>();
        public readonly List<float> Shade = new List<float>();
    }

    internal static class PlanetGen
    {
        /// <summary>The colour of a kind of world's rings (content/terrain.dnat rings-colour; icy grey-cream when none is given).</summary>
        public static float[] RingColour(ContentLibrary content, string id)
        {
            foreach (var e in content.OfKind("terrain.dnat", "terrain"))
                if (e.Id == id)
                {
                    var w = e.Get("rings-colour", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); float r, g, b; var ci = System.Globalization.CultureInfo.InvariantCulture; var nf = System.Globalization.NumberStyles.Float;
                    if (w.Length == 3 && float.TryParse(w[0], nf, ci, out r) && float.TryParse(w[1], nf, ci, out g) && float.TryParse(w[2], nf, ci, out b)) return new[] { r, g, b };
                }
            return new[] { 0.86f, 0.82f, 0.74f };
        }
        public static PlanetRings Build(ContentLibrary content, string id, ulong seed, out string name)
        {
            name = id;
            foreach (var e in content.OfKind("planets.dnat", "planet")) if (e.Id == id) name = e.Name;
            foreach (var e in content.OfKind("gas-giants.dnat", "giant")) if (e.Id == id) name = e.Name;
            var rings = new PlanetRings();
            var rng = new Rng(seed);
            // rings, from its kind's template (content/terrain.dnat): rings (how many specks), rings-radii (from and to, in the
            // planet's radii), rings-gap (a gap, in radii), rings-shade (banded, bright and banded like Saturn's, or a number:
            // faint and even)
            ContentEntry k = null; foreach (var e in content.OfKind("terrain.dnat", "terrain")) if (e.Id == id) k = e;
            double count, c2, from, to, gapA = 0, gapB = 0;
            if (k != null && k.TryRange("rings", out count, out c2) && count > 0 && k.TryRange("rings-radii", out from, out to))
            {
                k.TryRange("rings-gap", out gapA, out gapB);
                string shadeWord = k.Get("rings-shade", "banded"); bool banded = shadeWord == "banded"; double flat;
                if (!double.TryParse(shadeWord, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out flat)) flat = 0.25;
                for (int i = 0; i < (int)count; i++)
                {
                    float rad = rng.Range((float)from, (float)to);
                    if (gapB > gapA && rad > gapA && rad < gapB) continue;                // a gap in the rings
                    double a = rng.NextDouble() * Math.PI * 2;
                    float shade = banded ? 0.6f + 0.4f * (float)Math.Sin(rad * 40) * 0.5f : (float)flat;
                    var p = new Vec3((float)Math.Cos(a) * rad, rng.Range(-0.005f, 0.005f), (float)Math.Sin(a) * rad);
                    p = new Vec3(p.X, p.Y * 0.95f + p.Z * 0.3f, p.Z * 0.95f - p.Y * 0.3f);   // tilt the rings
                    rings.Points.Add(p); rings.Shade.Add(shade * 0.5f);
                }
            }
            return rings;
        }
    }
}
