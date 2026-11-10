// Universe Â· shell Â· the matter view, its part: nucleus (one class in several files: src/shell/MatterView.cs is its start)
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
            if (l.Count >= 1500000) return;                                                  // (a ceiling: a frame of millions of soft points had the card stop answering, the driver reset by Windows, the program gone with it)
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

    }
}
