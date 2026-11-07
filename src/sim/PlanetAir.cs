// DNA-AND · simulation · a planet's air and climate, on the planet's own clock (content/physics.dnat: pressure, greenhouse,
// runaway greenhouse, air escaping). Gases are kept relative to Earth's air before industry (pressure 1 bar, carbon
// dioxide 280 parts per million = 1, oxygen 21%, methane 0.7 parts per million = 1).
// What changes the air: volcanoes breathe out carbon dioxide; rain on rock draws it back out, faster when it is warm and when
// plates keep making fresh rock (the slow carbon-silicate thermostat, over hundreds of thousands of years); plant-like life
// makes oxygen; early microbes make methane, which oxygen destroys; with no magnetic shield the star's wind strips the air,
// faster on small worlds. The surface temperature follows the greenhouse; too hot and the seas boil into a runaway
// greenhouse, too cold and the world freezes over.
using System;
using DnaAnd.Core;

namespace DnaAnd.Sim
{
    internal sealed class PlanetAir
    {
        public double Pressure = 1;      // bar
        public double CO2 = 10;          // × 280 ppm; a young world's air is rich in it
        public double O2 = 0;            // share of the air (Earth today 0.21)
        public double CH4 = 1;           // × 0.7 ppm
        public double BaseK;             // temperature with no greenhouse at all (Earth: 255 K), at the star's brightness today (4.6 billion years)
        public double StarNow = 1;       // the star's brightness now against today's (a young star is dimmer)
        public double DarkK;             // cooling from dust and soot after a great impact
        public double TempK;             // average surface temperature
        public bool Runaway, Snowball;
        public string Events = "";
        private double _age = -1;
        private double _iron = 2;        // iron in the seas and rock that soaks up oxygen first; the air only fills once it is used up
        private bool _wasOxygen, _wasRunaway, _wasSnowball, _wasThin;

        /// <param name="baseK">The planet's temperature from its star alone, with no greenhouse (Earth: 255 K).</param>
        public PlanetAir(double baseK) { BaseK = baseK; Work(); }

        private void Work()
        {
            // the greenhouse: Earth's whole greenhouse adds 33 K; carbon dioxide adds about 4 K per doubling, methane a little;
            // thicker air holds more heat, and water vapour boosts it once the seas are warm
            double warm = 33 * Math.Pow(Math.Max(0.001, Pressure), 0.35) + 4.0 * Math.Log(Math.Max(1e-4, CO2), 2) + 0.6 * Math.Log(Math.Max(1e-3, CH4), 2);
            if (Runaway) warm += 400;
            TempK = BaseK * Math.Pow(StarNow, 0.25) + warm - DarkK;
        }

        /// <summary>Runs the air to the planet's new age. <paramref name="producers"/>: plant-like life on the planet (crowds),
        /// <paramref name="microbes"/>: early microbes (crowds).</summary>
        public void Advance(PlanetCore core, double producers, double microbes, bool hasWater)
        {
            var ev = new System.Text.StringBuilder();
            if (_age < 0) { _age = core.Age; Work(); return; }
            double years = core.Age - _age; _age = core.Age;
            if (years <= 0) return;
            // the star brightens as it ages: about 30% dimmer when young than at 4.6 billion years, brighter after
            StarNow = 1 / (1 + 0.4 * (1 - core.Age / 4.6e9));
            // carbon dioxide: in from volcanoes, out by rain on rock (needs liquid water; faster when warm and when plates make new rock)
            double outgas = 1e-6 * core.Volcanism;
            if (hasWater && !Snowball && !Runaway)
            {
                // the level where rain on rock takes out exactly what volcanoes put in; the air settles toward it over
                // about 400,000 years, smoothly for any length of step
                double eq = Balance(outgas, core.Plates);
                CO2 = eq + (CO2 - eq) * Math.Exp(-years / 4e5);
            }
            else CO2 = Step(CO2, outgas * years, 1e-4, 3e5);               // no liquid water to take it out: it only builds up
            // oxygen: made by plant-like life, used up by rock and volcano gases; it only builds once the sinks are filled
            // made by plant-like life (levelling off once there is a lot of it); used up by rot, breathing and fire in proportion to
            // how much there is, and by volcano gases; so it settles near Earth's 21% on a world full of plants
            double make = 2.2e-9 * producers / (producers + 2000) * years, sink = (1e-8 * O2 + 1e-11 * core.Volcanism) * years;
            double rust = Math.Min(_iron, make); _iron -= rust; make -= rust;                 // banded iron: the seas rust before the air fills
            O2 = Math.Max(0, Math.Min(0.35, O2 + make - Math.Min(sink, O2 + make)));
            // methane: made by early microbes, destroyed quickly where there is oxygen
            double target = 1 + microbes * 0.02 / (1 + O2 * 400);
            CH4 = target + (CH4 - target) * Math.Exp(-years / 1e4);
            // air lost to space: very slow behind a magnetic shield, fast without one, faster on small worlds
            double loss = (core.Field > 0.05 ? 1e-12 : 1.5e-9) / Math.Max(0.1, core.Size * core.Size);
            Pressure *= Math.Exp(-loss * years);
            // the seas: boiling into a runaway greenhouse when too hot, freezing over when too cold
            Work();
            if (hasWater && !Runaway && TempK > 340) Runaway = true;
            Snowball = hasWater && !Runaway && TempK < 250;
            Work();
            bool oxy = O2 > 0.01, thin = Pressure < 0.05;
            if (oxy && !_wasOxygen) Add(ev, "the great oxidation: oxygen is filling the air");
            if (Runaway && !_wasRunaway) Add(ev, "a runaway greenhouse: the seas are boiling away");
            if (Snowball && !_wasSnowball) Add(ev, "the world has frozen over into a snowball");
            if (!Snowball && _wasSnowball) Add(ev, "the snowball has thawed");
            if (thin && !_wasThin) Add(ev, "the air has thinned almost to nothing");
            _wasOxygen = oxy; _wasRunaway = Runaway; _wasSnowball = Snowball; _wasThin = thin;
            Events = ev.ToString();
        }

        // the carbon dioxide level where weathering equals outgassing (weathering grows with the gas and with warmth)
        private double Balance(double outgas, bool plates)
        {
            double lo = Math.Log(1e-4), hi = Math.Log(3e5), keep = CO2;
            for (int k = 0; k < 40; k++)
            {
                double mid = (lo + hi) / 2; CO2 = Math.Exp(mid); Work();
                double weather = 1e-6 * Math.Pow(CO2, 0.3) * Math.Exp(Math.Min(30, (TempK - 288) / 13.7)) * (plates ? 1 : 0.3);
                if (weather > outgas) hi = mid; else lo = mid;
            }
            double eq = Math.Exp((lo + hi) / 2); CO2 = keep; Work();
            return eq;
        }

        // a gas amount changed by a rate, kept inside sensible limits
        private static double Step(double v, double change, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v + change)); }
        private static void Add(System.Text.StringBuilder b, string s) { if (b.Length > 0) b.Append("; "); b.Append(s); }

        public string Describe()
        {
            return "air " + Pressure.ToString("0.00") + " bar, carbon dioxide " + (CO2 * 280).ToString("0") + " ppm, oxygen " + (O2 * 100).ToString("0.0") + "%, methane " + (CH4 * 0.7).ToString("0.0") + " ppm, " +
                   (TempK - 273.15).ToString("0") + " °C, star " + (StarNow * 100).ToString("0") + "% of today's" + (Runaway ? " (runaway greenhouse)" : Snowball ? " (frozen over)" : "");
        }

        public Chunk Save()
        {
            var ms = new System.IO.MemoryStream(); var o = new System.IO.BinaryWriter(ms);
            o.Write(Pressure); o.Write(CO2); o.Write(O2); o.Write(CH4); o.Write(BaseK); o.Write(Runaway); o.Write(_age); o.Write(_iron);
            o.Flush(); return new Chunk { Name = "AIR ", Version = 1, Data = ms.ToArray() };
        }

        public void Load(Chunk c)
        {
            if (c.Name != "AIR " || c.Version > 1) throw new System.IO.InvalidDataException("not an air chunk this game can read");
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(c.Data));
            Pressure = r.ReadDouble(); CO2 = r.ReadDouble(); O2 = r.ReadDouble(); CH4 = r.ReadDouble(); BaseK = r.ReadDouble(); Runaway = r.ReadBoolean(); _age = r.ReadDouble(); _iron = r.ReadDouble();
            Work();
        }
    }
}
