// Universe · shell · sample set 7: the periodic table, each element a game icon built from solid shapes.
// A tile with the element's symbol and number, and on it a model in its real state at the chosen temperature and pressure:
// solids a faceted lump in their real colour (metals shine), liquids a drop, gases glowing inside a glass bulb,
// lab-made elements a small glowing core. Up and Down arrows: temperature. Left and Right: pressure. Home: room conditions.
// Mouse wheel: closer and further. Drag: move across the table. Click a tile: look closer (details in the title and Legend).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class ElementView : IDisposable
    {
        private readonly List<ElementSample> _els;
        private readonly MeshPipes _pipes;
        private UploadBuffer _solidBuf, _glassBuf; private int _solidCount, _glassCount;
        private readonly IntPtr _device;
        private double _tempK = 293.15, _atm = 1;
        private string _builtFor = "";
        private double _camX = 4.25, _camY = -2.6, _dist = 7.2;
        private int _picked = -1, _hover = -1;
        private Mat4 _vp;
        private int _w = 1, _h = 1;
        private double _age;      // a click that opened or focused the window is not a pick
        private const float Tile = 0.5f;            // spacing between tiles

        public ElementView(Gpu gpu, MeshPipes pipes, ContentLibrary content)
        {
            foreach (var fe in content.OfKind("elements.unt", "family"))
            {
                var w = fe.Get("colour", "").Split(' '); float r, g, b; var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (w.Length == 3 && float.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out r) && float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out g) && float.TryParse(w[2], System.Globalization.NumberStyles.Float, ci, out b)) Family[fe.Name] = new[] { r, g, b };
            }
            _device = gpu.Device; _pipes = pipes;
            _els = ElementGen.Load(content);
            foreach (var e in _els) e.Centre = new Vec3((e.Col - 1) * Tile, -(e.Row - 1) * Tile - (e.Row >= 8 ? 0.3f : 0), 0);
        }

        private static string C(double k) { return (k - 273.15).ToString("0") + " °C"; }

        public string Title
        {
            get
            {
                string t = Universe.Shell.PreviewText.AppName + " samples · Set 7: Elements · " + _tempK.ToString("0") + " K (" + C(_tempK) + ") · pressure " + _atm.ToString(_atm < 0.01 ? "0.0000" : "0.00") + " atm";
                int show = _hover >= 0 ? _hover : _picked;
                if (show >= 0) { var e = _els[show]; t += " · " + e.Number + " " + e.Name + " (" + e.Symbol + "), " + e.Group + ", " + e.StateAt(_tempK, _atm) + " here"; }
                return t + " · Up/Down: temperature · Left/Right: pressure · Home: room · wheel: zoom · drag: move · click: look closer · Legend for details";
            }
        }

        public string Legend()
        {
            var sb = new System.Text.StringBuilder("Set 7 of 17: the elements, all 118, as game icons\n\n");
            sb.Append("Now: " + _tempK.ToString("0") + " K (" + C(_tempK) + "), pressure " + _atm.ToString("0.####") + " atmospheres.\n");
            sb.Append("Solid: a lump in its real colour (metals shine). Liquid: a drop. Gas: glowing in a glass bulb. Lab-made: a small glowing core, it falls apart almost at once. The yellow and black radiation sign: radioactive.\n");
            sb.Append("Tile edge colours show the family: alkali metals red, alkaline earths orange, transition metals blue-grey, post-transition grey, metalloids teal, nonmetals green, halogens yellow, noble gases violet, lanthanides pink, actinides magenta, never measured dark grey.\n\n");
            if (_picked >= 0)
            {
                var e = _els[_picked];
                sb.Append(e.Number + "  " + e.Name + " (" + e.Symbol + ")\n  Family: " + e.Group + "\n  Here and now: " + e.StateAt(_tempK, _atm) + "\n");
                sb.Append("  Melts at: " + (e.MeltK < 0 ? "never measured" : e.MeltK.ToString("0") + " K (" + C(e.MeltK) + ")") + "\n");
                sb.Append("  Boils at (1 atm): " + (e.BoilK < 0 ? "never measured" : e.BoilK.ToString("0") + " K (" + C(e.BoilK) + ")") + (e.BoilK == e.MeltK && e.BoilK > 0 ? " (turns straight to gas)" : "") + "\n");
                sb.Append("  Made: " + e.Origin + "\n  Found: " + e.FoundIn + "\n  In the game: " + e.Use + "\n\n");
            }
            else sb.Append("Click a tile to see its details here.\n\n");
            sb.Append("Controls:\n  Up / Down arrows: hotter / colder\n  Left / Right arrows: lower / higher pressure\n  Home: back to room temperature and normal air pressure\n  Mouse wheel: closer and further · drag: move across the table\n  Click a tile: look closer\n  Exit menu or Esc: close");
            return sb.ToString();
        }

        public void Update(Input input, double dt)
        {
            if (input.IsDown(Win32.VK_UP)) _tempK = Math.Min(7000, _tempK * Math.Pow(1.8, dt));
            if (input.IsDown(Win32.VK_DOWN)) _tempK = Math.Max(1, _tempK / Math.Pow(1.8, dt));
            if (input.IsDown(Win32.VK_RIGHT)) _atm = Math.Min(10000, _atm * Math.Pow(4, dt));
            if (input.IsDown(Win32.VK_LEFT)) _atm = Math.Max(1e-6, _atm / Math.Pow(4, dt));
            if (input.WasPressed(0x24)) { _tempK = 293.15; _atm = 1; }
            if (input.Wheel != 0) _dist = Math.Max(0.5, Math.Min(14, _dist * Math.Pow(0.85, input.Wheel)));
            if (input.LeftDown && (input.MouseDX != 0 || input.MouseDY != 0)) { _camX -= input.MouseDX * _dist * 0.0011; _camY += input.MouseDY * _dist * 0.0011; }
            _hover = Pick(input.MouseX, input.MouseY);
            _age += dt;
            if (input.LeftClicked && _hover >= 0 && _age > 0.6) { _picked = _hover; _camX = _els[_picked].Centre.X; _camY = _els[_picked].Centre.Y; _dist = 1.2; }
        }

        /// <summary>Test runs: hotter or colder, and higher or lower pressure, by a factor.</summary>
        public void SetConditions(double kelvin, double atm) { _tempK = kelvin; _atm = atm; }

        private int Pick(int mx, int my)
        {
            float x = mx / (float)Math.Max(1, _w) * 2 - 1, y = 1 - my / (float)Math.Max(1, _h) * 2;
            int best = -1; float bd = 0.06f;
            for (int i = 0; i < _els.Count; i++)
            {
                var c = _els[i].Centre;
                float cx = c.X * _vp.M11 + c.Y * _vp.M21 + c.Z * _vp.M31 + _vp.M41, cy = c.X * _vp.M12 + c.Y * _vp.M22 + c.Z * _vp.M32 + _vp.M42, cw = c.X * _vp.M14 + c.Y * _vp.M24 + c.Z * _vp.M34 + _vp.M44;
                if (cw <= 0) continue;
                float d = (float)Math.Sqrt((cx / cw - x) * (cx / cw - x) + (cy / cw - y) * (cy / cw - y));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ── building the icons from triangles ──
        private List<StarVertex> _s, _g;

        private void Tri(List<StarVertex> l, Vec3 a, Vec3 b, Vec3 c, float r, float g, float bl, float a1, float glow)
        {
            l.Add(V(a, r, g, bl, a1, glow)); l.Add(V(b, r, g, bl, a1, glow)); l.Add(V(c, r, g, bl, a1, glow));
        }
        private static StarVertex V(Vec3 p, float r, float g, float b, float a, float glow) { return new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = glow, R = r, G = g, B = b, A = a }; }

        private void Box(List<StarVertex> l, Vec3 lo, Vec3 hi, float r, float g, float b, float glow)
        {
            Vec3 p000 = lo, p100 = new Vec3(hi.X, lo.Y, lo.Z), p010 = new Vec3(lo.X, hi.Y, lo.Z), p110 = new Vec3(hi.X, hi.Y, lo.Z);
            Vec3 p001 = new Vec3(lo.X, lo.Y, hi.Z), p101 = new Vec3(hi.X, lo.Y, hi.Z), p011 = new Vec3(lo.X, hi.Y, hi.Z), p111 = hi;
            Quad(l, p000, p100, p110, p010, r, g, b, glow); Quad(l, p101, p001, p011, p111, r, g, b, glow);
            Quad(l, p001, p000, p010, p011, r, g, b, glow); Quad(l, p100, p101, p111, p110, r, g, b, glow);
            Quad(l, p010, p110, p111, p011, r, g, b, glow); Quad(l, p001, p101, p100, p000, r, g, b, glow);
        }
        private void Quad(List<StarVertex> l, Vec3 a, Vec3 b, Vec3 c, Vec3 d, float r, float g, float bl, float glow)
        {
            Tri(l, a, b, c, r, g, bl, 1, glow); Tri(l, a, c, d, r, g, bl, 1, glow);
        }

        // a ball made by splitting an icosahedron's faces; shape() pushes each point in or out
        private static readonly float T = 1.618034f;
        private static readonly Vec3[] IcoV = { new Vec3(-1, T, 0), new Vec3(1, T, 0), new Vec3(-1, -T, 0), new Vec3(1, -T, 0), new Vec3(0, -1, T), new Vec3(0, 1, T), new Vec3(0, -1, -T), new Vec3(0, 1, -T), new Vec3(T, 0, -1), new Vec3(T, 0, 1), new Vec3(-T, 0, -1), new Vec3(-T, 0, 1) };
        private static readonly int[] IcoF = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

        private void Ball(List<StarVertex> l, Vec3 centre, float radius, int split, Func<Vec3, Vec3> shape, float r, float g, float b, float a, float glow, bool spots, int seed)
        {
            for (int f = 0; f < 20; f++)
            {
                Vec3 A = IcoV[IcoF[f * 3]].Normalized, B = IcoV[IcoF[f * 3 + 1]].Normalized, Cc = IcoV[IcoF[f * 3 + 2]].Normalized;
                for (int i = 0; i < split; i++)
                    for (int j = 0; j < split - i; j++)
                    {
                        Vec3 p0 = Lerp(A, B, Cc, i, j, split), p1 = Lerp(A, B, Cc, i + 1, j, split), p2 = Lerp(A, B, Cc, i, j + 1, split);
                        Emit(l, centre, radius, shape, p0, p1, p2, r, g, b, a, glow, spots, seed);
                        if (j < split - i - 1)
                        {
                            Vec3 p3 = Lerp(A, B, Cc, i + 1, j + 1, split);
                            Emit(l, centre, radius, shape, p1, p3, p2, r, g, b, a, glow, spots, seed);
                        }
                    }
            }
        }
        private static Vec3 Lerp(Vec3 A, Vec3 B, Vec3 C, int i, int j, int n)
        {
            float u = i / (float)n, v = j / (float)n;
            return (A * (1 - u - v) + B * u + C * v).Normalized;
        }
        private void Emit(List<StarVertex> l, Vec3 c, float rad, Func<Vec3, Vec3> shape, Vec3 a, Vec3 b, Vec3 d, float r, float g, float bl, float al, float glow, bool spots, int seed)
        {
            float k = 1;
            if (spots) { var m = (a + b + d) * (1f / 3); k = 0.85f + 0.3f * Noise.Fractal(m.X * 4 + seed, m.Y * 4, m.Z * 4, (uint)seed, 2); }
            Tri(l, c + shape(a) * rad, c + shape(b) * rad, c + shape(d) * rad, r * k, g * k, bl * k, al, glow);
        }

        // each family's colour on the table (content/elements.unt @family)
        private readonly Dictionary<string, float[]> Family = new Dictionary<string, float[]>();

        private void Text(List<StarVertex> l, string s, Vec3 topLeft, float cell, float r, float g, float b)
        {
            for (int c = 0; c < s.Length; c++)
                foreach (var px in IconFont.Cells(s[c]))
                {
                    var lo = topLeft + new Vec3((c * 6 + px[0]) * cell, -(px[1] + 1) * cell, -cell * 0.6f);
                    Box(l, lo, lo + new Vec3(cell, cell, cell * 0.6f), r, g, b, 0.9f);
                }
        }


        // the radiation warning sign: a yellow disc with three black blades around a black centre
        private void RadSign(Vec3 c, float r)
        {
            const int Seg = 36;
            for (int i = 0; i < Seg; i++)
            {
                double a0 = i * Math.PI * 2 / Seg, a1 = (i + 1) * Math.PI * 2 / Seg;
                var p0 = new Vec3((float)Math.Cos(a0), (float)Math.Sin(a0), 0); var p1 = new Vec3((float)Math.Cos(a1), (float)Math.Sin(a1), 0);
                Tri(_s, c, c + p0 * r, c + p1 * r, 1f, 0.82f, 0.1f, 1, 0.9f);                          // yellow disc
                double mid = (a0 + a1) / 2 - Math.PI / 2;
                double deg = ((mid * 180 / Math.PI) % 120 + 120) % 120;
                if (deg < 60)                                                                         // three blades, 60 degrees wide, 60 apart
                {
                    var q = new Vec3(0, 0, -0.002f);
                    Tri(_s, c + q + p0 * (r * 0.3f), c + q + p0 * (r * 0.85f), c + q + p1 * (r * 0.85f), 0.05f, 0.05f, 0.05f, 1, 0.9f);
                    Tri(_s, c + q + p0 * (r * 0.3f), c + q + p1 * (r * 0.85f), c + q + p1 * (r * 0.3f), 0.05f, 0.05f, 0.05f, 1, 0.9f);
                }
                Tri(_s, c + new Vec3(0, 0, -0.002f), c + new Vec3(0, 0, -0.002f) + p0 * (r * 0.18f), c + new Vec3(0, 0, -0.002f) + p1 * (r * 0.18f), 0.05f, 0.05f, 0.05f, 1, 0.9f);   // centre
            }
        }

        /// <summary>How a solid element is kept: metals used as metal are bars, minerals are crystals, the rest are powders.</summary>
        private static string Form(ElementSample e)
        {
            if (e.Group == "Transition" || e.Group == "Post-transition" || e.Group == "Lanthanide" || e.Group == "Actinide") return "bars";
            if (e.Group == "Metalloid" || e.Symbol == "I" || e.Symbol == "C") return "crystals";
            return "powder";                                                         // alkali and alkaline-earth metals, sulfur, phosphorus, selenium
        }

        // a stack of metal bars: three, two, one; each bar wider at the bottom, its top lit like polished metal
        private void Bars(Vec3 at, ElementSample e)
        {
            float bl = 0.075f, bw = 0.032f, bh = 0.028f;
            int[] rows = { 3, 2, 1 };
            for (int layer = 0; layer < 3; layer++)
                for (int k = 0; k < rows[layer]; k++)
                {
                    float x = (k - (rows[layer] - 1) * 0.5f) * (bw * 2.15f);
                    var c = at + new Vec3(x, -0.1f + layer * bh * 1.05f, 0);
                    Ingot(c, bl, bw, bh, e.R, e.G, e.B, e.Shiny ? 0.18f : 0.05f);
                }
        }

        private void Ingot(Vec3 c, float len, float wid, float hgt, float r, float g, float b, float glow)
        {
            float tw = wid * 0.72f, tl = len * 0.86f;                                  // the top is narrower and shorter than the base
            Vec3 b00 = c + new Vec3(-wid, 0, -len), b10 = c + new Vec3(wid, 0, -len), b11 = c + new Vec3(wid, 0, len), b01 = c + new Vec3(-wid, 0, len);
            Vec3 t00 = c + new Vec3(-tw, hgt, -tl), t10 = c + new Vec3(tw, hgt, -tl), t11 = c + new Vec3(tw, hgt, tl), t01 = c + new Vec3(-tw, hgt, tl);
            Quad(_s, t00, t10, t11, t01, r * 1.1f, g * 1.1f, b * 1.1f, glow + 0.1f);     // top face, brightest
            Quad(_s, b00, b10, t10, t00, r, g, b, glow); Quad(_s, b10, b11, t11, t10, r * 0.85f, g * 0.85f, b * 0.85f, glow);
            Quad(_s, b11, b01, t01, t11, r, g, b, glow); Quad(_s, b01, b00, t00, t01, r * 0.85f, g * 0.85f, b * 0.85f, glow);
        }

        // a cluster of pointed six-sided crystals growing out of a small base
        private void Crystals(Vec3 at, ElementSample e)
        {
            var rng = new Rng((ulong)e.Number * 31UL);
            var basePt = at + new Vec3(0, -0.1f, 0);
            for (int k = 0; k < 6; k++)
            {
                float lean = rng.Range(-0.6f, 0.6f), turn = rng.Range(0, 6.28f), len = rng.Range(0.09f, 0.17f), rad = rng.Range(0.014f, 0.024f);
                var dir = new Vec3((float)Math.Sin(lean) * (float)Math.Cos(turn), (float)Math.Cos(lean), (float)Math.Sin(lean) * (float)Math.Sin(turn)).Normalized;
                Prism(basePt + new Vec3(rng.Range(-0.03f, 0.03f), 0, rng.Range(-0.03f, 0.03f)), dir, len, rad, e.R, e.G, e.B, 0.15f);
            }
        }

        private void Prism(Vec3 b, Vec3 dir, float len, float rad, float r, float g, float bl, float glow)
        {
            var side = Vec3.Cross(dir, Math.Abs(dir.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized;
            var side2 = Vec3.Cross(dir, side);
            var top = b + dir * len; var tip = top + dir * (rad * 2.2f);
            for (int i = 0; i < 6; i++)
            {
                double a0 = i * Math.PI / 3, a1 = (i + 1) * Math.PI / 3;
                var o0 = (side * (float)Math.Cos(a0) + side2 * (float)Math.Sin(a0)) * rad;
                var o1 = (side * (float)Math.Cos(a1) + side2 * (float)Math.Sin(a1)) * rad;
                float k = 0.8f + 0.25f * (i % 2);
                Quad(_s, b + o0, b + o1, top + o1, top + o0, r * k, g * k, bl * k, glow);
                Tri(_s, top + o0, top + o1, tip, r * 1.1f, g * 1.1f, bl * 1.1f, 1, glow + 0.1f);
            }
        }

        // a heap of powder: a low bumpy cone
        private void Powder(Vec3 at, ElementSample e)
        {
            int seed = e.Number * 17;
            Ball(_s, at + new Vec3(0, -0.11f, 0), 0.12f, 3, d =>
            {
                double rr = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                float bump = 0.9f + 0.2f * Noise.Fractal(d.X * 6 + seed, d.Y * 6, d.Z * 6, (uint)seed, 2);
                float y = d.Y > 0 ? (float)(1 - rr) * 0.75f * bump : 0;
                return new Vec3(d.X * bump, y, d.Z * bump);
            }, e.R, e.G, e.B, 1, 0.05f, true, seed);
        }

        // a drop of liquid: round below, coming to a point above
        private void Drop(Vec3 at, ElementSample e)
        {
            Ball(_s, at + new Vec3(0, -0.04f, 0), 0.075f, 3, d =>
            {
                if (d.Y <= 0) return d;
                float k = (float)Math.Pow(1 - d.Y, 1.1);                                // narrows to the tip
                return new Vec3(d.X * k / Math.Max(0.001f, (float)Math.Sqrt(1 - d.Y * d.Y)) * (float)Math.Sqrt(1 - d.Y * d.Y), d.Y * 1.7f, d.Z * k / Math.Max(0.001f, (float)Math.Sqrt(1 - d.Y * d.Y)) * (float)Math.Sqrt(1 - d.Y * d.Y));
            }, e.R, e.G, e.B, 1, e.Shiny ? 0.2f : 0.08f, false, 0);
        }

        // a cloud of gas: soft puffs in the gas's own colour, glowing faintly
        private void Cloud(Vec3 at, ElementSample e)
        {
            var rng = new Rng((ulong)e.Number * 57UL);
            for (int k = 0; k < 7; k++)
            {
                var off = new Vec3(rng.Range(-0.07f, 0.07f), rng.Range(-0.035f, 0.04f), rng.Range(-0.04f, 0.04f));
                float rad = rng.Range(0.04f, 0.065f);
                Ball(_g, at + off, rad, 2, d => d, e.R, e.G, e.B, 0.55f, 0.6f, false, 0);
            }
        }

        private void Rebuild()
        {
            var key = new System.Text.StringBuilder();
            foreach (var e in _els) key.Append(e.StateAt(_tempK, _atm)[0]);
            if (key.ToString() == _builtFor) return;
            _builtFor = key.ToString();
            _s = new List<StarVertex>(); _g = new List<StarVertex>();
            foreach (var e in _els)
            {
                var c = e.Centre; float half = Tile * 0.46f;
                // the tile: a dark plate with a family-coloured edge
                float[] fam; if (!Family.TryGetValue(e.Group, out fam)) fam = new[] { 0.3f, 0.3f, 0.32f };
                Box(_s, c + new Vec3(-half, -half, 0.0f), c + new Vec3(half, half, 0.03f), 0.08f, 0.085f, 0.1f, 0.25f);
                float rim = 0.022f;
                Box(_s, c + new Vec3(-half, half - rim, -0.008f), c + new Vec3(half, half, 0.0f), fam[0], fam[1], fam[2], 0.7f);
                Box(_s, c + new Vec3(-half, -half, -0.008f), c + new Vec3(half, -half + rim, 0.0f), fam[0], fam[1], fam[2], 0.7f);
                Box(_s, c + new Vec3(-half, -half, -0.008f), c + new Vec3(-half + rim, half, 0.0f), fam[0], fam[1], fam[2], 0.7f);
                Box(_s, c + new Vec3(half - rim, -half, -0.008f), c + new Vec3(half, half, 0.0f), fam[0], fam[1], fam[2], 0.7f);
                if (e.Radioactive) RadSign(c + new Vec3(half - 0.07f, -half + 0.07f, -0.012f), 0.045f);
                // symbol (large, top left) and number (small, top right)
                Text(_s, e.Symbol, c + new Vec3(-half + 0.035f, half - 0.04f, 0), 0.011f, 0.95f, 0.95f, 0.95f);
                string num = e.Number.ToString();
                Text(_s, num, c + new Vec3(half - 0.035f - num.Length * 6 * 0.006f, half - 0.04f, 0), 0.006f, 0.7f, 0.72f, 0.75f);
                // the sample, sitting on the lower part of the tile
                var at = c + new Vec3(0, 0.02f, -0.13f);   // the sample stands in the middle of its tile
                string state = e.StateAt(_tempK, _atm);
                if (state == "solid")
                {
                    string form = Form(e);
                    if (form == "bars") Bars(at, e);
                    else if (form == "crystals") Crystals(at, e);
                    else Powder(at, e);
                }
                else if (state == "liquid") Drop(at, e);
                else if (state == "gas") Cloud(at, e);
                else
                    Ball(_s, at, 0.04f, 1, d => d, 0.55f, 0.95f, 0.75f, 1, 1f, false, 0);                // lab-made: a small glowing core
            }
            if (_solidBuf != null) _solidBuf.Dispose(); if (_glassBuf != null) _glassBuf.Dispose();
            int st = Marshal.SizeOf(typeof(StarVertex));
            _solidCount = _s.Count; _solidBuf = new UploadBuffer(_device, (long)Math.Max(1, _solidCount) * st); _solidBuf.Write(_s.ToArray(), _solidCount);
            _glassCount = _g.Count; _glassBuf = new UploadBuffer(_device, (long)Math.Max(1, _glassCount) * st); _glassBuf.Write(_g.ToArray(), _glassCount);
        }

        public void Draw(Gpu gpu, int width, int height, double time)
        {
            _w = width; _h = height;
            Rebuild();
            var eye = new Vec3((float)_camX, (float)_camY + (float)_dist * 0.22f, (float)-_dist);
            Mat4 view = Mat4.LookAt(eye, new Vec3((float)_camX, (float)_camY, 0), new Vec3(0, 1, 0));
            _vp = view * Mat4.Perspective(0.9f, width / (float)Math.Max(1, height), 0.02f, 60f);
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = _vp.M11; c[1] = _vp.M12; c[2] = _vp.M13; c[3] = _vp.M14; c[4] = _vp.M21; c[5] = _vp.M22; c[6] = _vp.M23; c[7] = _vp.M24;
            c[8] = _vp.M31; c[9] = _vp.M32; c[10] = _vp.M33; c[11] = _vp.M34; c[12] = _vp.M41; c[13] = _vp.M42; c[14] = _vp.M43; c[15] = _vp.M44;
            var light = new Vec3(-0.45f, 0.7f, -0.55f).Normalized;
            c[16] = light.X; c[17] = light.Y; c[18] = light.Z;
            c[20] = eye.X; c[21] = eye.Y; c[22] = eye.Z;
            gpu.ClearNow(0.035f, 0.038f, 0.05f);
            _pipes.Solid.Draw(_solidBuf, _solidCount, c, 0, 0, width, height);
            c[26] = 2;                                              // see-through glass
            _pipes.Clear.Draw(_glassBuf, _glassCount, c, 0, 0, width, height);
        }

        public void Dispose() { if (_solidBuf != null) _solidBuf.Dispose(); if (_glassBuf != null) _glassBuf.Dispose(); }
    }
}
