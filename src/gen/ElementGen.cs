// DNA-AND · generators · the 118 elements as real samples, from content/elements.dnat.
// Solids are lumps with the element's real look, gases glow in their own colour inside a glass bulb,
// liquids are drops, and lab-made elements are a faint flicker because they fall apart almost at once.
using System;
using System.Collections.Generic;
using DnaAnd.Core;
using DnaAnd.Render;

namespace DnaAnd.Gen
{
    internal sealed class ElementSample
    {
        public int Number; public string Symbol, Name, Group, State, Origin, Use, FoundIn;
        public int Col, Row;                       // place in the periodic table (rows 8 and 9 are the two lower rows)
        public float R, G, B; public bool Shiny, Radioactive;
        public double MeltK = -1, BoilK = -1;      // -1: never measured

        /// <summary>solid, liquid, gas, or unknown, at a temperature (kelvin) and pressure (atmospheres).
        /// Higher pressure raises the boiling point; lower pressure lowers it.</summary>
        public string StateAt(double k, double atm)
        {
            if (MeltK < 0) return "unknown";
            double boil = BoilK < 0 ? MeltK * 1.6 : BoilK * (1 + 0.06 * Math.Log(Math.Max(1e-6, atm)));
            boil = Math.Max(boil, MeltK * (BoilK == MeltK ? 1 : 1.0001));
            if (k < MeltK && !(BoilK == MeltK && k >= boil)) return "solid";
            if (k < boil && BoilK != MeltK) return "liquid";
            return k >= boil ? "gas" : "solid";
        }
        public Vec3 Centre;
    }

    internal static class ElementGen
    {
        /// <summary>The element's place in the standard periodic table, from its number.</summary>
        public static void Place(int z, out int col, out int row)
        {
            if (z == 1) { col = 1; row = 1; return; }
            if (z == 2) { col = 18; row = 1; return; }
            if (z <= 18)
            {
                int start = z <= 10 ? 3 : 11; row = z <= 10 ? 2 : 3;
                int k = z - start;
                col = k < 2 ? k + 1 : k + 11; return;
            }
            if (z <= 54) { row = z <= 36 ? 4 : 5; col = z - (z <= 36 ? 18 : 36); return; }
            int first = z <= 86 ? 55 : 87; int prow = z <= 86 ? 6 : 7;
            int i = z - first;
            if (i < 2) { col = i + 1; row = prow; return; }
            if (i < 17) { col = i + 1; row = prow + 2; return; }       // the lanthanides and actinides, in the two lower rows
            col = i - 13; row = prow; return;                             // hafnium (or rutherfordium) onward: columns 4 to 18
        }

        private static float[] Rgb(string t) { var w = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); float r, g, b; var ci = System.Globalization.CultureInfo.InvariantCulture; return w.Length == 3 && float.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out r) && float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out g) && float.TryParse(w[2], System.Globalization.NumberStyles.Float, ci, out b) ? new[] { r, g, b } : null; }
        public static List<ElementSample> Load(ContentLibrary content)
        {
            var list = new List<ElementSample>();
            foreach (var e in content.OfKind("elements.dnat", "element"))
            {
                int z; if (!int.TryParse(e.Get("number", "0"), out z) || z < 1) continue;
                var s = new ElementSample { Number = z, Symbol = e.Get("symbol", "?"), Name = e.Name, Group = e.Get("group", ""),
                    State = e.Get("state-at-room-temp", "unknown"), Origin = e.Get("origin", ""), Use = e.Get("in-game", "") };
                Place(z, out s.Col, out s.Row);
                double mk, bk;
                if (double.TryParse(e.Get("melt-k", "x"), out mk)) s.MeltK = mk;
                if (double.TryParse(e.Get("boil-k", "x"), out bk)) s.BoilK = bk;
                bool metal = s.Group.Contains("metal") || s.Group == "Transition" || s.Group == "Post-transition" || s.Group == "Lanthanide" || s.Group == "Actinide";
                s.R = 0.72f; s.G = 0.73f; s.B = 0.76f; s.Shiny = metal;
                float[] look;
                look = Rgb(e.Get("look", "")); if (look != null) { s.R = look[0]; s.G = look[1]; s.B = look[2]; s.Shiny = e.Get("shiny", "no") == "yes"; }   // elements.dnat
                else if (!metal && s.State == "solid") { s.R = 0.6f; s.G = 0.58f; s.B = 0.55f; }
                float[] glow;
                glow = Rgb(e.Get("glow-colour", "")); if (s.State == "gas" && glow != null) { s.R = glow[0]; s.G = glow[1]; s.B = glow[2]; }
                s.Radioactive = s.Group == "Actinide" || z == 43 || z == 61 || z >= 84;
                list.Add(s);
            }
            list.Sort((a, b) => a.Number.CompareTo(b.Number));
            // where each is found: from the resource fields of set 5 and the space fields
            foreach (var s in list) s.FoundIn = FoundIn(s);
            return list;
        }

        private static string FoundIn(ElementSample s)
        {
            if (s.Origin.StartsWith("lab")) return "nowhere in nature in any amount: made in laboratories, one atom at a time";
            switch (s.Symbol)
            {
                case "H": case "He": return "stars, gas giants and nebula gas; hydrogen also in ocean water";
                case "O": return "the air, ocean water, and nearly every rock";
                case "Na": case "Cl": case "K": case "Mg": return "ocean water and salt flats (evaporites)";
                case "S": return "volcano vents (volcanic sulfide fields)";
                case "Fe": return "banded iron formations, laterite soil, metal asteroids, planet cores";
                case "Au": return "mountain-belt gold veins and river placers";
                case "Cu": return "porphyry copper over the mountain belts, skarns, buried copper basins";
                case "C": return "coal, oil and gas (only once life has lived), diamond pipes (kimberlite), carbon asteroids";
                case "Ni": case "Pt": case "Cr": return "layered intrusions and metal asteroids";
                case "Li": case "Be": case "Ta": case "Nb": case "Cs": case "Rb": return "pegmatites";
                case "Al": return "laterite soil (bauxite) and most rock";
                case "Si": return "sand and nearly every rock";
                case "Ag": case "Zn": case "Pb": return "hot-spring veins (epithermal) and volcanic sulfides";
                case "U": case "Th": return "pegmatites and old continent rock";
                case "Sn": case "W": case "Mo": return "porphyry and skarn deposits";
                case "N": case "Ar": return "the air";
                case "Ne": case "Kr": case "Xe": return "the air, in tiny amounts";
            }
            if (s.Group == "Lanthanide") return "rare-earth layers in pegmatites and placers";
            if (s.Group == "Transition") return "metal ores in mountain belts and metal asteroids";
            return "scattered through rock and seawater";
        }

        /// <summary>The points of one sample, centred on 0, about 0.4 across.</summary>
        public static void Build(ElementSample s, string state, Rng rng, List<StarVertex> outPts)
        {
            bool gas = state == "gas", liquid = state == "liquid", lab = state == "unknown";
            if (gas)
            {
                // a glass bulb, and the gas glowing inside it
                for (int i = 0; i < 160; i++) outPts.Add(SpaceGen.Star(OnSphere(rng) * 0.22f, 0.012f, 0.7f, 0.8f, 0.9f, 0.18f));
                for (int i = 0; i < 260; i++) outPts.Add(SpaceGen.Star(InSphere(rng) * 0.19f, 0.03f, s.R, s.G, s.B, 0.22f));
                return;
            }
            if (lab)
            {
                // a few short-lived atoms: a faint flickering glow
                for (int i = 0; i < 40; i++) outPts.Add(SpaceGen.Star(InSphere(rng) * 0.12f, 0.02f, 0.6f, 0.9f, 0.7f, 0.25f));
                return;
            }
            // solids and liquids: a lump (or a flattened drop), faceted, lit as metal or as a dull solid
            int n = 700;
            for (int i = 0; i < n; i++)
            {
                double yv = 1 - 2 * (i + 0.5) / n, rr = Math.Sqrt(1 - yv * yv), th = i * 2.399963229728653;
                var dir = new Vec3((float)(Math.Cos(th) * rr), (float)yv, (float)(Math.Sin(th) * rr));
                float bump = liquid ? 1f : 0.82f + 0.3f * Noise.Fractal(dir.X * 3 + s.Number, dir.Y * 3, dir.Z * 3, (uint)s.Number, 3);
                var p = dir * (0.18f * bump);
                if (liquid) p = new Vec3(p.X * 1.2f, p.Y * 0.55f - 0.04f, p.Z * 1.2f);             // a drop resting on the shelf
                float shade = 0.55f + 0.45f * Math.Max(0, Vec3.Dot(dir, new Vec3(-0.5f, 0.75f, -0.45f)));
                if (s.Shiny) shade += (float)Math.Pow(Math.Max(0, Vec3.Dot(dir, new Vec3(-0.35f, 0.8f, -0.5f))), 18) * 0.9f;   // metal glint
                if (p.Z > 0.03f) continue;                                    // only the side facing the viewer: piled-up points wash out to white
                outPts.Add(SpaceGen.Star(p, 0.02f, s.R * shade, s.G * shade, s.B * shade, 0.5f));
            }
            if (s.Radioactive)
                for (int i = 0; i < 60; i++) outPts.Add(SpaceGen.Star(InSphere(rng) * 0.26f, 0.015f, 0.4f, 1f, 0.5f, 0.2f));   // a faint glow from its radiation
        }

        private static Vec3 OnSphere(Rng rng)
        {
            double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - u * u);
            return new Vec3((float)(Math.Cos(a) * r), (float)u, (float)(Math.Sin(a) * r));
        }
        private static Vec3 InSphere(Rng rng) { return OnSphere(rng) * (float)Math.Pow(rng.NextDouble(), 1.0 / 3); }
    }
}
