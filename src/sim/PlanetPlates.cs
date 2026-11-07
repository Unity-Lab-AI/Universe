// Universe · simulation · a planet's plates, and the earthquakes and eruptions along their edges, on the planet's own clock.
// Each plate is the part of the surface nearest its middle point, and turns slowly about its own axis (a few centimetres
// a year, like Earth's). Where two plates meet, how they move against each other makes the edge spreading (ridges, rifts),
// crashing (trenches, mountain belts, the biggest quakes and explosive volcanoes) or sliding (faults).
// Sizes follow real records: quake counts fall tenfold per magnitude (about one magnitude 8 a year on Earth), and the biggest
// eruptions (VEI 8) come about once in 50,000 years. With no moving plates (a stagnant lid), only hot spots erupt.
using System;
using System.Collections.Generic;
using Universe.Core;

namespace Universe.Sim
{
    internal sealed class PlanetPlates
    {
        public sealed class Plate { public double X, Y, Z; public double Ax, Ay, Az; public double Rate; public bool Oceanic; }   // Rate: radians a year about axis A
        public sealed class Edge { public double X, Y, Z; public int A, B; public int Kind; public double Speed; }                   // Kind 0 spreading, 1 crashing, 2 sliding; Speed cm a year
        public sealed class Event { public double Age; public string What; public double X, Y, Z; public double Size; }

        public static readonly string[] EdgeKinds = { "spreading", "crashing", "sliding" };
        public readonly List<Plate> Plates = new List<Plate>();
        public readonly List<Edge> Edges = new List<Edge>();
        /// <summary>The latest earthquakes and eruptions, newest last (kept to the last 20).</summary>
        public readonly List<Event> Recent = new List<Event>();
        private readonly Rng _rng;
        private readonly double _radiusKm;
        private double _age = -1;

        public PlanetPlates(double radiusKm, ulong seed)
        {
            _radiusKm = radiusKm; _rng = new Rng(seed ^ 0x9A7EUL);
            int n = 8 + _rng.Below(8);                                         // Earth has about 15 big and small plates
            for (int i = 0; i < n; i++)
            {
                var c = RandomPoint(); var a = RandomPoint();
                double cmPerYear = 1 + _rng.NextDouble() * 9;                  // Earth's plates: about 1 to 10 cm a year
                Plates.Add(new Plate { X = c[0], Y = c[1], Z = c[2], Ax = a[0], Ay = a[1], Az = a[2], Rate = cmPerYear / (radiusKm * 1e5), Oceanic = _rng.NextDouble() < 0.6 });
            }
            // the edges: spots where the nearest two plates are about as near as each other
            for (int k = 0; k < 20000 && Edges.Count < 1500; k++)
            {
                var p = RandomPoint(); int a, b; double da, db; Nearest2(p, out a, out b, out da, out db);
                if (db - da > 0.02) continue;
                // how the two plates move against each other here, across the edge
                var va = Velocity(Plates[a], p); var vb = Velocity(Plates[b], p);
                double nx = Plates[b].X - Plates[a].X, ny = Plates[b].Y - Plates[a].Y, nz = Plates[b].Z - Plates[a].Z, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); nx /= nl; ny /= nl; nz /= nl;
                double rx = va[0] - vb[0], ry = va[1] - vb[1], rz = va[2] - vb[2];
                double closing = rx * nx + ry * ny + rz * nz, total = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                int kind = Math.Abs(closing) < total * 0.4 ? 2 : closing > 0 ? 1 : 0;
                Edges.Add(new Edge { X = p[0], Y = p[1], Z = p[2], A = a, B = b, Kind = kind, Speed = total * radiusKm * 1e5 });
            }
        }

        private double[] RandomPoint() { double u = _rng.NextDouble() * 2 - 1, t = _rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - u * u); return new[] { Math.Cos(t) * r, u, Math.Sin(t) * r }; }

        private static double[] Velocity(Plate pl, double[] p)
        {
            // a plate turning about its axis: the ground at p moves at axis × p
            return new[] { (pl.Ay * p[2] - pl.Az * p[1]) * pl.Rate, (pl.Az * p[0] - pl.Ax * p[2]) * pl.Rate, (pl.Ax * p[1] - pl.Ay * p[0]) * pl.Rate };
        }

        private void Nearest2(double[] p, out int a, out int b, out double da, out double db)
        {
            a = b = 0; da = db = double.MaxValue;
            for (int i = 0; i < Plates.Count; i++)
            {
                double d = 1 - (Plates[i].X * p[0] + Plates[i].Y * p[1] + Plates[i].Z * p[2]);
                if (d < da) { db = da; b = a; da = d; a = i; } else if (d < db) { db = d; b = i; }
            }
        }

        /// <summary>How long the plates have been moving (years since the game began), for drifting the ground.</summary>
        public double MovedYears;

        /// <summary>Where the ground at a spot came from: the spot turned back about its plate's axis by how far the plate has
        /// moved, and how near the edge of its plate it is (0 on the edge, 1 a few hundred km in) and the edge's kind.</summary>
        public void Back(double x, double y, double z, out double bx, out double by, out double bz, out double bx2, out double by2, out double bz2, out double share, out double edge, out int kind)
        {
            int a, b; double da, db; Nearest2(new[] { x, y, z }, out a, out b, out da, out db);
            Turn(Plates[b], x, y, z, out bx2, out by2, out bz2);
            double band = Math.Min(1, (db - da) / 0.006);                                // blend the two plates' ground across a narrow band
            share = 0.5 + 0.5 * band * band * (3 - 2 * band);
            var pl = Plates[a];
            double ang = -pl.Rate * MovedYears;                                      // turn back by the angle it has turned
            double c = Math.Cos(ang), s = Math.Sin(ang), k = pl.Ax * x + pl.Ay * y + pl.Az * z;
            bx = x * c + (pl.Ay * z - pl.Az * y) * s + pl.Ax * k * (1 - c);
            by = y * c + (pl.Az * x - pl.Ax * z) * s + pl.Ay * k * (1 - c);
            bz = z * c + (pl.Ax * y - pl.Ay * x) * s + pl.Az * k * (1 - c);
            edge = (db - da) / 0.01;
            kind = EdgeKind(a, b, x, y, z);
        }

        // a spot turned back about a plate's axis by how far that plate has moved
        private void Turn(Plate pl, double x, double y, double z, out double bx, out double by, out double bz)
        {
            double ang = -pl.Rate * MovedYears;
            double c = Math.Cos(ang), s = Math.Sin(ang), k = pl.Ax * x + pl.Ay * y + pl.Az * z;
            bx = x * c + (pl.Ay * z - pl.Az * y) * s + pl.Ax * k * (1 - c);
            by = y * c + (pl.Az * x - pl.Ax * z) * s + pl.Ay * k * (1 - c);
            bz = z * c + (pl.Ax * y - pl.Ay * x) * s + pl.Az * k * (1 - c);
        }

        // how two plates move against each other at a spot: spreading, crashing or sliding
        private int EdgeKind(int a, int b, double x, double y, double z)
        {
            var p = new[] { x, y, z };
            var va = Velocity(Plates[a], p); var vb = Velocity(Plates[b], p);
            double nx = Plates[b].X - Plates[a].X, ny = Plates[b].Y - Plates[a].Y, nz = Plates[b].Z - Plates[a].Z, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); nx /= nl; ny /= nl; nz /= nl;
            double rx = va[0] - vb[0], ry = va[1] - vb[1], rz = va[2] - vb[2];
            double closing = rx * nx + ry * ny + rz * nz, total = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            return Math.Abs(closing) < total * 0.4 ? 2 : closing > 0 ? 1 : 0;
        }

        /// <summary>Which plate a spot on the surface (unit vector) belongs to.</summary>
        public int PlateAt(double x, double y, double z) { int a, b; double da, db; Nearest2(new[] { x, y, z }, out a, out b, out da, out db); return a; }

        /// <summary>Runs the plates to the planet's new age: earthquakes and eruptions for the years that passed.
        /// Big eruptions call <paramref name="disaster"/> (place, share of life killed nearby).</summary>
        public void Advance(PlanetCore core, Action<double, double, double, double> disaster)
        {
            if (_age < 0) { _age = core.Age; return; }
            double years = core.Age - _age; _age = core.Age;
            if (years <= 0) return;
            double act = Math.Max(0.05, core.Volcanism);
            if (core.Plates && Edges.Count > 0)
            {
                // the biggest earthquake in this stretch of time: counts fall tenfold for each step of magnitude, about one
                // magnitude 8 a year on Earth; the largest possible is about 9.5. Crashing edges make the biggest.
                double big = 8 + Math.Log10(years * act) - Math.Log10(-Math.Log(Math.Max(1e-12, _rng.NextDouble())));
                if (big >= 6.5)
                {
                    var e = PickEdge(1, 0.6);
                    double m = Math.Min(e.Kind == 1 ? 9.5 : e.Kind == 2 ? 8.2 : 7.0, big);
                    Note(new Event { Age = core.Age, What = "a magnitude " + m.ToString("0.0") + " earthquake on a " + EdgeKinds[e.Kind] + " plate edge" + (m >= 8.5 && e.Kind == 1 && Plates[e.A].Oceanic ? ", with a tsunami" : ""), X = e.X, Y = e.Y, Z = e.Z, Size = m });
                    if (m >= 9) disaster(e.X, e.Y, e.Z, 0.15);
                }
            }
            // eruptions: VEI 6 about once a century, VEI 7 once in a thousand years, VEI 8 once in 50,000; more on a hotter world
            double rate8 = act / 50000.0, rate7 = act / 1000.0;
            double p8 = 1 - Math.Exp(-years * rate8), p7 = 1 - Math.Exp(-years * rate7);
            if (_rng.NextDouble() < p8 || _rng.NextDouble() < p7)
            {
                bool super = _rng.NextDouble() < p8 / Math.Max(1e-12, p7 + p8) || p8 > 0.5;
                double[] at;
                string where;
                if (core.Plates && Edges.Count > 0 && _rng.NextDouble() < 0.8) { var e = PickEdge(1, 0.75); at = new[] { e.X, e.Y, e.Z }; where = "on a " + EdgeKinds[e.Kind] + " plate edge"; }
                else { at = RandomPoint(); where = "over a hot spot"; }
                Note(new Event { Age = core.Age, What = (super ? "a super-eruption (VEI 8)" : "a great eruption (VEI 7)") + " " + where + (super ? ": ash cools the whole world" : ""), X = at[0], Y = at[1], Z = at[2], Size = super ? 8 : 7 });
                disaster(at[0], at[1], at[2], super ? 0.6 : 0.2);
            }
        }

        // an edge, preferring one kind (crashing edges hold most of the big quakes and explosive volcanoes)
        private Edge PickEdge(int kind, double prefer)
        {
            for (int t = 0; t < 30; t++) { var e = Edges[_rng.Below(Edges.Count)]; if (e.Kind == kind || _rng.NextDouble() > prefer) return e; }
            return Edges[_rng.Below(Edges.Count)];
        }

        /// <summary>How many earthquakes and eruptions there have been in all.</summary>
        public int Total;
        private void Note(Event e) { Total++; Recent.Add(e); if (Recent.Count > 20) Recent.RemoveAt(0); }

        public string Describe()
        {
            int[] c = new int[3]; foreach (var e in Edges) c[e.Kind]++;
            int tot = Math.Max(1, Edges.Count);
            return Plates.Count + " plates (edges " + (c[0] * 100 / tot) + "% spreading, " + (c[1] * 100 / tot) + "% crashing, " + (c[2] * 100 / tot) + "% sliding)";
        }
    }
}
