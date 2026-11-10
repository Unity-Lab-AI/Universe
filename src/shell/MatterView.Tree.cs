// Universe · shell · the matter view, its part: the tree (one class in several files: src/shell/MatterView.cs is its start)
using System;
using Universe.Core;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        // ── the one tree below what you are at (src/core/Thing.cs): the gas or the grain, its crystal, its molecule, the atom, and
        // inside the atom what is picked (an electron, the nucleus, a nucleon, a quark), each in the one above it ──
        /// <summary>The things you are inside at this scale, hung below <paramref name="top"/> (the body or the cloud they are in).</summary>
        public Thing Below(Thing top)
        {
            var t = top; double s = _scale; var e = FocusAtom();
            if (_space)
            {
                if (_spaceKind == 12) return t;                                              // (inside a black hole: no matter, only the fall)
                t = t.In(Rung.Gas, _spaceKind == 11 ? "its plasma" : _spaceKind == 13 ? "its crust" : _spaceKind == 10 ? "the air" : "its gas", _airText);
                if (e != null && s < Math.Max(AtomRadius(e) * 3, _gasSpacing * 0.5)) t = Inside(t.In(Rung.Atom, "an atom of " + e.Name.ToLowerInvariant()), e, s);
                return t;
            }
            var m = FocusMineral;
            t = t.In(Rung.Grain, m != null ? "a grain of " + m.Name.ToLowerInvariant() : "a grain", m != null ? m.Formula : null);
            if (s > AtomSpacing * 12 || m == null) return t;
            t = t.In(Rung.Crystal, Structured(m) ? "its crystal (" + m.Formula + ")" : "its " + m.Formula);
            if (_picked != null && _picked.Kind == 6) t = t.In(Rung.Molecule, "a molecule of " + (_picked.Sym ?? m.Formula));
            if (e == null || s > AtomRadius(e) * 3) return t;
            return Inside(t.In(Rung.Atom, "an atom of " + e.Name.ToLowerInvariant()), e, s);
        }
        // inside the atom: the picked electron, else the nucleus once you are near it, a nucleon of it, a quark of that
        private Thing Inside(Thing atom, Element e, double s)
        {
            int k = _picked != null ? _picked.Kind : -1;
            if (k == 3) return atom.In(Rung.Electron, "an electron");
            if (k != 7 && k != 5 && k != 4 && s > NucleusRadius(e) * 40) return atom;
            var n = atom.In(Rung.Nucleus, "its nucleus (" + e.Z + " protons, " + (e.A - e.Z) + " neutrons)");
            if (k == 5 || k == 4 || s < Nucleon * 8) n = n.In(Rung.Nucleon, k == 5 && _picked.Sym != null ? "a " + _picked.Sym : "a nucleon");
            if (k == 4 || s < Nucleon * 0.6) n = n.In(Rung.Quark, k == 4 && _picked.Sym == "up" ? "an up quark" : k == 4 && _picked.Sym == "down" ? "a down quark" : "a quark");
            return n;
        }
    }
}
