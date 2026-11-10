// Universe · shell · the matter view, its part: the photons (one class in several files: src/shell/MatterView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        // ── the light of the matter itself (Universe DESIGN 740): everything at a temperature gives off photons, as many and of the
        // energies Planck's law says (x^3 / (e^x - 1) in x = h nu / kT, peaking at Wien's 2.898 mm K / T): infrared at a world's
        // ground, visible at a star's face, X-rays in its core, gamma past a billion kelvin. Each photon flies straight at the speed of
        // light (slowed to be watched), in its wavelength's own colour where the eye sees it, and one carrying more than an atom's
        // first ionisation energy pulls an electron off it ──
        /// <summary>The temperature of the matter where you are (K), set by the universe view (the world's ground, the layer you
        /// are in, the plasma); 0: none known, no photons drawn.</summary>
        public double TempK;
        private const double Kb = 8.617333e-5;                                          // Boltzmann's constant, eV per K
        // a photon energy in units of kT, drawn exactly from Planck's spectrum of photon counts, x^2 / (e^x - 1): that is the sum over
        // n of x^2 e^(-n x), so n is drawn with the chance 1 / (n^3 zeta(3)) and then x from a gamma of three scaled by 1/n
        private static double PlanckX(double u, double v)
        {
            double pick = Hash01(u, 7) * 1.2020569031595942, acc = 0; int n = 1;   // (zeta(3))
            for (; n < 60; n++) { acc += 1.0 / ((double)n * n * n); if (pick <= acc) break; }
            double a = Math.Max(1e-12, Hash01(u, 1)), b = Math.Max(1e-12, Hash01(v, 2)), c = Math.Max(1e-12, Hash01(u + v, 3));
            return -Math.Log(a * b * c) / n;
        }
        private static double Hash01(double a, int t) { double h = Math.Sin(a * 12.9898 + t * 78.233) * 43758.5453; return h - Math.Floor(h); }
        // the colour of light of a wavelength (nm) where the eye sees it (380 to 700 nm), and beyond it the band's mark (infrared a
        // dull red, ultraviolet a violet, X-rays and gamma a blue-white): a mark, not light, as the card says
        private static void WaveColour(double nm, out float r, out float g, out float b)
        {
            if (nm > 700) { r = 0.45f; g = 0.05f; b = 0.02f; return; }
            if (nm < 380) { if (nm < 10) { r = 0.75f; g = 0.85f; b = 1f; } else { r = 0.45f; g = 0.2f; b = 0.85f; } return; }
            if (nm < 440) { r = (float)((440 - nm) / 60); g = 0; b = 1; }
            else if (nm < 490) { r = 0; g = (float)((nm - 440) / 50); b = 1; }
            else if (nm < 510) { r = 0; g = 1; b = (float)((510 - nm) / 20); }
            else if (nm < 580) { r = (float)((nm - 510) / 70); g = 1; b = 0; }
            else if (nm < 645) { r = 1; g = (float)((645 - nm) / 65); b = 0; }
            else { r = 1; g = 0; b = 0; }
        }
        private static string Band(double nm) { return nm > 1e6 ? "radio" : nm > 1e3 ? "infrared" : nm > 700 ? "near infrared" : nm >= 380 ? "visible" : nm > 10 ? "ultraviolet" : nm > 0.01 ? "X-ray" : "gamma"; }
        /// <summary>The peak of the light here (Wien), in nm.</summary>
        private double PeakNm { get { return TempK > 0 ? 2.897771955e6 / TempK : 0; } }
        // the share of the photons here that carry more than e electron-volts (the tail of Planck's spectrum past x = e / kT), worked
        // out by summing the spectrum
        private double ShareAbove(double ev)
        {
            if (TempK <= 0 || ev <= 0) return 0; double x0 = ev / (Kb * TempK), all = 0, above = 0, dx = 0.02;
            for (double x = dx * 0.5; x < 60; x += dx) { double n = x * x / (Math.Exp(x) - 1); all += n; if (x >= x0) above += n; }
            return all > 0 ? above / all : 0;
        }
        private void Photons(List<StarVertex> glow, double s, double time)
        {
            if (TempK <= 0) return;
            var focus = FocusAtom(); double ionEv = focus != null ? focus.IonEv : 0;
            const int N = 90;
            for (int k = 0; k < N; k++)
            {
                // each photon: born somewhere round you, at its own moment, flying its own way at the speed of light slowed to cross the
                // view in about a second and a half; its energy from Planck's spectrum at this temperature
                double cyc = Math.Floor(time * 0.66 + k * 0.618), ph = time * 0.66 + k * 0.618 - cyc, seed = k * 1.37 + cyc * 0.113;
                double ox = (Hash01(seed, 1) - 0.5) * 2.4 * s, oz = (Hash01(seed, 2) - 0.5) * 2.4 * s, oy = (Hash01(seed, 3) - 0.6) * 1.2 * s;
                double u = Hash01(seed, 4) * 2 - 1, a = Hash01(seed, 5) * Math.PI * 2, sq = Math.Sqrt(1 - u * u);
                double x = PlanckX(seed, seed * 1.7 + 0.3), ev = x * Kb * TempK, nm = 1239.84193 / ev;
                float r, g, b; WaveColour(nm, out r, out g, out b); float br = 0.8f * (float)Math.Sin(ph * Math.PI);
                double px = _fx + ox + sq * Math.Cos(a) * ph * 1.5 * s, py = _fy - _yOff + oy + u * ph * 1.5 * s, pz = _fz + oz + sq * Math.Sin(a) * ph * 1.5 * s;
                Add(glow, px, py, pz, s * 0.0035, r * br, g * br, b * br, s);
                if (ionEv > 0 && ev > ionEv && k % 9 == 0)
                {
                    // one carrying more than the atom here needs to lose its outer electron, arriving at it: the electron flung off
                    double fx, fy, fz; Focus(out fx, out fy, out fz); double flight = ph * 2 * s;
                    Add(glow, fx + flight * sq * Math.Cos(a), fy + flight * u, fz + flight * sq * Math.Sin(a), s * 0.005, 1f, 0.8f, 0.25f, s);
                }
            }
        }
        /// <summary>The light here, for the card: its temperature, its peak and band, and how much of it can ionise the atom here.</summary>
        private void PhotonWords(List<string> L)
        {
            if (TempK <= 0) return; double nm = PeakNm;
            L.Add("light here: about " + TempK.ToString("N0") + " K, its photons peaking at " + ScaleHud.Words(nm * 1e-12) + " (" + Band(nm) + ")" + (nm > 700 || nm < 380 ? "; colours past the eye's are marks" : ""));
            var e = FocusAtom();
            if (e != null && e.IonEv > 0)
            {
                double sh = ShareAbove(e.IonEv);
                L.Add(e.Name.ToLowerInvariant() + " is ionised by photons over " + e.IonEv.ToString("0.#") + " eV (" + ScaleHud.Words(1239.84193 / e.IonEv * 1e-12) + "): " + (sh >= 0.01 ? (sh * 100).ToString("0.#") + "%" : sh > 0 ? "1 in " + (1 / sh).ToString("0.#E+0") : "none") + " of the light here");
            }
        }
    }
}
