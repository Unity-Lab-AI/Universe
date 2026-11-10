// Universe · gen · where a world's water goes: its rain running downhill to the sea, made once for the world. Points spread evenly
// over its sphere (a Fibonacci lattice), each with its height from the world's own ground; every basin filled to the height it
// spills at (a priority flood: what fills is a lake); each point draining to the neighbour its water runs to; the rain gathered
// down every path (each point its share of rain), so the rivers are where the gathered water is, as wide as the root of it
// (hydraulic geometry: width as the square root of the flow).
using System;
using System.Collections.Generic;

namespace Universe.Gen
{
    internal sealed class Drainage
    {
        private readonly int _n; private readonly double[] _x, _y, _z, _h, _fill, _acc; private readonly int[] _to; private readonly int[][] _nb;
        private readonly List<int>[] _bucket; private const int BLat = 180, BLon = 360;
        /// <summary>The point spacing (as a share of the radius) and the least gathered rain (points) that makes a river.</summary>
        public readonly double Spacing; public const double RiverFrom = 12;

        public Drainage(Func<double, double, double, double> height, int n = 40000)
        {
            _n = n; _x = new double[n]; _y = new double[n]; _z = new double[n]; _h = new double[n]; _fill = new double[n]; _acc = new double[n]; _to = new int[n]; _nb = new int[n][];
            _bucket = new List<int>[BLat * BLon]; Spacing = Math.Sqrt(4 * Math.PI / n);
            double ga = Math.PI * (3 - Math.Sqrt(5));
            for (int i = 0; i < n; i++)
            {
                double y = 1 - (i + 0.5) * 2 / n, r = Math.Sqrt(1 - y * y), a = ga * i;
                _x[i] = Math.Cos(a) * r; _y[i] = y; _z[i] = Math.Sin(a) * r; _h[i] = height(_x[i], _y[i], _z[i]);
                int bi = Bucket(_x[i], _y[i], _z[i]); (_bucket[bi] ?? (_bucket[bi] = new List<int>())).Add(i);
            }
            // each point's neighbours: the six nearest
            for (int i = 0; i < n; i++)
            {
                var near = new List<KeyValuePair<double, int>>();
                foreach (int j in Around(_x[i], _y[i], _z[i], 2)) { if (j == i) continue; double d = Dist2(i, _x[j], _y[j], _z[j]); near.Add(new KeyValuePair<double, int>(d, j)); }
                near.Sort((p, q) => p.Key.CompareTo(q.Key)); int k = Math.Min(6, near.Count); _nb[i] = new int[k]; for (int q = 0; q < k; q++) _nb[i][q] = near[q].Value;
            }
            // the priority flood: from the sea (height under 0) inward, lowest first; each point reached is filled to at least the height
            // it was reached from, and drains to the point it was reached from (where no sea: from the lowest point)
            var heap = new SortedSet<KeyValuePair<double, int>>(Comparer<KeyValuePair<double, int>>.Create((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Value.CompareTo(q.Value)));
            var done = new bool[n]; int low = 0;
            for (int i = 0; i < n; i++) { _to[i] = -1; if (_h[i] < _h[low]) low = i; if (_h[i] <= 0) { _fill[i] = _h[i]; done[i] = true; heap.Add(new KeyValuePair<double, int>(_h[i], i)); } }
            if (heap.Count == 0) { _fill[low] = _h[low]; done[low] = true; heap.Add(new KeyValuePair<double, int>(_h[low], low)); }
            var order = new List<int>(n);
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top); int c = top.Value; order.Add(c);
                foreach (int j in _nb[c])
                {
                    if (done[j]) continue; done[j] = true;
                    _fill[j] = Math.Max(_h[j], _fill[c] + 1e-7); _to[j] = c;                    // (a basin's floor filled up to its spill, a whisker over it so the water still runs out)
                    heap.Add(new KeyValuePair<double, int>(_fill[j], j));
                }
            }
            // the rain gathered: each land point its share, passed down its path, from the highest down
            for (int i = 0; i < n; i++) _acc[i] = _h[i] > 0 ? 1 : 0;
            for (int q = order.Count - 1; q >= 0; q--) { int c = order[q]; if (_to[c] >= 0 && _h[c] > 0) _acc[_to[c]] += _acc[c]; }
        }

        private static int Bucket(double x, double y, double z)
        {
            int la = (int)((Math.Asin(Math.Max(-1, Math.Min(1, y))) / Math.PI + 0.5) * BLat), lo = (int)((Math.Atan2(z, x) / (2 * Math.PI) + 0.5) * BLon);
            la = Math.Max(0, Math.Min(BLat - 1, la)); lo = ((lo % BLon) + BLon) % BLon; return la * BLon + lo;
        }
        private IEnumerable<int> Around(double x, double y, double z, int reach)
        {
            int la = (int)((Math.Asin(Math.Max(-1, Math.Min(1, y))) / Math.PI + 0.5) * BLat), lo = (int)((Math.Atan2(z, x) / (2 * Math.PI) + 0.5) * BLon);
            int lr = Math.Abs(y) > 0.97 ? BLon : reach * (int)Math.Ceiling(1 / Math.Max(0.05, Math.Sqrt(1 - y * y)));   // (the longitudes crowd toward the poles)
            for (int a = Math.Max(0, la - reach); a <= Math.Min(BLat - 1, la + reach); a++)
                for (int b = lo - lr; b <= lo + lr; b++)
                {
                    var l = _bucket[a * BLon + (((b % BLon) + BLon) % BLon)]; if (l == null) continue;
                    foreach (int j in l) yield return j;
                }
        }
        private double Dist2(int i, double x, double y, double z) { double dx = _x[i] - x, dy = _y[i] - y, dz = _z[i] - z; return dx * dx + dy * dy + dz * dz; }

        /// <summary>At a place on the world (a unit direction) and its ground height there: how much a river is there (0 none, 1 its
        /// middle; -1 on its banks), and if it is in a lake, the lake's water level (else NaN).</summary>
        public double RiverAt(double x, double y, double z, double groundH, double radiusKm, out double lakeLevel)
        {
            lakeLevel = double.NaN; int best = -1; double bd = double.MaxValue;
            foreach (int j in Around(x, y, z, 1)) { double d = Dist2(j, x, y, z); if (d < bd) { bd = d; best = j; } }
            if (best < 0) return 0;
            if (_h[best] > 0 && _fill[best] > _h[best] + 0.002 && groundH < _fill[best]) lakeLevel = _fill[best];   // (in a filled basin, below its spill: lake water)
            double river = 0; bool bank = false;
            // the paths near: each point near here to the one it drains to, a river where enough rain has gathered, as wide as its root
            foreach (int a in Around(x, y, z, 1))
            {
                int b = _to[a]; if (b < 0 || _acc[a] < RiverFrom || _h[a] <= 0) continue;
                double ax = _x[a], ay = _y[a], az = _z[a], ex = _x[b] - ax, ey = _y[b] - ay, ez = _z[b] - az, el = ex * ex + ey * ey + ez * ez; if (el <= 0) continue;
                double t = Math.Max(0, Math.Min(1, ((x - ax) * ex + (y - ay) * ey + (z - az) * ez) / el)), px = ax + ex * t - x, py = ay + ey * t - y, pz = az + ez * t - z;
                double cellKm2 = 4 * Math.PI * radiusKm * radiusKm / _n, dist = Math.Sqrt(px * px + py * py + pz * pz) * radiusKm, halfW = 0.001 * Math.Sqrt(_acc[a] * cellKm2);   // (km: width about 0.002 km times the root of the basin in square km: the Mississippi 3.6 km, the Amazon 5)
                if (dist < halfW) river = Math.Max(river, 1 - dist / halfW); else if (dist < halfW * 1.6) bank = true;
            }
            return river > 0 ? river : bank ? -1 : 0;                                    // (-1: its banks, where its gravels and placers lie)
        }
    }
}
