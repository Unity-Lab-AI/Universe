// Universe · generators · bodies from genes: what the species in the running game look like.
// The stage of life decides what kind of thing a genome grows into (a virus, a cell, a plant, fungus or jelly, an animal);
// the body-plan genes decide its shape. Every mutation shows, so a family keeps its resemblance as it changes.
using System;
using Universe.Core;
using Universe.Sim;

namespace Universe.Gen
{
    internal static class GeneBody
    {
        public static Body FromGenome(Genome g, ulong seed)
        {
            var r = new Rng(seed * 2862933555777941757UL + 3037000493UL);
            var b = new Body { Name = PlanetLife.StageNames[Math.Max(0, Math.Min(PlanetLife.StageNames.Length - 1, g.Stage))] };
            if (g.Stage <= 0) Virus(b, g);
            else if (g.Stage == 1) Cell(b, g, r);
            else if (g.Stage == 2)
            {
                if (g.Photo > 0.5f) Plant(b, g, r);
                else if (g.Graze > 0.5f && g.Speed < 0.3f) Fungus(b, g, r);
                else Jelly(b, g);
            }
            else Animal(b, g);
            return b;
        }

        private static int Count(float gene, int max) { return (int)Math.Round(gene * max); }
        private static Vec3 Dir(Rng r) { return new Vec3(r.Gaussian(), r.Gaussian(), r.Gaussian()).Normalized; }

        private static void Virus(Body b, Genome g)
        {
            b.Blob(new Vec3(0, 0, 0), 0.55f, 0.55f, 0.55f, g.R, g.G, g.B);
            int n = 8 + g.Surface % 20;                                     // the surface gene sets the spikes it fits its hosts with
            float len = 0.8f + (g.Surface % 7) * 0.05f;
            for (int k = 0; k < n; k++)
            {
                double y = 1 - 2 * (k + 0.5) / n, rr = Math.Sqrt(1 - y * y), th = k * 2.399963;
                var d = new Vec3((float)(Math.Cos(th) * rr), (float)y, (float)(Math.Sin(th) * rr));
                b.Limb(d * 0.45f, d * len, 0.045f, 0.9f, 0.85f, 0.7f);
                b.Blob(d * (len + 0.03f), 0.08f, 0.08f, 0.08f, Math.Min(1, g.R + 0.3f), g.G * 0.7f, g.B * 0.5f, 0.2f);
            }
            b.Radius = 1;
        }

        private static void Cell(Body b, Genome g, Rng r)
        {
            float len = g.Shape == 0 ? 0 : 0.4f + g.Size * 0.15f, rad = 0.3f + g.Size * 0.05f;
            if (g.Shape == 2)
            {
                Vec3 prev = new Vec3(-0.8f, 0, 0);
                for (int k = 1; k <= 10; k++) { var p = new Vec3(-0.8f + k * 0.16f, (float)Math.Sin(k * 0.8) * 0.2f, (float)Math.Cos(k * 0.8) * 0.2f); b.Limb(prev, p, rad * 0.45f, g.R, g.G, g.B); prev = p; }
            }
            else if (len > 0) b.Limb(new Vec3(-len, 0, 0), new Vec3(len, 0, 0), rad, g.R, g.G, g.B);
            else b.Blob(new Vec3(0, 0, 0), rad * 1.4f, rad * 1.3f, rad * 1.4f, g.R, g.G, g.B);
            for (int k = 0; k < (int)(g.Coat * 30); k++) { var d = Dir(r); b.Limb(d * rad, d * (rad + 0.25f), 0.015f, 0.95f, 0.9f, 0.75f); }   // a tough coat: hairs
            for (int k = 0; k < (int)(g.Photo * 8); k++) b.Blob(Dir(r) * (rad * 0.5f), 0.07f, 0.05f, 0.07f, 0.2f, 0.8f, 0.25f);                 // light-catchers
            if (g.Hunt > 0.4f) b.Cone(new Vec3(len + rad * 0.8f, 0, 0), new Vec3(1, 0, 0), 0.2f, 0.12f, 0.9f, 0.3f, 0.3f);                       // a mouth for eating others
            if (g.Speed > 0.25f)
            {
                Vec3 prev = new Vec3(-len - rad, 0, 0); int n = 4 + (int)(g.Speed * 10);
                for (int k = 1; k <= n; k++) { var p = new Vec3(-len - rad - k * 0.1f, (float)Math.Sin(k * 0.9) * 0.1f, (float)Math.Cos(k * 0.9) * 0.1f); b.Limb(prev, p, 0.025f, 0.85f, 0.85f, 0.6f); prev = p; }
            }
            b.Radius = 1.2f;
        }

        private static void Plant(Body b, Genome g, Rng r)
        {
            b.GroundDisc(new Vec3(0, -0.95f, 0), 0.9f, 0.08f, 0.9f, 0.35f, 0.3f, 0.2f);
            float h = 0.4f + g.Size * 0.25f;
            float lr = Math.Min(1, g.R * 0.4f), lg = 0.4f + g.G * 0.5f, lb = g.B * 0.35f;     // leaves: green, tinted by the family colour
            Branch(b, r, new Vec3(0, -0.9f, 0), new Vec3(0, 1, 0), h, 0.06f + g.Size * 0.03f, 1 + Count(g.Branch, 2), lr, lg, lb, g.Toxin > 0.5f);
            b.Radius = 1.25f;
        }
        private static void Branch(Body b, Rng r, Vec3 from, Vec3 dir, float len, float rad, int left, float lr, float lg, float lb, bool berries)
        {
            var to = from + dir * len;
            b.Limb(from, to, rad, 0.45f, 0.32f, 0.2f);
            if (left == 0)
            {
                float s = 0.18f + len * 0.3f; b.Blob(to, s, s * 0.8f, s, lr, lg, lb);
                if (berries) for (int k = 0; k < 3; k++) b.Blob(to + Dir(r) * s, 0.04f, 0.04f, 0.04f, 0.9f, 0.15f, 0.2f);   // poison berries
                return;
            }
            int n = 2 + r.Below(2);
            for (int k = 0; k < n; k++)
            {
                var nd = (dir + Dir(r) * 0.8f).Normalized; if (nd.Y < 0.25f) nd = new Vec3(nd.X, 0.35f, nd.Z).Normalized;
                Branch(b, r, to, nd, len * 0.7f, rad * 0.62f, left - 1, lr, lg, lb, berries);
            }
        }

        private static void Fungus(Body b, Genome g, Rng r)
        {
            b.GroundDisc(new Vec3(0, -0.55f, 0), 0.8f, 0.08f, 0.8f, 0.35f, 0.28f, 0.18f);
            int caps = 1 + Count(g.Branch, 4);
            for (int k = 0; k < caps; k++)
            {
                var at = k == 0 ? new Vec3(0, -0.5f, 0) : new Vec3(r.Range(-0.5f, 0.5f), -0.5f, r.Range(-0.5f, 0.5f));
                float h = (k == 0 ? 0.6f : 0.3f) + g.Size * 0.12f, w = (k == 0 ? 0.45f : 0.22f) + g.Size * 0.06f;
                b.Limb(at, at + new Vec3(0, h, 0), w * 0.18f, 0.92f, 0.88f, 0.78f);
                b.Blob(at + new Vec3(0, h, 0), w, w * 0.38f, w, g.R, g.G, g.B);
                if (g.Toxin > 0.4f)
                    for (int s = 0; s < 6; s++) { double a = r.Range(0, 6.28f); float rr = r.Range(0, w * 0.7f); b.Blob(at + new Vec3((float)Math.Cos(a) * rr, h + w * 0.3f, (float)Math.Sin(a) * rr), 0.06f, 0.04f, 0.06f, 0.97f, 0.95f, 0.9f); }
            }
            b.Radius = 1;
        }

        private static void Jelly(Body b, Genome g)
        {
            float s = 0.45f + g.Size * 0.08f;
            b.Blob(new Vec3(0, 0.25f, 0), s, s * 0.6f, s, g.R, g.G, g.B, 0.15f);
            int n = 4 + Count(g.Segs, 8);
            for (int k = 0; k < n; k++)
            {
                double a = k * Math.PI * 2 / n; Vec3 prev = new Vec3((float)Math.Cos(a) * s * 0.7f, 0.05f, (float)Math.Sin(a) * s * 0.7f);
                for (int j = 1; j <= 5; j++) { var p = prev + new Vec3((float)Math.Sin(j + a) * 0.05f, -0.16f, (float)Math.Cos(j + a) * 0.05f); b.Limb(prev, p, 0.035f, g.R * 0.9f, g.G * 0.9f, g.B * 0.9f, 0.1f); prev = p; }
            }
            b.Radius = 1.1f;
        }

        // an animal: a row of body segments with a head at the front, then whatever its genes give it:
        // legs (or arms, once it stands up), fins, wings, a neck and tail, eyes, armour, a hunter's jaws, warning colours
        /// <summary>A species' skin colour from its colour genes, kept bright enough to read in light: the darkest families come out deep, never black.</summary>
        public static void Skin(Genome g, out float r, out float gr, out float b)
        {
            r = g.R; gr = g.G; b = g.B;
            float mean = (r + gr + b) / 3;                                    // a little stronger colour: families read apart
            r = mean + (r - mean) * 1.6f; gr = mean + (gr - mean) * 1.6f; b = mean + (b - mean) * 1.6f;
            r = Math.Max(0.04f, r); gr = Math.Max(0.04f, gr); b = Math.Max(0.04f, b);
            float top = Math.Max(r, Math.Max(gr, b));
            if (top < 0.7f) { float k = 0.7f / top; r *= k; gr *= k; b *= k; }
            r = Math.Min(1, r); gr = Math.Min(1, gr); b = Math.Min(1, b);
        }

        private static void Animal(Body b, Genome g)
        {
            float cr, cg, cb; Skin(g, out cr, out cg, out cb);
            int segs = g.Stage >= 6 ? 2 + Count(g.Segs, 1) : 1 + Count(g.Segs, 6), legPairs = Count(g.Legs, 4), eyePairs = Math.Max(1, Count(g.Eyes, 4));
            bool swims = g.Fins > 0.5f && legPairs == 0;
            float legLen = legPairs > 0 ? 0.2f + g.LegLen * 0.45f : 0;
            float seg = 0.16f + g.Size * 0.03f;                              // how thick each segment is
            float stand = g.Stage >= 5 && legPairs >= 2 ? g.Upright : g.Upright * 0.2f;   // standing up comes with big brains, on at least two pairs of legs
            double tilt = stand * 1.3;                                       // 0 lying flat, up to about 75 degrees
            var dir = new Vec3((float)Math.Cos(tilt), (float)Math.Sin(tilt), 0);
            float groundY = -0.7f;
            float bodyY = swims ? 0 : legPairs == 0 ? groundY + seg * 0.9f : groundY + legLen + seg * 0.4f;   // legless ones lie on the ground
            if (!swims) b.GroundDisc(new Vec3(0, groundY - 0.04f, 0), 1.0f, 0.04f, 0.75f, 0.35f, 0.5f, 0.22f);    // ground
            float step = seg * 1.4f;
            var start = new Vec3(-(segs - 1) * step * 0.5f * (float)Math.Cos(tilt * 0.5), bodyY, 0);   // the body rises from its hips
            var pts = new Vec3[segs];
            for (int k = 0; k < segs; k++)
            {
                pts[k] = start + dir * (k * step);
                float sr = seg * (0.85f + 0.3f * (float)Math.Sin((k + 0.5) / segs * Math.PI));
                b.Limb(pts[k] - dir * (step * 0.45f), pts[k] + dir * (step * 0.45f), sr, cr, cg, cb);
                if (g.Armour > 0.4f) b.Cone(pts[k] + new Vec3(0, sr * 0.8f, 0), new Vec3(-0.3f, 1, 0), sr * g.Armour * 1.2f, sr * 0.35f, cr * 0.6f, cg * 0.6f, cb * 0.5f);   // plates and spikes
                if (g.Toxin > 0.5f) b.Blob(pts[k] + new Vec3(0, sr * 0.7f, sr * 0.45f), sr * 0.28f, sr * 0.2f, sr * 0.28f, 1f, 0.85f, 0.1f);                                 // warning spots
            }
            // neck and head
            var front = pts[segs - 1] + dir * (step * 0.5f);
            var neckDir = (dir + new Vec3(0, 0.6f, 0)).Normalized;
            var headAt = front + neckDir * (0.05f + g.Neck * 0.5f);
            if (g.Neck > 0.1f) b.Limb(front, headAt, seg * 0.5f, cr, cg, cb);
            float hs = seg * (0.8f + g.Head * 0.6f + g.Stage * 0.08f);       // bigger brains, bigger heads
            b.Blob(headAt + new Vec3(hs * 0.4f, 0, 0), hs * 1.1f, hs * 0.85f, hs * 0.85f, cr, cg, cb);
            for (int e = 0; e < eyePairs; e++)
                for (int side = -1; side <= 1; side += 2)
                    b.Blob(headAt + new Vec3(hs * (0.95f - e * 0.25f), hs * (0.35f + e * 0.12f), side * hs * 0.55f), hs * 0.17f, hs * 0.17f, hs * 0.17f, 0.05f, 0.05f, 0.06f);
            if (g.Hunt > 0.4f)                                               // a hunter's jaws
            {
                b.Cone(headAt + new Vec3(hs * 1.3f, -hs * 0.2f, hs * 0.2f), new Vec3(1, -0.3f, 0.2f), hs * 0.5f, hs * 0.14f, 0.95f, 0.95f, 0.9f);
                b.Cone(headAt + new Vec3(hs * 1.3f, -hs * 0.2f, -hs * 0.2f), new Vec3(1, -0.3f, -0.2f), hs * 0.5f, hs * 0.14f, 0.95f, 0.95f, 0.9f);
            }
            else b.Blob(headAt + new Vec3(hs * 1.3f, -hs * 0.15f, 0), hs * 0.4f, hs * 0.32f, hs * 0.38f, cr * 0.8f, cg * 0.75f, cb * 0.75f);   // snout
            // legs along the body; once it stands up, the front pair become arms
            for (int k = 0; k < legPairs; k++)
            {
                int at = legPairs == 1 ? 0 : (int)Math.Round(k * (segs - 1) / (double)(legPairs - 1));
                bool arm = stand > 0.5f && k == legPairs - 1 && legPairs > 1;
                for (int side = -1; side <= 1; side += 2)
                {
                    var hip = pts[at] + new Vec3(0, -seg * 0.3f, side * seg * 0.7f);
                    var foot = arm ? hip + new Vec3(legLen * 0.6f, -legLen * 0.5f, side * seg * 0.4f) : new Vec3(hip.X, groundY, hip.Z + side * seg * 0.3f);
                    var knee = (hip + foot) * 0.5f + new Vec3(arm ? 0 : legLen * 0.15f, 0, side * seg * 0.2f);
                    b.Limb(hip, knee, seg * 0.32f, cr * 0.9f, cg * 0.9f, cb * 0.9f); b.Limb(knee, foot, seg * 0.26f, cr * 0.9f, cg * 0.9f, cb * 0.9f);
                }
            }
            // intelligent creatures: something they made, a tool in the hand, and a band of woven cloth round the body
            if (g.Stage >= 6)
            {
                var hand = pts[segs - 1] + new Vec3(legLen * 0.6f, -legLen * 0.5f - seg * 0.3f, seg * 1.1f);
                b.Limb(hand + new Vec3(-0.05f, -0.25f, 0), hand + new Vec3(0.08f, 0.45f, 0), 0.022f, 0.55f, 0.4f, 0.25f);
                b.Cone(hand + new Vec3(0.08f, 0.45f, 0), new Vec3(0.15f, 1, 0), 0.12f, 0.04f, 0.75f, 0.75f, 0.78f);
                float sr = seg * 1.05f;
                b.Limb(pts[0] - dir * (step * 0.2f), pts[0] + dir * (step * 0.3f), sr, 1 - cr * 0.6f, 0.35f + cb * 0.4f, 0.3f + cg * 0.5f);
            }
            // fins for water, wings for air
            if (g.Fins > 0.3f)
            {
                b.Cone(pts[segs - 1] + new Vec3(0, seg * 0.8f, 0), new Vec3(-0.5f, 1, 0), seg * (0.6f + g.Fins), seg * 0.5f, cr * 0.85f, cg * 0.85f, cb);   // back fin
                for (int side = -1; side <= 1; side += 2) b.Box(pts[segs - 1] + new Vec3(0, -seg * 0.4f, side * seg * 1.2f), seg * 0.5f, seg * 0.06f, seg * 0.5f * (0.5f + g.Fins), cr * 0.85f, cg * 0.85f, cb, 0, side * 0.3f);
            }
            if (g.Wings > 0.5f)
                for (int side = -1; side <= 1; side += 2)
                {
                    // a wing: a thin, rounded flap from the shoulders, longer the stronger the gene
                    float span = seg * (1.2f + (g.Wings - 0.5f) * 5);
                    var root = pts[segs - 1] + new Vec3(-seg * 0.2f, seg * 0.5f, side * seg * 0.6f);
                    b.Parts.Add(new Part { Kind = 0, C = root + new Vec3(-seg * 0.3f, seg * 0.1f, side * span), U = new Vec3(1, 0, 0), V = new Vec3(0, 1, 0), W = new Vec3(0, 0, 1),
                        SU = seg * 0.9f, SV = seg * 0.06f, SW = span, R = Math.Min(1, cr + 0.2f), G = Math.Min(1, cg + 0.2f), B = Math.Min(1, cb + 0.2f) });
                }
            // tail
            if (g.Tail > 0.1f)
            {
                var back = pts[0] - dir * (step * 0.5f); var tip = back - dir * (0.1f + g.Tail * 0.7f) + new Vec3(0, swims ? 0 : -0.1f, 0);
                b.Limb(back, tip, seg * 0.35f, cr, cg, cb);
                if (swims) b.Box(tip, seg * 0.15f, seg * 1.1f, seg * 0.05f, cr * 0.85f, cg * 0.85f, cb);   // tail fin
            }
            // fit it to the view
            float extent = 0.5f;
            foreach (var p in b.Parts) extent = Math.Max(extent, Math.Max(Math.Abs(p.C.X), Math.Max(Math.Abs(p.C.Y), Math.Abs(p.C.Z))) + Math.Max(p.SU, Math.Max(p.SV, p.SW)));
            b.Radius = extent;
        }

        public static readonly string[] KindNames = { "viruses", "single cells", "plants", "fungi", "jellies (soft sea life)", "animals", "intelligent creatures" };

        /// <summary>A family of nine of one kind of life: a first species, then each one a descendant of the one before,
        /// many generations on. The resemblance shows, and so do the changes. Animals climb from nerves to big brains.</summary>
        public static Genome[] Family(ulong seed, int kind)
        {
            var r = new Rng(seed ^ (0xFA111EUL + (ulong)kind * 7919UL));
            var g = new Genome { Size = r.Range(0.8f, 1.6f), Speed = r.Range(0.3f, 0.7f), Graze = r.Range(0.3f, 0.8f), Hunt = r.Range(0, 0.6f), Coat = r.Range(0, 0.6f),
                R = r.Range(0.35f, 0.95f), G = r.Range(0.3f, 0.8f), B = r.Range(0.2f, 0.7f), Surface = r.Below(64), Shape = r.Below(3),
                Segs = r.Range(0.2f, 0.8f), Legs = r.Range(0.3f, 0.9f), LegLen = r.Range(0.2f, 0.8f), Neck = r.Range(0, 0.5f), Tail = r.Range(0.2f, 0.8f),
                Fins = r.Range(0, 0.7f), Wings = r.Range(0, 0.3f), Head = r.Range(0.2f, 0.6f), Eyes = r.Range(0.1f, 0.6f), Armour = r.Range(0, 0.6f),
                Upright = r.Range(0, 0.5f), Toxin = r.Range(0, 0.7f), Branch = r.Range(0.2f, 0.9f), Warm = 0.5f };
            switch (kind)
            {
                case 0: g.Stage = 0; break;
                case 1: g.Stage = 1; g.Photo = r.Range(0, 1f); break;
                case 2: g.Stage = 2; g.Photo = r.Range(0.6f, 1f); break;
                case 3: g.Stage = 2; g.Photo = 0.1f; g.Graze = r.Range(0.6f, 1f); g.Speed = r.Range(0, 0.2f); break;
                case 4: g.Stage = 2; g.Photo = 0.1f; g.Graze = 0.3f; g.Speed = r.Range(0.4f, 0.8f); break;
                case 5: g.Stage = 3; break;
                default: g.Stage = 6; g.Legs = r.Range(0.45f, 0.75f); g.Upright = r.Range(0.75f, 1f); g.Head = r.Range(0.6f, 1f); g.Fins = Math.Min(g.Fins, 0.2f); g.Wings = 0; break;
            }
            var fam = new Genome[9];
            for (int i = 0; i < 9; i++)
            {
                fam[i] = g;
                int st = g.Stage;
                for (int gen = 0; gen < 8; gen++) g = Genome.Mutate(g, r, 2f);
                if (kind == 1 && r.NextDouble() < 0.3) g.Shape = r.Below(3);
                if (kind == 2) g.Photo = Math.Max(0.55f, g.Photo);
                if (kind == 3) { g.Photo = 0.1f; g.Graze = Math.Max(0.55f, g.Graze); g.Speed = Math.Min(0.25f, g.Speed); }
                if (kind == 4) { g.Photo = 0.1f; g.Speed = Math.Max(0.35f, g.Speed); g.Graze = Math.Min(0.45f, g.Graze); }
                if (kind == 5) g.Stage = Math.Min(5, 3 + (i + 1) / 3); else g.Stage = st;
                if (kind == 6) { g.Upright = Math.Max(0.7f, g.Upright); g.Legs = Math.Max(0.4f, Math.Min(0.75f, g.Legs)); g.Wings = Math.Min(0.4f, g.Wings); g.Fins = Math.Min(0.25f, g.Fins); g.R = Math.Max(0.4f, g.R); g.G = Math.Max(0.3f, g.G); }
            }
            return fam;
        }
    }
}
