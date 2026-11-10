// Universe · shell · the universe view, its part: supernovae (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── supernovae: every galaxy's, from its seed and the game's time (content/physics.unt @law supernova): when, at which of its
        // stars, which kind (the collapse of a heavy star's core, or a white dwarf blown apart), how heavy the star was; each one's
        // light by its real light curve, its shell of flung matter growing for a hundred thousand years, and the neutron star or
        // black hole born at its middle (one of the bodies round you, picked like any) ──
        private sealed class Sn { public double T, X, Y, Z, Ux, Uy, Uz, Mass; public int Kind; public Galaxy G; public SnKind K; public bool Ia { get { return K.Leaves == "nothing"; } } }   // T: when, in years of the game's time; X Y Z in its galaxy's frame; U: in the universe
        private sealed class SnKind { public string Id, Name, Leaves, Says; public double Share, Peak, Rise, Plateau, Halving, Tail, V, PeakK = 6000; public float R = 1, G = 1, B = 1; }
        private readonly List<SnKind> _snKinds = new List<SnKind>();
        private sealed class SnCache { public int From = int.MaxValue, To = int.MinValue; public bool Full; public readonly List<Sn> Events = new List<Sn>(); }
        private readonly Dictionary<ulong, SnCache> _snCache = new Dictionary<ulong, SnCache>();
        private double _snRate = 3, _snCc = 0.7, _snCcPeak = 5e8, _snIaPeak = 4e9, _snCcRise = 10, _snIaRise = 18, _snPlateau = 100, _snCcV = 5000, _snIaV = 10000, _snFreeYears = 300, _snRemnantYears = 1e5, _snNsMax = 20; private bool _snLoaded;
        private Sn _tSn;                                                                   // the supernova picked (its card, the tour's words)
        private void LoadSupernovaLaw()
        {
            _snLoaded = true; var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("physics.unt", "law"))
            {
                if (e.Id != "supernova") continue;
                Func<string, double, double> num = (k, d) => { double v; return double.TryParse(e.Get(k, ""), fs, ci, out v) ? v : d; };
                _snRate = num("rate-per-century", _snRate); _snCc = num("core-collapse-share", _snCc); _snCcPeak = num("cc-peak-sol", _snCcPeak); _snIaPeak = num("ia-peak-sol", _snIaPeak);
                _snCcRise = num("cc-rise-days", _snCcRise); _snIaRise = num("ia-rise-days", _snIaRise); _snPlateau = num("plateau-days", _snPlateau); _snCcV = num("cc-ejecta-km-s", _snCcV); _snIaV = num("ia-ejecta-km-s", _snIaV);
                _snFreeYears = num("free-years", _snFreeYears); _snRemnantYears = num("remnant-years", _snRemnantYears); _snNsMax = num("neutron-star-max-sol", _snNsMax);
            }
            // the kinds (content/physics.unt @supernova): each with its share, its light curve, how fast its matter flies, what it leaves
            foreach (var e in _content.OfKind("physics.unt", "supernova"))
            {
                Func<string, double, double> num = (k, d) => { double v; return double.TryParse(e.Get(k, ""), fs, ci, out v) ? v : d; };
                var k2 = new SnKind { Id = e.Id, Name = e.Name, Leaves = e.Get("leaves", "core"), Says = e.Get("says", ""), Share = num("share", 0), Peak = num("peak-sol", 5e8), Rise = num("rise-days", 10), Plateau = num("plateau-days", 0), Halving = num("halving-days", 15), Tail = num("tail-share", 0.05), V = num("ejecta-km-s", 5000), PeakK = num("peak-k", 6000) };
                var cw = e.Get("colour", "1 1 1").Split(' '); if (cw.Length == 3) { float.TryParse(cw[0], fs, ci, out k2.R); float.TryParse(cw[1], fs, ci, out k2.G); float.TryParse(cw[2], fs, ci, out k2.B); }
                if (k2.Share > 0) _snKinds.Add(k2);
            }
            if (_snKinds.Count == 0) _snKinds.Add(new SnKind { Id = "ii-p", Name = "Type II-P supernova", Leaves = "core", Share = 1, Peak = _snCcPeak, Rise = _snCcRise, Plateau = _snPlateau, Halving = 8, Tail = 0.05, V = _snCcV });
        }
        // a galaxy's rate a year: the law's rate for a galaxy of a hundred billion stars (about fifty thousand light-years across), by its size squared
        private double SnRate(Galaxy g) { double k = g.Scale / (5e4 / Units.LightYearsPerUnit); return _snRate / 100.0 * Math.Max(0.1, Math.Min(4, k * k)); }
        private void SnYear(Galaxy g, SnCache c, int year)
        {
            var rng = new Rng((ulong)year * 0x9E3779B97F4A7C15UL ^ g.Seed ^ 0xA5A5UL);
            double lam = SnRate(g); int n = (int)Math.Floor(lam) + (rng.NextDouble() < lam - Math.Floor(lam) ? 1 : 0);
            for (int q = 0; q < n; q++)
            {
                var sn = new Sn { G = g, T = year + rng.NextDouble() };
                { double tot = 0; foreach (var k2 in _snKinds) tot += k2.Share; double pick = rng.NextDouble() * tot; sn.Kind = _snKinds.Count - 1; for (int qk = 0; qk < _snKinds.Count; qk++) { pick -= _snKinds[qk].Share; if (pick <= 0) { sn.Kind = qk; break; } } sn.K = _snKinds[sn.Kind]; }
                if (g.FarPts != null && g.FarPts.Length > 0) { var p = g.FarPts[rng.Below(g.FarPts.Length)]; sn.X = p.X; sn.Y = p.Y; sn.Z = p.Z; }
                else { double r = Math.Sqrt(rng.NextDouble()) * 0.5, a = rng.NextDouble() * Math.PI * 2; sn.X = Math.Cos(a) * r; sn.Y = rng.Gaussian() * 0.02; sn.Z = Math.Sin(a) * r; }
                sn.Mass = sn.K.Leaves == "nothing" ? 1.4 : sn.K.Leaves == "magnetar" ? 40 * Math.Pow(3, rng.NextDouble()) : 8 * Math.Pow(5, rng.NextDouble());   // a white dwarf at its limit; a star of 8 to 40 solar masses; a very heavy one, 40 to 120
                ToUniverse(g, sn.X, sn.Y, sn.Z, out sn.Ux, out sn.Uy, out sn.Uz);
                c.Events.Add(sn);
            }
        }
        /// <summary>A galaxy's supernovae up to now: the last few years' (their light), or with its whole history back a hundred thousand
        /// years (their shells and the remnants at their middles) for the galaxy you are in.</summary>
        private SnCache Supernovae(Galaxy g, bool full)
        {
            if (!_snLoaded) LoadSupernovaLaw();
            SnCache c; if (!_snCache.TryGetValue(g.Seed, out c)) { c = new SnCache(); _snCache[g.Seed] = c; }
            int now = (int)Math.Floor(_simYears), from = full ? now - (int)_snRemnantYears : now - 2, to = now + 1;
            if (c.From == int.MaxValue) { for (int y = from; y <= to; y++) SnYear(g, c, y); c.From = from; c.To = to; c.Full = full; }
            else
            {
                if (full && !c.Full) { for (int y = from; y < c.From; y++) SnYear(g, c, y); c.From = Math.Min(c.From, from); c.Full = true; }
                while (c.To < to) { c.To++; SnYear(g, c, c.To); }
            }
            return c;
        }
        // its light, in solar luminosities, by its age in days: the real light curves (content/physics.unt @law supernova)
        private double SnLum(Sn sn, double days)
        {
            if (days < 0) return 0; var k = sn.K;
            double early;
            if (days < k.Rise) early = k.Peak * Math.Pow(days / k.Rise, 2);                                              // the rise: the fireball's growing face
            else if (days < k.Rise + k.Plateau) early = k.Peak * (1 - 0.3 * (days - k.Rise) / k.Plateau);              // a plateau (a thick hydrogen envelope recombining), dimming a little
            else early = k.Peak * (k.Plateau > 0 ? 0.7 : 1) * Math.Pow(0.5, (days - k.Rise - k.Plateau) / k.Halving);   // the fade: halving every so many days
            double tail = k.Peak * k.Tail * Math.Pow(0.5, Math.Max(0, days - k.Rise) / 77);                               // the radioactive tail: cobalt-56, halving every 77 days
            return Math.Max(early, tail);
        }
        // its shell's radius in light-years by its age in years: free expansion, then slowed as it sweeps up the gas between the stars
        private double SnShellLy(Sn sn, double years)
        {
            double v = sn.K.V, free = v * 3.15576e7 / 9.4607e12 * Math.Min(years, _snFreeYears);
            return years <= _snFreeYears ? free : free * Math.Pow(years / _snFreeYears, 0.4);
        }
        private double SnShellKmS(Sn sn, double years) { double v = sn.K.V; return years <= _snFreeYears ? v : v * 0.4 * Math.Pow(_snFreeYears / years, 0.6); }
        private static string Years(double y) { return y < 1 ? (y * 365.25).ToString("0") + " days" : y < 1000 ? y.ToString("0") + " years" : y < 1e6 ? (y / 1000).ToString("0.#") + " thousand years" : (y / 1e6).ToString("0.#") + " million years"; }
        private int _snLogTick = -1;   // (the log's clock for the tour's supernova: what its fireball is doing, twice a second)
        private UploadBuffer _snPointBuf, _shellYoungBuf, _shellOldBuf, _fireBuf, _shellCrabBuf, _shellCasBuf, _ejectaBuf; private int _ejectaN; private int _snPointCap, _shellN; private StarSurface _fireSurf; private StarVertex[] _fireVerts;   // (_fireSurf: a fireball's face, drawn as a star's, coloured by its heat each frame)
        private void MakeShells()
        {
            // a shell of flung matter: a thin sphere of points, bunched into filaments (knots and streamers, as the real ones are), the
            // young one yellow-white and red (hot, glowing in hydrogen and oxygen), the old one faint and blue-green
            // in the real colours of their glowing gas: hydrogen's red, oxygen's green and teal, sulfur's orange, the blue-white of electrons
            // spiralling in a magnetic field; each remnant one of the real looks (the Crab's red filaments round a blue-white heart, Cas A's
            // green oxygen and orange sulfur knots, the Veil's red and teal lace), the old ones faint red and blue
            var rng = new Rng(0x5E11UL); var young = new List<StarVertex>(); var old = new List<StarVertex>(); var crab = new List<StarVertex>(); var cas = new List<StarVertex>();
            for (int k = 0; k < 2400; k++)
            {
                double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, sq = Math.Sqrt(1 - u * u);
                double fil = 0.5 + 0.5 * Math.Sin(a * 7 + u * 11) * Math.Sin(u * 13 - a * 3);      // the filaments: bunched, not even
                double r = 1 + (rng.NextDouble() - 0.5) * 0.12 - 0.06 * fil;
                float br = (float)(0.25 + 0.75 * fil * fil); bool lace = fil > 0.55;
                float x = (float)(sq * Math.Cos(a) * r), y = (float)(u * r), z = (float)(sq * Math.Sin(a) * r);
                young.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.05f, R = (lace ? 1f : 0.3f) * br, G = (lace ? 0.32f : 0.85f) * br, B = (lace ? 0.3f : 0.8f) * br, A = 1 });          // the Veil: red hydrogen lace, teal oxygen
                crab.Add(new StarVertex { X = x * (lace ? 1f : 0.6f), Y = y * (lace ? 1f : 0.6f), Z = z * (lace ? 1f : 0.6f), Size = lace ? 0.05f : 0.09f, R = (lace ? 1f : 0.6f) * br, G = (lace ? 0.35f : 0.7f) * br, B = (lace ? 0.3f : 1f) * br, A = 1 });   // the Crab: red filaments round a blue-white glow of spiralling electrons
                cas.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.05f, R = (lace ? 0.3f : 1f) * br, G = (lace ? 0.95f : 0.5f) * br, B = (lace ? 0.55f : 0.2f) * br, A = 1 });            // Cas A: green oxygen knots, orange sulfur
                old.Add(new StarVertex { X = x, Y = y, Z = z, Size = 0.06f, R = (lace ? 0.6f : 0.35f) * br, G = (lace ? 0.3f : 0.4f) * br, B = (lace ? 0.35f : 0.7f) * br, A = 1 });
            }
            _shellN = young.Count;
            _shellYoungBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellYoungBuf.Write(young.ToArray(), _shellN);
            _shellOldBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellOldBuf.Write(old.ToArray(), _shellN);
            _shellCrabBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellCrabBuf.Write(crab.ToArray(), _shellN);
            _shellCasBuf = new UploadBuffer(_gpu.Device, (long)_shellN * Stride); _shellCasBuf.Write(cas.ToArray(), _shellN);
            // the ejecta's cloud: the flung matter filling the shell, soft points through its volume (denser toward the edge, as matter
            // swept up is), in hot gas's colours, so a young supernova is a ballooning cloud round its glowing face, never a bare star
            var cloud = new List<StarVertex>(); var rngC = new Rng(0xC10DUL);
            for (int k = 0; k < 2400; k++)
            {
                double u = rngC.NextDouble() * 2 - 1, a = rngC.NextDouble() * Math.PI * 2, sq = Math.Sqrt(1 - u * u), r = 0.35 + 0.65 * Math.Pow(rngC.NextDouble(), 0.4); float br = (float)(0.3 + 0.5 * rngC.NextDouble());
                cloud.Add(new StarVertex { X = (float)(sq * Math.Cos(a) * r), Y = (float)(u * r), Z = (float)(sq * Math.Sin(a) * r), Size = 0.16f, R = 1f * br, G = 0.55f * br, B = 0.35f * br, A = 1 });
            }
            _ejectaN = cloud.Count; _ejectaBuf = new UploadBuffer(_gpu.Device, (long)_ejectaN * Stride); _ejectaBuf.Write(cloud.ToArray(), _ejectaN);
        }
        // the colour of a glowing surface by its heat (the usual blackbody look: orange at 3,000 K, white near 6,500, blue-white past 10,000)
        private static void Blackbody(double tK, out float r, out float g, out float b)
        {
            double t = Math.Max(1500, Math.Min(60000, tK)) / 100;
            double rr = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592), gg = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492), bb = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            r = (float)Math.Max(0, Math.Min(1, rr / 255)); g = (float)Math.Max(0, Math.Min(1, gg / 255)); b = (float)Math.Max(0, Math.Min(1, bb / 255));
        }
        // its glowing face's heat by its age: a hundred thousand degrees at the shock's breakout, cooling to its kind's heat by the
        // peak, about 5,500 on a plateau, settling near 4,500 as it fades (the real run of every kind's photosphere)
        private double SnHeatK(Sn sn, double days)
        {
            var k = sn.K;
            if (days < k.Rise) return Math.Min(100000, k.PeakK * Math.Pow(k.Rise / Math.Max(0.02, days), 0.7));
            if (days < k.Rise + k.Plateau) return k.PeakK - (k.PeakK - 5000) * (days - k.Rise) / k.Plateau;
            return Math.Max(4500, k.PeakK - (k.PeakK - 4500) * Math.Min(1, (days - k.Rise - k.Plateau) / 60));
        }
        // the radius (km) of its glowing face: out at about six tenths of the ejecta's speed, to about ten billion km, holding while
        // it shines, then drawing back in as the fireball thins to a cloud (the nebular phase); and of the ejecta's own edge
        private double SnPhotKm(Sn sn, double days)
        {
            var k = sn.K; double end = k.Rise + k.Plateau + 3 * k.Halving, cap = 1e10 * Math.Sqrt(k.Peak / 5e8);
            double r = Math.Min(cap, 0.6 * k.V * 86400 * Math.Max(0, days));
            return days <= end ? r : r * Math.Exp(-(days - end) / 40);
        }
        private double SnEjectaKm(Sn sn, double days) { return sn.K.V * 86400 * Math.Max(0, days); }
        /// <summary>The supernovae going now in the galaxies round you, each as bright as physics says from where you are (its light
        /// over the square of its distance, against a star of one solar luminosity ten light-years off: a glow that grows with the logarithm of that,
        /// so in its own galaxy it outshines every star, as a real one does, and from another galaxy it is one point among that
        /// galaxy's); close in, the fireball itself at its real size and the colour of its heat, then the thinning cloud of its
        /// ejecta with its remnant inside; and in the galaxy you are in, the shells of the old ones, each at its own size by its age.</summary>
        private void DrawSupernovae(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (!_snLoaded) LoadSupernovaLaw();
            if (_shellYoungBuf == null) MakeShells();
            double now = _simYears; var pts = new List<StarVertex>();
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double gx = (g.X - _ax) - _ox, gy = (g.Y - _ay) - _oy, gz = (g.Z - _az) - _oz, gd = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                if (gd > 400 * g.Scale) continue;
                bool full = i == _near; var c = Supernovae(g, full); if (c.Events.Count == 0) continue;
                foreach (var sn in c.Events)
                {
                    double years = now - sn.T; if (years < 0) continue; double days = years * 365.25;
                    if (_tSn != null && Math.Abs(sn.T - _tSn.T) < 1e-9 && Math.Abs(sn.Ux - _tSn.Ux) < 1e-6 && Math.Abs(sn.Uz - _tSn.Uz) < 1e-6 && (int)(time * 2) != _snLogTick)
                    {
                        // the supernova the tour is at, in the log: its age, its light, its face and its ejecta now, how far off, so a blank
                        // stop can be read from the log instead of guessed at
                        _snLogTick = (int)(time * 2); double uxl = (sn.Ux - _ax) - _ox, uyl = (sn.Uy - _ay) - _oy, uzl = (sn.Uz - _az) - _oz;
                        ZoomLog("SN at the tour's pick: " + sn.K.Name + ", " + days.ToString("0") + " days, light " + SnLum(sn, days).ToString("0.#E+0") + " sol, face " + ScaleHud.Words(SnPhotKm(sn, days)) + " across, ejecta " + ScaleHud.Words(SnEjectaKm(sn, days)) + ", " + Units.Describe(Math.Sqrt(uxl * uxl + uyl * uyl + uzl * uzl)) + " away, galaxy " + (full ? "near" : "far") + ", leaves " + sn.K.Leaves + ", remnant bodies " + _extras.Count);
                    }
                    double ux = (sn.Ux - _ax) - _ox, uy = (sn.Uy - _ay) - _oy, uz = (sn.Uz - _az) - _oz, d = Math.Sqrt(ux * ux + uy * uy + uz * uz), dLy = Math.Max(1e-9, d * Units.LightYearsPerUnit);
                    if (days < 700)
                    {
                        double L = SnLum(sn, days); if (L < 1e4) continue;
                        double rPhot = Units.Km(SnPhotKm(sn, days)), rEj = Units.Km(SnEjectaKm(sn, days)); float cr, cg, cb; Blackbody(SnHeatK(sn, days), out cr, out cg, out cb);
                        bool closeIn = rPhot > 0 && rPhot / Math.Max(1e-30, d) * h > 6;
                        if (!closeIn)
                        {
                            double ratio = L / (dLy * dLy) * 100;                                                // against one solar luminosity at ten light-years
                            double frac = 0.0022 * Math.Max(0.25, 1 + 0.5 * Math.Log10(Math.Max(1e-6, ratio)));     // its glow's share of the view
                            float size = (float)(frac * d);                                                         // (the glow is that share of the view at its distance)
                            float fr = sn.K.R * 0.5f + cr * 0.5f, fg = sn.K.G * 0.5f + cg * 0.5f, fb = sn.K.B * 0.5f + cb * 0.5f;
                            pts.Add(new StarVertex { X = (float)ux, Y = (float)uy, Z = (float)uz, Size = size, R = fr, G = fg, B = fb, A = 1 });
                            if (ratio > 1e3) pts.Add(new StarVertex { X = (float)ux, Y = (float)uy, Z = (float)uz, Size = size * 3.5f, R = fr * 0.12f, G = fg * 0.12f, B = fb * 0.12f, A = 1 });   // its halo, once it is brilliant
                        }
                        else
                        {
                            // the fireball: its glowing face, a ball of plasma at its real size in the colour of its heat, drawn as a star's
                            // face is (one sheet of boiling cells, darker to the limb), full-bright while it shines (a photosphere's face is
                            // as bright as its heat says, whatever its light is against its peak: a thin dim puff of points had stood for it,
                            // and at two hundred days there was nothing to see in the ring), fading only as it thins to a cloud on the tail
                            double end = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving; float bright = (float)(L > 1e6 ? 1 : Math.Max(0.25, days <= end ? 1 : Math.Exp(-(days - end) / 40)));   // (full while it blazes at over a million suns, whatever its day; on the tail a quarter at the least: the thinning cloud still glows, the remnant seen through it)
                            if (_fireSurf == null) { string nm0; StarGen.Build(_content, "supernova-fireball", 0xF1AEUL, out nm0, out _fireSurf); _fireSurf.Giant = true; _fireSurf.Cell = 1.6f; _fireVerts = new StarVertex[_fireSurf.Normals.Length]; _fireBuf = new UploadBuffer(_gpu.Device, (long)_fireVerts.Length * Stride); }
                            _fireSurf.R = cr; _fireSurf.G = cg; _fireSurf.B = cb; _fireSurf.Hot = SnHeatK(sn, days) > 7000;
                            var toEye = new Vec3((float)(-ux / d), (float)(-uy / d), (float)(-uz / d));
                            int n = _fireSurf.Draw(toEye, time, _fireVerts, 0); _fireBuf.Write(_fireVerts, n);
                            var mf = Mat4.Identity; mf.M11 = mf.M22 = mf.M33 = (float)rPhot; mf.M41 = (float)ux; mf.M42 = (float)uy; mf.M43 = (float)uz;
                            DrawPoints(_fireBuf, n, mf * vp, right, up, 1f, 0f, w, h, time, 0, bright);
                        }
                        if (rEj > rPhot * 1.2 && rEj / Math.Max(1e-30, d) * h > 2.5)
                        {
                            // the ejecta: the flung matter's cloud ballooning out round the glowing face, bright while the supernova shines
                            // whatever its light is against its peak (its thin edge at a tenth had left a bare star with no cloud at every
                            // stop), and its glowing edge racing out ahead; what is left once the fireball has thinned
                            float fade = (float)Math.Max(L > 1e6 ? 0.85 : 0.2, Math.Min(0.9, L / sn.K.Peak + 0.1));
                            var m = Mat4.Identity; m.M11 = (float)rEj; m.M22 = (float)rEj; m.M33 = (float)rEj; m.M41 = (float)ux; m.M42 = (float)uy; m.M43 = (float)uz;
                            DrawPoints(_ejectaBuf, _ejectaN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade * 0.55f);
                            DrawPoints(_shellYoungBuf, _shellN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade);
                        }
                    }
                    else if (full && years < _snRemnantYears)
                    {
                        // a remnant's shell: a faint nebula, seen only from near (within about two thousand light-years, the youngest from
                        // farther), never a galaxy-wide crop of gas balls; a going supernova is the thing seen across the galaxy
                        double reachLy = years < 2000 ? 6000 : years < 20000 ? 2500 : 1200; if (dLy > reachLy) continue;
                        double rly = SnShellLy(sn, years), r = rly / Units.LightYearsPerUnit;
                        if (r / Math.Max(1e-30, d) * h < 2.5) continue;                                          // (too small on screen to show)
                        float fade = (float)(Math.Max(0.04, Math.Min(1, Math.Pow(300 / Math.Max(300, years), 0.4))) * Math.Min(1, (reachLy - dLy) / (reachLy * 0.5)));
                        var m = Mat4.Identity; m.M11 = (float)r; m.M22 = (float)r; m.M33 = (float)r; m.M41 = (float)ux; m.M42 = (float)uy; m.M43 = (float)uz;
                        int look = (int)(Math.Abs(sn.T * 1000) % 3);
                        DrawPoints(years > 30000 ? _shellOldBuf : look == 0 ? _shellCrabBuf : look == 1 ? _shellCasBuf : _shellYoungBuf, _shellN, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade);
                    }
                }
            }
            if (pts.Count > 0)
            {
                if (_snPointBuf == null || _snPointCap < pts.Count) { if (_snPointBuf != null) _snPointBuf.Dispose(); _snPointCap = pts.Count + 64; _snPointBuf = new UploadBuffer(_gpu.Device, (long)_snPointCap * Stride); }
                var arr = pts.ToArray(); _snPointBuf.Write(arr, arr.Length);
                DrawPoints(_snPointBuf, arr.Length, vp, right, up, 1f, 0.0005f, w, h, time, 0, 1f, 2f);
            }
        }
        // the nearest supernova going now (within about a year of its blast), in any galaxy made round you; null for none
        private Sn NearestSupernova()
        {
            Sn best = null; double bd = double.MaxValue;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var c = Supernovae(_galaxies[i], i == _near);
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > 1.2) continue; double days = years * 365.25, end = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving;
                    // the nearest, but a shining one (its face still blazing) far before one on its tail, and one that leaves a neutron star
                    // or a black hole before a white dwarf blown to nothing (the tour had kept landing on white dwarfs a hundred and seventy
                    // days in, their faces shrunk and dimmed: empty space at the stop)
                    double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) * (days > end ? 50 : 1) * (sn.K.Leaves == "nothing" ? 8 : 1);
                    if (d < bd) { bd = d; best = sn; }
                }
            }
            return best;
        }
        // the youngest supernova remnant in the galaxy you are in (a few years to a few thousand old): its shell still bright, the
        // neutron star or black hole born at its middle; the nearest of the young ones
        private Sn YoungRemnant()
        {
            if (_near < 0 || _near >= _galaxies.Count) return null; var c = Supernovae(_galaxies[_near], true); Sn best = null; double bs = double.MaxValue;
            foreach (var sn in c.Events)
            {
                double years = _simYears - sn.T; if (years < 1.2 || years > 3000) continue;
                double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, score = Math.Sqrt(dx * dx + dy * dy + dz * dz) * (1 + years / 300);
                if (score < bs) { bs = score; best = sn; }
            }
            return best;
        }
        private void PickSupernova(Sn sn)
        {
            double years = _simYears - sn.T, days = Math.Max(1, years * 365.25);
            _tKind = 1; _tx = sn.Ux; _ty = sn.Uy; _tz = sn.Uz; _tName = "a supernova"; _tSn = sn;
            // the shots' measure: the glowing face while it shines; once it has thinned (the tail), or for a remnant, the neutron star
            // or black hole at its middle, so the tour flies in to what is there (it had gone to ten times the face's size at its
            // biggest, a thousand AU from a face shrunk to nothing and a remnant not yet made: empty space); a white dwarf blown apart
            // leaves nothing, and its shots stay at its thinning cloud
            double endD = sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving; bool tail = days > endD;
            _tRadius = sn.K.Leaves != "nothing" && (tail || years > 2) ? Units.Km(sn.K.Leaves == "magnetar" || sn.Mass < _snNsMax ? 12 : Physics.HorizonKm(sn.Mass) * 3)
                : years > 2 ? Units.Km(SnShellLy(sn, years) * 9.4607e12)
                : Units.Km(Math.Max(1e8, Math.Max(SnPhotKm(sn, days), SnEjectaKm(sn, days) * 0.5)));   // (the shots framed on the ballooning cloud, not only the face inside it)
        }
        // a click on one: the supernova going now nearest the line under the mouse, within about two degrees
        private bool ClickSupernova(Vec3 dir)
        {
            Sn best = null; double bestAng = 0.035;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double gx = (g.X - _ax) - _ox, gy = (g.Y - _ay) - _oy, gz = (g.Z - _az) - _oz; if (gx * gx + gy * gy + gz * gz > Math.Pow(400 * g.Scale, 2)) continue;
                var c = Supernovae(g, i == _near);
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > 1.5) continue;
                    double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0) continue;
                    double ang = Math.Acos(Math.Max(-1, Math.Min(1, (dx * dir.X + dy * dir.Y + dz * dir.Z) / l))); if (ang < bestAng) { bestAng = ang; best = sn; }
                }
            }
            if (best == null) return false; PickSupernova(best); return true;
        }
        private string SupernovaWords()
        {
            var sn = _tSn; if (sn == null) return "a supernova";
            if (_simYears - sn.T > 2)
            {
                double yrs = _simYears - sn.T;
                return "A " + sn.K.Name.ToLowerInvariant() + " " + Years(yrs) + " ago, mid-process: its shell " + SnShellLy(sn, yrs).ToString("0.#") + " light-years out, moving at " + SnShellKmS(sn, yrs).ToString("N0") + " km/s, " + (yrs < 300 ? "flying free" : "slowed as it sweeps up the gas between the stars") + "; " + (sn.K.Leaves == "nothing" ? "nothing at its middle but the shell" : (sn.K.Leaves == "magnetar" ? "a magnetar" : sn.Mass < _snNsMax ? "a neutron star" : "a black hole") + " at its middle, born in it; the star that died is gone, its worlds with it");
            }
            double days = (_simYears - sn.T) * 365.25, L = SnLum(sn, days), v = sn.K.V; string stage = days < sn.K.Rise ? "still rising" : days < sn.K.Rise + sn.K.Plateau ? "on its plateau" : days < sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? "fading" : "on its long radioactive tail";
            return sn.K.Name + ", " + days.ToString("0") + " days after its blast, " + stage + ": " + (sn.Ia ? "a white dwarf blown apart, " : "the core of a star of " + sn.Mass.ToString("0") + " solar masses collapsed, ") + "shining at " + (L >= 1e6 ? (L / 1e6).ToString("0.#") + " million" : L.ToString("N0")) + " solar luminosities, its matter flung out at " + v.ToString("N0") + " km/s" + (sn.K.Leaves == "nothing" ? "; it leaves nothing but its shell" : sn.K.Leaves == "magnetar" ? "; a magnetar is born at its middle" : sn.Mass < _snNsMax ? "; a neutron star is born at its middle" : "; a black hole is born at its middle");
        }
        private void SupernovaCard(List<string> L, double dist)
        {
            var sn = _tSn; double years = _simYears - sn.T, days = years * 365.25, L0 = SnLum(sn, days), peak = sn.K.Peak, v = sn.K.V;
            if (years > 2)
            {
                // a remnant, mid-process: the shell by its age, what is at its middle, what became of the star and its worlds, what comes next
                L.Add(sn.K.Name + ", " + Years(years) + " ago"); L.Add("id: SN-" + ((long)(sn.T * 1000) & 0xFFFFFF).ToString("X6") + " in " + sn.G.Name);
                L.Add("its shell: " + (SnShellLy(sn, years) * 2).ToString("0.#") + " light-years across, moving at " + SnShellKmS(sn, years).ToString("N0") + " km/s" + (years < _snFreeYears ? " (flying free: it has not yet swept up its own mass of gas)" : " (slowed as it sweeps up the gas between the stars, its radius growing as the two-fifths power of its age)") + (years < 5000 ? "; hot, glowing in hydrogen and oxygen" : "; cooling, faint, blue-green"));
                L.Add(sn.K.Leaves == "nothing" ? "at its middle: nothing; the white dwarf was blown apart entirely" : sn.K.Leaves == "magnetar" ? "at its middle: a magnetar, born in it, lighting the blast from inside as it spins down" : sn.Mass < _snNsMax ? "at its middle: a neutron star, born in it, spinning about " + Math.Min(700, 30 * Math.Sqrt(970 / Math.Max(1, years))).ToString("0") + " times a second; among the bodies round you, picked like any" : "at its middle: a black hole, born in it; among the bodies round you, picked like any");
                L.Add("the star that died: " + (sn.Ia ? "a white dwarf of 1.4 solar masses" : "a star of " + sn.Mass.ToString("0") + " solar masses") + ", gone; its worlds with it: the inner ones vaporised, the outer flung loose");
                L.Add("distance: " + Units.Describe(dist));
                return;
            }
            string stage = days < sn.K.Rise ? "still rising to its peak" : days < sn.K.Rise + sn.K.Plateau ? "on its plateau" : days < sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? "fading, halving every " + sn.K.Halving.ToString("0") + " days" : "on its long radioactive tail (cobalt-56, halving every 77 days)";
            L.Add(sn.K.Name); L.Add("id: SN-" + ((long)(sn.T * 1000) & 0xFFFFFF).ToString("X6") + " in " + sn.G.Name);
            L.Add(sn.Ia ? "a white dwarf of 1.4 solar masses, blown apart entirely" : "a star of " + sn.Mass.ToString("0") + " solar masses, its fuel spent");
            L.Add(days.ToString("0") + " days since its blast, " + stage + "; shining now at " + (L0 >= 1e6 ? (L0 / 1e6).ToString("0.#") + " million" : L0.ToString("N0")) + " solar luminosities (its peak " + (peak / 1e6).ToString("0") + " million: " + (peak >= 2e9 ? "brighter than most galaxies" : "a hundred million stars' light") + ")");
            L.Add("its matter flung out at " + v.ToString("N0") + " km/s (" + (v / 299792.458 * 100).ToString("0.#") + "% of light's speed, the outermost up to a tenth of it): its edge now " + ScaleHud.Words(SnEjectaKm(sn, Math.Max(1, days)) * 2) + " across");
            { double rp = SnPhotKm(sn, days), tk = SnHeatK(sn, days); L.Add(rp > 1e6 ? "its glowing face: " + ScaleHud.Words(rp * 2) + " across, " + tk.ToString("N0") + " K (" + (tk > 9000 ? "blue-white" : tk > 6500 ? "white" : tk > 5000 ? "yellow-white" : "orange") + ")" + (days > sn.K.Rise + sn.K.Plateau + 3 * sn.K.Halving ? ", drawing back in as the fireball thins to a cloud" : "") : "the fireball has thinned to a cloud: its remnant shows at the middle"); }
            L.Add("its worlds: the inner ones vaporised, the outer flung loose; its neighbours scorched");
            L.Add(sn.K.Leaves == "nothing" ? "what it leaves: nothing but its shell, seen for a hundred thousand years as it sweeps up the gas between the stars" : sn.K.Leaves == "magnetar" ? "what it leaves: a magnetar at its middle, a neutron star with a field a thousand times a pulsar's, spinning down and lighting the blast from inside; its shell seen for a hundred thousand years" : sn.Mass < _snNsMax ? "what it leaves: a neutron star at its middle, revealed as the ejecta thin, spinning tens of times a second; its shell seen for a hundred thousand years" : "what it leaves: a black hole at its middle, revealed as the ejecta thin; its shell seen for a hundred thousand years");
            L.Add("this galaxy: about " + (SnRate(sn.G) * 100).ToString("0.#") + " supernovae a century");
            L.Add("distance: " + Units.Describe(dist));
        }
        // a supernova whose shock has reached here: the nearest, and what it did (by how near: within a couple of light-years the worlds'
        // air is stripped and their faces scorched; within ten the air thins and its ozone goes, life on the exposed sides dies; out to
        // about thirty the sky is lit as by a second star for months and the particles harm life)
        private string ScorchWords(double ux, double uy, double uz)
        {
            if (_near < 0 || _near >= _galaxies.Count) return null; var c = Supernovae(_galaxies[_near], true); string best = null; double bd = double.MaxValue;
            foreach (var sn in c.Events)
            {
                double years = _simYears - sn.T; if (years < 0 || years > _snRemnantYears) continue;
                double dx = sn.Ux - ux, dy = sn.Uy - uy, dz = sn.Uz - uz, dLy = Math.Sqrt(dx * dx + dy * dy + dz * dz) * Units.LightYearsPerUnit;
                if (dLy > 30 || dLy > SnShellLy(sn, years) || dLy >= bd || dLy < 1e-6) continue;
                bd = dLy; best = "a " + sn.K.Name.ToLowerInvariant() + " " + dLy.ToString("0.#") + " light-years away, " + Years(years) + " ago: its shock has passed here" + (dLy < 2 ? "; the worlds' air stripped and their faces scorched, their life gone" : dLy < 10 ? "; the air thinned and its ozone gone, life on the exposed sides killed" : "; the sky lit as by a second star for months, its particles harming life");
            }
            return best;
        }
        private HoleParts PartsFor(Place pl, double spin)
        {
            HoleParts hp; string id = (pl != null ? pl.Id : "") + ":" + Math.Round(spin, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); if (_holeParts.TryGetValue(id, out hp)) return hp;
            // its disc's inner edge by its spin: the innermost stable orbit comes in from three horizon radii at rest (content/physics.unt @law frame-dragging)
            double lo = Math.Min(pl != null ? pl.DiscLo : 3, Math.Max(0.6, IscoRs(spin))), hi = pl != null ? pl.DiscHi : 60, secs = pl != null ? pl.OrbitSeconds : 8;
            var rng = new Rng(0xB1AC4UL); var disc = new List<StarVertex>(); var jets = new List<StarVertex>(); var swarm = new List<StarVertex>(); var stream = new List<StarVertex>();
            for (int k = 0; k < 28000; k++)
            {
                // the disc's gas from the innermost stable orbit out to its rim, more of it close in; hottest and brightest inside
                // (blue-white), cooling outward (yellow, orange, red), thin, with denser streams through it (sheared into spirals
                // by the rotation, since each point goes round at its own rate)
                double r = lo * Math.Pow(hi / lo, Math.Pow(rng.NextDouble(), 1.6)), a = rng.NextDouble() * Math.PI * 2, t = Math.Pow(lo / r, 0.75);
                double dens = 0.82 + 0.18 * Math.Sin(Math.Log(r / lo) * 21 + rng.NextDouble() * 0.6) + 0.12 * (rng.NextDouble() - 0.5);   // fine turbulent bands, never arms (the Kepler shear on the card is all the spiral there is)
                float br = (float)(Math.Min(1, 1.4 * Math.Pow(lo / r, 1.1) + 0.06) * dens);
                float cr = (float)(1.0 * br), cg = (float)((0.45 + 0.55 * t) * br), cb = (float)((0.15 + 0.85 * t * t) * br);
                disc.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)(rng.Gaussian() * r * 0.02), Z = (float)(Math.Sin(a) * r), Size = (float)(0.14 + r * 0.012), R = cr, G = cg, B = cb, A = 1 });
            }
            for (int k = 0; k < 2400; k++)
            {
                // two beams from the poles, narrow, fading as they go, blue-violet
                double y = 3 + Math.Pow(rng.NextDouble(), 1.8) * 600, w = 0.6 + y * 0.03, a = rng.NextDouble() * Math.PI * 2, f = Math.Exp(-y / 300);
                float br = (float)(0.25 + 0.75 * f); int side = (k & 1) == 0 ? 1 : -1;
                jets.Add(new StarVertex { X = (float)(Math.Cos(a) * w * rng.NextDouble()), Y = (float)(side * y), Z = (float)(Math.Sin(a) * w * rng.NextDouble()), Size = (float)(1 + y * 0.01), R = 0.55f * br, G = 0.6f * br, B = 1f * br, A = 1 });
            }
            int nsw = pl != null ? pl.Swarm : 0;
            for (int k = 0; k < nsw; k++)
            {
                // a star of the swarm round the hole: its ellipse (size, shape, timing) and its orientation in its record, its path
                // worked out on the card; in the colours of stars
                double sa = pl.SwarmLo * Math.Pow(pl.SwarmHi / pl.SwarmLo, rng.NextDouble()), ecc = 0.2 + 0.7 * rng.NextDouble(), ph = rng.NextDouble() * Math.PI * 2;
                uint seed = (uint)rng.Below(0x7F000000); float heat = (float)rng.NextDouble();
                swarm.Add(new StarVertex { X = (float)sa, Y = (float)ecc, Z = (float)ph, Size = BitConverter.ToSingle(BitConverter.GetBytes(seed), 0), R = 1f, G = 0.75f + 0.25f * heat, B = 0.55f + 0.45f * heat, A = 1 });
            }
            for (int k = 0; k < 1800; k++)
            {
                // the torn star: each point a share of the way along its stream (denser at the head, the star's remains), with its
                // own small offset across the stream; its brightness in R
                double u = Math.Pow(rng.NextDouble(), 1.4);
                stream.Add(new StarVertex { X = (float)u, Y = (float)(rng.Gaussian() * 0.22), Z = (float)(rng.Gaussian() * 0.22), Size = (float)(0.25 + 0.4 * rng.NextDouble()), R = (float)(0.6 + 0.4 * rng.NextDouble()), G = 1, B = 1, A = 1 });
            }
            hp = new HoleParts { DiscN = disc.Count, JetsN = jets.Count, SwarmN = swarm.Count, StreamN = stream.Count, K = (float)(2 * Math.PI * Math.Pow(lo, 1.5) / secs) };
            hp.Disc = new UploadBuffer(_gpu.Device, (long)disc.Count * Stride); hp.Disc.Write(disc.ToArray(), disc.Count);
            hp.Jets = new UploadBuffer(_gpu.Device, (long)jets.Count * Stride); hp.Jets.Write(jets.ToArray(), jets.Count);
            hp.SwarmRecs = swarm.ToArray();   // (kept here too: each star of the swarm is clicked by working out where the card has drawn it)
            if (swarm.Count > 0) { hp.Swarm = new UploadBuffer(_gpu.Device, (long)swarm.Count * Stride); hp.Swarm.Write(swarm.ToArray(), swarm.Count); }
            hp.Stream = new UploadBuffer(_gpu.Device, (long)stream.Count * Stride); hp.Stream.Write(stream.ToArray(), stream.Count);
            _holeParts[id] = hp; return hp;
        }
        private void DrawBlackHoles(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (rs * 600 / Math.Max(1e-30, d) * h < 2) continue;                      // (too small on screen to show yet)
                var hp = PartsFor(ex.Pl, ex.Spin);
                // its frame: tilted its own way, its horizon's radius 1; nothing turns as a plate: every point moves on its own
                // orbit on the card (the hole's own turn is only a phase of the time)
                float sc = (float)rs, ct = (float)Math.Cos(ex.Tilt), st = (float)Math.Sin(ex.Tilt);
                var tilt = Mat4.Identity; tilt.M22 = ct; tilt.M23 = st; tilt.M32 = -st; tilt.M33 = ct;
                var scale = Mat4.Identity; scale.M11 = sc; scale.M22 = sc; scale.M33 = sc;
                var place = tilt * scale * Mat4.Translation((float)dx, (float)dy, (float)dz);
                // where the camera is in that frame (for the beaming: which side of the disc comes toward you)
                double cx = -dx / rs, cy0 = -dy / rs, cz0 = -dz / rs; float hx = (float)cx, hy = (float)(cy0 * ct + cz0 * st), hz = (float)(-cy0 * st + cz0 * ct);
                double tt = time + ex.Turn * 10;
                // its gravity well: the dip of space round it (Flamm's paraboloid: the depth 2 root(rs (r - rs)), the real shape of space's
                // stretch round a mass), rings and spokes from the horizon out to forty radii, and at the horizon a sharp step straight down to
                // a point: the singularity. Round every hole, feeding or quiet; the disc, swarm and jets only round a feeding one
                if (_wellBuf == null) MakeWell();
                // (no well drawn in space: the shadow, the disc and the bent light are what a hole shows from outside; the well is the fall's own, inside)
                if (ex.Feeding > 0) DrawPoints(hp.Disc, hp.DiscN, place * vp, right, up, 1f, 0.0012f / sc, w, h, tt, 0.02f / sc, 1f, 2f, hp.K, 1, hx, hy, hz);
                if (ex.Feeding == 2) DrawPoints(hp.Jets, hp.JetsN, place * vp, right, up, 1f, 0.0012f / sc, w, h, time, 0.02f / sc, 1f, 2f);
                if (ex.Feeding > 0 && hp.SwarmN > 0) DrawPoints(hp.Swarm, hp.SwarmN, place * vp, right, up, 1f, 0.0018f / sc, w, h, tt, 0.03f / sc, 1f, 2f, hp.K, 2, hx, hy, hz);
                if (ex.Eating) for (int q = 0; q < Math.Max(1, ex.EatN); q++)
                {
                    // each star being eaten: its own stream, turned its own way round the axis and at its own moment (the stars came from different directions, at different times)
                    double ra = q * Math.PI * 2 / Math.Max(1, ex.EatN) + ex.Turn * 3; float cq = (float)Math.Cos(ra), sq = (float)Math.Sin(ra);
                    var roty = Mat4.Identity; roty.M11 = cq; roty.M13 = sq; roty.M31 = -sq; roty.M33 = cq;
                    float hx2 = hx * cq - hz * sq, hz2 = hx * sq + hz * cq;
                    DrawPoints(hp.Stream, hp.StreamN, roty * place * vp, right, up, 1f, 0.0012f / sc, w, h, tt + q * 37, 0.02f / sc, 1f, 2f, hp.K, 3, hx2, hy, hz2);
                }
            }
        }
        // -- asteroids and comets close in: a lumpy cratered rock lit from its star's side, made once (radius about 1) and drawn for
        // each, turned to its star and spinning on that line (so its lit side stays lit); a comet's dark nucleus in its coma, with
        // its ion tail straight away from the star and its wider curved dust tail --
        private UploadBuffer _cometBuf; private int _cometN;
        private bool ShapeShown(Extra ex, int h)
        {
            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double reach = ex.Shape == "comet" ? Units.Km(ex.TailKm) / Math.Max(1e-30, ex.R) : 1;   // (a comet shows by its tail, millions of km long: across its whole system)
            return ex.R * reach / Math.Max(1e-30, d) * h > 3;
        }
        private static void RockPoints(List<StarVertex> l, Rng rng, int n, float r0, float g0, float b0, double albedo, double lump = 1)
        {
            var bumps = new double[12][]; for (int q = 0; q < 12; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); bumps[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), (rng.NextDouble() - 0.5) * 0.5 * lump, 1 + rng.NextDouble() * 2.5 }; }
            var craters = new double[30][]; for (int q = 0; q < 30; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); craters[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), Math.Cos(0.08 + rng.NextDouble() * 0.3) }; }
            for (int k = 0; k < n; k++)
            {
                double u = (k + 0.5) / n * 2 - 1, a = k * 2.399963229728653, rr = Math.Sqrt(1 - u * u), x = rr * Math.Cos(a), y = u, z = rr * Math.Sin(a);
                double rad = 1; foreach (var bq in bumps) rad += bq[3] * Math.Pow(Math.Max(0, x * bq[0] + y * bq[1] + z * bq[2]), bq[4]);
                double shade = 1;
                foreach (var cq in craters) { double c = x * cq[0] + y * cq[1] + z * cq[2]; if (c > cq[3]) { double t = (c - cq[3]) / (1 - cq[3]); rad -= 0.05 * (1 - t); shade *= 0.75 + 0.25 * t; } else if (c > cq[3] - 0.02) shade *= 1.15; }
                double sx = x * (1 + 0.5 * lump), sy = y * (1 - 0.15 * lump), sz = z;          // (elongated, as small bodies are: too little gravity to pull them round)
                double lit = Math.Max(0, x * 0.9 + 0.1) * shade * albedo + 0.015;              // its star is along +x
                l.Add(new StarVertex { X = (float)(sx * rad), Y = (float)(sy * rad), Z = (float)(sz * rad), Size = 0.045f, R = (float)(r0 * lit), G = (float)(g0 * lit), B = (float)(b0 * lit), A = 1 });
            }
        }
        // a solid body: a sphere of triangles (an icosahedron cut four times), pushed in and out by broad lumps and pitted by craters,
        // drawn as lit stone with flat facets (never points of light); its star along +x
        private UploadBuffer _rockSolid, _roundSolid, _nucSolid; private int _rockSolidN, _roundSolidN, _nucSolidN; private Func<double[], double[]> _rockShape, _roundShape, _nucShape; private const int RockBands = 4, RockSeeds = 3; private static readonly double[] RockLumps = { 1.3, 0.9, 0.55, 0.25 }; private Func<double[], double[]>[] _rockShapes; private UploadBuffer[] _rockSolids; private int[] _rockSolidNs; private double _lastDrawTime; private int _diveLogTick = -1; private double[] _tSpot;   // _tSpot: a spot picked on the small body you are at (unit, from its middle): the zoom flies to it, as to a spot of a world's land
        // a small body's shape: its radius and shade along any direction from its middle (its bumps, craters, stretch and grit), the
        // one function the mesh is built from and a dive lands on, so the ground you stand on is the stone that is drawn
        private static Func<double[], double[]> RockShape(Rng rng, double lump)
        {
            var bumps = new double[14][]; for (int q = 0; q < 14; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); bumps[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), (rng.NextDouble() - 0.5) * 0.55 * lump, 1 + rng.NextDouble() * 3 }; }
            var craters = new double[40][]; for (int q = 0; q < 40; q++) { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u); craters[q] = new[] { rr * Math.Cos(a), u, rr * Math.Sin(a), Math.Cos(0.05 + rng.NextDouble() * 0.28), rng.NextDouble() }; }
            Func<double[], double[]> place = v =>
            {
                double x = v[0], y = v[1], z = v[2], rad = 1, shade = 1;
                foreach (var bq in bumps) rad += bq[3] * Math.Pow(Math.Max(0, x * bq[0] + y * bq[1] + z * bq[2]), bq[4]);
                foreach (var cq in craters)
                {
                    double c = x * cq[0] + y * cq[1] + z * cq[2]; if (c < cq[3] - 0.03) continue;
                    double t = (c - cq[3]) / (1 - cq[3]);
                    if (t > 0) { rad -= 0.06 * (1 - cq[3]) * 12 * Math.Sqrt(Math.Max(0, 1 - (1 - t) * (1 - t))) * 0.25; shade *= 0.8 + 0.2 * t; } else rad += 0.008;   // a bowl, and its raised rim
                }
                double grit = 0.94 + 0.12 * (Math.Abs(Math.Sin(x * 91.7 + y * 37.3 + z * 63.1) * 4375.5) % 1);
                return new[] { x * (1 + 0.5 * lump) * rad, y * (1 - 0.15 * lump) * rad, z * rad, shade * grit };
            };
            return place;
        }
        private static List<StarVertex> SolidRock(Rng rng, double lump, float r0, float g0, float b0)
        {
            var place = RockShape(rng, lump);
            // an icosahedron, each face cut in four, four times over, each corner put out to the sphere
            double t0 = (1 + Math.Sqrt(5)) / 2; var vs = new List<double[]>();
            double[][] iv = { new[] { -1, t0, 0 }, new[] { 1, t0, 0 }, new[] { -1, -t0, 0 }, new[] { 1, -t0, 0 }, new[] { 0, -1, t0 }, new[] { 0, 1, t0 }, new[] { 0, -1, -t0 }, new[] { 0, 1, -t0 }, new[] { t0, 0, -1 }, new[] { t0, 0, 1 }, new[] { -t0, 0, -1 }, new[] { -t0, 0, 1 } };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            Func<double[], double[]> unit = v => { double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); return new[] { v[0] / l, v[1] / l, v[2] / l }; };
            var tris = new List<double[][]>(); for (int i = 0; i < f.Length; i += 3) tris.Add(new[] { unit(iv[f[i]]), unit(iv[f[i + 1]]), unit(iv[f[i + 2]]) });
            for (int lv = 0; lv < 4; lv++)
            {
                var nt = new List<double[][]>();
                foreach (var tr in tris)
                {
                    Func<double[], double[], double[]> mid = (a, b) => unit(new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2 });
                    double[] ab = mid(tr[0], tr[1]), bc = mid(tr[1], tr[2]), ca = mid(tr[2], tr[0]);
                    nt.Add(new[] { tr[0], ab, ca }); nt.Add(new[] { ab, tr[1], bc }); nt.Add(new[] { ca, bc, tr[2] }); nt.Add(new[] { ab, bc, ca });
                }
                tris = nt;
            }
            var l2 = new List<StarVertex>(tris.Count * 3);
            foreach (var tr in tris)
                foreach (var c in tr)
                {
                    var p = place(c); float sh = (float)p[3];
                    l2.Add(new StarVertex { X = (float)p[0], Y = (float)p[1], Z = (float)p[2], Size = 0, R = r0 * sh, G = g0 * sh, B = b0 * sh, A = 1 });
                }
            return l2;
        }
        private void MakeSmallBodies()
        {
            {
                _roundShape = RockShape(new Rng(0x60BE5UL), 0.08); _nucShape = RockShape(new Rng(0xC0CE7UL), 0.9);   // (the same shapes the meshes are built from, for the dive)
                // every rock its own shape: four bands of lumpiness (the smallest the most irregular, the largest near round, as gravity
                // pulls a body round as it grows) by three seeds each; a body's by its size and its own seed, so no two beside each other are alike
                _rockShapes = new Func<double[], double[]>[RockBands * RockSeeds]; _rockSolids = new UploadBuffer[RockBands * RockSeeds]; _rockSolidNs = new int[RockBands * RockSeeds];
                for (int bq = 0; bq < RockBands; bq++) for (int sq = 0; sq < RockSeeds; sq++)
                {
                    int ix = bq * RockSeeds + sq; ulong seed = 0x5A57E1UL + (ulong)(bq * 7 + sq * 131);
                    _rockShapes[ix] = RockShape(new Rng(seed), RockLumps[bq]);
                    var rv = SolidRock(new Rng(seed), RockLumps[bq], 0.52f, 0.47f, 0.42f); _rockSolidNs[ix] = rv.Count; _rockSolids[ix] = new UploadBuffer(_gpu.Device, (long)rv.Count * Stride); _rockSolids[ix].Write(rv.ToArray(), rv.Count);
                }
                _rockSolid = _rockSolids[0]; _rockSolidN = _rockSolidNs[0]; _rockShape = _rockShapes[0];   // (the fallback rock is the first of the twelve, never a mesh of its own)
                var b = SolidRock(new Rng(0x60BE5UL), 0.08, 0.55f, 0.52f, 0.5f); _roundSolidN = b.Count; _roundSolid = new UploadBuffer(_gpu.Device, (long)b.Count * Stride); _roundSolid.Write(b.ToArray(), b.Count);
                var c = SolidRock(new Rng(0xC0CE7UL), 0.9, 0.42f, 0.40f, 0.38f); _nucSolidN = c.Count; _nucSolid = new UploadBuffer(_gpu.Device, (long)c.Count * Stride); _nucSolid.Write(c.ToArray(), c.Count);   // a comet's nucleus: among the darkest bodies there are (albedo 0.04), shown as a camera exposing for it sees it, mid-grey, as Rosetta saw 67P (at its true darkness it was black against the stars) things known
            }
            var cm = new List<StarVertex>(); var rng = new Rng(0xC0CE7UL);   // (the coma and the tails round the solid nucleus)
            for (int k = 0; k < 9000; k++)
            {
                // the coma: gas and dust round the nucleus, densest close in, out to about 2000 times its size
                double rr = Math.Exp(Math.Log(0.0007) + rng.NextDouble() * Math.Log(1 / 0.0007)), u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, s2 = Math.Sqrt(1 - u * u);   // (in the coma's own size: 1 its edge)
                float br = (float)(0.5 * Math.Pow(0.0007 / rr, 0.35));
                cm.Add(new StarVertex { X = (float)(rr * s2 * Math.Cos(a) + rr * 0.3), Y = (float)(rr * u), Z = (float)(rr * s2 * Math.Sin(a)), Size = (float)(rr * 0.35), R = 0.55f * br, G = 0.75f * br, B = 0.8f * br, A = 1 });
            }
            for (int k = 0; k < 9000; k++)
            {
                // the ion tail: straight away from the star (along -x), thin, blue; the dust tail: wider, curved, yellow-white
                bool ion = (k & 1) == 0; double t = Math.Pow(rng.NextDouble(), 0.7), w = (ion ? 0.02 : 0.08) * t + 0.0033, a = rng.NextDouble() * Math.PI * 2, q = rng.Gaussian() * 0.4;   // (in the tail's own length: 1 its end)
                float br = (float)((ion ? 0.35 : 0.3) * Math.Exp(-t / 0.5));
                double bend = ion ? 0 : t * t * 0.25;
                cm.Add(new StarVertex { X = (float)(-t), Y = (float)(Math.Cos(a) * w * q), Z = (float)(Math.Sin(a) * w * q + bend), Size = (float)(w * 0.8), R = (ion ? 0.35f : 0.9f) * br, G = (ion ? 0.55f : 0.85f) * br, B = (ion ? 1f : 0.7f) * br, A = 1 });
            }
            // the coma (the first 9,000) and the tails (the rest) each on its own, each drawn to its own size
            _comaN = 9000; var tl = cm.GetRange(_comaN, cm.Count - _comaN); _tailN = tl.Count;
            _cometN = _comaN; _cometBuf = new UploadBuffer(_gpu.Device, (long)_comaN * Stride); _cometBuf.Write(cm.GetRange(0, _comaN).ToArray(), _comaN);
            _tailBuf = new UploadBuffer(_gpu.Device, (long)_tailN * Stride); _tailBuf.Write(tl.ToArray(), _tailN);
        }
    }
}
