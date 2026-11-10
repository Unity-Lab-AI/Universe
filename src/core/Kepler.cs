// Universe · core · a body's path under one mass's pull from where it is and how fast it moves, after any time: bound (an ellipse) or
// flying free (a hyperbola) alike, by the universal variable (Curtis, Orbital Mechanics for Engineering Students, algorithms 3.3 and 3.4:
// Stumpff's functions, Newton's method on the universal Kepler equation, the Lagrange coefficients f and g).
using System;

namespace Universe.Core
{
    internal static class Kepler
    {
        private static double C(double z) { return z > 1e-8 ? (1 - Math.Cos(Math.Sqrt(z))) / z : z < -1e-8 ? (Math.Cosh(Math.Sqrt(-z)) - 1) / -z : 0.5 - z / 24; }
        private static double S(double z) { if (z > 1e-8) { double s = Math.Sqrt(z); return (s - Math.Sin(s)) / (s * s * s); } if (z < -1e-8) { double s = Math.Sqrt(-z); return (Math.Sinh(s) - s) / (s * s * s); } return 1.0 / 6 - z / 120; }
        /// <summary>Carries a state (place r, velocity v) on under a pull mu (G M) for a time dt: the new place and velocity.</summary>
        public static void Propagate(double mu, double rx, double ry, double rz, double vx, double vy, double vz, double dt,
            out double nx, out double ny, out double nz, out double nvx, out double nvy, out double nvz)
        {
            double r0 = Math.Sqrt(rx * rx + ry * ry + rz * rz), v2 = vx * vx + vy * vy + vz * vz, vr0 = (rx * vx + ry * vy + rz * vz) / Math.Max(1e-300, r0);
            double alpha = 2 / r0 - v2 / mu, smu = Math.Sqrt(mu);
            // Newton on the universal Kepler equation for chi
            double chi = smu * Math.Abs(alpha) * dt; if (alpha <= 0 || chi == 0) chi = smu * dt / Math.Max(1e-300, r0);
            for (int it = 0; it < 60; it++)
            {
                double z = alpha * chi * chi, c = C(z), s = S(z);
                double F = r0 * vr0 / smu * chi * chi * c + (1 - alpha * r0) * chi * chi * chi * s + r0 * chi - smu * dt;
                double dF = r0 * vr0 / smu * chi * (1 - alpha * chi * chi * s) + (1 - alpha * r0) * chi * chi * c + r0;
                double step = F / dF; chi -= step; if (Math.Abs(step) < 1e-12 * Math.Max(1, Math.Abs(chi))) break;
            }
            double zz = alpha * chi * chi, cc = C(zz), ss = S(zz);
            double f = 1 - chi * chi / r0 * cc, g = dt - chi * chi * chi / smu * ss;
            nx = f * rx + g * vx; ny = f * ry + g * vy; nz = f * rz + g * vz; double r = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double fd = smu / (r * r0) * (alpha * chi * chi * chi * ss - chi), gd = 1 - chi * chi / r * cc;
            nvx = fd * rx + gd * vx; nvy = fd * ry + gd * vy; nvz = fd * rz + gd * vz;
        }
    }
}
