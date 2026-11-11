// Universe · shell · the universe view, its part: coordinates by scale (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using Universe.Gen;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── where a place is, in the frame of the smallest thing it is in: on a world its latitude, longitude and height; in a
        // star's system its distance from the star in AU and its longitude and latitude against the planets' plane; in a galaxy
        // its distance from the galaxy's heart in kiloparsecs and its longitude and latitude against the galaxy's disc; else the
        // universe's own mesh (its 0,0,0 the universe's) in megaparsecs. The place is given from the anchor (the anchor plus an
        // offset), as every place is kept, so it is exact at every scale ──
        private const double LyPerKpc = 3261.56;

        /// <summary>The coordinate line for a place <paramref name="rx"/>, <paramref name="ry"/>, <paramref name="rz"/> from the
        /// anchor (universe units).</summary>
        private string CoordWords(double rx, double ry, double rz)
        {
            double k = Units.Km(1);
            // on (or over) the world whose ground is made: from its centre
            if (_ground != null)
            {
                double x = (rx - _px) / k, y = (ry - _py) / k, z = (rz - _pz) / k, r = Math.Sqrt(x * x + y * y + z * z);
                if (r < _ground.Radius * 40 && r > 0)
                {
                    double h = r - _ground.Radius;
                    return "on " + GroundKind() + ": " + LatLong(y / r, Math.Atan2(z, x)) + ", " + (h >= 0 ? ScaleHud.Words(h) + " up" : ScaleHud.Words(-h) + " down");
                }
            }
            // in a star's system: from the star, against its planets' plane
            if (_system != null)
            {
                double r = Math.Sqrt(rx * rx + ry * ry + rz * rz), au = r / Units.Au(1), reach = Math.Max(2000, _system.OortOutAu);
                if (au < reach && r > 0) return "in its system: " + Num(au) + " AU from the star, " + LatLong(ry / r, Math.Atan2(rz, rx));
            }
            // in a galaxy: from its heart, against its disc
            if (_near >= 0 && _near < _galaxies.Count)
            {
                var g = _galaxies[_near]; double gx, gy, gz; ToGalaxy(g, _ax + rx, _ay + ry, _az + rz, out gx, out gy, out gz);
                double s = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                if (s < 3 && s > 0)
                {
                    double kpc = s * g.Scale * Units.LightYearsPerUnit / LyPerKpc;
                    return "in " + g.Name + ": " + Num(kpc) + " kpc from its heart, " + LatLong(gy / s, Math.Atan2(gz, gx));
                }
            }
            // the universe's mesh
            double m = Units.LightYearsPerUnit / LyPerKpc / 1000;
            return "in the universe: " + Num((_ax + rx) * m) + ", " + Num((_ay + ry) * m) + ", " + Num((_az + rz) * m) + " Mpc";
        }

        /// <summary>The camera's coordinate line.</summary>
        private string CameraCoords() { return CoordWords(_ox, _oy, _oz); }

        private static string LatLong(double sinLat, double lon)
        {
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, sinLat))) * 180 / Math.PI, lo = lon * 180 / Math.PI;
            return Math.Abs(lat).ToString("0.###") + (lat >= 0 ? "° N, " : "° S, ") + Math.Abs(lo).ToString("0.###") + (lo >= 0 ? "° E" : "° W");
        }
        private static string Num(double v) { double a = Math.Abs(v); return a >= 1000 ? v.ToString("N0") : a >= 10 ? v.ToString("0.##") : a >= 0.01 ? v.ToString("0.####") : v.ToString("0.###E+0"); }
    }
}
