// Universe · core · the laws of nature the universe is made by, read from content/physics.unt (none written in code):
// gravity's pull, the speed of light, and what follows from them (a black hole's horizon, an escape speed).
using System;

namespace Universe.Core
{
    internal static class Physics
    {
        /// <summary>G times one solar mass (m³/s²): the pull of a star of one solar mass.</summary>
        public static double GmSol = 1.32712e20;
        /// <summary>G itself (m³ kg⁻¹ s⁻²), for the pull of a world whose mass is known in kilograms.</summary>
        public static double G = 6.674e-11;
        /// <summary>The speed of light (m/s).</summary>
        public static double C = 2.998e8;
        /// <summary>Gravity at Earth's surface (m/s²), to compare a pull with.</summary>
        public static double EarthG = 9.81;
        private static bool _loaded;

        public static void Load(ContentLibrary content)
        {
            if (_loaded || content == null) return; _loaded = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture; var nf = System.Globalization.NumberStyles.Float; double v;
            foreach (var e in content.OfKind("physics.unt", "law"))
            {
                if (e.Id == "gravity")
                {
                    if (double.TryParse(e.Get("gm-sol-m3-s2", ""), nf, ci, out v)) GmSol = v;
                    if (double.TryParse(e.Get("g-m3-kg-s2", ""), nf, ci, out v)) G = v;
                    if (double.TryParse(e.Get("earth-g-m-s2", ""), nf, ci, out v)) EarthG = v;
                }
                if (e.Id == "light" && double.TryParse(e.Get("speed-m-s", ""), nf, ci, out v)) C = v;
            }
        }

        /// <summary>A black hole's event horizon (the Schwarzschild radius, 2GM/c²) in km, for a mass in solar masses.</summary>
        public static double HorizonKm(double massSol) { return 2 * GmSol * massSol / (C * C) / 1000; }
        /// <summary>Gravity's pull (m/s²) at a distance in metres from a mass in solar masses (Newton: falling with the distance squared).</summary>
        public static double Pull(double massSol, double metres) { return GmSol * massSol / (metres * metres); }
        /// <summary>The speed needed to get away (m/s) at a distance in metres from a mass in solar masses.</summary>
        public static double Escape(double massSol, double metres) { return Math.Sqrt(2 * GmSol * massSol / metres); }
    }
}
