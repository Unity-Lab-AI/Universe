// Universe · shell · the universe view, its part: the lives (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── stars living and dying, worked out only for the stars round you (render by scale): each its own mass about its kind's and
        // its own age from its place in the list; on the main sequence for about ten billion years times its mass to the minus 2.5, then
        // a tenth of that again as a giant (a red supergiant from eight solar masses up), then what its mass leaves: a white dwarf under
        // eight, a neutron star to about twenty-five, a black hole past it, drawn in that kind's colour, size and light ──
        private static readonly string[] MainSequence = { "o-type", "b-type", "a-type", "f-type", "g-type", "k-type", "m-type" };
        private readonly Dictionary<string, int> _kindIx = new Dictionary<string, int>();
        private int KindIx(string id)
        {
            int k; if (_kindIx.TryGetValue(id, out k)) return k; k = -1;
            for (int i = 0; i < _kinds.Count; i++) if (_kinds[i].Id == id) { k = i; break; }
            _kindIx[id] = k; return k;
        }
        private static double Hash01(int k, int salt) { uint h = (uint)k * 2654435761u ^ (uint)salt * 0x9E3779B9u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; return (h & 0xFFFFFF) / 16777216.0; }
        // star k's own mass, main-sequence life and age at the start, by its own lasting id (never its place in the list, which is remade as you move)
        private int StarKey(int k) { long id = k >= 0 && k < _localId.Count ? _localId[k] : k; return (int)(id ^ (id >> 31)); }
        private void LocalLife(int k, int bornKind, out double m, out double life, out double age0)
        {
            int key = StarKey(k); m = _kinds[bornKind].MassSol * Math.Exp(0.25 * (Hash01(key, 1) * 2 - 1)); life = 1e10 * Math.Pow(m, -2.5); age0 = Hash01(key, 2) * Math.Min(life, 1.2e10);
        }
        /// <summary>The game's year star k round you dies in a supernova (NaN: it never does: under eight solar masses, or not on the main sequence).</summary>
        private double LocalDeathYear(int k)
        {
            int born = k < _localKind.Count ? _localKind[k] : -1; if (born < 0 || born >= _kinds.Count || Array.IndexOf(MainSequence, _kinds[born].Id) < 0) return double.NaN;
            double m, life, age0; LocalLife(k, born, out m, out life, out age0); return m >= 8 ? life * 1.1 - age0 : double.NaN;
        }
        /// <summary>What star k round you is now: its kind (index into the kinds; -1 gone from sight, a black hole), from its own mass and age.</summary>
        private int LocalKindNow(int k, int bornKind)
        {
            if (bornKind < 0 || bornKind >= _kinds.Count || Array.IndexOf(MainSequence, _kinds[bornKind].Id) < 0) return bornKind;   // (a giant, a remnant, a brown dwarf: as it is)
            double m, life, age0; LocalLife(k, bornKind, out m, out life, out age0); double age = age0 + Math.Max(0, _simYears);   // (somewhere in its main sequence at the start, older with the game's time)
            if (age < life) return bornKind;
            if (age < life * 1.1) { int g = KindIx(m >= 8 ? "red-supergiant" : "red-giant"); return g >= 0 ? g : bornKind; }
            int r = KindIx(m < 8 ? "white-dwarf" : m < 25 ? "neutron-star" : "black-hole"); return r;   // (a black hole is not among the kinds drawn: gone from sight)
        }
        // ── the open system's star's flares (content/physics.unt @law flares): so many a day by its kind, each at its own place on its face,
        // its energy from the power law (the number falling as the energy to the minus 1.8: a flare ten times brighter about seven times
        // rarer), rising in a minute and fading over its minutes; drawn where they face you ──
        private UploadBuffer _flareBuf; private readonly StarVertex[] _flareV = new StarVertex[64]; private Dictionary<string, double> _flarePerDay; private double _flarePow = 1.8, _flareLo = 10, _flareHi = 60;
        private double FlaresPerDay()
        {
            if (_flarePerDay == null)
            {
                _flarePerDay = new Dictionary<string, double>();
                foreach (var e in _content.OfKind("physics.unt", "law"))
                {
                    if (e.Id != "flares") continue; double lo, hi;
                    foreach (var part in e.Get("per-day", "").Split(',')) { var b = part.Trim().Split(' '); double v; if (b.Length == 2 && double.TryParse(b[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) _flarePerDay[b[0]] = v; }
                    if (e.TryRange("energy-power", out lo, out hi)) _flarePow = lo; if (e.TryRange("minutes", out lo, out hi)) { _flareLo = lo; _flareHi = hi; }
                }
            }
            double r; return _flarePerDay.TryGetValue(SystemStarId(), out r) ? r : 0;
        }
        private int Flares(Vec3 toEye)
        {
            double perDay = FlaresPerDay(); if (perDay <= 0 || _system == null) return 0; int n = 0; double days = _simYears * 365.25; long d0 = (long)Math.Floor(days);
            for (long d = d0 - 1; d <= d0 && n < _flareV.Length; d++)
            {
                int key = (int)(d * 7919) ^ _sysStar * 104729; int count = (int)Math.Floor(perDay) + (Hash01(key, 1) < perDay - Math.Floor(perDay) ? 1 : 0);
                for (int q = 0; q < count && n < _flareV.Length; q++)
                {
                    double start = d + Hash01(key, 10 + q), mins = _flareLo + (_flareHi - _flareLo) * Hash01(key, 20 + q), age = (days - start) * 1440; if (age < 0 || age > mins * 3) continue;
                    double E = Math.Pow(Math.Max(1e-9, 1 - Hash01(key, 30 + q)), -1 / (_flarePow - 1)), shape = age < 1 ? age : Math.Exp(-(age - 1) / mins);   // (a minute up, its minutes down)
                    double u = Hash01(key, 40 + q) * 2 - 1, a = Hash01(key, 50 + q) * 6.2832, sq = Math.Sqrt(1 - u * u); var dir = new Vec3((float)(sq * Math.Cos(a)), (float)u, (float)(sq * Math.Sin(a)));
                    if (Vec3.Dot(dir, toEye) <= 0.05f) continue;                               // (on the far side: unseen)
                    float b = (float)(Math.Min(4, 0.6 * Math.Sqrt(E)) * shape);
                    _flareV[n++] = new StarVertex { X = dir.X * 1.002f, Y = dir.Y * 1.002f, Z = dir.Z * 1.002f, Size = (float)(0.02 * Math.Min(5, Math.Sqrt(E))), R = b, G = b * 0.95f, B = b * 1.1f, A = 1 };
                }
            }
            return n;
        }
        // ── a blast's gamma-ray burst (content/physics.unt @law gamma-ray-bursts): one in a hundred stripped-core supernovae a collapsar
        // with a long burst, every kilonova a short one; its jets along a way fixed by the blast's seed, seen only from within a few degrees ──
        private double _grbLong = -1;
        private void GrbLine(List<string> L, Sn sn)
        {
            if (_grbLong < 0) { _grbLong = 0.01; foreach (var e in _content.OfKind("physics.unt", "law")) if (e.Id == "gamma-ray-bursts") { double a, b; if (e.TryRange("long-share", out a, out b)) _grbLong = a; } }
            int key = (int)(sn.T * 1000) ^ (int)(sn.Ux * 1e3); bool kilo = sn.K.Id == "kilonova", stripped = sn.K.Id == "ib-c";
            if (!kilo && !(stripped && Hash01(key, 61) < _grbLong)) return;
            // the jet's way, and how far off it you look from: within about five degrees of it the burst is seen
            double u = Hash01(key, 62) * 2 - 1, a2 = Hash01(key, 63) * 6.2832, sq = Math.Sqrt(1 - u * u), jx = sq * Math.Cos(a2), jy = u, jz = sq * Math.Sin(a2);
            double dx = (sn.Ux - _ax) - _ox, dy = (sn.Uy - _ay) - _oy, dz = (sn.Uz - _az) - _oz, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double off = dl > 0 ? Math.Acos(Math.Min(1, Math.Abs(-(dx * jx + dy * jy + dz * jz) / dl))) * 180 / Math.PI : 90;
            L.Add((kilo ? "it fired a short gamma-ray burst (under two seconds)" : "a collapsar: it fired a long gamma-ray burst (seconds to minutes)") + " along its spin; you are " + off.ToString("0") + " degrees off its jet" + (off < 5 ? ": you saw it, the brightest flash there is" : ": seen only from within a few degrees"));
        }
        // ── a star's wind (content/physics.unt @law winds): its mass blown away a year, by its kind ──
        private Dictionary<string, double> _winds;
        private double WindSolPerYear(string kindId)
        {
            if (_winds == null)
            {
                _winds = new Dictionary<string, double>();
                foreach (var e in _content.OfKind("physics.unt", "law")) if (e.Id == "winds") foreach (var part in e.Get("solar-masses-a-year", "").Split(',')) { var b = part.Trim().Split(' '); double v; if (b.Length == 2 && double.TryParse(b[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) _winds[b[0]] = v; }
            }
            double r; return _winds.TryGetValue(kindId ?? "", out r) ? r : 0;
        }
    }
}
