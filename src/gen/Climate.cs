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
