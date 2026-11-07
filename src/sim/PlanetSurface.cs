// Universe · simulation · the planet's surface wearing down and building up, region by region, on the planet's own clock.
// Mountains rise where plates crash (a few millimetres a year) and are worn down by rain and ice, faster the higher they
// are, so a range wears away over tens of millions of years. Worn rock becomes soil (about a few centimetres per thousand
// years where it is warm and wet, much faster under plants; steep slopes lose it). Rivers carry the worn rock to the sea,
// where it piles up as sediment that is buried into stone and, at plate edges, carried down to melt again: the rock cycle.
// Soil feeds land life; river mud feeds the seas off the coast.
using System;

namespace Universe.Sim
{
    internal sealed class PlanetSurface
    {
        public readonly double[] ReliefKm;    // per region: how high its mountains stand above the lowlands
        public readonly double[] StartReliefKm; // per region: how high they stood when the game began
        public readonly double[] SoilM;       // per region: soil depth on land
        public readonly double[] MudM;        // per region: sediment laid on the sea floor
        public readonly double[] RiverMud;    // per region: nutrients rivers are bringing to this sea right now (0 to 0.3)
        public double StoneKm3;               // sediment buried and pressed into new stone, in all
        private double _age = -1;
        private readonly bool[] _uplift;

        public PlanetSurface(PlanetLife life, PlanetPlates plates, Func<double, double, double, double> groundAt)
        {
            int n = life.RegionCount;
            ReliefKm = new double[n]; StartReliefKm = new double[n]; SoilM = new double[n]; MudM = new double[n]; RiverMud = new double[n]; _uplift = new bool[n];
            var rng = new Universe.Core.Rng(31337);
            for (int i = 0; i < n; i++)
            {
                // the region's mountains to begin with: the highest of a few spots in it, above its lowest
                double cx, cy, cz; life.RegionCentre(i, out cx, out cy, out cz);
                double hi = double.MinValue, lo = double.MaxValue;
                for (int k = 0; k < 12; k++)
                {
                    double x = cx + rng.Range(-0.12f, 0.12f), y = cy + rng.Range(-0.12f, 0.12f), z = cz + rng.Range(-0.12f, 0.12f), l = Math.Sqrt(x * x + y * y + z * z);
                    double g = groundAt(x / l, y / l, z / l); hi = Math.Max(hi, g); lo = Math.Min(lo, g);
                }
                ReliefKm[i] = Math.Max(0, hi - Math.Max(0, lo)); StartReliefKm[i] = ReliefKm[i];
                SoilM[i] = 0;
            }
            if (plates != null) foreach (var e in plates.Edges) if (e.Kind == 1) _uplift[PlanetLife.RegionAt(e.X, e.Y, e.Z)] = true;
        }

        public void Advance(PlanetCore core, PlanetLife life, PlanetWeather wx)
        {
            if (_age < 0) { _age = core.Age; return; }
            double years = core.Age - _age; _age = core.Age;
            if (years <= 0 || wx == null) return;
            int n = ReliefKm.Length;
            for (int i = 0; i < n; i++) RiverMud[i] = 0;
            for (int i = 0; i < n; i++)
            {
                bool sea = life.RegionSea(i) > 0.6;
                // mountains: pushed up where plates crash, while the inside is hot enough to drive them
                if (_uplift[i] && core.Plates) ReliefKm[i] = Math.Min(9, ReliefKm[i] + 2e-6 * core.Heat * years * (1 - ReliefKm[i] / 9));
                if (sea) continue;
                // wearing down: rain and ice grind the high ground; exact for any length of step
                double rate = 1.5e-8 * (0.3 + wx.Rain[i]) * (1 + 2 * wx.Ice[i]);
                double before = ReliefKm[i];
                ReliefKm[i] *= Math.Exp(-rate * years);
                double wornKm = before - ReliefKm[i];
                // soil: worn rock turns to soil, faster when warm and wet and under plants; steep, icy ground keeps little
                double warm = Math.Max(0, Math.Min(1, (wx.TempC[i] + 5) / 30));
                double plants = Math.Min(1, life.RegionProducers(i) / 50);
                double make = 3e-5 * wx.Rain[i] * warm * (1 + 4 * plants);                 // metres a year
                double keep = 3 / (1 + ReliefKm[i] * 1.5) * (1 - wx.Ice[i] * 0.9);        // how deep soil can get here
                SoilM[i] = keep + (SoilM[i] - keep) * Math.Exp(-make * years / Math.Max(0.05, keep));
                // rivers carry what was worn off to the seas next door
                double carried = wornKm + 1e-9 * years * wx.Rain[i];
                int seas = 0; foreach (int j in life.Neighbours(i)) if (life.RegionSea(j) > 0.6) seas++;
                if (seas > 0) foreach (int j in life.Neighbours(i)) if (life.RegionSea(j) > 0.6) { MudM[j] += carried * 1000 / seas; RiverMud[j] = Math.Min(0.3, RiverMud[j] + wx.Rain[i] * 0.05); }
            }
            // the rock cycle: deep sediment is buried and pressed into stone
            for (int i = 0; i < n; i++)
                if (MudM[i] > 1000) { double press = (MudM[i] - 1000) * (1 - Math.Exp(-years / 1e7)); MudM[i] -= press; StoneKm3 += press * 1e-3 * 5e5; }
        }

        /// <summary>How tall a region's mountains stand now against how they began (0.2 to 3).</summary>
        public double Scale(int i) { return StartReliefKm[i] < 0.05 ? 1 : Math.Max(0.2, Math.Min(3, ReliefKm[i] / StartReliefKm[i])); }

        public string Describe(PlanetLife life)
        {
            double r = 0, s = 0; int land = 0;
            for (int i = 0; i < ReliefKm.Length; i++) if (life.RegionSea(i) <= 0.6) { r += ReliefKm[i]; s += SoilM[i]; land++; }
            if (land == 0) return "no dry land";
            return "mountains " + (r / land).ToString("0.0") + " km on average, soil " + (s / land * 100).ToString("0") + " cm" + (StoneKm3 > 0 ? ", " + StoneKm3.ToString("0.#e0") + " km³ of new stone" : "");
        }
    }
}
