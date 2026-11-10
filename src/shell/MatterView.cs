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
    internal sealed partial class MatterView : IDisposable
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
        private sealed class Element { public double IonEv; public double Ionic; public int Charge; public string Symbol, Name; public int Z, A; public float R, G, B; public double Radius, Cov, Chi; public int[] Shells; public double HalfLifeS; public string Decays = "", MadeIn = ""; }   // HalfLifeS: 0 stable; MadeIn: where its atoms were forged (content/elements.unt made-in) (content/elements.unt half-life, decays)
        private readonly Dictionary<string, Mineral> _minerals = new Dictionary<string, Mineral>();
        private readonly Dictionary<string, List<KeyValuePair<Mineral, double>>> _mixes = new Dictionary<string, List<KeyValuePair<Mineral, double>>>();
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>(); private readonly Dictionary<int, Element> _byZ = new Dictionary<int, Element>();
        private List<KeyValuePair<Mineral, double>> _mix; private string _mixId = "";
        private ulong _seed; private bool _noGrains;
        // in space (a nebula's or cluster's gas) instead of a ground: its kind (0 globular cluster, 1 open cluster, 2 emission
        // nebula, 3 reflection nebula) and how far apart its atoms are (km)
        /// <summary>Every call that sets the matter's scene, as it is made (the outermost only: a call made inside another is rebuilt
        /// by replaying that one), with its exact arguments: the clip records them (src/render/ClipWriter.cs).</summary>
        public static Action<string, object[]> Recorded; private static int _recDepth;
        /// <summary>The calls that built the scene now shown, in order (a new ground, gas, plasma, crust or fall starts it afresh): a
        /// clip begun mid-dive starts with these.</summary>
        public static readonly List<KeyValuePair<string, object[]>> Scene = new List<KeyValuePair<string, object[]>>();
        private static void Announce(string name, bool root, object[] args)
        {
            if (_recDepth > 0) return;
            if (root) Scene.Clear();
            Scene.Add(new KeyValuePair<string, object[]>(name, args));
            if (Recorded != null) Recorded(name, args);
        }
        /// <summary>The matter view's own camera: where it looks (its focus) and where that focus is going.</summary>
        public void WriteFocus(System.IO.BinaryWriter o) { o.Write(_fx); o.Write(_fy); o.Write(_fz); o.Write(_tfx); o.Write(_tfy); o.Write(_tfz); }
        public void ReadFocus(System.IO.BinaryReader r) { _fx = r.ReadDouble(); _fy = r.ReadDouble(); _fz = r.ReadDouble(); _tfx = r.ReadDouble(); _tfy = r.ReadDouble(); _tfz = r.ReadDouble(); }
        private bool _space; private int _spaceKind; private double _gasSpacing = 1e-6;
        /// <summary>Into the gas of a nebula or a cluster: about 1,000 atoms a cubic centimetre in an emission nebula (one about every
        /// millimetre), fewer in a reflection nebula and between a cluster's stars; mostly hydrogen, about 9 in 100 helium, a trace of
        /// oxygen and nitrogen.</summary>
        public void SetSpace(int kind, ulong spot, double fromCentre) { SetSpace(kind, spot, fromCentre, null); }
        private SpaceGen.Part[] _gasParts;
        public void SetSpace(int kind, ulong spot, double fromCentre, SpaceGen.Part[] parts)
        { Announce("SetSpace", true, new object[] { kind, spot, fromCentre, parts }); _recDepth++; try { Generation++;
            _gasParts = parts; _bornSec = double.NaN; TempK = 0;   // (a cloud's temperature is not known here yet: no light drawn rather than another place's)
            _space = true; _spaceKind = kind; _seed = spot; _mix = null; _mixId = "space"; _noGrains = true; _plasma = false;
            // denser at the middle: an emission nebula about 9,000 atoms a cubic centimetre at its centre down to about 100 at its edge,
            // a reflection nebula about 3,000 to 30; between a cluster's stars about 1 (fromCentre: 0 the middle, 1 the edge)
            double core = 0.33 + 30 * Math.Exp(-Math.Pow(fromCentre / 0.25, 2));
            double perCc = kind == 2 ? 300 * core : kind == 3 ? 100 * core : kind == 4 ? 1000 * core : kind == 5 ? 3 * core : kind == 6 ? 300 * core : 1;   // planetary nebulae up to tens of thousands, remnants thin and hot, dark clouds dense
            _gasSpacing = 1e-5 / Math.Pow(perCc, 1 / 3.0);                                   // km between atoms (a centimetre is 1e-5 km)
            _fx = _fz = _tfx = _tfz = _fy = _tfy = 0; _picked = null;
        } finally { _recDepth--; } }
        public void Dispose() { if (_solidBuf != null) _solidBuf.Dispose(); if (_glowBuf != null) _glowBuf.Dispose(); if (_meshBuf != null) _meshBuf.Dispose(); }
    }
}
