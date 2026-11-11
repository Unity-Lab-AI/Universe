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
        private sealed class El { public string Sym, Name; public double Ab; public List<Line> Lines = new List<Line>(); }
        private static List<El> _els; private static double _boost = 2700, _scatter = 0.15;
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
            _els = els;
        }
        private static int Roman(string s) { switch (s) { case "I": return 1; case "II": return 2; case "III": return 3; case "IV": return 4; case "V": return 5; case "VI": return 6; case "VII": return 7; case "VIII": return 8; case "IX": return 9; case "X": return 10; default: return 1; } }
        private static readonly string[] RomanOf = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        private static double Hash(double u, int k) { double v = Math.Sin(u * 12.9898 + k * 78.233) * 43758.5453; return v - Math.Floor(v); }

        /// <summary>A nebula's glowing gas, each part one ion of one element in its real lines: kind 2 emission, 4 planetary, 5 a
        /// supernova's remnant; <paramref name="u"/> the nebula's own number (0 to 1), which sets how hard it is lit, how dense
        /// it is and its own scatter of every element's share. Null when the elements' data is not loaded.</summary>
        public static SpaceGen.Part[] Parts(int kind, double u, int most = 16)
        {
            if (!Loaded) return null;
            string key = kind == 4 ? "planetary" : kind == 5 ? "remnant" : "emission"; double[] rg; if (!_range.TryGetValue(key, out rg)) rg = new[] { 2.0, 4.0, 0.1, 0.65 };
            double logN = rg[0] + (rg[1] - rg[0]) * Hash(u, 1), exc = rg[2] + (rg[3] - rg[2]) * Hash(u, 2);
            var parts = new List<SpaceGen.Part>();
            foreach (var el in _els)
            {
                double ab = el.Ab; double pn; if (kind == 4 && _pn.TryGetValue(el.Sym, out pn)) ab = pn;
                int z = 0; foreach (char ch in el.Sym) z = z * 31 + ch;
                ab += _scatter * (Hash(u, 100 + z) * 2 - 1) * 1.7;                    // (its own share in this nebula, about the cosmic one)
                double perH = Math.Pow(10, ab - 12);
                // each ion of it: its share of the atoms (how hard the gas is lit puts the most at stage 1 + 2.5 x excitation, a spread
                // of about one stage either side), its lines' light and colour
                var byStage = new Dictionary<int, double[]>();
                foreach (var l in el.Lines)
                {
                    // the ion that gives the line: hydrogen's and helium's recombination lines come from atoms recombining, as many
                    // as are ionised (helium's second ion only round the hottest stars); a metal's own lab lines as its share in that ion
                    double frac;
                    if (!l.Forbidden && (el.Sym == "H" || el.Sym == "He")) frac = l.Stage == 1 ? (el.Sym == "H" ? 1 : Math.Min(1, 0.3 + 1.2 * exc)) : Math.Pow(exc, 3);
                    else { double c = 1 + 2.5 * exc, d = l.Stage - c; frac = Math.Exp(-d * d / 0.9); }
                    double light = perH * frac * l.Str * (l.Forbidden ? _boost / (1 + Math.Pow(10, logN - l.Ncr)) : 1);
                    if (light <= 0) continue;
                    float r, g, b; Wavelength(l.Wl, out r, out g, out b);
                    double[] acc; if (!byStage.TryGetValue(l.Stage, out acc)) { acc = new double[6]; byStage[l.Stage] = acc; }
                    acc[0] += light; acc[1] += r * light; acc[2] += g * light; acc[3] += b * light; if (light > acc[5]) { acc[5] = light; acc[4] = l.Wl; }
                }
                foreach (var kv in byStage)
                {
                    var a = kv.Value; if (a[0] <= 0) continue; float r = (float)(a[1] / a[0]), g = (float)(a[2] / a[0]), b = (float)(a[3] / a[0]), m = Math.Max(1e-4f, Math.Max(r, Math.Max(g, b)));
                    bool forb = false; foreach (var l in el.Lines) if (l.Stage == kv.Key && l.Forbidden && Math.Abs(l.Wl - a[4]) < 0.01) forb = true;
                    string ion = kv.Key == 1 ? "neutral " : kv.Key == 2 ? "ionised " : kv.Key == 3 ? "doubly ionised " : "highly ionised ";
                    string nm = ion + el.Name + " " + (forb ? "[" + el.Sym + " " + RomanOf[Math.Min(10, kv.Key)] + "]" : el.Sym + " " + RomanOf[Math.Min(10, kv.Key)]) + ", brightest at " + (a[4] / 10).ToString("0.0", CultureInfo.InvariantCulture) + " nm";
                    var p = new SpaceGen.Part(nm, el.Sym, a[0], r / m, g / m, b / m);
                    p.Zone = kv.Key >= 3 && el.Sym != "H" ? 1 : kv.Key == 1 && el.Sym != "H" && el.Sym != "He" ? 2 : 0;   // (twice-ionised and more round the hot middle; neutral at the edges)
                    p.Salt = (uint)(z * 977 + kv.Key * 131);
                    parts.Add(p);
                }
            }
            parts.Sort((x, y) => y.Share.CompareTo(x.Share));
            if (parts.Count > most) parts.RemoveRange(most, parts.Count - most);
            return parts.ToArray();
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
