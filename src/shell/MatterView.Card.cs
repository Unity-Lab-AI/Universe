// Universe Â· shell Â· the matter view, its part: card (one class in several files: src/shell/MatterView.cs is its start)
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
        /// <summary>The information card: what you are in, and what you picked.</summary>
        /// <summary>The card of what you are at or have picked; below light's wavelength it says there is no colour this small.</summary>
        public List<string> Card()
        {
            var L = CardLines();
            if (L.Count > 0) L.Add(CensusWords());                                         // (what there is to pick at this size, or that there is nothing)
            // colour is light of a wavelength (about 400 to 700 nanometres): nothing smaller than that can be seen in colour, so
            // from there down what is drawn is in greys, as an electron microscope shows it (a true rendering of light this small
            // is for later)
            { var fmL = FocusMineral; if (!_space && fmL != null && Structured(fmL) && L.Count > 0 && _scale < AtomSpacing * 40 && _scale > AtomSpacing * 0.05) L.Insert(1, _scale < AtomSpacing * 4 ? "level: the atom: a nucleus a hundred thousand times smaller than the atom, and its electrons as fields of where they may be; its size is their reach, and nothing in it is solid" : "level: the lattice: each atom the fields of its electron shells, its size their reach, nothing solid in it; the bonds their shared electrons"); }   // (one picture at a level, named)
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
                    // the way is measured from the event horizon down the funnel: the singularity is millions of light-years down it, by
                    // the hole's mass (content/physics.unt @law hole-inside), not a horizon's width away (the card had given the
                    // distance to the middle in the outside's measure, eleven AU, against a way in of millions of light-years)
                    double share = Math.Max(0, Math.Min(1, _fallShare));
                    L.Add(WayWords(_wayInKm * share) + " down the funnel from the horizon, " + WayWords(_wayInKm * (1 - share)) + " to the singularity, by its mass (the horizon " + ScaleHud.Words(_holeRs * 2) + " across); escape speed " + Math.Sqrt(_holeRs / Math.Max(1e-300, r)).ToString(_holeRs / r < 100 ? "0.#" : "0.#E+0") + " times light's");
                    L.Add("your own time: " + Secs(_fallTau) + " since the horizon, " + Secs(2.0 / 3 * Math.Pow(rM, 1.5) / Math.Sqrt(2 * gm)) + " left" + (Math.Abs(_fallRate - 1) > 0.05 ? " (shown " + (_fallRate < 1 ? (1 / _fallRate).ToString("N0") + " times slower" : _fallRate.ToString("N0") + " times faster") + ")" : ""));
                    L.Add("a far watcher never sees you cross the horizon; your own clock runs on to the middle");
                    L.Add("the way in " + WayWords(_wayInKm) + " from the horizon to the singularity, by its mass: space inside stretches without end, and closes to nothing");
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

    }
}
