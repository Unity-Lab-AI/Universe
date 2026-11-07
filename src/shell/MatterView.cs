// Universe · shell · the ground's own matter, all the way down, at its real sizes: below the finest piece of ground the camera
// carries on into what the ground is made of. Everything sits at a fixed place around the point you dive into, so zooming truly
// scales it into view (nothing fades between pictures):
//   grains (about 0.2 mm), each one mineral from its ground's own mix (content/minerals.unt);
//   the grain's surface, its detail at every size fixed in place;
//   the mineral's atoms in rows about 0.25 nm apart, its real elements in their real ratio, one atom right under you;
//   that atom: its electrons in their real shells (worked out from its atomic number), moving round it;
//   inside it: mostly empty, the nucleus a spark at its middle;
//   the nucleus: its real protons and neutrons (from the element's most common isotope, content/elements.unt).
// (Microbes, bacteria and viruses join when life is built.)
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class MatterView : IDisposable
    {
        private readonly Gpu _gpu; private readonly BillboardDrawer _dots;
        private UploadBuffer _solidBuf, _glowBuf, _meshBuf; private StarVertex[] _sv = new StarVertex[0], _gv = new StarVertex[0], _mv = new StarVertex[0];
        public const double TopKm = 3e-9;           // where the matter alone is drawn, about 3 micrometres up, on one grain's face (above it the ground itself, its grains its own)
        public const double OverlayKm = 1e-5;       // 1 cm up: the grains begin to show over the ground, as specks that grow as you come down
        /// <summary>The zoom in has no floor: this is only where a double-precision number gives out, 10^255 times below the
        /// Planck length. Inside a quark nothing is known to be there, and the picture and the numbers go on down honestly.</summary>
        public const double BottomKm = 1e-290;
        /// <summary>The smallest anything is known to be: a quark shows no size down to a ten-thousandth of a femtometre (1e-19 m).</summary>
        public const double KnownKm = 1e-22;
        /// <summary>The Planck length (1.6e-35 m), below which length itself is thought to mean nothing.</summary>
        public const double PlanckKm = 1.6e-38;
        private double Grain = 2e-7;                // a grain's size across (km): from its ground's mix (content: grain-mm)
        private readonly Dictionary<string, double> _grainKm = new Dictionary<string, double>();
        private readonly Dictionary<string, double> _mixVary = new Dictionary<string, double>();   // a mix's spread from body to body (content/minerals.unt vary)
        private const double AtomSpacing = 2.5e-13; // about 0.25 nm between atoms in a crystal
        private const double Nucleon = 0.85e-18;    // a proton's or neutron's radius, about 0.85 femtometres

        private sealed class Mineral { public string Id, Name, Formula; public string[] Atoms; public float R, G, B, Lustre = 1; public Lattice Lat; public KeyValuePair<string, double[]>[] Molecule; public string Structure = ""; public bool Liquid; public double Spacing; public Dictionary<string, KeyValuePair<string, double>[]> Traces; }   // Traces: for each atom of the formula, the elements that stand in for it on its sites now and then, each with its share of those sites (content: trace)
        private sealed class Element { public string Symbol, Name; public int Z, A; public float R, G, B; public double Radius, Cov, Chi; public int[] Shells; public double HalfLifeS; public string Decays = "", MadeIn = ""; }   // HalfLifeS: 0 stable; MadeIn: where its atoms were forged (content/elements.unt made-in) (content/elements.unt half-life, decays)
        private readonly Dictionary<string, Mineral> _minerals = new Dictionary<string, Mineral>();
        private readonly Dictionary<string, List<KeyValuePair<Mineral, double>>> _mixes = new Dictionary<string, List<KeyValuePair<Mineral, double>>>();
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>(); private readonly Dictionary<int, Element> _byZ = new Dictionary<int, Element>();
        private List<KeyValuePair<Mineral, double>> _mix; private string _mixId = "";
        private ulong _seed; private bool _noGrains;
        // in space (a nebula's or cluster's gas) instead of a ground: its kind (0 globular cluster, 1 open cluster, 2 emission
        // nebula, 3 reflection nebula) and how far apart its atoms are (km)
        private bool _space; private int _spaceKind; private double _gasSpacing = 1e-6;
        /// <summary>Into the gas of a nebula or a cluster: about 1,000 atoms a cubic centimetre in an emission nebula (one about every
        /// millimetre), fewer in a reflection nebula and between a cluster's stars; mostly hydrogen, about 9 in 100 helium, a trace of
        /// oxygen and nitrogen.</summary>
        public void SetSpace(int kind, ulong spot, double fromCentre) { SetSpace(kind, spot, fromCentre, null); }
        private SpaceGen.Part[] _gasParts;
        public void SetSpace(int kind, ulong spot, double fromCentre, SpaceGen.Part[] parts)
        {
            _gasParts = parts; _bornSec = double.NaN;
            _space = true; _spaceKind = kind; _seed = spot; _mix = null; _mixId = "space"; _noGrains = true; _plasma = false;
            // denser at the middle: an emission nebula about 9,000 atoms a cubic centimetre at its centre down to about 100 at its edge,
            // a reflection nebula about 3,000 to 30; between a cluster's stars about 1 (fromCentre: 0 the middle, 1 the edge)
            double core = 0.33 + 30 * Math.Exp(-Math.Pow(fromCentre / 0.25, 2));
            double perCc = kind == 2 ? 300 * core : kind == 3 ? 100 * core : kind == 4 ? 1000 * core : kind == 5 ? 3 * core : kind == 6 ? 300 * core : 1;   // planetary nebulae up to tens of thousands, remnants thin and hot, dark clouds dense
            _gasSpacing = 1e-5 / Math.Pow(perCc, 1 / 3.0);                                   // km between atoms (a centimetre is 1e-5 km)
            _fx = _fz = _tfx = _tfz = _fy = _tfy = 0; _picked = null;
        }
        // ── an air: going into a world with no ground (a giant's clouds), its own gases at their real density ──
        private readonly Dictionary<string, string[]> _gasAtoms = new Dictionary<string, string[]>(); private double _airPerCc = 2.5e19;
        /// <summary>Into an air (its make-up as the content gives it, e.g. "hydrogen 90%, helium 10%, methane 0.3%"): each
        /// molecule's atoms in their shares, at about one bar.</summary>
        private string _airText = "";
        // the air over the ground: its molecules (from the world's own air make-up), about 3.4 nm apart at one bar, jostling
        private List<KeyValuePair<string[], double>> _airMols; private double _airAbove;
        /// <summary>The air over this ground (the world's air make-up, as content gives it; none for a world without air).</summary>
        public void SetAirAbove(string air, double perCc = 0)
        {
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
        }
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
        public void SetLife(bool alive) { _lifeHere = alive; }
        private bool _plasma; private string _plasmaMake = "hydrogen 91.2%, helium 8.7%"; private double _plasmaPerCc = 1e17;
        /// <summary>Into a star's surface: its plasma (content/physics.unt star-plasma): bare nuclei of its elements in their
        /// shares, at its density, with free electrons darting between them (too hot for any atom to keep its electrons).</summary>
        public void SetPlasma(ulong spot) { SetPlasma(spot, _plasmaMake); }
        /// <summary>A star's plasma, of its own make-up ("hydrogen 91.2%, helium 8.7%, ...", by count of atoms).</summary>
        public void SetPlasma(ulong spot, string makeup) { SetPlasma(spot, makeup, 0, null, 0); }
        private string _plasmaWhere; private double _plasmaTempK;
        /// <summary>A star's plasma at a layer: its make-up there, its density (g/cc: the nuclei packed accordingly), the layer's name and its heat.</summary>
        public void SetPlasma(ulong spot, string makeup, double densityGcc, string where, double tempK)
        {
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
            SetSpace(11, spot, 0, parts.ToArray()); _plasma = true; _plasmaWhere = where; _plasmaTempK = tempK;
            double perCc = _plasmaPerCc;
            if (densityGcc > 0) { double meanA = 0; foreach (var q in parts) { var el = El(q.El); meanA += q.Share * (el != null ? el.A : 1); } perCc = densityGcc * 6.022e23 / Math.Max(1, meanA); }   // nuclei a cubic centimetre, from the density and the mean mass of them
            _gasSpacing = 1e-5 / Math.Pow(perCc, 1 / 3.0);
        }
        public bool InSpace { get { return _space; } }
        public int SpaceKind { get { return _space ? _spaceKind : 0; } }   // (none at all on a body's ground: a hole's kind had outlived the fall, and the well's grid, its black and its clock had followed you onto every rock after)
        /// <summary>A neutron star's crust: iron nuclei packed in a lattice (about a hundred million grams a cubic centimetre: the nuclei
        /// a hundredth of an atom's width apart), electrons racing between them; the same drawing as a star's plasma, at that spacing.</summary>
        public void SetNeutronCrust(ulong spot)
        {
            SetPlasma(spot, "iron 100%"); _spaceKind = 13; _gasSpacing = 1e-15;
        }
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
        {
            SetSpace(12, spot, 0, null); _holeRs = rsKm; _holeMass = massSol; _holeSpin = spin; _wayInKm = wayInKm; _gasSpacing = 1e-40;
        }
        // the singularity: its whole mass in a point with no size at all, as far as the laws we have can say; drawn, once the zoom
        // is down at the quarks' size, as quarks pressed into a ball a third of the view wide at every size on down (a point: however
        // far in you go it is the same), all three colour charges, picked like any quark; above that size, nothing: the fall is black
        private void Singularity(List<StarVertex> glow, double s, double time)
        {
            if (s > Nucleon * 8) return;
            float[][] colour = { new[] { 1f, 0.3f, 0.3f }, new[] { 0.3f, 1f, 0.35f }, new[] { 0.4f, 0.5f, 1f } };
            double R = s * 0.32;
            for (int i = 0; i < 1200; i++)
            {
                double u = Hash(i, 1, 3, _seed), v = Hash(i, 2, 3, _seed) * 2 - 1, a = Hash(i, 3, 3, _seed) * Math.PI * 2 + time * (0.02 + 0.01 * (i % 7)), r = R * Math.Pow(u, 1 / 3.0), sq = Math.Sqrt(Math.Max(0, 1 - v * v));
                double x = r * sq * Math.Cos(a), y = r * v, z = r * sq * Math.Sin(a);
                var c = colour[i % 3]; float f = (float)(0.55 + 0.45 * Math.Sin(time * 0.6 + i));
                Add(glow, x, y, z, Math.Max(s * 0.004, Nucleon * 0.02), c[0] * f, c[1] * f, c[2] * f, s);
                if (i < 240) { int nb = _bodies.Count; AddBody(4, i, i % 2, x, y, z, Math.Max(s * 0.01, Nucleon * 0.05), s); if (_bodies.Count > nb) { _bodies[nb].Sym = i % 2 == 1 ? "up" : "down"; _bodies[nb].Key = 1; } }
            }
            Add(glow, 0, 0, 0, s * 0.06, 1f, 1f, 1f, s);
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
        {
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
        }
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
        private sealed class Body { public int Kind; public long I, J, Key; public double X, Y, Z, Size; public string Sym; public double AX, AY, AZ; }   // AX AY AZ: an electron's atom, so the ride can work out where the electron is every frame   // Kind 0 a grain, 1 an atom, 2 a spot   // 0 a grain, 1 an atom
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


        // ── each element's own electrons, as its real make-up gives them: every electron in its subshell (1s 2s 2p 3s ...,
        // filled in the Madelung order), each orbital holding two of opposite spin (filled one to each first, Hund's rule); how
        // far out each subshell sits from the charge its electrons feel (Slater's rules: the nucleus less the screening of the
        // electrons inside), its mean distance n^2 a0 / Zeff, so heavy atoms pull their inner shells in tight; and how fast each
        // goes round (as the charge it feels and its shell: inner electrons of a heavy atom far faster); each moves on a path in
        // its orbital's own shape (s round, p through its two lobes, d round its four leaves) ──
        private sealed class Electron { public int N, L, M, Spin, Sub; public double R, W; }
        private readonly Dictionary<int, Electron[]> _electrons = new Dictionary<int, Electron[]>();
        private Electron[] ElectronsOf(Element e)
        {
            Electron[] r; if (_electrons.TryGetValue(e.Z, out r)) return r;
            var subs = Subshells(e.Z); var list = new List<Electron>(); double rmax = 0;
            var rs = new double[subs.Count]; var ws = new double[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                int n = subs[i][0], l = subs[i][1];
                // Slater's groups: (1s)(2s 2p)(3s 3p)(3d)(4s 4p)(4d)(4f)(5s 5p)...: the same group screens 0.35 each (1s 0.30); for an
                // s or p electron the shell below screens 0.85 and all deeper 1.00; for a d or f electron everything inside 1.00
                double screen = 0;
                for (int j = 0; j < subs.Count; j++)
                {
                    int n2 = subs[j][0], l2 = subs[j][1], c = subs[j][2] - (j == i ? 1 : 0); if (c <= 0) continue;
                    bool sameGroup = l <= 1 ? (n2 == n && l2 <= 1) : (n2 == n && l2 == l);
                    bool inside = l <= 1 ? (n2 < n || (n2 == n && l2 > 1 && false)) : (n2 < n || (n2 == n && l2 < l));
                    if (sameGroup) screen += c * (n == 1 ? 0.30 : 0.35);
                    else if (l <= 1 && n2 == n - 1) screen += c * 0.85;
                    else if (l <= 1 && n2 < n - 1) screen += c * 1.00;
                    else if (l > 1 && inside) screen += c * 1.00;
                }
                double zeff = Math.Max(1, e.Z - screen);
                rs[i] = n * n / zeff; ws[i] = zeff * zeff / (n * n * n); rmax = Math.Max(rmax, rs[i]);
            }
            double wOut = 0; for (int i = 0; i < subs.Count; i++) if (rs[i] == rmax) wOut = ws[i];
            for (int i = 0; i < subs.Count; i++)
            {
                int n = subs[i][0], l = subs[i][1], count = subs[i][2], orbs = 2 * l + 1;
                for (int k = 0; k < count; k++)
                    list.Add(new Electron { N = n, L = l, M = k % orbs, Spin = k / orbs, Sub = i, R = rs[i] / rmax, W = Math.Min(8, ws[i] / Math.Max(1e-9, wOut)) });
            }
            r = list.ToArray(); _electrons[e.Z] = r; return r;
        }
        // where an electron is on its path (as a share of the atom's size, from its nucleus) at phase a, its orbital's shape
        // turned its own way for this atom (key)
        private void ElectronAt(Electron el, long key, double a, out double x, out double y, out double z, double drift = 0)
        {
            double r = el.R; a += el.Spin * Math.PI;                                       // (its partner of opposite spin: across from it)
            double u, v, w;
            switch (el.L)
            {
                case 0:
                    {
                        // s: round, a path on its sphere, tilted its own way
                        double tilt = Hash(key + el.Sub, el.Spin, 33, _seed) * Math.PI + drift * 0.5;   // (its plane turning slowly: a path never the same twice)
                        u = Math.Cos(a) * r; v = Math.Sin(a) * r * Math.Sin(tilt); w = Math.Sin(a) * r * Math.Cos(tilt); break;
                    }
                case 1:
                    {
                        // p: out through one lobe, back through the node at the nucleus, out through the other, along its axis
                        double t = Math.Sin(a), side = Math.Sin(2 * a) * 0.32;
                        double ax = el.M == 0 ? 1 : 0, ay = el.M == 1 ? 1 : 0, az = el.M == 2 ? 1 : 0;
                        double px = ay, py = az, pz = ax;                                      // (a direction across it)
                        u = (ax * t * 1.25 + px * side) * r; v = (ay * t * 1.25 + py * side) * r; w = (az * t * 1.25 + pz * side) * r; break;
                    }
                case 2:
                    {
                        // d: round its four leaves in their plane (or, for d z^2, through its two lobes and round its ring)
                        if (el.M == 4) { double t = Math.Sin(a); u = Math.Cos(3 * a) * r * 0.45; v = t * r * 1.2; w = Math.Sin(3 * a) * r * 0.45; break; }
                        double q = Math.Abs(Math.Cos(2 * a)) * r * 1.3, c = Math.Cos(a) * q, d2 = Math.Sin(a) * q;
                        if (el.M == 3) { double cc = (c - d2) * 0.7071, dd = (c + d2) * 0.7071; c = cc; d2 = dd; }
                        if (el.M == 0 || el.M == 3) { u = c; v = el.M == 0 ? d2 : 0; w = el.M == 3 ? d2 : 0; }
                        else if (el.M == 1) { u = 0; v = c; w = d2; } else { u = c; v = 0; w = d2; }
                        break;
                    }
                default:
                    {
                        // f: round its six leaves
                        double q = Math.Abs(Math.Cos(3 * a)) * r * 1.3, b = el.M * 0.9;
                        u = Math.Cos(a) * q; v = Math.Sin(b) * Math.Sin(a) * q; w = Math.Cos(b) * Math.Sin(a) * q; break;
                    }
            }
            // the whole atom's orbitals turned one way for this atom (its own place in the crystal), and turning on slowly with time
            // (drift: each subshell and orbital its own way), so every path keeps changing, as an electron's does
            // and a new path every time round: an electron has no path of its own, only a cloud of where it may be, so the loop it is
            // shown on is never the same twice. Each time round the loop's turn and tilt change (from the count of times round, so
            // every electron its own), eased in over the last quarter of the way round, never a jump
            double rev = Math.Floor(a / (2 * Math.PI)), fr = a / (2 * Math.PI) - rev, ease = Math.Max(0, (fr - 0.75) / 0.25); ease = ease * ease * (3 - 2 * ease);
            double h0 = Hash(key + el.Sub * 7, el.Spin + (long)rev * 13, 35 + el.M, _seed), h1 = Hash(key + el.Sub * 7, el.Spin + ((long)rev + 1) * 13, 35 + el.M, _seed);
            double revTurn = (h0 + (h1 - h0) * ease) * Math.PI * 2, revTilt = (Hash(key + el.Sub * 7, el.Spin + (long)rev * 13, 36 + el.M, _seed) * 2 - 1) * 0.9;
            double revTilt1 = (Hash(key + el.Sub * 7, el.Spin + ((long)rev + 1) * 13, 36 + el.M, _seed) * 2 - 1) * 0.9; revTilt += (revTilt1 - revTilt) * ease;
            double turn = Hash(key, 7, 34, _seed) * Math.PI * 2 + drift * (0.6 + 0.25 * el.Sub + 0.1 * el.M) + revTurn, ct = Math.Cos(turn), st = Math.Sin(turn);
            double x1 = u * ct - w * st, z1 = u * st + w * ct, tb = drift * 0.37 * (1 + 0.2 * el.Sub) + revTilt, cb = Math.Cos(tb), sb = Math.Sin(tb);
            x = x1; y = v * cb - z1 * sb; z = v * sb + z1 * cb;
        }
        // how far round its path an electron is at a time (inner and more tightly held electrons faster; slowed to be watched)
        private static double ElectronPhase(Electron el, int k, double time) { return time * 0.3 * Math.Sqrt(el.W) + k * 2.399963229728653; }   // (slowed to be watched: the outer shell a turn in about twenty seconds, the innermost in about seven; at a twentieth of that the outer ones looked frozen)
        private static double ElectronDrift(double time) { return time * 0.025; }   // how far the paths have turned on (radians): a slow precession, a whole turn in about four minutes

        private static int[] Shells(int z)
        {
            int[][] order = { new[] { 1, 0 }, new[] { 2, 0 }, new[] { 2, 1 }, new[] { 3, 0 }, new[] { 3, 1 }, new[] { 4, 0 }, new[] { 3, 2 }, new[] { 4, 1 }, new[] { 5, 0 }, new[] { 4, 2 }, new[] { 5, 1 },
                new[] { 6, 0 }, new[] { 4, 3 }, new[] { 5, 2 }, new[] { 6, 1 }, new[] { 7, 0 }, new[] { 5, 3 }, new[] { 6, 2 }, new[] { 7, 1 } };
            var shells = new int[7]; int left = z, top = 0;
            foreach (var o in order)
            {
                if (left <= 0) break;
                int take = Math.Min(left, 2 * (2 * o[1] + 1)); shells[o[0] - 1] += take; left -= take; top = Math.Max(top, o[0]);
            }
            var r = new int[top]; Array.Copy(shells, r, top); return r;
        }

        /// <summary>What the ground at this spot is made of: its kind, whether it is under water or ice, and its colour; and
        /// the spot (a seed), so the same place always holds the same grains.</summary>
        public void Set(string planetKind, bool sea, bool ice, float r, float g, float b, ulong spot)
        { _bornSec = double.NaN;
            string id = sea ? "sea" : ice ? "ice" : planetKind;
            if (!_mixes.ContainsKey(id)) id = "desert";
            _space = false; _spaceKind = 0; _mixId = id; _mix = _mixes.ContainsKey(id) ? _mixes[id] : null; _seed = spot;
            _fx = _fz = _tfx = _tfz = _fy = _tfy = 0; _picked = null;
            double gk; Grain = _grainKm.TryGetValue(id, out gk) ? gk : 3e-7; _noGrains = Grain <= 0; if (_noGrains) Grain = 3e-7;   // the sea has no grains
        }

        private static double Hash(long i, long j, long k, ulong seed)
        {
            ulong h = (ulong)i * 0x9E3779B97F4A7C15UL ^ (ulong)j * 0xC2B2AE3D27D4EB4FUL ^ (ulong)k * 0x165667B19E3779F9UL ^ seed;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29; h *= 0x94D049BB133111EBUL; h ^= h >> 32;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }

        // ── the ground's grains: the planet's own (GrainField), the same ones its ground is the top of ──
        private Universe.Gen.GrainField.Mix _gm; private ulong _gseed; private double _R, _sx, _sy, _sz, _ax, _ay, _az, _nx, _ny, _nz, _he, _hn;
        private sealed class GrainRec { public long Key; public double X, Y, Z, Rad; public Mineral M; public float Shade; public int Layer; }
        private readonly Dictionary<long, GrainRec> _grains = new Dictionary<long, GrainRec>();
        private double _gcx = double.NaN, _gcz, _greach; private readonly List<GrainRec> _gnear = new List<GrainRec>();
        /// <summary>The planet the matter is on: its radius (km), the spot's unit direction, its grains (the mix and seed its
        /// ground uses) and the land's slope there (east and north, km per km), so the grains are the planet's own.</summary>
        public void SetGround(double radiusKm, double dx, double dy, double dz, string mixId, ulong grainSeed, double slopeE, double slopeN)
        {
            _gm = Universe.Gen.GrainField.For(mixId); _gseed = grainSeed; _R = radiusKm; _sx = dx; _sy = dy; _sz = dz; _he = slopeE; _hn = slopeN;
            Universe.Gen.GrainField.Axes(dx, dy, dz, out _ax, out _ay, out _az, out _nx, out _ny, out _nz);
            _grains.Clear(); _gcx = double.NaN; _gcx2 = double.NaN;
            if (_gm != null && !_noGrains) { Grain = _gm.Size; }
        }
        // the grains within reach of (cx, cz), where they lie in the matter's frame (east, up, north from the spot), cached
        private double _gcx2 = double.NaN, _gcz2, _greach2; private readonly List<GrainRec> _gnear2 = new List<GrainRec>();
        private List<GrainRec> NearGrains(double cx, double cz, double reach)
        {
            bool small = reach <= Grain * 4.5;                                           // round the point you dive into, or all in view
            var outL = small ? _gnear : _gnear2;
            if (_gm == null) return outL;
            double ocx = small ? _gcx : _gcx2, ocz = small ? _gcz : _gcz2, ore = small ? _greach : _greach2;
            if (!double.IsNaN(ocx) && Math.Abs(cx - ocx) < Grain * 0.05 && Math.Abs(cz - ocz) < Grain * 0.05 && Math.Abs(reach - ore) < reach * 0.25) return outL;
            if (small) { _gcx = cx; _gcz = cz; _greach = reach; } else { _gcx2 = cx; _gcz2 = cz; _greach2 = reach; }
            outL.Clear();
            double px = _sx * _R + _ax * cx + _nx * cz, py = _sy * _R + _ay * cx + _ny * cz, pz = _sz * _R + _az * cx + _nz * cz, pl = Math.Sqrt(px * px + py * py + pz * pz);
            var list = new List<Universe.Gen.GrainField.Grain>(); Universe.Gen.GrainField.Near(px / pl, py / pl, pz / pl, _R, reach, _gm, _gseed, list);
            foreach (var g in list)
            {
                GrainRec rec;
                if (!_grains.TryGetValue(g.Key, out rec))
                {
                    double ox = (g.Dx - _sx) * _R, oy = (g.Dy - _sy) * _R, oz = (g.Dz - _sz) * _R;
                    double x = ox * _ax + oy * _ay + oz * _az, z = ox * _nx + oy * _ny + oz * _nz;
                    Mineral m; _minerals.TryGetValue(_gm.Minerals[g.Mineral], out m);
                    rec = new GrainRec { Key = g.Key, X = x, Z = z, Y = _he * x + _hn * z - g.Depth, Rad = g.Rad, M = m, Shade = g.Shade, Layer = (int)(g.Key & 1) };   // on the land's own slope
                    if (_grains.Count > 200000) _grains.Clear();
                    _grains[g.Key] = rec;
                }
                if (rec.M != null) outL.Add(rec);
            }
            return outL;
        }
        // the grain that is the top of the ground at (x, z): the one whose face is highest there
        private GrainRec TopGrain(double x, double z)
        {
            GrainRec best = null; double bestY = double.MinValue;
            foreach (var g in NearGrains(_fx, _fz, Grain * 4))
            {
                double y; if (!GrainTop(g.Key, 0, g.X, g.Y, g.Z, g.Rad, x, z, Math.Max(_scale / 40, AtomSpacing), out y)) continue;
                if (y > bestY) { bestY = y; best = g; }
            }
            if (best == null)                                                            // in a gap: the nearest grain
            { double bd = double.MaxValue; foreach (var g in _gnear) { double d = (g.X - x) * (g.X - x) + (g.Z - z) * (g.Z - z); if (d < bd) { bd = d; best = g; } } }
            return best;
        }
        private GrainRec _focusRec; private double _focusAtX = double.NaN, _focusAtZ;
        private GrainRec FocusRec { get { if (_focusRec == null || _focusAtX != _fx || _focusAtZ != _fz) { _focusRec = TopGrain(_fx, _fz); _focusAtX = _fx; _focusAtZ = _fz; } return _focusRec; } }

        // the mineral of a grain (by its key); a mix's mineral picked by (i, j) for a speck of the ground
        private Mineral GrainMineral(long i, long j)
        {
            GrainRec rec; if (j == 0 && _grains.TryGetValue(i, out rec)) return rec.M;
            return MixPick(i, j);
        }
        private Mineral MixPick(long i, long j)
        {
            if (_mix == null) return null;
            double total = 0; foreach (var kv in _mix) total += kv.Value;
            double t = Hash(i, j, 7, _seed) * total;
            foreach (var kv in _mix) { t -= kv.Value; if (t <= 0) return kv.Key; }
            return _mix[_mix.Count - 1].Key;
        }

        private Element El(string sym) { Element e; return _elements.TryGetValue(sym, out e) ? e : null; }
        // ── decay before your eyes: the universe's clock here, and each unstable atom's own moment to go ──
        private double _simSec, _simPerReal = 1, _bornSec = double.NaN; private string _speedName = "";
        /// <summary>The universe's time (seconds), how many of them pass a real second, and the speed's name: each unstable atom here
        /// decays at its own moment by its half-life (content/elements.unt), counted from when this matter came into view.</summary>
        public void SetClock(double simSec, double simPerRealSec, string speedName) { _simSec = simSec; _simPerReal = Math.Max(1e-30, simPerRealSec); _speedName = speedName; if (double.IsNaN(_bornSec)) _bornSec = simSec; }
        private static double HalfLifeSeconds(string t)
        {
            if (string.IsNullOrEmpty(t)) return 0; var w = t.Trim().Split(' '); double v; var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (w.Length < 2 || !double.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out v)) return 0;
            string u = w[w.Length - 1]; if (w.Length >= 3) { if (w[1] == "thousand") v *= 1e3; else if (w[1] == "million") v *= 1e6; else if (w[1] == "billion") v *= 1e9; }
            return u.StartsWith("millisecond") ? v * 1e-3 : u.StartsWith("second") ? v : u.StartsWith("minute") ? v * 60 : u.StartsWith("hour") ? v * 3600 : u.StartsWith("day") ? v * 86400 : v * 31557600.0;
        }
        private readonly Dictionary<Element, Element> _daughters = new Dictionary<Element, Element>();
        // what an unstable atom becomes: alpha two down the table and four lighter; beta-minus one up; electron capture (or beta-plus) one
        // down; fission about two fifths of it (the heavier half shown)
        private Element DaughterOf(Element e)
        {
            Element d; if (_daughters.TryGetValue(e, out d)) return d;
            string how = e.Decays ?? ""; int z = how == "alpha" ? e.Z - 2 : how == "beta-minus" ? e.Z + 1 : how == "electron-capture" || how == "beta-plus" ? e.Z - 1 : how == "fission" ? (int)Math.Round(e.Z * 0.6) : e.Z;
            Element b; if (z == e.Z || !_byZ.TryGetValue(z, out b)) return _daughters[e] = null;
            int A = how == "alpha" ? e.A - 4 : how == "fission" ? (int)Math.Round(e.A * 0.6) : e.A;
            d = new Element { Symbol = b.Symbol, Name = b.Name, Z = b.Z, A = Math.Max(b.Z, A), R = b.R, G = b.G, B = b.B, Radius = b.Radius, Cov = b.Cov, Chi = b.Chi, Shells = b.Shells, HalfLifeS = 0, Decays = "" };   // (one step shown; its own decays follow in their own time)
            return _daughters[e] = d;
        }
        // the atom as it is now: itself, or what it has decayed into (its moment from its own hash and its half-life: half of them gone
        // after one half-life, as the law has it); sinceReal: real seconds since it went (the particle still flying), -1 if not
        private Element Daughter(Element e, long key, out double sinceReal, out Element parent)
        {
            sinceReal = -1; parent = null; if (e == null || e.HalfLifeS <= 0 || double.IsNaN(_bornSec)) return e;
            double u = Math.Max(1e-12, Hash(key, 7701, 7702, _seed)), tD = _bornSec + e.HalfLifeS * (-Math.Log(u) / Math.Log(2));
            if (_simSec < tD) return e;
            var d = DaughterOf(e); if (d == null) return e;
            parent = e; sinceReal = (_simSec - tD) / _simPerReal; return d;
        }
        private static long AtomKey(double x, double y, double z) { return (long)Math.Round(x / 1e-14) * 73856093L ^ (long)Math.Round(y / 1e-14) * 19349663L ^ (long)Math.Round(z / 1e-14) * 83492791L; }
        private static string DecayWords(string how) { return how == "alpha" ? "alpha decay: two protons and two neutrons flung out" : how == "beta-minus" ? "beta-minus decay: a neutron turned into a proton, an electron flung out" : how == "electron-capture" ? "electron capture: a proton turned into a neutron" : how == "beta-plus" ? "beta-plus decay: a proton turned into a neutron, a positron flung out" : how == "fission" ? "fission: the nucleus split in two" : how; }
        private static string Span(double s)
        {
            if (s < 1e-3) return (s * 1e6).ToString("0.#") + " microseconds"; if (s < 1) return (s * 1e3).ToString("0.#") + " milliseconds"; if (s < 90) return s.ToString("0.#") + " seconds";
            if (s < 5400) return (s / 60).ToString("0.#") + " minutes"; if (s < 172800) return (s / 3600).ToString("0.#") + " hours"; if (s < 3.156e7 * 2) return (s / 86400).ToString("0.#") + " days";
            double y = s / 31557600.0; return y < 1000 ? y.ToString("0") + " years" : y < 1e6 ? (y / 1e3).ToString("0.#") + " thousand years" : y < 1e9 ? (y / 1e6).ToString("0.#") + " million years" : y < 1e12 ? (y / 1e9).ToString("0.#") + " billion years" : y.ToString("0.#E+0") + " years";
        }
        // the particle flung out of a decayed atom, still flying: an alpha (a tight knot of two protons and two neutrons, yellow-white),
        // an electron (a blue-white spark), or the other half of a split nucleus; out along its own way, a share of the view a second,
        // gone from view in a few seconds
        private void DecayFlash(List<StarVertex> glow, List<StarVertex> solid, Element parent, double x, double y, double z, double since, double s)
        {
            if (parent == null || since < 0 || since > 4) return;
            string how = parent.Decays ?? ""; long k = AtomKey(x, y + _yOff, z);
            double th = Math.Acos(1 - 2 * Hash(k, 1, 7703, _seed)), ph = Hash(k, 2, 7703, _seed) * Math.PI * 2, dx = Math.Sin(th) * Math.Cos(ph), dy = Math.Cos(th), dz = Math.Sin(th) * Math.Sin(ph);
            double d = (how == "beta-minus" || how == "beta-plus" ? 0.9 : 0.5) * s * since; float f = (float)Math.Max(0, 1 - since / 4);
            double px = x + dx * d, py = y + dy * d, pz = z + dz * d;
            if (how == "alpha" || how == "fission")
            {
                int n = how == "alpha" ? 4 : (int)Math.Max(4, Math.Round(parent.A * 0.4));
                double R = (how == "alpha" ? 1.2 : 0.6 * Math.Pow(n, 1 / 3.0) * 1.8) * 1e-18;
                for (int q = 0; q < Math.Min(n, 60); q++)
                {
                    double u = q * 2.399963, v = Math.Acos(1 - 2 * ((q * 0.618034) % 1)), rr = R * Math.Pow((q + 0.5) / Math.Min(n, 60), 1 / 3.0); bool pr = q % 2 == 0;
                    Add(solid, px + rr * Math.Sin(v) * Math.Cos(u), py + rr * Math.Cos(v), pz + rr * Math.Sin(v) * Math.Sin(u), Nucleon / 0.75, pr ? 0.9f : 0.6f, pr ? 0.22f : 0.65f, pr ? 0.2f : 0.75f, s);
                }
                Add(glow, px, py, pz, Math.Max(s * 0.008, R * 3), 1f * f, 0.95f * f, 0.6f * f, s);
            }
            else Add(glow, px, py, pz, s * 0.006, 0.7f * f, 0.85f * f, 1f * f, s);
        }
        private double _focusSince = -1; private Element _focusParent;

        // the atom right under you: one of the focus grain's own atoms (picked from its formula by the spot)
        private long FocusGI { get { return (long)Math.Round(_fx / Grain); } }
        // the grain you dive into: its size, and its shade (so its face can carry on in exactly its colour)
        private double FocusRad { get { var g = FocusRec; return g != null ? g.Rad : Grain * 0.4; } }
        private float FocusShade { get { var g = FocusRec; return g != null ? g.Shade : 1f; } }
        private long FocusGJ { get { return (long)Math.Round(_fz / Grain); } }
        private Mineral FocusMineral { get { return MaterialAt(_fx, _fz); } }
        // what the matter is at a point of the surface: a thing lying there (a clay platelet is kaolinite, a coating or a
        // nanoparticle hematite, the water film water), else the grain's own mineral; worked out from where the point is, never
        // from what you picked
        private Mineral MaterialAt(double x, double z)
        {
            for (int t = 0; t < _surf.Count; t++)
            {
                var st = _surf[t];
                double cell = st.Hi * st.Cell; long ci = (long)Math.Floor(x / cell), cj = (long)Math.Floor(z / cell);
                for (long i = ci - 1; i <= ci + 1; i++)
                    for (long j = cj - 1; j <= cj + 1; j++)
                    {
                        if (Hash(i, j, 100 + t, _seed) > st.Chance) continue;
                        double tx = (i + Hash(i, j, 101 + t, _seed)) * cell, tz = (j + Hash(i, j, 102 + t, _seed)) * cell, size = st.Lo + (st.Hi - st.Lo) * Hash(i, j, 103 + t, _seed);
                        if ((tx - x) * (tx - x) + (tz - z) * (tz - z) < size * size * 0.25) { var om = ThingMat(st, i, j); if (om != null) return om; }
                    }
            }
            var fg = FocusRec; return fg != null ? fg.M : MixPick(0, 0);             // the grain that is the ground's top here (no grains, as in the sea: what it is made of)
        }
        // a thing on the grain picked (a clay platelet, a coating, a nanoparticle, the water film): its own mineral is what you go into
        // a mineral's lustre and clarity as one number the mesh shader reads (see MeshPipeline): tenths the lustre, hundredths the clarity
        private static double TryD(string t, double f) { double v; return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : f; }
        // what one surface thing is made of: its own mineral, or (a speck of the ground) a mineral of this ground's mix
        private Mineral ThingMat(SurfThing st, long i, long j) { return st.FromGround ? MixPick(i * 7 + 1000003, j * 13 + 7) : st.M; }
        private static float LustreCode(string lustre, string clarity)
        {
            double l = lustre == "vitreous" || lustre == "adamantine" ? 0.9 : lustre == "metallic" || lustre == "submetallic" ? 0.8 : lustre == "pearly" || lustre == "silky" ? 0.7
                : lustre == "resinous" || lustre == "waxy" || lustre == "greasy" ? 0.6 : lustre == "wet" ? 0.5 : 1.0;
            if (l >= 1) return 1;
            return (float)(l + (clarity == "clear" ? 0.02 : clarity == "translucent" ? 0.01 : 0) + 0.002);
        }
        // where a molecule of matter with no crystal is: on its site for a solid; loosely packed for a liquid, each site shaken
        // off its place (no two layers alike). It does not sway: an atom's thermal motion is ten trillion times a second, which
        // no eye could see as movement (only as the blur its cloud already is), and a slow sway drawn for it only made the view
        // wobble, the atom you were on with it
        private double SiteA(Mineral m) { return m != null && m.Spacing > 0 ? m.Spacing : AtomSpacing; }
        private void SitePos(Mineral m, long i, long j, long k, out double x, out double y, out double z)
        {
            double a = SiteA(m); x = i * a; y = k * a; z = j * a;
            if (m == null || !m.Liquid) return;
            x += (Hash(i, j, k + 80, _seed) - 0.5) * a * 0.7;
            y += (Hash(i, j, k + 81, _seed) - 0.5) * a * 0.5;
            z += (Hash(i, j, k + 82, _seed) - 0.5) * a * 0.7;
        }
        private long FocusAI { get { return (long)Math.Round(_fx / SiteA(FocusMineral)); } }
        private long FocusAJ { get { return (long)Math.Round(_fz / SiteA(FocusMineral)); } }
        private Element SiteAtom(Mineral m, long i, long j, long k)
        {
            if (m == null) return null;
            var e0 = m.Molecule != null ? El(Traced(m, m.Molecule[0].Key, i, j, k))            // a molecule's first atom sits on the site (or a dissolved ion in a molecule's place)
                : El(Traced(m, m.Atoms[(int)(Hash(i, j, k + 20, _seed) * m.Atoms.Length) % m.Atoms.Length], i, j, k));
            double sr; Element par; return Daughter(e0, i * 9176 + j * 131 + k, out sr, out par);   // (as it is now: decayed, if its moment has come)
        }
        // the atom on a site, after its mineral's trace elements have had their chance to stand in for it (each on its share of
        // the host's sites, from the site's own hash: fixed in place, the same each time you come back)
        private string Traced(Mineral m, string sym, long i, long j, long k)
        {
            KeyValuePair<string, double>[] tr; if (m == null || m.Traces == null || sym == null || !m.Traces.TryGetValue(sym, out tr)) return sym;
            double r = Hash(i * 131 + 7, j * 17 + 3, k + 120, _seed), acc = 0;
            for (int t = 0; t < tr.Length; t++) { acc += tr[t].Value; if (r < acc) return tr[t].Key; }
            return sym;
        }
        /// <summary>Whether this element is a trace in the mineral: not one of its formula's atoms, standing in for one.</summary>
        private static bool IsTrace(Mineral m, Element e) { if (m == null || e == null || m.Traces == null) return false; foreach (var a in m.Atoms) if (a == e.Symbol) return false; return true; }
        private static string TraceHost(Mineral m, Element e) { if (m == null || m.Traces == null || e == null) return null; foreach (var kv in m.Traces) foreach (var t in kv.Value) if (t.Key == e.Symbol) return kv.Key; return null; }
        private static double TraceShare(Mineral m, Element e) { if (m == null || m.Traces == null || e == null) return 0; foreach (var kv in m.Traces) foreach (var t in kv.Value) if (t.Key == e.Symbol) return t.Value; return 0; }
        // the mineral's trace elements, for its card: each with its share, from the most common down
        private string TraceWords(Mineral m)
        {
            if (m == null || m.Traces == null) return null;
            var all = new List<KeyValuePair<string, double>>();
            foreach (var kv in m.Traces) foreach (var t in kv.Value) { var e = El(t.Key); if (e != null) all.Add(new KeyValuePair<string, double>(e.Name.ToLowerInvariant(), t.Value * CountOf(m, kv.Key) / Math.Max(1, m.Atoms.Length))); }
            if (all.Count == 0) return null; all.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            foreach (var a in all) parts.Add(a.Key + " " + (a.Value >= 0.0001 ? (a.Value * 100).ToString(a.Value >= 0.01 ? "0.#" : "0.##") + "%" : a.Value >= 1e-7 ? (a.Value * 1e6).ToString("0.#") + " ppm" : (a.Value * 1e9).ToString("0.##") + " ppb"));
            return string.Join(", ", parts.ToArray());
        }
        private static int CountOf(Mineral m, string sym) { int n = 0; foreach (var a in m.Atoms) if (a == sym) n++; return n; }

        // the atom right under you: the site of the focus grain's lattice nearest the point you dive into
        private Element FocusAtom() { double x, y, z; return Focus(out x, out y, out z); }

        // the atom you dive into and where it is: the nearest atom of the top layer to the point you dive into
        private Element Focus(out double x, out double y, out double z)
        {
            var m = FocusMineral; x = FocusAI * AtomSpacing; z = FocusAJ * AtomSpacing; y = 0;
            if (_space) { x = y = z = 0; return GasAtom(0, 0, 0); }                     // in the gas: the atom right where you dive
            if (m == null) return null;
            if (Structured(m))
            {
                // the atom nearest the point you dive into, in any layer (worked out from where it is)
                Element best = null; double bd = double.MaxValue, fy = _fy; long bestKey = 0;                   // (heights here measured from the surface under you)
                foreach (var at in Crystal(m, _fx, _fz, AtomSpacing * 3, AtomSpacing * 8, 4000))
                {
                    double ay = at.Y - AtomRadius(at.E), d = (at.X - _fx) * (at.X - _fx) + (ay - fy) * (ay - fy) + (at.Z - _fz) * (at.Z - _fz);
                    if (d < bd) { bd = d; best = at.E; x = at.X; y = ay - _yOff; z = at.Z; bestKey = AtomKey(at.X, at.Y, at.Z); }
                }
                if (best != null) return Daughter(best, bestKey, out _focusSince, out _focusParent);
            }
            long fk = (long)Math.Max(-7, Math.Min(0, Math.Round(_fy / SiteA(m))));               // the layer you are at
            var e = SiteAtom(m, FocusAI, FocusAJ, fk); double sx, sy, sz; SitePos(m, FocusAI, FocusAJ, fk, out sx, out sy, out sz);
            x = sx; z = sz; y = sy - AtomRadius(e) - _yOff; return e;   // (SiteAtom gives it as it is now)
        }

        // ── real crystal structures, where they are in: rock salt (halite) and the silica network (quartz) ──
        private sealed class CAtom { public Element E; public double X, Y, Z; }
        // a crystal from its data (content/minerals.unt): its unit cell (edges in angstroms, angles in degrees) and every atom
        // site in it (fractional positions; a site shared by several kinds of atom holds each as often as the data says)
        private sealed class Lattice
        {
            public double[] A, B, C;                 // the cell's edges in km, world x, height, z (c points up, out of the face)
            public double[][] Pos; public string[][] Els; public double[][] Occ; public double Area;
            public static Lattice From(ContentEntry e)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
                var c = e.Get("cell", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); var sites = e.All("site");
                if (c.Length != 6 || sites.Count == 0) return null;
                var v = new double[6]; for (int q = 0; q < 6; q++) if (!double.TryParse(c[q], fs, ci, out v[q])) return null;
                double a = v[0] * 1e-13, b = v[1] * 1e-13, cc = v[2] * 1e-13, al = v[3] * Math.PI / 180, be = v[4] * Math.PI / 180, ga = v[5] * Math.PI / 180;
                double cx = cc * Math.Cos(be), cy = cc * (Math.Cos(al) - Math.Cos(be) * Math.Cos(ga)) / Math.Sin(ga), cz = Math.Sqrt(Math.Max(0, cc * cc - cx * cx - cy * cy));
                var L = new Lattice { A = new[] { a, 0, 0 }, B = new[] { b * Math.Cos(ga), 0, b * Math.Sin(ga) }, C = new[] { cx, cz, cy } };
                L.Area = Math.Abs(L.A[0] * L.B[2] - L.A[2] * L.B[0]);
                var pos = new List<double[]>(); var els = new List<string[]>(); var occ = new List<double[]>();
                foreach (var line in sites)
                {
                    var w = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length < 4) continue;
                    double x, y, z; if (!double.TryParse(w[0], fs, ci, out x) || !double.TryParse(w[1], fs, ci, out y) || !double.TryParse(w[2], fs, ci, out z)) continue;
                    var el = new List<string>(); var oc = new List<double>();
                    for (int q = 3; q < w.Length; q++)
                    {
                        double o; if (double.TryParse(w[q], fs, ci, out o)) { if (oc.Count > 0) oc[oc.Count - 1] = o; continue; }
                        el.Add(w[q]); oc.Add(1);
                    }
                    if (el.Count == 0) continue;
                    pos.Add(new[] { x, y, z }); els.Add(el.ToArray()); occ.Add(oc.ToArray());
                }
                if (pos.Count == 0) return null;
                L.Pos = pos.ToArray(); L.Els = els.ToArray(); L.Occ = occ.ToArray(); return L;
            }
        }
        // a molecule from the data ("C 0 0 0, H 0.063 0.063 0.063, ..." in nm, from its first atom)
        private static KeyValuePair<string, double[]>[] MoleculeFrom(string t)
        {
            if (string.IsNullOrEmpty(t)) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var l = new List<KeyValuePair<string, double[]>>();
            foreach (var part in t.Split(','))
            {
                var w = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length != 4) continue;
                l.Add(P(w[0], double.Parse(w[1], ci) * 1e-12, double.Parse(w[2], ci) * 1e-12, double.Parse(w[3], ci) * 1e-12));
            }
            return l.Count > 0 ? l.ToArray() : null;
        }
        private bool Structured(Mineral m) { return m != null && m.Lat != null; }
        private string StructureName(Mineral m) { return m == null || m.Structure.Length == 0 ? "" : ", " + m.Structure; }

        // the atoms of a crystal within reach of (cx, cz): its face at height 0, down to 'depth' below it, at most 'budget' atoms
        // (the reach shrinks to fit). Each site's atom from its cell and site (a shared site picks by the data's shares); a
        // molecule's other atoms round each of its first atoms, turned its own way.
        private List<CAtom> Crystal(Mineral m, double cx, double cz, double reach, double depth, int budget)
        {
            var list = new List<CAtom>(); var L = m.Lat; if (L == null) return list;
            int per = L.Pos.Length * (m.Molecule != null ? m.Molecule.Length : 1);
            long nk = (long)Math.Max(1, Math.Ceiling(depth / Math.Max(1e-20, L.C[1])));
            double most = Math.Sqrt(budget * L.Area / Math.Max(1, per * nk)) * 0.5; reach = Math.Min(reach, most);
            // cells covering the square round (cx, cz): solve x = i*A + j*B in the face's plane
            double det = L.A[0] * L.B[2] - L.A[2] * L.B[0];
            for (long k = -1; k >= -nk; k--)
            {
                double ox = cx - k * L.C[0], oz = cz - k * L.C[2];
                double i0 = double.MaxValue, i1 = double.MinValue, j0 = double.MaxValue, j1 = double.MinValue;
                for (int q = 0; q < 4; q++)
                {
                    double px = ox + ((q & 1) == 0 ? -reach : reach), pz = oz + ((q & 2) == 0 ? -reach : reach);
                    double fi = (px * L.B[2] - pz * L.B[0]) / det, fj = (pz * L.A[0] - px * L.A[2]) / det;
                    i0 = Math.Min(i0, fi); i1 = Math.Max(i1, fi); j0 = Math.Min(j0, fj); j1 = Math.Max(j1, fj);
                }
                for (long i = (long)Math.Floor(i0) - 1; i <= (long)Math.Ceiling(i1); i++)
                    for (long j = (long)Math.Floor(j0) - 1; j <= (long)Math.Ceiling(j1); j++)
                        for (int q = 0; q < L.Pos.Length; q++)
                        {
                            var f = L.Pos[q]; double u = i + f[0], w = j + f[1], h = k + f[2];
                            double X = u * L.A[0] + w * L.B[0] + h * L.C[0], Y = h * L.C[1], Z = u * L.A[2] + w * L.B[2] + h * L.C[2];
                            if (Y > 0 || Y < -depth || Math.Abs(X - cx) > reach || Math.Abs(Z - cz) > reach) continue;
                            // which atom is on this site here (a shared site: each as often as the data gives; maybe none)
                            var els = L.Els[q]; var oc = L.Occ[q]; string sym = null; double r = Hash(i * 977 + q, j, k + 90, _seed), acc = 0;
                            for (int t = 0; t < els.Length; t++) { acc += oc[t]; if (r < acc) { sym = els[t]; break; } }
                            if (sym == null) continue;
                            sym = Traced(m, sym, i * 977 + q, j, k);                                   // (its trace elements take the host's place on their share of its sites)
                            var e = El(sym); if (e == null) continue;
                            list.Add(new CAtom { E = e, X = X, Y = Y, Z = Z });
                            if (m.Molecule != null && m.Molecule[0].Key == sym)
                                for (int t = 1; t < m.Molecule.Length; t++)
                                {
                                    var me = El(m.Molecule[t].Key); if (me == null) continue; double dx, dy, dz; Orient(i * 31 + q, j, k, m.Molecule[t].Value, out dx, out dy, out dz);
                                    if (Y + dy <= 0) list.Add(new CAtom { E = me, X = X + dx, Y = Y + dy, Z = Z + dz });
                                }
                        }
            }
            return list;
        }

        // the crystal round you, worked out once and kept while you stay about where you are (not every frame)
        private List<CAtom> _kept; private Mineral _keptM; private double _keptX, _keptZ, _keptReach, _keptDepth;
        private List<CAtom> CrystalKept(Mineral m, double reach, double depth, int budget)
        {
            if (_kept != null && _keptM == m && Math.Abs(_keptDepth - depth) < 1e-20 && reach <= _keptReach && reach > _keptReach * 0.4
                && Math.Abs(_fx - _keptX) < _keptReach * 0.25 && Math.Abs(_fz - _keptZ) < _keptReach * 0.25) return _kept;
            _keptReach = reach * 1.6; _keptX = _fx; _keptZ = _fz; _keptM = m; _keptDepth = depth;
            _kept = Crystal(m, _fx, _fz, _keptReach, depth, budget); return _kept;
        }

        private static KeyValuePair<string, double[]> P(string el, double x, double y, double z) { return new KeyValuePair<string, double[]>(el, new[] { x, y, z }); }

        // turns a molecule's offset by its site's own orientation (from the site's hash)
        private void Orient(long i, long j, long k, double[] v, out double x, out double y, out double z)
        {
            double a = Hash(i, j, k + 40, _seed) * Math.PI * 2, b = Math.Acos(1 - 2 * Hash(i, j, k + 41, _seed)), c = Hash(i, j, k + 42, _seed) * Math.PI * 2;
            double x1 = v[0] * Math.Cos(a) - v[1] * Math.Sin(a), y1 = v[0] * Math.Sin(a) + v[1] * Math.Cos(a), z1 = v[2];
            double y2 = y1 * Math.Cos(b) - z1 * Math.Sin(b), z2 = y1 * Math.Sin(b) + z1 * Math.Cos(b);
            x = x1 * Math.Cos(c) - z2 * Math.Sin(c); z = x1 * Math.Sin(c) + z2 * Math.Cos(c); y = y2;
        }

        // a big count in plain words
        private static string Big(double n)
        {
            string[] names = { "", " thousand", " million", " billion", " trillion", " thousand trillion", " million trillion", " billion trillion" };
            int k = 0; while (n >= 1000 && k < names.Length - 1) { n /= 1000; k++; }
            return n.ToString(n < 10 ? "0.#" : "0") + names[k];
        }

        private static double AtomRadius(Element e)
        {
            if (e == null) return 1e-13;
            if (e.Radius > 0) return e.Radius;
            return (0.7 + 0.5 * Math.Min(1, e.Z / 30.0)) * 1e-13;                  // the rest: a rough size from its number
        }
        // ── the nucleus by the real forces (content/physics.unt) ──
        private static double _coreFm = 1.7, _spacingFm = 1.8, _rangeFm = 2.6, _kCoulomb = 1.44, _aV = 15.75, _aS = 17.8, _aC = 0.711, _aA = 23.7, _aP = 11.18, _betaC = 0.0155; private static int _alphaZ = 82;
        private static readonly Dictionary<string, double[][]> _nuclei = new Dictionary<string, double[][]>();
        // where each of the nucleus's protons and neutrons sits (fm from its middle; [3] = 1 for a proton): settled by the
        // strong force (each pair pushed apart inside its hard core, pulled together just beyond it, free past its range), the
        // protons' electric push on each other, and the pull of the whole toward its middle; worked out once per isotope
        private static double[][] NucleonsOf(Element e)
        {
            double[][] n; string key = e.Symbol + e.A; if (_nuclei.TryGetValue(key, out n)) return n;
            int A = Math.Max(1, e.A), Z = Math.Min(e.Z, A); n = new double[A][];
            double R = 0.6 * _spacingFm * Math.Pow(A, 1 / 3.0);
            for (int k = 0; k < A; k++)
            {
                double rr = R * Math.Pow((k + 0.5) / A, 1 / 3.0), u = k * 2.399963, v = Math.Acos(1 - 2 * ((k * 0.618034) % 1));
                n[k] = new[] { rr * Math.Sin(v) * Math.Cos(u), rr * Math.Cos(v), rr * Math.Sin(v) * Math.Sin(u), 0 };
            }
            // protons spread evenly through it to start (the weak and strong forces keep their mix; the push sorts them)
            for (int k = 0, p = 0; k < A; k++) if (p < Z && (long)k * Z / A >= p) { n[k][3] = 1; p++; }
            var f = new double[A, 3];
            for (int it = 0; it < 120; it++)
            {
                for (int a = 0; a < A; a++) { f[a, 0] = -n[a][0] * 0.02; f[a, 1] = -n[a][1] * 0.02; f[a, 2] = -n[a][2] * 0.02; }   // held together as one
                for (int a = 0; a < A; a++)
                    for (int b = a + 1; b < A; b++)
                    {
                        double dx = n[b][0] - n[a][0], dy = n[b][1] - n[a][1], dz = n[b][2] - n[a][2], d = Math.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-9, push = 0;
                        if (d < _coreFm) push = (_coreFm - d) * 0.5;                                   // the strong force's hard core
                        else if (d < _rangeFm) push = -(d - _spacingFm) * 0.08 * (_rangeFm - d);        // its pull just beyond
                        if (n[a][3] > 0 && n[b][3] > 0) push += _kCoulomb / (d * d) * 0.01;           // two protons push apart (weaker)
                        dx /= d; dy /= d; dz /= d;
                        f[a, 0] -= dx * push; f[a, 1] -= dy * push; f[a, 2] -= dz * push; f[b, 0] += dx * push; f[b, 1] += dy * push; f[b, 2] += dz * push;
                    }
                for (int a = 0; a < A; a++) { n[a][0] += f[a, 0]; n[a][1] += f[a, 1]; n[a][2] += f[a, 2]; }
            }
            _nuclei[key] = n; return n;
        }
        // what holds it together (MeV), by the semi-empirical mass formula: the strong force's pull less the surface, the
        // protons' electric push, the imbalance of protons and neutrons, and the pairing
        private static double BindingMeV(int Z, int A)
        {
            if (A < 2) return 0;
            int N = A - Z; double pair = (Z % 2 == 0 && N % 2 == 0) ? _aP / Math.Sqrt(A) : (Z % 2 == 1 && N % 2 == 1) ? -_aP / Math.Sqrt(A) : 0;
            return Math.Max(0, _aV * A - _aS * Math.Pow(A, 2 / 3.0) - _aC * Z * (Z - 1) / Math.Pow(A, 1 / 3.0) - _aA * (A - 2.0 * Z) * (A - 2.0 * Z) / A + pair);
        }
        // how it changes, if it does: by the weak force (beta decay) or, past lead, by throwing out alpha particles
        private static string Decay(Element e)
        {
            int A = e.A, Z = e.Z; if (A < 2) return "stable: a lone proton";
            double z0 = A / (2 + _betaC * Math.Pow(A, 2 / 3.0));
            if (Z > _alphaZ) return "unstable: throws out alpha particles (2 protons, 2 neutrons), its protons' electric push outgrowing the strong pull" + (Z > z0 + 0.6 ? "; or a proton becomes a neutron (the weak force)" : "");
            if (Z < z0 - 1.2) return "unstable: a neutron turns into a proton by the weak force (beta-minus: an electron and an antineutrino fly out)";
            if (Z > z0 + 1.2) return "unstable: a proton turns into a neutron by the weak force (beta-plus or electron capture)";
            return e.Z == 43 || e.Z == 61 ? "unstable: no form of it lasts (beta decay by the weak force)" : "stable: the strong force holds it, the weak force has no lower state to take it to";
        }

        private static double NucleusRadius(Element e) { return 0.6 * _spacingFm * 1e-18 * Math.Pow(e == null ? 28 : e.A, 1 / 3.0) + Nucleon; }

        public string MineralName { get { var m = FocusMineral; return m != null ? m.Name : "rock"; } }

        /// <summary>What you are looking at, at this scale.</summary>
        public string Stage(double s)
        {
            var m = FocusMineral; var e = FocusAtom();
            if (_space && e != null && s > AtomRadius(e) * 3)
            {
                string[] kinds = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                string where = _spaceKind == 12 ? "a black hole, inside its horizon, falling" : _spaceKind == 13 ? "a neutron star's crust (iron nuclei in a lattice, electrons racing between them)" : _spaceKind == 11 ? "a star's plasma" + (_plasmaWhere != null ? ", its " + _plasmaWhere : "") + " (bare nuclei and free electrons" + (_plasmaTempK > 0 ? " at about " + _plasmaTempK.ToString("N0") + " K" : ", thousands of degrees") + ")" : _spaceKind == 10 ? "the air (the world's own gases, at about one bar)" : _spaceKind >= 0 && _spaceKind < kinds.Length ? kinds[_spaceKind] : "the space between the stars";
                return "the gas of " + where + ": one atom about every " + ScaleHud.Words(_gasSpacing) + ", mostly hydrogen; the one ahead " + e.Name.ToLowerInvariant();
            }
            string mn = m != null ? m.Name.ToLowerInvariant() + " (" + m.Formula + ")" : "rock";
            if (s > Grain * 1.5) return "grains of " + (_mixId == "sea" ? "the sea" : "the ground") + ", this one " + mn;
            if (s > AtomSpacing * 12) return "the surface of a grain of " + mn;
            if (e == null) return "atoms of " + mn;
            double ar = AtomRadius(e), nr = NucleusRadius(e);
            if (s > ar * 3) return "the atoms of " + mn + "; under you " + e.Name.ToLowerInvariant();
            string sh = string.Join(", ", Array.ConvertAll(e.Shells, x => x.ToString()));
            if (s > nr * 40) return "an atom of " + e.Name.ToLowerInvariant() + ": " + e.Z + " electrons in shells of " + sh + ", round a nucleus far below";
            // below the quark there is no floor: what is there is what is known, which is nothing, said plainly
            if (s < PlanckKm) return "below the Planck length (1.6e-35 m): as far as anyone knows, length itself means nothing here. Nothing is known to be smaller; the numbers simply go on";
            if (s < KnownKm) return "inside a quark, past anything measured: a quark shows no size down to a ten-thousandth of a femtometre (1e-19 m), a point. Below here nothing is known, and the zoom goes on";
            if (s < Nucleon * 8) return "the nucleus of " + e.Name.ToLowerInvariant() + ", close in: each of its " + e.Z + " protons and " + (e.A - e.Z) + " neutrons three quarks, held by gluons";
            return "the nucleus of " + e.Name.ToLowerInvariant() + ": " + e.Z + " protons and " + (e.A - e.Z) + " neutrons";
        }

        private void AddBody(int kind, long i, long j, double x, double y, double z, double size, double s)
        {
            double vx = (x - _fx) / s, vz = (z - _fz) / s, vs = size / s;
            if (Math.Abs(vx) > 6 || Math.Abs(vz) > 6 || (vs < 0.004 && kind < 3) || _bodies.Count > 80000) return;   // only bodies big enough on screen to pick (points always are); room for every atom of a lattice in view and their electrons
            _bodies.Add(new Body { Kind = kind, I = i, J = j, X = x, Y = y + _yOff, Z = z, Size = size });
        }

        private void Add(List<StarVertex> l, double x, double y, double z, double size, float r, float g, float b, double s)
        {
            x -= _fx; z -= _fz;                                                             // around the point you dive into
            double vx = x / s + _ex, vy = (y + _yOff - _fy) / s + _ey, vz = z / s + _ez, vs = size / s;        // view units: one unit = the scale; you are 0.6 above the surface under you
            if (Math.Abs(vx - _ex) > 60 || Math.Abs(vz - _ez) > 60 || vy - _ey < -60 || (vs < 2e-4 && size > 1e-20) || vs > 400) return;   // (electrons are points: kept as the smallest dot on screen)
            l.Add(new StarVertex { X = (float)vx, Y = (float)vy, Z = (float)vz, Size = (float)vs, R = r, G = g, B = b, A = 1 });
        }

        private double _yOff;
        // where the point you dive into is from your eye, in view units (one unit = the scale): straight below at first; looking
        // round (dragging) moves your eye round it, as round a planet, so it stays in the middle
        private double _ex = 0, _ey = -0.6, _ez = 0;
        /// <summary>Your view's way (unit, in the matter's frame): the point you dive into lies 0.6 of the scale along it.</summary>
        public void SetEye(double lx, double ly, double lz) { double l = Math.Sqrt(lx * lx + ly * ly + lz * lz); if (l <= 0) return; _ex = lx / l * 0.6; _ey = ly / l * 0.6; _ez = lz / l * 0.6; }                                                              // the surface under you is height 0: everything is moved by this

        // ── a grain's real surface: one shape at every size, so zooming into a grain never swaps it for anything else ──
        // a squashed, turned ellipsoid, roughened by detail on detail (each finer layer half as tall, without end), worked out from
        // the grain's own seed; drawn coarse as a stone from afar, and close in as a patch under you, finer the closer you come.
        // Below about 300 nm its height steps in single atomic layers (0.3 nm), so terraces form along its slopes as on real faces.
        private double VNoise(double x, double y, double z, long key)
        {
            long xi = (long)Math.Floor(x), yi = (long)Math.Floor(y), zi = (long)Math.Floor(z);
            double fx = x - xi, fy = y - yi, fz = z - zi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            Func<long, long, long, double> h = (a, b, c) => Hash(a * 7 + key, b, c + key * 31, _seed);
            double x00 = h(xi, yi, zi) + (h(xi + 1, yi, zi) - h(xi, yi, zi)) * fx, x10 = h(xi, yi + 1, zi) + (h(xi + 1, yi + 1, zi) - h(xi, yi + 1, zi)) * fx;
            double x01 = h(xi, yi, zi + 1) + (h(xi + 1, yi, zi + 1) - h(xi, yi, zi + 1)) * fx, x11 = h(xi, yi + 1, zi + 1) + (h(xi + 1, yi + 1, zi + 1) - h(xi, yi + 1, zi + 1)) * fx;
            double y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }

        // how far the grain's surface is from its middle, along the unit direction (dx, dy, dz), with detail down to 'cell' (km)
        private double GrainSurface(long gi, long gj, double rad, double dx, double dy, double dz, double cell)
        {
            return Universe.Gen.GrainField.Surface(gi, rad, dx, dy, dz, cell, _gseed);   // the same shape the planet's ground gives it
        }

        // the point on top of a grain straight above (x, z), and false if (x, z) is off the grain
        // the one cell the face's coarse shape is read at (the camera's height, the spots to pick, the face's rings): finer detail is the
        // card's, per pixel, so nothing worked out here stands off anything else
        private static double CoarseCell(double s, double frad) { return Math.Max(Math.Max(s / 40, AtomSpacing), s < frad * 1.5 ? frad * 0.004 : 0); }
        private bool GrainTop(long gi, long gj, double cx, double cy, double cz, double rad, double x, double z, double cell, out double y)
        {
            y = cy + rad; double dx = x - cx, dz = z - cz;
            if (dx * dx + dz * dz > rad * rad * 1.6) return false;
            double py = cy + rad * 0.6;
            for (int it = 0; it < 2; it++)
            {
                double ex = dx, ey = py - cy, ez = dz, l = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (l <= 0) return false;
                double rr = GrainSurface(gi, gj, rad, ex / l, ey / l, ez / l, cell);
                double hz2 = rr * rr - dx * dx - dz * dz; if (hz2 <= 0) return false;
                py = cy + Math.Sqrt(hz2);
            }
            y = py; return true;
        }

        // the grain you dive into: where it is and how big
        private void FocusGrain(out long gi, out long gj, out double gx, out double gy, out double gz, out double rad)
        {
            var g = FocusRec;
            if (g == null) { gi = 0; gj = 0; rad = Grain * 0.4; gx = _fx; gz = _fz; gy = -rad * 0.7; return; }
            gi = g.Key; gj = 0; rad = g.Rad; gx = g.X; gz = g.Z; gy = g.Y;
        }

        // the patch of the grain's surface under you, as a fine lit mesh
        private readonly Dictionary<long, double[]> _patchCache = new Dictionary<long, double[]>(); private long _patchGrain = long.MinValue; private int _patchBudget;
                private readonly List<StarVertex> _stoneMesh = new List<StarVertex>(); private readonly List<double[]> _stones = new List<double[]>(); private StarVertex[] _stoneArr = new StarVertex[0]; private UploadBuffer _stoneBuf;
        /// <summary>Whether the grain's face gets its own level floor far out all round (not when the planet's ground is drawn behind the matter).</summary>
        public bool Floor = true;
        private readonly List<StarVertex> _patchMesh = new List<StarVertex>(); private long _patchKey; private double _patchGX, _patchGY, _patchGZ, _patchRad;
        private void GrainPatch(List<StarVertex> mesh, double s)
        {
            long gi, gj; double gx, gy, gz, rad; FocusGrain(out gi, out gj, out gx, out gy, out gz, out rad);
            _patchKey = gi; _patchGX = gx; _patchGY = gy; _patchGZ = gz; _patchRad = rad;
            var m = GrainMineral(gi, gj); if (m == null) return;
            if (_patchGrain != gi || _patchCache.Count > 600000) { _patchCache.Clear(); _patchGrain = gi; }
            float dimF = s < AtomSpacing * 40 ? 0.45f : 1f, lusF = s < AtomSpacing * 40 ? 0.3f : m.Lustre;   // (at the nanometres: matte, dimmed, in greys: no glint blowing the face out to white)
            if (Floor)
            {
                double big = 400, y0 = 0, sxp = 0, szp = 0;
                bool near = s < rad * 0.05 && GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz, rad * 0.004, out y0);
                if (near)
                {
                    // close in (within a twentieth of the grain): a sheet in the face's own plane here, a hair under it, far out all
                    // round, in the face's colour and shading; a quad of the face not made yet, or thrown out as a spike, shows the
                    // sheet, never a black hole with the stars through it
                    double d = Math.Max(2.4 * s, rad * 0.008), ya, yb;
                    if (GrainTop(gi, gj, gx, gy, gz, rad, _fx + d, _fz, rad * 0.004, out ya) && GrainTop(gi, gj, gx, gy, gz, rad, _fx - d, _fz, rad * 0.004, out yb)) sxp = (ya - yb) / (2 * d);
                    if (GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz + d, rad * 0.004, out ya) && GrainTop(gi, gj, gx, gy, gz, rad, _fx, _fz - d, rad * 0.004, out yb)) szp = (ya - yb) / (2 * d);
                    double sink0 = Structured(m) ? AtomSpacing * 3 * Fade(s, AtomSpacing * 40, AtomSpacing * 22) : 0, under = Math.Max(AtomSpacing * 0.4, s * 0.004);
                    float n1 = PackNormal(-sxp, 1, -szp), fr1 = m.R * FocusShade * dimF, fg1 = m.G * FocusShade * dimF, fb1 = m.B * FocusShade * dimF;
                    Func<double, double, StarVertex> sv = (x, z) => new StarVertex { X = (float)(x + _ex), Y = (float)((y0 + sxp * x * s + szp * z * s - sink0 - under + _yOff - _fy) / s + _ey), Z = (float)(z + _ez), Size = n1, R = fr1, G = fg1, B = fb1, A = lusF };
                    _patchMesh.Add(sv(-big, -big)); _patchMesh.Add(sv(big, -big)); _patchMesh.Add(sv(big, big)); _patchMesh.Add(sv(-big, -big)); _patchMesh.Add(sv(big, big)); _patchMesh.Add(sv(-big, big));
                }
                else
                {
                    // farther out: a level floor of the face's own colour under its rings, far out all round, at the bed the grain lies
                    // on (never at its top, which buried the grains round it to their middles): never a hole or the dark past its edge
                    float fr = m.R * FocusShade * 0.85f * dimF, fg = m.G * FocusShade * 0.85f * dimF, fb = m.B * FocusShade * 0.85f * dimF, n0 = PackNormal(0, 1, 0);
                    double fy = (Math.Max(gy - rad * 0.9, -_yOff - 6 * s) + _yOff - _fy) / s + _ey;   // (deep in, just under the face: a backdrop past the rings, never black)
                    Func<double, double, StarVertex> fv = (x, z) => new StarVertex { X = (float)(x + _ex), Y = (float)fy, Z = (float)(z + _ez), Size = n0, R = fr, G = fg, B = fb, A = lusF };
                    _patchMesh.Add(fv(-big, -big)); _patchMesh.Add(fv(big, -big)); _patchMesh.Add(fv(big, big)); _patchMesh.Add(fv(-big, -big)); _patchMesh.Add(fv(big, big)); _patchMesh.Add(fv(-big, big));
                }
            }
            // the grain's face as rings of detail round you: the finest grid under you, then rings each four times coarser out
            // to the whole grain, each on a grid fixed on the grain (never on the camera, so nothing slides as you zoom and
            // move), each point worked out once; the finest eases between its two sizes so nothing pops
            const int N0 = 40; double want = 4.8 * s / N0, lv = Math.Max(0, Math.Log(want / AtomSpacing, 2)), L = Math.Floor(lv), frac = lv - L;   // never finer than the atoms (they are the face from there)
            _patchBudget = 36000;                                                          // new points a frame at most: the rest come in over the next frames, the frame never stalls
            double inner = 0;
            for (int ring = 0; ring < 12; ring++)
            {
                double Lr = L + ring * 2, step = AtomSpacing * Math.Pow(2, Lr);
                double half = (ring == 0 ? 2.4 * s : inner * 4) * 1.02;
                int N = (int)Math.Min(160, Math.Ceiling(2 * half / step / 2) * 2);
                PatchRing(mesh, s, m, gi, gj, gx, gy, gz, rad, Lr, step, N, ring == 0 ? frac : 0, ring == 0 ? 0 : inner * 0.98);
                inner = N / 2 * step;
                if (inner > rad * 1.3 || ring >= 6) break;                                // the grain round you as far as can be seen (never the whole of it at nanometres: it cannot show)
            }
        }
        // whether a point (in the view's units) is in view, with a margin of its own size: what is not is neither worked out nor drawn
        private bool InView(double x, double y, double z, double margin)
        {
            if (!_haveVp) return true; var m = _lastVp;
            double cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw < -margin) return false;
            double lim = Math.Max(0.05, cw) * 1.15 + margin * 2;
            return Math.Abs(cx) <= lim && Math.Abs(cy) <= lim;
        }
        // one ring: a grid of N x N steps round the point you dive into, its quads inside 'hole' (the finer ring's reach) left out
        private void PatchRing(List<StarVertex> mesh, double s, Mineral m, long gi, long gj, double gx, double gy, double gz, double rad, double L, double step, int N, double frac, double hole)
        {
            long ia0 = (long)Math.Round(_fx / step) - N / 2, ib0 = (long)Math.Round(_fz / step) - N / 2;
            Func<int, double> atA = q => (ia0 + q) * step - _fx; Func<int, double> atB = q => (ib0 + q) * step - _fz;
            var hy = new double[N + 1, N + 1]; var ok = new bool[N + 1, N + 1]; var st = new float[N + 1, N + 1, 3];
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    long key = ((long)L * 1000003L + (ia0 + a)) * 2000003L + (ib0 + b); double[] c;   // each point fixed on the grain: worked out once
                    if (!_patchCache.TryGetValue(key, out c) && !InView(atA(a) / s + _ex, _ey, atB(b) / s + _ez, step / s * 2 + 1.5)) { hy[a, b] = 0; ok[a, b] = false; continue; }   // (out of view, with a view and a half to spare for the face's own height: not worked out)
                    if (c == null)
                    {
                        if (_patchBudget-- <= 0) { hy[a, b] = 0; ok[a, b] = false; continue; }      // not worked out yet: next frame
                        double y; bool o = GrainTop(gi, gj, gx, gy, gz, rad, _fx + atA(a), _fz + atB(b), rad * 0.004, out y);   // every ring the same coarse shape (so no ring shows its edge); all finer detail per pixel on the card, the same everywhere   // the coarse shape here; the fine detail is the card's
                        float sr = 1, sg = 1, sb2 = 1;                                     // (its stains too: worked out per pixel on the card)
                        c = new[] { y, o ? 1 : 0, sr, sg, sb2 }; _patchCache[key] = c;
                    }
                    hy[a, b] = c[0]; ok[a, b] = c[1] > 0; st[a, b, 0] = (float)c[2]; st[a, b, 1] = (float)c[3]; st[a, b, 2] = (float)c[4];
                }
            for (int a = 1; a < N; a++)
                for (int b = 1; b < N; b++)
                {
                    // a point standing off its worked-out neighbours by far more than a terrace's step is a spike (a pillar or a pit
                    // on the face): thrown out, and the sheet under the face shows there instead
                    if (!ok[a, b]) continue; double sum = 0; int cnt = 0;
                    if (ok[a - 1, b]) { sum += hy[a - 1, b]; cnt++; } if (ok[a + 1, b]) { sum += hy[a + 1, b]; cnt++; } if (ok[a, b - 1]) { sum += hy[a, b - 1]; cnt++; } if (ok[a, b + 1]) { sum += hy[a, b + 1]; cnt++; }
                    if (cnt >= 2 && Math.Abs(hy[a, b] - sum / cnt) > step * 1.2 + AtomSpacing) ok[a, b] = false;   // (a step off its neighbours is a spike: a black wall across the face otherwise)
                }
            // never a hole with the stars through it: a point not worked out yet, out of view, or thrown out as a spike takes its
            // neighbours' height for the drawing (not for the spike test), so every quad in view is drawn
            var okd = (bool[,])ok.Clone();
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    if (ok[a, b]) continue; double sum = 0; int cnt = 0;
                    if (a > 0 && ok[a - 1, b]) { sum += hy[a - 1, b]; cnt++; } if (a < N && ok[a + 1, b]) { sum += hy[a + 1, b]; cnt++; } if (b > 0 && ok[a, b - 1]) { sum += hy[a, b - 1]; cnt++; } if (b < N && ok[a, b + 1]) { sum += hy[a, b + 1]; cnt++; }
                    if (cnt > 0) { hy[a, b] = sum / cnt; okd[a, b] = true; }
                }
            if (frac > 0)
            {
                // the points the next coarser grid does not have ease toward its shape as this size gives way to it
                var hm = (double[,])hy.Clone(); double ease = frac * frac * (3 - 2 * frac);
                for (int a = 1; a < N; a++)
                    for (int b = 1; b < N; b++)
                    {
                        bool oa = ((ia0 + a) & 1) != 0, ob = ((ib0 + b) & 1) != 0; if (!oa && !ob || !ok[a, b]) continue;
                        // only from neighbours that are worked out: one not yet (or out of view) stands at zero, and easing toward it
                        // pulled a point of the face up into a wall (a fence of grey pillars at the edge of what was made)
                        if (oa && ob ? !(ok[a - 1, b - 1] && ok[a + 1, b - 1] && ok[a - 1, b + 1] && ok[a + 1, b + 1]) : oa ? !(ok[a - 1, b] && ok[a + 1, b]) : !(ok[a, b - 1] && ok[a, b + 1])) continue;
                        double c = oa && ob ? (hy[a - 1, b - 1] + hy[a + 1, b - 1] + hy[a - 1, b + 1] + hy[a + 1, b + 1]) * 0.25 : oa ? (hy[a - 1, b] + hy[a + 1, b]) * 0.5 : (hy[a, b - 1] + hy[a, b + 1]) * 0.5;
                        hm[a, b] = hy[a, b] + (c - hy[a, b]) * ease;
                    }
                hy = hm;
            }
            float sh = FocusShade * (s < AtomSpacing * 40 ? 0.45f : 1f), lusR = s < AtomSpacing * 40 ? 0.3f : m.Lustre;   // (at the nanometres: matte, dimmed: no glint blowing the face out to white)
            double sink = Structured(m) ? AtomSpacing * 3 * Fade(s, AtomSpacing * 40, AtomSpacing * 22) : 0;   // under the atoms once they show
            var nrm = new float[N + 1, N + 1]; var occ = new float[N + 1, N + 1];
            mesh = _patchMesh;                                                           // drawn on its own, with the card working out its detail
            for (int a = 0; a <= N; a++)
                for (int b = 0; b <= N; b++)
                {
                    // its slope and hollow from the neighbours that are worked out (a missing one stands at zero: never from it)
                    int a0 = a > 0 && ok[a - 1, b] ? a - 1 : a, a1 = a < N && ok[a + 1, b] ? a + 1 : a, b0 = b > 0 && ok[a, b - 1] ? b - 1 : b, b1 = b < N && ok[a, b + 1] ? b + 1 : b;
                    double dx = atA(a1) - atA(a0), dz = atB(b1) - atB(b0), sx = a1 > a0 ? (hy[a1, b] - hy[a0, b]) / Math.Max(1e-30, dx) : 0, sz = b1 > b0 ? (hy[a, b1] - hy[a, b0]) / Math.Max(1e-30, dz) : 0;
                    nrm[a, b] = PackNormal(-sx, 1, -sz);                                      // each corner's surface direction: shaded smooth
                    bool four = a1 > a0 && b1 > b0 && a1 - a0 == 2 && b1 - b0 == 2;
                    double hollow = four ? (hy[a0, b] + hy[a1, b] + hy[a, b0] + hy[a, b1]) * 0.25 - hy[a, b] : 0, span = Math.Max(1e-30, (dx + dz) * 0.5);
                    occ[a, b] = (float)Math.Max(0.55, Math.Min(1.08, 1 - hollow / span * 3));   // a hollow sees less of the sky
                }
            float r = m.R * sh, g = m.G * sh, bl = m.B * sh;
            Func<int, int, StarVertex> v = (aa, bb) => new StarVertex { X = (float)(atA(aa) / s + _ex), Y = (float)((hy[aa, bb] - sink + _yOff - _fy) / s + _ey), Z = (float)(atB(bb) / s + _ez), Size = nrm[aa, bb], R = r * occ[aa, bb] * st[aa, bb, 0], G = g * occ[aa, bb] * st[aa, bb, 1], B = bl * occ[aa, bb] * st[aa, bb, 2], A = lusR };
            for (int a = 0; a < N; a++)
                for (int b = 0; b < N; b++)
                {
                    if (!okd[a, b] || !okd[a + 1, b] || !okd[a, b + 1] || !okd[a + 1, b + 1]) continue;
                    if (!InView((atA(a) + step * 0.5) / s + _ex, (hy[a, b] - sink + _yOff - _fy) / s + _ey, (atB(b) + step * 0.5) / s + _ez, step / s * 1.5)) continue;   // only what is in view is drawn
                    // each ring round, never a square: the finer ring inside its hole, its own edge a circle, the grain's body beyond
                    double r00 = Math.Sqrt(atA(a) * atA(a) + atB(b) * atB(b)), r11 = Math.Sqrt(atA(a + 1) * atA(a + 1) + atB(b + 1) * atB(b + 1)), r10 = Math.Sqrt(atA(a + 1) * atA(a + 1) + atB(b) * atB(b)), r01 = Math.Sqrt(atA(a) * atA(a) + atB(b + 1) * atB(b + 1));
                    double rmax = Math.Max(Math.Max(r00, r11), Math.Max(r10, r01)), edge = N / 2 * step * 0.98;
                    if (hole > 0 && rmax < hole) continue;
                    if (s >= FocusRad * 0.05 && Math.Min(Math.Min(r00, r11), Math.Min(r10, r01)) > edge) continue;   // (closer in, with no body to blend into, the rings fill the view)
                    mesh.Add(v(a, b)); mesh.Add(v(a + 1, b)); mesh.Add(v(a + 1, b + 1)); mesh.Add(v(a, b)); mesh.Add(v(a + 1, b + 1)); mesh.Add(v(a, b + 1));
                }
        }

        // ── what sits on a grain's surface, each at its real size, fixed in place (from its cell), each pickable ──
        // (all of them from content/minerals.unt @surface: what it is made of, its sizes, how common, its shape, what it is)
        private sealed class SurfThing { public string Id, Name; public Mineral M; public double Lo, Hi, Chance, Bond, Angle; public bool Flat, Molecule, FromGround, Life; public double Thick = 0.05, Cell = 4, Aspect = 1; public string[] About; }
        private readonly List<SurfThing> _surf = new List<SurfThing>();
        private double _clock;                                                             // the matter's time, for the electrons of things drawn here
        private void SurfaceThings(List<StarVertex> solid, List<StarVertex> glow, List<StarVertex> mesh, double s)
        {
            long fgi, fgj; double fgx, fgy, fgz, frad; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
            for (int t = 0; t < _surf.Count; t++)
            {
                var st = _surf[t]; if (st.Life && !_lifeHere) continue;                     // living things only on a living world
                if (st.Hi / s < 0.004 || st.Lo / s > 3) continue;                           // only at sizes where it shows
                double cell = st.Hi * st.Cell; long n = (long)Math.Ceiling(2 * s / cell);
                if (n > 40) continue;
                long ci0 = (long)Math.Floor(_fx / cell), cj0 = (long)Math.Floor(_fz / cell);
                for (long i = ci0 - n; i <= ci0 + n; i++)
                    for (long j = cj0 - n; j <= cj0 + n; j++)
                    {
                        if (Hash(i, j, 100 + t, _seed) > st.Chance) continue;
                        double x = (i + Hash(i, j, 101 + t, _seed)) * cell, z = (j + Hash(i, j, 102 + t, _seed)) * cell, size = st.Lo + (st.Hi - st.Lo) * Hash(i, j, 103 + t, _seed), top;
                        if (!GrainTop(fgi, fgj, fgx, fgy, fgz, frad, x, z, Math.Max(size * 0.5, AtomSpacing), out top)) continue;
                        double y = top + size * 0.5; var tm = ThingMat(st, i, j); if (tm == null) continue;
                        if (st.Flat)
                        {
                            // a plate or skin lies on the face: it rests on the highest point under it, clear of the face, never in it
                            for (int q = 0; q < 6; q++) { double cq, a6 = q * Math.PI / 3; if (GrainTop(fgi, fgj, fgx, fgy, fgz, frad, x + Math.Cos(a6) * size * 0.5, z + Math.Sin(a6) * size * 0.5, Math.Max(size * 0.25, AtomSpacing), out cq)) top = Math.Max(top, cq); }
                            y = top + size * st.Thick * 0.5 + Math.Max(AtomSpacing, s * 0.003);
                        }
                        if (s < size * 0.4 && (x - _fx) * (x - _fx) + (z - _fz) * (z - _fz) < size * size * 0.25) continue;   // you are at it: its own atoms show instead (by where you are)
                        if (st.Molecule && st.M.Atoms.Length >= 3)
                        {
                            // a molecule drawn atom by atom from its mineral's formula: the first atom in the middle, the next two at the
                            // bond length and angle the data gives, each molecule turned its own way
                            var o = El(st.M.Atoms[0]); var hh = El(st.M.Atoms[1]); if (o == null || hh == null) continue;
                            double oh = st.Bond, half = st.Angle / 2 * Math.PI / 180, turn = Hash(i, j, 104, _seed) * Math.PI * 2;
                            double ball = 1 - Fade(s, AtomSpacing * 4, AtomSpacing * 2);               // close in, each atom becomes its own system
                            float cf = (float)Fade(s, AtomSpacing * 4, AtomSpacing * 2);
                            if (ball > 0.03) AtomSystem(glow, o, x, y, z, s, _clock, i * 31 + j, (float)ball);
                            if (cf > 0.01 && NearLook(x, y, z, s)) Cloud(glow, o, x, y, z, s, _clock, i * 31 + j, cf);
                            for (int q = -1; q <= 1; q += 2)
                            {
                                double hx = q * oh * Math.Sin(half), hy = oh * Math.Cos(half), px = x + hx * Math.Cos(turn), py = y + hy * 0.5, pz = z + hx * Math.Sin(turn);
                                if (ball > 0.03) AtomSystem(glow, hh, px, py, pz, s, _clock, i * 31 + j + q + 7, (float)ball);
                                if (cf > 0.01 && NearLook(px, py, pz, s)) Cloud(glow, hh, px, py, pz, s, _clock, i * 31 + j + q + 7, cf);
                            }
                        }
                        else
                        {
                            if (st.Flat) { if (mesh != null) Plate(mesh, st, tm, x, y, z, size, i, j, s); }   // a six-sided plate (clay) or a thin skin (coating), with real thickness
                            else Stone(mesh, solid, tm, x, y, z, size, i, j, s, st.Aspect);
                        }
                        int nb = _bodies.Count; AddBody(8, i, j, x, y, z, size * 0.5, s); if (_bodies.Count > nb) _bodies[nb].Sym = st.Id;
                    }
            }
        }

        private static double Cov(Element e) { return e.Cov > 0 ? e.Cov : 1.5e-13; }

        // a thin six-sided plate lying on the face, as wide as 'size' and as thick as its data gives, turned its own way;
        // its top and bottom shaded smooth, its rim in facets
        private void Plate(List<StarVertex> mesh, SurfThing st, Mineral m, double x, double y, double z, double size, long i, long j, double s)
        {
            double R = size * 0.5, t = size * st.Thick, turn = Hash(i, j, 105, _seed) * Math.PI;
            float sh = (float)(0.85 + 0.25 * Hash(i, j, 106, _seed)), r = m.R * sh, g = m.G * sh, b = m.B * sh, lu = m.Lustre;
            Func<double, double, double, StarVertex> V = (px, py, pz) => new StarVertex { X = (float)((px - _fx) / s + _ex), Y = (float)((py + _yOff - _fy) / s + _ey), Z = (float)((pz - _fz) / s + _ez), R = r, G = g, B = b, A = lu };
            if (Math.Abs((x - _fx) / s) > 60 || Math.Abs((z - _fz) / s) > 60 || R / s < 0.002) return;
            float up = PackNormal(0, 1, 0), down = PackNormal(0, -1, 0);
            var cx = new double[6]; var cz = new double[6];
            for (int q = 0; q < 6; q++) { double a = turn + q * Math.PI / 3; cx[q] = x + Math.Cos(a) * R; cz[q] = z + Math.Sin(a) * R; }
            for (int q = 0; q < 6; q++)
            {
                int n = (q + 1) % 6;
                var c0 = V(x, y + t * 0.5, z); var c1 = V(cx[q], y + t * 0.5, cz[q]); var c2 = V(cx[n], y + t * 0.5, cz[n]); c0.Size = c1.Size = c2.Size = up;
                mesh.Add(c0); mesh.Add(c1); mesh.Add(c2);
                var d0 = V(x, y - t * 0.5, z); var d1 = V(cx[n], y - t * 0.5, cz[n]); var d2 = V(cx[q], y - t * 0.5, cz[q]); d0.Size = d1.Size = d2.Size = down;
                mesh.Add(d0); mesh.Add(d1); mesh.Add(d2);
                var a0 = V(cx[q], y + t * 0.5, cz[q]); var a1 = V(cx[n], y + t * 0.5, cz[n]); var a2 = V(cx[n], y - t * 0.5, cz[n]); var a3 = V(cx[q], y - t * 0.5, cz[q]);
                mesh.Add(a0); mesh.Add(a1); mesh.Add(a2); mesh.Add(a0); mesh.Add(a2); mesh.Add(a3);
            }
        }
        // a small rounded speck (a silt grain, a nanoparticle): a lumpy ball of its own mineral, shaded smooth, sitting on the face
        private void Stone(List<StarVertex> mesh, List<StarVertex> solid, Mineral m, double x, double y, double z, double size, long i, long j, double s, double aspect = 1)
        {
            double turn = Hash(i, j, 109, _seed) * Math.PI, ca = Math.Cos(turn), sa = Math.Sin(turn), wide = 1 / Math.Sqrt(aspect);
            if (mesh == null || _ico == null) { Add(solid, x, y, z, size * 0.5 / 0.75, m.R, m.G, m.B, s); return; }
            double R = size * 0.5; if (R / s < 0.004 || Math.Abs((x - _fx) / s) > 60 || Math.Abs((z - _fz) / s) > 60) return;
            float sh = (float)(0.8 + 0.3 * Hash(i, j, 107, _seed)), r = m.R * sh, g = m.G * sh, b = m.B * sh;
            foreach (int k in _icoTri)
            {
                var d = _ico[k]; double lump = 1 + 0.18 * (Hash(i * 64 + k, j, 108, _seed) - 0.5) * 2 * 0 + 0.12 * Math.Sin(d.X * 3.1 + i) * Math.Cos(d.Z * 2.7 + j);
                double lx = d.X * aspect * wide, lz = d.Z * wide, rx = lx * ca - lz * sa, rz = lx * sa + lz * ca;   // long along its own way (a rod)
                mesh.Add(new StarVertex { X = (float)((x + rx * R * lump - _fx) / s + _ex), Y = (float)((y + d.Y * R * 0.8 * wide * lump + _yOff - _fy) / s + _ey), Z = (float)((z + rz * R * lump - _fz) / s + _ez),
                    Size = PackNormal(d.X, d.Y / 0.8, d.Z), R = r, G = g, B = b, A = m.Lustre });
            }
        }

        // a bond as it really is: not a stick but the two atoms' outer electrons shared between them. A soft cloud of electron
        // density along the line between them, densest between, with the shared pair moving in it; pulled toward the atom that
        // pulls harder (electronegativity, content/elements.unt). Atoms that differ by 1.7 or more share nothing: one has given
        // its electron to the other and they hold as ions (salt), so nothing is drawn between them.
        private void SharedPair(List<StarVertex> glow, CAtom a, CAtom b, double s, double time, int n, float f)
        {
            double ca = a.E.Chi, cb = b.E.Chi, diff = ca > 0 && cb > 0 ? Math.Abs(ca - cb) : 0;
            if (diff >= 1.7) return;                                                    // ionic: no shared electrons
            double ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, len = Math.Sqrt(ex * ex + ey * ey + ez * ez); if (len < 1e-20) return;
            double toward = ca > 0 && cb > 0 ? (cb > ca ? 1 : -1) * diff / 1.7 * 0.25 : 0, mid = 0.5 + toward;   // nearer the one that pulls harder
            for (int q = 0; q <= 8; q++)
            {
                double t = q / 8.0, bell = Math.Exp(-Math.Pow((t - mid) / 0.28, 2));
                Add(glow, a.X + ex * t, a.Y + ey * t, a.Z + ez * t, len * (0.1 + 0.18 * bell), 0.07f * f * (float)bell, 0.055f * f * (float)bell, 0.025f * f * (float)bell, s);
            }
            // the shared pair: two electrons on opposite sides, circling the line between the atoms and sliding along it
            double ux = ex / len, uy = ey / len, uz = ez / len, px = Math.Abs(uy) < 0.9 ? -uz : 1, py = Math.Abs(uy) < 0.9 ? 0 : 0, pz = Math.Abs(uy) < 0.9 ? ux : 0;
            double pl = Math.Sqrt(px * px + py * py + pz * pz); px /= pl; py /= pl; pz /= pl;
            double qx = uy * pz - uz * py, qy = uz * px - ux * pz, qz = ux * py - uy * px;
            double ph = time * 3.1 + n * 1.7, slide = mid + 0.14 * Math.Sin(time * 1.9 + n), r = len * 0.13, dot = s * 0.0035;
            for (int k = 0; k < 2; k++)
            {
                double ang = ph + k * Math.PI, cx = a.X + ex * slide, cy = a.Y + ey * slide, cz = a.Z + ez * slide;
                Add(glow, cx + (px * Math.Cos(ang) + qx * Math.Sin(ang)) * r, cy + (py * Math.Cos(ang) + qy * Math.Sin(ang)) * r, cz + (pz * Math.Cos(ang) + qz * Math.Sin(ang)) * r, dot, 1f * f, 0.8f * f, 0.25f * f, s);
            }
        }

        // the bonds between the atoms drawn, each as what it is: the two atoms' outer electrons shared between them (never a stick:
        // that is the picture from before the quantum theory and the mapping of the electrons)
        private void Bonds(List<StarVertex> glow, List<CAtom> atoms, double s, double time, float f)
        {
            if (f <= 0.02 || atoms.Count < 2) return;
            double cell = 3.2e-13; var grid = new Dictionary<long, List<int>>();
            Func<double, long> c = v => (long)Math.Floor(v / cell);
            for (int i = 0; i < atoms.Count; i++) { long key = (c(atoms[i].X) * 73856093L) ^ (c(atoms[i].Y) * 19349663L) ^ (c(atoms[i].Z) * 83492791L); List<int> l; if (!grid.TryGetValue(key, out l)) grid[key] = l = new List<int>(); l.Add(i); }
            int bonds = 0;
            for (int i = 0; i < atoms.Count && bonds < 4000; i++)
            {
                var a = atoms[i]; long cx = c(a.X), cy = c(a.Y), cz = c(a.Z);
                for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) for (long dz = -1; dz <= 1; dz++)
                {
                    List<int> l; if (!grid.TryGetValue(((cx + dx) * 73856093L) ^ ((cy + dy) * 19349663L) ^ ((cz + dz) * 83492791L), out l)) continue;
                    foreach (int j in l)
                    {
                        if (j <= i) continue; var b = atoms[j];
                        double ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, d = Math.Sqrt(ex * ex + ey * ey + ez * ez), lim = (Cov(a.E) + Cov(b.E)) * 1.2;
                        if (d < 1e-16 || d > lim) continue;
                        bonds++;
                        SharedPair(glow, a, b, s, time, bonds, f);
                    }
                }
            }
        }

        // a surface direction packed into a vertex's glow (below -1), so the mesh is shaded smooth, not in facets
        private static float PackNormal(double x, double y, double z)
        {
            double l = Math.Abs(x) + Math.Abs(y) + Math.Abs(z); if (l <= 0) return 0; x /= l; y /= l; z /= l;
            double ex = x, ey = z;
            if (y < 0) { double tx = (1 - Math.Abs(z)) * (x >= 0 ? 1 : -1), tz = (1 - Math.Abs(x)) * (z >= 0 ? 1 : -1); ex = tx; ey = tz; }
            int a = (int)Math.Round((ex * 0.5 + 0.5) * 4095), b = (int)Math.Round((ey * 0.5 + 0.5) * 4095);
            return -(2 + (a | (b << 12)));
        }

        private static double Fade(double s, double from, double to) { return Math.Max(0, Math.Min(1, Math.Log(from / s) / Math.Log(from / to))); }

        // the lattice's level: an atom as its electron cloud, a soft glow of its element's colour at its real size (the cloud of where
        // its electrons may be, densest at the middle), its nucleus a spark at the middle; never a ball. Bright enough to read on a
        // capture (at a third of this brightness the level had read as black between the grain's face and the atoms' level)
        private void LatticeCloud(List<StarVertex> glow, Element e, double x, double y, double z, double s, float w)
        {
            if (e == null || w <= 0.02f) return; double ar = AtomRadius(e);
            Add(glow, x, y, z, ar * 1.1, (0.45f + 0.55f * e.R) * w * 0.75f, (0.45f + 0.55f * e.G) * w * 0.75f, (0.45f + 0.55f * e.B) * w * 0.75f, s);
            Add(glow, x, y, z, ar * 0.45, (0.45f + 0.55f * e.R) * w * 0.35f, (0.45f + 0.55f * e.G) * w * 0.35f, (0.45f + 0.55f * e.B) * w * 0.35f, s);
            Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.002), 1f * w, 0.95f * w, 0.9f * w, s);
        }

        // a grain as a lit, lumpy stone: a once-split icosahedron, squashed and turned its own way, each corner pushed in or out
        private static Vec3[] _ico; private static int[] _icoTri;
        private static void MakeIco()
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            var v = new List<Vec3> { new Vec3(-1, (float)t, 0), new Vec3(1, (float)t, 0), new Vec3(-1, (float)-t, 0), new Vec3(1, (float)-t, 0), new Vec3(0, -1, (float)t), new Vec3(0, 1, (float)t),
                new Vec3(0, -1, (float)-t), new Vec3(0, 1, (float)-t), new Vec3((float)t, 0, -1), new Vec3((float)t, 0, 1), new Vec3((float)-t, 0, -1), new Vec3((float)-t, 0, 1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var mid = new Dictionary<long, int>(); var tri = new List<int>();
            Func<int, int, int> m = (a, b) => { long key = Math.Min(a, b) * 1000L + Math.Max(a, b); int idx; if (mid.TryGetValue(key, out idx)) return idx; var p = (v[a] + v[b]) * 0.5f; v.Add(p); mid[key] = v.Count - 1; return v.Count - 1; };
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = m(a, b), bc = m(b, c), ca = m(c, a);
                tri.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            _ico = new Vec3[v.Count]; for (int i = 0; i < v.Count; i++) _ico[i] = v[i].Normalized; _icoTri = tri.ToArray();
        }

        private void Stone(List<StarVertex> l, long gi, long gj, double cx, double cy, double cz, double rad, float r, float g, float b, double s)
        {
            if (_ico == null) MakeIco();
            var pts = new double[_ico.Length * 3];
            for (int k = 0; k < _ico.Length; k++)
            {
                double rr = GrainSurface(gi, gj, rad, _ico[k].X, _ico[k].Y, _ico[k].Z, rad * 0.2);   // the same surface as close in, coarse
                pts[k * 3] = (cx + _ico[k].X * rr - _fx) / s + _ex; pts[k * 3 + 1] = (cy + _ico[k].Y * rr + _yOff - _fy) / s + _ey; pts[k * 3 + 2] = (cz + _ico[k].Z * rr - _fz) / s + _ez;
            }
            foreach (int k in _icoTri)
                l.Add(new StarVertex { X = (float)pts[k * 3], Y = (float)pts[k * 3 + 1], Z = (float)pts[k * 3 + 2], Size = PackNormal(_ico[k].X, _ico[k].Y, _ico[k].Z), R = r, G = g, B = b, A = 1 });   // shaded smooth; its stains and fine detail on the card
        }

        // a far grain: eight faces, squashed and turned its own way
        private void Octa(List<StarVertex> l, long gi, long gj, double cx, double cy, double cz, double rad, float r, float g, float b, double s)
        {
            double sx = (0.8 + 0.4 * Universe.Gen.GrainField.Hash(gi, 16, 0, _gseed)) * rad, sy = (0.6 + 0.35 * Universe.Gen.GrainField.Hash(gi, 17, 0, _gseed)) * rad, sz = (0.8 + 0.4 * Universe.Gen.GrainField.Hash(gi, 18, 0, _gseed)) * rad, turn = Universe.Gen.GrainField.Hash(gi, 19, 0, _gseed) * Math.PI * 2;   // the field's own shape
            double ct = Math.Cos(turn), st = Math.Sin(turn);
            double[][] p = { new[] { sx, 0, 0.0 }, new[] { -sx, 0, 0.0 }, new[] { 0, sy, 0.0 }, new[] { 0, -sy, 0.0 }, new[] { 0, 0, sz }, new[] { 0, 0, -sz } };
            int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
            foreach (int k in f)
            {
                double x = p[k][0] * ct - p[k][2] * st, z = p[k][0] * st + p[k][2] * ct;
                l.Add(new StarVertex { X = (float)((cx + x - _fx) / s + _ex), Y = (float)((cy + p[k][1] + _yOff - _fy) / s + _ey), Z = (float)((cz + z - _fz) / s + _ez), Size = 0, R = r, G = g, B = b, A = 1 });
            }
        }

        // every solid point (the grain's face, atoms, protons and neutrons) as a lit shape: a round ball when it is big on screen,
        // an eight-sided piece when small; a face tile (flat on the ground) stays flat
        private void Solidify(List<StarVertex> solid, List<StarVertex> mesh, int h)
        {
            if (_ico == null) MakeIco();
            int balls = 0;
            // nearest first, so the round balls (and the room in the mesh) go to the atoms closest to you
            var order = new int[solid.Count]; var dd = new float[solid.Count];
            for (int q = 0; q < order.Length; q++) { var w = solid[q]; order[q] = q; dd[q] = w.X * w.X + w.Y * w.Y + w.Z * w.Z; }
            Array.Sort(dd, order);
            foreach (int oi in order)
            {
                var v = solid[oi];
                double rad = v.Size * 0.75, dist = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z), px = rad / Math.Max(1e-9, dist) * h / (2 * Math.Tan(0.45));
                if (px < 0.6 || mesh.Count > 1500000) continue;
                if (v.A < 0.9f)
                {
                    // a tile of the grain's face: flat, side by side with its neighbours, so the face is one surface
                    double hw = v.Size / 0.72 * 0.5;
                    var a0 = new StarVertex { X = (float)(v.X - hw), Y = v.Y, Z = (float)(v.Z - hw), R = v.R, G = v.G, B = v.B, A = 1 };
                    var a1 = a0; a1.X = (float)(v.X + hw); var a2 = a0; a2.X = (float)(v.X + hw); a2.Z = (float)(v.Z + hw); var a3 = a0; a3.Z = (float)(v.Z + hw);
                    mesh.Add(a0); mesh.Add(a1); mesh.Add(a2); mesh.Add(a0); mesh.Add(a2); mesh.Add(a3);
                    continue;
                }
                if (px > 6 && balls < 6000)
                {
                    balls++;
                    foreach (int k in _icoTri) mesh.Add(new StarVertex { X = (float)(v.X + _ico[k].X * rad), Y = (float)(v.Y + _ico[k].Y * rad), Z = (float)(v.Z + _ico[k].Z * rad), Size = PackNormal(_ico[k].X, _ico[k].Y, _ico[k].Z), R = v.R, G = v.G, B = v.B, A = 1 });
                }
                else
                {
                    double[][] p = { new[] { rad, 0, 0.0 }, new[] { -rad, 0, 0.0 }, new[] { 0, rad, 0.0 }, new[] { 0, -rad, 0.0 }, new[] { 0, 0, rad }, new[] { 0, 0, -rad } };
                    int[] f = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
                    foreach (int k in f) mesh.Add(new StarVertex { X = (float)(v.X + p[k][0]), Y = (float)(v.Y + p[k][1]), Z = (float)(v.Z + p[k][2]), Size = PackNormal(p[k][0], p[k][1], p[k][2]), R = v.R, G = v.G, B = v.B, A = 1 });
                }
            }
        }

        /// <summary>The starlight's way in the ground's own frame (x east, y up, z north), for lighting the grains.</summary>
        public float[] Light = { 0.4f, 0.8f, 0.3f };

        private void Build(double s, double time, List<StarVertex> solid, List<StarVertex> glow, List<StarVertex> mesh, bool overlay)
        {
            _yOff = 0; _patchMesh.Clear(); _orbRecs.Clear(); _stoneMesh.Clear(); _stones.Clear();
            if (_space && _spaceKind == 12) { Singularity(glow, s, time); return; }          // inside a black hole: the fall is black, down to the singularity                                                 // (the grain's face: made afresh each frame from its kept points)
            if (!_space && !_noGrains)
            {
                long fgi, fgj; double fgx, fgy, fgz, frad, top; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
                if (GrainTop(fgi, fgj, fgx, fgy, fgz, frad, _fx, _fz, CoarseCell(s, frad), out top)) _yOff = -top;   // you stay the same height above it (on the face: above the coarse shape the face is drawn from, its finer detail only shading; measured against the fine shape the camera sat a few views under the drawn face at nanometres, looking up through it)
            }
            if (_space && s < _gasSpacing * 10 && s > AtomSpacing * 2)
            {
                // the gas round you: its atoms, each a point (far too small to see but as a speck), about a millimetre apart
                double a = _gasSpacing; long n = (long)Math.Min(16, Math.Ceiling(2 * s / a));
                for (long i = -n; i <= n; i++)
                    for (long j = -n; j <= n; j++)
                        for (long k = -n; k <= n; k++)
                        {
                            if (i == 0 && j == 0 && k == 0) continue;
                            var ge = GasAtom(i, j, k); double jx = (Hash(i, j, k + 91, _seed) - 0.5) * a, jy = (Hash(i, j, k + 92, _seed) - 0.5) * a, jz = (Hash(i, j, k + 93, _seed) - 0.5) * a;
                            float gf = (float)Fade(s, _gasSpacing * 10, _gasSpacing * 4);       // fading in as you come within about ten spacings
                            Add(glow, i * a + jx, k * a + jy, j * a + jz, s * 0.002, ge.R * 0.6f * gf, ge.G * 0.6f * gf, ge.B * 0.6f * gf, s);
                        }
            }
            // grains: lumpy stones, each its own mineral from the mix, scattered (not on a grid) in two layers, the top one
            // resting with its tops at the level you dive into; near ones are lit 3D stones, far ones specks of their colour
            // (once you are down at the grain's own surface it is no longer drawn as a stone: you are at its face)
            if (!_noGrains)
            {
                var focusG = FocusRec;
                var nearG = NearGrains(_fx, _fz, Math.Max(Grain * 12, Math.Min((overlay ? 30 : 110) * Grain, 2.2 * s) + Grain));   // always the grains round you, to the horizon
                // the biggest on screen first, so the grains right in front of you always get their real 3D bodies (never a flat
                // eight-sided stand-in, which is only for grains a few pixels across)
                Func<GrainRec, double> onScreen = gq => gq.Rad / Math.Max(1e-30, Math.Sqrt((gq.X - _fx) * (gq.X - _fx) + (gq.Z - _fz) * (gq.Z - _fz) + (0.6 * s - gq.Y) * (0.6 * s - gq.Y)));
                nearG.Sort((p1, p2) => onScreen(p2).CompareTo(onScreen(p1)));
                foreach (var gr in nearG)
                {
                    var m = gr.M; long gi = gr.Key, gj = 0;
                    bool under = gr == focusG && s < FocusRad * 1.5;                             // close in: its fine face is drawn round you (below); its body carries it on out to its curve
                    double rad = gr.Rad, gx = gr.X, gy = gr.Y, gz = gr.Z;
                    float sh = gr.Shade * (gr.Layer == 0 ? 1f : 0.7f);
                    double dx = gx - _fx, dz = gz - _fz, dist = Math.Sqrt(dx * dx + dz * dz + (0.6 * s - gy) * (0.6 * s - gy));
                    double px = rad / Math.Max(1e-30, dist) * _lastH / (2 * Math.Tan(0.45));    // its size on screen, in pixels
                    if (gr.Layer == 0) { int nb = _bodies.Count; AddBody(0, gi, gj, gx, gy, gz, rad, s); if (_bodies.Count > nb) _bodies[nb].Sym = m.Id; }
                    if (overlay || px < 1.5) continue;                                           // over the ground: the ground itself is their top
                    // the grain you are on keeps its whole 3D body while its shape shows (its fine face lies over it round you, in round
                    // rings); closer in than a twentieth of its size its face and atoms are all there is to see
                    if (s < FocusRad * 0.05) continue;                                       // closer than a twentieth of the grain: its face and atoms are all there is; the grains round it are below and behind it, and their coarse bodies, which stand off their true shape by far more than the view, poked through as floating slabs
                    // (no grain bodies of its own: the grains are the ground's, drawn by the ground; here only the one you are on, its face)
                }
            }
            if (overlay) return;                                                         // over the ground: the ground itself is its grains; nothing is laid over it (only the grains to click)
            if (!_noGrains && !_space && mesh != null && s < FocusRad * 1.5 && (s > AtomSpacing * 12 || !Structured(FocusMineral))) GrainPatch(mesh, s);   // (once its crystal's atoms are in, they are the face: no sheet among them)
            if (!_noGrains && !_space && s < FocusRad * 1.5 && s > AtomSpacing * 1.5)
            {
                // the grain's face can be picked anywhere, at the size you are looking at: a grid of spots round you (each its card:
                // the mineral, its make-up, its terraces close in, how high you are above it), sitting on the face's real height
                long fgi, fgj; double fgx, fgy, fgz, frad; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
                double step = s * 0.8; long a0 = (long)Math.Round(_fx / step), b0 = (long)Math.Round(_fz / step);
                for (long a = a0 - 2; a <= a0 + 2; a++)
                    for (long b = b0 - 2; b <= b0 + 2; b++)
                    {
                        double sx = a * step, sz = b * step, sy;
                        if (!GrainTop(fgi, fgj, fgx, fgy, fgz, frad, sx, sz, Math.Max(step, CoarseCell(s, frad)), out sy)) continue;   // (on the same coarse shape the face is drawn from and the camera is held over: a picked spot is on the drawn face, never nanometres off it with the lattice whole views away)
                        AddBody(2, a, b, sx, sy, sz, step * 0.45, s);
                    }
            }
            if (!_noGrains && !_space && s < FocusRad * 1.5) SurfaceThings(solid, glow, mesh, s);
            AirAbove(glow, s);                                                               // the world's air over the ground, molecule by molecule
            if (overlay) return;                                                        // over the ground: only its grains so far
            // the focus grain's surface: detail at every size, fixed in place (cells of powers of ten, two at a time, blended),
            // in the grain's colour; its finest cells take on the colours of its atoms
            var fm = FocusMineral;
            if (fm != null && _noGrains && s > AtomSpacing * 8)                          // (only where there are no grains: the sea; a grain's face is its own surface above)
            {
                double c1 = Math.Pow(10, Math.Floor(Math.Log10(s / 30))), frac = Math.Log10(s / 30) - Math.Floor(Math.Log10(s / 30));
                double atomish = Fade(s, AtomSpacing * 200, AtomSpacing * 20);
                for (int lv = 0; lv < 2; lv++)
                {
                    double c = lv == 0 ? c1 : c1 * 10, wgt = lv == 0 ? 1 - frac : frac;
                    if (wgt < 0.5) continue;                                               // solid tiles: only the main size of the two (they would fight)
                    long n = (long)Math.Ceiling(1.6 * s / c), ci0 = (long)Math.Round(_fx / c), cj0 = (long)Math.Round(_fz / c);
                    if (n > 70) continue;
                    for (long i = ci0 - n; i <= ci0 + n; i++)
                        for (long j = cj0 - n; j <= cj0 + n; j++)
                        {
                            double x = i * c, z = j * c;
                            if ((x - _fx) * (x - _fx) + (z - _fz) * (z - _fz) > 2.6 * s * s) continue;
                            long ci = (long)Math.Floor(x / c), cj = (long)Math.Floor(z / c);
                            float sh = (float)(0.9 + 0.2 * Hash(ci, cj, (long)Math.Round(Math.Log10(c) * 10), _seed));   // gentle, so it carries on from the stone
                            // below about 300 nm the face shows its atomic terraces: flat patches tens of nm wide, each a layer
                            // (0.3 nm) above or below its neighbours
                            double ty = 0;
                            if (c < 3e-10) { const double tw = 4e-11; long ti = (long)Math.Floor(x / tw), tj = (long)Math.Floor(z / tw); ty = -Math.Floor(Hash(ti, tj, 60, _seed) * 4) * 3e-13; sh *= (float)(1 + ty / 3e-13 * 0.08); }
                            float fsh = FocusShade, r = fm.R * fsh, g = fm.G * fsh, b = fm.B * fsh;             // the stone's own colour, carried on
                            if (atomish > 0) { var ae = El(fm.Atoms[(int)(Hash(ci, cj, 5, _seed) * fm.Atoms.Length) % fm.Atoms.Length]); if (ae != null) { r = (float)(r * (1 - atomish) + ae.R * atomish); g = (float)(g * (1 - atomish) + ae.G * atomish); b = (float)(b * (1 - atomish) + ae.B * atomish); } }
                            float f = (float)(1 - Fade(s, AtomSpacing * 40, AtomSpacing * 22));
                            if (f > 0.01) { Add(solid, x, ty - c * 0.05 - (Structured(fm) ? AtomSpacing * 3 * (1 - f) : 0), z, c * 0.72, r * sh, g * sh, b * sh, s); if (solid.Count > 0) { var tv = solid[solid.Count - 1]; tv.A = 0.5f; solid[solid.Count - 1] = tv; } }   // a flat tile of the face
                        }
                }
            }
            // the mineral's atoms, rows about 0.25 nm apart, in its real ratio; one atom right at the point you dive into
            double fax, fay, faz; var focus = Focus(out fax, out fay, out faz);
            if (fm != null && s < AtomSpacing * 40 && Structured(fm))
            {
                // from about 40 atom spacings (some 10 nm) the crystal's real lattice fills the whole view, coming in as a fine
                // grain of atoms over the surface and opening into its rows, layers and bonds as you come in
                // no fading in: an atom is there at its real size, and is drawn once it spans about a pixel (the face's own dots,
                // worked out per pixel on the card, stand for the atoms too small to draw); a bond once it spans a few
                double px = 2.0 * s / Math.Max(1, _viewH); float f = 1;
                var drawnAt = new List<CAtom>(); double bondReach = Math.Min(1.2 * s, AtomSpacing * 22);
                double depth = s < AtomSpacing * 14 ? AtomSpacing * 9 : AtomSpacing * 3;     // close in, deep: solid every way you look
                // one picture at a level, never two at once, and neither the picture from before the quantum theory: the lattice's
                // level (down to about a nanometre, four spacings) is every atom as its electron cloud at its real size, its nucleus a
                // spark at the middle, the bonds the shared electrons between neighbours; the atom's level below it is each atom's
                // nucleus and its electrons on their paths and in their orbitals (an atom is never a ball, a bond never a stick)
                // the levels hand over by degrees, never in one frame: the lattice's clouds (every atom its soft cloud) fade out and
                // the atoms' own systems fade in between five spacings and three; the atom you are at hands over to its inside view
                // between eight widths and four, its cloud fading as its electrons' shells and orbitals come up (it had been one
                // switch at four spacings and another at four radii: black, then orbs in a frame, then the orbitals in a frame)
                double cw = Fade(s, AtomSpacing * 5, AtomSpacing * 3); bool lattice = cw < 0.98;
                foreach (var at0 in CrystalKept(fm, 2.4 * s, depth, 25000))
                {
                    var at = at0; double dSince; Element dPar; { var dE = Daughter(at0.E, AtomKey(at0.X, at0.Y, at0.Z), out dSince, out dPar); if (dE != at0.E) at = new CAtom { E = dE, X = at0.X, Y = at0.Y, Z = at0.Z }; }   // (as it is now: decayed, if its moment has come)
                    double ar = AtomRadius(at.E), ay = at.Y - ar - _yOff;                     // on the grain's real surface
                    if (dSince >= 0) DecayFlash(glow, solid, dPar, at.X, ay, at.Z, dSince, s);   // (its particle still flying out)
                    bool inFocus = at.E == focus && Math.Abs(at.X - fax) < 1e-20 && Math.Abs(ay - fay) < 1e-20 && Math.Abs(at.Z - faz) < 1e-20 && s < Math.Max(ar, AtomSpacing) * 8;
                    float fw = inFocus ? (float)Fade(s, Math.Max(ar, AtomSpacing) * 8, Math.Max(ar, AtomSpacing) * 4) : 0f; if (fw >= 0.98f) continue;   // (handed over by degrees to its inside view below)
                    float sh = (float)(1 + 0.6 * at.Y / AtomSpacing * 0.25); sh = Math.Max(0.45f, sh);
                    // from afar a ball in its colour, growing up out of the face; close in the ball gives way to the atom's own system
                    // (its nucleus and its electrons on their paths), as the electrons are what an atom's size really is
                    if (ar < px * 0.5) continue;                                                 // (too small to show yet: the face's dots stand for it)
                    long akey = (long)(at.X / 1e-14) ^ (long)(at.Z / 1e-14) * 31;   // each atom at its own size, its colour coming in
                    if ((at.X - _fx) * (at.X - _fx) + (at.Z - _fz) * (at.Z - _fz) < bondReach * bondReach) drawnAt.Add(new CAtom { E = at.E, X = at.X, Y = ay, Z = at.Z });
                    // between the face and the atom's own system (from about ten nanometres in to about an atom's width), each atom as
                    // its electron cloud: a soft glow of its element's colour at its real size (the cloud of where its electrons may be,
                    // densest at the middle), its nucleus a spark at the middle; never a ball
                    float lw = (float)((1 - cw) * (1 - fw));
                    if (lw > 0.02f && ar > px * 2) LatticeCloud(glow, at.E, at.X, ay, at.Z, s, sh * lw);
                    // the atom's level: one drawing of each atom (its nucleus and its electrons, each shown once big enough to see), the
                    // ones near where you look with their electrons' paths too; coming up by degrees as the lattice's clouds fade
                    if (cw > 0.02) { float fa = f * sh * (float)cw * (1 - fw); if (NearLook(at.X, ay, at.Z, s)) Cloud(glow, at.E, at.X, ay, at.Z, s, time, akey, fa); else AtomSystem(glow, at.E, at.X, ay, at.Z, s, time, akey, fa); }
                    { int nb = _bodies.Count; AddBody(1, 0, 0, at.X, ay, at.Z, ar, s); if (_bodies.Count > nb) _bodies[nb].Sym = at.E.Symbol; }   // every layer's atoms
                }
                if (AtomSpacing > px * 3) Bonds(glow, drawnAt, s, time, (float)(1 - 0.6 * cw));   // (bonds once they span a few pixels: the shared pairs, dimmer among the electrons at the atom's level)
            }
            else if (fm != null && s < AtomSpacing * 40)
            {
                double a = SiteA(fm); long n = (long)Math.Min(60, Math.Ceiling(2.4 * s / a) + 1), ai0 = FocusAI, aj0 = FocusAJ;
                double px = 2.0 * s / Math.Max(1, _viewH); float f = 1;                               // (no fading in: drawn once big enough to see)
                for (long i = ai0 - n; i <= ai0 + n; i++)
                    for (long j = aj0 - n; j <= aj0 + n; j++)
                        for (long k = 0; k >= -7; k--)
                        {
                            var el = SiteAtom(fm, i, j, k);
                            if (el == null) continue;
                            double ar = AtomRadius(el);
                            bool inFocus = i == ai0 && j == aj0 && k == 0 && s < Math.Max(ar, AtomSpacing) * 8;
                            float fw = inFocus ? (float)Fade(s, Math.Max(ar, AtomSpacing) * 8, Math.Max(ar, AtomSpacing) * 4) : 0f; if (fw >= 0.98f) continue;   // (you are going into this one: handed over by degrees to its inside view below)
                            float sh = (float)(1 - 0.25 * -k);
                            double sx, sy, sz; SitePos(fm, i, j, k, out sx, out sy, out sz);                // its place (a liquid's: loose and moving)
                            var mol = fm.Molecule;
                            if (mol != null && el.Symbol == mol[0].Key)
                            {
                                // a whole molecule on this site, in its real shape, each turned its own way (a dissolved ion in a molecule's place stands alone)
                                for (int q = 1; q < mol.Length; q++)
                                {
                                    var me = El(mol[q].Key); if (me == null) continue; double ox, oy, oz; Orient(i, j, k, mol[q].Value, out ox, out oy, out oz);
                                    double mr = AtomRadius(me) * 0.55;                       // bonded atoms overlap: drawn at their bonded size
                                    AtomSystem(glow, me, sx + ox, sy - ar + oy - _yOff, sz + oz, s, time, i * 7919 ^ j * 104729 ^ k * 31 + q, sh * f * (1 - fw));
                                }
                                AtomSystem(glow, el, sx, sy - ar - _yOff, sz, s, time, i * 7919 ^ j * 104729 ^ k, sh * f * (1 - fw));
                                { int nb = _bodies.Count; AddBody(6, i, j, sx, sy - ar - _yOff, sz, ar * 1.6, s); if (_bodies.Count > nb) _bodies[nb].Sym = fm.Id; }   // the whole molecule
                            }
                            else
                            {
                                if (ar < px * 0.5) continue;
                                double cw2 = Fade(s, AtomSpacing * 5, AtomSpacing * 3); float lw2 = (float)((1 - cw2) * (1 - fw));
                                if (ar > px * 2) LatticeCloud(glow, el, sx, sy - ar - _yOff, sz, s, sh * lw2);   // (above the atoms' level each as its cloud, as a crystal's; the hand-overs by degrees, the same)
                                if (cw2 > 0.02) { float fa = sh * f * (float)cw2 * (1 - fw); if (NearLook(sx, sy - ar - _yOff, sz, s)) Cloud(glow, el, sx, sy - ar - _yOff, sz, s, time, i * 7919 ^ j * 104729 ^ k, fa); else AtomSystem(glow, el, sx, sy - ar - _yOff, sz, s, time, i * 7919 ^ j * 104729 ^ k, fa); }
                            }
                            { int nb = _bodies.Count; AddBody(1, i, j, sx, sy - ar - _yOff, sz, ar, s); if (_bodies.Count > nb) _bodies[nb].Sym = el.Symbol; }   // every layer's atoms
                        }
            }
            // the atom under you, from inside: its electrons in their shells, moving round it, and its nucleus at the middle
            if (focus != null && s < Math.Max(AtomRadius(focus), AtomSpacing) * 8)
            {
                double ar = AtomRadius(focus), cy = fay, nr = NucleusRadius(focus), ax0 = fax, az0 = faz;
                float iw = (float)Fade(s, Math.Max(ar, AtomSpacing) * 8, Math.Max(ar, AtomSpacing) * 4);   // (coming up by degrees from eight widths to four, as its drawing among the others above fades out)
                // its electrons, the same as every atom's system: each a clear dot on its own path in its shells, the valence shell
                // in gold (the nucleus is drawn below, for real)
                float cloud = (float)Fade(s, Math.Max(ar, AtomSpacing) * 4, Math.Max(ar, AtomSpacing) * 2);
                Cloud(glow, focus, ax0, cy, az0, s, time, 777, Math.Max(0.6f, cloud) * iw);   // fully drawn from as far as every atom's
                if (s < ar * 0.5)
                {
                    // inside its electron cloud it is not empty: the innermost electrons (1s) are densest at the nucleus itself,
                    // |psi|^2 falling as exp(-2Zr/a0), so all the way down to the nucleus you are inside their cloud: a shimmer of
                    // where they may be found, as bright as that density, sampled afresh a few times a second
                    double a1s = 5.29e-14 / Math.Max(1, focus.Z); long tick = 0;                                         // (steady: never a flicker)
                    for (int k = 0; k < 400; k++)
                    {
                        double u = Hash(k, tick, 401, _seed), v = Hash(k, tick, 402, _seed), wq = Hash(k, tick, 403, _seed);
                        double rr = s * Math.Exp(Math.Log(0.15) + wq * Math.Log(4 / 0.15)), th = Math.Acos(1 - 2 * u), ph = v * 2 * Math.PI;
                        double dens = Math.Exp(-2 * rr / a1s); if (dens < 0.02) continue;
                        float b = (float)(0.2 * dens);
                        Add(glow, ax0 + rr * Math.Sin(th) * Math.Cos(ph), cy + rr * Math.Cos(th), az0 + rr * Math.Sin(th) * Math.Sin(ph), s * 0.004, b * 0.6f, b * 0.75f, b, s);
                    }
                }
                // the nucleus: always its real protons (red) and neutrons (grey) packed in a ball, growing as you come in; while
                // it is too small to see, a small spark marks where it is
                if (nr / s < 0.004) Add(glow, ax0, cy, az0, s * 0.0014, 1f, 0.95f, 0.9f, s);   // a sharp point of light until its protons and neutrons can be seen
                var nuc = NucleonsOf(focus); int A = nuc.Length;                                   // every one of its protons and neutrons, where the forces put them
                if (_focusSince >= 0) DecayFlash(glow, solid, _focusParent, ax0, cy, az0, _focusSince, s);   // (just decayed: its alpha, electron or other half still flying out)
                for (int k = 0; k < A; k++)
                {
                    bool proton = nuc[k][3] > 0;
                    double nx = ax0 + nuc[k][0] * 1e-18, ny = cy + nuc[k][1] * 1e-18, nz = az0 + nuc[k][2] * 1e-18;
                    // every proton and neutron, close in, shows its three quarks; from afar it is a ball (red a proton, grey a neutron)
                    double nball = 1 - Fade(s, Nucleon * 8, Nucleon * 3);
                    if (s < Nucleon * 8)
                    {
                        // the one at the middle, from inside: its three quarks (a proton two up and one down, a neutron one up
                        // and two down), points as far as measured, moving in about 0.84 fm, in the glow of the gluons that hold them
                        float gf = (float)Math.Max(0.15, Fade(s, Nucleon * 8, Nucleon * 3));
                        if (nball > 0.03) Add(solid, nx, ny, nz, Nucleon / 0.75 * nball, proton ? 0.9f : 0.6f, proton ? 0.22f : 0.65f, proton ? 0.2f : 0.75f, s);
                        Add(glow, nx, ny, nz, Nucleon, 0.1f * gf, 0.05f * gf, 0.02f * gf, s);   // the gluon field's faint glow (never an orb hiding its quarks)
                        string q = proton ? "uud" : "udd"; float[][] colour = { new[] { 1f, 0.2f, 0.2f }, new[] { 0.2f, 1f, 0.2f }, new[] { 0.3f, 0.4f, 1f } };   // their three colour charges
                        // the strong field between them: flux tubes of gluon field joining the three quarks (it does not spread out
                        // like the electric field, it pulls into strings), and quark-antiquark pairs of the sea popping in and out
                        double[][] qp = new double[3][];
                        for (int qi = 0; qi < 3; qi++)
                        {
                            double a1 = time * (0.07 + qi * 0.02) + qi * 2.1 + k * 0.77, a2 = time * (0.05 + qi * 0.016) + qi + k * 1.3, qr = Nucleon * (0.35 + 0.2 * Math.Sin(time * 0.05 + qi + k));   // (slowed to be watched: a quark crosses its nucleon in about half a minute)
                            qp[qi] = new[] { nx + qr * Math.Sin(a2) * Math.Cos(a1), ny + qr * Math.Cos(a2), nz + qr * Math.Sin(a2) * Math.Sin(a1) };
                        }
                        double ycx = (qp[0][0] + qp[1][0] + qp[2][0]) / 3, ycy = (qp[0][1] + qp[1][1] + qp[2][1]) / 3, ycz = (qp[0][2] + qp[1][2] + qp[2][2]) / 3;
                        for (int qi = 0; qi < 3; qi++)
                            for (int t2 = 1; t2 < 10; t2++)
                            {
                                double tt = t2 / 10.0; float fl = gf * (float)(0.35 + 0.25 * Math.Sin(time * 1.5 + t2 + qi * 2)) * 0.5f;   // (the tubes breathe slowly: never a flicker)
                                Add(glow, qp[qi][0] + (ycx - qp[qi][0]) * tt, qp[qi][1] + (ycy - qp[qi][1]) * tt, qp[qi][2] + (ycz - qp[qi][2]) * tt, Nucleon * 0.12, colour[qi][0] * fl, colour[qi][1] * fl, colour[qi][2] * fl, s);
                            }
                        long qt = (long)Math.Floor(time * 0.7);                                       // (a pair lasts over a second: watchable, never a strobe)
                        for (int sp = 0; sp < 6; sp++)
                        {
                            if (Hash(k, sp, qt, _seed) > 0.5) continue;                       // a virtual pair, there for a moment
                            double ox = (Hash(k, sp, qt + 1, _seed) - 0.5) * Nucleon * 1.4, oy = (Hash(k, sp, qt + 2, _seed) - 0.5) * Nucleon * 1.4, oz = (Hash(k, sp, qt + 3, _seed) - 0.5) * Nucleon * 1.4, sep = Nucleon * 0.12;
                            Add(glow, nx + ox + sep, ny + oy, nz + oz, s * 0.002, 0.7f * gf, 0.7f * gf, 0.9f * gf, s); Add(glow, nx + ox - sep, ny + oy, nz + oz, s * 0.002, 0.9f * gf, 0.6f * gf, 0.6f * gf, s);
                        }
                        for (int qi = 0; qi < 3; qi++)
                        {
                            double a1 = time * (0.07 + qi * 0.02) + qi * 2.1 + k * 0.77, a2 = time * (0.05 + qi * 0.016) + qi + k * 1.3, qr = Nucleon * (0.35 + 0.2 * Math.Sin(time * 0.05 + qi + k));   // (slowed to be watched: a quark crosses its nucleon in about half a minute)
                            double qx = nx + qr * Math.Sin(a2) * Math.Cos(a1), qy = ny + qr * Math.Cos(a2), qz = nz + qr * Math.Sin(a2) * Math.Sin(a1);
                            // the quark you picked moves on: the view keeps going to where it is now (as with an electron), and
                            // once it is all but there it rides with it exactly, so the zoom in goes on into it without end
                            if (_picked != null && _picked.Kind == 4 && _picked.Key == k + 1 && _picked.I == qi)
                            {
                                _tfx = qx; _tfy = qy + _yOff; _tfz = qz; _picked.X = qx; _picked.Y = qy + _yOff; _picked.Z = qz;
                                double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < Math.Pow(s * 3, 2)) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                            }
                            // a point as far as anyone has measured: its glow fills the view and stays, however far in you go
                            Add(glow, qx, qy, qz, Math.Min(s * 4, Math.Max(s * 0.003, Nucleon * 0.05)), colour[qi][0] * gf, colour[qi][1] * gf, colour[qi][2] * gf, s);   // (never wider than a few screens: a glow hundreds of screens wide was dropped on some frames and kept on others, a strobe)
                            int nb = _bodies.Count; AddBody(4, qi, q[qi] == 'u' ? 1 : 0, qx, qy, qz, Nucleon * 0.3, s); if (_bodies.Count > nb) { _bodies[nb].Sym = q[qi] == 'u' ? "up" : "down"; _bodies[nb].Key = k + 1; }
                        }
                        continue;
                    }
                    Add(solid, nx, ny, nz, Nucleon / 0.75, proton ? 0.9f : 0.6f, proton ? 0.22f : 0.65f, proton ? 0.2f : 0.75f, s);
                    { int nb = _bodies.Count; AddBody(5, k, proton ? 1 : 0, nx, ny, nz, Nucleon, s); if (_bodies.Count > nb) _bodies[nb].Sym = proton ? "proton" : "neutron"; }
                }
            }
        }

        // an atom's electrons, for the atoms around the one you are in: a faint glow for each shell and its electrons moving
        private void Cloud(List<StarVertex> glow, Element e, double x, double y, double z, double s, double time, long key, float f)
        {
            if (f <= 0.01) return;
            if (_plasma)
            {
                // too hot to keep its electrons: the bare nucleus, and free electrons racing past on their own
                double ar0 = AtomRadius(e), dt0 = s * 0.0035;
                for (int k = 0; k < 4; k++)
                {
                    double ph = _clock * (3 + k) + k * 1.7, rr0 = ar0 * (1.5 + 3 * Hash(k, 1, 7, _seed));
                    Add(glow, x + Math.Cos(ph) * rr0, y + Math.Sin(ph * 0.7) * rr0 * 0.6, z + Math.Sin(ph) * rr0, dt0, 1f * f, 0.8f * f, 0.25f * f, s);
                }
                Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.0014), 1f * f, 0.95f * f, 0.9f * f, s);
                { int nb = _bodies.Count; AddBody(7, 0, 0, x, y, z, ar0 * 0.08, s); if (_bodies.Count > nb) _bodies[nb].Sym = e.Symbol; }
                return;
            }
            key = (long)Math.Round(x / 1e-16) * 73856093L ^ (long)Math.Round(y / 1e-16) * 19349663L ^ (long)Math.Round(z / 1e-16) * 83492791L;   // from where the atom is: the same atom, the same electrons, however it is drawn
            double ar = AtomRadius(e); int shells = e.Shells.Length;
            double dot = s * 0.006;                                                     // electrons: points, shown as a clear dot a few pixels wide
            {
                // every one of its electrons, in its own subshell and orbital (this element's own make-up), each on its path in its
                // orbital's shape, at its subshell's distance, at its own speed; the valence (outermost) shell in gold
                var els = ElectronsOf(e);
                for (int k = 0; k < els.Length; k++)
                {
                    var el = els[k]; bool valence = el.N == shells;
                    // its path, seen (only one of each orbital's pair: they share it), turning on slowly with the electron, so the
                    // orbital's shape is read and is never the same twice
                    if (el.Spin == 0)
                        for (int q = 0; q < 48; q++)
                        {
                            double px, py, pz; ElectronAt(el, key, Math.Floor(ElectronPhase(el, k, time) / (2 * Math.PI)) * 2 * Math.PI + q * Math.PI * 2 / 48, out px, out py, out pz, ElectronDrift(time));   // (the loop of this time round)
                            Add(glow, x + px * ar, y + py * ar, z + pz * ar, dot * 0.4, (valence ? 0.4f : 0.2f) * f, (valence ? 0.3f : 0.3f) * f, (valence ? 0.1f : 0.5f) * f, s);
                        }
                    double ox, oy, oz; ElectronAt(el, key, ElectronPhase(el, k, time), out ox, out oy, out oz, ElectronDrift(time));
                    double exx = x + ox * ar, eyy = y + oy * ar, ezz = z + oz * ar;
                    // the electron you picked moves on its path: the camera keeps going to where it is now, and once all but
                    // there rides with it exactly (a point, however far in you go)
                    if (_picked != null && _picked.Kind == 3 && _picked.Key == key && _picked.J == k)
                    {
                        _tfx = exx; _tfy = eyy + _yOff; _tfz = ezz; _picked.X = exx; _picked.Y = eyy + _yOff; _picked.Z = ezz;
                        double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < Math.Pow(s * 3, 2)) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                    }
                    bool riding = _picked != null && _picked.Kind == 3 && _picked.Key == key && _picked.J == k;
                    Add(glow, exx, eyy, ezz, riding ? Math.Max(dot, s * 0.07) : dot, (valence ? 1f : 0.45f) * f, (valence ? 0.8f : 0.75f) * f, (valence ? 0.25f : 1f) * f, s);   // (the one you ride beside: a glowing body a share of the view, as a comet is when you fly with it; a point has no size, this is the size we give it)
                    { int nb = _bodies.Count; AddBody(3, el.N, k, exx, eyy, ezz, ar * 0.05, s); if (_bodies.Count > nb) { _bodies[nb].Sym = "electron:" + e.Symbol + ":" + shells + ":" + el.N + "spdf"[Math.Min(3, el.L)]; _bodies[nb].Key = key; _bodies[nb].AX = x; _bodies[nb].AY = y; _bodies[nb].AZ = z; } }   // every atom's electrons can be picked
                }
            }
            { double odx = x - _fx, ody = y + _yOff - _fy, odz = z - _fz; if (odx * odx + ody * ody + odz * odz < Math.Pow(AtomSpacing * 0.6, 2)) Orbitals(glow, e, x, y, z, s, time, key, f); }   // and where its electrons may be, as steady clouds: only for the atom you are at
            Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.0014), 1f * f, 0.95f * f, 0.9f * f, s);   // its nucleus: a sharp point of light at the middle
            { int nb = _bodies.Count; AddBody(7, 0, 0, x, y, z, ar * 0.08, s); if (_bodies.Count > nb) _bodies[nb].Sym = e.Symbol; }   // and its nucleus can be picked
        }

        private void Put(List<StarVertex> l, ref StarVertex[] arr, ref UploadBuffer buf)
        {
            if (arr.Length < l.Count) arr = new StarVertex[l.Count + 2048];
            l.CopyTo(arr);
            long need = (long)Math.Max(1, l.Count) * Marshal.SizeOf(typeof(StarVertex));
            if (buf == null || buf.Bytes < need) { if (buf != null) buf.Dispose(); buf = new UploadBuffer(_gpu.Device, (long)(l.Count + 2048) * Marshal.SizeOf(typeof(StarVertex)) * 2); }
            if (l.Count > 0) buf.Write(arr, l.Count);
        }

        /// <summary>Draws the matter at <paramref name="scaleKm"/> (how far above the point you dive into), in view units.</summary>
        public void Draw(Mat4 view, double scaleKm, int w, int h, double time) { Draw(view, scaleKm, w, h, time, null, false); }

        /// <summary>Draws the matter; with <paramref name="overlay"/>, only its grains, lying over the ground still drawn below.</summary>
        public void Draw(Mat4 view, double scaleKm, int w, int h, double time, MeshPipes pipes, bool overlay)
        {
            _viewH = h;
            Universe.Render.GpuLog.Pass = overlay ? "the grains over the ground" : "the matter (a grain's face, crystals, atoms)";
            _lastH = h;
            // the point you dive into eases across to what you picked
            double dt = _lastTime < 0 ? 0 : Math.Max(0, Math.Min(0.1, time - _lastTime)); _lastTime = time;
            double k = 1 - Math.Exp(-dt * Glide);
            // an electron moves on its path faster than the view could glide after it: once you are almost at it the view rides with
            // it, so zooming in goes to it, never past
            {
                double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz, gd = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                if (_picked != null && (_picked.Kind == 3 || _picked.Kind == 4) && gd < scaleKm * 3) k = 1;
                if (gd < scaleKm * 0.01) k = 1;                                                 // a hundredth of the picture away: there, exactly
                if (k >= 1) { _fx = _tfx; _fz = _tfz; _fy = _tfy; }                             // (exactly: a rounding's width is a whole screen, far enough in)
                else
                {
                    _fx += gx * k; _fz += gz * k; _fy += gy * k;
                    // the zoom never outruns the glide: whatever you picked (an atom, its nucleus, a nucleon, a quark) stays
                    // within the picture however fast the wheel goes, so coming down always comes down onto it, never past
                    // it into black with its name on the card
                    double left = gd * (1 - k), keep = scaleKm * 0.3;
                    if (left > keep) { double pull = (left - keep) / gd; _fx += gx * pull; _fz += gz * pull; _fy += gy * pull; }
                }
            }
            var solid = new List<StarVertex>(20000); var glow = new List<StarVertex>(2000); var mesh = pipes != null ? new List<StarVertex>(60000) : null;
            _bodies.Clear();
            _clock = time; Build(scaleKm, time, solid, glow, mesh, overlay);
            if (_picked != null && _picked.Kind == 3 && !overlay)
            {
                // the electron you ride: where it is now, worked out from the pick itself (its atom, its shell and orbital, its phase), the
                // dive point taken to it, and it drawn there as a glowing body a share of the view, whatever its atom's own drawing did
                var ps = (_picked.Sym ?? "").Split(':'); var pe = ps.Length > 1 ? El(ps[1]) : null;
                if (pe != null)
                {
                    var els = ElectronsOf(pe); int kq = (int)_picked.J;
                    if (kq >= 0 && kq < els.Length)
                    {
                        double ox, oy, oz; ElectronAt(els[kq], _picked.Key, ElectronPhase(els[kq], kq, time), out ox, out oy, out oz, ElectronDrift(time)); double ar = AtomRadius(pe);
                        double exx = _picked.AX + ox * ar, eyy = _picked.AY + oy * ar, ezz = _picked.AZ + oz * ar;
                        _picked.X = exx; _picked.Y = eyy + _yOff; _picked.Z = ezz; _tfx = exx; _tfy = eyy + _yOff; _tfz = ezz;
                        double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < scaleKm * scaleKm * 9) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                        Add(glow, exx, eyy, ezz, scaleKm * 0.07, 1f, 0.8f, 0.25f, scaleKm);
                    }
                }
            }
            if (mesh != null) { Solidify(solid, mesh, h); solid.Clear(); }
            Put(solid, ref _sv, ref _solidBuf); Put(glow, ref _gv, ref _glowBuf);
            if (mesh != null) Put(mesh, ref _mv, ref _meshBuf);
            Mat4 vp = view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), 0.001f, 1e9f);   // out to the grains round you, however close in you are
            _lastVp = vp; _lastW = w; _lastH = h; _haveVp = true; _scale = scaleKm; _lastView = view;
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = 1; c[28] = w; c[29] = h; c[30] = 0.0012f;
            // (solid things are drawn as lit shapes below, never as glowing dots, which add up to white)
            c[26] = 0; if (glow.Count > 0) _dots.Draw(_glowBuf, glow.Count, c, 0, 0, w, h);      // electrons, their shells and the nucleus's spark: glows
            if (_orbRecs.Count > 0)
            {
                // the orbitals' clouds: each record drawn as its points, placed by the card
                Put(_orbRecs, ref _orbArr, ref _orbBuf);
                int per = Math.Max(6, Math.Min(OrbitalPoints, 4000000 / Math.Max(1, _orbRecs.Count)));   // with many atoms on screen, fewer points to each orbital: the shapes stay, the frame rate too
                var oc = (float[])c.Clone(); oc[41] = 1; oc[42] = per; oc[30] = 0.0028f;
                _dots.Draw(_orbBuf, _orbRecs.Count * per, oc, 0, 0, w, h);
            }
            if (mesh != null && mesh.Count > 0)
            {
                // the near grains as lit stones (flat facets), in the starlight of where you are
                var mc = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) mc[q] = c[q];
                mc[16] = Light[0]; mc[17] = Light[1]; mc[18] = Light[2]; mc[20] = 0; mc[21] = 0; mc[22] = 0; mc[25] = 0; mc[26] = 0;
                pipes.Solid.Draw(_meshBuf, mesh.Count, mc, 0, 0, w, h);
            }
            if (pipes != null && _stoneMesh.Count > 0)
            {
                // the near stones, each drawn with where each pixel is on it, so the card works out its detail and stains
                Put(_stoneMesh, ref _stoneArr, ref _stoneBuf);
                var sc0 = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) sc0[q] = c[q];
                sc0[16] = Light[0]; sc0[17] = Light[1]; sc0[18] = Light[2];
                foreach (var st in _stones)
                {
                    double rad = st[6], sr2 = _scale / Math.Max(1e-30, rad);
                    var k2 = (float[])sc0.Clone();
                    double sx0 = (_fx - st[3]) / rad - _ex * sr2, sy0 = (_fy - _yOff - st[4]) / rad - _ey * sr2, sz0 = (_fz - st[5]) / rad - _ez * sr2;
                    k2[40] = (float)sx0; k2[41] = (float)sy0; k2[42] = (float)sz0; k2[43] = (float)sr2; k2[19] = (float)(sx0 - k2[40]); k2[23] = (float)(sy0 - k2[41]); k2[31] = (float)(sz0 - k2[42]);
                    k2[44] = BitConverter.ToSingle(BitConverter.GetBytes((uint)((long)st[2] & 0xFFFFFFFF)), 0); k2[45] = (float)(rad / Math.Max(1e-30, _scale)); k2[46] = 0; k2[47] = 1; k2[36] = Greyness;   // (no atomic terraces or atoms on a stone: that slot is the layer's height, 0 none)
                    pipes.Solid.Draw(_stoneBuf, (int)st[0], (int)st[1], k2, 0, 0, w, h);
                }
            }
            if (pipes != null && _patchMesh.Count > 0)
            {
                // the grain's face: its shape from the processor's coarse points, its detail and stains worked out per pixel on the
                // card from where each pixel is on the grain (its own frame, radius 1)
                var pc = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) pc[q] = c[q];
                pc[16] = Light[0]; pc[17] = Light[1]; pc[18] = Light[2];
                double sr = _scale / Math.Max(1e-30, _patchRad);
                double ox0 = (_fx - _patchGX) / _patchRad - _ex * sr, oy0 = (_fy - _yOff - _patchGY) / _patchRad - _ey * sr, oz0 = (_fz - _patchGZ) / _patchRad - _ez * sr;
                pc[40] = (float)ox0; pc[41] = (float)oy0; pc[42] = (float)oz0; pc[43] = (float)sr;
                pc[19] = (float)(ox0 - pc[40]); pc[23] = (float)(oy0 - pc[41]); pc[31] = (float)(oz0 - pc[42]);   // the low halves
                pc[44] = BitConverter.ToSingle(BitConverter.GetBytes((uint)(_patchKey & 0xFFFFFFFF)), 0); pc[45] = (float)(_patchRad / Math.Max(1e-30, _scale)); pc[46] = (float)(3e-13 / Math.Max(1e-30, _patchRad)); pc[47] = 1; pc[36] = Greyness;   // (one atomic layer, 0.3 nm, in the grain's own units: its terraces)
                Put(_patchMesh, ref _patchArr, ref _patchBuf);
                Universe.Render.GpuLog.Pass = "a grain's face, its detail worked out per pixel";
                pipes.Solid.Draw(_patchBuf, _patchMesh.Count, pc, 0, 0, w, h);
            }
        }

        private double _scale = TopKm; private Mat4 _lastView;
        private StarVertex[] _patchArr = new StarVertex[0]; private UploadBuffer _patchBuf;

        /// <summary>Picks the grain or atom under the mouse (within about 24 pixels): the point you dive into eases to it.
        /// False if nothing is there.</summary>
        public bool Pick(int mx, int my)
        {
            if (!_haveVp) return false;
            // a click always picks something: the thing it is on, else the nearest thing within a good reach of it (a sixth of the
            // screen), else the spot on the surface under it, else the point one view ahead along it (looking out across a face, or
            // up, there was nothing to pick at all)
            double best = Math.Pow(Math.Max(160, _lastH * 0.15), 2); Body pick = null; bool pickInside = false; double pickDepth = 0; var m = _lastVp;
            foreach (var b in _bodies)
            {
                float x = (float)((b.X - _fx) / _scale + _ex), y = (float)((b.Y - _fy) / _scale + _ey), z = (float)((b.Z - _fz) / _scale + _ez);
                float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
                if (cw <= 0) continue;
                double sx = (cx / cw * 0.5 + 0.5) * _lastW, sy = (0.5 - cy / cw * 0.5) * _lastH, d2 = (sx - mx) * (sx - mx) + (sy - my) * (sy - my);
                double rpx = b.Size / _scale / cw / Math.Tan(0.45) * _lastH * 0.5;            // its own size on screen counts too
                // the smallest thing the click is on wins (so a water molecule on a clay plate is the molecule, not the plate);
                // else the nearest within reach
                // of the things the click is on: the front one wins (never one behind it), except a much smaller thing on or in
                // the one in front (an electron of that atom, a molecule on a plate); else the nearest within reach
                bool on = d2 < rpx * rpx, pickOn = pick != null && pickInside;
                double depth = cw * _scale;                                                  // how far in front of you (km)
                bool wins;
                if (!on) wins = !pickOn && d2 < best;
                else if (!pickOn) wins = true;
                else if (b.Size < pick.Size * 0.3) wins = depth <= pickDepth + pick.Size;    // smaller, and on or in front of the pick
                else if (pick.Size < b.Size * 0.3) wins = pickDepth > depth + b.Size;        // bigger: only if the pick is behind it
                else wins = depth < pickDepth;                                               // alike: the one in front
                if (wins) { best = d2; pick = b; pickInside = on; pickDepth = depth; }
            }
            if (pick == null)
            {
                // nothing right there: the spot you clicked on the surface (where the line under the mouse meets it)
                var v = _lastView; var right = new Vec3(v.M11, v.M21, v.M31); var up = new Vec3(v.M12, v.M22, v.M32); var fwd = Vec3.Cross(up, right).Normalized;
                double t = Math.Tan(0.45), nx = mx / (double)Math.Max(1, _lastW) * 2 - 1, ny = 1 - my / (double)Math.Max(1, _lastH) * 2, aspect = _lastW / (double)Math.Max(1, _lastH);
                for (int sign = 0; sign < 2; sign++)
                {
                    var f = sign == 0 ? fwd : new Vec3(-fwd.X, -fwd.Y, -fwd.Z);
                    var d = (f + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
                    float cw = d.X * m.M14 + d.Y * m.M24 + d.Z * m.M34 + m.M44;              // in front of you?
                    if (cw <= 0 || d.Y >= -1e-4f) continue;
                    double k = _ey / d.Y;                                                    // where the line meets the level of the point you dive into
                    _tfx = _fx + (d.X * k - _ex) * _scale; _tfz = _fz + (d.Z * k - _ez) * _scale;
                    _picked = new Body { Kind = 2, X = _tfx, Y = 0, Z = _tfz, Size = _scale * 0.02 }; return true;
                }
                {
                    // the point one view ahead along the line under the mouse
                    var d = (fwd + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
                    _tfx = _fx + (d.X - _ex) * _scale; _tfy = _fy + (d.Y - _ey) * _scale; _tfz = _fz + (d.Z - _ez) * _scale;
                    _picked = new Body { Kind = 2, X = _tfx, Y = _tfy, Z = _tfz, Size = _scale * 0.02 }; return true;
                }
            }
            _picked = pick; _tfx = pick.X; _tfz = pick.Z; _tfy = pick.Kind == 0 ? 0 : pick.Y;
            return true;   // whatever you pick is gone to, in any layer (a grain: to its top)   // grains and atoms are flown to; electrons, nucleons and quarks are only named
        }

        /// <summary>Puts the point you dive into right under you (km east and north of where this spot's matter began), so the
        /// grains pass by as you fly over them.</summary>
        public void Follow(double east, double north) { _fx = _tfx = east; _fz = _tfz = north; _fy = _tfy = 0; _picked = null; }

        /// <summary>Moves the point under you (km east and north), as W A S D fly you across.</summary>
        public void Move(double east, double north) { _fx += east; _fz += north; _tfx = _fx; _tfz = _fz; }

        /// <summary>Where the picked grain or atom is on screen (pixels) and how big, for its ring; false if nothing is picked.</summary>
        /// <summary>Picks the nearest of a kind of thing to the point you dive into, as a click on it would (atom, electron,
        /// nucleus, proton, quark, a grain, a spot on a grain's face): for the tour.</summary>
        /// <summary>How quickly the point you dive into glides to what is picked (a share a second): slow on the tour, a film's glide.</summary>
        public double Glide = 4; private double _lastLeftX = double.NaN, _lastLeftZ = double.NaN;
        /// <summary>How far the point you dive into still is from what is picked, in shares of the view's size (0 with nothing
        /// picked): the tour flies in only once it is there, so it never flies past what it shows.</summary>
        public double PickOff { get { if (_picked == null) return 0; double dx = _tfx - _fx, dz = _tfz - _fz; return Math.Sqrt(dx * dx + dz * dz) / Math.Max(1e-40, _scale); } }   // (across: how high it sits is the zoom's own)
        /// <summary>Zooming in by hand with nothing finer picked: the view locks onto the landmark at each size (the nearest atom,
        /// then its nucleus, then a proton or neutron, then a quark), so the wheel goes down into matter, never between it into
        /// nothing. Called on each wheel notch in, with the scale.</summary>
        public void Landmarks(double s)
        {
            if (_space) return;
            int k = _picked != null ? _picked.Kind : -1;
            double fx, fy, fz; var fe = Focus(out fx, out fy, out fz); double ar = fe != null ? AtomRadius(fe) : AtomSpacing * 0.5;
            if (s < Nucleon * 0.6) { if (k != 4) PickNearest("quark"); }
            else if (s < Nucleon * 8) { if (k != 5 && k != 4) PickNearest("proton"); }
            else if (s < ar * 0.5) { if (k != 7 && k != 5 && k != 4) PickNearest("nucleus"); }
            else if (s < AtomSpacing * 6) { if (k != 1 && k != 3 && k != 6 && k != 7 && k != 5 && k != 4) PickNearest("atom"); }
        }
        public void PickNearest(string what)
        {
            if (what == "bonded-atom")
            {
                // the atom next to this one: the nearest other atom (its neighbour in the crystal, sharing its outer electrons),
                // never the one just left
                Body nb = null; double nd = double.MaxValue;
                foreach (var b in _bodies)
                {
                    if (b.Kind != 1 || b == _picked) continue;
                    double dx = b.X - _fx, dy = b.Y - _yOff - _fy, dz = b.Z - _fz, d = dx * dx + dy * dy + dz * dz;
                    if (d < AtomSpacing * AtomSpacing * 0.25 || (_lastLeftX == b.X && _lastLeftZ == b.Z)) continue;   // not the one you are on, nor the one you came from
                    if (d < nd) { nd = d; nb = b; }
                }
                if (nb == null) return;
                if (_picked != null) { _lastLeftX = _picked.X; _lastLeftZ = _picked.Z; }
                _picked = nb; _tfx = nb.X; _tfz = nb.Z; _tfy = nb.Y; PickCount++; return;
            }
            int kind = what == "grain" ? 0 : what == "spot" ? 2 : what == "atom" ? 1 : what == "electron" ? 3 : what == "nucleus" ? 7 : what == "proton" ? 5 : what == "quark" ? 4 : -1; if (kind < 0) return;
            // an atom: the mineral's elements in turn (its oxygen is three atoms in five of every silicate and two in three of ice,
            // and the face's top layer is mostly its oxygen, so the nearest atom was oxygen every time); each pick takes the next
            // element the face has, and the nearest atom of that
            string wantSym = null;
            if (kind == 1)
            {
                var syms = new List<string>();
                foreach (var b in _bodies) if (b.Kind == 1 && b.Sym != null && !syms.Contains(b.Sym)) syms.Add(b.Sym);
                if (syms.Count > 1) { syms.Sort(string.CompareOrdinal); wantSym = syms[(int)(((long)PickCount + (long)(_seed & 0xFFFF)) % syms.Count)]; }
            }
            Body best = null; double bd = double.MaxValue;
            foreach (var b in _bodies)
            {
                if (b.Kind != kind || (kind == 5 && b.J != 1) || b == _picked) continue;
                if (wantSym != null && b.Sym != wantSym) continue;
                double dx = b.X - _fx, dy = b.Y - _yOff - _fy, dz = b.Z - _fz, d = dx * dx + dy * dy + dz * dz;
                if (kind == 3) d -= b.I * 1e-40;                                             // (the outer, valence, shell first, among equals)
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null)
            {
                // a spot or a grain where none is laid out to pick (an ice sheet or the sea, which have no grains: a level face), the
                // spot right under the point you dive into is the pick, as a click there would make it; never nothing, since every
                // stop below (the crystal, the atom, the nucleus) waits on it
                if (what == "spot" || what == "grain") { _picked = new Body { Kind = 2, X = _fx, Y = _fy, Z = _fz, Size = _scale * 0.02 }; _tfx = _fx; _tfz = _fz; _tfy = _fy; PickCount++; }
                return;
            }
            _picked = best; _tfx = best.X; _tfz = best.Z; _tfy = best.Y; PickCount++;
        }

        public bool PickedOnScreen(out double sx, out double sy, out double rpx)
        {
            sx = sy = rpx = 0; if (_picked == null || !_haveVp) return false; var m = _lastVp;
            if (_picked.Kind == 0 && _scale < Grain * 0.35) return false;               // you are at its face: no ring round it
            float x = (float)((_picked.X - _fx) / _scale + _ex), y = (float)((_picked.Y - _fy) / _scale + _ey), z = (float)((_picked.Z - _fz) / _scale + _ez);
            float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw <= 0) return false;
            sx = (cx / cw * 0.5 + 0.5) * _lastW; sy = (0.5 - cy / cw * 0.5) * _lastH;
            rpx = Math.Max(10, _picked.Size / _scale / cw / Math.Tan(0.45) * _lastH * 0.5 * 1.3); return true;
        }

        /// <summary>Lets go of the pick (the point you dive into stays where it is).</summary>
        public void Unpick() { _picked = null; _tfx = _fx; _tfz = _fz; _tfy = _fy; }

        /// <summary>The information card: what you are in, and what you picked.</summary>
        /// <summary>The card of what you are at or have picked; below light's wavelength it says there is no colour this small.</summary>
        public List<string> Card()
        {
            var L = CardLines();
            // colour is light of a wavelength (about 400 to 700 nanometres): nothing smaller than that can be seen in colour, so
            // from there down what is drawn is in greys, as an electron microscope shows it (a true rendering of light this small
            // is for later)
            { var fmL = FocusMineral; if (!_space && fmL != null && Structured(fmL) && L.Count > 0 && _scale < AtomSpacing * 40 && _scale > AtomSpacing * 0.05) L.Insert(1, _scale < AtomSpacing * 4 ? "level: the atom (its nucleus, its electrons on their paths; no ball)" : "level: the lattice (each atom its electron cloud, the bonds their shared electrons)"); }   // (one picture at a level, named)
            if (!_space && Greyness > 0 && L.Count > 0) L.Add(Greyness >= 1 ? "no colour this small: below light's wavelength, in greys as an electron microscope sees; the atoms' and electrons' colours are marks, not light" : "colour is going: the view is nearing light's wavelength");
            return L;
        }
        private List<string> CardLines()
        {
            var L = new List<string>();
            if (_space && _spaceKind == 12)
            {
                // the fall into a black hole: where you are, by the laws of gravity (the event horizon is where the speed to escape is
                // exactly light's; inside it is more, and outside it is less: content/physics.unt); the time left to the middle on
                // your own clock (a radial fall from rest far away, the same in Newton's and Einstein's gravity: two thirds of
                // r^1.5 over the root of 2GM); the stretch head to foot (the tide: 2GM over r cubed, across two metres)
                double r = _fallShare < 1 ? _holeRs * Math.Pow(10, -_fallShare * FallTenfolds) : Math.Min(_scale * 0.6, _holeRs * Math.Pow(10, -FallTenfolds)), rM = r * 1000, gm = Universe.Core.Physics.GmSol * _holeMass; bool at = _fallShare >= 1 && _scale <= Nucleon * 8;
                if (!at)
                {
                    L.Add("inside the event horizon: no light out");
                    L.Add(ScaleHud.Words(r) + " from the middle (the horizon " + ScaleHud.Words(_holeRs * 2) + " across); escape speed " + Math.Sqrt(_holeRs / Math.Max(1e-300, r)).ToString(_holeRs / r < 100 ? "0.#" : "0.#E+0") + " times light's");
                    L.Add("your own time: " + Secs(_fallTau) + " since the horizon, " + Secs(2.0 / 3 * Math.Pow(rM, 1.5) / Math.Sqrt(2 * gm)) + " left" + (Math.Abs(_fallRate - 1) > 0.05 ? " (shown " + (_fallRate < 1 ? (1 / _fallRate).ToString("N0") + " times slower" : _fallRate.ToString("N0") + " times faster") + ")" : ""));
                    L.Add("a far watcher never sees you cross the horizon; your own clock runs on to the middle");
                    { double share = Math.Max(0, Math.Min(1, _fallShare)); L.Add("the way in: " + WayWords(_wayInKm * share) + " of " + WayWords(_wayInKm) + " from the horizon to the singularity, by its mass (space inside stretches without end, and closes to nothing)"); }
                    L.Add("falling inward at " + (_fallSpeed < 10 ? _fallSpeed.ToString("0.##") : _fallSpeed.ToString("0.#E+0")) + " times light (root(rs over r): light's own at the horizon)");
                    if (_holeSpin > 0) L.Add("spin " + _holeSpin.ToString("0.00") + " of the most: frame dragging sweeps you round");
                    { double tide = 2 * gm / Math.Pow(Math.Max(1e-300, rM), 3) * 2; L.Add("the tide head to foot: " + (tide < 1e4 ? tide.ToString("0.###") : tide.ToString("0.#E+0")) + " m/s2" + (tide > 100 ? ", torn apart" : "")); }
                }
                else
                {
                    L.Add("the singularity");
                    L.Add("its whole mass, " + (_holeMass >= 1e4 ? _holeMass.ToString("0.##E+0") : _holeMass.ToString("0.#")) + " solar masses, in a point with no size at all, as far as the laws we have can say");
                    L.Add("quarks pressed closer than anything, until those laws give out: what is really here is not known");
                    L.Add("the view: " + ScaleHud.Words(_scale) + "; the ball stays the same however far in you go: a point");
                }
                return L;
            }
            // a grain you are now at the face of: it is where you are, not a pick any more
            if (_picked != null && _picked.Kind == 0 && _scale < Grain * 0.35) _picked = null;
            if (_picked != null && _picked.Kind == 2)
            {
                var fm2 = FocusMineral; if (fm2 == null) return L;
                L.Add("a spot on " + (_noGrains ? "the sea" : "a grain of " + fm2.Name.ToLowerInvariant()));
                L.Add("made of: " + fm2.Formula + StructureName(fm2));
                { var tw = TraceWords(fm2); if (tw != null) L.Add("traces: " + tw); }
                if (_scale < 3e-10 / 0.6 * 30) L.Add("its face: atomic terraces, each step 0.3 nm");
                L.Add("you are " + ScaleHud.Words(_scale * 0.6) + " above it");
                return L;
            }
            if (_picked != null && _picked.Kind == 3)
            {
                var ps = (_picked.Sym ?? "electron").Split(':'); var pe = ps.Length > 1 ? El(ps[1]) : null; int pshells = ps.Length > 2 ? int.Parse(ps[2]) : 0;
                L.Add("an electron of " + (pe != null ? "an atom of " + pe.Name.ToLowerInvariant() + " (" + pe.Symbol + ")" : "an atom"));
                L.Add("shell " + _picked.I + (pshells > 0 && _picked.I == pshells ? ", its outermost: a valence electron, the kind that bonds" : " of " + Math.Max(pshells, (int)_picked.I)));
                if (ps.Length > 3) L.Add("in its " + ps[3] + " subshell (" + (ps[3].EndsWith("s") ? "round" : ps[3].EndsWith("p") ? "two lobes" : ps[3].EndsWith("d") ? "four leaves" : "six leaves") + ")");
                L.Add("charge -1; mass 1/1836 of a proton");
                L.Add("a point as far as measured: smaller than 10^-22 m");
                L.Add("where it is: a cloud of chance round the nucleus");
                return L;
            }
            if (_picked != null && _picked.Kind == 8)
            {
                var st = _surf.Find(q => q.Id == _picked.Sym); if (st == null) return L;
                L.Add(st.Name);
                if (st.FromGround) { var gm = ThingMat(st, _picked.I, _picked.J); if (gm != null) L.Add("made of: " + gm.Name.ToLowerInvariant() + " (" + gm.Formula + ")"); }
                L.Add("width: about " + ScaleHud.Words(_picked.Size * 2));
                L.AddRange(st.About);
                return L;
            }
            if (_picked != null && _picked.Kind == 6)
            {
                var mm = _minerals.ContainsKey(_picked.Sym) ? _minerals[_picked.Sym] : null; if (mm == null) return L;
                string f0 = mm.Formula.Split(' ')[0];
                L.Add("a molecule of " + f0 + " (" + mm.Name.ToLowerInvariant() + ")");
                if (f0 == "H2O") { L.Add("bent at 104.5 degrees"); L.Add("O-H bonds 0.096 nm"); }
                else if (f0 == "CO2") { L.Add("straight; C=O bonds 0.116 nm"); }
                else if (f0 == "CH4") { L.Add("a tetrahedron; C-H bonds 0.109 nm"); }
                else if (f0 == "N2") { L.Add("a pair; N-N triple bond 0.110 nm"); }
                L.Add("atoms in it: " + (mm.Molecule != null ? mm.Molecule.Length : mm.Atoms.Length));
                return L;
            }
            if (_picked != null && _picked.Kind == 7)
            {
                var ne = El(_picked.Sym); if (ne == null) return L;
                L.Add("the nucleus of an atom of " + ne.Name.ToLowerInvariant());
                L.Add(ne.Z + " protons, " + (ne.A - ne.Z) + " neutrons (" + ne.Symbol + "-" + ne.A + ")");
                double bind = BindingMeV(ne.Z, ne.A);
                if (ne.A > 1) L.Add("held by the strong force: " + bind.ToString("0") + " MeV in all, " + (bind / ne.A).ToString("0.0") + " MeV each, against its protons' electric push (" + (_aC * ne.Z * (ne.Z - 1) / Math.Pow(ne.A, 1 / 3.0)).ToString("0") + " MeV)");
                L.Add(Decay(ne));
                if (ne.HalfLifeS > 0) L.Add("half-life " + Span(ne.HalfLifeS) + " (" + ne.Decays + "); here half go in " + Span(ne.HalfLifeS / _simPerReal));
                L.Add("width: about " + ScaleHud.Words(NucleusRadius(ne) * 2));
                L.Add("about 100,000 times smaller than its atom");
                return L;
            }
            if (_picked != null && _picked.Kind == 5)
            {
                bool pr = _picked.Sym == "proton";
                { double fx0, fy0, fz0; var fe = Focus(out fx0, out fy0, out fz0); L.Add("a " + _picked.Sym + " of " + (fe != null ? "a nucleus of " + fe.Name.ToLowerInvariant() + " (" + fe.Z + " proton" + (fe.Z == 1 ? "" : "s") + ", " + (fe.A - fe.Z) + " neutron" + (fe.A - fe.Z == 1 ? "" : "s") + ")" : "the nucleus")); }
                L.Add(pr ? "charge +1; quarks: up, up, down" : "charge 0; quarks: up, down, down");
                L.Add("width: about 1.7 femtometres");
                L.Add("held in the nucleus by the strong force");
                return L;
            }
            if (_picked != null && _picked.Kind == 4)
            {
                bool up = _picked.Sym == "up";
                { double fx0, fy0, fz0; var fe = Focus(out fx0, out fy0, out fz0); L.Add((_picked.Sym == "up" ? "an " : "a ") + _picked.Sym + " quark" + (fe != null ? ", in a nucleon of a nucleus of " + fe.Name.ToLowerInvariant() : "")); }
                L.Add(up ? "charge +2/3" : "charge -1/3");
                L.Add("a point as far as measured; held by gluons");
                L.Add("it has a colour charge (red, green or blue)");
                return L;
            }
            if (_picked != null && _picked.Kind == 0)
            {
                Mineral g; if (_picked.Sym == null || !_minerals.TryGetValue(_picked.Sym, out g)) g = GrainMineral(_picked.I, _picked.J); if (g == null) return L;
                L.Add("a grain of " + g.Name.ToLowerInvariant()); L.Add("formula: " + g.Formula);
                L.Add("width: " + ScaleHud.Words(_picked.Size * 2));
                var seen = new List<string>(); foreach (var a in g.Atoms) if (!seen.Contains(a)) seen.Add(a);
                L.Add("elements: " + string.Join(", ", seen.ToArray()));
                L.Add("ground: " + (_mixes.ContainsKey(_mixId) ? _mixId.Split('#')[0] : "rock"));   // (its kind's mix; a body's own is the kind's id and its seed)
                return L;
            }
            if (_picked == null && !_space && _scale > AtomSpacing * 8)
            {
                // above the atoms: the surface you are over, not an atom far too small to see
                var gm = FocusMineral; if (gm == null) return L;
                L.Add("the surface of a grain of " + gm.Name.ToLowerInvariant());
                L.Add("made of: " + gm.Formula + StructureName(gm));
                { var tw = TraceWords(gm); if (tw != null) L.Add("traces: " + tw); }
                L.Add("you are " + ScaleHud.Words(_scale * 0.6) + " above it");
                if (_scale < 6e-10) L.Add("its face steps down in atomic layers, 0.3 nm each");
                return L;
            }
            var e = _picked != null && _picked.Kind == 1 && _picked.Sym != null ? El(_picked.Sym) : FocusAtom();
            var fm = FocusMineral;
            if (e == null) return L;
            L.Add((_picked != null ? "an atom of " : "under you: ") + e.Name.ToLowerInvariant() + " (" + e.Symbol + ")");
            L.Add("atomic number " + e.Z + ", mass " + e.A);
            L.Add("protons: " + e.Z + ", neutrons: " + (e.A - e.Z) + ", electrons: " + e.Z);
            if (e.HalfLifeS > 0) L.Add("half-life " + Span(e.HalfLifeS) + " (" + e.Decays + "); here half go in " + Span(e.HalfLifeS / _simPerReal));
            if (_picked == null && _focusParent != null) L.Add("decayed " + Span(_focusSince * _simPerReal) + " ago (" + _focusParent.Decays + ") from " + _focusParent.Name.ToLowerInvariant() + "-" + _focusParent.A);
            L.Add("shells: " + string.Join(", ", Array.ConvertAll(e.Shells, x => x.ToString())));
            L.Add("width: about " + ScaleHud.Words(AtomRadius(e) * 2) + ", nucleus " + ScaleHud.Words(NucleusRadius(e) * 2));
            if (_space && _gasParts != null) { var top = (SpaceGen.Part[])_gasParts.Clone(); Array.Sort(top, (a, b) => b.Share.CompareTo(a.Share)); L.Add("made of: " + top[0].Name + ", " + top[1].Name + (top.Length > 2 ? ", " + top[2].Name : "")); }
            if (_space) { L.Add("in the gas of " + (_spaceKind == 2 ? "an emission nebula (most of its hydrogen ionized by hot stars)" : _spaceKind == 3 ? "a reflection nebula" : _spaceKind == 4 ? "a planetary nebula (its gas glowing from the white dwarf at its middle)" : _spaceKind == 5 ? "a supernova remnant (hot, racing outward)" : _spaceKind == 6 ? "a dark nebula (cold, mostly molecules of hydrogen)" : _spaceKind == 9 ? "the space between the stars" : _spaceKind == 13 ? "a neutron star's crust: iron nuclei packed in a lattice, electrons racing between them; deeper, below about a kilometre, the nuclei dissolve into neutrons packed shoulder to shoulder" : _spaceKind == 11 ? "a star's surface: plasma, bare nuclei and free electrons" : _spaceKind == 10 ? "the air: " + _airText : "space between a cluster's stars")); double inView = Math.Pow(_scale * 1.2 / _gasSpacing, 3), perCc = Math.Pow(1e-5 / _gasSpacing, 3);
                L.Add("about " + perCc.ToString(perCc < 10 ? "0.#" : "N0") + " atoms a cubic centimetre (air has 25 billion billion)");
                L.Add(inView >= 2 ? "in view: about " + Big(inView) + " atoms, far too thin to see" : "in view: the one atom ahead of you");
                L.Add("each about " + ScaleHud.Words(_gasSpacing) + " from the next"); }
            else if (fm != null && fm.Molecule != null && fm.Lat == null) L.Add("in a molecule of " + fm.Formula.Split(' ')[0] + ", in " + fm.Name.ToLowerInvariant());
            else if (fm != null) L.Add("in a grain of " + fm.Name.ToLowerInvariant() + " (" + fm.Formula + ")" + StructureName(fm));
            if (!_space && fm != null && IsTrace(fm, e)) { var host = El(TraceHost(fm, e)); double sh = TraceShare(fm, e); L.Add("a trace: in place of " + (host != null ? host.Name.ToLowerInvariant() : "another atom") + " on about one site in " + Big(1 / Math.Max(1e-12, sh))); }
            return L;
        }

        public void Dispose() { if (_solidBuf != null) _solidBuf.Dispose(); if (_glowBuf != null) _glowBuf.Dispose(); if (_meshBuf != null) _meshBuf.Dispose(); }
    }
}
