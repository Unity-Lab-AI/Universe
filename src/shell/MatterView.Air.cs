// Universe Â· shell Â· the matter view, its part: air (one class in several files: src/shell/MatterView.cs is its start)
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        // ── an air: going into a world with no ground (a giant's clouds), its own gases at their real density ──
        private readonly Dictionary<string, string[]> _gasAtoms = new Dictionary<string, string[]>(); private double _airPerCc = 2.5e19;
        /// <summary>Into an air (its make-up as the content gives it, e.g. "hydrogen 90%, helium 10%, methane 0.3%"): each
        /// molecule's atoms in their shares, at about one bar.</summary>
        private string _airText = "";
        // the air over the ground: its molecules (from the world's own air make-up), about 3.4 nm apart at one bar, jostling
        private List<KeyValuePair<string[], double>> _airMols; private double _airAbove;
        /// <summary>The air over this ground (the world's air make-up, as content gives it; none for a world without air).</summary>
        public void SetAirAbove(string air, double perCc = 0)
        { Announce("SetAirAbove", false, new object[] { air, perCc }); _recDepth++; try {
            _airMols = null; if (string.IsNullOrEmpty(air)) return;
            var l = new List<KeyValuePair<string[], double>>(); double total = 0;
            foreach (var bit in air.Split(','))
            {
                string t = bit.Trim(); int pc = t.IndexOf('%'), sp = pc > 0 ? t.LastIndexOf(' ', pc) : -1; if (sp < 0) continue;
                double share; if (!double.TryParse(t.Substring(sp + 1, pc - sp - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out share)) continue;
                string name = t.Substring(0, sp).Trim().ToLowerInvariant(); if (name.EndsWith(" vapour")) name = name.Substring(0, name.Length - 7);
                string[] atoms; if (_gasAtoms.TryGetValue(name, out atoms)) { l.Add(new KeyValuePair<string[], double>(atoms, share)); total += share; }
            }
            if (total <= 0) return;
            for (int i = 0; i < l.Count; i++) l[i] = new KeyValuePair<string[], double>(l[i].Key, l[i].Value / total);
            _airMols = l; _airAbove = 1e-5 / Math.Pow(perCc > 0 ? perCc : _airPerCc, 1 / 3.0);   // (a liquid above, the sea over its bed: its own packing)
        } finally { _recDepth--; } }
        private void AirAbove(List<StarVertex> glow, double s)
        {
            if (_airMols == null || _space || s > _airAbove * 14) return;
            double a = _airAbove; long n = (long)Math.Min(14, Math.Ceiling(2.2 * s / a)), i0 = (long)Math.Round(_fx / a), j0 = (long)Math.Round(_fz / a);
            float f = (float)Fade(s, a * 14, a * 6);
            for (long i = i0 - n; i <= i0 + n; i++)
                for (long j = j0 - n; j <= j0 + n; j++)
                    for (long k = 0; k <= n; k++)
                    {
                        double u = Hash(i, j, k + 300, _seed), acc = 0; string[] mol = _airMols[0].Key;
                        foreach (var kv in _airMols) { acc += kv.Value; if (u < acc) { mol = kv.Key; break; } }
                        double x = (i + Hash(i, j, k + 301, _seed)) * a + Math.Sin(_clock * 3.7 + i) * a * 0.1, z = (j + Hash(i, j, k + 302, _seed)) * a + Math.Cos(_clock * 3.1 + j) * a * 0.1;
                        double y = (k + Hash(i, j, k + 303, _seed)) * a + a * 0.3 - _yOff;     // over the ground, not in it
                        for (int q = 0; q < mol.Length; q++)
                        {
                            var el = El(mol[q]); if (el == null) continue; double off = (q - (mol.Length - 1) * 0.5) * 1.1e-13;
                            Add(glow, x + off, y, z, s * 0.0024, el.R * 0.7f * f, el.G * 0.7f * f, el.B * 0.7f * f, s);
                        }
                    }
        }
        /// <summary>How far the view is below light's wavelength (0 none, 1 wholly: no colour this small, greys as an electron
        /// microscope sees; from 700 nanometres across the view down to 400).</summary>
        private float Greyness { get { return (float)Math.Max(0, Math.Min(1, Math.Log10(7e-10 / Math.Max(1e-30, _scale * 2)) / Math.Log10(7.0 / 4.0))); } }
        private int _viewH = 1080;                 // the view's height in pixels: what spans a pixel is drawn, what does not is not
        private bool _lifeHere;
        /// <summary>Whether this ground's world is alive (microbes on its grains).</summary>
        public void SetLife(bool alive) { Announce("SetLife", false, new object[] { alive }); _recDepth++; try { _lifeHere = alive; } finally { _recDepth--; } }
        private bool _plasma; private string _plasmaMake = "hydrogen 91.2%, helium 8.7%"; private double _plasmaPerCc = 1e17;
        /// <summary>Into a star's surface: its plasma (content/physics.unt star-plasma): bare nuclei of its elements in their
        /// shares, at its density, with free electrons darting between them (too hot for any atom to keep its electrons).</summary>
        public void SetPlasma(ulong spot) { SetPlasma(spot, _plasmaMake); }
        /// <summary>A star's plasma, of its own make-up ("hydrogen 91.2%, helium 8.7%, ...", by count of atoms).</summary>
        public void SetPlasma(ulong spot, string makeup) { SetPlasma(spot, makeup, 0, null, 0); }
        private string _plasmaWhere; private double _plasmaTempK;
        /// <summary>A star's plasma at a layer: its make-up there, its density (g/cc: the nuclei packed accordingly), the layer's name and its heat.</summary>
        public void SetPlasma(ulong spot, string makeup, double densityGcc, string where, double tempK)
        { Announce("SetPlasma", true, new object[] { spot, makeup, densityGcc, where, tempK }); _recDepth++; try { Generation++;
            var parts = new List<SpaceGen.Part>(); double total = 0;
            foreach (var bit in (makeup ?? _plasmaMake).Split(','))
            {
                string t = bit.Trim(); int pc = t.IndexOf('%'), sp = pc > 0 ? t.LastIndexOf(' ', pc) : -1; if (sp < 0) continue;
                double share; if (!double.TryParse(t.Substring(sp + 1, pc - sp - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out share)) continue;
                string name = t.Substring(0, sp).Trim();
                foreach (var el in _elements.Values) if (string.Equals(el.Name, name, StringComparison.OrdinalIgnoreCase)) { parts.Add(new SpaceGen.Part(name, el.Symbol, share, 1, 1, 1)); total += share; break; }
            }
            if (total <= 0) { parts.Add(new SpaceGen.Part("hydrogen", "H", 1, 1, 1, 1)); total = 1; }
            foreach (var q in parts) q.Share /= total;
            SetSpace(11, spot, 0, parts.ToArray()); _plasma = true; _plasmaWhere = where; _plasmaTempK = tempK; if (tempK > 0) TempK = tempK;
            double perCc = _plasmaPerCc;
            if (densityGcc > 0) { double meanA = 0; foreach (var q in parts) { var el = El(q.El); meanA += q.Share * (el != null ? el.A : 1); } perCc = densityGcc * 6.022e23 / Math.Max(1, meanA); }   // nuclei a cubic centimetre, from the density and the mean mass of them
            _gasSpacing = 1e-5 / Math.Pow(perCc, 1 / 3.0);
        } finally { _recDepth--; } }
        public bool InSpace { get { return _space; } }
        /// <summary>Goes up by one every time the matter is set afresh (a ground, a gas, a plasma, a hole, a crust): what was made for one dive is known not to be this one's.</summary>
        public int Generation;
        public int SpaceKind { get { return _space ? _spaceKind : 0; } }   // (none at all on a body's ground: a hole's kind had outlived the fall, and the well's grid, its black and its clock had followed you onto every rock after)
        /// <summary>A neutron star's crust: iron nuclei packed in a lattice (about a hundred million grams a cubic centimetre: the nuclei
        /// a hundredth of an atom's width apart), electrons racing between them; the same drawing as a star's plasma, at that spacing.</summary>
        public void SetNeutronCrust(ulong spot)
        { Announce("SetNeutronCrust", true, new object[] { spot }); _recDepth++; try { Generation++;
            SetPlasma(spot, "iron 100%"); _spaceKind = 13; _gasSpacing = 1e-15;
        } finally { _recDepth--; } }
        private double _holeRs, _holeMass, _fallTau, _fallRate = 1, _fallSpeed;   // the fall's own clock: your proper time since the horizon, how fast it is shown against real time, your speed inward
        public void SetFallClock(double tau, double rate, double speedC) { _fallTau = tau; _fallRate = rate; _fallSpeed = speedC; }
        private double _fallShare;
        /// <summary>The way in, 0 at the horizon to 1 at the point: the fall's own measure (the view is held at the horizon's scale till the walls meet).</summary>
        public void SetFallShare(double share) { _fallShare = share; }
        public double HoleRs { get { return _holeRs; } }
        /// <summary>Inside a black hole's horizon: the fall, pitch black (no light from anything here reaches anything else), down to
        /// the singularity at the middle, where the zoom finds quarks pressed closer than anything, as far as the laws we have go.</summary>
        public void SetHole(ulong spot, double rsKm, double massSol) { SetHole(spot, rsKm, massSol, 0, 3e6 * 9.4607e12); }
        /// <summary>The fall's own measure: tenfolds from the horizon to the singularity, the same for every hole.</summary>
        public const double FallTenfolds = 40;
        private double _wayInKm = 3e6 * 9.4607e12;   // the way in from the horizon to the singularity (content/physics.unt @law hole-inside)
        private double _holeSpin;
        /// <summary>The hole's spin, a share of the most it could have: frame dragging twists the fall's funnel (content/physics.unt @law frame-dragging).</summary>
        public double HoleSpin { get { return _holeSpin; } }
        public void SetHole(ulong spot, double rsKm, double massSol, double spin, double wayInKm)
        { Announce("SetHole", true, new object[] { spot, rsKm, massSol, spin, wayInKm }); _recDepth++; try { Generation++;
            SetSpace(12, spot, 0, null); _holeRs = rsKm; _holeMass = massSol; _holeSpin = spin; _wayInKm = wayInKm; _gasSpacing = 1e-40;
        } finally { _recDepth--; } }
        // the singularity: its whole mass in a point with no size at all, as far as the laws we have can say; drawn, once the zoom
        // is down at the quarks' size, as quarks pressed into a ball a third of the view wide at every size on down (a point: however
        // far in you go it is the same), all three colour charges, picked like any quark; above that size, nothing: the fall is black
        private void Singularity(List<StarVertex> glow, double s, double time)
        {
            if (s > Nucleon * 8) return;
            float[][] colour = { new[] { 1f, 0.3f, 0.3f }, new[] { 0.3f, 1f, 0.35f }, new[] { 0.4f, 0.5f, 1f } };
            // the singularity: quarks beyond counting crammed into no space, one seething mass of colour charge a third of the view
            // wide at every size on down, never dots to be counted (twelve hundred separate glows had stood for it, each one countable):
            // a few hundred soft glows wide enough to overlap into one mass (never thousands: the card is not to be fried for it),
            // densest at the middle, drawn afresh at every scale so the mass is the same however far in you go, and a white heart
            // where the density runs away
            double R = s * 0.32;
            for (int i = 0; i < 500; i++)
            {
                double u = Hash(i, 1, 3, _seed), v = Hash(i, 2, 3, _seed) * 2 - 1, a = Hash(i, 3, 3, _seed) * Math.PI * 2 + time * (0.02 + 0.01 * (i % 7)), r = R * Math.Pow(u, 0.5), sq = Math.Sqrt(Math.Max(0, 1 - v * v));
                double x = r * sq * Math.Cos(a), y = r * v, z = r * sq * Math.Sin(a);
                var c = colour[i % 3]; float f = (float)(0.35 + 0.25 * Math.Sin(time * 0.6 + i)) * (float)(0.5 + 0.5 * (1 - u));
                Add(glow, x, y, z, s * 0.045, c[0] * f * 0.6f, c[1] * f * 0.6f, c[2] * f * 0.6f, s);
                if (i < 240) { int nb = _bodies.Count; AddBody(4, i, i % 2, x, y, z, Math.Max(s * 0.01, Nucleon * 0.05), s); if (_bodies.Count > nb) { _bodies[nb].Sym = i % 2 == 1 ? "up" : "down"; _bodies[nb].Key = 1; } }
            }
            Add(glow, 0, 0, 0, s * 0.10, 1f, 0.98f, 0.95f, s); Add(glow, 0, 0, 0, s * 0.2, 0.6f, 0.55f, 0.6f, s);
        }
        private static string WayWords(double km) { double ly = km / 9.4607e12; return ly >= 1e9 ? (ly / 1e9).ToString("0.#") + " billion light-years" : ly >= 1e6 ? (ly / 1e6).ToString("0.#") + " million light-years" : ly >= 1 ? ly.ToString(ly >= 100 ? "N0" : "0.#") + " light-years" : ScaleHud.Words(km); }
        private static string Secs(double t)
        {
            if (t < 1e-3) return (t * 1e6).ToString("0.#") + " microseconds";
            if (t < 1) return (t * 1e3).ToString("0.#") + " milliseconds";
            if (t < 90) return t.ToString("0.#") + " seconds";
            if (t < 5400) return (t / 60).ToString("0.#") + " minutes";
            return (t / 3600).ToString("0.#") + " hours";
        }
        /// <summary>What a ground's mix is made of, in words for a card: its minerals with their shares, the elements they hold (by count
        /// of atoms) and the traces down to parts per billion (from every mineral's real trace chemistry, content/minerals.unt).</summary>
        /// <summary>A body's own make-up: its kind's mix with each mineral's share scattered by the body's seed (content/minerals.unt
        /// vary), registered under its own id for the card, the grains and the dive alike; the kind's id back when its mix does not vary.</summary>
        public string VaryMix(string mixId, ulong seed)
        {
            double vary; List<KeyValuePair<Mineral, double>> b;
            if (mixId == null || !_mixVary.TryGetValue(mixId, out vary) || vary <= 0 || !_mixes.TryGetValue(mixId, out b)) return mixId;
            var gm = Universe.Gen.GrainField.Vary(mixId, seed, vary); if (gm == null || gm.Id == mixId) return mixId;
            if (!_mixes.ContainsKey(gm.Id))
            {
                var list = new List<KeyValuePair<Mineral, double>>();
                foreach (var kv in b) { int q = Array.IndexOf(gm.Minerals, kv.Key.Id); list.Add(new KeyValuePair<Mineral, double>(kv.Key, q >= 0 ? gm.Share[q] : kv.Value)); }
                _mixes[gm.Id] = list; double gk; if (_grainKm.TryGetValue(mixId, out gk)) _grainKm[gm.Id] = gk;
            }
            return gm.Id;
        }
        public string[] MakeUpWords(string mixId)
        {
            List<KeyValuePair<Mineral, double>> mix; if (mixId == null || !_mixes.TryGetValue(mixId, out mix) || mix.Count == 0) return new string[0];
            double total = 0; foreach (var kv in mix) total += kv.Value; if (total <= 0) return new string[0];
            var mw = new List<string>(); var el = new Dictionary<string, double>(); var tr = new Dictionary<string, double>();
            foreach (var kv in mix)
            {
                double sh = kv.Value / total; string nm = kv.Key.Name.ToLowerInvariant(); int br = nm.IndexOf(" ("); if (br > 0) nm = nm.Substring(0, br);
                mw.Add(nm + " " + (sh * 100).ToString(sh >= 0.1 ? "0" : "0.#") + "%");
                int na = Math.Max(1, kv.Key.Atoms.Length);
                foreach (var a in kv.Key.Atoms) { double v; el.TryGetValue(a, out v); el[a] = v + sh / na; }
                if (kv.Key.Traces != null) foreach (var host in kv.Key.Traces) { double hostShare = CountOf(kv.Key, host.Key) / (double)na * sh; foreach (var t in host.Value) { double v; tr.TryGetValue(t.Key, out v); tr[t.Key] = v + t.Value * hostShare; } }
            }
            foreach (var k in new List<string>(tr.Keys)) if (el.ContainsKey(k)) { el[k] += tr[k]; tr.Remove(k); }   // (a trace in one mineral, a main element of another: an element)
            var els = new List<KeyValuePair<string, double>>(el); els.Sort((a, b) => b.Value.CompareTo(a.Value));
            var trs = new List<KeyValuePair<string, double>>(tr); trs.Sort((a, b) => b.Value.CompareTo(a.Value));
            Func<string, string> name = sym => { var e = El(sym); return e != null ? e.Name.ToLowerInvariant() : sym; };
            Func<double, string> share = v => v >= 0.0001 ? (v * 100).ToString(v >= 0.01 ? "0.#" : "0.##") + "%" : v >= 1e-7 ? (v * 1e6).ToString("0.#") + " ppm" : (v * 1e9).ToString("0.##") + " ppb";
            var ew = new List<string>(); for (int q = 0; q < Math.Min(8, els.Count); q++) ew.Add(name(els[q].Key) + " " + share(els[q].Value)); if (els.Count > 8) ew.Add("and " + (els.Count - 8) + " more");
            var tw = new List<string>(); int shown = Math.Min(trs.Count, 5); for (int q = 0; q < shown; q++) tw.Add(name(trs[q].Key) + " " + share(trs[q].Value));
            if (mw.Count > 5) { int more = mw.Count - 5; mw.RemoveRange(5, more); mw.Add("and " + more + " more"); }
            var L = new List<string> { "minerals: " + string.Join(", ", mw.ToArray()), "elements: " + string.Join(", ", ew.ToArray()) };
            if (tw.Count > 0) L.Add("traces: " + string.Join(", ", tw.ToArray()) + (trs.Count > shown ? ", and " + (trs.Count - shown) + " more" : ""));
            // where its elements were forged (content/elements.unt made-in): the big bang, dying low-mass stars, exploding massive stars
            // and white dwarfs, merging neutron stars; the forges in the order of the elements' shares, each with its first two
            var forge = new Dictionary<string, List<string>>(); var forgeOrder = new List<string>();
            foreach (var kv in els) { var e = El(kv.Key); if (e == null || e.MadeIn.Length == 0) continue; List<string> lst; if (!forge.TryGetValue(e.MadeIn, out lst)) { if (forgeOrder.Count >= 3) continue; lst = new List<string>(); forge[e.MadeIn] = lst; forgeOrder.Add(e.MadeIn); } if (lst.Count < 2) lst.Add(e.Name.ToLowerInvariant()); }
            if (forgeOrder.Count > 0) { var fw = new List<string>(); foreach (var f in forgeOrder) fw.Add(f + " (" + string.Join(", ", forge[f].ToArray()) + ")"); L.Add("forged in: " + string.Join("; ", fw.ToArray())); }
            return L.ToArray();
        }
        public void SetAir(string air, ulong spot)
        { Announce("SetAir", false, new object[] { air, spot }); _recDepth++; try {
            _airText = air ?? "";
            var parts = new List<SpaceGen.Part>(); double total = 0;
            foreach (var bit in (air ?? "").Split(','))
            {
                string t = bit.Trim(); int pc = t.IndexOf('%'); if (pc < 0) continue;
                int sp = t.LastIndexOf(' ', pc); if (sp < 0) continue;
                double share; if (!double.TryParse(t.Substring(sp + 1, pc - sp - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out share)) continue;
                string name = t.Substring(0, sp).Trim().ToLowerInvariant(); if (name.EndsWith(" vapour")) name = name.Substring(0, name.Length - 7);
                string[] atoms; if (!_gasAtoms.TryGetValue(name, out atoms)) continue;
                foreach (var a in atoms) { parts.Add(new SpaceGen.Part(name, a, share, 1, 1, 1)); total += share; }
            }
            if (total <= 0) { parts.Add(new SpaceGen.Part("hydrogen", "H", 1, 1, 1, 1)); total = 1; }
            foreach (var q in parts) q.Share /= total;
            SetSpace(10, spot, 0, parts.ToArray());
            _gasSpacing = 1e-5 / Math.Pow(_airPerCc, 1 / 3.0);                           // about 3.4 nm between molecules at one bar
        } finally { _recDepth--; } }
        private Element GasAtom(long i, long j, long k)
        {
            double u = Hash(i, j, k + 90, _seed);
            if (_gasParts != null)
            {
                // its own make-up (hydrogen most, then its oxygen, sulfur, nitrogen, iron, or its molecules' carbon, oxygen and nitrogen)
                foreach (var q in _gasParts) { u -= q.Share; if (u <= 0) { var e = El(q.El); if (e != null) { double sr; Element par; return Daughter(e, i * 9176 + j * 131 + k, out sr, out par); } } }
            }
            { double sr; Element par; return Daughter(El(u < 0.905 ? "H" : u < 0.995 ? "He" : u < 0.998 ? "O" : "N"), i * 9176 + j * 131 + k, out sr, out par); }
        }
        // the point you dive into (km, across the ground) and where it is easing to (what you picked)
        private double _fx, _fz, _tfx, _tfz, _fy, _tfy, _lastTime = -1;   // (and its height, so a pick in any layer is gone to)
        private sealed class Body { public int Kind; public bool Free; public long I, J, Key; public double X, Y, Z, Size; public string Sym; public double AX, AY, AZ; }   // AX AY AZ: an electron's atom, so the ride can work out where the electron is every frame   // Kind 0 a grain, 1 an atom, 2 a spot   // 0 a grain, 1 an atom
        private readonly List<Body> _bodies = new List<Body>();
        private Mat4 _lastVp; private int _lastW = 1, _lastH = 1; private bool _haveVp;
        private Body _picked;
        /// <summary>Counts the picks made (the tour knows a pick it asked for has been made).</summary>
        public int PickCount;


        public MatterView(Gpu gpu, BillboardPipeline pipe, ContentLibrary content)
        {
            _gpu = gpu; _dots = new BillboardDrawer(gpu.CommandList, pipe);
            foreach (var e in content.OfKind("elements.unt", "element"))
            {
                int z, a; if (!int.TryParse(e.Get("number", ""), out z) || !int.TryParse(e.Get("mass-number", ""), out a)) continue;
                // every atom's colour and sizes from content/elements.unt (none named in code)
                string sym = e.Get("symbol", e.Id); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
                var cs = e.Get("colour", "0.7 0.6 0.8").Split(' '); float cr = 0.7f, cg = 0.6f, cb = 0.8f;
                if (cs.Length == 3) { float.TryParse(cs[0], fs, ci, out cr); float.TryParse(cs[1], fs, ci, out cg); float.TryParse(cs[2], fs, ci, out cb); }
                double cov, rad; if (!double.TryParse(e.Get("covalent-a", ""), fs, ci, out cov)) cov = 1.5;
                if (!double.TryParse(e.Get("atomic-a", ""), fs, ci, out rad)) rad = cov;
                _elements[sym] = new Element { Symbol = sym, Name = e.Name, Z = z, A = a, R = cr, G = cg, B = cb, Radius = rad * 1e-13, Cov = cov * 1e-13, Chi = TryD(e.Get("electronegativity", "0"), 0), Shells = Shells(z), HalfLifeS = HalfLifeSeconds(e.Get("half-life", "")), Decays = e.Get("decays", ""), MadeIn = e.Get("made-in", "") };
                { double ia; int ic; if (double.TryParse(e.Get("ionic-a", ""), fs, ci, out ia) && int.TryParse(e.Get("ion-charge", "0").Replace("+", ""), out ic)) { _elements[sym].Ionic = ia * 1e-13; _elements[sym].Charge = ic; } }   // (its common ion: content/elements.unt, Shannon 1976)
                { double ev; if (double.TryParse(e.Get("ionise-ev", ""), fs, ci, out ev)) _elements[sym].IonEv = ev; }   // (its first ionisation energy, NIST)
                _byZ[z] = _elements[sym];
            }
            foreach (var e in content.OfKind("minerals.unt", "mineral"))
            {
                var col = e.Get("colour", "0.7 0.7 0.7").Split(' ');
                var m = new Mineral { Id = e.Id, Name = e.Name, Formula = e.Get("formula", ""), Atoms = e.Get("atoms", "Si O O").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) };
                float r, g, b; float.TryParse(col[0], out r); float.TryParse(col.Length > 1 ? col[1] : col[0], out g); float.TryParse(col.Length > 2 ? col[2] : col[0], out b);
                m.R = r; m.G = g; m.B = b; m.Lustre = LustreCode(e.Get("lustre", "earthy"), e.Get("clarity", "opaque"));
                m.Lat = Lattice.From(e); m.Structure = e.Get("structure", ""); m.Liquid = e.Get("state", "") == "liquid"; m.Spacing = TryD(e.Get("spacing-nm", "0"), 0) * 1e-12; m.Molecule = MoleculeFrom(e.Get("molecule", ""));
                // its trace elements (content: trace <host> <element> <share of the host's sites> ...): the real minor and trace
                // chemistry of the mineral, each element taking the host's place on that share of its sites
                foreach (var tl in e.All("trace"))
                {
                    var w = tl.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length < 3) continue;
                    var list = new List<KeyValuePair<string, double>>();
                    for (int q = 1; q + 1 < w.Length; q += 2) { double sh; if (double.TryParse(w[q + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sh) && sh > 0) list.Add(new KeyValuePair<string, double>(w[q], sh)); }
                    if (list.Count == 0) continue;
                    if (m.Traces == null) m.Traces = new Dictionary<string, KeyValuePair<string, double>[]>();
                    m.Traces[w[0]] = list.ToArray();
                }
                _minerals[m.Id] = m;
            }
            foreach (var e in content.OfKind("minerals.unt", "surface"))
            {
                Mineral om; bool fromGround = e.Get("mineral", "") == "ground"; if (!_minerals.TryGetValue(e.Get("mineral", ""), out om) && !fromGround) continue;
                double lo, hi; if (!e.TryRange("size-km", out lo, out hi)) continue;
                double ch, bl, ang; double.TryParse(e.Get("chance", "0.5"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ch);
                double.TryParse(e.Get("bond-nm", "0.1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bl);
                double.TryParse(e.Get("angle", "180"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ang);
                string shape = e.Get("shape", "round");
                _surf.Add(new SurfThing { Id = e.Id, Name = e.Name, M = om, Lo = lo, Hi = hi, Chance = ch, Flat = shape == "flat", Molecule = shape == "molecule",
                    Bond = bl * 1e-12, Angle = ang, FromGround = fromGround, Thick = TryD(e.Get("thickness", "0.05"), 0.05), Cell = TryD(e.Get("spacing", "4"), 4), Aspect = TryD(e.Get("aspect", "1"), 1), Life = e.Get("life", "no") == "yes", About = e.Get("about", "").Split('|').Select(q => q.Trim()).Where(q => q.Length > 0).ToArray() });
            }
            foreach (var e in content.OfKind("physics.unt", "gas")) _gasAtoms[e.Id.ToLowerInvariant()] = e.Get("atoms", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var e in content.OfKind("physics.unt", "law"))
            {
                if (e.Id == "air-density") _airPerCc = TryD(e.Get("number-density", "2.5e19"), 2.5e19);
                if (e.Id == "star-plasma") { _plasmaMake = e.Get("makeup", _plasmaMake); _plasmaPerCc = TryD(e.Get("per-cc", "1e17"), 1e17); }
                if (e.Id == "nuclear-force") { _coreFm = TryD(e.Get("core-fm", "1.7"), 1.7); _spacingFm = TryD(e.Get("spacing-fm", "1.8"), 1.8); _rangeFm = TryD(e.Get("range-fm", "2.6"), 2.6); }
                if (e.Id == "coulomb") _kCoulomb = TryD(e.Get("k-mev-fm", "1.44"), 1.44);
                if (e.Id == "mass-formula") { _aV = TryD(e.Get("volume-mev", "15.75"), 15.75); _aS = TryD(e.Get("surface-mev", "17.8"), 17.8); _aC = TryD(e.Get("coulomb-mev", "0.711"), 0.711); _aA = TryD(e.Get("asymmetry-mev", "23.7"), 23.7); _aP = TryD(e.Get("pairing-mev", "11.18"), 11.18); }
                if (e.Id == "beta-stability") { _betaC = TryD(e.Get("c", "0.0155"), 0.0155); _alphaZ = (int)TryD(e.Get("alpha-above-z", "82"), 82); }
            }
            foreach (var e in content.OfKind("minerals.unt", "mix"))
            {
                var list = new List<KeyValuePair<Mineral, double>>();
                foreach (var part in e.Get("minerals", "").Split(','))
                {
                    var w = part.Trim().Split(' '); double share; Mineral m;
                    if (w.Length == 2 && double.TryParse(w[1], out share) && _minerals.TryGetValue(w[0], out m)) list.Add(new KeyValuePair<Mineral, double>(m, share));
                }
                if (list.Count > 0) _mixes[e.Id] = list;
                double glo, ghi; if (e.TryRange("grain-mm", out glo, out ghi)) _grainKm[e.Id] = (glo + ghi) * 0.5 * 1e-6;
                double vv; if (double.TryParse(e.Get("vary", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv) && vv > 0) _mixVary[e.Id] = vv;
            }
        }

        // electrons per shell (1, 2, 3, ...) for atomic number z, filled in the usual order (Madelung's rule)
        // an atom's subshells, filled in the Madelung order (1s 2s 2p 3s 3p 4s 3d ...): (n, l, electrons) for each
        private static List<int[]> Subshells(int z)
        {
            int[][] order = { new[] { 1, 0 }, new[] { 2, 0 }, new[] { 2, 1 }, new[] { 3, 0 }, new[] { 3, 1 }, new[] { 4, 0 }, new[] { 3, 2 }, new[] { 4, 1 }, new[] { 5, 0 }, new[] { 4, 2 }, new[] { 5, 1 },
                new[] { 6, 0 }, new[] { 4, 3 }, new[] { 5, 2 }, new[] { 6, 1 }, new[] { 7, 0 }, new[] { 5, 3 }, new[] { 6, 2 }, new[] { 7, 1 } };
            var l = new List<int[]>(); int left = z;
            foreach (var o in order) { if (left <= 0) break; int take = Math.Min(left, 2 * (2 * o[1] + 1)); l.Add(new[] { o[0], o[1], take }); left -= take; }
            return l;
        }

        // how strongly an orbital of angular kind l and number m (0 .. 2l) leans along a direction (its real |Y|^2 shape):
        // s round, p three dumbbells on the axes, d four-leaf clovers and the ring-and-dumbbell, f the more lobed beyond
        private static double Lobe(int l, int m, double x, double y, double z)
        {
            switch (l)
            {
                case 0: return 1;
                case 1: return m == 0 ? x * x * 3 : m == 1 ? y * y * 3 : z * z * 3;
                case 2:
                    switch (m)
                    {
                        case 0: return 15 * x * x * y * y; case 1: return 15 * y * y * z * z; case 2: return 15 * x * x * z * z;
                        case 3: return 3.75 * (x * x - z * z) * (x * x - z * z); default: return 1.25 * (3 * y * y - 1) * (3 * y * y - 1);
                    }
                default: { double c = Math.Cos(m * 1.3) * x + Math.Sin(m * 1.3) * z; return 7 * Math.Pow(Math.Abs(c * y), 1.5) + 0.2; }
            }
        }

        // an atom's electrons as what they are: each occupied orbital a cloud of where its electron may be found (its
        // probability), in its real shape about its shell's distance, shimmering as it is sampled afresh a few times a second
        // (the electron is a wave, not a dot on a ring); the outer (valence) orbitals in gold
        // an atom's electrons as what they are, worked out on the card: one record for each occupied orbital (where the atom
        // is, its shell's distance, its kind l and number m, its colour); the point shader places each orbital's cloud of points
        // in its real shape (the processor no longer works out a single point of it)
        private readonly List<StarVertex> _orbRecs = new List<StarVertex>(); private StarVertex[] _orbArr = new StarVertex[0]; private UploadBuffer _orbBuf;
        private const int OrbitalPoints = 60;
        // an atom near where you look (within about three atoms, or half the view): only these show their electrons, paths and
        // orbitals; every other atom is its nucleus in its place in the lattice, so the crystal's pattern reads, never a fog
        private bool NearLook(double x, double y, double z, double s)
        {
            double dx = x - _fx, dy = y + _yOff - _fy, dz = z - _fz, r = Math.Max(AtomSpacing * 3, s * 0.5);
            return dx * dx + dy * dy + dz * dz < r * r;
        }
        // an atom as what it is, at any size on screen: its nucleus (a spark at the middle) and its electrons' orbitals (clouds in
        // their real shapes, worked out on the card); never a solid ball (an atom has no surface: its size is its electrons)
        private void AtomSystem(List<StarVertex> glow, Element e, double x, double y, double z, double s, double time, long key, float f)
        {
            if (e == null || f <= 0.02) return;
            // its orbitals' clouds only for the atoms close to where you look (within about three atoms): drawn for every atom
            // of a crystal at once, their lobes would hang everywhere as pairs of glowing blobs; farther ones show their nucleus
            // and their electrons on their paths
            double fdx = x - _fx, fdy = y + _yOff - _fy, fdz = z - _fz;
            if (fdx * fdx + fdy * fdy + fdz * fdz < Math.Pow(AtomSpacing * 0.6, 2)) Orbitals(glow, e, x, y, z, s, time, key, f);   // (only the atom you are at: the clouds of every atom in view at once read as static)
            // its electrons, each going round on its own path tilted its own way in three dimensions (never all in one plane),
            // the inner shells faster, the valence shell (the one it bonds with) in gold
            double ar = AtomRadius(e), dot = s * 0.006; int shells = e.Shells.Length;
            // (only the atoms near where you look: never every electron of every atom in view, millions of them)
            if (ar / s > 0.004 && fdx * fdx + fdy * fdy + fdz * fdz < Math.Pow(Math.Max(AtomSpacing * 6, s * 1.2), 2))
                {
                    var els = ElectronsOf(e);
                    for (int k = 0; k < els.Length; k++)
                    {
                        var el = els[k]; bool valence = el.N == shells; double ox, oy, oz; ElectronAt(el, key, ElectronPhase(el, k, time), out ox, out oy, out oz, ElectronDrift(time));
                        double exx = x + ox * ar, eyy = y + oy * ar, ezz = z + oz * ar;
                        // the electron you ride beside (picked): the view goes to where it is now and rides with it, and it shows as a
                        // glowing body a share of the view, whichever way its atom is drawn
                        bool riding = _picked != null && _picked.Kind == 3 && _picked.Key == key && _picked.J == k;
                        if (riding)
                        {
                            _tfx = exx; _tfy = eyy + _yOff; _tfz = ezz; _picked.X = exx; _picked.Y = eyy + _yOff; _picked.Z = ezz;
                            double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < Math.Pow(s * 3, 2)) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                        }
                        Add(glow, exx, eyy, ezz, riding ? Math.Max(dot, s * 0.07) : dot, (valence ? 1f : 0.45f) * f, (valence ? 0.8f : 0.75f) * f, (valence ? 0.25f : 1f) * f, s);
                        { int nb = _bodies.Count; AddBody(3, el.N, k, exx, eyy, ezz, ar * 0.05, s); if (_bodies.Count > nb) { _bodies[nb].Sym = "electron:" + e.Symbol + ":" + shells + ":" + el.N + "spdf"[Math.Min(3, el.L)]; _bodies[nb].Key = key; _bodies[nb].AX = x; _bodies[nb].AY = y; _bodies[nb].AZ = z; } }   // every atom's electrons can be picked, however its atom is drawn
                    }
                }
            // its nucleus: a clear sharp point of light in its element's own colour, so the crystal's pattern reads at a glance
            // (each kind of atom in its own place); its true size once close enough to show it
            Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.003), (0.5f + 0.5f * e.R) * f, (0.5f + 0.5f * e.G) * f, (0.5f + 0.5f * e.B) * f, s);
        }
        // the one atom model at every size (Universe DESIGN 740): an atom as its electron density, one record the card spreads into
        // its electrons' cloud (Pipeline.cs, code 7: r^2 e^(-2r/a) out past its radius, so neighbours' outer electrons overlap into the
        // solid's sea of them), its light kept by its size on screen; its nucleus a mark once the atom is big enough to hold one
        // how far an atom's electrons reach in this mineral: its own size where it shares them, its ion's where it gives or takes them
        // (an ionic bond), in proportion to its ionic character against its most different neighbour (Pauling: 1 - e^(-dchi^2 / 4));
        // a cation only shrinks and an anion only swells as their listed charge says (content/elements.unt ion-charge, ionic-a)
        private readonly Dictionary<Mineral, Dictionary<Element, double>> _reach = new Dictionary<Mineral, Dictionary<Element, double>>(); private readonly Dictionary<Element, double> _reachNone = new Dictionary<Element, double>();
        private double IonicCharacter(Mineral m, Element e, out Element partner)
        {
            partner = null; if (m == null || m.Atoms == null) return 0; double best = 0;
            foreach (var sym in m.Atoms) { var o = El(sym); if (o == null || o == e || o.Chi <= 0 || e.Chi <= 0) continue; double d = Math.Abs(o.Chi - e.Chi); if (d > best) { best = d; partner = o; } }
            return 1 - Math.Exp(-best * best / 4);
        }
        private double Reach(Mineral m, Element e)
        {
            Dictionary<Element, double> d; if (m == null) d = _reachNone; else if (!_reach.TryGetValue(m, out d)) { d = new Dictionary<Element, double>(); _reach[m] = d; }
            double r; if (d.TryGetValue(e, out r)) return r;
            r = AtomRadius(e); Element p; double ic = IonicCharacter(m, e, out p);
            if (p != null && e.Ionic > 0 && e.Charge != 0 && ((e.Charge > 0) == (e.Chi < p.Chi))) r += (e.Ionic - r) * ic;   // (giving: smaller; taking: bigger)
            d[e] = r; return r;
        }
        private void Density(List<StarVertex> glow, Mineral m, Element e, double x, double y, double z, double s, float f)
        {
            if (e == null || f <= 0.01) return;
            double ar = Reach(m, e), vx = (x - _fx) / s + _ex, vy = (y + _yOff - _fy) / s + _ey, vz = (z - _fz) / s + _ez;
            if (Math.Abs(vx - _ex) > 60 || Math.Abs(vz - _ez) > 60) return;
            float b = 0.5f * f;
            _orbRecs.Add(new StarVertex { X = (float)vx, Y = (float)vy, Z = (float)vz, Size = (float)(ar * 1.25 / s), R = (0.35f + 0.65f * e.R) * b, G = (0.35f + 0.65f * e.G) * b, B = (0.35f + 0.65f * e.B) * b, A = (7 * 8 + 0.25f) / 64f });
            if (ar / s * _viewH > 40) Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.0014), (0.5f + 0.5f * e.R) * f, (0.5f + 0.5f * e.G) * f, (0.5f + 0.5f * e.B) * f, s);   // (its nucleus, a point at its middle, once the atom is a few dozen pixels across)
        }
        private void Orbitals(List<StarVertex> glow, Element e, double x, double y, double z, double s, double time, long key, float f)
        {
            if (f <= 0.05) return;
            double ar = AtomRadius(e); var subs = Subshells(e.Z); int top = 0; foreach (var o in subs) top = Math.Max(top, o[0]);
            foreach (var o in subs)
            {
                int n = o[0], l = o[1], count = o[2], orbitals = 2 * l + 1; bool valence = n == top;
                for (int m = 0; m < orbitals; m++)
                {
                    if (count <= m) continue;                                              // filled one to each first (Hund), then paired
                    float b = (valence ? 0.9f : 0.5f) * f * 0.35f;
                    double vx = (x - _fx) / s + _ex, vy = (y + _yOff - _fy) / s + _ey, vz = (z - _fz) / s + _ez;
                    if (Math.Abs(vx - _ex) > 60 || Math.Abs(vz - _ez) > 60) continue;
                    double rf = 1; foreach (var el in ElectronsOf(e)) if (el.N == n && el.L == l) { rf = el.R; break; }   // (its subshell's own distance)
                    _orbRecs.Add(new StarVertex { X = (float)vx, Y = (float)vy, Z = (float)vz, Size = (float)(ar * rf / s),
                        R = valence ? b : b * 0.55f, G = valence ? b * 0.78f : b * 0.7f, B = valence ? b * 0.25f : b, A = (l * 8 + m + 0.25f) / 64f });
                }
            }
        }


    }
}
