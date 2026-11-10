// Universe · gen · the small streams round where you are: a fine drainage map of the ground in view only, at the detail the view
// needs (render by scale: any one view is easily made), laid flat on the world round you (a gnomonic grid, 256 by 256, its span
// forty times your height over the ground), each point's height from the world's own ground at that fineness; every hollow filled
// to its spill (a priority flood, the map's edges and the sea its outlets), each point draining to its lowest neighbour of eight,
// the rain gathered down every path; a stream where the ground drained passes about half a square kilometre (where real channels
// begin), as wide as 0.002 km times the root of it, as the world's great rivers are.
using System;
using System.Collections.Generic;

namespace Universe.Gen
{
    internal sealed class LocalDrainage
    {
        public const int G = 256;
        private readonly double _cx, _cy, _cz, _ex, _ey, _ez, _nx, _ny, _nz, _rKm, _d;
        private readonly double[] _h, _fill, _acc; private readonly int[] _to;
        /// <summary>The map's span (km) and where its middle is (a unit direction from the world's middle).</summary>
        public readonly double SpanKm; public readonly double Cx, Cy, Cz;

        public LocalDrainage(double cx, double cy, double cz, double spanKm, double radiusKm, Func<double, double, double, int, double> height)
        {
            double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz); _cx = cx / cl; _cy = cy / cl; _cz = cz / cl; Cx = _cx; Cy = _cy; Cz = _cz;
            // the ground's own east and north here
            double ax = Math.Abs(_cy) < 0.99 ? 0 : 1, ay = Math.Abs(_cy) < 0.99 ? 1 : 0, az = 0;
            _ex = ay * _cz - az * _cy; _ey = az * _cx - ax * _cz; _ez = ax * _cy - ay * _cx; double el = Math.Sqrt(_ex * _ex + _ey * _ey + _ez * _ez); _ex /= el; _ey /= el; _ez /= el;
            _nx = _cy * _ez - _cz * _ey; _ny = _cz * _ex - _cx * _ez; _nz = _cx * _ey - _cy * _ex;
            _rKm = radiusKm; SpanKm = spanKm; _d = spanKm / (G - 1);
            int n = G * G; _h = new double[n]; _fill = new double[n]; _acc = new double[n]; _to = new int[n];
            // the ground's detail for this spacing: the world's map has six layers at about 110 km; each halving of the spacing one more
            int layers = (int)Math.Max(6, Math.Min(22, 6 + Math.Log(110 / Math.Max(1e-6, _d), 2)));
            for (int j = 0; j < G; j++)
                for (int i = 0; i < G; i++)
                {
                    double x, y, z; Dir(i, j, out x, out y, out z); _h[j * G + i] = height(x, y, z, layers);
                }
            // the priority flood from the edges and the sea inward
            var heap = new SortedSet<KeyValuePair<double, int>>(Comparer<KeyValuePair<double, int>>.Create((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Value.CompareTo(q.Value)));
            var done = new bool[n];
            for (int k = 0; k < n; k++)
            {
                _to[k] = -1; int i = k % G, j = k / G;
                if (i == 0 || j == 0 || i == G - 1 || j == G - 1 || _h[k] <= 0) { _fill[k] = _h[k]; done[k] = true; heap.Add(new KeyValuePair<double, int>(_h[k], k)); }
            }
            var order = new List<int>(n);
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top); int c = top.Value; order.Add(c); int ci = c % G, cj = c / G;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue; int ni = ci + di, nj = cj + dj; if (ni < 0 || nj < 0 || ni >= G || nj >= G) continue;
                        int k = nj * G + ni; if (done[k]) continue; done[k] = true;
                        _fill[k] = Math.Max(_h[k], _fill[c] + 1e-9); _to[k] = c; heap.Add(new KeyValuePair<double, int>(_fill[k], k));
                    }
            }
            for (int k = 0; k < n; k++) _acc[k] = _h[k] > 0 ? 1 : 0;
            for (int q = order.Count - 1; q >= 0; q--) { int c = order[q]; if (_to[c] >= 0 && _h[c] > 0) _acc[_to[c]] += _acc[c]; }
        }

        private void Dir(double i, double j, out double x, out double y, out double z)
        {
            double u = (i - (G - 1) * 0.5) * _d / _rKm, v = (j - (G - 1) * 0.5) * _d / _rKm;
            x = _cx + _ex * u + _nx * v; y = _cy + _ey * u + _ny * v; z = _cz + _ez * u + _nz * v; double l = Math.Sqrt(x * x + y * y + z * z); x /= l; y /= l; z /= l;
        }
        // where a place falls on the map (grid units), false off it
        private bool Grid(double x, double y, double z, out double fi, out double fj)
        {
            double pc = x * _cx + y * _cy + z * _cz; fi = fj = 0; if (pc <= 0) return false;
            fi = (x * _ex + y * _ey + z * _ez) / pc * _rKm / _d + (G - 1) * 0.5; fj = (x * _nx + y * _ny + z * _nz) / pc * _rKm / _d + (G - 1) * 0.5;
            return fi >= 1 && fj >= 1 && fi <= G - 2 && fj <= G - 2;
        }
        /// <summary>A stream at a place (0 none, 1 its middle, -1 its banks), and a pond's water level if it is in one (else NaN);
        /// false off the map.</summary>
        public bool StreamAt(double x, double y, double z, double groundH, out double stream, out double pondLevel)
        {
            stream = 0; pondLevel = double.NaN; double fi, fj; if (!Grid(x, y, z, out fi, out fj)) return false;
            int i0 = (int)Math.Round(fi), j0 = (int)Math.Round(fj), k0 = j0 * G + i0;
            if (_h[k0] > 0 && _fill[k0] > _h[k0] + 1e-4 && groundH < _fill[k0]) pondLevel = _fill[k0];
            double cellKm2 = _d * _d, least = Math.Max(8, 0.5 / cellKm2); bool bank = false;
            for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                {
                    int ai = i0 + di, aj = j0 + dj; if (ai < 0 || aj < 0 || ai >= G || aj >= G) continue; int a = aj * G + ai, b = _to[a];
                    if (b < 0 || _acc[a] < least || _h[a] <= 0) continue;
                    double bi = b % G, bj = b / G, ex = bi - ai, ey = bj - aj, el = ex * ex + ey * ey;
                    double t = Math.Max(0, Math.Min(1, ((fi - ai) * ex + (fj - aj) * ey) / el)), px = ai + ex * t - fi, py = aj + ey * t - fj;
                    double dist = Math.Sqrt(px * px + py * py) * _d, halfW = 0.001 * Math.Sqrt(_acc[a] * cellKm2);
                    if (dist < halfW) stream = Math.Max(stream, 1 - dist / halfW); else if (dist < halfW * 1.6) bank = true;
                }
            if (stream <= 0 && bank) stream = -1;
            return true;
        }
    }
}
