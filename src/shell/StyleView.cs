// Universe · shell · the style sample for living and built things: nine subjects (one from each kingdom of life, a village
// and a spaceship) all made by the body builder, shown in one of three looks. Keys 1, 2, 3: smooth, chunky, blocky.
// Drag: turn them. Wheel: closer and further. Space: stop or start the slow turning. Right-click a tile: mark it as a keeper.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;
using Universe.Sim;

namespace Universe.Shell
{
    internal sealed class StyleView : IDisposable
    {
        private readonly MeshPipes _pipes; private readonly IntPtr _device;
        private readonly Body[] _bodies = new Body[9];
        private readonly UploadBuffer[] _bufs = new UploadBuffer[9]; private readonly int[] _counts = new int[9];
        private int _look, _built = -1;
        private double _turn, _tilt = 0.35, _zoom = 1; private bool _still;
        private readonly bool[] _marks = new bool[9];
        // family mode: the nine are a line of descent, each built from its own genes; E evolves them all a generation, N starts a new family
        private readonly bool _family; private Genome[] _genes; private ulong _seed; private int _gens; private Rng _rng; private int _kind = 5; private readonly bool _civ;

        public StyleView(Gpu gpu, MeshPipes pipes, ulong seed, int look, bool family = false, bool civ = false)
        {
            _civ = civ; if (civ) { _kind = 6; family = true; }
            _device = gpu.Device; _pipes = pipes; _look = Math.Max(0, Math.Min(2, look)); _family = family; _seed = seed;
            if (family) { _look = 0; NewFamily(); }
            else for (int i = 0; i < 9; i++) _bodies[i] = BodyGen.Subject(i, seed);
        }

        private void NewFamily() { _genes = GeneBody.Family(_seed, _kind); _rng = new Rng(_seed + 99); _gens = 0; BodiesFromGenes(); }
        private void BodiesFromGenes()
        {
            for (int i = 0; i < 9; i++) _bodies[i] = _civ ? CivGen.Build(_genes[0], i, _seed) : GeneBody.FromGenome(_genes[i], _seed + (ulong)i);
            _built = -1;
        }

        // give the species the body plan of one way of building: 1 walls and pointed roofs, 2 domes, 3 mounds and hive spires (insect-like), 4 spiked forts (armoured), 5 stilts (water)
        private void ShapeFor(int style)
        {
            var g = _genes[0];
            g.Armour = 0.2f; g.Fins = 0.1f;
            switch (style)
            {
                case 0: g.Segs = 0.45f; g.Legs = 0.4f; break;
                case 1: g.Segs = 0.2f; g.Legs = 0.4f; break;
                case 2: g.Segs = 0.7f; g.Legs = 0.8f; break;
                case 3: g.Segs = 0.5f; g.Legs = 0.5f; g.Armour = 0.7f; break;
                default: g.Segs = 0.5f; g.Legs = 0.4f; g.Fins = 0.6f; break;
            }
            _genes[0] = g;
        }

        private static string Describe(Genome g)
        {
            if (g.Stage == 0) return "virus: " + (8 + g.Surface % 20) + " spikes, set by its surface gene";
            if (g.Stage == 1) return "single cell: " + new[] { "round", "rod", "spiral" }[g.Shape] + (g.Coat > 0.3f ? ", hairy coat" : "") + (g.Photo > 0.2f ? ", light-catchers" : "") + (g.Hunt > 0.4f ? ", a mouth" : "") + (g.Speed > 0.25f ? ", a tail to swim with" : "");
            if (g.Stage == 2) return (g.Photo > 0.5f ? "plant" : g.Graze > 0.5f && g.Speed < 0.3f ? "fungus" : "jelly") + ": branching " + g.Branch.ToString("0.00") + (g.Toxin > 0.4f ? ", poisonous" : "");
            int segs = 1 + (int)Math.Round(g.Segs * 6), legs = (int)Math.Round(g.Legs * 4) * 2;
            var parts = new List<string>();
            parts.Add(segs + " segment" + (segs == 1 ? "" : "s")); parts.Add(legs + " legs");
            if (g.Fins > 0.3f) parts.Add("fins"); if (g.Wings > 0.3f) parts.Add("wings"); if (g.Neck > 0.1f) parts.Add("a neck"); if (g.Tail > 0.1f) parts.Add("a tail");
            if (g.Armour > 0.4f) parts.Add("armour"); if (g.Toxin > 0.5f) parts.Add("poison (warning spots)"); if (g.Hunt > 0.4f) parts.Add("hunter's jaws");
            if (g.Stage >= 5 && g.Upright > 0.5f) parts.Add("stands upright");
            return PlanetLife.StageNames[g.Stage] + ": " + string.Join(", ", parts.ToArray());
        }

        public string Title
        {
            get
            {
                var kept = new List<string>(); for (int i = 0; i < 9; i++) if (_marks[i]) kept.Add((i + 1).ToString());
                if (_civ)
                    return Universe.Shell.PreviewText.AppName + " samples · What an intelligent species builds, from its own genes · builds in " + CivGen.StyleNames[CivGen.Style(_genes[0])] + " · " + (_gens == 0 ? "as first made" : _gens + " generations evolved") +
                           " · keepers: " + (kept.Count == 0 ? "none" : string.Join(", ", kept.ToArray())) +
                           " · 1-5: way of building · E: evolve the species · N: new species · drag: turn · wheel: zoom · Space: stop turning · right-click: mark keeper · Legend";
                if (_family)
                    return Universe.Shell.PreviewText.AppName + " samples · A family of " + GeneBody.KindNames[_kind] + ", built from their genes · " + (_gens == 0 ? "as first made" : _gens + " generations evolved") +
                           " · keepers: " + (kept.Count == 0 ? "none" : string.Join(", ", kept.ToArray())) +
                           " · 1-7: kind of life · E: evolve a generation · N: new family · drag: turn · wheel: zoom · Space: stop turning · right-click: mark keeper · Legend";
                return Universe.Shell.PreviewText.AppName + " samples · Style of living and built things · look " + (_look + 1) + " of 3: " + BodyGen.LookNames[_look] +
                       " · keepers: " + (kept.Count == 0 ? "none" : string.Join(", ", kept.ToArray())) +
                       " · 1 2 3: change look · drag: turn · wheel: zoom · Space: stop turning · right-click: mark keeper · Legend";
            }
        }

        public string Legend()
        {
            if (_civ)
            {
                var cb = new System.Text.StringBuilder("What an intelligent species builds, from its own genes\n\n");
                cb.Append("One species, and everything it builds as it climbs from a tribe to the stars. Nothing here is a fixed model: the builders' body plan sets their way of building, and their colours run through all of it.\n\n");
                cb.Append("Ways of building: insect-like species (many legs, many segments) raise mounds and hive spires; armoured ones build spiked forts; ones at home in water build on stilts; round ones with few segments build domes; the rest build walls with pointed roofs. Their ships follow their bodies: hull segments like their own, their wings or fins, spikes if they are armoured.\n\n");
                cb.Append("This species: " + Describe(_genes[0]) + ". It builds in " + CivGen.StyleNames[CivGen.Style(_genes[0])] + ".\n\nTiles:\n");
                for (int i = 0; i < 9; i++) cb.Append("  " + (i + 1) + ": " + CivGen.Names[i] + (_marks[i] ? "   (keeper)" : "") + "\n");
                cb.Append("\nPress E to evolve the species a generation: as its body changes, so does everything it builds. Press N for a new species.\n\nControls:\n  E: evolve · N: new species\n  Drag: turn · Mouse wheel: closer and further\n  Space: stop or start the slow turning\n  Right-click a tile, or the Keepers menu: mark it\n  Exit menu or Esc: close");
                return cb.ToString();
            }
            if (_family)
            {
                var fb = new System.Text.StringBuilder("A family of " + GeneBody.KindNames[_kind] + ", built from their genes\n\n");
                fb.Append("Kinds of life (keys 1 to 7): "); for (int k = 0; k < 7; k++) fb.Append((k + 1) + " " + GeneBody.KindNames[k] + (k < 6 ? ", " : "\n\n"));
                fb.Append("Nothing here is a fixed model. Each animal's body is built from its genes: how many body segments, legs, eyes, fins and wings it has, the length of its legs, neck and tail, its armour and poison, and whether it hunts. Its size and colour come from its genes too.\n\n");
                fb.Append("Each tile is a descendant of the tile before it, eight generations later, so the family resemblance shows. Along the way the line climbs from nerves to brains to big brains, and big-brained animals can stand up, turning their front legs into arms.\n\n");
                fb.Append("Press E to evolve all of them one more generation and watch them change. Press N for a whole new family.\n\n");
                for (int i = 0; i < 9; i++) fb.Append("  " + (i + 1) + ": " + Describe(_genes[i]) + (_marks[i] ? "   (keeper)" : "") + "\n");
                fb.Append("\nControls:\n  E: evolve a generation · N: new family\n  Drag: turn them · Mouse wheel: closer and further\n  Space: stop or start the slow turning\n  Right-click a tile, or the Keepers menu: mark it\n  Exit menu or Esc: close");
                return fb.ToString();
            }
            var sb = new System.Text.StringBuilder("Style sample: how living and built things are drawn\n\n");
            sb.Append("The same nine subjects, all built from their own seed by the body builder, in one of three looks:\n");
            for (int k = 0; k < 3; k++) sb.Append("  " + (k + 1) + ": " + BodyGen.LookNames[k] + (k == _look ? "   <- showing now" : "") + "\n");
            sb.Append("\nTiles, left to right, top to bottom:\n");
            for (int i = 0; i < 9; i++) sb.Append("  " + (i + 1) + ": " + BodyGen.SubjectNames[i] + (_marks[i] ? "   (keeper)" : "") + "\n");
            sb.Append("\nControls:\n  1, 2, 3: change the look\n  Drag: turn them · Mouse wheel: closer and further\n  Space: stop or start the slow turning\n  Right-click a tile, or the Keepers menu: mark it\n  Exit menu or Esc: close");
            return sb.ToString();
        }

        public void ToggleMark(int i) { if (i >= 0 && i < 9) _marks[i] = !_marks[i]; }
        public bool[] Marks { get { return _marks; } }

        public void Update(Input input, double dt, int width, int height)
        {
            if (!_family) for (int k = 0; k < 3; k++) if (input.WasPressed(0x31 + k)) _look = k;
            if (_family && !_civ) for (int k = 0; k < 7; k++) if (input.WasPressed(0x31 + k)) { _kind = k; NewFamily(); }
            if (_civ) for (int k = 0; k < 5; k++) if (input.WasPressed(0x31 + k)) { ShapeFor(k); BodiesFromGenes(); }
            if (_family && input.WasPressed(0x45)) { for (int i = 0; i < 9; i++) _genes[i] = Genome.Mutate(_genes[i], _rng, 3f); _gens++; BodiesFromGenes(); }
            if (_family && input.WasPressed(0x4E)) { _seed = _seed * 6364136223846793005UL + 1442695040888963407UL; NewFamily(); }
            if (input.WasPressed(Win32.VK_SPACE)) _still = !_still;
            if (input.Wheel != 0) _zoom = Math.Max(0.3, Math.Min(3, _zoom * Math.Pow(0.87, input.Wheel)));
            if (input.LeftDown) { _turn += input.MouseDX * 0.01; _tilt = Math.Max(-1.2, Math.Min(1.4, _tilt + input.MouseDY * 0.01)); }
            if (!_still) _turn += dt * 0.35;
            if (input.RightClicked)
            {
                int col = Math.Max(0, Math.Min(2, input.MouseX * 3 / Math.Max(1, width))), row = Math.Max(0, Math.Min(2, input.MouseY * 3 / Math.Max(1, height)));
                ToggleMark(row * 3 + col);
            }
        }

        private void Rebuild()
        {
            if (_built == _look) return;
            _built = _look;
            int st = Marshal.SizeOf(typeof(StarVertex));
            for (int i = 0; i < 9; i++)
            {
                var v = BodyGen.Build(_bodies[i], _look);
                if (_bufs[i] != null) _bufs[i].Dispose();
                _counts[i] = v.Length; _bufs[i] = new UploadBuffer(_device, (long)Math.Max(1, v.Length) * st); _bufs[i].Write(v, v.Length);
            }
        }

        public void Draw(Gpu gpu, int width, int height, double time)
        {
            Rebuild();
            gpu.ClearNow(0.16f, 0.2f, 0.26f);
            int tw = width / 3, th = height / 3;
            var light = new Vec3(-0.45f, 0.75f, -0.5f).Normalized;
            for (int i = 0; i < 9; i++)
            {
                int x = (i % 3) * tw, y = (i / 3) * th;
                // look at the middle of the body, from far enough to see all of it
                Vec3 lo = new Vec3(1e9f, 1e9f, 1e9f), hi = new Vec3(-1e9f, -1e9f, -1e9f);
                foreach (var p in _bodies[i].Parts) { float e = Math.Max(p.SU, Math.Max(p.SV, p.SW)); lo = new Vec3(Math.Min(lo.X, p.C.X - e), Math.Min(lo.Y, p.C.Y - e), Math.Min(lo.Z, p.C.Z - e)); hi = new Vec3(Math.Max(hi.X, p.C.X + e), Math.Max(hi.Y, p.C.Y + e), Math.Max(hi.Z, p.C.Z + e)); }
                var mid = (lo + hi) * 0.5f; float half = (hi - lo).Length * 0.5f;
                float d = Math.Max(0.5f, half) * 1.45f * (float)_zoom;
                var eye = mid + new Vec3((float)(Math.Sin(_turn) * Math.Cos(_tilt)) * d, (float)Math.Sin(_tilt) * d, (float)(-Math.Cos(_turn) * Math.Cos(_tilt)) * d);
                var vp = Mat4.LookAt(eye, mid, new Vec3(0, 1, 0)) * Mat4.Perspective(0.8f, tw / (float)Math.Max(1, th), d * 0.05f, d * 4);
                var c = new float[BillboardPipeline.ConstantCount];
                c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
                c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
                c[16] = light.X; c[17] = light.Y; c[18] = light.Z; c[20] = eye.X; c[21] = eye.Y; c[22] = eye.Z;
                _pipes.Solid.Draw(_bufs[i], _counts[i], c, x + 2, y + 2, tw - 4, th - 4);
            }
        }

        public void Dispose() { foreach (var b in _bufs) if (b != null) b.Dispose(); }
    }
}
