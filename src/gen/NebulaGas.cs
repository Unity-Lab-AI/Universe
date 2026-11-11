// Universe · gen · a glowing nebula's gas from the elements themselves: every element by its cosmic abundance, glowing in its
// own real lines (content/elements.unt abundance, lines, nebular-lines; content/nebulas.unt @law nebula-gas)
using System;
using System.Collections.Generic;
using System.Globalization;
using Universe.Core;

namespace Universe.Gen
{
    internal static class NebulaGas
    {
        private sealed class Line { public double Wl, Ncr, Str; public int Stage; public bool Forbidden; }
        private sealed class Grp { public int Stage; public bool Forbidden; public double Ncr, Str, Wl, Best; public float R, G, B; }
        private sealed class El { public string Sym, Name; public double Ab; public int Z; public bool Rec; public List<Line> Lines = new List<Line>(); public List<Grp> Groups = new List<Grp>(); }
        private static List<El> _bright;
        private static List<El> _els; private static double _boost = 2700, _scatter = 0.15, _avLo = 0, _avHi = 3, _slLo = 0, _slHi = 0.35, _tkLo = 3500, _tkHi = 40000, _excSpread = 0.25, _colourShare = 0.7;
        /// <summary>How bright the gas is drawn against the stars, at any distance (content/nebulas.unt glow).</summary>
        public static double Glow = 0.35;
        private static readonly Dictionary<string, double[]> _range = new Dictionary<string, double[]>();
        private static readonly Dictionary<string, double> _pn = new Dictionary<string, double>();
        public static bool Loaded { get { return _els != null && _els.Count > 0; } }

        public static void Load(ContentLibrary content)
        {
            if (_els != null) return;
            var ci = CultureInfo.InvariantCulture; var fs = NumberStyles.Float; var els = new List<El>();
            foreach (var e in content.OfKind("elements.unt", "element"))
            {
                double ab; if (!double.TryParse(e.Get("abundance", ""), fs, ci, out ab)) continue;
                var el = new El { Sym = e.Get("symbol", e.Id), Name = e.Name.ToLowerInvariant(), Ab = ab };
                foreach (var bit in e.Get("lines", "").Split(','))
                {
                    var t = bit.Trim().Split(' '); double wl, st; if (t.Length < 3 || !double.TryParse(t[0], fs, ci, out wl) || !double.TryParse(t[2], fs, ci, out st)) continue;
                    el.Lines.Add(new Line { Wl = wl, Stage = Roman(t[1]), Str = st });
                }
                // (a line list's strengths are its own: each element's recombination light shared out over them)
                double sum = 0; foreach (var l in el.Lines) sum += l.Str; if (sum > 0) foreach (var l in el.Lines) l.Str /= sum;
                foreach (var bit in e.Get("nebular-lines", "").Split(','))
                {
                    var t = bit.Trim().Split(' '); double wl, nc, st; if (t.Length < 4 || !double.TryParse(t[0], fs, ci, out wl) || !double.TryParse(t[2], fs, ci, out nc) || !double.TryParse(t[3], fs, ci, out st)) continue;
                    el.Lines.Add(new Line { Wl = wl, Stage = Roman(t[1]), Ncr = nc, Str = st, Forbidden = true });
                }
                if (el.Lines.Count > 0) els.Add(el);
            }
            foreach (var law in content.OfKind("nebulas.unt", "law"))
            {
                if (law.Id != "nebula-gas") continue;
                double v; if (double.TryParse(law.Get("forbidden-boost", "2700"), fs, ci, out v)) _boost = v;
                if (double.TryParse(law.Get("scatter-dex", "0.15"), fs, ci, out v)) _scatter = v;
                { double lo, hi; if (law.TryRange("dust-av", out lo, out hi)) { _avLo = lo; _avHi = hi; } if (law.TryRange("starlight-share", out lo, out hi)) { _slLo = lo; _slHi = hi; } if (law.TryRange("star-k", out lo, out hi)) { _tkLo = lo; _tkHi = hi; } }
                if (double.TryParse(law.Get("excitation-spread", "0.25"), fs, ci, out v)) _excSpread = v;
                if (double.TryParse(law.Get("glow", "0.35"), fs, ci, out v)) Glow = v;
                if (double.TryParse(law.Get("colour-share", "0.7"), fs, ci, out v)) _colourShare = v;
                foreach (var k in new[] { "emission", "planetary", "remnant" })
                {
                    double lo, hi, a, b;
                    if (law.TryRange(k + "-density", out lo, out hi) && law.TryRange(k + "-excitation", out a, out b)) _range[k] = new[] { lo, hi, a, b };
                }
                foreach (var bit in law.Get("planetary-abundance", "").Split(','))
                {
                    var t = bit.Trim().Split(' '); double ab; if (t.Length == 2 && double.TryParse(t[1], fs, ci, out ab)) _pn[t[0]] = ab;
                }
            }
            // each element's lines summed once into groups by ion and by the density that puts them out, their colour summed by
            // strength; the elements that can ever glow bright enough to show kept apart (a ten-thousandth of hydrogen's light)
            var bright = new List<El>();
            foreach (var el in els)
            {
                foreach (char ch in el.Sym) el.Z = el.Z * 31 + ch; el.Rec = el.Sym == "H" || el.Sym == "He";
                foreach (var l in el.Lines)
                {
                    Grp gp = null; foreach (var q in el.Groups) if (q.Stage == l.Stage && q.Forbidden == l.Forbidden && q.Ncr == l.Ncr) gp = q;
                    if (gp == null) { gp = new Grp { Stage = l.Stage, Forbidden = l.Forbidden, Ncr = l.Ncr }; el.Groups.Add(gp); }
                    float r, g, b; Wavelength(l.Wl, out r, out g, out b); gp.Str += l.Str; gp.R += r * (float)l.Str; gp.G += g * (float)l.Str; gp.B += b * (float)l.Str;
                    if (l.Str > gp.Best) { gp.Best = l.Str; gp.Wl = l.Wl; }
                }
                foreach (var gp in el.Groups) if (gp.Str > 0) { gp.R /= (float)gp.Str; gp.G /= (float)gp.Str; gp.B /= (float)gp.Str; }
                bool forb = false; foreach (var l in el.Lines) if (l.Forbidden) forb = true;
                if (Math.Pow(10, el.Ab + 0.3 - 12) * (forb ? _boost : 1) >= 1e-4 || el.Rec) bright.Add(el);
            }
            _bright = bright;
            _els = els;
        }
        private static int Roman(string s) { switch (s) { case "I": return 1; case "II": return 2; case "III": return 3; case "IV": return 4; case "V": return 5; case "VI": return 6; case "VII": return 7; case "VIII": return 8; case "IX": return 9; case "X": return 10; default: return 1; } }
        private static readonly string[] RomanOf = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        private static double Hash(double u, int k) { double v = Math.Sin(u * 12.9898 + k * 78.233) * 43758.5453; return v - Math.Floor(v); }

        /// <summary>A nebula's glowing gas, each part one ion of one element in its real lines: kind 2 emission, 4 planetary, 5 a
        /// supernova's remnant; <paramref name="u"/> the nebula's own number (0 to 1), which sets how hard it is lit, how dense
        /// it is and its own scatter of every element's share. Null when the elements' data is not loaded.</summary>
        public static SpaceGen.Part[] Parts(int kind, double u, int most = 16, bool names = true)
        {
            if (!Loaded) return null;
            string key = kind == 4 ? "planetary" : kind == 5 ? "remnant" : "emission"; double[] rg; if (!_range.TryGetValue(key, out rg)) rg = new[] { 2.0, 4.0, 0.1, 0.65 };
            double logN = rg[0] + (rg[1] - rg[0]) * Hash(u, 1), exc = Math.Max(0, Math.Min(1, rg[2] - _excSpread + (rg[3] - rg[2] + 2 * _excSpread) * Hash(u, 2))), centre = 1 + 2.5 * exc;
            // its own dust: how much it dims its light (blue more than red, as one over the wavelength: at 610, 550 and 465 nm for
            // the eye's red, green and blue), and how much of its light is its star's own scattered by that dust
            double av = _avLo + (_avHi - _avLo) * Math.Pow(Hash(u, 3), 1.6), tauV = av / 1.086;
            float er = (float)Math.Exp(-tauV * 550 / 610.0), eg = (float)Math.Exp(-tauV), eb = (float)Math.Exp(-tauV * 550 / 465.0);
            double starShare = _slLo + (_slHi - _slLo) * Hash(u, 4), starK = Math.Exp(Math.Log(_tkLo) + (Math.Log(_tkHi) - Math.Log(_tkLo)) * Hash(u, 5));
            var parts = new List<SpaceGen.Part>(24); var st = new double[11 * 6];
            // only the elements that can ever glow bright enough to show (worked out once: hydrogen, helium, and those with a
            // forbidden line or common enough); every one of them still counts in the dive's atoms. Each nebula had gone through
            // every element and every line, and named every part, for the thousands of nebulae of a galaxy at once: two to three
            // seconds a frame, and the watchdog closed the game
            foreach (var el in _bright)
            {
                double ab = el.Ab; double pn; if (kind == 4 && _pn.TryGetValue(el.Sym, out pn)) ab = pn;
                ab += _scatter * (Hash(u, 100 + el.Z) * 2 - 1) * 1.7;                  // (its own share in this nebula, about the cosmic one)
                double perH = Math.Pow(10, ab - 12); Array.Clear(st, 0, st.Length);
                foreach (var gp in el.Groups)
                {
                    // the ion that gives the light: hydrogen's and helium's recombination lines from as many as are ionised (helium's
                    // second ion only round the hottest stars); the rest as their share in that ion, most at 1 + 2.5 x excitation
                    double frac;
                    if (!gp.Forbidden && el.Rec) frac = gp.Stage == 1 ? (el.Sym == "H" ? 1 : Math.Min(1, 0.3 + 1.2 * exc)) : exc * exc * exc;
                    else { double d = gp.Stage - centre; frac = Math.Exp(-d * d / 0.9); }
                    double light = perH * frac * gp.Str * (gp.Forbidden ? _boost / (1 + Math.Pow(10, logN - gp.Ncr)) : 1);
                    if (light <= 0) continue; int o = Math.Min(10, gp.Stage) * 6;
                    st[o] += light; st[o + 1] += gp.R * light; st[o + 2] += gp.G * light; st[o + 3] += gp.B * light;
                    if (light > st[o + 5]) { st[o + 5] = light; st[o + 4] = gp.Wl * (gp.Forbidden ? -1 : 1); }
                }
                for (int k = 1; k <= 10; k++)
                {
                    int o = k * 6; if (st[o] <= 0) continue;
                    float r = (float)(st[o + 1] / st[o]), g = (float)(st[o + 2] / st[o]), b = (float)(st[o + 3] / st[o]), m = Math.Max(1e-4f, Math.Max(r, Math.Max(g, b)));
                    string nm = null;
                    if (names)
                    {
                        bool forb = st[o + 4] < 0; double wl = Math.Abs(st[o + 4]);
                        string ion = k == 1 ? "neutral " : k == 2 ? "ionised " : k == 3 ? "doubly ionised " : "highly ionised ";
                        nm = ion + el.Name + " " + (forb ? "[" + el.Sym + " " + RomanOf[k] + "]" : el.Sym + " " + RomanOf[k]) + ", brightest at " + (wl / 10).ToString("0.0", CultureInfo.InvariantCulture) + " nm";
                    }
                    var p = new SpaceGen.Part(nm ?? el.Sym, el.Sym, st[o], r / m, g / m, b / m);
                    p.Zone = k >= 3 && el.Sym != "H" ? 1 : k == 1 && !el.Rec ? 2 : 0;   // (twice-ionised and more round the hot middle; neutral at the edges)
                    p.Salt = (uint)(el.Z * 977 + k * 131);
                    parts.Add(p);
                }
            }
            // the starlight its dust scatters: the star's own colour, bluer for the scattering (as one over the wavelength), a share
            // of all its light; then every part through the nebula's dust (dimmed, blue most), and each colour a glowing gas's, its
            // pure line colour partly toward its brightness
            double total = 0; foreach (var p in parts) total += p.Share;
            if (starShare > 0 && total > 0)
            {
                float sr, sg, sb; Planck(starK, out sr, out sg, out sb); sr *= 550f / 610; sb *= 550f / 465; float sm = Math.Max(1e-4f, Math.Max(sr, Math.Max(sg, sb)));
                var sp = new SpaceGen.Part(names ? "starlight scattered by its dust (its star about " + ((int)(starK / 100) * 100).ToString(CultureInfo.InvariantCulture) + " K)" : "dust", "Si", total * starShare / (1 - starShare), sr / sm, sg / sm, sb / sm);
                sp.Salt = 0x5D57u; parts.Add(sp);
            }
            foreach (var p in parts)
            {
                float r = p.R * er, g = p.G * eg, b = p.B * eb, lum = 0.3f * r + 0.59f * g + 0.11f * b, m0 = Math.Max(p.R, Math.Max(p.G, p.B));
                float k = (float)_colourShare; r = r * k + lum * (1 - k); g = g * k + lum * (1 - k); b = b * k + lum * (1 - k);
                float m = Math.Max(1e-4f, Math.Max(r, Math.Max(g, b))); p.Share *= m / Math.Max(1e-4f, m0); p.R = r / m; p.G = g / m; p.B = b / m;
            }
            if (names && av > 0.05) foreach (var p in parts) if (p.Name != null && !p.Name.StartsWith("starlight")) { p.Name += ", seen through its dust (" + av.ToString("0.0", CultureInfo.InvariantCulture) + " magnitudes)"; break; }
            parts.Sort((x, y) => y.Share.CompareTo(x.Share));
            if (parts.Count > most) parts.RemoveRange(most, parts.Count - most);
            return parts.ToArray();
        }

        /// <summary>A reflection nebula: its own lighting star's light scattered by its dust (bluer, as about one over the wavelength
        /// to the 1.5: small grains), seen through the same dust; blue-white round a hot star, white to gold round a cooler one.</summary>
        public static SpaceGen.Part[] Reflection(double u)
        {
            if (!Loaded) return null;
            double starK = Math.Exp(Math.Log(_tkLo) + (Math.Log(_tkHi) - Math.Log(_tkLo)) * Hash(u, 5)), tauV = (_avLo + (_avHi - _avLo) * Math.Pow(Hash(u, 3), 1.6)) / 1.086 * 0.5;
            float r, g, b; Planck(starK, out r, out g, out b);
            r *= (float)(Math.Pow(550 / 610.0, 1.5) * Math.Exp(-tauV * 550 / 610.0)); g *= (float)Math.Exp(-tauV); b *= (float)(Math.Pow(550 / 465.0, 1.5) * Math.Exp(-tauV * 550 / 465.0));
            float lum = 0.3f * r + 0.59f * g + 0.11f * b, k = (float)_colourShare; r = r * k + lum * (1 - k); g = g * k + lum * (1 - k); b = b * k + lum * (1 - k);
            float m = Math.Max(1e-4f, Math.Max(r, Math.Max(g, b)));
            var p = new SpaceGen.Part("dust (silicate and carbon grains) lit by its star of about " + ((int)(starK / 100) * 100).ToString(CultureInfo.InvariantCulture) + " K", "Si", 1, r / m, g / m, b / m); p.Salt = 0x5D58u;
            return new[] { p };
        }

        /// <summary>A star's light's colour from its heat (Planck's law at 610, 550 and 465 nm), brightest channel 1.</summary>
        public static void Planck(double k, out float r, out float g, out float b)
        {
            Func<double, double> bb = nm => { double l = nm * 1e-9; return 1 / (l * l * l * l * l * (Math.Exp(1.4388e-2 / (l * Math.Max(500, k))) - 1)); };
            double R = bb(610), G = bb(550), B = bb(465), m = Math.Max(R, Math.Max(G, B)); r = (float)(R / m); g = (float)(G / m); b = (float)(B / m);
        }

        /// <summary>A glowing nebula's atoms by count, every element with a measured abundance (in this nebula's own scatter, a
        /// planetary nebula's own averages), for the dive into its gas: its share of the atoms, not of the light.</summary>
        public static SpaceGen.Part[] Atoms(int kind, double u)
        {
            if (!Loaded) return null; var l = new List<SpaceGen.Part>(); double sum = 0;
            foreach (var el in _els)
            {
                double ab = el.Ab; double pn; if (kind == 4 && _pn.TryGetValue(el.Sym, out pn)) ab = pn;
                int z = 0; foreach (char ch in el.Sym) z = z * 31 + ch;
                ab += _scatter * (Hash(u, 100 + z) * 2 - 1) * 1.7; double n = Math.Pow(10, ab - 12); sum += n;
                float r, g, b; double best = 0, wl = 6563; foreach (var ln in el.Lines) if (ln.Str > best) { best = ln.Str; wl = ln.Wl; } Wavelength(wl, out r, out g, out b);
                l.Add(new SpaceGen.Part(el.Name, el.Sym, n, r, g, b));
            }
            foreach (var p in l) p.Share /= sum;
            l.Sort((x, y) => y.Share.CompareTo(x.Share)); return l.ToArray();
        }

        /// <summary>The colour of light of one wavelength (angstroms, in air), as the eye sees it (Bruton's approximation of the
        /// CIE curves, dimmed toward the eye's limits at the violet and red ends).</summary>
        public static void Wavelength(double wlA, out float r, out float g, out float b)
        {
            double w = wlA / 10, R = 0, G = 0, B = 0;
            if (w < 440) { R = -(w - 440) / 60; B = 1; } else if (w < 490) { G = (w - 440) / 50; B = 1; } else if (w < 510) { G = 1; B = -(w - 510) / 20; }
            else if (w < 580) { R = (w - 510) / 70; G = 1; } else if (w < 645) { R = 1; G = -(w - 645) / 65; } else R = 1;
            double f = w < 420 ? 0.3 + 0.7 * (w - 380) / 40 : w > 700 ? 0.3 + 0.7 * (780 - w) / 80 : 1; f = Math.Max(0.05, f);
            r = (float)Math.Max(0, R * f); g = (float)Math.Max(0, G * f); b = (float)Math.Max(0, B * f);
        }
    }
}
