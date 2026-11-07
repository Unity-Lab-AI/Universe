// DNA-AND · simulation · one planet's life, all of it: a planet starts dead. A lightning strike (or the spark of life)
// at a spot makes the first cells there, and every strike starts its own family tree. Life then spreads region by region
// across the seas and land, breeds with mutation into new species, and the ones that fit each place win.
// What you see up close is drawn from this: the species of that spot, from that planet's own history.
using System;
using System.Collections.Generic;
using DnaAnd.Core;

namespace DnaAnd.Sim
{
    internal sealed class Species
    {
        public int Id, Parent, Root;            // Root: which spark of life it comes from
        public Genome G; public double Born;    // years since the planet's first spark
        public double Total;                    // how many there are on the whole planet (in crowds)
        public bool Extinct;
        public double Spoke = -1;               // when its line first reached communication (years), or -1
    }

    internal sealed class PlanetLife
    {
        public const int Regions = 100;                            // the 100 rule: 100 regions spread evenly over the planet
        public readonly Dictionary<int, Species> All = new Dictionary<int, Species>();   // the living species; extinct ones are let go
        public int Climbs;                                         // how many lineages have climbed a step
        public int EverLived { get { return _nextId - 1; } }
        public double Years;                                       // since the first spark
        public int Sparks;                                         // how many times life has been started
        /// <summary>The god's Souls on this planet. Living things give them off; powers spend them.</summary>
        public double Souls;
        /// <summary>How much of the star's harmful radiation reaches the ground (0 to 1), set from the planet's magnetic shield. Water shields sea life.</summary>
        public double Radiation = 0.25;
        /// <summary>Set from the planet's air: how much warmer or colder the world is than Earth's 15 °C, and the oxygen in the air.</summary>
        public double WarmerK, Oxygen = 0.21;
        /// <summary>The regions' weather, set by the planet's weather each step: rain (1 = average) and ice cover (0 to 1).</summary>
        public PlanetWeather Weather;
        public double RegionY(int i) { return PY[i]; }
        public int[] Neighbours(int i) { return Near[i]; }
        public void RegionCentre(int i, out double x, out double y, out double z) { Centre(i, out x, out y, out z); }
        /// <summary>Plant-like life in a region (crowds).</summary>
        public double RegionProducers(int i) { double p = 0; foreach (var kv in _cells[i]) if (All[kv.Key].G.Photo > 0.5f) p += kv.Value; return p; }
        /// <summary>The surface (soil, river mud), set by the planet each step.</summary>
        public PlanetSurface Surface;
        public double RegionWarmth(int i) { return _warm[i]; }
        public double RegionSea(int i) { return _wet[i]; }
        /// <summary>Plant-like life and early microbes on the whole planet (crowds), for the air.</summary>
        public double Producers, Microbes;
        public const double LightningCost = 4;                     // Lightning is tier 4: tier x area (one strike) x strength (1)
        /// <summary>Living things give off Souls slowly: this many a year for every crowd of life (a starting value, to be balanced).</summary>
        public const double SoulsPerCrowdYear = 5e-11;
        private readonly Dictionary<int, double>[] _cells = new Dictionary<int, double>[Regions];   // species -> crowds, per region
        private readonly double[] _warm = new double[Regions], _wet = new double[Regions], _light = new double[Regions];
        private static readonly double[] PX = new double[Regions], PY = new double[Regions], PZ = new double[Regions];
        private static readonly int[][] Near = new int[Regions][];     // each region's six nearest neighbours

        static PlanetLife()
        {
            // 100 points spread evenly over the sphere (a golden-angle spiral); a region is everything nearest its point
            for (int i = 0; i < Regions; i++)
            {
                double y = 1 - 2 * (i + 0.5) / Regions, r = Math.Sqrt(1 - y * y), th = i * 2.399963229728653;
                PX[i] = Math.Cos(th) * r; PY[i] = y; PZ[i] = Math.Sin(th) * r;
            }
            for (int i = 0; i < Regions; i++)
            {
                var order = new List<int>(); for (int j = 0; j < Regions; j++) if (j != i) order.Add(j);
                order.Sort((a, b) => (PX[b] * PX[i] + PY[b] * PY[i] + PZ[b] * PZ[i]).CompareTo(PX[a] * PX[i] + PY[a] * PY[i] + PZ[a] * PZ[i]));
                Near[i] = order.GetRange(0, 6).ToArray();
            }
        }
        private readonly Rng _rng;
        private int _nextId = 1;
        public int Alive { get; private set; }
        public static readonly string[] StageNames = { "replicators (virus-like)", "single cells", "many-celled life", "nerves", "brains", "big-brained hunters", "communication" };
        /// <summary>The highest stage any living species has reached on the planet.</summary>
        public int TopStage { get; private set; }
        private const double Room = 200;          // how many crowds of life a region can hold
        private const int MaxPerRegion = 100;     // the 100 rule: up to 100 species groups in a region, each standing for many close real species
        // Pacing: each stage takes about as long as it did on Earth, played over a set number of minutes, so a game reaches
        // communication in about three hours and space in about four to six (starting values, to be balanced).
        //                                     replicators  cells   many-celled  nerves  brains  big-brained  communication
        public static readonly double[] StageYears   = { 200e6,      3.0e9,  100e6,       80e6,   500e6,  2e6,         0 };
        public static readonly double[] StageMinutes = { 15,         40,     20,          20,     45,     30,          60 };
        /// <summary>How fast time runs on this planet now: it follows the stage life has reached.</summary>
        /// <summary>After communication, an intelligent species goes step by step: it lives wild on the land, then forms tribes,
        /// then villages, towns and cities, then goes into space. Each step takes an even share of that stage's play time.</summary>
        public static readonly string[] CivSteps = { "living wild on the land", "tribes", "villages", "towns", "cities", "into space" };
        public static double CivStepYears { get { return 50.0 * StageMinutes[StageMinutes.Length - 1] * 60 / CivSteps.Length; } }
        public int CivStep(Species s) { return s.Spoke < 0 ? 0 : (int)Math.Min(CivSteps.Length - 1, Math.Max(0, (Years - s.Spoke) / CivStepYears)); }
        public double YearsPerSecond { get { int s = TopStage; return s < StageNames.Length - 1 ? StageYears[s] / (StageMinutes[s] * 60) : 50; } }
        private double _stageReached;                // when the planet's current top stage was first reached

        /// <summary>Builds the regions from the planet's own ground: how warm, how wet (sea or land) and how lit each one is.</summary>
        public PlanetLife(Func<double, double, double, double> groundAt, ulong seed)
        {
            _rng = new Rng(seed ^ 0x11FEUL);
            // each region is measured over its whole area, not just its middle: 4,000 spots spread over the planet
            var cnt = new int[Regions];
            for (int i = 0; i < Regions; i++) _cells[i] = new Dictionary<int, double>();
            const int S = 4000;
            // the ground heights are measured on every core at once (each spot writes only its own slot); the first one
            // is measured alone, so anything the ground sets up on first use is ready before the workers start
            var gs = new double[S];
            Func<int, double> spotY = k => 1 - 2 * (k + 0.5) / S;
            Action<int, double[]> spot = (k, xyz) => { double y = spotY(k), r = Math.Sqrt(1 - y * y), th = k * 2.399963229728653; xyz[0] = Math.Cos(th) * r; xyz[1] = y; xyz[2] = Math.Sin(th) * r; };
            { var p0 = new double[3]; spot(0, p0); gs[0] = groundAt(p0[0], p0[1], p0[2]); }
            Jobs.For(S - 1, j => { var p = new double[3]; spot(j + 1, p); gs[j + 1] = groundAt(p[0], p[1], p[2]); });
            for (int k = 0; k < S; k++)
            {
                double y = spotY(k), r = Math.Sqrt(1 - y * y), th = k * 2.399963229728653, x = Math.Cos(th) * r, z = Math.Sin(th) * r;
                int i = RegionAt(x, y, z); double g = gs[k];
                _wet[i] += g < 0 ? 1 : Math.Max(0, 0.5 - g * 0.2);          // seas are wet; low land is damp, high land is dry
                _warm[i] += 1 - Math.Abs(y) - Math.Max(0, g) * 0.06;        // warm at the middle, cold at the poles and on mountains
                _light[i] += g < -2 ? 0.3 : 1;                               // deep water is dim
                cnt[i]++;
            }
            for (int i = 0; i < Regions; i++) { double c = Math.Max(1, cnt[i]); _wet[i] /= c; _warm[i] /= c; _light[i] /= c; }
        }

        private static void Centre(int i, out double x, out double y, out double z) { x = PX[i]; y = PY[i]; z = PZ[i]; }

        /// <summary>The region holding a unit direction: the one whose point is nearest.</summary>
        public static int RegionAt(double x, double y, double z)
        {
            int best = 0; double bd = -2;
            for (int i = 0; i < Regions; i++) { double d = PX[i] * x + PY[i] * y + PZ[i] * z; if (d > bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Lightning (or the spark of life) at a spot: the first cells of a new family tree, if the place can hold life.
        /// The first spark on a planet is free; every one after costs Souls. Returns a plain reason when it does not work.</summary>
        public string Strike(double x, double y, double z)
        {
            bool free = Sparks == 0;
            if (!free && Souls < LightningCost) return "not enough souls: lightning costs " + LightningCost + ", you have " + Souls.ToString("0.0");
            int i = RegionAt(x, y, z);
            if (_wet[i] < 0.2 || _warm[i] < 0.05) { if (!free) Souls -= LightningCost; return "the lightning struck, but this spot is too dry or too frozen for the first cells"; }
            if (!free) Souls -= LightningCost;
            Spark(x, y, z);
            return free ? "life has begun here (the first spark is free)" : "life has begun here (" + LightningCost + " souls)";
        }

        public bool Spark(double x, double y, double z)
        {
            int i = RegionAt(x, y, z);
            if (_wet[i] < 0.2 || _warm[i] < 0.05) return false;           // too dry or too frozen for the first cells
            var g = new Genome { Stage = 0, Size = 0.3f, Speed = 0.1f, Photo = 0.1f, Graze = 0.7f, Hunt = 0, Toxin = 0, Defence = 0.05f, Coat = 0.1f, Warm = (float)_warm[i],
                Surface = _rng.Below(64), Shape = 0, R = _rng.Range(0.3f, 1f), G = _rng.Range(0.3f, 1f), B = _rng.Range(0.3f, 1f) };
            int id = _nextId++;
            All.Add(id, new Species { Id = id, Parent = 0, Root = id, G = g, Born = Years });
            _cells[i][id] = 1;
            Sparks++;
            return true;
        }

        /// <summary>A region's food web for one step: what there is to eat, and how hard each kind is being eaten.</summary>
        private struct Web
        {
            public double Nutrients;   // nutrients left for each plant-like living thing, once the region's crowd shares them
            public double Plants;      // 0..1: how much plant-like life a grazer finds
            public double Prey;        // 0..1: how much animal life a hunter finds
            public double Grazed;      // how hard plant-like life is being grazed (per thousand years)
            public double Hunted;      // how hard animal life is being hunted (per thousand years)
            /// <summary>A region nobody lives in yet: all its nutrients, nothing to graze or hunt, nothing eating.</summary>
            public static Web Open(double nutrients) { return new Web { Nutrients = nutrients, Plants = 0.3, Prey = 0.2 }; }
        }

        // the nutrients a region holds: water and warmth wash minerals loose; deep cold or dry ground holds little
        private double Nutrients(int i) { return Math.Max(0.05, Math.Min(1, 0.2 + _wet[i] * 0.5 + _warm[i] * 0.4 + (Weather != null ? Weather.Upwelling[i] : 0) + (Surface != null ? (_wet[i] > 0.6 ? Surface.RiverMud[i] : Math.Min(0.3, Surface.SoilM[i] / 10)) : 0))); }   // soil on land, river mud in the sea   // upwelling currents add more

        // how fast it is eaten: plant-like life by grazers, animal life by hunters; defences, speed, armour and a tough coat help
        private static double Eaten(Genome g, Web w)
        {
            double guard = (1 - g.Defence * 0.5) * (1 - g.Coat * 0.3) * (1 - g.Armour * 0.4) * (1 - g.Speed * 0.3);
            return (w.Grazed * g.Photo + w.Hunted * (1 - g.Photo) * (1 - g.Hunt * 0.5)) * guard;
        }

        private double Fit(Genome g, int i, Web w)
        {
            double warmth = Weather != null ? 0.62 + (Weather.TempC[i] - 15) / 40.0 : _warm[i] + WarmerK / 60.0;   // the region's own temperature, from the weather
            double heat = 1 - Math.Abs(warmth - g.Warm) * 2.2;
            // food: light and nutrients for plant-like life, plant-like life for grazers, other animals for hunters
            double food = g.Photo * _light[i] * (0.4 + w.Nutrients) + g.Graze * (0.3 * w.Nutrients + 0.9 * w.Plants) + g.Hunt * 1.1 * w.Prey;
            double wet = Weather != null && _wet[i] < 0.6 ? Math.Min(1, _wet[i] * Weather.Rain[i]) : _wet[i];   // on land, rain decides how wet it is
            double water = 0.3 + g.Coat * 0.7 + (0.7 - g.Coat * 0.7) * wet;   // the wetter the region, the less a tough coat is needed              // dry land needs a tough coat
            double shield = 1 - Radiation * 0.7 * (1 - _wet[i]);                     // without a magnetic shield, life on dry land is burned by the star
            double ice = Weather != null ? 1 - Weather.Ice[i] * (_wet[i] > 0.6 ? 0.4 : 0.85) : 1;          // ice: hard on land, sea life lives on under it
            double breath = g.Stage >= 3 ? Math.Min(1, 0.05 + Oxygen / 0.02) : 1;      // animals with nerves and more need oxygen in the air
            return Math.Max(0, heat) * food * water * shield * breath * ice;
        }

        /// <summary>One step of the planet's life: growth, competition, spread to neighbours, new species. <paramref name="years"/> long.</summary>
        public void Step(double years)
        {
            if (Sparks == 0) { Years += years; return; }
            Years += years;
            var add = new List<KeyValuePair<int, KeyValuePair<int, double>>>();
            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i]; if (cell.Count == 0) continue;
                var keys = new List<int>(cell.Keys);
                // the food web of this region: how much plant-like life there is to graze, how much animal life to hunt,
                // and how hard each is being eaten
                double pm = 0, gm = 0, am = 0, hm = 0;
                foreach (int id in keys)
                {
                    var g0 = All[id].G; double n0 = cell[id];
                    pm += n0 * g0.Photo; gm += n0 * g0.Graze; hm += n0 * g0.Hunt; am += n0 * (1 - g0.Photo);
                }
                var web = new Web { Nutrients = Nutrients(i) * Room / (pm + Room), Plants = pm / (pm + gm + 1), Prey = am / (am + hm + 1),
                    Grazed = 0.5 * gm / (pm + gm + 1), Hunted = 0.6 * hm / (am + hm + 1) };
                foreach (int id in keys)
                {
                    var sp = All[id]; double n = cell[id];
                    double f = Fit(sp.G, i, web);
                    // grows where it finds food and fits the conditions, and is eaten by what grazes or hunts it; rates are per thousand years, worked out exactly for any step
                    double kyr = years / 1000.0, r = f * 0.6 - 0.08 - Eaten(sp.G, web);
                    n *= Math.Exp(Math.Max(-4, Math.Min(4, r * kyr)));
                    if (n < 0.05) { cell.Remove(id); continue; }
                    cell[id] = n;
                    // spreading: a share drifts into a neighbouring region, faster in water
                    if (_rng.NextDouble() < 1 - Math.Exp(-kyr * 0.6 * (0.5 + _wet[i] + sp.G.Speed)))
                    {
                        int j = Neighbour(i);
                        if (Fit(sp.G, j, Web.Open(Nutrients(j))) < 0.12) continue;       // spreads only where the nutrients and conditions suit it
                        add.Add(new KeyValuePair<int, KeyValuePair<int, double>>(j, new KeyValuePair<int, double>(id, Math.Min(n * 0.2, 1))));
                    }
                    // a new species: copies drift far enough from the parent (about once in two million years per region)
                    if (n > 2 && _rng.NextDouble() < (1 - Math.Exp(-kyr * 0.0005)) / keys.Count)
                    {
                        var g = Genome.Mutate(sp.G, _rng, 3);
                        // now and then a lineage climbs one step up the ladder of life; never skipping a step
                        if (g.Stage == TopStage && g.Stage < StageNames.Length - 1 && Years - _stageReached > StageYears[TopStage] && _rng.NextDouble() < 0.2)
                        { g.Stage++; g.Size = Math.Min(3f, g.Size * 1.6f); Climbs++; }
                        g.R = Math.Max(0.1f, Math.Min(1, sp.G.R + _rng.Range(-0.2f, 0.2f))); g.G = Math.Max(0.1f, Math.Min(1, sp.G.G + _rng.Range(-0.2f, 0.2f))); g.B = Math.Max(0.1f, Math.Min(1, sp.G.B + _rng.Range(-0.2f, 0.2f)));
                        int nid = _nextId++;
                        All.Add(nid, new Species { Id = nid, Parent = id, Root = sp.Root, G = g, Born = Years, Spoke = sp.G.Stage >= StageNames.Length - 1 ? sp.Spoke : g.Stage >= StageNames.Length - 1 ? Years : -1 });
                        add.Add(new KeyValuePair<int, KeyValuePair<int, double>>(i, new KeyValuePair<int, double>(nid, n * 0.1)));
                    }
                }
            }
            // competition: a region holds only so much of each way of living. Each stage competes with its own kind
            // (cells with cells, animals with animals), so new kinds of life find room beside the old instead of being crowded out.
            var totals = new double[StageNames.Length];
            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i]; if (cell.Count == 0) continue;
                Array.Clear(totals, 0, totals.Length);
                foreach (var kv in cell) totals[All[kv.Key].G.Stage] += kv.Value;
                bool over = false; foreach (double t in totals) if (t > Room) over = true;
                if (!over) continue;
                var ids = new List<int>(cell.Keys);
                foreach (int id in ids)
                {
                    double t = totals[All[id].G.Stage]; if (t <= Room) continue;
                    double nn = cell[id] * Room / t; if (nn < 0.05) cell.Remove(id); else cell[id] = nn;
                }
            }
            foreach (var a in add)
            {
                var cell = _cells[a.Key];
                double have; cell.TryGetValue(a.Value.Key, out have);
                cell[a.Value.Key] = have + a.Value.Value;
                if (cell.Count > MaxPerRegion)
                {
                    int least = -1; double ln = double.MaxValue;
                    foreach (var kv in cell) if (kv.Value < ln) { ln = kv.Value; least = kv.Key; }
                    cell.Remove(least);
                }
            }
            // totals and extinctions
            foreach (var sp in All.Values) sp.Total = 0;
            foreach (var cell in _cells) foreach (var kv in cell) All[kv.Key].Total += kv.Value;
            int before = TopStage;
            Alive = 0; TopStage = 0; double crowds = 0;
            var gone = new List<int>();
            foreach (var sp in All.Values) { sp.Extinct = sp.Total <= 0; if (sp.Extinct) gone.Add(sp.Id); else { Alive++; crowds += sp.Total; if (sp.G.Stage > TopStage) TopStage = sp.G.Stage; } }
            foreach (int id in gone) All.Remove(id);
            Producers = 0; Microbes = 0; foreach (var sp in All.Values) { if (sp.G.Photo > 0.5f) Producers += sp.Total; if (sp.G.Stage <= 1) Microbes += sp.Total; }
            if (TopStage > before) _stageReached = Years;
            Souls += crowds * SoulsPerCrowdYear * years;
        }

        /// <summary>The whole life of this planet as a save chunk ("LIFE"): its clock, its species and where each one lives.
        /// The regions' warmth, wetness and light are not saved; they come from the planet's own ground.</summary>
        public Chunk Save()
        {
            var ms = new System.IO.MemoryStream(); var o = new System.IO.BinaryWriter(ms);
            o.Write(Years); o.Write(Sparks); o.Write(Souls); o.Write(Climbs); o.Write(_nextId); o.Write(_stageReached);
            foreach (var u in _rng.State) o.Write(u);
            o.Write(All.Count);
            foreach (var sp in All.Values)
            {
                o.Write(sp.Id); o.Write(sp.Parent); o.Write(sp.Root); o.Write(sp.Born); o.Write(sp.Total); o.Write(sp.Spoke);
                SaveFile.WriteFields(o, sp.G);
            }
            o.Write(_cells.Length);
            foreach (var cell in _cells) { o.Write(cell.Count); foreach (var kv in cell) { o.Write(kv.Key); o.Write(kv.Value); } }
            o.Flush();
            return new Chunk { Name = "LIFE", Version = 1, Data = ms.ToArray() };
        }

        /// <summary>Puts back the life saved by <see cref="Save"/>, on the same planet.</summary>
        public void Load(Chunk c)
        {
            if (c.Name != "LIFE" || c.Version > 1) throw new System.IO.InvalidDataException("not a life chunk this game can read");
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(c.Data));
            Years = r.ReadDouble(); Sparks = r.ReadInt32(); Souls = r.ReadDouble(); Climbs = r.ReadInt32(); _nextId = r.ReadInt32(); _stageReached = r.ReadDouble();
            _rng.SetState(new[] { r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64() });
            All.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var sp = new Species { Id = r.ReadInt32(), Parent = r.ReadInt32(), Root = r.ReadInt32(), Born = r.ReadDouble(), Total = r.ReadDouble(), Spoke = r.ReadDouble() };
                sp.G = SaveFile.ReadFields<Genome>(r);
                All[sp.Id] = sp;
            }
            int regions = r.ReadInt32();
            if (regions != _cells.Length) throw new System.IO.InvalidDataException("this life was saved on a planet with " + regions + " regions");
            for (int i = 0; i < regions; i++) { _cells[i].Clear(); int m = r.ReadInt32(); for (int k = 0; k < m; k++) { int id = r.ReadInt32(); _cells[i][id] = r.ReadDouble(); } }
            Alive = 0; TopStage = 0;
            foreach (var sp in All.Values) { Alive++; if (sp.G.Stage > TopStage) TopStage = sp.G.Stage; }
        }

        /// <summary>A disaster at a spot (a great eruption or quake): kills a share of the life in that region and half that in
        /// its neighbours. A share of a half or more also darkens and cools the whole world, so everywhere loses some.</summary>
        public void Disaster(double x, double y, double z, double share)
        {
            int i = RegionAt(x, y, z);
            Kill(i, share);
            foreach (int j in Near[i]) Kill(j, share * 0.5);
            if (share >= 0.5) for (int k = 0; k < _cells.Length; k++) Kill(k, 0.15);
        }

        /// <summary>A disaster for the whole world (a great impact): every region loses this share of its life.</summary>
        public void GlobalKill(double share)
        {
            for (int k = 0; k < _cells.Length; k++)
            {
                // microbes deep in the rock and at sea-floor vents ride out even the worst: a few of each stay alive
                var cell = _cells[k]; var ids = new List<int>(cell.Keys);
                foreach (int id in ids)
                {
                    double n = cell[id] * (1 - share);
                    if (All[id].G.Stage <= 1) n = Math.Max(n, Math.Min(cell[id], 0.1));
                    if (n < 0.05) cell.Remove(id); else cell[id] = n;
                }
            }
        }

        private void Kill(int i, double share)
        {
            var cell = _cells[i]; var ids = new List<int>(cell.Keys);
            foreach (int id in ids)
            {
                double n = cell[id] * (1 - share);
                if (All[id].G.Stage <= 1) n = Math.Max(n, Math.Min(cell[id], 0.1));   // microbes deep in the rock and at vents ride it out
                if (n < 0.05) cell.Remove(id); else cell[id] = n;
            }
        }

        private int Neighbour(int i) { return Near[i][_rng.Below(6)]; }

        /// <summary>The species living at a spot, most numerous first, with how many crowds of each.</summary>
        public List<KeyValuePair<Species, double>> At(double x, double y, double z)
        {
            var list = new List<KeyValuePair<Species, double>>();
            foreach (var kv in _cells[RegionAt(x, y, z)]) list.Add(new KeyValuePair<Species, double>(All[kv.Key], kv.Value));
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        /// <summary>The highest stage reached by the species living at a spot (-1 if none).</summary>
        public int StageAt(double x, double y, double z)
        {
            int best = -1; foreach (var kv in _cells[RegionAt(x, y, z)]) best = Math.Max(best, All[kv.Key].G.Stage);
            return best;
        }

        /// <summary>How many of the planet's regions hold any life.</summary>
        public int RegionsWithLife() { int n = 0; foreach (var c in _cells) if (c.Count > 0) n++; return n; }
        public int RegionCount { get { return _cells.Length; } }
    }
}
