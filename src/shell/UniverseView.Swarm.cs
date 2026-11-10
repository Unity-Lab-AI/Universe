// Universe · shell · the universe view, its part: the swarm (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── the stars round a feeding black hole, each one a star you can click: where each is worked out here exactly as the card
        // works it out to draw it (src/render/Pipeline.cs, record 2: its ellipse, Kepler's equation, its tilt from its seed), so
        // the one under the mouse is the one picked, and the camera follows it round its orbit ──
        private Extra _tSwarmHole; private int _tSwarmK; private string _tSwarmName;

        private static uint OHash(uint a, uint b) { unchecked { uint h = a * 747796405u + b * 2891336453u + 1u; h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u; return (h >> 22) ^ h; } }
        private static float Oh01(uint a, uint b) { return (OHash(a, b) & 0xFFFFFF) / 16777216.0f; }

        // where star k of a hole's swarm is now, against the camera (universe units), and its place in the hole's own frame (horizon radii)
        private bool SwarmStarAt(Extra ex, HoleParts hp, int k, double time, out double x, out double y, out double z)
        {
            // with gravity off: from where it was at that moment, straight on at its velocity there (as the card moves it)
            double off = !Flown ? 0 : Math.Max(1e-3, _lastDrawTime - _gravityOffWatch);
            if (off <= 0) return SwarmStarRaw(ex, hp, k, time, out x, out y, out z);
            double x0, y0, z0, x1, y1, z1; bool ok = SwarmStarRaw(ex, hp, k, time - off, out x0, out y0, out z0) & SwarmStarRaw(ex, hp, k, time - off + 0.01, out x1, out y1, out z1);
            double fl = OffWatchFlight, vx = (x1 - x0) / 0.01, vy = (y1 - y0) / 0.01, vz = (z1 - z0) / 0.01; x = x0 + vx * fl; y = y0 + vy * fl; z = z0 + vz * fl;
            // gravity back on: falling back under the hole's pull on its new path (as the card draws it: mu nine K squared)
            // (about the hole's middle, its pull in these units: nine K squared times its horizon's radius cubed; its turn changes nothing)
            if (Window.GravityOn && off > fl && hp.K > 0)
            {
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), hx = RelX(ex) - _ox, hy = RelY(ex) - _oy, hz = RelZ(ex) - _oz, nx, ny, nz, nvx, nvy, nvz;
                Kepler.Propagate(9.0 * hp.K * hp.K * rs * rs * rs, x - hx, y - hy, z - hz, vx, vy, vz, off - fl, out nx, out ny, out nz, out nvx, out nvy, out nvz);
                x = nx + hx; y = ny + hy; z = nz + hz;
            }
            return ok;
        }
        private bool SwarmStarRaw(Extra ex, HoleParts hp, int k, double time, out double x, out double y, out double z)
        {
            x = y = z = 0; if (hp == null || hp.SwarmRecs == null || k < 0 || k >= hp.SwarmRecs.Length) return false;
            var s = hp.SwarmRecs[k]; float t = (float)(time + ex.Turn * 10), K = hp.K;
            float a = s.X, e = s.Y, M = s.Z + K * t * 3.0f / (float)Math.Pow(a, 1.5);
            float E = M; for (int i = 0; i < 6; i++) E -= (E - e * (float)Math.Sin(E) - M) / (1 - e * (float)Math.Cos(E));
            float px = a * ((float)Math.Cos(E) - e), pz = a * (float)Math.Sqrt(1 - e * e) * (float)Math.Sin(E);
            uint seed = BitConverter.ToUInt32(BitConverter.GetBytes(s.Size), 0);
            float inc = Oh01(seed, 1u) * 1.4f - 0.7f, node = Oh01(seed, 2u) * 6.2832f, peri = Oh01(seed, 3u) * 6.2832f;
            float cp = (float)Math.Cos(peri), sp = (float)Math.Sin(peri); float x1 = px * cp - pz * sp, z1 = px * sp + pz * cp;
            float ci = (float)Math.Cos(inc), si = (float)Math.Sin(inc); float y2 = z1 * si, z2 = z1 * ci;
            float cn = (float)Math.Cos(node), sn = (float)Math.Sin(node);
            double hx = x1 * cn - z2 * sn, hy = y2, hz = x1 * sn + z2 * cn;
            // the hole's frame on screen: tilted its own way, then its horizon's radius, then where it is (as DrawBlackHoles places it)
            double ct = Math.Cos(ex.Tilt), st = Math.Sin(ex.Tilt), rs = Units.Km(Physics.HorizonKm(ex.Mass));
            double ty = hy * ct - hz * st, tz = hy * st + hz * ct;
            x = hx * rs + RelX(ex) - _ox; y = ty * rs + RelY(ex) - _oy; z = tz * rs + RelZ(ex) - _oz;
            return true;
        }

        // the swarm star under the mouse (within the click's reach), picked; false if none
        private bool ClickSwarmStar(Vec3 dir, double reachPx)
        {
            double perRad = _lastH * 0.5 / Math.Tan(0.45), bestA = reachPx / perRad; Extra bh = null; int bk = -1;
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0 || ex.Feeding <= 0) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (rs * 600 / Math.Max(1e-30, d) * _lastH < 2) continue;                      // (not drawn yet: too small on screen)
                var hp = PartsFor(ex.Pl, ex.Spin); if (hp.SwarmN <= 0) continue;
                for (int k = 0; k < hp.SwarmN; k++)
                {
                    double x, y, z; if (!SwarmStarAt(ex, hp, k, _lastDrawTime, out x, out y, out z)) continue;
                    double l = Math.Sqrt(x * x + y * y + z * z); if (l <= 0) continue;
                    double c = (x * dir.X + y * dir.Y + z * dir.Z) / l; if (c <= 0) continue;
                    double ang = Math.Acos(Math.Min(1, c)); if (ang < bestA) { bestA = ang; bh = ex; bk = k; }
                }
            }
            if (bh == null) return false;
            _tSwarmHole = bh; _tSwarmK = bk; _tSwarmName = "a star round " + bh.Name.ToLowerInvariant();
            _tKind = 1; _tIndex = -1; _tName = _tSwarmName; _tRadius = Units.Km(696000); _tSpot = null;
            return true;
        }
        private bool SwarmPicked { get { return _tKind == 1 && _tSwarmHole != null && _tName == _tSwarmName; } }

        // its card: what it is, its orbit round the hole in kilometres and astronomical units, its year, its speed (vis-viva)
        private void SwarmCard(List<string> L, double dist)
        {
            var ex = _tSwarmHole; var hp = PartsFor(ex.Pl, ex.Spin); var s = hp.SwarmRecs[_tSwarmK];
            double rsKm = Physics.HorizonKm(ex.Mass), aM = s.X * rsKm * 1000, e = s.Y, gm = Physics.GmSol * ex.Mass;
            double years = 2 * Math.PI * Math.Sqrt(aM * aM * aM / gm) / 31557600.0, vPeri = Math.Sqrt(gm * (1 + e) / (aM * (1 - e)));
            L.Add("a star on its own orbit round " + ex.Name.ToLowerInvariant());
            L.Add("orbit: " + (aM / 1.496e11).ToString(aM > 1.496e12 ? "N0" : "0.##") + " AU across its long half (" + s.X.ToString("0") + " horizon radii), eccentricity " + e.ToString("0.00"));
            L.Add("its year: " + (years >= 1 ? years.ToString("0.#") + " years" : (years * 365.25).ToString("0.#") + " days"));
            L.Add("speed at its closest: " + (vPeri / 1000).ToString("N0") + " km/s, " + (vPeri / Physics.C * 100).ToString("0.#") + "% of light's");
            L.Add("distance: " + Units.Describe(dist));
        }
        // ── a feeding hole's disc and jets, each a thing you can click: the line under the mouse met with the disc's plane (in the
        // hole's own frame, as DrawBlackHoles places it) within the disc's reach, or passing within the click's reach of a jet along
        // the spin axis; picked, it is the point of gas there, its card its heat, its speed and its turn ──
        private Extra _tPartHole; private int _tPart; private double _tPartR, _tPartX, _tPartY, _tPartZ; private string _tPartName;   // _tPart 1 the disc, 2 a jet; X Y Z the point in the hole's frame (horizon radii)
        private bool PartPicked { get { return _tKind == 1 && _tPartHole != null && _tName == _tPartName; } }
        // a point of the hole's frame (horizon radii) against the camera (universe units)
        private void HoleToCamera(Extra ex, double hx, double hy, double hz, out double x, out double y, out double z)
        {
            double ct = Math.Cos(ex.Tilt), st = Math.Sin(ex.Tilt), rs = Units.Km(Physics.HorizonKm(ex.Mass));
            x = hx * rs + RelX(ex) - _ox; y = (hy * ct - hz * st) * rs + RelY(ex) - _oy; z = (hy * st + hz * ct) * rs + RelZ(ex) - _oz;
        }
        private bool ClickHolePart(Vec3 dir, double reachPx)
        {
            double perRad = _lastH * 0.5 / Math.Tan(0.45), bestT = double.MaxValue; Extra bh = null; int part = 0; double br = 0, bx = 0, by = 0, bz = 0;
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0 || ex.Feeding <= 0 || ex.Pl == null) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), Dx = RelX(ex) - _ox, Dy = RelY(ex) - _oy, Dz = RelZ(ex) - _oz, d = Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);
                if (rs * 600 / Math.Max(1e-30, d) * _lastH < 2) continue;                      // (not drawn yet)
                double ct = Math.Cos(ex.Tilt), st = Math.Sin(ex.Tilt);
                // the disc: where the line meets its plane (y = 0 in the hole's frame)
                double den = dir.Y * ct + dir.Z * st;
                if (Math.Abs(den) > 1e-12)
                {
                    double t = (Dy * ct + Dz * st) / den;
                    if (t > 0 && t < bestT)
                    {
                        double qx = dir.X * t - Dx, qy = dir.Y * t - Dy, qz = dir.Z * t - Dz;
                        double hx = qx / rs, hz = (-qy * st + qz * ct) / rs, r = Math.Sqrt(hx * hx + hz * hz);
                        if (r >= ex.Pl.DiscLo && r <= ex.Pl.DiscHi) { bestT = t; bh = ex; part = 1; br = r; bx = hx; by = 0; bz = hz; }
                    }
                }
                // a jet: the spin axis (the hole frame's y) from 3 to 600 radii either way; the line's closest pass to it
                if (ex.Feeding == 2)
                {
                    double ax = 0, ay = ct, az = st;                                           // the axis in the camera's frame
                    double b = dir.X * ax + dir.Y * ay + dir.Z * az, dd = dir.X * Dx + dir.Y * Dy + dir.Z * Dz, ad = ax * Dx + ay * Dy + az * Dz, den2 = 1 - b * b;
                    if (den2 > 1e-9)
                    {
                        double t = (dd - b * ad) / den2, s = (b * dd - ad) / den2;               // along the line, along the axis (universe units)
                        double ys = s / rs, px = dir.X * t - (Dx + ax * s), py = dir.Y * t - (Dy + ay * s), pz = dir.Z * t - (Dz + az * s);
                        double miss = Math.Sqrt(px * px + py * py + pz * pz), w = (0.6 + Math.Abs(ys) * 0.03) * rs;   // (the jet's width at that height, as drawn)
                        if (t > 0 && t < bestT && Math.Abs(ys) >= 3 && Math.Abs(ys) <= 600 && (miss < w || miss / t * perRad < reachPx)) { bestT = t; bh = ex; part = 2; br = Math.Abs(ys); bx = 0; by = ys; bz = 0; }
                    }
                }
            }
            // a torn star's stream (a tidal disruption): its middle line, the same curve the card winds its gas along, turned its own way per star
            foreach (var ex in _extras)
            {
                if (!ex.Hole || !ex.Eating || ex.Mass <= 0) continue;
                for (int q = 0; q < Math.Max(1, ex.EatN); q++)
                {
                    double ra = q * Math.PI * 2 / Math.Max(1, ex.EatN) + ex.Turn * 3, cq = Math.Cos(ra), sq = Math.Sin(ra), rs = Units.Km(Physics.HorizonKm(ex.Mass));
                    for (int k = 0; k <= 200; k++)
                    {
                        double s0 = k / 200.0, rr = 3.2 + 90 * Math.Exp(-s0 * 3.4), an = -s0 * 6.2832 * 2.4 + 0.9, lx = Math.Cos(an) * rr, lz = Math.Sin(an) * rr;
                        double hx = lx * cq - lz * sq, hz = lx * sq + lz * cq, x, y, z; HoleToCamera(ex, hx, 0, hz, out x, out y, out z);
                        double l = Math.Sqrt(x * x + y * y + z * z); if (l <= 0 || l >= bestT) continue;
                        double c = (x * dir.X + y * dir.Y + z * dir.Z) / l; if (c <= 0) continue;
                        double miss = Math.Sqrt(Math.Max(0, 1 - c * c)) * l, wide = (0.25 + 4 * s0 * s0) * 0.5 * rs;   // (the stream's width there, as drawn)
                        if (miss < wide || miss / l * perRad < reachPx) { bestT = l; bh = ex; part = 3; br = rr; bx = hx; by = 0; bz = hz; }
                    }
                }
            }
            if (bh == null) return false;
            _tPartHole = bh; _tPart = part; _tPartR = br; _tPartX = bx; _tPartY = by; _tPartZ = bz;
            _tPartName = (part == 1 ? "the disc of " : part == 2 ? "a jet of " : "a star torn apart by ") + bh.Name.ToLowerInvariant();
            _tKind = 1; _tIndex = -1; _tName = _tPartName; _tRadius = Units.Km(Physics.HorizonKm(bh.Mass)) * (part == 1 ? 0.5 : 2); _tSpot = null;
            return true;
        }
        private static string Span(double sec) { return sec < 120 ? sec.ToString("0.#") + " seconds" : sec < 7200 ? (sec / 60).ToString("0") + " minutes" : sec < 172800 ? (sec / 3600).ToString("0.#") + " hours" : Years(sec / 31557600.0); }
        // the disc's or the jet's card: where on it, how hot, how fast, its turn
        private void HolePartCard(List<string> L, double dist)
        {
            var ex = _tPartHole; double rsKm = Physics.HorizonKm(ex.Mass), r = _tPartR;
            if (_tPart == 1)
            {
                // a thin disc (Shakura and Sunyaev 1973): hottest near its inner edge, by its feeding rate and the hole's mass, cooling as r^-3/4
                double tMax = 6.3e5 * Math.Pow(ex.Pl.FeedEdd, 0.25) * Math.Pow(Math.Max(1e-9, ex.Mass) / 1e8, -0.25), tK = tMax * Math.Pow(r / ex.Pl.DiscLo, -0.75);
                double beta = Math.Sqrt(0.5 / r), periodS = 2 * Math.PI * r * rsKm * 1000 / (beta * Physics.C);   // (its circular speed, as the card draws it)
                L.Add("gas falling in round " + ex.Name.ToLowerInvariant() + ", " + r.ToString("0.#") + " horizon radii out (" + Units.Describe(Units.Km(r * rsKm)) + ")");
                L.Add("about " + tK.ToString(tK >= 1e4 ? "N0" : "0") + " K here, " + (tK > 1e5 ? "glowing in X-rays and far ultraviolet" : tK > 1e4 ? "glowing in ultraviolet" : "glowing in visible and infrared light"));
                L.Add("speed " + (beta * Physics.C / 1000).ToString("N0") + " km/s, " + (beta * 100).ToString("0") + "% of light's; once round in " + Span(periodS));
                L.Add("fed at " + (ex.Pl.FeedEdd * 100).ToString("0.#") + "% of the most its own light allows (the Eddington rate)");
            }
            else if (_tPart == 3)
            {
                // where a star like ours is torn: the hole's pull on its near side outdoing its own gravity (the tidal radius, R (M / m)^(1/3))
                double tidalKm = 696000 * Math.Pow(Math.Max(1, ex.Mass), 1 / 3.0);
                L.Add("a star that came too close to " + ex.Name.ToLowerInvariant() + ", stretched along its orbit into a stream of gas winding in to feed the disc");
                L.Add("here " + r.ToString("0.#") + " horizon radii out (" + Units.Describe(Units.Km(r * rsKm)) + "), hotter and bluer the deeper it falls");
                L.Add("a star like ours is torn within about " + Units.Describe(Units.Km(tidalKm)) + (tidalKm < rsKm ? ": inside the horizon, so a hole this heavy swallows such a star whole" : ""));
            }
            else
            {
                L.Add("a jet of " + ex.Name.ToLowerInvariant() + ": plasma flung out along its spin axis at nearly light's speed, wound by its magnetic field");
                L.Add(r.ToString("N0") + " horizon radii up its length (" + Units.Describe(Units.Km(r * rsKm)) + "); real jets reach thousands of light-years");
            }
            L.Add("distance: " + Units.Describe(dist));
        }
    }
}
