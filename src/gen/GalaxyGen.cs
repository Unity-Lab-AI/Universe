// Universe · gen · the live galaxy generator. Builds a galaxy from a seed and the content in
// content/galaxies.unt and content/stars.unt. Nothing is pre-built.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    internal sealed class StarKind
    {
        public string Id, Name; public float R, G, B; public double Weight; public double Lum = 1, RadiusSol = 1, MassSol = 1;   // RadiusSol, MassSol: the middle of its kind's ranges (content/stars.unt)   // Lum: its light in solar units, R^2 (T / 5772 K)^4 from its kind's radius and temperature (content/stars.unt)
        public bool Remnant;     // a dead star (white dwarf, neutron star, pulsar, magnetar): found among old stars, not young ones
    }

    internal static class GalaxyGen
    {
        /// <summary>Main-sequence star types and how common each is, read from content/stars.unt.</summary>
        // each kind of galaxy's shape (content/galaxies.unt layout:), read once
        private static readonly Dictionary<string, string> Shapes = new Dictionary<string, string>();
        public static void LoadShapes(ContentLibrary content) { if (Shapes.Count == 0) foreach (var e in content.OfKind("galaxies.unt", "galaxy")) { Shapes[e.Id] = e.Get("layout", "spiral"); Kinds[e.Id] = e; } }
        // each kind of galaxy's template (content/galaxies.unt): how it is grown is read from its traits, never its name
        private static readonly Dictionary<string, ContentEntry> Kinds = new Dictionary<string, ContentEntry>();
        /// <summary>A trait of a kind of galaxy for one galaxy of it: a range (a..b) gives each galaxy its own value from its seed.</summary>
        public static double Trait(string type, string key, double fallback, ulong seed)
        {
            ContentEntry e; double lo, hi; if (!Kinds.TryGetValue(type ?? "", out e) || !e.TryRange(key, out lo, out hi)) return fallback;
            if (hi == lo) return lo;
            ulong h = seed * 0x9E3779B97F4A7C15UL; foreach (char c in key) h = (h ^ c) * 0x100000001B3UL; h ^= h >> 29;
            return lo + (hi - lo) * ((h >> 11) * (1.0 / 9007199254740992.0));
        }
        /// <summary>A yes-or-no trait of a kind of galaxy.</summary>
        public static bool Yes(string type, string key, bool fallback) { ContentEntry e; if (!Kinds.TryGetValue(type ?? "", out e) || !e.Props.ContainsKey(key)) return fallback; return e.Get(key, "") == "yes"; }
        /// <summary>A word trait of a kind of galaxy.</summary>
        public static string Word(string type, string key, string fallback) { ContentEntry e; return Kinds.TryGetValue(type ?? "", out e) ? e.Get(key, fallback) : fallback; }
        private static string ShapeOf(string type) { string v; return Shapes.TryGetValue(type ?? "", out v) ? v : type; }
        public static List<StarKind> StarKinds(ContentLibrary content)
        {
            LoadShapes(content);
            var list = new List<StarKind>();
            foreach (var e in content.OfKind("stars.unt", "star"))
            {
                if (!e.Props.ContainsKey("share-of-stars") || (!e.Props.ContainsKey("color") && !e.Props.ContainsKey("temp-k"))) continue;
                float r, g, b; ColorFor(e, out r, out g, out b);
                bool remnant = e.Get("remnant", "no") == "yes";                        // from the data (stars.unt)
                double lum = 1, r0, r1, t0, t1; if (e.TryRange("radius-sol", out r0, out r1) && e.TryRange("temp-k", out t0, out t1) && r0 > 0 && t0 > 0) { double rs = Math.Sqrt(r0 * r1), tk = Math.Sqrt(t0 * t1); lum = rs * rs * Math.Pow(tk / 5772, 4); }   // (the middle of its kind's ranges)
                double rsol = 1, msol = 1, m0, m1; if (e.TryRange("radius-sol", out r0, out r1) && r0 > 0) rsol = Math.Sqrt(r0 * r1); if (e.TryRange("mass-sol", out m0, out m1) && m0 > 0) msol = Math.Sqrt(m0 * m1); else msol = Math.Pow(Math.Max(1e-4, lum), 1 / 3.5);   // (none given: the main sequence's mass from its light, L ~ M^3.5)
                list.Add(new StarKind { Id = e.Id, Name = e.Name, R = r, G = g, B = b, Weight = ShareWeight(e.Get("share-of-stars", "common")), Remnant = remnant, Lum = lum, RadiusSol = rsol, MassSol = msol });
            }
            if (list.Count == 0) list.Add(new StarKind { Id = "g-type", Name = "G", R = 1, G = 0.95f, B = 0.8f, Weight = 1 });
            return list;
        }

        private static double ShareWeight(string share)
        {
            switch (share.Trim())
            {
                case "most common": return 76; case "common": return 10; case "uncommon": return 3;
                case "rare": return 0.4; case "rarest": return 0.05; default: return 1;
            }
        }

        /// <summary>A star's colour from its surface temperature, the way a glowing hot body shines (red when cool, blue when hot);
        /// the named colour is used only for a kind with no temperature.</summary>
        public static void ColorFor(ContentEntry e, out float r, out float g, out float b)
        {
            double lo, hi;
            if (e.TryRange("temp-k", out lo, out hi)) { Glow(Math.Sqrt(lo * hi), out r, out g, out b); return; }
            ColorOf(e.Get("color", "white"), out r, out g, out b);
        }

        /// <summary>Colour of a body glowing at <paramref name="kelvin"/>: its light at red, green and blue wavelengths
        /// (Planck's law), brightest channel set to 1, softened a little the way the eye sees a bright star.</summary>
        public static void Glow(double kelvin, out float r, out float g, out float b)
        {
            double pr = Planck(611, kelvin), pg = Planck(549, kelvin), pb = Planck(464, kelvin), top = Math.Max(pr, Math.Max(pg, pb));
            r = (float)Math.Pow(pr / top, 0.75); g = (float)Math.Pow(pg / top, 0.75); b = (float)Math.Pow(pb / top, 0.75);
        }

        private static double Planck(double nm, double kelvin) { return Math.Pow(nm, -5) / (Math.Exp(1.4388e7 / (nm * kelvin)) - 1); }

        public static void ColorOf(string c, out float r, out float g, out float b)
        {
            switch (c.Trim())
            {
                case "blue": r = 0.55f; g = 0.68f; b = 1f; break;
                case "blue-white": r = 0.72f; g = 0.80f; b = 1f; break;
                case "white": r = 0.95f; g = 0.95f; b = 1f; break;
                case "yellow-white": r = 1f; g = 0.96f; b = 0.86f; break;
                case "yellow": r = 1f; g = 0.90f; b = 0.62f; break;
                case "orange": r = 1f; g = 0.70f; b = 0.40f; break;
                case "red": r = 1f; g = 0.45f; b = 0.30f; break;
                case "magenta-red": r = 0.85f; g = 0.28f; b = 0.38f; break;   // brown dwarfs: dim deep red-magenta
                default: r = g = b = 1f; break;
            }
        }

        private static StarKind Pick(List<StarKind> kinds, Rng rng, double hotBias)
        {
            // hotBias > 1 favours rare hot stars (young arms); < 1 favours cool red stars (old core)
            double total = 0;
            for (int i = 0; i < kinds.Count; i++) total += Biased(kinds[i], hotBias);
            double x = rng.NextDouble() * total;
            for (int i = 0; i < kinds.Count; i++) { x -= Biased(kinds[i], hotBias); if (x <= 0) return kinds[i]; }
            return kinds[kinds.Count - 1];
        }

        private static double Biased(StarKind k, double hotBias)
        {
            // dead stars glow blue-white but are old: the young arms (hotBias > 1) have fewer of them, the old core more
            if (k.Remnant) return k.Weight * Math.Min(4, 1 / Math.Max(0.25, hotBias));
            double hot = k.B - k.R + 0.6; // bluer = hotter
            return k.Weight * Math.Pow(Math.Max(0.05, hot), Math.Log(hotBias + 1e-9) * 2.0 + 0.0001);
        }

        /// <summary>Generates <paramref name="count"/> stars for the given galaxy type, from <paramref name="seed"/>.</summary>
        public static StarVertex[] Generate(string type, ulong seed, int count, List<StarKind> kinds, int[] kindOut = null)
        {
            var rng = new Rng(seed);
            var stars = new StarVertex[count];
            int arms = 2 + rng.Int(3);
            float twist = rng.Range(2.6f, 4.2f), armSpread = rng.Range(0.22f, 0.38f);
            float barLen = rng.Range(0.25f, 0.45f), tilt = rng.Range(0.35f, 0.7f);
            for (int i = 0; i < count; i++)
            {
                float x, y, z, size = 1f; double hotBias = 1;
                double part = rng.NextDouble();
                switch (ShapeOf(type))
                {
                    case "elliptical":
                    {
                        float r = Math.Abs(rng.Gaussian()) * 0.45f;
                        Vec3 d = RandomDir(rng);
                        x = d.X * r; y = d.Y * r * tilt; z = d.Z * r * 0.85f;
                        hotBias = 0.25; break;
                    }
                    case "irregular":
                    {
                        float a = rng.Range(0, (float)Math.PI * 2), r = (float)Math.Sqrt(rng.NextDouble()) * 0.8f;
                        x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r;
                        float clump = Noise.Fractal(x * 3 + 7, 0, z * 3, (uint)seed, 4);
                        if (clump < 0.45f && rng.NextDouble() < 0.7) { i--; continue; }
                        y = rng.Gaussian() * 0.04f; hotBias = 2.5; break;
                    }
                    case "ring":
                    {
                        if (part < 0.2) { Bulge(rng, 0.08f, out x, out y, out z); hotBias = 0.3; }
                        else { float a = rng.Range(0, (float)Math.PI * 2), r = 0.75f + rng.Gaussian() * 0.06f; x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r; y = rng.Gaussian() * 0.02f; hotBias = 3; }
                        break;
                    }
                    case "lenticular":
                    {
                        if (part < 0.35) { Bulge(rng, 0.14f, out x, out y, out z); hotBias = 0.3; }
                        else { float a = rng.Range(0, (float)Math.PI * 2), r = (float)Math.Abs(rng.Gaussian()) * 0.45f; x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r; y = rng.Gaussian() * 0.02f; hotBias = 0.5; }
                        break;
                    }
                    case "barred-spiral":
                    {
                        // a real barred spiral: a thick glowing oval bar of old stars, an inner ring around it,
                        // and two arms that sweep out from the bar's tips
                        if (part < 0.12) { Bulge(rng, 0.09f, out x, out y, out z); hotBias = 0.3; }
                        else if (part < 0.34)
                        {
                            float bx = rng.Gaussian() * barLen * 0.45f, bz = rng.Gaussian() * barLen * 0.13f;
                            x = bx; z = bz; y = rng.Gaussian() * 0.025f; hotBias = 0.45;
                        }
                        else if (part < 0.44)
                        {
                            float a = rng.Range(0, (float)Math.PI * 2), r = barLen * 1.05f + rng.Gaussian() * 0.035f;
                            x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r * 0.85f; y = rng.Gaussian() * 0.02f; hotBias = 2.2;
                        }
                        else if (part < 0.52)
                        {
                            float a = rng.Range(0, (float)Math.PI * 2), r = rng.Range(0.1f, 1f);
                            x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r; y = rng.Gaussian() * 0.03f; hotBias = 0.8;
                        }
                        else
                        {
                            int arm = rng.Int(2);
                            float r = barLen + (float)Math.Pow(rng.NextDouble(), 0.8) * (1f - barLen);
                            float ang = arm * (float)Math.PI + (float)Math.Log(r / barLen) * twist * 0.9f;
                            ang += rng.Gaussian() * armSpread * 0.55f * (0.6f + r);
                            x = (float)Math.Cos(ang) * r; z = (float)Math.Sin(ang) * r; y = rng.Gaussian() * 0.025f;
                            hotBias = 3.5; size = 1.1f;
                        }
                        break;
                    }
                    default: // spiral family: spiral, grand-design-spiral, flocculent-spiral
                    {
                        int nArms = Math.Max(1, (int)Math.Round(Trait(type, "arms", arms, seed)));                // (its template's arms, else 2 to 4 by chance)
                        if (part < 0.18) { Bulge(rng, 0.10f, out x, out y, out z); hotBias = 0.3; }
                        else if (part < 0.35) { float a = rng.Range(0, (float)Math.PI * 2), r = rng.Range(0.05f, 1f); x = (float)Math.Cos(a) * r; z = (float)Math.Sin(a) * r; y = rng.Gaussian() * 0.03f; hotBias = 0.8; }
                        else
                        {
                            float r = (float)Math.Pow(rng.NextDouble(), 0.7) * 0.95f + 0.05f;
                            int arm = rng.Int(nArms);
                            float spread = armSpread * (float)Trait(type, "arm-spread", 1, seed);              // (how loose its arms are, from its template)
                            float ang = arm * (float)(Math.PI * 2 / nArms) + (float)Math.Log(r / 0.05f + 1) * twist;
                            ang += rng.Gaussian() * spread * (1.2f - r * 0.5f);
                            x = (float)Math.Cos(ang) * r; z = (float)Math.Sin(ang) * r; y = rng.Gaussian() * 0.025f;
                            hotBias = 3.5; size = 1.1f;
                        }
                        break;
                    }
                }
                StarKind k = Pick(kinds, rng, hotBias);
                if (kindOut != null) kindOut[i] = kinds.IndexOf(k);
                float bright = rng.Range(0.12f, 0.45f); // additive glow: keep each star dim so crowds don't blow out
                stars[i] = new StarVertex { X = x, Y = y, Z = z, Size = size * rng.Range(0.6f, 1.4f), R = k.R * bright, G = k.G * bright, B = k.B * bright, A = 1 };
            }
            return stars;
        }

        private static void Bulge(Rng rng, float radius, out float x, out float y, out float z)
        {
            Vec3 d = RandomDir(rng); float r = Math.Abs(rng.Gaussian()) * radius;
            x = d.X * r; y = d.Y * r * 0.7f; z = d.Z * r;
        }

        private static Vec3 RandomDir(Rng rng)
        {
            return new Vec3(rng.Gaussian(), rng.Gaussian(), rng.Gaussian()).Normalized;
        }
    }
}
