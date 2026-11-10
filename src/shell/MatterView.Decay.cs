// Universe Â· shell Â· the matter view, its part: decay (one class in several files: src/shell/MatterView.cs is its start)
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
        private Element _lockE; private double _lockX, _lockY, _lockZ;   // the atom the pick belongs to, held while the pick is it or part of it
        private Element Focus(out double x, out double y, out double z)
        {
            var m = FocusMineral; x = FocusAI * AtomSpacing; z = FocusAJ * AtomSpacing; y = 0;
            // the atom you picked is the atom in focus: picked itself (or its nucleus), or by one of its electrons (the atom it belongs
            // to is on the pick), and held through its proton and its quark; the atom nearest the dive point otherwise (the tour had
            // picked one atom's nucleus and drawn another's, the one under the dive point two nanometres off, so at thirty femtometres
            // there was no proton in view to pick and the quark stops were skipped)
            if (_picked != null && !_space)
            {
                if ((_picked.Kind == 1 || _picked.Kind == 7) && _picked.Sym != null) { var pe = El(_picked.Sym); if (pe != null) { _lockE = pe; _lockX = _picked.X; _lockY = _picked.Y; _lockZ = _picked.Z; } }
                else if (_picked.Kind == 3 && _picked.Sym != null) { var bits = _picked.Sym.Split(':'); var pe = bits.Length > 1 ? El(bits[1]) : null; if (pe != null) { _lockE = pe; _lockX = _picked.AX; _lockY = _picked.AY + _yOff; _lockZ = _picked.AZ; } }
                else if (_picked.Kind != 5 && _picked.Kind != 4) _lockE = null;
                if (_lockE != null && (_picked.Kind == 1 || _picked.Kind == 7 || _picked.Kind == 3 || _picked.Kind == 5 || _picked.Kind == 4)) { x = _lockX; y = _lockY - _yOff; z = _lockZ; return _lockE; }   // (its height as the crystal's atoms give theirs, the face's height taken off: held as it is, every nucleus, proton and quark picked in it had the face's height added again, 218 nm a pick, so the dive point ended 655 nm over the crystal and the zoom back out ran up through empty air)
            }
            else _lockE = null;
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

    }
}
