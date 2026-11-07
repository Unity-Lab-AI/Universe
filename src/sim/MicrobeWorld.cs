// Universe · simulation · a live microscopic ecosystem: microbes with real genes, breeding with mutation,
// natural selection by light, food, temperature, predators and toxins, and viruses that infect by surface match.
// Nothing is scripted: who wins comes out of the rules.
using System;
using System.Collections.Generic;
using Universe.Core;

namespace Universe.Sim
{
    /// <summary>A microbe's genes. Every number is a trait; copies are made with small mistakes (mutation).</summary>
    internal struct Genome
    {
        public float Size;        // 0.5..3 micrometres across
        public float Speed;       // how fast it swims (costs energy)
        public float Photo;       // 0..1: how much of its food comes from light (plants of the micro world)
        public float Graze;       // 0..1: how much it eats food dissolved in the water
        public float Hunt;        // 0..1: how much it eats other microbes
        public float Toxin;       // 0..1: poison it puts out (costs energy, kills microbes without the defence)
        public float Defence;     // 0..1: protection from poison
        public float Coat;        // 0..1: a tough coat (survives being eaten and dry times, but grows slowly)
        public float Warm;        // 0..1: the temperature it likes best (cold to hot)
        public int Surface;       // 0..63: the shape of its outer surface; viruses must match it to get in
        public int Shape;         // 0 ball, 1 rod, 2 spiral
        public int Stage;         // how far up the ladder of life: 0 replicators (virus-like), 1 cells, 2 many-celled, 3 nerves, 4 brains, 5 big-brained hunters, 6 communication
        public float R, G, B;     // family colour (drifts a little each generation, so families stay visible)
        // the body plan: read by the body builder once life is many-celled; each 0..1, drifting a little every generation
        public float Segs;        // how many body segments (1 to 7)
        public float Legs;        // how many pairs of legs (none to 4)
        public float LegLen;      // how long the legs are
        public float Neck;        // how long the neck is
        public float Tail;        // how long the tail is
        public float Fins;        // fins along the body (for water)
        public float Wings;       // wings (for air)
        public float Head;        // how big the head is (grows with brains)
        public float Eyes;        // how many eyes (none to 4 pairs)
        public float Armour;      // shell plates and spikes
        public float Upright;     // stands on its back legs
        public float Branch;      // for plants and fungi: how much it branches

        public static Genome Mutate(Genome g, Rng rng, float rate)
        {
            var c = g;
            float big = rng.NextDouble() < 0.01 ? 6f : 1f;                     // rarely, a big jump: a weird new combination
            c.Size = Clamp(g.Size + rng.Range(-0.15f, 0.15f) * rate * big, 0.5f, 3f);
            c.Speed = Clamp(g.Speed + rng.Range(-0.08f, 0.08f) * rate * big, 0, 1);
            c.Photo = Clamp(g.Photo + rng.Range(-0.06f, 0.06f) * rate * big, 0, 1);
            c.Graze = Clamp(g.Graze + rng.Range(-0.06f, 0.06f) * rate * big, 0, 1);
            c.Hunt = Clamp(g.Hunt + rng.Range(-0.06f, 0.06f) * rate * big, 0, 1);
            c.Toxin = Clamp(g.Toxin + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Defence = Clamp(g.Defence + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Coat = Clamp(g.Coat + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Warm = Clamp(g.Warm + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            if (rng.NextDouble() < 0.04 * rate * big) c.Surface = (g.Surface + (rng.NextDouble() < 0.5 ? 1 : 63)) % 64;   // a new surface: old viruses no longer fit
            if (rng.NextDouble() < 0.004 * big) c.Shape = rng.Below(3);
            c.Segs = Clamp(g.Segs + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Legs = Clamp(g.Legs + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.LegLen = Clamp(g.LegLen + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Neck = Clamp(g.Neck + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Tail = Clamp(g.Tail + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Fins = Clamp(g.Fins + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Wings = Clamp(g.Wings + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Head = Clamp(g.Head + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Eyes = Clamp(g.Eyes + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Armour = Clamp(g.Armour + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Upright = Clamp(g.Upright + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1); c.Branch = Clamp(g.Branch + rng.Range(-0.05f, 0.05f) * rate * big, 0, 1);
            c.Stage = g.Stage;                                                // climbing the ladder is decided by the planet's life, rarely
            c.R = Clamp(g.R + rng.Range(-0.03f, 0.03f), 0.1f, 1); c.G = Clamp(g.G + rng.Range(-0.03f, 0.03f), 0.1f, 1); c.B = Clamp(g.B + rng.Range(-0.03f, 0.03f), 0.1f, 1);
            return c;
        }
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    internal sealed class Microbe
    {
        public double X, Y, Vx, Vy, Energy, Age; public Genome G; public int Family, Gen;
        public bool Alive = true; public double Infected = -1; public int VirusSurface; public int VirusFamily;
    }

    internal sealed class Virus
    {
        public double X, Y, Age; public int Target; public int Burst; public bool Alive = true; public int Family;
    }

    internal sealed class MicrobeWorld
    {
        public const double Width = 400, Height = 250;        // micrometres: the patch under the microscope
        public readonly List<Microbe> Microbes = new List<Microbe>();
        public readonly List<Virus> Viruses = new List<Virus>();
        public double Time;                                     // in game hours
        public int Families;                                    // how many different families are alive
        public int Births, Deaths, Infections;
        public string Leader = "";                              // the most common family right now, and why it does well
        private readonly Rng _rng;
        private const int Cols = 80, Rows = 50;
        private readonly double[] _food = new double[Cols * Rows];   // food dissolved in the water
        private int _nextFamily = 1;
        public const int MaxMicrobes = 600, MaxViruses = 400;   // each one stands for a crowd of its kind: we never simulate every single microbe

        /// <summary>A patch holding the planet's own species of that spot (most numerous first, with how many crowds of each).</summary>
        public MicrobeWorld(ulong seed, List<KeyValuePair<Species, double>> local)
        {
            _rng = new Rng(seed);
            for (int i = 0; i < _food.Length; i++) _food[i] = 0.6;
            double total = 0; foreach (var kv in local) total += kv.Value;
            foreach (var kv in local)
            {
                int count = (int)Math.Round(250 * kv.Value / Math.Max(1e-9, total));
                if (kv.Key.G.Stage == 0)
                {
                    // the first life: virus-like replicators, too small and simple to be cells
                    for (int k = 0; k < count && Viruses.Count < MaxViruses; k++)
                        Viruses.Add(new Virus { X = _rng.NextDouble() * Width, Y = _rng.NextDouble() * Height, Target = kv.Key.G.Surface, Burst = 10, Family = kv.Key.Id });
                    continue;
                }
                for (int k = 0; k < count && Microbes.Count < MaxMicrobes / 2; k++)
                    Microbes.Add(new Microbe { X = _rng.NextDouble() * Width, Y = _rng.NextDouble() * Height, G = kv.Key.G, Energy = 0.6, Family = kv.Key.Id });
            }
            _nextFamily = 100000;
        }

        public MicrobeWorld(ulong seed)
        {
            _rng = new Rng(seed);
            for (int i = 0; i < _food.Length; i++) _food[i] = 0.6;
            // a starting mix: some light-eaters, some food-eaters, a few hunters, and a few viruses
            for (int f = 0; f < 10; f++)
            {
                var g = new Genome { Size = _rng.Range(0.8f, 2f), Speed = _rng.Range(0, 0.5f), Photo = f < 4 ? _rng.Range(0.6f, 1f) : _rng.Range(0, 0.2f),
                    Graze = f >= 4 && f < 8 ? _rng.Range(0.5f, 1f) : _rng.Range(0, 0.3f), Hunt = f >= 8 ? _rng.Range(0.5f, 0.9f) : 0, Toxin = 0, Defence = _rng.Range(0, 0.2f),
                    Coat = _rng.Range(0, 0.3f), Warm = _rng.Range(0.2f, 0.8f), Surface = _rng.Below(64), Shape = f % 3,
                    R = _rng.Range(0.2f, 1f), G = _rng.Range(0.2f, 1f), B = _rng.Range(0.2f, 1f) };
                int fam = _nextFamily++;
                for (int k = 0; k < 25; k++) Microbes.Add(new Microbe { X = _rng.NextDouble() * Width, Y = _rng.NextDouble() * Height, G = Genome.Mutate(g, _rng, 0.3f), Energy = 0.6, Family = fam });
            }
            for (int k = 0; k < 60; k++)
            {
                var host = Microbes[_rng.Below(Microbes.Count)];
                Viruses.Add(new Virus { X = _rng.NextDouble() * Width, Y = _rng.NextDouble() * Height, Target = host.G.Surface, Burst = 12, Family = 1000 + host.Family });
            }
        }

        /// <summary>Light: brightest at the top (the water's surface), fading with depth.</summary>
        public static double Light(double y) { return Math.Exp(-y / Height * 3.0); }
        /// <summary>Temperature 0..1: cold on the left, warm on the right (a warm vent).</summary>
        public static double Temp(double x) { return x / Width; }

        private int Cell(double x, double y) { int cx = Math.Max(0, Math.Min(Cols - 1, (int)(x / Width * Cols))), cy = Math.Max(0, Math.Min(Rows - 1, (int)(y / Height * Rows))); return cy * Cols + cx; }

        // a grid of who is where, for finding neighbours quickly
        private readonly Dictionary<int, List<Microbe>> _grid = new Dictionary<int, List<Microbe>>();
        private const double GridCell = 8;
        private int Key(double x, double y) { return (int)(x / GridCell) * 1000 + (int)(y / GridCell); }

        public void Step(double dt)
        {
            Time += dt;
            _grid.Clear();
            foreach (var m in Microbes) { int k = Key(m.X, m.Y); List<Microbe> l; if (!_grid.TryGetValue(k, out l)) { l = new List<Microbe>(); _grid[k] = l; } l.Add(m); }
            var born = new List<Microbe>();
            foreach (var m in Microbes)
            {
                if (!m.Alive) continue;
                var g = m.G; m.Age += dt;
                // feeding: light, dissolved food, other microbes
                double sizeCost = 0.012 * g.Size;
                double heatFit = 1 - Math.Abs(Temp(m.X) - g.Warm) * 2;                // far from its best temperature, it does badly
                double gain = g.Photo * Light(m.Y) * 0.09 * Math.Max(0, heatFit);
                int c = Cell(m.X, m.Y);
                double take = Math.Min(_food[c], g.Graze * 0.05 * dt * 10);
                _food[c] -= take; gain += take * 1.6 * Math.Max(0, heatFit);
                double cost = sizeCost + g.Speed * 0.025 + g.Toxin * 0.02 + g.Defence * 0.008 + (g.Photo + g.Graze + g.Hunt) * 0.006;
                m.Energy += (gain - cost * Math.Max(0.3, 1 - g.Coat * 0.5)) * dt * 10;
                // swimming: wander, and hunters swim harder
                double sp = g.Speed * 6 * (1 - g.Coat * 0.6);
                m.Vx = m.Vx * 0.9 + _rng.Range(-1, 1) * sp; m.Vy = m.Vy * 0.9 + _rng.Range(-1, 1) * sp;
                if (g.Photo > 0.5) m.Vy -= 0.3 * g.Speed;                                // light-eaters drift upward toward the light
                m.X = Math.Max(0, Math.Min(Width, m.X + m.Vx * dt)); m.Y = Math.Max(0, Math.Min(Height, m.Y + m.Vy * dt));
                // neighbours: hunting and poison
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        List<Microbe> l; if (!_grid.TryGetValue(Key(m.X + dx * GridCell, m.Y + dy * GridCell), out l)) continue;
                        foreach (var o in l)
                        {
                            if (o == m || !o.Alive) continue;
                            double ddx = o.X - m.X, ddy = o.Y - m.Y, d2 = ddx * ddx + ddy * ddy, reach = (g.Size + o.G.Size) * 1.2;
                            if (d2 > reach * reach) continue;
                            if (g.Toxin > 0.2 && o.Family != m.Family && _rng.NextDouble() < (g.Toxin - o.G.Defence) * 0.05) { o.Alive = false; Deaths++; DropFood(o); }
                            else if (g.Hunt > 0.3 && g.Size > o.G.Size * 1.3 && o.Family != m.Family && _rng.NextDouble() < g.Hunt * 0.25 * (1 - o.G.Coat * 0.8))
                            { o.Alive = false; Deaths++; m.Energy += 0.4 * o.G.Size; }
                        }
                    }
                // a virus inside it: after a while it bursts, releasing new viruses
                if (m.Infected >= 0)
                {
                    m.Infected += dt; m.Energy -= 0.02 * dt * 10;
                    if (m.Infected > 3)
                    {
                        m.Alive = false; Deaths++;
                        for (int k = 0; k < 14 && Viruses.Count < MaxViruses; k++)
                        {
                            int t = m.VirusSurface; if (_rng.NextDouble() < 0.08) t = (t + (_rng.NextDouble() < 0.5 ? 1 : 63)) % 64;   // viruses mutate too
                            Viruses.Add(new Virus { X = m.X + _rng.Range(-2, 2), Y = m.Y + _rng.Range(-2, 2), Target = t, Burst = 14, Family = m.VirusFamily });
                        }
                        DropFood(m);
                        continue;
                    }
                }
                // breeding: split in two when it has stored enough
                if (m.Energy > 1.2 + g.Size * 0.4 && Microbes.Count + born.Count < MaxMicrobes && m.Infected < 0)
                {
                    m.Energy *= 0.5;
                    var child = new Microbe { X = m.X + _rng.Range(-1, 1), Y = m.Y + _rng.Range(-1, 1), G = Genome.Mutate(g, _rng, 1), Energy = m.Energy, Family = m.Family, Gen = m.Gen + 1 };
                    // a big enough change makes a new family
                    if (Math.Abs(child.G.Photo - g.Photo) + Math.Abs(child.G.Hunt - g.Hunt) + Math.Abs(child.G.Toxin - g.Toxin) > 0.25 || child.G.Shape != g.Shape)
                    { child.Family = _nextFamily++; child.G.R = _rng.Range(0.2f, 1f); child.G.G = _rng.Range(0.2f, 1f); child.G.B = _rng.Range(0.2f, 1f); }
                    born.Add(child); Births++;
                }
                if (m.Energy <= 0 || m.Age > 400 * (1 + g.Coat)) { m.Alive = false; Deaths++; DropFood(m); }
            }
            Microbes.RemoveAll(x => !x.Alive);
            Microbes.AddRange(born);
            // viruses drift; touching a microbe whose surface fits, they get in
            foreach (var v in Viruses)
            {
                v.Age += dt; v.X += _rng.Range(-1, 1) * 4 * dt; v.Y += _rng.Range(-1, 1) * 4 * dt;
                if (v.Age > 40 || v.X < 0 || v.Y < 0 || v.X > Width || v.Y > Height) { v.Alive = false; continue; }
                List<Microbe> l; if (!_grid.TryGetValue(Key(v.X, v.Y), out l)) continue;
                foreach (var m in l)
                {
                    if (!m.Alive || m.Infected >= 0) continue;
                    double ddx = m.X - v.X, ddy = m.Y - v.Y;
                    if (ddx * ddx + ddy * ddy > m.G.Size * m.G.Size * 1.5) continue;
                    int diff = Math.Abs(m.G.Surface - v.Target); diff = Math.Min(diff, 64 - diff);
                    if (diff <= 1 && _rng.NextDouble() < 0.5 * (1 - m.G.Coat * 0.5)) { m.Infected = 0; m.VirusSurface = v.Target; m.VirusFamily = v.Family; v.Alive = false; Infections++; break; }
                }
            }
            Viruses.RemoveAll(x => !x.Alive);
            // food spreads through the water and slowly renews from the sediment below
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    int i = y * Cols + x;
                    _food[i] += (0.004 + 0.01 * y / (double)Rows) * dt;
                    if (x > 0) { double f = (_food[i] - _food[i - 1]) * 0.1; _food[i] -= f; _food[i - 1] += f; }
                    if (y > 0) { double f = (_food[i] - _food[i - Cols]) * 0.1; _food[i] -= f; _food[i - Cols] += f; }
                    _food[i] = Math.Min(1.5, _food[i]);
                }
            // a few drift in from outside now and then, so a dead patch can be re-seeded
            if (Microbes.Count < 20 && _rng.NextDouble() < 0.2)
                Microbes.Add(new Microbe { X = _rng.NextDouble() * Width, Y = _rng.NextDouble() * Height, G = Genome.Mutate(new Genome { Size = 1, Speed = 0.3f, Photo = 0.6f, Graze = 0.4f, Warm = 0.5f, Surface = _rng.Below(64), R = 0.5f, G = 0.8f, B = 0.4f }, _rng, 2), Energy = 0.6, Family = _nextFamily++ });
            Count();
        }

        private void DropFood(Microbe m) { _food[Cell(m.X, m.Y)] += 0.15 * m.G.Size; }

        private void Count()
        {
            var counts = new Dictionary<int, int>(); var sample = new Dictionary<int, Microbe>();
            foreach (var m in Microbes) { int c; counts.TryGetValue(m.Family, out c); counts[m.Family] = c + 1; sample[m.Family] = m; }
            Families = counts.Count;
            int best = -1, bestN = 0; foreach (var kv in counts) if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
            if (best < 0) { Leader = "none"; return; }
            var g = sample[best].G;
            string how = g.Photo > 0.5 ? "lives on light" : g.Hunt > 0.4 ? "hunts others" : g.Graze > 0.4 ? "eats food in the water" : "a bit of everything";
            if (g.Toxin > 0.4) how += ", poisons rivals";
            if (g.Coat > 0.5) how += ", tough coat";
            string shape = g.Shape == 0 ? "ball" : g.Shape == 1 ? "rod" : "spiral";
            Leader = "family " + best + " (" + bestN + ", " + shape + ", " + how + ", likes it " + (g.Warm < 0.35 ? "cold" : g.Warm > 0.65 ? "warm" : "mild") + ")";
        }
    }
}
