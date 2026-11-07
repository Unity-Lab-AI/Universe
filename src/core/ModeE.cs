// DNA-AND · core · mode-E N³: the endless-detail generator, ported from the lab's own engine (Deviant 3D src/field/fractalize.ts,
// the signed-off mode E of the Uni Vs Matics archive). Where a field runs out of stored detail (past its finest wavelet
// level), mode-E grows NEW self-similar detail of the same character, forever, with no bottom-out:
//   1. Newton–Raphson on z³ − K (Cayley's fractal) to convergence,
//   2. then the 3D cubic continuation: Newton-projection of (x, y, z) onto x³ − 2y³ − z³ = K (∇g = (3x², −6y², −3z²)),
//      z seeded by sine/cosine navigators,
//   3. textured by multi-octave cellular (Worley) noise on a decimal lattice, octaves stepping by √10 (detail per zoom decade),
//   4. coloured by which cube root of K the point lands nearest.
// K bias and plane rotation come from the field's own character, so the growth carries it.
using System;

namespace DnaAnd.Core
{
    internal static class ModeE
    {
        /// <summary>Where and how deep to look, and the field's own character.</summary>
        public struct View
        {
            public double Cx, Cy, Span;     // window centre and half-extent, in equation space
            public double Seed;             // 0..1 (angular entropy of the field's gradients)
            public double Zoom;             // zoom factor (drives the cellular octave frequency)
            public double KBias;            // 0..1 (gradient-collapse magnitude)
            public double Ang;              // plane rotation, radians (gradient-collapse angle)

            /// <summary>A view of a field at a zoom, from the field's own character (as Deviant 3D's detailView): the window
            /// shrinks as you zoom in and the cellular octaves rise, so detail keeps subdividing without repeating.</summary>
            public static View For(double zoom, double entropy, double collapse, double angle, double focusX = 0.5, double focusY = 0.5)
            {
                double z = Math.Max(1, zoom);
                return new View { Cx = (focusX - 0.5) * 2, Cy = (focusY - 0.5) * 2, Span = 3.0 / z, Seed = Math.Min(1, Math.Max(0, entropy)), Zoom = z,
                    KBias = Math.Min(1, Math.Max(0, collapse)), Ang = angle };
            }
        }

        /// <summary>One point's result: how bright the detail is there (0..1 and up), and which of the three basins it fell in.</summary>
        public struct Point { public double Shade; public int Basin; }

        private static double Hash(double x, double y) { double h = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453; return h - Math.Floor(h); }

        // cellular (Worley) distance on a wrapped decimal lattice: cells subdivide as the frequency rises
        private static double CellDist(double x, double y, double freq, double basinSeed)
        {
            double sx = x * freq, sy = y * freq, gx = Math.Floor(sx), gy = Math.Floor(sy), d1 = 9;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    double cgx = (((gx + ox) % 1000) + 1000) % 1000, cgy = (((gy + oy) % 1000) + 1000) % 1000;
                    double fx = Hash(cgx + basinSeed, cgy), fy = Hash(cgx, cgy + basinSeed + 19.3);
                    double dx = gx + ox + fx - sx, dy = gy + oy + fy - sy, dd = dx * dx + dy * dy;
                    if (dd < d1) d1 = dd;
                }
            return Math.Min(1, Math.Sqrt(d1));
        }

        // Newton on z³ − K to convergence, then on into the 3D cubic x³ − 2y³ − z³ = K from that point
        private static void N3(double X, double Y, double K, out int its, out double zrOut, out double ziOut)
        {
            double zr = X, zi = Y; int it = 0;
            for (; it < 16; it++)
            {
                double r2 = zr * zr - zi * zi, i2 = 2 * zr * zi, r3 = r2 * zr - i2 * zi, i3 = r2 * zi + i2 * zr;
                double fr = r3 - K, fi = i3, dr = 3 * r2, di = 3 * i2, den = dr * dr + di * di + 1e-9;
                double qr = (fr * dr + fi * di) / den, qi = (fi * dr - fr * di) / den;
                zr -= qr; zi -= qi;
                if (qr * qr + qi * qi < 1e-6) break;
            }
            double x = zr, y = zi, z = 0.6 * Math.Sin(zr * 1.7 + zi * 1.3) + 0.4 * Math.Cos(zr * 2.1 - zi * 0.9);
            int it2 = 0;
            for (; it2 < 14; it2++)
            {
                double r = x * x * x - 2 * y * y * y - z * z * z - K;
                double gx = 3 * x * x, gy = -6 * y * y, gz = -3 * z * z, den = gx * gx + gy * gy + gz * gz + 1e-9, s = r / den;
                x -= s * gx; y -= s * gy; z -= s * gz;
                if (r * r < 1e-12) break;
            }
            its = it + it2; zrOut = x; ziOut = y;
        }

        /// <summary>The detail at one point of equation space (X0, Y0) for a view: exactly what fractalize.ts gives for that pixel.</summary>
        public static Point At(double X0, double Y0, View v)
        {
            double Zm = Math.Max(1, v.Zoom), K = 3 + v.KBias * 2.4, ca = Math.Cos(v.Ang), sa = Math.Sin(v.Ang);
            double wf = 1.3 + Math.Log(Math.Max(1, Zm), 2) * 0.05, ph = v.Seed * 6.2832;
            double X = X0 * ca - Y0 * sa, Y = X0 * sa + Y0 * ca;
            // domain warp along both axes: trajectories propagate, ever-expanding
            double Xw = X + 0.22 * Math.Sin(Y * wf + ph), Yw = Y + 0.22 * Math.Cos(X * wf - ph);
            Xw += 0.12 * Math.Sin(Yw * wf * 2.0); Yw += 0.12 * Math.Cos(Xw * wf * 2.0);
            int its; double zr, zi; N3(Xw, Yw, K, out its, out zr, out zi);
            // the basin: which cube root of K it reached
            double rr = Math.Pow(Math.Abs(K), 1.0 / 3) ; if (rr == 0) rr = 1;
            int bi = 0; double bd = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                double dx = zr - rr * Math.Cos(2 * Math.PI * k / 3), dy = zi - rr * Math.Sin(2 * Math.PI * k / 3), dd = dx * dx + dy * dy;
                if (dd < bd) { bd = dd; bi = k; }
            }
            // micro: multi-octave cellular detail, base-ten octave steps (×√10), subdividing with zoom forever
            double cellv = 0, amp = 1, f = Zm * 0.5, tot = 0;
            for (int oc = 0; oc < 5; oc++) { cellv += amp * CellDist(X0, Y0, f, bi); tot += amp; amp *= 0.5; f *= 3.16227766; }
            cellv /= tot;
            double edge = Math.Pow(cellv, 0.55);
            return new Point { Shade = (0.4 + 0.6 * (its / 24.0)) * (0.45 + 0.9 * edge), Basin = bi };
        }

        /// <summary>A size × size grid of detail for a view (as fractalize.ts's solveField, as a 0..1 grey field; rows on every core).</summary>
        public static double[] Solve(View v, int size)
        {
            var o = new double[size * size];
            Jobs.For(size, py =>
            {
                for (int px = 0; px < size; px++)
                {
                    double X0 = v.Cx + (px / (double)size - 0.5) * 2 * v.Span, Y0 = v.Cy + (py / (double)size - 0.5) * 2 * v.Span;
                    o[py * size + px] = Math.Min(1, At(X0, Y0, v).Shade * 1.05);
                }
            });
            return o;
        }
    }
}
