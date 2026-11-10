// Universe · generators · a whole planet's ground, from orbit down to a few metres.
// The planet is a cube blown up into a ball. Each of the 6 faces is split into smaller and smaller square
// patches near the camera (a patch splits into 4), so the land gets more detailed the closer you come.
// Heights and colours come from our own layered noise worked out in double precision. No life is placed:
// bare rock, soil, sand, salt, snow, ice, water and lava only.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    internal sealed class PlanetTerrain : IDisposable
    {
        public readonly string Kind, Name;
        public readonly double Radius;          // km
        public readonly bool Solid;             // false for gas and ice giants: no ground, only cloud tops
        public double[] StarDir = { 0.78, 0.35, 0.52 };
        public float[] SunColour, NightColour;
        /// <summary>Whether the ground's sand is worked out per pixel on the card (not once the real grains are drawn over it).</summary>
        public bool SandOnCard = true;
        /// <summary>The detail the ground is built for (1 = the usual): its upload time a frame and its builders scale with it.</summary>
        public static double Power = 1;
        /// <summary>Pieces waiting to be built or put on the card: when this is large, the ground is not keeping up.</summary>
        public int Backlog { get { lock (_askLock) return _inFlight.Count + _asks.Count; } }
        /// <summary>Where the camera looks (unit, planet frame) and the half-angle the view covers corner to corner; null: everything round you.</summary>
        public double[] ViewDir; public double ViewHalfAngle = 0.85;           // the light on the ground by day and by night (null: plain white)   // direction the starlight comes from (unit length)
        private readonly uint _seed;
        private const int Grid = 16;            // quads along a patch edge
        // deepest split for each layer: the ground goes down to about a millionth of a metre (microbe size),
        // the sea surface to a few centimetres, the clouds to about a kilometre
        private static readonly int[] MaxLevel = { 44, 26, 10, 44 };
        /// <summary>The finest piece edge (km) the ground is split to, 0 for no limit: set to a few grains, since below that the grains
        /// themselves are drawn (over the ground, and as the matter) and finer pieces carry no shape of their own.</summary>
        public double FinestEdgeKm;
        private const int MaxCached = 1600;
        private readonly Dictionary<PKey, Patch> _cache = new Dictionary<PKey, Patch>();
        public const int Ground = 0, Water = 1, Clouds = 2, Hull = 3;

        // ── Dyson shells: a hollow world turned inside out ──
        /// <summary>True for a Dyson shell or ring: the land is on the inside, facing the star at the centre;
        /// the spin's centrifugal gravity holds everything onto the inner surface.</summary>
        public bool Shell { get; private set; }
        public bool Habitable { get; private set; }     // a landscape inside (land, seas, clouds)
        public int Mask;                                // 0 whole, 1 a ring band, 2 part-built, 3 a curved mirror cap (stellar engine)
        public double HullKm = 40;                      // the engineered hull, outside the land
        public float HullR = 0.3f, HullG = 0.31f, HullB = 0.35f, HullGlow = 0.35f;

        /// <summary>A Dyson shell (or ring, or partial shell) around a star, radius in km.</summary>
        public static PlanetTerrain MakeShell(IntPtr device, string name, uint seed, double radiusKm, bool habitable, int mask)
        {
            var t = new PlanetTerrain(device, habitable ? "dyson-shell" : "dyson-hull", name, seed, false, radiusKm);
            t.Shell = true; t.Habitable = habitable; t.Mask = mask;
            return t;
        }
        public readonly bool HasWater, HasClouds;
        public readonly double CloudKm, AirKm;
        public readonly float AirR, AirG, AirB;
        private UploadBuffer _airShell; private int _airCount;

        private struct PKey : IEquatable<PKey>
        {
            public long A, B;
            public bool Equals(PKey o) { return A == o.A && B == o.B; }
            public override bool Equals(object o) { return o is PKey && Equals((PKey)o); }
            public override int GetHashCode() { return (A * 397 ^ B).GetHashCode(); }
        }
        private readonly IntPtr _device;
        private int _frame, _budget;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private double _msLimit = 9;               // milliseconds per frame spent making new detail
        /// <summary>The size (km) of the finest ground piece ready under the camera. The camera may not go lower
        /// than a few of its steps, so it always lands on finished ground and never inside a rough early piece.</summary>
        public double ReadyStep { get; private set; }
        /// <summary>For the log: the edge (km) and level of the finished piece under the camera this frame, 0 when none was found.</summary>
        public double CamPieceEdge; public int CamPieceLevel;
        /// <summary>The processor's part of the last frame on this ground, by phase: pieces drawn, taken in, chosen, drawn, the rest (for the stats card and the log).</summary>
        public string FrameStats { get; private set; }
        private int _uploadsLast;
        // the work arrays a piece is built in: one set per thread, so pieces are built in the background on every core
        private sealed class Work
        {
            public readonly StarVertex[] Scratch = new StarVertex[Grid * Grid * 6 + 4 * Grid * 6];
            public readonly double[] Hx = new double[(Grid + 1) * (Grid + 1)], Hy = new double[(Grid + 1) * (Grid + 1)], Hz = new double[(Grid + 1) * (Grid + 1)], Hh = new double[(Grid + 1) * (Grid + 1)];
            public readonly float[] Ca = new float[(Grid + 1) * (Grid + 1)], Cr = new float[(Grid + 1) * (Grid + 1)], Cg = new float[(Grid + 1) * (Grid + 1)], Cb = new float[(Grid + 1) * (Grid + 1)], Cgl = new float[(Grid + 1) * (Grid + 1)];
        }
        [ThreadStatic] private static Work _work;
        private static Work W { get { return _work ?? (_work = new Work()); } }
        private static StarVertex[] _scratch { get { return W.Scratch; } }
        private static double[] _hx { get { return W.Hx; } }
        private static double[] _hy { get { return W.Hy; } }
        private static double[] _hz { get { return W.Hz; } }
        private static double[] _hh { get { return W.Hh; } }
        private static float[] _ca { get { return W.Ca; } }
        private static float[] _cr { get { return W.Cr; } }
        private static float[] _cg { get { return W.Cg; } }
        private static float[] _cb { get { return W.Cb; } }
        private static float[] _cgl { get { return W.Cgl; } }

        private sealed class Patch
        {
            public double Cx, Cy, Cz, Edge; public UploadBuffer Buffer; public int Count; public int LastUsed; public int Level; public int Gen;
            public StarVertex[] Verts;          // built, not yet on the graphics card
        }

        public PlanetTerrain(IntPtr device, string kind, string name, uint seed, bool resourceView = false, double radiusKm = 0)
        {
            _device = device; Kind = kind; Name = name; _seed = seed; ResourceView = resourceView;
            var tk = Instance(kind, seed); Radius = tk.Radius;                    // this world's own traits, rolled from its kind's ranges
            if (radiusKm > 0) Radius = radiusKm;
            Solid = tk.Solid; HasWater = tk.Water; HasClouds = tk.Clouds;
            CloudKm = tk.CloudKm; AirKm = tk.AirKm; AirR = tk.Air[0]; AirG = tk.Air[1]; AirB = tk.Air[2];
            if (tk.AirBar > 0 && AirB >= AirR)                                     // (a clear air's: a dusty one's sky is its dust's light, as Mars's butterscotch, kept as its kind gives it)
            {
                // the air above you by its pressure: its kind's colour is its make-up's (blue for a clear air, butterscotch for a dusty one),
                // its light scattered as the column's depth through it, Rayleigh's at red, green and blue (Earth's 0.05, 0.097, 0.21 at one
                // bar) times its pressure; the sky's light as one less e to the minus that depth, set against Earth's, so a thin air (Mars's
                // 6 thousandths of a bar) is dark by day and a thick one (Venus's 92) saturates toward white
                double[] tau = { 0.05, 0.097, 0.21 }; double[] c = { AirR, AirG, AirB }; double top = 0;
                for (int k = 0; k < 3; k++) { c[k] *= (1 - Math.Exp(-tau[k] * tk.AirBar)) / (1 - Math.Exp(-tau[k])); top = Math.Max(top, c[k]); }
                if (top > 1) for (int k = 0; k < 3; k++) c[k] /= top;
                AirR = (float)c[0]; AirG = (float)c[1]; AirB = (float)c[2];
            }
            _tk = tk; MixId = tk.Grains;                                                 // (the ground its matter is made of: its kind's grains)
            _reliefScale = tk.ReliefOfRadius > 0 ? Radius * tk.ReliefOfRadius / 8.0 : 1;   // (a small body: its relief a share of its radius, the base relief being about eight kilometres on a world)
        }

        // ── each kind of world is a template in content/terrain.unt (none named in code): any number may be a range
        // (a..b) and any colour a range (r g b .. r g b); every world of the kind takes its own values from its seed, so no
        // two are the same, and the same seed always gives the same world ──
        public sealed class TerrainKindData { public double Radius = 6371, CloudKm = 9, AirKm = 110, CloudThick = 1, BaseK = 255, Albedo = 0.3, Tau, AirBar; public bool Solid = true, Water, Clouds = true; public float[] Air = { 0.35f, 0.56f, 1f }, Cloud = { 0.97f, 0.97f, 0.97f }, Rock = { 0.4f, 0.38f, 0.36f };
            public double LandShare = 0.06, CloudShift, DayHours = 24, Tides = 0.5, Bands = 19, GreatStorm = 1, ReliefOfRadius;   // ReliefOfRadius: the ground's relief as a share of the world's radius (a small body's lumps and craters); 0 for a world's own kilometres
            public bool Molten, Dunes, FrozenSea, SnowPatches, Locked, PolarIce = true, Dry, TropicalSoil, Life = true, ManyVolcanoes, Rivers = true, SeaFloorVolcanoes;
            public double VolcanoCount = 30; public string Grains;
            public double[][] Ground;                  // land colours by height: below h, r g b, and 1 if the ground's patchiness tints it
            public float[] BandLow = { 0.42f, 0.26f, 0.16f }, BandHigh = { 0.96f, 0.88f, 0.78f }, Storm = { 0.96f, 0.93f, 0.86f }, River = { 0.1f, 0.3f, 0.5f }; }
        private static readonly Dictionary<string, TerrainKindData> Kinds = new Dictionary<string, TerrainKindData>();
        private static readonly Dictionary<string, ContentEntry> KindEntries = new Dictionary<string, ContentEntry>();
        private static ContentEntry _defEntry;
        private TerrainKindData _tk;
        /// <summary>Whether one face always points at its star (by its orbit, not its kind): scorched day side, frozen night side, a twilight ring.</summary>
        public bool Locked { get { return _tk.Locked; } set { _tk.Locked = value; } }
        /// <summary>This world's own traits (its kind's template, with its own values from its seed).</summary>
        public TerrainKindData Traits { get { return _tk; } }
        /// <summary>A kind's template, each range at its middle (for what is the same for every world of the kind).</summary>
        public static TerrainKindData TerrainKind(string kind) { TerrainKindData t; return Kinds.TryGetValue(kind ?? "", out t) ? t : Kinds.TryGetValue("default", out t) ? t : new TerrainKindData(); }
        /// <summary>A world of this kind with this seed: its kind's ranges rolled for it.</summary>
        public static TerrainKindData Instance(string kind, uint seed)
        {
            ContentEntry e;
            if (!KindEntries.TryGetValue(kind ?? "", out e) && !KindEntries.TryGetValue("default", out e)) return new TerrainKindData();
            return Build(e, seed, false);
        }
        private static void LoadKinds(ContentLibrary content)
        {
            _defEntry = null; foreach (var e in content.OfKind("terrain.unt", "terrain")) if (e.Id == "default") _defEntry = e;
            foreach (var e in content.OfKind("terrain.unt", "terrain")) { KindEntries[e.Id] = e; Kinds[e.Id] = Build(e, 0, true); }
        }
        private static TerrainKindData Build(ContentEntry e, uint seed, bool middle)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float; var def = _defEntry;
            Func<string, string> g = k => e.Props.ContainsKey(k) ? e.Get(k, "") : def != null ? def.Get(k, "") : "";
            // where in a range this world falls, for each key on its own (the middle for the template)
            Func<string, double> at = k => { if (middle) return 0.5; uint h = seed * 2654435761u; foreach (char ch in k) h = (h ^ ch) * 16777619u; return Hash(h, h >> 7, 3, seed ^ 0x5EEDu); };
            Func<string, double, double> d = (k, f) =>
            {
                string v = g(k); int dots = v.IndexOf("..", StringComparison.Ordinal); double lo, hi;
                if (dots < 0) return double.TryParse(v, fs, ci, out lo) ? lo : f;
                if (!double.TryParse(v.Substring(0, dots), fs, ci, out lo) || !double.TryParse(v.Substring(dots + 2), fs, ci, out hi)) return f;
                return lo + (hi - lo) * at(k);
            };
            Func<string, float[], float[]> c = (k, f) =>
            {
                string v = g(k); var halves = v.Split(new[] { ".." }, StringSplitOptions.None);
                Func<string, float[]> rgb = s => { var w = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); float x, y, z; return w.Length == 3 && float.TryParse(w[0], fs, ci, out x) && float.TryParse(w[1], fs, ci, out y) && float.TryParse(w[2], fs, ci, out z) ? new[] { x, y, z } : null; };
                var p0 = rgb(halves[0]); if (p0 == null) return f; if (halves.Length < 2) return p0;
                var p1 = rgb(halves[1]); if (p1 == null) return p0; float u = (float)at(k);
                return new[] { p0[0] + (p1[0] - p0[0]) * u, p0[1] + (p1[1] - p0[1]) * u, p0[2] + (p1[2] - p0[2]) * u };
            };
            var t = new TerrainKindData();
            t.Radius = d("radius-km", 6371); t.CloudKm = d("cloud-km", 9); t.AirKm = d("air-km", 110); t.CloudThick = d("cloud-thickness", 1); t.BaseK = d("base-k", 255); t.Albedo = d("albedo", 0.3); t.Tau = d("optical-depth", 0); t.AirBar = d("air-bar", 0);
            t.Solid = g("solid") != "no"; t.Water = g("water") == "yes"; t.Clouds = g("clouds") != "no";
            t.Air = c("air-colour", t.Air); t.Cloud = c("cloud-colour", t.Cloud); t.Rock = c("rock-colour", t.Rock);
            Func<string, bool, bool> yes = (k, f) => { var v = g(k); return v == "" ? f : v == "yes"; };
            t.LandShare = d("land-share", 0.06); t.CloudShift = d("cloud-shift", 0); t.DayHours = d("day-hours", 24); t.Tides = d("tides", 0.5); t.ReliefOfRadius = d("relief-of-radius", 0);
            t.Bands = Math.Round(d("bands", 19)); t.GreatStorm = d("great-storm", 1);
            t.Molten = yes("molten", false); t.Dunes = yes("dunes", false); t.FrozenSea = yes("frozen-sea", false); t.SnowPatches = yes("snow-patches", false);
            t.Locked = yes("tidally-locked", false); t.PolarIce = yes("polar-ice", true); t.Dry = yes("dry", false); t.TropicalSoil = yes("tropical-soil", false);
            t.Life = yes("life", true); t.ManyVolcanoes = g("volcanoes") == "many";
            t.Grains = g("grains").Length > 0 ? g("grains") : null; t.Rivers = yes("rivers", true); t.SeaFloorVolcanoes = yes("seafloor-volcanoes", false); t.VolcanoCount = d("volcano-count", 30);
            t.BandLow = c("band-dark", t.BandLow); t.BandHigh = c("band-light", t.BandHigh); t.Storm = c("storm-colour", t.Storm); t.River = c("river-colour", t.River);
            var gl = g("ground");
            if (gl.Length > 0)
            {
                var bands = new List<double[]>();
                foreach (var part in gl.Split(';'))
                {
                    var w = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (w.Length < 4) continue;
                    var row = new double[5]; for (int q = 0; q < 4; q++) double.TryParse(w[q], fs, ci, out row[q]); row[4] = w.Length > 4 && w[4] == "tinted" ? 1 : 0; bands.Add(row);
                }
                if (bands.Count > 0) t.Ground = bands.ToArray();
            }
            return t;
        }

        // ── shape and colour ─────────────────────────────────────────────

        private static double Hash(long x, long y, long z, uint seed)
        {
            ulong h = (ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)x * 0xBF58476D1CE4E5B9UL ^ (ulong)y * 0x94D049BB133111EBUL ^ (ulong)z * 0xD6E8FEB86659FD93UL;
            h ^= h >> 31; h *= 0x7FB5D329728EA185UL; h ^= h >> 27; h *= 0x81DADEF4BC2DD44DUL; h ^= h >> 33;
            return (h >> 11) * (1.0 / 9007199254740992.0);
        }

        private static double Value(double x, double y, double z, uint seed)
        {
            double fx0 = Math.Floor(x), fy0 = Math.Floor(y), fz0 = Math.Floor(z);
            long ix = (long)fx0, iy = (long)fy0, iz = (long)fz0;
            double fx = x - fx0, fy = y - fy0, fz = z - fz0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            double a = Hash(ix, iy, iz, seed), b = Hash(ix + 1, iy, iz, seed), c = Hash(ix, iy + 1, iz, seed), d = Hash(ix + 1, iy + 1, iz, seed);
            double e = Hash(ix, iy, iz + 1, seed), f = Hash(ix + 1, iy, iz + 1, seed), g = Hash(ix, iy + 1, iz + 1, seed), h = Hash(ix + 1, iy + 1, iz + 1, seed);
            double x0 = a + (b - a) * fx, x1 = c + (d - c) * fx, x2 = e + (f - e) * fx, x3 = g + (h - g) * fx;
            double y0 = x0 + (x1 - x0) * fy, y1 = x2 + (x3 - x2) * fy;
            return y0 + (y1 - y0) * fz;
        }

        // each finer layer of noise turned to its own angle (the same turn as the card's clouds), so no layer's grid lines up
        // with the one before: nature does not build in straight lines, and noise on one grid shows its rows
        private static void TurnLayer(ref double x, ref double y, ref double z)
        {
            double a = 0.00 * x + 0.80 * y + 0.60 * z, b = -0.80 * x + 0.36 * y - 0.48 * z, c = -0.60 * x - 0.48 * y + 0.64 * z;
            x = a; y = b; z = c;
        }

        /// <summary>Layered noise, 0..1. <paramref name="first"/> is the first layer's frequency.</summary>
        private static double Fbm(double x, double y, double z, uint seed, double first, int layers)
        {
            double sum = 0, amp = 0.5, norm = 0, f = first;
            for (int i = 0; i < layers; i++) { sum += Value(x * f + i * 17.3, y * f - i * 9.1, z * f + i * 3.7, seed + (uint)i * 101u) * amp; norm += amp; amp *= 0.5; f *= 2; TurnLayer(ref x, ref y, ref z); }
            return sum / norm;
        }

        /// <summary>Layered noise, -1..1, where each finer layer keeps <paramref name="gain"/> of the one before:
        /// a gain near 0.6 keeps real roughness at every scale, from hills down to stones.</summary>
        private static double Rough(double x, double y, double z, uint seed, double first, int layers, double gain)
        {
            double sum = 0, amp = 1, f = first;
            for (int i = 0; i < layers; i++) { sum += (Value(x * f + i * 7.7, y * f - i * 3.3, z * f + i * 1.9, seed + (uint)i * 31u) * 2 - 1) * amp; amp *= gain; f *= 2; TurnLayer(ref x, ref y, ref z); }
            return sum;
        }

        /// <summary>Fine roughness: from layer <paramref name="from"/> to <paramref name="to"/>, each layer's bumps are about a
        /// fifth of their own width tall, so the ground looks just as rough at a metre, a millimetre or a micrometre.</summary>
        private double _grainKm { get { var m = GrainMix(0); return m != null ? m.Size : 0; } }
        private double Micro(double x, double y, double z, uint seed, int from, int to)
        {
            double sum = 0, f = 48 * Math.Pow(2, from);
            for (int i = from; i < to; i++)
            {
                double width = Radius / f;                           // km across one bump at this layer
                if (_grainKm > 0 && width < _grainKm * 0.25) break;  // finer than a quarter of a sand grain: the grains themselves take over (the stones lie on it all the way)
                sum += Waveform.Layer(x * f + i * 3.1, y * f - i * 5.3, z * f + i * 2.9, seed, i, 26) * 2.2 * Math.Min(width * 0.18, 3.0);   // each layer one octave of the waveform (stored CDF 9/7 levels, mode-E past them)
                f *= 2;
            }
            return sum;
        }

        private static double Ridged(double x, double y, double z, uint seed, double first, int layers)
        {
            double sum = 0, amp = 0.5, norm = 0, f = first;
            for (int i = 0; i < layers; i++)
            {
                double v = 1 - Math.Abs(Value(x * f + 5.1, y * f + i, z * f - 2.3, seed + 7000u + (uint)i) * 2 - 1);
                sum += v * v * amp; norm += amp; amp *= 0.5; f *= 2; TurnLayer(ref x, ref y, ref z);
            }
            return sum / norm;
        }

        /// <summary>Ground height in km above the sea for a unit direction, with the colour and glow of the surface there.
        /// <paramref name="layers"/> is how much fine detail to add (more when the camera is close).</summary>
        private double _reliefScale = 1;
        public double Height(double x, double y, double z, int layers, out float r, out float g, out float b, out float glow)
        {
            if (Shell && Habitable)
            {
                // the inside of the orb is a planet landscape turned inside out: the same working terrain as the planets,
                // sized to the orb itself, so it curves up and around you with the star at the centre
                _latOverride = 0.25;                                  // the star is overhead everywhere: no frozen poles
                double hs = HeightBase(x, y, z, layers, out r, out g, out b, out glow) * _reliefScale;
                _latOverride = -1;
                int biome = (int)(Fbm(x, y, z, _seed + 970u, 3, 3) * 8) % 8;   // regions with their own terraformed climate
                float tv = (float)(0.85 + 0.3 * _tint);
                if (hs < 0)
                {
                    double dp = Math.Min(1, -hs / 3);                    // seas, painted flat at sea level
                    r = (float)(0.1 - 0.07 * dp); g = (float)(0.32 - 0.2 * dp); b = (float)(0.55 - 0.25 * dp);
                    hs = 0;
                }
                else if (hs < 3.6 && glow == 0)
                {
                    float[,] pal = { { 0.27f, 0.47f, 0.2f }, { 0.22f, 0.5f, 0.24f }, { 0.33f, 0.48f, 0.25f }, { 0.6f, 0.55f, 0.32f },
                                     { 0.2f, 0.36f, 0.22f }, { 0.36f, 0.47f, 0.24f }, { 0.3f, 0.43f, 0.27f }, { 0.55f, 0.44f, 0.3f } };
                    double up2 = Math.Max(0, Math.Min(1, (hs - 2.0) / 1.4));          // higher up: rock shows through
                    r = (float)(pal[biome, 0] * tv * (1 - up2) + 0.46 * up2); g = (float)(pal[biome, 1] * tv * (1 - up2) + 0.44 * up2); b = (float)(pal[biome, 2] * tv * (1 - up2) + 0.4 * up2);
                    if (hs < 0.06) { r = 0.78f * tv; g = 0.72f * tv; b = 0.54f * tv; }     // beaches
                }
                if (Mask == 1)
                {
                    // a ring world: tall walls along both edges hold the air in
                    double edge = Math.Abs(y) / 0.045;
                    if (edge > 0.82)
                    {
                        double t = Math.Min(1, (edge - 0.82) / 0.1);
                        hs = Math.Max(hs, t * t * 1500);
                        if (t > 0.3) { r = 0.42f; g = 0.43f; b = 0.47f; glow = 0; }
                    }
                }
                LastField = -1; return hs;
            }
            // drifting plates: the ground here is the ground that was carried here from where its plate started
            double ox = x, oy = y, oz = z, edgeAt = 9; int edgeKind = 2;
            double x2 = x, y2 = y, z2 = z, share = 1;
            if (Drift != null && Solid) Drift(ox, oy, oz, out x, out y, out z, out x2, out y2, out z2, out share, out edgeAt, out edgeKind);
            if (Drift != null && Solid) _latOverride = Math.Abs(oy);                // climate follows where the ground is now
            double h = HeightBase(x, y, z, layers, out r, out g, out b, out glow) * _reliefScale;
            if (!Solid) { LastField = -1; return h; }
            h = Landforms(x, y, z, h, layers, ref r, ref g, ref b, ref glow);
            if (share < 1)
            {
                // near a plate edge, the ground of both plates is blended, half and half right on the edge, so the two join
                // smoothly instead of standing as a cliff
                float r2, g2, b2, gl2;
                double h2 = HeightBase(x2, y2, z2, layers, out r2, out g2, out b2, out gl2) * _reliefScale;
                h2 = Landforms(x2, y2, z2, h2, layers, ref r2, ref g2, ref b2, ref gl2);
                float w = (float)share;
                h = h * share + h2 * (1 - share); r = r * w + r2 * (1 - w); g = g * w + g2 * (1 - w); b = b * w + b2 * (1 - w); glow = glow * w + gl2 * (1 - w);
            }
            if (edgeAt < 1)
            {
                // plate edges shape the ground: pulling apart, a deep ridge under the sea and a shallow rift valley on land;
                // crashing, mountains crumple up
                double t = 1 - edgeAt, s2 = t * t * (3 - 2 * t);
                if (edgeKind == 0) { if (h < 0) { h = h * (1 - s2) + (-2.6 + 0.6 * t) * s2; r = 0.18f; g = 0.2f; b = 0.22f; } else h -= 0.35 * s2; }
                else if (edgeKind == 1) { h = Math.Max(h, 0) + 3.5 * s2; double rock = Math.Min(1, s2 * 1.5); r = (float)(r * (1 - rock) + 0.5 * rock); g = (float)(g * (1 - rock) + 0.47 * rock); b = (float)(b * (1 - rock) + 0.43 * rock); }
            }
            LastField = ClassifyField(x, y, z, h, layers, ref r, ref g, ref b, ref glow);
            _latOverride = -1;
            x = ox; y = oy; z = oz;
            h = Reshaped(x, y, z, h);                                                // craters and sea level are where they are now
            h = Grains(x, y, z, h, layers, ref r, ref g, ref b, glow);
            if (ResourceView) ViewColour(LastField, h, ref r, ref g, ref b, ref glow);
            return h;
        }

        /// <summary>Which ground's grains this world has (content/minerals.unt @mix): its kind, or what it is set to (a moon's
        /// regolith); the same grains the matter view goes into.</summary>
        public string MixId;
        public ulong GrainSeed { get { return (ulong)_seed * 0x9E3779B97F4A7C15UL + 0x6A1u; } }
        /// <summary>Where its permanent ice begins (the sine of the latitude): its climate's, moving with its ice ages (src/gen/Climate.cs); 0.86 until it is worked out.</summary>
        public double IceSin = 0.86;
        public GrainField.Mix GrainMix(double y) { if (!Solid) return null; if (Math.Abs(y) > IceSin && GrainField.For("ice") != null) return GrainField.For("ice"); return GrainField.For(MixId ?? Kind) ?? GrainField.For("desert"); }   // (the same choice the matter view makes)
        // the ground's finest detail: the top of its grains, coming in as the detail grows finer than a couple of grains, so
        // zooming in, the ground itself breaks into its grains (each its own mineral, shape and shade), never a layer laid over it
        private double Grains(double x, double y, double z, double h, int layers, ref float r, ref float g, ref float b, float glow)
        {
            if (_noGrainsNow || !Solid || Shell || glow > 0.01 || layers <= 6) return h;   // (the sea bed too: it is dived to)
            var gm = GrainMix(y); if (gm == null || gm.Size <= 0) return h;
            double W = Math.Max(Radius * 0.1125 / Math.Pow(2, layers - 6) * 2, _cellAtLeast), G = gm.Size;   // the finest detail here (km): twice the real spacing of this piece's corners, so a stone comes into the ground's shape only once its corners can round it (never a single coloured corner: a soft hexagon)
            double big = GrainField.SizeOf(gm, GrainField.Classes(gm) - 1);              // the biggest stones come in first
            if (W > big * 0.25) return h;
            double wgt = Math.Max(0, Math.Min(1, (G * 0.25 - W) / (G * 0.15)));         // the sand's own share (each size class weighs itself in Top)
            double top; GrainField.Grain gr;
            bool on = GrainField.Top(x, y, z, Radius, gm, GrainSeed, Math.Max(W, 1e-13), out top, out gr);
            float cr, cg, cb;
            if (on)
            {
                var c = gm.Colour[gr.Mineral]; float sr, sg, sb;
                double ex, ey, ez, nx, ny, nz; GrainField.Axes(x, y, z, out ex, out ey, out ez, out nx, out ny, out nz);
                double ox = (x - gr.Dx) * Radius, oy = (y - gr.Dy) * Radius, oz = (z - gr.Dz) * Radius, la = ox * ex + oy * ey + oz * ez, lb = ox * nx + oy * ny + oz * nz, lu = top + gr.Depth, ll = Math.Sqrt(la * la + lb * lb + lu * lu) + 1e-30;
                GrainField.Stain(gr.Key, la / ll, lu / ll, lb / ll, gr.Rad, Math.Max(W, 1e-13), out sr, out sg, out sb);   // its stains, the same the matter view shows
                cr = c[0] * gr.Shade * sr; cg = c[1] * gr.Shade * sg; cb = c[2] * gr.Shade * sb;
            }
            else { cr = r * 0.25f; cg = g * 0.25f; cb = b * 0.25f; }                    // down in a gap between grains
            float w = (float)(on && gr.Rad > G ? 1 : wgt); r = r * (1 - w) + cr * w; g = g * (1 - w) + cg * w; b = b * (1 - w) + cb * w;   // a stone shows its colour as soon as it shows
            return h + top;                                                              // (Top has weighed each size in as it comes)
        }

        // ── the planet changing shape over time ──
        /// <summary>Bumped whenever the planet's shape changes; pieces made before are remade, a few each frame.</summary>
        public int Generation;
        /// <summary>How far the seas have fallen (km; ice ages lock water in ice), so the ground stands that much higher against them.</summary>
        public double SeaDropKm;
        /// <summary>How tall each place's mountains now stand against how they began (from erosion and uplift); null: unchanged.</summary>
        public Func<double, double, double, double> MountainScale;
        /// <summary>Where the ground at a spot came from, as its plate carried it, and where the neighbouring plate's would have
        /// come from (both unit vectors), how much of the spot is its own plate's ground (1 well inside, a half on the edge), how near a plate edge the
        /// spot is (0 on the edge, 1 or more well inside) and what kind of edge (0 spreading, 1 crashing, 2 sliding).</summary>
        public delegate void DriftFn(double x, double y, double z, out double bx, out double by, out double bz, out double bx2, out double by2, out double bz2, out double share, out double edge, out int kind);
        /// <summary>The plates moving the ground; null: the ground stays where it formed.</summary>
        public DriftFn Drift;
        /// <summary>Craters from impacts: direction (unit x, y, z), radius in km.</summary>
        public List<double[]> Craters = new List<double[]>();          // replaced whole (never changed in place): background builders read it

        /// <summary>The shape the ground on screen was built for; it lags <see cref="Generation"/> until the new ground is ready.</summary>
        public int DrawnGeneration;
        /// <summary>How many times the whole planet's ground has been swapped to a new shape.</summary>
        public int Swaps;
        private readonly Dictionary<PKey, Patch> _pending = new Dictionary<PKey, Patch>();
        private int _missing, _framesWaiting;

        private void SwapGround()
        {
            foreach (var kv in _pending) { Patch old; if (_cache.TryGetValue(kv.Key, out old)) Release(old); _cache[kv.Key] = kv.Value; }
            _pending.Clear();
            // pieces of the old shape that were out of view are dropped, so they are made fresh in the new shape when needed
            var stale = new List<PKey>(); foreach (var kv in _cache) if (kv.Value.Gen != Generation) stale.Add(kv.Key);
            foreach (var k in stale) { Release(_cache[k]); _cache.Remove(k); }
            DrawnGeneration = Generation; _framesWaiting = 0; Swaps++;
        }

        // ── the planet's changes as coefficient waveforms ──
        // Each change is a field over the planet's six cube faces (Bake), stored as sparse CDF 9/7 wavelet terms (the lab's
        // engine, src/core/Wavelet97.cs) and read back by the rebuilt grid. Mountain height scale and the crater shapes are
        // two fields; the ground looks them up at any spot without touching every crater, and saves carry only the terms.
        public const int ChangeGrid = 512;                                           // cells along each cube face
        /// <summary>The change fields as wavelet equations: [0..5] mountain scale per face, [6..11] crater depth per face (km).</summary>
        public Wavelet97.Field[] ChangeTerms;
        private double[][] _change;                                                  // the rebuilt grids, read by the ground
        /// <summary>How many wavelet terms the change fields carry in all, against the grid cells they stand for.</summary>
        public int ChangeTermCount { get { int n = 0; if (ChangeTerms != null) foreach (var f in ChangeTerms) n += f.Terms; return n; } }

        /// <summary>Writes the planet's current changes (mountain scale, craters) into the wavelet change fields.</summary>
        public void BakeChanges()
        {
            int N = ChangeGrid;
            var terms = new Wavelet97.Field[12]; var grids = new double[12][];
            for (int fi = 0; fi < 12; fi++)
            {
                int f = fi % 6; bool crater = fi >= 6;
                var g = new double[N * N];
                Jobs.For(N, row =>
                {
                    for (int col = 0; col < N; col++)
                    {
                        double s = (col + 0.5) / N * 2 - 1, t = (row + 0.5) / N * 2 - 1, x, y, z; Dir(f, s, t, out x, out y, out z);
                        g[row * N + col] = crater ? CraterAt(x, y, z) : (MountainScale != null ? MountainScale(x, y, z) - 1 : 0);
                    }
                });
                Wavelet97.Forward(g, N, N);
                terms[fi] = Wavelet97.Keep(g, N, N, 0.01);                           // 99.99% of the change's energy
                grids[fi] = Wavelet97.Rebuild(terms[fi]);
            }
            ChangeTerms = terms; _change = grids;
        }

        /// <summary>Puts back change fields read from a save.</summary>
        public void LoadChanges(Wavelet97.Field[] terms)
        {
            var grids = new double[terms.Length][];
            for (int i = 0; i < terms.Length; i++) grids[i] = Wavelet97.Rebuild(terms[i]);
            ChangeTerms = terms; _change = grids;
        }

        // a change field read at a spot: the cube face it falls on, then the rebuilt grid smoothly between cells
        private double ChangeAt(int which, double x, double y, double z)
        {
            int f = 0; double best = -2;
            for (int q = 0; q < 6; q++) { double k = FaceN[q, 0] * x + FaceN[q, 1] * y + FaceN[q, 2] * z; if (k > best) { best = k; f = q; } }
            double s = (FaceU[f, 0] * x + FaceU[f, 1] * y + FaceU[f, 2] * z) / best, t = (FaceV[f, 0] * x + FaceV[f, 1] * y + FaceV[f, 2] * z) / best;
            int N = ChangeGrid;
            double gx = (s + 1) / 2 * N - 0.5, gy = (t + 1) / 2 * N - 0.5;
            int x0 = Math.Max(0, Math.Min(N - 2, (int)Math.Floor(gx))), y0 = Math.Max(0, Math.Min(N - 2, (int)Math.Floor(gy)));
            double fx = Math.Max(0, Math.Min(1, gx - x0)), fy = Math.Max(0, Math.Min(1, gy - y0));
            var g = _change[which * 6 + f];
            double a = g[y0 * N + x0] * (1 - fx) + g[y0 * N + x0 + 1] * fx, b = g[(y0 + 1) * N + x0] * (1 - fx) + g[(y0 + 1) * N + x0 + 1] * fx;
            return a * (1 - fy) + b * fy;
        }

        // the craters' shape at a spot, as the change field stores it
        private double CraterAt(double x, double y, double z)
        {
            double h = 0;
            var cl2 = Craters;
            for (int k = 0; k < cl2.Count; k++)
            {
                var c = cl2[k];
                double d = (1 - (x * c[0] + y * c[1] + z * c[2])) * 2;
                double rad = c[3] / Radius; if (d > rad * rad * 9) continue;
                double t = Math.Sqrt(d) / rad;
                double depth = Math.Min(4, 0.3 + c[3] * 0.025);
                double bowl = t < 0.6 ? 1 : 1 - (t - 0.6) / 0.4 * ((t - 0.6) / 0.4);
                h += t < 1 ? -depth * Math.Max(0, bowl) + depth * 0.15 * t * t : depth * 0.15 * Math.Exp(-(t - 1) * 3);
            }
            return h;
        }

        private double Reshaped(double x, double y, double z, double h)
        {
            if (_change != null)
            {
                // the baked change fields, rebuilt from their wavelet terms
                if (h > 0) h *= 1 + ChangeAt(0, x, y, z);
                return h + SeaDropKm + ChangeAt(1, x, y, z);
            }
            if (h > 0 && MountainScale != null) h *= MountainScale(x, y, z);
            h += SeaDropKm;
            var cl = Craters;
            for (int k = 0; k < cl.Count; k++)
            {
                var c = cl[k];
                double d = (1 - (x * c[0] + y * c[1] + z * c[2])) * 2;                 // about the squared angle apart
                double rad = c[3] / Radius; if (d > rad * rad * 9) continue;
                double t = Math.Sqrt(d) / rad;                                          // 0 at the middle, 1 at the rim
                double depth = Math.Min(4, 0.3 + c[3] * 0.025);                       // real big craters are shallow: a few km deep at most, flat-floored
                double bowl = t < 0.6 ? 1 : 1 - (t - 0.6) / 0.4 * ((t - 0.6) / 0.4);       // a flat floor, then walls rising to the rim
                h += t < 1 ? -depth * Math.Max(0, bowl) + depth * 0.15 * t * t : depth * 0.15 * Math.Exp(-(t - 1) * 3);   // floor, walls, a low rim fading into the land
            }
            return h;
        }

        private double HeightBase(double x, double y, double z, int layers, out float r, out float g, out float b, out float glow)
        {
            glow = 0;
            double lat = _latOverride >= 0 ? _latOverride : Math.Abs(y);
            if (!Solid)
            {
                // a giant's "surface" is its deep cloud deck: billowing tops tens of km high, rough down to the smallest puff
                int puff = Math.Max(0, layers - 6);
                GiantColour(x, y, z, Math.Min(puff, 8), out r, out g, out b);
                double billow = Fbm(x, y, z, _seed + 300u, 30, 3 + Math.Min(puff, 6));
                double towers = Ridged(x, y, z, _seed + 310u, 60, 3 + Math.Min(puff, 5));
                double hh = (billow - 0.5) * 30 + towers * towers * 25 + (puff > 8 ? Micro(x, y, z, _seed + 320u, 8, Math.Min(puff, 30)) * 0.5 : 0);
                float shade = (float)(0.85 + 0.25 * billow);
                r *= shade; g *= shade; b *= shade;
                return hh;
            }
            int fine = Math.Max(0, layers - 6);
            double cont = Fbm(x, y, z, _seed, 1.1, 5) * 2 - 1;                     // continents and ocean basins
            double ridge = Ridged(x, y, z, _seed, 3.0, 5);                          // mountain chains
            double detail = fine > 0 ? Rough(x, y, z, _seed + 55u, 48, Math.Min(fine, 10), 0.62) : 0;   // hills and valleys
            double micro = fine > 8 ? Micro(x, y, z, _seed + 66u, 8, fine) : 0;                         // rocks, stones, grains: the same roughness at every size
            double mtnFine = fine > 0 ? Ridged(x, y, z, _seed + 77u, 96, Math.Min(fine, 12)) : 0.5; // sharp crests and gullies in the mountains
            double tint = Fbm(x, y, z, _seed + 99u, 20, 3 + Math.Min(fine, 8));      // colour variety: patches of different ground
            double soil = Fbm(x, y, z, _seed + 123u, 60, 3 + Math.Min(fine, 10));
            double land, h;
            switch (Kind)
            {
                default: land = Shell && Habitable ? cont + _shellLand : cont + _tk.LandShare; break;   // (a shell: each terraformed section its own share of land)   // from its kind (terrain.unt): Earth-like about a third land
            }
            double mtn = ridge * ridge * Smooth(0, 0.35, land) * (Shell && Habitable ? _shellMtn : 1);
            _land = land; _mtn = mtn; _tint = tint;
            h = land * 3.2 + mtn * 7.5 + mtn * (mtnFine - 0.5) * 2.2 + detail * (0.12 + 0.35 * Smooth(0, 0.4, land) + 0.6 * mtn) + micro;
            if (_tk.Dunes)
            {
                double dune = Math.Sin(x * 900 + Fbm(x, y, z, _seed + 3u, 30, 3) * 40) * 0.5 + 0.5;  // long dune ridges
                h += dune * 0.06 * Smooth(0.0, 0.3, 0.9 - ridge);
            }
            float tv = (float)(0.9 + 0.2 * tint);
            // the ground of this kind of world (terrain.unt): molten, frozen seas, a locked day and night side, then its colours by height
            if (_tk.Molten)
            {
                double crack = Ridged(x, y, z, _seed + 900u, 5, 4 + Math.Min(fine, 8));
                if (crack > 0.9 - 0.04 * Math.Min(1, fine / 10.0) && h < 3) { r = 1f; g = (float)(0.3 + 0.35 * tint); b = 0.06f; glow = 0.9f; return h - 0.03; }   // glowing cracks and rivers
                if (h < -0.3) { r = 1f; g = (float)(0.35 + 0.3 * tint); b = 0.08f; glow = 0.95f; return -0.3; }   // molten lowlands
            }
            if (_tk.FrozenSea && h < 0) { r = 0.78f * tv; g = 0.88f * tv; b = 0.97f; return 0; }                // frozen sea: flat ice
            if (_tk.SnowPatches && (h > 2.5 || tint > 0.55)) { r = 0.95f; g = 0.96f; b = 1f; return h; }        // snow lying in patches
            if (_tk.Locked)
            {
                double day = x * StarDir[0] + y * StarDir[1] + z * StarDir[2];
                if (day > 0.3) { if (h < 0) h = h * 0.5; r = 0.55f * tv; g = 0.28f * tv; b = 0.14f * tv; return h; }      // scorched, its seas boiled away
                if (day < -0.2) { if (h < 0) { r = 0.8f; g = 0.88f; b = 0.96f; return 0; } r = 0.92f; g = 0.94f; b = 0.98f; return h; }  // frozen night side
            }
            if (_tk.Ground != null && !(_tk.Locked) && !(HasWater && h < 0))
            {
                // its ground's colours by height (terrain.unt), each band shading into the next across a fifth of a km, never a
                // hard line (a hard line lands on the ground's corners and shows as jagged patches)
                r = g = b = 0.5f; var gb = _tk.Ground;
                for (int q = 0; q < gb.Length; q++)
                    if (h < gb[q][0] || q == gb.Length - 1)
                    {
                        float k = gb[q][4] > 0 ? tv : 1; r = (float)gb[q][1] * k; g = (float)gb[q][2] * k; b = (float)gb[q][3] * k;
                        if (q > 0) { float w = (float)Smooth(-0.1, 0.1, h - gb[q - 1][0]); float k0 = gb[q - 1][4] > 0 ? tv : 1; r = (float)gb[q - 1][1] * k0 * (1 - w) + r * w; g = (float)gb[q - 1][2] * k0 * (1 - w) + g * w; b = (float)gb[q - 1][3] * k0 * (1 - w) + b * w; }
                        break;
                    }
                return h;
            }
            if (h < 0)
            {
                if (lat > Math.Min(0.995, IceSin + 0.04) && _tk.PolarIce) { r = 0.85f; g = 0.9f; b = 0.95f; return 0.002; }   // sea ice near the poles, floating on top
                if (_tk.Locked)
                {
                    // the twilight ring keeps its water painted on (no separate sea surface on this world)
                    double depth = Math.Min(1, -h / 4);
                    r = (float)(0.10 - 0.08 * depth); g = (float)(0.34 - 0.26 * depth); b = (float)(0.55 - 0.3 * depth);
                    return 0;
                }
                // the sea floor, under the water: sand in the shallows, then silt and mud, dark rock in the deeps
                if (h > -0.15) { r = 0.66f * tv; g = 0.6f * tv; b = 0.46f * tv; }
                else if (h > -1.5) { r = (float)(0.36 * tv); g = (float)(0.34 * tv); b = (float)(0.3 * tv); }
                else { r = 0.22f * tv; g = 0.22f * tv; b = 0.23f * tv; }
                return h;
            }
            // the land's colours, each shading into the next (never a hard line, which lands on the ground's corners as jagged
            // patches): bare ground in patches (dark gravel, brown soil, pale clay), beaches at the shore, grey rock higher up,
            // snow on the peaks, ice caps toward the poles
            double wg = Smooth(0.34, 0.46, soil), wc = Smooth(0.54, 0.66, soil);
            double sr = 0.30 + (0.46 - 0.30) * wg + (0.62 - 0.46) * wc, sg = 0.28 + (0.38 - 0.28) * wg + (0.55 - 0.38) * wc, sb = 0.26 + (0.29 - 0.26) * wg + (0.45 - 0.29) * wc;
            double wb = Smooth(0.02, 0.08, h), wr = Smooth(2.2, 2.7, h), ws = Smooth(4.0, 4.5, h);
            double cr = 0.78 + (sr - 0.78) * wb, cg = 0.72 + (sg - 0.72) * wb, cb = 0.52 + (sb - 0.52) * wb;
            cr += (0.48 - cr) * wr; cg += (0.47 - cg) * wr; cb += (0.46 - cb) * wr;
            r = (float)(cr * tv); g = (float)(cg * tv); b = (float)(cb * tv);
            r += (0.94f - r) * (float)ws; g += (0.95f - g) * (float)ws; b += (0.97f - b) * (float)ws;
            float wi = (float)Smooth(0.84 + 0.05 * tint, 0.9 + 0.05 * tint, lat);
            r += (0.93f - r) * wi; g += (0.95f - g) * wi; b += (0.99f - b) * wi;
            return h;
        }

        private void GiantColour(double x, double y, double z, int extra, out float r, out float g, out float b)
        {
            // bands of cloud, pushed and curled by the winds (layered warping), with many storms of different sizes
            // the winds and their curls from the waveform (stored CDF 9/7 octaves, mode-E past them), finer as you come closer
            double wx = 0.5 * Waveform.Octaves(x, y, z, _seed + 11u, 2.5, 1, 0.5, 2.5 * Math.Pow(2, 3 + extra)), wz = 0.5 * Waveform.Octaves(x, y, z, _seed + 12u, 2.5, 1, 0.5, 2.5 * Math.Pow(2, 3 + extra));
            double curl = 0.5 * Waveform.Octaves(x + wx * 0.6, y, z + wz * 0.6, _seed + 13u, 7, 1, 0.5, 7 * Math.Pow(2, 2 + extra));
            double yy = y + wx * 0.06 + curl * 0.05;
            double band = Math.Sin(yy * _tk.Bands + curl * 4) * 0.5 + 0.5;
            double fine = 0.5 + 0.5 * Waveform.Octaves(x + wx, y * 6, z + wz, _seed + 14u, 12, 1, 0.5, 12 * Math.Pow(2, 2 + extra));
            band = band * 0.8 + fine * 0.2;
            // its dark belts and light zones, in this kind's colours (terrain.unt)
            r = (float)(_tk.BandLow[0] + (_tk.BandHigh[0] - _tk.BandLow[0]) * band); g = (float)(_tk.BandLow[1] + (_tk.BandHigh[1] - _tk.BandLow[1]) * band); b = (float)(_tk.BandLow[2] + (_tk.BandHigh[2] - _tk.BandLow[2]) * band);
            // storms: round swirling ovals, one great one and many small ones
            var rng = new Rng(_seed ^ 0x570CUL);
            for (int k = 0; k < 14; k++)
            {
                double la = (rng.NextDouble() - 0.5) * 1.6, lo = rng.NextDouble() * Math.PI * 2;
                double size = k == 0 ? 0.16 : 0.03 + rng.NextDouble() * 0.05;
                double sx = Math.Cos(la) * Math.Cos(lo), sy = Math.Sin(la), sz = Math.Cos(la) * Math.Sin(lo);
                double dx = x - sx, dy = (y - sy) * 1.7, dz = z - sz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > size) continue;
                double t = 1 - d / size, swirl = Math.Sin(d / size * 14 + Math.Atan2(dy, dx + dz) * 2) * 0.5 + 0.5;
                float sr, sg, sb;
                if (k == 0 && _tk.GreatStorm > 0.5) { sr = 0.82f; sg = 0.38f; sb = 0.26f; }       // the great red storm
                else if (_tk.GreatStorm <= 0.5) { sr = _tk.Storm[0]; sg = _tk.Storm[1]; sb = _tk.Storm[2]; }   // this kind's storms (dark, like Neptune's)
                else { sr = 0.96f; sg = 0.93f; sb = 0.86f; }                                      // white ovals
                float m = (float)(Math.Min(1, t * 2.5) * (0.75 + 0.25 * swirl));
                r += (sr - r) * m; g += (sg - g) * m; b += (sb - b) * m;
            }
        }

        // ── volcanoes, rivers and resource fields ───────────────────────

        // the latitude to use for climate when the ground has drifted from where it formed (one per thread: heights are worked out on many cores)
        [ThreadStatic] private static double _latOverrideT;
        [ThreadStatic] private static bool _latSet;
        private double _latOverride { get { return _latSet ? _latOverrideT : -1; } set { _latOverrideT = value; _latSet = value >= 0; } }
        [ThreadStatic] private static double _land, _mtn, _tint;
        private double _shellLand = 0.1, _shellMtn = 1;
        /// <summary>The resource field at the last point asked about (index into <see cref="Fields"/>), or -1.</summary>
        [ThreadStatic] private static int _lastField;
        public int LastField { get { return _lastField; } private set { _lastField = value; } }
        /// <summary>True: every field lights up in its own colour, buried ones too (the resource view).</summary>
        public readonly bool ResourceView;
        /// <summary>Coal, oil and gas are made from buried life, so they appear only once life has lived and died on this world.</summary>
        public bool LifeHasLived = false;

        public sealed class FieldKind { public string Id, Name, Where; public float R, G, B; public bool Buried; }
        public static readonly List<FieldKind> Fields = new List<FieldKind>();
        /// <summary>What each kind of world's air is made of, from content.</summary>
        public static readonly Dictionary<string, string> Air = new Dictionary<string, string>();

        // the colour each field shows in the resource view, and whether it is buried out of sight
        private static readonly string[] FieldIds = { "ocean-water", "volcanic-sulfide", "epithermal", "porphyry", "orogenic-gold", "skarn", "pegmatite",
            "placer", "evaporite", "laterite", "banded-iron", "kimberlite", "layered-intrusion", "coal", "oil-gas", "sediment-copper", "ice-field" };
        private static readonly float[,] FieldColour = { { 0.2f, 0.5f, 1f }, { 1f, 0.92f, 0.1f }, { 1f, 0.6f, 0.1f }, { 0.2f, 0.95f, 0.6f }, { 1f, 0.75f, 0f },
            { 0.75f, 0.4f, 1f }, { 1f, 0.5f, 0.8f }, { 1f, 0.88f, 0.45f }, { 1f, 1f, 1f }, { 0.95f, 0.32f, 0.12f }, { 0.8f, 0.1f, 0.1f }, { 0.45f, 0.9f, 1f },
            { 0.65f, 0.65f, 0.25f }, { 0.55f, 0.55f, 0.58f }, { 0.95f, 0.2f, 0.95f }, { 0.2f, 0.75f, 0.25f }, { 0.75f, 0.97f, 1f } };
        private static readonly bool[] FieldBuried = { false, false, false, true, false, true, true, false, false, false, false, true, true, true, true, true, false };

        /// <summary>Reads every field's name and where it is found from content/resource-fields.unt.</summary>
        public static void LoadFields(ContentLibrary content)
        {
            if (Kinds.Count == 0) LoadKinds(content);
            GrainField.Load(content);
            Waveform.Init();                                                             // the stored waveforms, made once here
            if (Fields.Count > 0) return;
            foreach (var e in content.Entries) if ((e.Kind == "planet" || e.Kind == "giant") && e.Props.ContainsKey("air")) Air[e.Id] = e.Get("air", "");
            for (int i = 0; i < FieldIds.Length; i++)
            {
                var k = new FieldKind { Id = FieldIds[i], Name = FieldIds[i], Where = "", R = FieldColour[i, 0], G = FieldColour[i, 1], B = FieldColour[i, 2], Buried = FieldBuried[i] };
                foreach (var e in content.Entries)
                    if (e.Id == FieldIds[i] && (e.Kind == "deposit" || e.Kind == "field")) { k.Name = e.Name; k.Where = e.Get("where", e.Get("says", "")); }
                Fields.Add(k);
            }
        }

        private static int FieldIndex(string id) { return Array.IndexOf(FieldIds, id); }

        private struct Volcano { public double X, Y, Z, Size, Tall; public bool Active; }
        private Volcano[] _volcanoes;

        private void MakeVolcanoes()
        {
            int count;
            count = (int)Math.Round(_tk.VolcanoCount);                                  // (its template: volcano-count)
            var rng = new Rng(_seed ^ 0xF1AEUL);
            var list = new List<Volcano>();
            for (int tries = 0; tries < 4000 && list.Count < count; tries++)
            {
                double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u);
                double x = Math.Cos(a) * rr, y = u, z = Math.Sin(a) * rr;
                float r0, g0, b0, gl0;
                double h0 = HeightBase(x, y, z, 6, out r0, out g0, out b0, out gl0);
                // volcanoes rise where plates meet (the mountain belts) and on hot spots; on wet worlds some are islands
                bool belt = _mtn > 0.15, hotspot = rng.NextDouble() < 0.25;
                if (!belt && !hotspot) continue;
                if (h0 < -2.5 && !_tk.SeaFloorVolcanoes) continue;   // (deep under the sea only where its template says: seafloor-volcanoes)
                list.Add(new Volcano { X = x, Y = y, Z = z, Size = (_tk.ManyVolcanoes ? 0.012 : 0.007) * (0.6 + rng.NextDouble()) * 6371 / Radius,
                    Tall = _tk.ManyVolcanoes ? 3 + rng.NextDouble() * 6 : 2 + rng.NextDouble() * 4, Active = _tk.ManyVolcanoes || rng.NextDouble() < 0.4 });
            }
            _volcanoes = list.ToArray();
        }

        private double _volcano, _vent, _river; private bool _activeVent; private volatile Drainage _drain; private readonly object _drainLock = new object();
        // the small streams round where you are (src/gen/LocalDrainage.cs): made in the background for the span asked, kept until another is asked
        private volatile LocalDrainage _local; private volatile bool _localMaking;
        /// <summary>Asks for the small streams round a place (a direction from the world's middle) over a span (km): made in the
        /// background, a new one only once the view has moved a quarter of the span or its span changed threefold.</summary>
        public void AskStreams(double x, double y, double z, double spanKm)
        {
            if (!Solid || !_tk.Rivers || _localMaking) return; var l = _local; double cl = Math.Sqrt(x * x + y * y + z * z); if (cl <= 0) return;
            if (l != null)
            {
                double dot = (x * l.Cx + y * l.Cy + z * l.Cz) / cl, movedKm = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * Radius;
                if (movedKm < l.SpanKm * 0.25 && spanKm > l.SpanKm / 3 && spanKm < l.SpanKm * 3) return;
            }
            _localMaking = true; double fx = x, fy = y, fz = z, span = Math.Max(0.2, Math.Min(4000, spanKm));
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { _local = new LocalDrainage(fx, fy, fz, span, Radius, (dx, dy, dz, layers) => { float r1, g1, b1, gl1; return HeightBase(dx, dy, dz, layers, out r1, out g1, out b1, out gl1); }); System.Threading.Interlocked.Increment(ref Generation); }   // (the ground remade with its streams, swapped in whole when ready, as any change of its shape)
                catch (Exception) { }
                finally { _localMaking = false; }
            });
        }

        private double Landforms(double x, double y, double z, double h, int layers, ref float r, ref float g, ref float b, ref float glow)
        {
            if (_volcanoes == null) MakeVolcanoes();
            _volcano = 0; _vent = 0; _activeVent = false;
            foreach (var v in _volcanoes)
            {
                double d = 1 - (x * v.X + y * v.Y + z * v.Z);           // about half the angle squared: cheap and smooth
                double s2 = v.Size * v.Size * 0.5;
                if (d > s2 * 9) continue;
                double t = Math.Sqrt(d / s2);                          // 0 at the top, 1 at the foot
                if (t < 1)
                {
                    double cone = Math.Pow(1 - t, 1.6) * v.Tall;
                    double crater = t < 0.12 ? (0.12 - t) / 0.12 * v.Tall * 0.25 : 0;
                    h = Math.Max(h, h * (1 - (1 - t)) + cone - crater);
                    if (1 - t > _volcano) { _volcano = 1 - t; _vent = t < 0.12 ? 1 - t / 0.12 : 0; _activeVent = v.Active; }
                }
                else if (3 - t > 0 && (3 - t) / 2 * 0.3 > _volcano) _volcano = (3 - t) / 2 * 0.3;   // the field of ash and hot springs around it
            }
            if (_volcano > 0.05)
            {
                // dark ash and lava rock on the slopes; a glowing crater if it is active
                float m = (float)Math.Min(1, _volcano * 1.5);
                r += (0.2f - r) * m; g += (0.18f - g) * m; b += (0.17f - b) * m;
                if (_vent > 0 && _activeVent) { r = 1f; g = 0.42f; b = 0.08f; glow = 0.9f; }
            }
            // rivers and lakes: the world's rain run downhill to the sea (src/gen/Drainage.cs, made once for the world): a river where
            // enough has gathered, as wide as the root of it; a lake where a basin fills to its spill, its water level flat
            _river = 0;
            if (h > 0 && _tk.Rivers)   // (its template: rivers)
            {
                if (_drain == null) lock (_drainLock) if (_drain == null) _drain = new Drainage((dx, dy, dz) => { float r1, g1, b1, gl1; return HeightBase(dx, dy, dz, 6, out r1, out g1, out b1, out gl1); });   // (made once, by whichever thread needs it first)
                double lake, rv = _drain.RiverAt(x, y, z, h, Radius, out lake);
                if (!double.IsNaN(lake)) { h = lake; _river = 1; r = _tk.River[0]; g = _tk.River[1]; b = _tk.River[2]; }   // (lake water, at its level)
                else if (rv > 0)
                {
                    _river = rv; h -= 0.03 * rv;
                    r = _tk.River[0]; g = _tk.River[1]; b = _tk.River[2];                  // river water, or a dry bed (terrain.unt)
                }
                else if (rv < 0) _river = -1;                                             // the river banks
                var loc = _local; double st, pond;
                if (_river <= 0 && loc != null && loc.StreamAt(x, y, z, h, out st, out pond))
                {
                    // the small streams round where you are, finer than the world's map: brooks, rills, ponds
                    if (!double.IsNaN(pond)) { h = pond; _river = 1; r = _tk.River[0]; g = _tk.River[1]; b = _tk.River[2]; }
                    else if (st > 0) { _river = st; h -= 0.0005 * st; r = _tk.River[0]; g = _tk.River[1]; b = _tk.River[2]; }
                    else if (st < 0 && _river == 0) _river = -1;
                }
            }
            return h;
        }

        private bool Spot(double x, double y, double z, int id, double freq, double above)
        {
            return Fbm(x, y, z, _seed + 6000u + (uint)id * 97u, freq, 3) > above;
        }

        /// <summary>Which resource field is here, by real geology, and what it looks like where it shows at the surface.</summary>
        private int ClassifyField(double x, double y, double z, double h, int layers, ref float r, ref float g, ref float b, ref float glow)
        {
            double lat = _latOverride >= 0 ? _latOverride : Math.Abs(y), land = _land, mtn = _mtn;
            if (HasWater && h < -0.0001 && lat < 0.9) return FieldIndex("ocean-water");
            if (lat > IceSin + 0.05 * _tint || _tk.FrozenSea && h <= 0.0001) return FieldIndex("ice-field");
            if (_vent > 0.3) { r = 0.95f; g = 0.85f; b = 0.15f; if (_activeVent) { r = 1f; g = 0.42f; b = 0.08f; } return FieldIndex("volcanic-sulfide"); }   // yellow sulfur crust
            if (_volcano > 0.25) { if (Spot(x, y, z, 2, 30, 0.55)) { r = 0.85f; g = 0.55f; b = 0.2f; } return FieldIndex("epithermal"); }   // hot-spring stains
            if (_river < 0) { if (Spot(x, y, z, 7, 200, 0.62)) { r = 0.9f; g = 0.78f; b = 0.3f; } return FieldIndex("placer"); }   // glints of gold in the banks
            if (_volcano > 0.05 && mtn > 0.1) return FieldIndex("porphyry");
            if (mtn > 0.3)
            {
                if (Spot(x, y, z, 4, 40, 0.6)) { if (Spot(x, y, z, 40, 900, 0.7)) { r = 0.95f; g = 0.8f; b = 0.25f; } return FieldIndex("orogenic-gold"); }   // quartz veins with gold
                if (Spot(x, y, z, 5, 35, 0.64)) { r = 0.3f; g = 0.55f; b = 0.45f; return FieldIndex("skarn"); }        // green copper stains
                if (Spot(x, y, z, 6, 50, 0.66)) return FieldIndex("pegmatite");
            }
            bool dry = _tk.Dry;
            if (dry && h < -0.8) return FieldIndex("evaporite");                                      // the salt flats (already white)
            if (!dry && h > 0 && h < 0.06 && lat < 0.45 && Spot(x, y, z, 8, 25, 0.62)) { r = 0.93f; g = 0.92f; b = 0.88f; return FieldIndex("evaporite"); }   // salt pans by the sea
            if (_tk.TropicalSoil)
                if (lat < 0.32 && h > 0.04 && h < 1.2 && mtn < 0.15) { r = 0.62f; g = 0.26f; b = 0.14f; return FieldIndex("laterite"); }   // red tropical soil
            if (land > 0.25 && mtn < 0.08)
            {
                if (Spot(x, y, z, 10, 18, 0.6))
                {
                    double band = Math.Sin(Fbm(x, y, z, _seed + 77u, 300, 3) * 60) * 0.5 + 0.5;            // red and grey stripes
                    if (band > 0.5) { r = 0.55f; g = 0.2f; b = 0.15f; } else { r = 0.4f; g = 0.38f; b = 0.38f; }
                    return FieldIndex("banded-iron");
                }
                if (Spot(x, y, z, 11, 60, 0.74)) { r = 0.3f; g = 0.36f; b = 0.38f; return FieldIndex("kimberlite"); }   // blue-grey pipe rock
                if (Spot(x, y, z, 12, 20, 0.68)) return FieldIndex("layered-intrusion");
            }
            if (h > -0.4 && h < 0.7 && mtn < 0.12)
            {
                // coal, oil and gas form only from buried dead life: none until life has lived and died here
                if (LifeHasLived && Spot(x, y, z, 14, 14, 0.6)) { if (h > 0 && Spot(x, y, z, 41, 400, 0.72)) { r = 0.06f; g = 0.05f; b = 0.05f; } return FieldIndex("oil-gas"); }   // black seeps
                if (LifeHasLived && h > 0 && Spot(x, y, z, 13, 16, 0.6)) return FieldIndex("coal");
                if (Spot(x, y, z, 15, 22, 0.66)) return FieldIndex("sediment-copper");
            }
            return -1;
        }

        private void ViewColour(int field, double h, ref float r, ref float g, ref float b, ref float glow)
        {
            if (field < 0) { float grey = (r + g + b) / 3 * 0.35f; r = g = b = grey; glow = 0; return; }   // no field: dim grey
            r = FieldColour[field, 0]; g = FieldColour[field, 1]; b = FieldColour[field, 2]; glow = 0.55f;  // lit even at night
        }

        /// <summary>Where on the planet a field of this kind can be found (unit direction), searching from a seed.</summary>
        public bool FindField(int field, int start, out double x, out double y, out double z)
        {
            var rng = new Rng(_seed ^ (ulong)(field * 7919 + start * 104729));
            for (int i = 0; i < 20000; i++)
            {
                double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, rr = Math.Sqrt(1 - u * u);
                x = Math.Cos(a) * rr; y = u; z = Math.Sin(a) * rr;
                if (x * StarDir[0] + y * StarDir[1] + z * StarDir[2] < 0.35 && i < 15000) continue;   // a sunlit example
                float r0, g0, b0, gl0;
                Height(x, y, z, 10, out r0, out g0, out b0, out gl0);
                if (LastField == field) return true;
            }
            x = y = 0; z = 1; return false;
        }

        /// <summary>The field under a unit direction (with full detail).</summary>
        public int FieldAt(double x, double y, double z) { float r, g, b, gl; Height(x, y, z, 20, out r, out g, out b, out gl); return LastField; }

        /// <summary>Hexagon grid on flat coordinates: which cell, and how close to its edge (0 = on the edge) and centre.</summary>
        private static void Hex(double u, double v, out long ci, out long cj, out double edge, out double centre)
        {
            // cell centres at i*(1,0) + j*(0.5, 0.866)
            double fj = v / 0.8660254, fi = u - fj * 0.5;
            long bi = (long)Math.Floor(fi), bj = (long)Math.Floor(fj);
            double d1 = 1e9, d2 = 1e9; ci = bi; cj = bj;
            for (long a = bi - 1; a <= bi + 2; a++)
                for (long b = bj - 1; b <= bj + 2; b++)
                {
                    double cx = a + b * 0.5, cy = b * 0.8660254, d = Math.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
                    if (d < d1) { d2 = d1; d1 = d; ci = a; cj = b; } else if (d < d2) d2 = d;
                }
            edge = d2 - d1; centre = d1;
        }

        private static double Smooth(double a, double b, double v) { double t = Math.Max(0, Math.Min(1, (v - a) / (b - a))); return t * t * (3 - 2 * t); }

        /// <summary>Ground height in km (below 0 is the sea floor) under a unit direction, with every detail: keeps the camera on top of the ground.</summary>
        /// <summary>The colour of the ground under a unit direction (what you see when you push into it).</summary>
        public void GroundColourAt(double x, double y, double z, out float r, out float g, out float b)
        {
            float gl; Height(x, y, z, 30, out r, out g, out b, out gl);
        }

        /// <summary>The ground's own level, without its grains (km above the sea), for the land's slope under the grains.</summary>
        public double BaseAt(double x, double y, double z) { _noGrainsNow = true; try { return GroundAt(x, y, z); } finally { _noGrainsNow = false; } }
        [ThreadStatic] private static bool _noGrainsNow;
        [ThreadStatic] private static double _cellAtLeast;
        /// <summary>The ground's height (km above the sea) with its grains worked out no finer than <paramref name="cellKm"/>:
        /// for asking how high the ground is under you (a thirtieth of your height is all that can matter), fast.</summary>
        // ── the ocean's currents drawn on its water, as they show from above: a map of its basins made once (each latitude a ring of 720
        // points round the world, one pass along it finding every stretch of sea between its coasts), each sea point's share of the way
        // across its basin from its western shore; a gyre's water heaped against its western shore runs fast and warm poleward there
        // (westward intensification: the Gulf Stream, the Kuroshio), a deep warm blue; along a subtropical eastern shore the deep cold
        // water rises (Ekman's transport), and at the equator where the trade winds part the water, greener with the life it feeds ──
        private float[] _basin; private readonly object _basinLock = new object(); private const int BasinW = 720, BasinH = 360;
        private void MakeBasins()
        {
            var m = new float[BasinW * BasinH]; var land = new bool[BasinW]; float rr, gg, bb, ll;
            for (int j = 0; j < BasinH; j++)
            {
                double lat = ((j + 0.5) / BasinH - 0.5) * Math.PI, cy = Math.Sin(lat), cr = Math.Cos(lat); bool any = false;
                for (int i = 0; i < BasinW; i++) { double lo = (i + 0.5) / BasinW * 2 * Math.PI - Math.PI; land[i] = Height(cr * Math.Cos(lo), cy, cr * Math.Sin(lo), 8, out rr, out gg, out bb, out ll) > 0; any |= land[i]; }
                if (!any) { for (int i = 0; i < BasinW; i++) m[j * BasinW + i] = -1; continue; }
                // two passes round the ring (twice, so it wraps): the steps back to the last coast, and on to the next
                var wd = new int[BasinW]; var ed = new int[BasinW]; int run = 0;
                for (int q = 0; q < 2 * BasinW; q++) { int i = q % BasinW; run = land[i] ? 0 : run + 1; wd[i] = run; }
                run = 0; for (int q = 2 * BasinW - 1; q >= 0; q--) { int i = q % BasinW; run = land[i] ? 0 : run + 1; ed[i] = run; }
                for (int i = 0; i < BasinW; i++) m[j * BasinW + i] = land[i] ? -1 : wd[i] / (float)(wd[i] + ed[i]);
            }
            _basin = m;
        }
        private void SeaTint(double x, double y, double z, ref float r, ref float g, ref float b)
        {
            if (_basin == null) lock (_basinLock) if (_basin == null) MakeBasins();
            double l = Math.Sqrt(x * x + y * y + z * z), lat = Math.Asin(Math.Max(-1, Math.Min(1, y / l))), lo = Math.Atan2(z, x);
            int j = Math.Max(0, Math.Min(BasinH - 1, (int)((lat / Math.PI + 0.5) * BasinH))), i = Math.Max(0, Math.Min(BasinW - 1, (int)((lo + Math.PI) / (2 * Math.PI) * BasinW)));
            float share = _basin[j * BasinW + i]; if (share < 0) return; double la = Math.Abs(lat) * 180 / Math.PI;
            // the western boundary current: within the basin's westmost tenth, 15 to 45 degrees, strongest at its middle
            double wbc = la > 15 && la < 45 ? Math.Max(0, 1 - share / 0.1) * Math.Sin((la - 15) / 30 * Math.PI) : 0;
            // the upwellings: the eastmost tenth from 15 to 40 degrees, and within 5 degrees of the equator
            double up = Math.Max(la > 15 && la < 40 ? Math.Max(0, (share - 0.9) / 0.1) : 0, la < 5 ? 1 - la / 5 : 0);
            r = (float)(r * (1 - 0.3 * wbc)); g = (float)(g * (1 - 0.15 * wbc) + 0.07 * up); b = (float)(b * (1 + 0.25 * wbc) - 0.04 * up);
        }

        public double GroundAt(double x, double y, double z, double cellKm) { _cellAtLeast = cellKm; try { return GroundAt(x, y, z); } finally { _cellAtLeast = 0; } }
        public double GroundAt(double x, double y, double z)
        {
            float r, g, b, gl;
            return Height(x, y, z, 46, out r, out g, out b, out gl);
        }

        /// <summary>A good place to start the fly-down: on the lit side, on land with hills or mountains in view.</summary>
        public void StartPoint(out double lat, out double lon)
        {
            if (Shell && Habitable)
            {
                // inside a shell: dry land on a habitat plate, near the middle band
                var rs = new Rng(_seed ^ 0xD150UL);
                lat = 0.01; lon = 0.4;
                for (int i = 0; i < 4000; i++)
                {
                    double la = (rs.NextDouble() - 0.5) * 0.06, lo = rs.NextDouble() * Math.PI * 2;
                    if (GroundAt(Math.Cos(la) * Math.Cos(lo), Math.Sin(la), Math.Cos(la) * Math.Sin(lo)) > 0.1) { lat = la; lon = lo; return; }
                }
                return;
            }
            lat = Math.Asin(StarDir[1]) * 0.7; lon = Math.Atan2(StarDir[2], StarDir[0]);
            if (!Solid) return;
            var rng = new Rng(_seed ^ 0x5EEDUL);
            double best = -1;
            for (int i = 0; i < 4000; i++)
            {
                double la = Math.Asin(rng.NextDouble() * 1.6 - 0.8), lo = rng.NextDouble() * Math.PI * 2;
                double x = Math.Cos(la) * Math.Cos(lo), y = Math.Sin(la), z = Math.Cos(la) * Math.Sin(lo);
                double day = x * StarDir[0] + y * StarDir[1] + z * StarDir[2];
                if (day < 0.45 || day > 0.8) continue;           // a lower star: hills throw shading
                float r, g, b, gl;
                double h = Height(x, y, z, 8, out r, out g, out b, out gl);
                double score = (h > 0.15 && h < 1.8 ? 1 : 0) - Math.Abs(h - 0.8) * 0.2 + (_tk.Molten && gl > 0 ? -1 : 0);
                if (score > best) { best = score; lat = la; lon = lo; }
            }
        }

        /// <summary>How cloudy the sky is (0..1) straight above a unit direction (in the clouds' own turning frame).</summary>
        public double CloudCover(double x, double y, double z)
        {
            if (!HasClouds) return 0;
            if (!Solid)
            {
                // giants: high clouds stretched out along the bands by the winds
                double gc = Fbm(x * 1.2, y * 9, z * 1.2, _seed + 510u, 2.0, 7);
                return Smooth(0.55, 0.72, gc) * 0.55;
            }
            double c = Fbm(x, y, z, _seed + 500u, 2.4, 7) + 0.1 * Math.Cos(y * 7);
            if (Shell) c -= 0.1;                                     // shells: scattered clouds, so the land shows
            c += _tk.CloudShift;                                     // more cloud or less, by its kind (terrain.unt)
            if (_tk.Locked) { double day = x * StarDir[0] + y * StarDir[1] + z * StarDir[2]; c += 0.25 * Smooth(-0.3, 0.3, day) - 0.18; }
            return Smooth(0.54, 0.7, c);
        }

        /// <summary>How far the clouds have drifted round the planet by <paramref name="time"/> seconds (radians).</summary>
        public double CloudTurn(double time) { return time * 0.0035; }

        // ── patches ──────────────────────────────────────────────────────

        private static readonly double[,] FaceN = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        private static readonly double[,] FaceU = { { 0, 0, -1 }, { 0, 0, 1 }, { 1, 0, 0 }, { 1, 0, 0 }, { 1, 0, 0 }, { -1, 0, 0 } };
        private static readonly double[,] FaceV = { { 0, 1, 0 }, { 0, 1, 0 }, { 0, 0, -1 }, { 0, 0, 1 }, { 0, 1, 0 }, { 0, 1, 0 } };

        private static void Dir(int f, double s, double t, out double x, out double y, out double z)
        {
            x = FaceN[f, 0] + FaceU[f, 0] * s + FaceV[f, 0] * t;
            y = FaceN[f, 1] + FaceU[f, 1] * s + FaceV[f, 1] * t;
            z = FaceN[f, 2] + FaceU[f, 2] * s + FaceV[f, 2] * t;
            double l = Math.Sqrt(x * x + y * y + z * z); x /= l; y /= l; z /= l;
        }

        private static PKey Key(int layer, int f, int level, long i, long j) { return new PKey { A = i | ((long)level << 45) | ((long)f << 51) | ((long)layer << 55), B = j }; }

        private double LayerRadius(int layer)
        {
            if (layer == Hull) return Radius + HullKm;
            if (layer == Clouds) return Shell ? Radius - CloudKm : Radius + CloudKm;
            return Radius;
        }

        /// <summary>How tall the hull's machinery stands (km above the bare hull) at a unit direction: the same shapes the hull is built from.</summary>
        public double HullHeightAt(double x, double y, double z)
        {
            double ax2 = Math.Abs(x), ay2 = Math.Abs(y), az2 = Math.Abs(z), pu, pv;
            if (ax2 >= ay2 && ax2 >= az2) { pu = y / ax2; pv = z / ax2; } else if (ay2 >= az2) { pu = x / ay2; pv = z / ay2; } else { pu = x / az2; pv = y / az2; }
            long face = ax2 >= ay2 && ax2 >= az2 ? (x > 0 ? 1 : 2) : ay2 >= az2 ? (y > 0 ? 3 : 4) : (z > 0 ? 5 : 6);
            double h = 0, bn = 5;
            for (int sc = 0; sc < 6; sc++, bn *= 6)
            {
                double cu = pu * bn, cv = pv * bn;
                long iu = (long)Math.Floor(cu), iv = (long)Math.Floor(cv);
                double fu = cu - iu, fv = cv - iv, edge = Math.Min(Math.Min(fu, 1 - fu), Math.Min(fv, 1 - fv));
                double width = Radius * 2 / bn;
                float hsh = (float)Hash(iu, iv, face * 16 + sc, _seed + 8400u);
                if (sc == 0 && edge < 0.015) { h -= width * 0.02; break; }
                if (edge < 0.06 || hsh < 0.35) continue;
                double tall = hsh * hsh * width * 0.035;
                if (hsh > 0.93) tall *= 3;
                if (hsh > 0.8 && hsh < 0.9) { double pf = Math.Abs(Math.Sin(fu * Math.PI * 6)); tall = pf > 0.6 ? width * 0.012 : 0; }
                h += tall;
            }
            return h;
        }

        /// <summary>Whether this part of a shell has been built (rings, part-built shells and mirror caps have gaps).</summary>
        private bool Built(double x, double y, double z, int level, double angle)
        {
            if (Mask == 0 || level < 1) return true;
            if (Mask == 1) return Math.Abs(y) < 0.045 + angle;                                    // a band around the middle
            if (Mask == 3) return y > 0.25 - angle;                                               // a curved mirror on one side
            if (level < 3) return true;
            return level > 3 || Fbm(x, y, z, _seed + 8100u, 2.2, 3) > 0.47;                       // part-built: whole panels missing
        }

        private bool _drawingHull, _camInside;
        [ThreadStatic] private static bool _makingHull;
        [ThreadStatic] private static int _makeLevel;
        /// <summary>Sample views only: cut away the part of a shell facing this direction, so the star inside shows.</summary>
        public double[] Cutaway;

        private Patch Make(int layer, int f, int level, long i, long j)
        {
            _makingHull = layer == Hull; _makeLevel = level;
            double n = Math.Pow(2, level), size = 2.0 / n, s0 = -1 + i * size, t0 = -1 + j * size;
            double cx, cy, cz; Dir(f, s0 + size / 2, t0 + size / 2, out cx, out cy, out cz);
            double baseR = LayerRadius(layer);
            var p = new Patch { Level = level, Edge = size * baseR * 0.9 };
            int layers = Math.Min(46, 6 + level);
            // the piece's centre sits on the real ground, not at sea level, so distances to the camera are true
            double hc = 0;
            if (layer == Ground) { float r0, g0, b0, gl0; hc = Height(cx, cy, cz, layers, out r0, out g0, out b0, out gl0); }
            p.Cx = cx * (baseR + hc); p.Cy = cy * (baseR + hc); p.Cz = cz * (baseR + hc);
            for (int b = 0; b <= Grid; b++)
                for (int a = 0; a <= Grid; a++)
                {
                    double x, y, z; Dir(f, s0 + size * a / Grid, t0 + size * b / Grid, out x, out y, out z);
                    float r, g, bl, gl, al = 1;
                    double h;
                    if (layer == Hull)
                    {
                        // hull plates: a grid of panels with seams, some lit by docks and windows
                        h = 0; gl = HullGlow; al = 1;
                        // industrial megastructure: real built shapes, not a painted skin. Square sectors split by deep
                        // trenches, filled with blocks of machinery at many sizes (each one taller the bigger it is), long
                        // pipe runs and radiator ridges; steel, dark iron and rust, with only small warm work lights
                        double ax2 = Math.Abs(x), ay2 = Math.Abs(y), az2 = Math.Abs(z), pu, pv;
                        if (ax2 >= ay2 && ax2 >= az2) { pu = y / ax2; pv = z / ax2; } else if (ay2 >= az2) { pu = x / ay2; pv = z / ay2; } else { pu = x / az2; pv = y / az2; }
                        long face = ax2 >= ay2 && ax2 >= az2 ? (x > 0 ? 1 : 2) : ay2 >= az2 ? (y > 0 ? 3 : 4) : (z > 0 ? 5 : 6);
                        h = 0; gl = 0.12f;                                       // faint light from the rest of the galaxy, so the dark side still shows its shapes
                        float tone = 1f; int metal = 0; bool trench = false;
                        int scales = Math.Min(6, 1 + _makeLevel / 3);              // finer machinery appears as you come closer
                        double bn = 5;
                        for (int sc = 0; sc < scales; sc++, bn *= 6)
                        {
                            double cu = pu * bn, cv = pv * bn;
                            long iu = (long)Math.Floor(cu), iv = (long)Math.Floor(cv);
                            double fu = cu - iu, fv = cv - iv, edge = Math.Min(Math.Min(fu, 1 - fu), Math.Min(fv, 1 - fv));
                            double width = Radius * 2 / bn;                          // km across one block at this size
                            float hsh = (float)Hash(iu, iv, face * 16 + sc, _seed + 8400u);
                            if (sc == 0 && edge < 0.015) { h -= width * 0.02; trench = true; break; }       // the deep trenches between sectors
                            if (edge < 0.06) continue;                                                    // a street: no block here at this size
                            if (hsh < 0.35) continue;                                                     // open deck
                            double tall = hsh * hsh * width * 0.035;
                            if (hsh > 0.93) tall *= 3;                                                    // a tower
                            bool pipe = hsh > 0.8 && hsh < 0.9;                                           // pipe runs: long ridges along one side
                            if (pipe) { double pf = Math.Abs(Math.Sin(fu * Math.PI * 6)); tall = pf > 0.6 ? width * 0.012 : 0; }
                            h += tall;
                            if (tall > 0) { metal = (int)(hsh * 997) % 4; tone = 0.8f + 0.4f * (float)Hash(iu, iv, sc, _seed + 8500u); }
                        }
                        if (metal == 0) { r = 0.36f; g = 0.37f; bl = 0.39f; }        // steel
                        else if (metal == 1) { r = 0.2f; g = 0.2f; bl = 0.21f; }     // dark iron
                        else if (metal == 2) { r = 0.42f; g = 0.27f; bl = 0.18f; }   // rust
                        else { r = 0.5f; g = 0.49f; bl = 0.46f; }                    // weathered plate
                        r *= tone; g *= tone; bl *= tone;
                        if (trench) { r = 0.08f; g = 0.08f; bl = 0.09f; }
                        // small warm work lights, only close enough to see them
                        if (_makeLevel >= 12 && Hash((long)Math.Floor(pu * 40000), (long)Math.Floor(pv * 40000), face, _seed + 8600u) > 0.992) { r = 1f; g = 0.78f; bl = 0.45f; gl = 0.8f; }
                    }
                    else if (layer == Ground) h = Height(x, y, z, layers, out r, out g, out bl, out gl);
                    else if (layer == Water)
                    {
                        h = 0; r = 0.04f; g = 0.18f; bl = 0.34f; gl = 0; al = 0.72f;
                        if (HasWater) SeaTint(x, y, z, ref r, ref g, ref bl);
                        // water only where the ground is really below the sea; over land it is hidden under the ground
                        float r0, g0, b0, gl0;
                        double ground = Height(x, y, z, layers, out r0, out g0, out b0, out gl0);
                        if (ground > 0.0) { al = 0; h = -Math.Min(50, ground + 1); }
                    }
                    else
                    {
                        h = 0; gl = 0; al = (float)(CloudCover(x, y, z) * 0.92);
                        r = _tk.Cloud[0]; g = _tk.Cloud[1]; bl = _tk.Cloud[2]; al *= (float)_tk.CloudThick;   // the clouds of this kind of world (terrain.unt)
                    }
                    double rad = Shell && layer != Hull ? baseR - h : baseR + h;   // a shell's land rises inward, toward the star
                    int k = b * (Grid + 1) + a;
                    _hx[k] = x * rad - p.Cx; _hy[k] = y * rad - p.Cy; _hz[k] = z * rad - p.Cz;
                    _cr[k] = r; _cg[k] = g; _cb[k] = bl; _cgl[k] = gl; _hh[k] = h; _ca[k] = al;
                }
            if (Solid && layer == Ground && !Shell)
            {
                double spacing = p.Edge / Grid;
                for (int b = 0; b <= Grid; b++)
                    for (int a = 0; a <= Grid; a++)
                    {
                        int k = b * (Grid + 1) + a;
                        if (_hh[k] <= 0.04 || _cgl[k] > 0) continue;
                        int ka = a < Grid ? k + 1 : k - 1, kb = b < Grid ? k + Grid + 1 : k - Grid - 1;
                        double slope = Math.Max(Math.Abs(_hh[ka] - _hh[k]), Math.Abs(_hh[kb] - _hh[k])) / spacing;
                        float rock = (float)Math.Max(0, Math.Min(1, (slope - 0.7) * 2.0));
                        if (rock <= 0) continue;
                        float rr = _tk.Rock[0], rg = _tk.Rock[1], rb = _tk.Rock[2];                   // steep bare rock of this kind (terrain.unt)
                        _cr[k] += (rr - _cr[k]) * rock; _cg[k] += (rg - _cg[k]) * rock; _cb[k] += (rb - _cb[k]) * rock;
                    }
            }
            int v = 0;
            for (int b = 0; b < Grid; b++)
                for (int a = 0; a < Grid; a++)
                {
                    int k00 = b * (Grid + 1) + a, k10 = k00 + 1, k01 = k00 + Grid + 1, k11 = k01 + 1;
                    Tri(ref v, k00, k10, k11); Tri(ref v, k00, k11, k01);
                }
            // skirts: short walls hanging down from the patch edges, so no cracks show where detail levels meet
            double drop = p.Edge / Grid * 4;                                       // (deeper skirts stood out as big flat walls at grain scale)
            for (int e = 0; e < (layer == Ground || layer == Hull ? 4 : 0); e++)
                for (int q = 0; q < Grid; q++)
                {
                    int ka, kb;
                    if (e == 0) { ka = q; kb = q + 1; }
                    else if (e == 1) { ka = Grid * (Grid + 1) + q; kb = ka + 1; }
                    else if (e == 2) { ka = q * (Grid + 1); kb = ka + Grid + 1; }
                    else { ka = q * (Grid + 1) + Grid; kb = ka + Grid + 1; }
                    Skirt(ref v, ka, kb, drop, p);
                }
            p.Count = v;
            p.Verts = new StarVertex[v]; Array.Copy(_scratch, p.Verts, v);
            return p;
        }

        // a built piece goes to the graphics card (main thread only)
        // pieces' buffers on the card are kept and used again (making a new one is the slow part of putting a piece on the card);
        // every frame waits for the card to finish, so a buffer let go before this frame's drawing is free to be written again
        private const int PoolVerts = Grid * Grid * 6 + 4 * Grid * 6;
        private readonly Stack<UploadBuffer> _pool = new Stack<UploadBuffer>();
        private void Release(Patch p)
        {
            if (p == null || p.Buffer == null) return;
            if (p.Buffer.Bytes == (long)PoolVerts * Marshal.SizeOf(typeof(StarVertex)) && _pool.Count < 4000) _pool.Push(p.Buffer); else p.Buffer.Dispose();
            p.Buffer = null;
        }

        private void Upload(Patch p)
        {
            int stride = Marshal.SizeOf(typeof(StarVertex));
            if (p.Count <= PoolVerts) p.Buffer = _pool.Count > 0 ? _pool.Pop() : new UploadBuffer(_device, (long)PoolVerts * stride);
            else p.Buffer = new UploadBuffer(_device, (long)Math.Max(1, p.Count) * stride);
            p.Buffer.Write(p.Verts, p.Count); p.Verts = null;
        }

        private Patch MakeNow(int layer, int f, int level, long i, long j) { var p = Make(layer, f, level, i, j); Upload(p); return p; }

        // ── background building ──
        // Missing pieces are asked for while the planet is walked, nearest first; background workers (one per core, from the
        // job system) build them; each frame takes in whatever is finished. The frame never waits for a piece.
        private sealed class Ask { public PKey Key; public int Layer, F, Level; public long I, J; public bool Pending; public int Gen; public double Need; }   // Need: its distance from you in its own sizes (smaller: wanted sooner)
        private readonly object _askLock = new object();
        private readonly List<Ask> _asks = new List<Ask>();                 // waiting, nearest first (rebuilt every frame)
        private readonly HashSet<PKey> _inFlight = new HashSet<PKey>();     // being built now
        private readonly List<KeyValuePair<Ask, Patch>> _done = new List<KeyValuePair<Ask, Patch>>();
        private int _workersBusy;
        private readonly List<Ask> _askedThisFrame = new List<Ask>(); private readonly HashSet<PKey> _askedKeys = new HashSet<PKey>();

        private double _askCamX, _askCamY, _askCamZ;                            // the camera, for how much each piece is needed
        private void AskFor(PKey key, int layer, int f, int level, long i, long j, bool pending)
        {
            lock (_askLock) _wantedAt[key] = _frame;                               // still wanted: build and keep it
            if (!_askedKeys.Add(key)) return;
            double size = 2.0 / Math.Pow(2, level), x, y, z; Dir(f, -1 + (i + 0.5) * size, -1 + (j + 0.5) * size, out x, out y, out z);
            double rr = LayerRadius(layer), ex = x * rr - _askCamX, ey = y * rr - _askCamY, ez = z * rr - _askCamZ;
            _askedThisFrame.Add(new Ask { Key = key, Layer = layer, F = f, Level = level, I = i, J = j, Pending = pending, Gen = Generation, Need = Math.Sqrt(ex * ex + ey * ey + ez * ez) / (size * rr) });
        }

        // after the walk: the new wish list replaces the old (so pieces no longer needed are not built), and workers start
        private void StartBuilding()
        {
            lock (_askLock)
            {
                _asks.Clear();
                foreach (var a in _askedThisFrame) if (!_inFlight.Contains(a.Key)) _asks.Add(a);
                _asks.Sort((p0, p1) => p0.Need.CompareTo(p1.Need));                // the pieces you need most (close for their size) first
            }
            _askedThisFrame.Clear(); _askedKeys.Clear();
            int want = Math.Max(1, (int)Math.Round((Jobs.Workers - 1) * Math.Min(1, 0.25 + 0.75 * Power)));   // builders: fewer on light settings
            while (_workersBusy < want)
            {
                System.Threading.Interlocked.Increment(ref _workersBusy);
                Jobs.Run(BuildLoop);
            }
        }

        private void BuildLoop()
        {
            try
            {
                while (true)
                {
                    Ask a;
                    lock (_askLock)
                    {
                        if (_asks.Count == 0 || _disposed) return;
                        a = _asks[0]; _asks.RemoveAt(0);
                        if (!a.Pending && !Wanted(a.Key)) { _droppedUnwanted++; continue; }   // you have moved on: not built
                        _inFlight.Add(a.Key);
                    }
                    var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var p = Make(a.Layer, a.F, a.Level, a.I, a.J);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    lock (_askLock) { _done.Add(new KeyValuePair<Ask, Patch>(a, p)); _builtCount++; _builtMs += ms; }
                }
            }
            finally { System.Threading.Interlocked.Decrement(ref _workersBusy); }
        }

        // take in finished pieces: up to a few milliseconds of uploads a frame
        private void TakeFinished()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            List<KeyValuePair<Ask, Patch>> take;
            lock (_askLock)
            {
                // once a frame: throw out every finished piece you have moved on from, then the rest most needed first
                take = new List<KeyValuePair<Ask, Patch>>(_done.Count);
                foreach (var d0 in _done)
                {
                    if (d0.Key.Pending || Wanted(d0.Key.Key)) take.Add(d0);
                    else { _inFlight.Remove(d0.Key.Key); _droppedUnwanted++; }
                }
                _done.Clear();
            }
            take.Sort((p0, p1) => p0.Key.Need.CompareTo(p1.Key.Need));
            int ti = 0;
            for (; ti < take.Count && clock.Elapsed.TotalMilliseconds < Math.Max(2, Math.Min(30, 12 * Power)); ti++)   // uploads a frame: more on stronger settings
            {
                var d = take[ti];
                lock (_askLock) _inFlight.Remove(d.Key.Key);
                var a = d.Key; var np = d.Value;
                if (a.Gen != Generation) { _droppedOld++; continue; }             // built for a shape the planet no longer has
                Upload(np); np.LastUsed = _frame; np.Gen = a.Gen;
                if (a.Pending) _pending[a.Key] = np;
                else { Patch old; if (_cache.TryGetValue(a.Key, out old)) Release(old); _cache[a.Key] = np; }
            }
            _uploadsLast = ti;
            if (ti < take.Count) lock (_askLock) for (; ti < take.Count; ti++) _done.Add(take[ti]);   // the rest wait for the next frame
        }
        private volatile bool _disposed;
        private int _builtCount; private double _builtMs; private int _droppedOld, _droppedUnwanted;
        private readonly Dictionary<PKey, int> _wantedAt = new Dictionary<PKey, int>();   // the last frame each piece was asked for
        private bool Wanted(PKey k) { int fr; return _wantedAt.TryGetValue(k, out fr) && _frame - fr <= 3; }   // (under _askLock)
        /// <summary>For the log: pieces wished for, being built, built so far, their mean build time, and finished pieces thrown away as out of date.</summary>
        public string BuildStats { get { lock (_askLock) return "asked " + _asks.Count + " building " + _inFlight.Count + " built " + _builtCount + " at " + (_builtCount > 0 ? (_builtMs / _builtCount).ToString("0.0") : "0") + " ms each, old " + _droppedOld + ", unwanted " + _droppedUnwanted + ", cached " + _cache.Count + ", workers " + _workersBusy; } }

        private void Tri(ref int v, int a, int b, int c)
        {
            float r = (_cr[a] + _cr[b] + _cr[c]) / 3, g = (_cg[a] + _cg[b] + _cg[c]) / 3, bl = (_cb[a] + _cb[b] + _cb[c]) / 3, gl = (_cgl[a] + _cgl[b] + _cgl[c]) / 3;
            Put(ref v, a, r, g, bl, gl); Put(ref v, b, r, g, bl, gl); Put(ref v, c, r, g, bl, gl);
        }

        private void Put(ref int v, int k, float r, float g, float b, float gl)
        {
            _scratch[v++] = new StarVertex { X = (float)_hx[k], Y = (float)_hy[k], Z = (float)_hz[k], Size = gl, R = r, G = g, B = b, A = _ca[k] };
        }

        private void Skirt(ref int v, int a, int b, double drop, Patch p)
        {
            // straight down toward the planet's centre
            double ax = _hx[a] + p.Cx, ay = _hy[a] + p.Cy, az = _hz[a] + p.Cz, al = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (Shell) drop *= 0.15;                              // shells: only a tiny lip, or they show as boxes from inside
            if (Shell && !_makingHull) drop = -drop;             // the inner land's "down" is outward, away from the star
            double bx = _hx[b] + p.Cx, by = _hy[b] + p.Cy, bz = _hz[b] + p.Cz, bl = Math.Sqrt(bx * bx + by * by + bz * bz);
            var A = new StarVertex { X = (float)_hx[a], Y = (float)_hy[a], Z = (float)_hz[a], Size = _cgl[a], R = _cr[a], G = _cg[a], B = _cb[a], A = 1 };
            var B = new StarVertex { X = (float)_hx[b], Y = (float)_hy[b], Z = (float)_hz[b], Size = _cgl[b], R = _cr[a], G = _cg[a], B = _cb[a], A = 1 };
            var A2 = A; A2.X = (float)(_hx[a] - ax / al * drop); A2.Y = (float)(_hy[a] - ay / al * drop); A2.Z = (float)(_hz[a] - az / al * drop);
            var B2 = B; B2.X = (float)(_hx[b] - bx / bl * drop); B2.Y = (float)(_hy[b] - by / bl * drop); B2.Z = (float)(_hz[b] - bz / bl * drop);
            // the skirt hangs down under the edge to hide cracks between pieces of different detail; it faces sideways, so the
            // light would leave it black: it glows a little instead, to read as the ground beside it
            var As = A; As.Size = Math.Max(A.Size, 0.6f); var Bs = B; Bs.Size = Math.Max(B.Size, 0.6f);
            A2.Size = As.Size; B2.Size = Bs.Size;
            _scratch[v++] = As; _scratch[v++] = Bs; _scratch[v++] = B2;
            _scratch[v++] = As; _scratch[v++] = B2; _scratch[v++] = A2;
        }

        private Patch Get(int layer, int f, int level, long i, long j, bool mayMake)
        {
            PKey key = Key(layer, f, level, i, j);
            Patch p;
            if (_cache.TryGetValue(key, out p))
            {
                p.LastUsed = _frame;
                // the planet has changed shape since this piece was made: build its new form out of sight, nearest first,
                // and keep drawing the old one; the whole planet swaps to the new ground at once when every piece is ready
                if (p.Gen != Generation)
                {
                    if (_pending.ContainsKey(key)) return p;
                    if (mayMake) AskFor(key, layer, f, level, i, j, true);
                    _missing++;
                }
                return p;
            }
            if (!mayMake) return null;
            if (level > 1) { AskFor(key, layer, f, level, i, j, false); return null; }   // built in the background
            p = MakeNow(layer, f, level, i, j); p.LastUsed = _frame; p.Gen = Generation;
            _cache[key] = p;
            return p;
        }

        // ── drawing ──────────────────────────────────────────────────────

        /// <summary>Draws the whole planet as seen from the camera (km, planet centre at 0): ground, sea, clouds and air.
        /// <paramref name="viewRot"/> is the camera's turn only (the camera sits at the origin), so everything is drawn
        /// relative to the camera in double precision and never shakes, from orbit down to microbe size.</summary>
        public void Draw(MeshPipes pipes, double camX, double camY, double camZ, Mat4 viewRot, Mat4 proj, double time, double splitFactor,
            float fogDist, float fr, float fg, float fb, int x, int y, int w, int h, int newPatchBudget)
        {
            if ((_frame & 63) == 0) lock (_askLock) { var gone = new List<PKey>(); foreach (var kv in _wantedAt) if (_frame - kv.Value > 60) gone.Add(kv.Key); foreach (var k in gone) _wantedAt.Remove(k); }
            _frame++; _budget = newPatchBudget; _clock.Restart(); _msLimit = newPatchBudget >= 400 ? 10 : 30;
            var swf = System.Diagnostics.Stopwatch.StartNew(); double tTake, tChoose = 0, tDraw = 0; int groundPieces = 0;
            TakeFinished(); tTake = swf.Elapsed.TotalMilliseconds;
            // last frame every piece in view had its new form ready: swap the whole planet's ground at once
            if (Generation != DrawnGeneration && _framesWaiting > 0 && _missing == 0) SwapGround();
            if (Generation != DrawnGeneration) _framesWaiting++;
            _missing = 0;
            _camInside = Shell && Math.Sqrt(camX * camX + camY * camY + camZ * camZ) < Radius;
            ReadyStep = double.MaxValue; CamPieceEdge = 0; CamPieceLevel = -1;
            float lx = (float)StarDir[0], ly = (float)StarDir[1], lz = (float)StarDir[2];
            if (Shell)
            {
                // a shell: from outside you see only the metal hull; from inside only the land (the hull is behind it).
                // Sample views cut the shell open, so they show both.
                if (!_camInside || !Habitable)
                {
                    Collect(Hull, camX, camY, camZ, splitFactor);
                    _drawingHull = true;
                    DrawList(pipes.Solid, 0, camX, camY, camZ, Mat4.Identity, viewRot, proj, lx, ly, lz, fogDist, fr, fg, fb, x, y, w, h);
                    _drawingHull = false;
                }
                if (!Habitable || !_camInside && Cutaway == null) { StartBuilding(); Trim(); return; }
            }
            // the ground's sand worked out on the card: where you are, in grains (wrapped to 32 bits as the grain field's keys are)
            _gg = false;
            {
                double cl = Math.Sqrt(camX * camX + camY * camY + camZ * camZ); var gm = Solid && !Shell && cl > 0 ? GrainMix(camY / cl) : null;
                // (within a few thousand grains of the ground itself, not of the world's reference sphere: on land a few hundred metres
                // up the sand never came, and from a millimetre down to the hand-over the ground was one blank colour)
                double overGround = gm != null && gm.Size > 0 ? cl - Radius - GroundAt(camX / cl, camY / cl, camZ / cl) : double.MaxValue;
                if (gm != null && gm.Size > 0 && SandOnCard && overGround < gm.Size * 4000)
                {
                    double gs = gm.Size, ux = camX / cl, uy = camY / cl, uz = camZ / cl;
                    long bi = (long)Math.Floor(ux * Radius / gs), bj = (long)Math.Floor(uy * Radius / gs), bk = (long)Math.Floor(uz * Radius / gs);
                    double bx = bi * gs, by = bj * gs, bz = bk * gs;
                    _gg = true; _ggG = gs; _ggI = bi; _ggJ = bj; _ggK = bk; _ggUx = ux; _ggUy = uy; _ggUz = uz; _ggMix = gm.Index; _ggSeed = GrainField.SeedWord(GrainSeed);
                    _ggH = (Math.Sqrt(bx * bx + by * by + bz * bz) - Radius) / gs;
                }
            }
            // the ground (and the sea floor)
            Collect(Ground, camX, camY, camZ, splitFactor); tChoose = swf.Elapsed.TotalMilliseconds; groundPieces = _drawList.Count;
            DrawList(pipes.Solid, 0, camX, camY, camZ, Mat4.Identity, viewRot, proj, lx, ly, lz, fogDist, fr, fg, fb, x, y, w, h); tDraw = swf.Elapsed.TotalMilliseconds;
            // the sea surface: see-through, with glints of starlight
            if (HasWater)
            {
                Collect(Water, camX, camY, camZ, splitFactor);
                DrawList(pipes.Clear, 3, camX, camY, camZ, Mat4.Identity, viewRot, proj, lx, ly, lz, fogDist, fr, fg, fb, x, y, w, h);
            }
            // the clouds: a slowly turning layer, drawn in its own turned frame
            if (HasClouds && !(ResourceView && Solid))   // the resource view hides the clouds so every field shows
            {
                double ang = CloudTurn(time), ca = Math.Cos(ang), sa = Math.Sin(ang);
                double kx = camX * ca - camZ * sa, kz = camX * sa + camZ * ca;            // the camera, turned back into the clouds' frame
                float clx = (float)(lx * ca - lz * sa), clz = (float)(lx * sa + lz * ca);
                Collect(Clouds, kx, camY, kz, splitFactor);
                DrawList(pipes.Clear, 2, kx, camY, kz, Mat4.RotationY((float)ang), viewRot, proj, clx, ly, clz, fogDist, fr, fg, fb, x, y, w, h);
            }
            if (!Shell) DrawAir(pipes.Glow, camX, camY, camZ, viewRot, proj, lx, ly, lz, x, y, w, h);
            double tRest = swf.Elapsed.TotalMilliseconds;
            StartBuilding();
            Trim();
            FrameStats = "ground " + groundPieces + " pieces: take " + tTake.ToString("0.0") + " (" + _uploadsLast + " up), choose " + (tChoose - tTake).ToString("0.0") + ", draw " + (tDraw - tChoose).ToString("0.0") + ", rest " + (tRest - tDraw).ToString("0.0") + ", build " + (swf.Elapsed.TotalMilliseconds - tRest).ToString("0.0") + " ms";
        }

        private int _camF; private double _camS, _camT;
        private bool _gg; private double _ggG, _ggUx, _ggUy, _ggUz, _ggH; private long _ggI, _ggJ, _ggK; private int _ggMix; private uint _ggSeed;

        private void Collect(int layer, double camX, double camY, double camZ, double splitFactor)
        {
            _askCamX = camX; _askCamY = camY; _askCamZ = camZ;
            // which cube face, and where on it, lies straight under the camera
            double cl = Math.Sqrt(camX * camX + camY * camY + camZ * camZ), dx = camX / cl, dy = camY / cl, dz = camZ / cl, best = -2;
            for (int f = 0; f < 6; f++)
            {
                double k = FaceN[f, 0] * dx + FaceN[f, 1] * dy + FaceN[f, 2] * dz;
                if (k > best) { best = k; _camF = f; }
            }
            double kk = best;
            _camS = (FaceU[_camF, 0] * dx + FaceU[_camF, 1] * dy + FaceU[_camF, 2] * dz) / kk;
            _camT = (FaceV[_camF, 0] * dx + FaceV[_camF, 1] * dy + FaceV[_camF, 2] * dz) / kk;
            _drawList.Clear();
            double[] fd = new double[6]; int[] fo = { 0, 1, 2, 3, 4, 5 };
            for (int f = 0; f < 6; f++) fd[f] = -(FaceN[f, 0] * camX + FaceN[f, 1] * camY + FaceN[f, 2] * camZ);
            Array.Sort(fd, fo);
            for (int q = 0; q < 6; q++) Visit(layer, fo[q], 0, 0, 0, camX, camY, camZ, splitFactor);
        }

        private void DrawList(MeshDrawer drawer, int surface, double camX, double camY, double camZ, Mat4 turn, Mat4 viewRot, Mat4 proj,
            float lx, float ly, float lz, float fogDist, float fr, float fg, float fb, int x, int y, int w, int h)
        {
            Mat4 rest = turn * viewRot * proj;
            Universe.Render.GpuLog.Pass = "a world's " + (_drawingHull ? "hull" : surface == 0 ? (_gg ? "ground with its sand" : "ground") : surface == 2 ? "clouds" : surface == 3 ? "sea" : "surface");
            foreach (var p in _drawList)
            {
                float ox = (float)(p.Cx - camX), oy = (float)(p.Cy - camY), oz = (float)(p.Cz - camZ);
                Mat4 vp = Mat4.Translation(ox, oy, oz) * rest;
                var c = Pack(vp);
                c[16] = lx; c[17] = ly; c[18] = lz;
                if (Shell && !_drawingHull)
                {
                    // inside a shell the star is at the centre: light comes from the centre toward each piece
                    double pl = Math.Sqrt(p.Cx * p.Cx + p.Cy * p.Cy + p.Cz * p.Cz);
                    c[16] = (float)(-p.Cx / pl); c[17] = (float)(-p.Cy / pl); c[18] = (float)(-p.Cz / pl);
                }
                c[20] = -ox; c[21] = -oy; c[22] = -oz;          // the camera, in the patch's own frame
                c[25] = fogDist; c[26] = surface; c[28] = fr; c[29] = fg; c[30] = fb;
                Light(c);
                if (Shell)
                {
                    // outward direction of this piece (turned into the drawing frame for the turning clouds)
                    double pl = Math.Sqrt(p.Cx * p.Cx + p.Cy * p.Cy + p.Cz * p.Cz);
                    var rd = new Vec3((float)(p.Cx / pl), (float)(p.Cy / pl), (float)(p.Cz / pl));
                    float rx = rd.X * turn.M11 + rd.Y * turn.M21 + rd.Z * turn.M31, ry = rd.X * turn.M12 + rd.Y * turn.M22 + rd.Z * turn.M32, rz = rd.X * turn.M13 + rd.Y * turn.M23 + rd.Z * turn.M33;
                    c[19] = rx; c[23] = ry; c[31] = rz;
                    c[27] = _drawingHull ? 2 : 1;
                }
                else if (surface == 2 && Solid)
                {
                    // the clouds worked out per pixel on the card: this piece's place in the world's radii (its corners are its
                    // own offsets from it), the kind's share of cloud, whether one side always faces its star, the seed, the thickness
                    c[40] = (float)(p.Cx / Radius); c[41] = (float)(p.Cy / Radius); c[42] = (float)(p.Cz / Radius); c[43] = (float)(1 / Radius);
                    c[44] = (float)_tk.CloudShift; c[45] = _tk.Locked ? 1 : 0; c[46] = Bits(_seed + 500u); c[47] = 0; c[19] = (float)_tk.CloudThick;
                }
                else if (_gg && surface == 0)
                {
                    // the sand on the card: this piece's place in grains from the cell under you, that cell (wrapped as the keys
                    // are), the seed, which way is up, how high that cell lies against the ground, and the mix
                    c[40] = (float)(p.Cx / _ggG - _ggI); c[41] = (float)(p.Cy / _ggG - _ggJ); c[42] = (float)(p.Cz / _ggG - _ggK); c[43] = (float)(1 / _ggG);
                    c[44] = Bits((uint)_ggI); c[45] = Bits((uint)_ggJ); c[46] = Bits((uint)_ggK); c[47] = Bits(_ggSeed);
                    c[19] = (float)_ggUx; c[23] = (float)_ggUy; c[31] = (float)_ggUz; c[24] = (float)_ggH; c[39] = _ggMix + 1;
                }
                drawer.Draw(p.Buffer, p.Count, c, x, y, w, h);
            }
        }

        /// <summary>How much of the star's light reaches here (1 in the open; under the sea, less with depth).</summary>
        public float Dim = 1;
        private void Light(float[] c)
        {
            if (SunColour == null || NightColour == null) return;
            c[32] = SunColour[0] * Dim; c[33] = SunColour[1] * Dim; c[34] = SunColour[2] * Dim; c[35] = 1;
            c[36] = NightColour[0]; c[37] = NightColour[1]; c[38] = NightColour[2];
        }

        private static float Bits(uint u) { return BitConverter.ToSingle(BitConverter.GetBytes(u), 0); }
        private static float[] Pack(Mat4 vp)
        {
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            return c;
        }

        // the air: one glowing shell around the planet, brightest at its edge, seen from space and from the ground alike
        private void DrawAir(MeshDrawer drawer, double camX, double camY, double camZ, Mat4 viewRot, Mat4 proj, float lx, float ly, float lz, int x, int y, int w, int h)
        {
            if (AirKm <= 0) return;                                                      // (an airless world, as most moons are: no glow at its edge)
            if (_airShell == null)
            {
                const int Lon = 96, Lat = 48;
                var v = new StarVertex[Lon * Lat * 6]; int n = 0;
                double rr = Radius + AirKm;
                for (int a = 0; a < Lat; a++)
                    for (int o = 0; o < Lon; o++)
                    {
                        double la0 = -Math.PI / 2 + Math.PI * a / Lat, la1 = -Math.PI / 2 + Math.PI * (a + 1) / Lat;
                        double lo0 = Math.PI * 2 * o / Lon, lo1 = Math.PI * 2 * (o + 1) / Lon;
                        StarVertex p00 = AirPoint(la0, lo0, rr), p10 = AirPoint(la0, lo1, rr), p01 = AirPoint(la1, lo0, rr), p11 = AirPoint(la1, lo1, rr);
                        v[n++] = p00; v[n++] = p10; v[n++] = p11; v[n++] = p00; v[n++] = p11; v[n++] = p01;
                    }
                _airCount = n;
                _airShell = new UploadBuffer(_device, (long)n * Marshal.SizeOf(typeof(StarVertex)));
                _airShell.Write(v, n);
            }
            // the shell is in the planet's frame; the camera is far smaller than the shell's size, so plain floats are enough here
            Mat4 vp = Mat4.Translation((float)-camX, (float)-camY, (float)-camZ) * viewRot * proj;
            var c = Pack(vp);
            c[16] = lx; c[17] = ly; c[18] = lz;
            c[20] = (float)camX; c[21] = (float)camY; c[22] = (float)camZ;
            c[26] = 1; Light(c);
            drawer.Draw(_airShell, _airCount, c, x, y, w, h);
        }

        private StarVertex AirPoint(double lat, double lon, double r)
        {
            return new StarVertex { X = (float)(Math.Cos(lat) * Math.Cos(lon) * r), Y = (float)(Math.Sin(lat) * r), Z = (float)(Math.Cos(lat) * Math.Sin(lon) * r), R = AirR, G = AirG, B = AirB, A = 1 };
        }

        private readonly List<Patch> _drawList = new List<Patch>();

        private void Visit(int layer, int f, int level, long i, long j, double camX, double camY, double camZ, double splitFactor)
        {
            Patch p = Get(layer, f, level, i, j, true);
            if (p == null) return;
            double dx = p.Cx - camX, dy = p.Cy - camY, dz = p.Cz - camZ, dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            // past the horizon: hidden by the planet itself
            double camDist = Math.Sqrt(camX * camX + camY * camY + camZ * camZ), r = LayerRadius(layer);
            // the piece the camera stands over: never thrown out of the walk for lying outside the view (looking sideways it was,
            // so the ground under your feet was neither split finer nor counted as ready: "ready: none" at four millimetres up,
            // the hand-over to the grains mistimed, and the pieces round you left coarse)
            bool underCam = (layer == Ground && (!Shell || _camInside) || layer == Hull && (!_camInside || !Habitable)) && f == _camF;
            if (underCam) { double szc = 2.0 / Math.Pow(2, level), s0c = -1 + i * szc, t0c = -1 + j * szc; underCam = _camS >= s0c && _camS <= s0c + szc && _camT >= t0c && _camT <= t0c + szc; }
            // gaps in a ring, a part-built shell or a mirror cap
            if (Shell)
            {
                double pl = Math.Sqrt(p.Cx * p.Cx + p.Cy * p.Cy + p.Cz * p.Cz);
                if (!Built(p.Cx / pl, p.Cy / pl, p.Cz / pl, level, p.Edge / Radius * 0.8)) return;
                if (Cutaway != null && level >= 3 && (p.Cx * Cutaway[0] + p.Cy * Cutaway[1] + p.Cz * Cutaway[2]) / pl > 0.45) return;
            }
            if (level >= 2 && camDist > r && !Shell)
            {
                double horizon = Math.Sqrt(camDist * camDist - Radius * Radius) + Math.Sqrt(Math.Max(0, r * r - Radius * Radius));
                if (dist > horizon + p.Edge * 1.6 + 12) return;
            }
            // out of view (behind you or off to the side): drawn as it is, but not split finer, so only what you see loads in
            // detail; pieces right around you always split, so turning your head never shows a hole
            if (ViewDir != null && level >= 3 && dist > p.Edge * 1.5 && !underCam)
            {
                double ang = Math.Acos(Math.Max(-1, Math.Min(1, (dx * ViewDir[0] + dy * ViewDir[1] + dz * ViewDir[2]) / Math.Max(1e-12, dist))));
                if (ang > ViewHalfAngle + Math.Asin(Math.Min(1, p.Edge * 0.9 / dist)) + 0.05) { _drawList.Add(p); return; }
            }
            bool mustSplit = Shell && (Mask != 0 || Cutaway != null) && level < 4;     // rings, gaps and cutaways show only once the shell is cut into small enough pieces
            if (level < MaxLevel[layer] && (dist < p.Edge * splitFactor || mustSplit) && (FinestEdgeKm <= 0 || layer != Ground || p.Edge * 0.5 >= FinestEdgeKm))
            {
                // the four smaller pieces, nearest to the camera first, so detail fills in where you are looking from
                long[] ci = { i * 2, i * 2 + 1, i * 2, i * 2 + 1 }, cj = { j * 2, j * 2, j * 2 + 1, j * 2 + 1 };
                double[] cd = new double[4]; int[] order = { 0, 1, 2, 3 };
                double n = Math.Pow(2, level + 1), size = 2.0 / n;
                for (int q = 0; q < 4; q++)
                {
                    double x, y, z; Dir(f, -1 + (ci[q] + 0.5) * size, -1 + (cj[q] + 0.5) * size, out x, out y, out z);
                    double rr = LayerRadius(layer);
                    cd[q] = (x * rr - camX) * (x * rr - camX) + (y * rr - camY) * (y * rr - camY) + (z * rr - camZ) * (z * rr - camZ);
                }
                Array.Sort(cd, order);
                bool all = true;
                for (int q = 0; q < 4; q++) if (Get(layer, f, level + 1, ci[order[q]], cj[order[q]], true) == null) all = false;
                if (all)
                {
                    for (int q = 0; q < 4; q++) Visit(layer, f, level + 1, ci[order[q]], cj[order[q]], camX, camY, camZ, splitFactor);
                    return;
                }
            }
            // the finished piece under the camera: the hull when outside a shell, the land when inside (or on a planet)
            if ((layer == Ground && (!Shell || _camInside) || layer == Hull && (!_camInside || !Habitable)) && f == _camF)
            {
                double sz = 2.0 / Math.Pow(2, level), s0 = -1 + i * sz, t0 = -1 + j * sz;
                if (_camS >= s0 && _camS <= s0 + sz && _camT >= t0 && _camT <= t0 + sz)
                {
                    ReadyStep = p.Edge / Grid; CamPieceEdge = p.Edge; CamPieceLevel = level;
                    // the finer pieces under you asked for ahead of the camera (four levels down, all four of each, as the piece
                    // descends only when all four are made), so coming down they are built before you get there and the floor
                    // never holds you waiting at each level (the lock-and-hand-off on the way down to the ground)
                    Prefetch(layer, f, level, i, j, PrefetchLevels);
                }
            }
            _drawList.Add(p);
        }
        /// <summary>How many levels of finer ground are asked for ahead of the camera, under it.</summary>
        public static int PrefetchLevels = 4;
        private void Prefetch(int layer, int f, int level, long i, long j, int depth)
        {
            if (depth <= 0 || level >= MaxLevel[layer]) return;
            double size = 2.0 / Math.Pow(2, level + 1);
            if (FinestEdgeKm > 0 && layer == Ground && size * LayerRadius(layer) * 0.9 < FinestEdgeKm) return;   // (no finer than the grains need)
            long ci0 = (long)Math.Floor((_camS + 1) / size), cj0 = (long)Math.Floor((_camT + 1) / size);
            for (long a = i * 2; a <= i * 2 + 1; a++) for (long b = j * 2; b <= j * 2 + 1; b++) Get(layer, f, level + 1, a, b, true);
            if (ci0 >= i * 2 && ci0 <= i * 2 + 1 && cj0 >= j * 2 && cj0 <= j * 2 + 1) Prefetch(layer, f, level + 1, ci0, cj0, depth - 1);
        }

        private void Trim()
        {
            if (_cache.Count <= MaxCached) return;
            var old = new List<PKey>();
            foreach (var kv in _cache) if (kv.Value.Level > 1 && _frame - kv.Value.LastUsed > 30) old.Add(kv.Key);
            foreach (PKey k in old) { Release(_cache[k]); _cache.Remove(k); }
        }

        public void Dispose()
        {
            _disposed = true; lock (_askLock) _asks.Clear();
            foreach (var p in _cache.Values) p.Buffer.Dispose();
            while (_pool.Count > 0) _pool.Pop().Dispose();
            _cache.Clear();
            foreach (var p in _pending.Values) p.Buffer.Dispose();
            _pending.Clear();
            if (_airShell != null) _airShell.Dispose();
        }
    }
}
