// Universe · gen · a world's temperature at any place, from its star's light and its own air: the star-only temperature from its
// star's light, its distance and its albedo; its air's greenhouse (a grey atmosphere's optical depth, content/terrain.unt
// optical-depth); and the spread by latitude from the year's sunshine there (North 1975).
using System;

namespace Universe.Gen
{
    internal static class Climate
    {
        /// <summary>The star-only (equilibrium) temperature, K: 278.6 K L^(1/4) (1 - A)^(1/4) / root(a), the star's light L in
        /// solar units, the distance a in AU, the albedo A (Earth: 254 K).</summary>
        public static double EquilibriumK(double lumSol, double au, double albedo)
        {
            return 278.6 * Math.Pow(Math.Max(1e-9, lumSol), 0.25) * Math.Pow(Math.Max(0, 1 - albedo), 0.25) / Math.Sqrt(Math.Max(1e-6, au));
        }
        /// <summary>Its ground's mean temperature under its air: T_e (1 + 0.75 tau)^(1/4) (Earth 255 K to 288 K).</summary>
        public static double MeanSurfaceK(double equilibriumK, double tau) { return equilibriumK * Math.Pow(1 + 0.75 * Math.Max(0, tau), 0.25); }
        /// <summary>Where a world's permanent ice begins, the sine of its latitude: where the year's mean falls to 263 K (-10 C), by the
        /// sunshine's spread (s = (263 / T)^4, sin^2 lat = (2 (1 - s) / 0.482 + 1) / 3: Earth about 60 degrees); 0 all ice, 1 none.</summary>
        public static double IceLineSin(double meanK)
        {
            if (meanK <= 0) return 1; double s = Math.Pow(263 / meanK, 4), x2 = (2 * (1 - s) / 0.482 + 1) / 3;
            return x2 <= 0 ? 0 : x2 >= 1 ? 1 : Math.Sqrt(x2);
        }
        /// <summary>Its ice ages: the sway of its temperature (K) with its orbit's cycles at a year (the eccentricity's about 100,000 and
        /// 405,000 years, the tilt's 41,000, the wobble's 23,000, each in its own phase by the world's seed), their sum swaying over
        /// Earth's measured range, about 6 K from the warm spells to the deepest cold.</summary>
        public static double IceAgeK(double year, ulong seed)
        {
            double p1 = (seed % 997) / 997.0 * 6.2832, p2 = (seed / 997 % 991) / 991.0 * 6.2832, p3 = (seed / 7919 % 983) / 983.0 * 6.2832, p4 = (seed / 104729 % 977) / 977.0 * 6.2832;
            double w = 0.4 * Math.Sin(6.2832 * year / 1.0e5 + p1) + 0.15 * Math.Sin(6.2832 * year / 4.05e5 + p2) + 0.3 * Math.Sin(6.2832 * year / 4.1e4 + p3) + 0.15 * Math.Sin(6.2832 * year / 2.3e4 + p4);
            return 3 * w;                                                                // (a sum of four sines of weights adding to one: from -3 to +3 K, six from the warmest to the coldest)
        }
        /// <summary>The year's mean temperature at a latitude (radians): the sunshine there against the world's mean (North 1975:
        /// 1 - 0.482 P2(sin lat)) to the quarter power. Checked against Earth's measured yearly means: about 300 K at the equator and
        /// 240 K over the poles, as this gives (303 K and 243 K from its 288 K mean).</summary>
        public static double SurfaceK(double meanK, double latRad, bool hasAir)
        {
            double x = Math.Sin(latRad), s = 1 - 0.482 * (3 * x * x - 1) / 2;
            return meanK * Math.Pow(Math.Max(0.05, s), 0.25);
        }
    }
}
