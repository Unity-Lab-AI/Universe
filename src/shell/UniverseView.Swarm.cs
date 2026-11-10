// Universe · shell · the universe view, its part: the swarm (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;
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
    }
}
