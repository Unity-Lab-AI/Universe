// DNA-AND · simulation · rocks from space hitting the planet, on its own clock: the 10 impact sizes of the space page.
// How often each size hits follows real counts for Earth today (a house-size rock about every 60 years, a 50 m one about
// every 1,000, a 1 km one about every 500,000, a 10 km one about every 100 million), many times more often while the young
// system is still full of leftover rock (the heavy bombardment), falling away over its first billion years.
// What each does to life follows the table: from nothing, through flattening a region, to a mass extinction.
using System;
using DnaAnd.Core;

namespace DnaAnd.Sim
{
    internal sealed class PlanetImpacts
    {
        public static readonly string[] Sizes = { "dust", "pebble", "boulder", "house-size rock", "stadium-size rock", "city-killer", "country-killer", "extinction-size rock", "world-breaker", "planet collision" };
        // years between hits on an Earth-sized world today (0: never by nature after the planet forms)
        private static readonly double[] Every = { 0, 0, 1, 60, 1000, 20000, 5e5, 1e8, 0, 0 };
        public int Biggest = -1;                 // the biggest size that has hit so far
        public int Total;                        // hits of size 5 (stadium) and up
        public string Last = "";
        public double DarkYearsLeft;
        /// <summary>Craters left on the ground (direction and radius in km), newest last; the oldest wear away first.</summary>
        public readonly System.Collections.Generic.List<double[]> Craters = new System.Collections.Generic.List<double[]>();
        // crater radius in km by size (about ten to twenty times the rock): from a 150 m rock up to a 100 km one
        private static readonly double[] CraterKm = { 0, 0, 0, 0, 0, 1.5, 8, 90, 600, 0 };             // dust and soot dimming the star after a great impact
        private readonly Rng _rng;
        private double _age = -1;

        public PlanetImpacts(ulong seed) { _rng = new Rng(seed ^ 0x1A9AC7UL); }

        /// <summary>How many times more rock is flying about than today: high while the system is young.</summary>
        public static double Bombardment(double age) { return 1 + 300 * Math.Exp(-age / 1.5e8); }

        public void Advance(PlanetCore core, PlanetLife life)
        {
            if (_age < 0) { _age = core.Age; return; }
            double years = core.Age - _age; _age = core.Age;
            if (years <= 0) return;
            DarkYearsLeft = Math.Max(0, DarkYearsLeft - years);
            double flux = Bombardment(core.Age) * core.Size * core.Size;          // bigger worlds are bigger targets
            // every size gets its own count of hits for the years that passed; each hit does its harm (the small ones are
            // counted, but their harm is felt once per step, as they are spread over the whole world and time)
            int biggest = -1;
            for (int sz = Every.Length - 1; sz >= 4; sz--)
            {
                double every = Every[sz];
                if (sz == 8) every = 5e7 * Bombardment(0) / Bombardment(core.Age);          // world-breakers: only in the heavy bombardment
                if (every <= 0) continue;
                int k = Hits(years * flux / every);
                if (k == 0) continue;
                Total += k; if (biggest < 0) biggest = sz;
                for (int h = 0; h < (sz >= 6 ? Math.Min(k, 5) : 1); h++) Harm(sz, life);
            }
            if (biggest < 0) return;
            Biggest = Math.Max(Biggest, biggest);
            Last = "a " + Sizes[biggest] + " hit the planet: " + Effect(biggest);
        }

        // how many hits, given how many are expected (a Poisson count; large counts are close to the expected number)
        private int Hits(double expect)
        {
            if (expect > 50) return (int)Math.Min(int.MaxValue / 4, Math.Round(expect + Math.Sqrt(expect) * _rng.Gaussian()));
            double l = Math.Exp(-expect), p = 1; int k = 0;
            do { k++; p *= _rng.NextDouble(); } while (p > l);
            return k - 1;
        }

        private void Harm(int size, PlanetLife life)
        {
            var d = new Vec3(_rng.Gaussian(), _rng.Gaussian(), _rng.Gaussian()).Normalized;
            if (CraterKm[size] > 0) { Craters.Add(new double[] { d.X, d.Y, d.Z, CraterKm[size] }); if (Craters.Count > 60) { int least = 0; for (int q = 1; q < Craters.Count; q++) if (Craters[q][3] < Craters[least][3]) least = q; Craters.RemoveAt(least); } }   /* the smallest wear away first */
            switch (size)
            {
                case 4: life.Disaster(d.X, d.Y, d.Z, 0.03); break;
                case 5: life.Disaster(d.X, d.Y, d.Z, 0.08); break;
                case 6: life.Disaster(d.X, d.Y, d.Z, 0.5); life.GlobalKill(0.05); DarkYearsLeft = Math.Max(DarkYearsLeft, 3); break;
                case 7: life.Disaster(d.X, d.Y, d.Z, 0.95); life.GlobalKill(0.75); DarkYearsLeft = Math.Max(DarkYearsLeft, 10); break;
                default: life.GlobalKill(0.98); DarkYearsLeft = Math.Max(DarkYearsLeft, 1000); break;
            }
        }

        private static string Effect(int size)
        {
            switch (size)
            {
                case 4: return "flattened a forest's worth of land";
                case 5: return "wiped out life across a region";
                case 6: return "firestorms across a continent and a winter for the world";
                case 7: return "a global firestorm, years of dark, and a mass extinction";
                default: return "the oceans boiled and the crust melted; almost nothing survived";
            }
        }

        public string Describe() { return Total == 0 ? "no big impacts yet" : Total.ToString("N0") + " impacts of stadium size or more, the biggest a " + Sizes[Biggest]; }
    }
}
