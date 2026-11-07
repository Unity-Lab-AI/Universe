// Universe · generators · what an intelligent species builds, from its own genes: its tribe's camp, villages, towns,
// cities, spaceships, hyperdrive ships, space stations and colonies. Nothing is a fixed model. The builders' body plan
// sets the way they build, so an insect-like species raises mounds and hive spires, a round soft-bodied one builds domes,
// an armoured one builds spiked walls and roofs, and a sea-going one builds on stilts. Their colours run through all of it.
using System;
using Universe.Core;
using Universe.Sim;

namespace Universe.Gen
{
    internal static class CivGen
    {
        public static readonly string[] Names = { "the species", "tribe camp", "village", "town", "city", "spaceship", "hyperdrive ship", "space station", "colony" };
        public static readonly string[] StyleNames = { "boxes with pointed roofs", "domes", "mounds and hive spires", "spiked forts", "houses on stilts" };

        /// <summary>How a species builds, from its body plan.</summary>
        public static int Style(Genome g)
        {
            if (g.Legs > 0.6f && g.Segs > 0.55f) return 2;     // many legs and many segments: insect-like, mounds and spires
            if (g.Armour > 0.5f) return 3;                     // armoured: spiked forts
            if (g.Fins > 0.35f) return 4;                      // at home in water: stilts
            if (g.Segs < 0.35f) return 1;                      // round, few segments: domes
            return 0;
        }

        public static Body Build(Genome g, int what, ulong seed)
        {
            if (what > 0) { float sr, sg, sb; GeneBody.Skin(g, out sr, out sg, out sb); g.R = sr; g.G = sg; g.B = sb; }   // their colours, bright enough to read
            var r = new Rng(seed * 0x9E3779B97F4A7C15UL + (ulong)what * 977UL + 5);
            var b = new Body { Name = Names[what] };
            int style = Style(g);
            switch (what)
            {
                case 0: return GeneBody.FromGenome(g, seed);
                case 1: Camp(b, g, r, style); break;
                case 2: Settle(b, g, r, style, 1); break;
                case 3: Settle(b, g, r, style, 2); break;
                case 4: Settle(b, g, r, style, 3); break;
                case 5: Ship(b, g, r, style, false); break;
                case 6: Ship(b, g, r, style, true); break;
                case 7: Station(b, g, r, style); break;
                default: Colony(b, g, r, style); break;
            }
            return b;
        }

        // the species' colour mixed into a building material
        private static float Mix(float gene, float mat, float k) { return Math.Min(1, gene * k + mat * (1 - k)); }

        // ── one building in the species' way of building; tier 0 hide, 1 wood and straw, 2 stone, 3 concrete and glass ──
        private static void House(Body b, Genome g, Rng r, int style, int tier, Vec3 at, float s, float tall)
        {
            float wr, wg, wb, rr, rg, rb;
            switch (tier)
            {
                case 0: wr = 0.6f; wg = 0.45f; wb = 0.3f; break;
                case 1: wr = 0.62f; wg = 0.48f; wb = 0.32f; break;
                case 2: wr = 0.7f; wg = 0.68f; wb = 0.62f; break;
                default: wr = 0.72f; wg = 0.74f; wb = 0.78f; break;
            }
            float k = tier == 3 ? 0.35f : 0.25f;
            wr = Mix(g.R, wr, k); wg = Mix(g.G, wg, k); wb = Mix(g.B, wb, k);
            rr = Mix(g.R, tier == 1 ? 0.85f : 0.45f, 0.5f); rg = Mix(g.G, tier == 1 ? 0.72f : 0.3f, 0.5f); rb = Mix(g.B, tier == 1 ? 0.3f : 0.25f, 0.5f);
            float glow = tier >= 3 ? 0.35f : 0;
            float h = s * tall;
            switch (style)
            {
                case 1:   // domes, stacked higher the bigger the place
                    int levels = Math.Max(1, (int)(tall * 0.8f));
                    for (int l = 0; l < levels; l++) { float w = s * (1 - l * 0.15f); b.Blob(at + new Vec3(0, s * 0.5f + l * s * 0.9f, 0), w, s * 0.6f, w, wr, wg, wb, l == levels - 1 ? glow : 0); }
                    break;
                case 2:   // a mound, or a hive spire in towns and cities
                    b.Cone(at, new Vec3(0, 1, 0), h * 1.4f + s, s * 1.1f, Mix(g.R, 0.55f, 0.3f), Mix(g.G, 0.42f, 0.3f), Mix(g.B, 0.3f, 0.3f), glow * 0.5f);
                    for (int hole = 0; hole < 3; hole++) b.Blob(at + new Vec3(s * 0.5f, s * (0.3f + hole * 0.5f) * Math.Max(1, tall * 0.5f), 0), s * 0.14f, s * 0.14f, s * 0.14f, 0.12f, 0.1f, 0.08f, glow);
                    break;
                case 3:   // a squat fort with spikes on the roof
                    b.Box(at + new Vec3(0, h * 0.5f, 0), s * 0.8f, h * 0.5f, s * 0.8f, wr, wg, wb, 0, r.Range(0, 1.5f));
                    for (int sp = 0; sp < 4; sp++) b.Cone(at + new Vec3((sp & 1) == 0 ? -s * 0.5f : s * 0.5f, h, (sp & 2) == 0 ? -s * 0.5f : s * 0.5f), new Vec3(0, 1, 0), s * 0.8f, s * 0.15f, rr, rg, rb);
                    if (glow > 0) b.Box(at + new Vec3(0, h * 0.6f, 0), s * 0.82f, h * 0.06f, s * 0.82f, 1f, 0.85f, 0.5f, 1f);
                    break;
                case 4:   // up on stilts, with a round roof
                    for (int st = 0; st < 4; st++) b.Limb(at + new Vec3((st & 1) == 0 ? -s * 0.5f : s * 0.5f, 0, (st & 2) == 0 ? -s * 0.5f : s * 0.5f), at + new Vec3((st & 1) == 0 ? -s * 0.5f : s * 0.5f, s * 0.8f, (st & 2) == 0 ? -s * 0.5f : s * 0.5f), s * 0.07f, 0.4f, 0.3f, 0.2f);
                    b.Box(at + new Vec3(0, s * 0.8f + h * 0.4f, 0), s * 0.7f, h * 0.4f, s * 0.7f, wr, wg, wb, glow);
                    b.Blob(at + new Vec3(0, s * 0.8f + h * 0.8f, 0), s * 0.8f, s * 0.35f, s * 0.8f, rr, rg, rb);
                    break;
                default:  // walls and a pointed roof; in cities, tall towers with lit windows
                    float turn = r.Range(0, 1.5f);
                    b.Box(at + new Vec3(0, h * 0.5f, 0), s * 0.7f, h * 0.5f, s * 0.6f, wr, wg, wb, 0, turn);
                    if (tier < 3) b.Cone(at + new Vec3(0, h, 0), new Vec3(0, 1, 0), s * 0.8f, s * 0.95f, rr, rg, rb);
                    else for (int w = 1; w < (int)(tall * 1.5f); w++) b.Box(at + new Vec3(0, w * h / (tall * 1.5f), 0), s * 0.72f, s * 0.04f, s * 0.62f, 1f, 0.88f, 0.55f, 1f, turn);   // window rows
                    break;
            }
        }

        private static void Ground(Body b, float size, float r, float g, float bl) { b.GroundSlab(new Vec3(0, -0.62f, 0), size, 0.04f, size, r, g, bl); }

        // a tribe's camp: shelters of hide round a fire, drying racks, and the tribe's own banner
        private static void Camp(Body b, Genome g, Rng r, int style)
        {
            Ground(b, 1.2f, 0.4f, 0.55f, 0.25f);
            int n = 4 + r.Below(3);
            for (int k = 0; k < n; k++)
            {
                double a = k * Math.PI * 2 / n + r.Range(-0.2f, 0.2f); float d = r.Range(0.45f, 0.7f);
                var at = new Vec3((float)Math.Cos(a) * d, -0.58f, (float)Math.Sin(a) * d);
                if (style == 1 || style == 4) b.Blob(at + new Vec3(0, 0.06f, 0), 0.16f, 0.14f, 0.16f, Mix(g.R, 0.6f, 0.3f), Mix(g.G, 0.45f, 0.3f), Mix(g.B, 0.3f, 0.3f));
                else if (style == 2) b.Cone(at, new Vec3(0, 1, 0), 0.3f, 0.15f, Mix(g.R, 0.55f, 0.3f), Mix(g.G, 0.42f, 0.3f), Mix(g.B, 0.3f, 0.3f));
                else b.Cone(at, new Vec3(0, 1, 0), 0.32f, 0.17f, Mix(g.R, 0.62f, 0.3f), Mix(g.G, 0.48f, 0.3f), Mix(g.B, 0.32f, 0.3f));
            }
            b.Blob(new Vec3(0, -0.56f, 0), 0.09f, 0.04f, 0.09f, 0.4f, 0.38f, 0.35f);
            b.Cone(new Vec3(0, -0.56f, 0), new Vec3(0, 1, 0), 0.18f, 0.06f, 1f, 0.55f, 0.1f, 1f);
            b.Limb(new Vec3(0.25f, -0.58f, 0.2f), new Vec3(0.25f, -0.05f, 0.2f), 0.012f, 0.45f, 0.32f, 0.2f);                  // the banner pole
            b.Box(new Vec3(0.33f, -0.12f, 0.2f), 0.08f, 0.06f, 0.005f, g.R, g.G, g.B);                                         // in the tribe's colours
            for (int k = 0; k < 2; k++) b.Box(new Vec3(-0.25f + k * 0.12f, -0.48f, -0.3f), 0.05f, 0.1f, 0.01f, 0.5f, 0.35f, 0.2f);
            b.Radius = 1.4f;
        }

        // villages (tier 1), towns (tier 2) and cities (tier 3): more and bigger buildings, streets, walls, a great building at the middle
        private static void Settle(Body b, Genome g, Rng r, int style, int tier)
        {
            float size = 1.2f + tier * 0.25f;
            Ground(b, size, tier == 3 ? 0.35f : 0.4f, tier == 3 ? 0.38f : 0.55f, tier == 3 ? 0.36f : 0.25f);
            if (style == 4) b.Box(new Vec3(0, -0.6f, 0), size * 0.95f, 0.03f, size * 0.95f, 0.2f, 0.45f, 0.65f);               // they build over water
            int grid = tier == 1 ? 3 : tier == 2 ? 5 : 7;
            float cell = size * 1.7f / grid;
            for (int x = 0; x < grid; x++)
                for (int z = 0; z < grid; z++)
                {
                    if (tier == 1 && r.NextDouble() < 0.35) continue;                                                           // villages are loose
                    var at = new Vec3((x - (grid - 1) * 0.5f) * cell, -0.58f, (z - (grid - 1) * 0.5f) * cell);
                    bool middle = x == grid / 2 && z == grid / 2;
                    float s = cell * (middle ? 0.42f : r.Range(0.25f, 0.36f));
                    float tall = middle ? (tier == 3 ? 9 : tier == 2 ? 3.5f : 2) : tier == 3 ? r.Range(2.5f, 7) : tier == 2 ? r.Range(1, 1.8f) : r.Range(0.8f, 1.2f);
                    House(b, g, r, style, tier, at, s, tall);
                }
            if (tier >= 2)
            {
                // streets between the blocks
                for (int k = 1; k < grid; k++)
                {
                    float o = (k - grid * 0.5f) * cell;
                    b.Box(new Vec3(o, -0.575f, 0), cell * 0.06f, 0.006f, size * 0.85f, 0.3f, 0.3f, 0.32f, tier == 3 ? 0.15f : 0);
                    b.Box(new Vec3(0, -0.575f, o), size * 0.85f, 0.006f, cell * 0.06f, 0.3f, 0.3f, 0.32f, tier == 3 ? 0.15f : 0);
                }
            }
            if (tier == 1)
            {
                b.Box(new Vec3(size * 0.6f, -0.57f, -size * 0.6f), size * 0.25f, 0.01f, size * 0.2f, 0.75f, 0.65f, 0.3f);        // a field
                for (int k = 0; k < 40; k++) { double a = k * Math.PI * 2 / 40; b.Box(new Vec3((float)Math.Cos(a) * size * 0.93f, -0.52f, (float)Math.Sin(a) * size * 0.93f), 0.02f, 0.07f, 0.02f, 0.5f, 0.35f, 0.2f); }
            }
            if (tier == 2 || (tier == 3 && g.Armour > 0.4f))
            {
                // a wall with towers; spiked if the builders are armoured
                float w = size * 0.95f, wh = 0.08f + tier * 0.03f;
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2; float o = side % 2 == 0 ? -w : w;
                    b.Box(alongX ? new Vec3(0, -0.58f + wh, o) : new Vec3(o, -0.58f + wh, 0), alongX ? w : 0.025f, wh, alongX ? 0.025f : w, 0.62f, 0.6f, 0.56f);
                }
                for (int c = 0; c < 4; c++)
                {
                    var at = new Vec3((c & 1) == 0 ? -w : w, -0.58f, (c & 2) == 0 ? -w : w);
                    b.Box(at + new Vec3(0, wh * 1.6f, 0), 0.06f, wh * 1.6f, 0.06f, 0.6f, 0.58f, 0.54f);
                    b.Cone(at + new Vec3(0, wh * 3.2f, 0), new Vec3(0, 1, 0), 0.1f, 0.07f, Mix(g.R, 0.5f, 0.5f), Mix(g.G, 0.3f, 0.5f), Mix(g.B, 0.25f, 0.5f));
                }
            }
            b.Radius = size * 1.2f;
        }

        // a ship shaped like its builders: the hull follows their body segments, with their wings or fins, spikes if armoured
        private static void Ship(Body b, Genome g, Rng r, int style, bool hyper)
        {
            int segs = 2 + (int)Math.Round(g.Segs * 4) + (hyper ? 2 : 0);
            float hr = Mix(g.R, 0.72f, 0.55f), hg = Mix(g.G, 0.74f, 0.55f), hb = Mix(g.B, 0.78f, 0.55f);
            float len = hyper ? 2.2f : 1.6f, step = len / segs, rad = (hyper ? 0.22f : 0.16f) * (style == 1 ? 1.3f : 1);
            for (int k = 0; k < segs; k++)
            {
                float x = -len * 0.5f + (k + 0.5f) * step, sr = rad * (0.75f + 0.35f * (float)Math.Sin((k + 0.5) / segs * Math.PI));
                if (style == 3 || style == 0) b.Box(new Vec3(x, 0, 0), step * 0.48f, sr, sr, hr, hg, hb);
                else b.Blob(new Vec3(x, 0, 0), step * 0.62f, sr, sr, hr, hg, hb);
                if (k % 2 == 1) b.Box(new Vec3(x, 0, 0), step * 0.12f, sr * 1.04f, sr * 1.04f, g.R, g.G, g.B);   // a band in their own colour
                if (g.Armour > 0.4f) b.Cone(new Vec3(x, sr * 0.9f, 0), new Vec3(-0.3f, 1, 0), sr * 0.8f, sr * 0.25f, hr * 0.6f, hg * 0.6f, hb * 0.6f);
            }
            b.Cone(new Vec3(len * 0.5f, 0, 0), new Vec3(1, 0, 0), rad * 2.2f, rad * 0.8f, hr, hg, hb);                       // nose
            b.Blob(new Vec3(len * 0.3f, rad * 0.75f, 0), rad * 0.9f, rad * 0.4f, rad * 0.5f, 0.35f, 0.8f, 1f, 0.5f);          // bridge windows
            // wings like the builders' wings, or fins like their fins, or plain struts
            float span = g.Wings > 0.3f ? 0.5f + g.Wings * 0.6f : g.Fins > 0.3f ? 0.35f : 0.28f;
            for (int s = -1; s <= 1; s += 2)
            {
                if (g.Wings > 0.3f) b.Parts.Add(new Part { Kind = 0, C = new Vec3(-len * 0.05f, 0, s * (rad + span * 0.5f)), U = new Vec3(1, 0, 0), V = new Vec3(0, 1, 0), W = new Vec3(0, 0, 1), SU = len * 0.22f, SV = 0.02f, SW = span * 0.55f, R = g.R, G = g.G, B = g.B });
                else b.Box(new Vec3(-len * 0.15f, 0, s * (rad + span * 0.5f)), len * 0.12f, 0.02f, span * 0.5f, g.R, g.G, g.B, 0, s * 0.3f);
                b.Limb(new Vec3(-len * 0.5f - 0.05f, 0, s * rad * 0.6f), new Vec3(-len * 0.3f, 0, s * rad * 0.6f), rad * 0.45f, 0.4f, 0.42f, 0.45f);   // engine
                b.Blob(new Vec3(-len * 0.5f - 0.1f, 0, s * rad * 0.6f), 0.04f, rad * 0.35f, rad * 0.35f, 0.4f, 0.8f, 1f, 1f);               // engine glow
            }
            if (hyper)
            {
                // the hyperdrive: a glowing ring round the middle of the ship
                for (int k = 0; k < 28; k++)
                {
                    double a = k * Math.PI * 2 / 28;
                    b.Blob(new Vec3(-len * 0.1f, (float)Math.Cos(a) * rad * 2.6f, (float)Math.Sin(a) * rad * 2.6f), 0.05f, 0.06f, 0.06f, 0.55f, 0.5f, 1f, 1f);
                }
                for (int k = 0; k < 4; k++) { double a = k * Math.PI / 2 + 0.785; b.Limb(new Vec3(-len * 0.1f, 0, 0), new Vec3(-len * 0.1f, (float)Math.Cos(a) * rad * 2.5f, (float)Math.Sin(a) * rad * 2.5f), 0.02f, 0.5f, 0.52f, 0.55f); }
            }
            b.Radius = len * 0.75f;
        }

        // a space station: a hub with spokes out to a turning ring of modules built in the species' way
        private static void Station(Body b, Genome g, Rng r, int style)
        {
            float hr = Mix(g.R, 0.76f, 0.5f), hg = Mix(g.G, 0.78f, 0.5f), hb = Mix(g.B, 0.82f, 0.5f);
            b.Blob(new Vec3(0, 0, 0), 0.22f, 0.22f, 0.22f, hr, hg, hb);
            b.Limb(new Vec3(0, -0.5f, 0), new Vec3(0, 0.5f, 0), 0.06f, hr * 0.9f, hg * 0.9f, hb * 0.9f);                        // docking spine
            b.Blob(new Vec3(0, 0.55f, 0), 0.08f, 0.04f, 0.08f, 0.4f, 0.9f, 0.5f, 1f);                                           // docking light
            int mods = 12 + (int)(g.Segs * 8);
            for (int k = 0; k < mods; k++)
            {
                double a = k * Math.PI * 2 / mods; var at = new Vec3((float)Math.Cos(a) * 0.9f, 0, (float)Math.Sin(a) * 0.9f);
                if (style == 1 || style == 4) b.Blob(at, 0.16f, 0.1f, 0.16f, hr, hg, hb);
                else if (style == 2) b.Cone(at - new Vec3(0, 0.1f, 0), new Vec3(0, 1, 0), 0.22f, 0.12f, hr, hg, hb);
                else b.Box(at, 0.13f, 0.08f, 0.13f, hr, hg, hb, 0, -(float)a);
                b.Box(at + new Vec3(0, 0.081f, 0), 0.08f, 0.01f, 0.08f, 1f, 0.85f, 0.5f, 1f, -(float)a);                             // lit windows
            }
            for (int k = 0; k < 4; k++) { double a = k * Math.PI / 2; b.Limb(new Vec3(0, 0, 0), new Vec3((float)Math.Cos(a) * 0.85f, 0, (float)Math.Sin(a) * 0.85f), 0.03f, hr * 0.8f, hg * 0.8f, hb * 0.8f); }
            for (int s = -1; s <= 1; s += 2) b.Box(new Vec3(s * 0.45f, 0.35f, 0), 0.3f, 0.005f, 0.12f, 0.15f, 0.2f, 0.45f, 0.2f);   // solar panels
            b.Radius = 1.2f;
        }

        // a colony on another world: sealed buildings on bare rock, tubes between them, a landing pad, a ship on it
        private static void Colony(Body b, Genome g, Rng r, int style)
        {
            b.Blob(new Vec3(0, -0.7f, 0), 1.3f, 0.12f, 1.3f, 0.55f, 0.4f, 0.32f);                                                 // bare red rock
            for (int k = 0; k < 6; k++) b.Blob(new Vec3(r.Range(-1, 1), -0.6f, r.Range(-1, 1)), r.Range(0.08f, 0.18f), r.Range(0.05f, 0.12f), r.Range(0.08f, 0.18f), 0.5f, 0.36f, 0.3f);
            int n = 5 + r.Below(3); var spots = new Vec3[n];
            for (int k = 0; k < n; k++)
            {
                double a = k * Math.PI * 2 / n; float d = k == 0 ? 0 : 0.55f;
                spots[k] = new Vec3((float)Math.Cos(a) * d, -0.58f, (float)Math.Sin(a) * d);
                float s = k == 0 ? 0.28f : 0.17f;
                if (style == 2) b.Cone(spots[k], new Vec3(0, 1, 0), s * 2.2f, s, Mix(g.R, 0.7f, 0.5f), Mix(g.G, 0.7f, 0.5f), Mix(g.B, 0.72f, 0.5f), 0.15f);
                else if (style == 3 || style == 0) { b.Box(spots[k] + new Vec3(0, s * 0.5f, 0), s, s * 0.5f, s, Mix(g.R, 0.75f, 0.5f), Mix(g.G, 0.75f, 0.5f), Mix(g.B, 0.78f, 0.5f)); b.Blob(spots[k] + new Vec3(0, s, 0), s * 0.9f, s * 0.4f, s * 0.9f, 0.5f, 0.8f, 0.95f, 0.35f); }
                else b.Blob(spots[k], s, s * 0.8f, s, 0.55f, 0.82f, 0.95f, 0.35f);                                                 // glass domes
                if (k > 0) b.Limb(spots[0] + new Vec3(0, 0.05f, 0), spots[k] + new Vec3(0, 0.05f, 0), 0.03f, Mix(g.R, 0.7f, 0.4f), Mix(g.G, 0.72f, 0.4f), Mix(g.B, 0.75f, 0.4f)); // tubes
            }
            var pad = new Vec3(0.75f, -0.58f, 0.6f);
            b.Box(pad, 0.22f, 0.01f, 0.22f, 0.4f, 0.42f, 0.45f);
            b.Blob(pad + new Vec3(0, 0.012f, 0), 0.04f, 0.005f, 0.04f, 1f, 0.6f, 0.2f, 1f);
            b.Radius = 1.4f;
        }
    }
}
