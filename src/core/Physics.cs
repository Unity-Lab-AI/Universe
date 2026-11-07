// DNA-AND · core · the laws of nature the universe is made by, read from content/physics.dnat (none written in code):
// gravity's pull, the speed of light, and what follows from them (a black hole's horizon, an escape speed).
using System;

namespace DnaAnd.Core
{
    internal static class Physics
    {
        /// <summary>G times the Sun's mass (m³/s²): gravity's pull of one Sun.</summary>
        public static double GmSun = 1.32712e20;
        /// <summary>The speed of light (m/s).</summary>
        public static double C = 2.998e8;
        /// <summary>Gravity at Earth's surface (m/s²), to compare a pull with.</summary>
        public static double EarthG = 9.81;
        private static bool _loaded;

        public static void Load(ContentLibrary content)
        {
            if (_loaded || content == null) return; _loaded = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var nf = System.Globalization.NumberStyles.Float; double v;
            foreach (var e in content.OfKind("physics.dnat", "law"))
            {
                if (e.Id == "gravity")
                {
                    if (double.TryParse(e.Get("gm-sun-m3-s2", ""), nf, ci, out v)) GmSun = v;
                    if (double.TryParse(e.Get("earth-g-m-s2", ""), nf, ci, out v)) EarthG = v;
                }
                if (e.Id == "light" && double.TryParse(e.Get("speed-m-s", ""), nf, ci, out v)) C = v;
            }
        }

        /// <summary>A black hole's event horizon (the Schwarzschild radius, 2GM/c²) in km, for a mass in Suns.</summary>
        public static double HorizonKm(double massSol) { return 2 * GmSun * massSol / (C * C) / 1000; }
        /// <summary>Gravity's pull (m/s²) at a distance in metres from a mass in Suns (Newton: falling with the distance squared).</summary>
        public static double Pull(double massSol, double metres) { return GmSun * massSol / (metres * metres); }
        /// <summary>The speed needed to get away (m/s) at a distance in metres from a mass in Suns.</summary>
        public static double Escape(double massSol, double metres) { return Math.Sqrt(2 * GmSun * massSol / metres); }
    }
}
