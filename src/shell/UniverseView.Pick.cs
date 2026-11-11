// Universe · shell · the universe view, its part: pick (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── what you clicked: the camera turns to face it and the wheel flies to it ──
        private Extra _tExtra;                   // the body picked (kind 6): the body itself, found again by its place whenever the bodies round you are made again
        private int _tKind = -1;                 // 0 a galaxy, 1 a star, 2 a planet or moon, 3 a spot on the planet's ground
        private int _tIndex; private double _tx, _ty, _tz, _tRadius;   // galaxy or body index; a star's place (universe units) or a ground spot (unit direction)
        private string _tName = "";
        private bool _pressed, _dragged; private int _pressX, _pressY;
        private Mat4 _lastVp; private int _lastW = 1, _lastH = 1; private bool _haveVp;
        /// <summary>Set when something the menu shows has changed (the scale's tick), so the menu is rebuilt.</summary>
        public bool MenuChanged;
        /// <summary>Whether the scale shows on screen (H, or the View menu).</summary>
        public bool ScaleOn { get { return _hud.Visible; } set { _hud.Visible = value; } }
        private void AimNext()
        {
            double tx, ty, tz;
            if (_ground != null) { tx = _px; ty = _py; tz = _pz; }                                  // straight down to the ground
            else if (_system != null)
            {
                int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Interest > _system.Bodies[pick].Interest)) pick = i;   // (the world of most interest, by its template)
                if (pick < 0) return;
                BodyPos(pick, out tx, out ty, out tz);
                // come in over its day side: a point a few radii out toward the star
                double l0 = Math.Sqrt(tx * tx + ty * ty + tz * tz), rr = _system.Bodies[pick].RadiusUnits * 4;
                if (l0 > 0) { tx -= tx / l0 * rr; ty -= ty / l0 * rr; tz -= tz / l0 * rr; }
            }
            else if (_near >= 0 && _localCount > 0 && NearShare() > 0.9)
            {
                double d; int k = NearestStar(out d); if (k < 0) return;
                double ux, uy, uz; StarPos(k, out ux, out uy, out uz); tx = (ux - _ax); ty = (uy - _ay); tz = (uz - _az);
            }
            else { var g = _galaxies[0]; tx = g.X - _ax; ty = g.Y - _ay; tz = g.Z - _az; }
            double dx = tx - _ox, dy = ty - _oy, dz = tz - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l > 0) SetLook(dx, dy, dz);
        }

        // where the clicked thing is now, from the camera (universe units), and how big it is; false if it is gone
        private bool TargetFromCamera(out double dx, out double dy, out double dz, out double radius)
        {
            dx = dy = dz = 0; radius = 0;
            if (_tKind == 8)
                for (int i = 0; i < _galaxies.Count; i++)
                {
                    // the far galaxy you picked, now made in full near where its light was: the pick goes on to it
                    var g8 = _galaxies[i]; double ex = g8.X - _tx, ey = g8.Y - _ty, ez = g8.Z - _tz;
                    if (ex * ex + ey * ey + ez * ez < Math.Pow(3e6 / Units.LightYearsPerUnit, 2)) { _tKind = 0; _tIndex = i; _tName = g8.Name; break; }
                }
            if (_tKind == 0) { var g = _galaxies[_tIndex]; dx = (g.X - _ax) - _ox; dy = (g.Y - _ay) - _oy; dz = (g.Z - _az) - _oz; radius = g.Scale * 0.3; return true; }
            if (_tKind == 6 && _tSpot != null && _tExtra != null)
            {
                // the spot picked on the body's stone: the point itself on its drawn surface, a small mark to fly down to
                double rr = BodyRadiusToward(_tExtra, _lastDrawTime, _tSpot[0], _tSpot[1], _tSpot[2]);
                dx = RelX(_tExtra) - _ox + _tSpot[0] * rr; dy = RelY(_tExtra) - _oy + _tSpot[1] * rr; dz = RelZ(_tExtra) - _oz + _tSpot[2] * rr; radius = _tExtra.R * 0.05; return true;
            }
            // a small body: from its precise offset, the same place it is drawn at (its absolute coordinate, taken against the anchor,
            // is off by up to a million kilometres: the zoom had flown to a point beside the body and stalled there, the dive never
            // within reach of the stone it was aimed at)
            if (_tKind == 6 && _tExtra != null) { dx = RelX(_tExtra) - _ox; dy = RelY(_tExtra) - _oy; dz = RelZ(_tExtra) - _oz; radius = _tRadius; return true; }
            if (PartPicked) { HoleToCamera(_tPartHole, _tPartX, _tPartY, _tPartZ, out dx, out dy, out dz); radius = _tRadius; return true; }   // (a point of a hole's disc or jet)
            if (SwarmPicked && SwarmStarAt(_tSwarmHole, PartsFor(_tSwarmHole.Pl, _tSwarmHole.Spin), _tSwarmK, _lastDrawTime, out dx, out dy, out dz)) { radius = _tRadius; return true; }   // (a swarm star: where it is on its orbit now)
            if (_tKind == 1 && _tSn != null && _tName == SnName(_tSn)) { _tx = _tSn.Ux; _ty = _tSn.Uy; _tz = _tSn.Uz; }
            if (_tKind == 1 && _system != null && _tName == "a star" && Math.Abs(_tx - _ax) + Math.Abs(_ty - _ay) + Math.Abs(_tz - _az) < SystemRange * 0.5) { dx = -_ox; dy = -_oy; dz = -_oz; double want = StarRadiusNow; _tRadius = _tRadius > 0 && want > 0 ? Math.Exp(Math.Log(_tRadius) + (Math.Log(want) - Math.Log(_tRadius)) * 0.08) : want; radius = _tRadius; return true; }   // (its real radius eased in from the one the pick was made with, a twelfth of the way a frame: the shot measures its distance in radii, and the radius jumping from one solar radius to a dwarf's or a giant's the frame the system opened jerked the camera)   // (a star whose system is open is that star: its real place and radius, so a zoom onto its face ends at its face. The pick's stored point can sit a tenth of an AU from where the star is drawn, and aimed at the point the zoom crawled to a kilometre short of nothing, the caption saying onto its face with the star a dot in the distance, and the dive never came; one solar radius had stood for every star)   // (where it is now: a supernova is moved onto a real star of its galaxy once that galaxy's stars are made, after the pick from afar had copied its far place; the shots had flown to the old point, light-years from the dying star, and seen it as a point of light with no cloud)
            if (_tKind == 1 || _tKind == 4 || _tKind == 5 || _tKind == 6 || _tKind == 7 || _tKind == 8) { dx = (_tx - _ax) - _ox; dy = (_ty - _ay) - _oy; dz = (_tz - _az) - _oz; radius = _tRadius; return true; }   // a star, or a spot in a belt
            if (_tKind == 2 && _system != null && _tIndex < _system.Bodies.Count)
            { double x, y, z; BodyPos(_tIndex, out x, out y, out z); dx = x - _ox; dy = y - _oy; dz = z - _oz; radius = _system.Bodies[_tIndex].RadiusUnits; return true; }
            if (_tKind == 3 && _ground != null)
            {
                double g = _ground.Solid ? Surface(_tx, _ty, _tz) : 0, r = (_ground.Radius + g) * Units.Km(1);
                dx = _px + _tx * r - _ox; dy = _py + _ty * r - _oy; dz = _pz + _tz * r - _oz; radius = 0; return true;
            }
            return false;
        }

        // where a point (relative to the camera, universe units) lands on screen, in pixels; false if behind you
        private bool ToScreen(double dx, double dy, double dz, out double sx, out double sy)
        {
            sx = sy = 0; if (!_haveVp) return false;
            var m = _lastVp; float x = (float)dx, y = (float)dy, z = (float)dz;
            float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw <= 0) return false;
            sx = (cx / cw * 0.5 + 0.5) * _lastW; sy = (0.5 - cy / cw * 0.5) * _lastH; return true;
        }

        // pick what is under the mouse: the planet's ground if you are at one, else the nearest planet or moon, star or galaxy to the line
        private void Pick(Vec3 dir, int mx, int my)
        {
            _tSn = null;
            if (ClickSupernova(dir)) return;                                                // a supernova going now, under the mouse: picked before any star
            _tSwarmHole = null;
            if (ClickSwarmStar(dir, 24 * Math.Max(1, _lastH / 1080.0))) return;            // a star of a feeding hole's swarm, under the mouse
            _tPartHole = null;
            if (ClickHolePart(dir, 24 * Math.Max(1, _lastH / 1080.0))) return;             // a feeding hole's disc or jet, under the mouse
            double reachPx = 24 * Math.Max(1, _lastH / 1080.0), best = reachPx * reachPx; int kind = -1, idx = 0; double tx = 0, ty = 0, tz = 0, trad = 0; string name = "";
            _tSpot = null;
            // a spot on the small body you are at: where the line under the mouse meets its drawn stone (its real shape, as a world's
            // land is met), once it stands a few dozen pixels wide; picked, the zoom flies down to that spot, as to a spot of land
            if (_tKind == 6 && _tExtra != null && _tExtra.Shape.Length > 0 && !_tExtra.Hole && _ground == null)
            {
                var exs = _tExtra; double cx0 = RelX(exs) - _ox, cy0 = RelY(exs) - _oy, cz0 = RelZ(exs) - _oz, cl0 = Math.Sqrt(cx0 * cx0 + cy0 * cy0 + cz0 * cz0);
                if (cl0 > 0 && exs.R / cl0 * _lastH > 40)
                {
                    Func<double, double> over = t => { double hx = dir.X * t - cx0, hy = dir.Y * t - cy0, hz = dir.Z * t - cz0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz); return hl - BodyRadiusToward(exs, _lastDrawTime, hx / hl, hy / hl, hz / hl); };
                    double t0 = Math.Max(0, cl0 - exs.R * 2), t1 = cl0 + exs.R * 2, hitT = -1, prev = t0, prevV = over(t0);
                    for (int q = 1; q <= 240; q++) { double t = t0 + (t1 - t0) * q / 240.0, v = over(t); if (prevV > 0 && v <= 0) { double lo = prev, hi = t; for (int b2 = 0; b2 < 24; b2++) { double mid = (lo + hi) / 2; if (over(mid) > 0) lo = mid; else hi = mid; } hitT = (lo + hi) / 2; break; } prev = t; prevV = v; }
                    if (hitT > 0)
                    {
                        double hx = dir.X * hitT - cx0, hy = dir.Y * hitT - cy0, hz = dir.Z * hitT - cz0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                        _tSpot = new[] { hx / hl, hy / hl, hz / hl }; _tName = "a spot on " + exs.Name.ToLowerInvariant(); return;
                    }
                }
            }
            Func<double, double, double, double> near = (dx, dy, dz) => { double sx, sy; if (!ToScreen(dx, dy, dz, out sx, out sy)) return double.MaxValue; return (sx - mx) * (sx - mx) + (sy - my) * (sy - my); };
            for (int q = 0; q < _extras.Count && _ground == null; q++)
            {
                // the other bodies round you (black holes, neutron stars, rogue planets, asteroids, comets...): clickable as any star
                var ex = _extras[q]; double d2 = near(RelX(ex) - _ox, RelY(ex) - _oy, RelZ(ex) - _oz);
                if (d2 < best) { best = d2; kind = 6; idx = q; tx = ex.X; ty = ex.Y; tz = ex.Z; trad = ex.R; name = ex.Name; }
            }
            if (_tKind == 5 && _near >= 0 && _tGal == _near && _tIndex >= 0 && _tIndex < _features.Count && _ground == null)
            {
                // inside a picked nebula or cluster: a click is a point in its gas, right where you click, at the depth you are at
                // (a star only if you click right on it), so the wheel flies there and on into its atoms
                var g = _galaxies[_near]; var ft = _features[_tIndex]; double cux, cuy, cuz; ToUniverse(g, ft[0], ft[1], ft[2], out cux, out cuy, out cuz);
                double ex = (cux - _ax) - _ox, ey = (cuy - _ay) - _oy, ez = (cuz - _az) - _oz, toCentre = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                if (toCentre < ft[3] * g.Scale * 1.2)
                {
                    double tight = Math.Pow(16 * Math.Max(1, _lastH / 1080.0), 2);
                    for (int k = 0; k < _localCount; k++)
                    {
                        var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
                        if (near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz) < tight) { _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a " + _kinds[_localKind[k]].Name; return; }
                    }
                    double cdx, cdy, cdz, crad; TargetFromCamera(out cdx, out cdy, out cdz, out crad);
                    double depth = Math.Max(Units.Km(10), Math.Sqrt(cdx * cdx + cdy * cdy + cdz * cdz));
                    _tx = _ax + _ox + dir.X * depth; _ty = _ay + _oy + dir.Y * depth; _tz = _az + _oz + dir.Z * depth; _tRadius = 0;
                    _tName = "a point in the gas of " + _tName.Replace("a point in the gas of ", ""); return;
                }
            }
            if (_ground != null)
            {
                // the ground: where the line under the mouse meets the planet
                double x, y, z; CamFromPlanet(out x, out y, out z);
                // follow the line of sight down to the real ground (its hills and basins, the sea where there is sea): step out
                // along it, growing with the distance, until it passes below the ground, then close in on the crossing
                double alt0 = Math.Max(1e-9, AboveGround() / Units.Km(1)), t0 = 0, t1 = alt0 * 0.05, far = Math.Sqrt(Math.Max(0, (x * x + y * y + z * z) - _ground.Radius * _ground.Radius)) * 1.2 + 50;
                Func<double, double> above = t =>
                {
                    double hx = x + dir.X * t, hy = y + dir.Y * t, hz = z + dir.Z * t, l = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    return l - _ground.Radius - (_ground.Solid ? Surface(hx / l, hy / l, hz / l) : 0);
                };
                bool hit = false;
                for (int step = 0; step < 400 && t1 < far; step++)
                {
                    if (above(t1) <= 0) { hit = true; break; }
                    t0 = t1; t1 += Math.Max(alt0 * 0.02, t1 * 0.04);
                }
                if (hit)
                {
                    for (int q = 0; q < 40; q++) { double tm = (t0 + t1) * 0.5; if (above(tm) > 0) t0 = tm; else t1 = tm; }
                    double hx = x + dir.X * t1, hy = y + dir.Y * t1, hz = z + dir.Z * t1, l = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    _tKind = 3; _tx = hx / l; _ty = hy / l; _tz = hz / l; _tName = "a spot on " + GroundKind(); return;
                }
            }
            if (_system != null)
            {
                PlaceSystem();
                for (int i = 1; i < _bodyCount; i++)
                {
                    double d2 = near(_bodyVerts[i].X - _ox, _bodyVerts[i].Y - _oy, _bodyVerts[i].Z - _oz);
                    if (d2 < best) { best = d2; kind = 2; idx = i - 1; name = _system.Bodies[i - 1].Kind; }
                }
            }
            if (_system != null && kind < 0)
            {
                // a belt: where the line under the mouse crosses the plane of the system, if it falls in a belt (the asteroid belt,
                // the Kuiper belt, or the Oort cloud's inner reaches)
                if (Math.Abs(dir.Y) > 1e-6)
                {
                    double t = -_oy / dir.Y;
                    if (t > 0)
                    {
                        double hx = _ox + dir.X * t, hz = _oz + dir.Z * t, au = Math.Sqrt(hx * hx + hz * hz) / Units.Au(1);
                        var sy = _system; string bn = null;
                        if (sy.BeltOutAu > 0 && au >= sy.BeltInAu && au <= sy.BeltOutAu) bn = "the asteroid belt";
                        else if (au >= sy.KuiperInAu && au <= sy.KuiperOutAu) bn = "the Kuiper belt";
                        else if (au >= sy.OortInAu && au <= sy.OortOutAu) bn = "the Oort cloud";
                        if (bn != null)
                        {
                            double sx2, sy2; bool onScreen = ToScreen(hx - _ox, -_oy, hz - _oz, out sx2, out sy2);
                            if (onScreen) { _tKind = 4; _tx = _ax + hx; _ty = _ay; _tz = _az + hz; _tRadius = 0; _tName = bn; return; }
                        }
                    }
                }
            }
            if (_near >= 0 && kind < 0)
            {
                var g = _galaxies[_near];
                for (int k = 0; k < _localCount; k++)
                {
                    var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
                    double d2 = near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz);
                    if (d2 < best) { best = d2; kind = 1; tx = ux; ty = uy; tz = uz; trad = Units.Km(696000); { int kn = LocalKindNow(k, _localKind[k]); name = kn >= 0 ? "a " + _kinds[kn].Name : "a black hole, all a massive star left"; } }
                }
                if (kind < 0 && NearShare() > 0.5)
                    for (int k = 0; k < _nearLocal.Length; k++)
                    {
                        var s0 = _nearLocal[k]; double ux, uy, uz; ToUniverse(g, s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                        double d2 = near((ux - _ax) - _ox, (uy - _ay) - _oy, (uz - _az) - _oz);
                        if (d2 < best) { best = d2; kind = 1; tx = ux; ty = uy; tz = uz; trad = Units.Km(696000); name = "a " + _kinds[_nearKinds[k]].Name; }
                    }
            }
            int pickGal = -1;
            for (int gi = 0; gi < _galaxies.Count && kind != 5; gi++)
            {
                // a cluster or nebula of another galaxy near enough to see them
                var g = _galaxies[gi]; if (gi == _near || g.Features == null) continue;
                double gdx = g.X - _cx, gdy = g.Y - _cy, gdz = g.Z - _cz; if (Math.Sqrt(gdx * gdx + gdy * gdy + gdz * gdz) / g.Scale > 60) continue;
                string[] nm = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                for (int q = 0; q < g.Features.Count; q++)
                {
                    var ft = g.Features[q]; double ux, uy, uz; ToUniverse(g, ft[0], ft[1], ft[2], out ux, out uy, out uz);
                    double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = ft[3] * g.Scale / Math.Max(1e-12, l) / Math.Tan(0.45) * _lastH * 0.5, d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && kind != 1 && (kind < 0 || d2 < best)) { best = d2; kind = 5; idx = q; tx = ux; ty = uy; tz = uz; trad = ft[3] * g.Scale; name = nm[(int)ft[4]] + " of " + g.Name; pickGal = gi; }
                }
            }
            if (_near >= 0 && (kind < 0 || kind == 1) && NearShare() > 0.2)
            {
                // a star cluster or a nebula of the near galaxy: by its place on screen, or anywhere on it
                var g = _galaxies[_near]; string[] names = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                for (int q = 0; q < _features.Count; q++)
                {
                    var ft = _features[q]; double ux, uy, uz; ToUniverse(g, ft[0], ft[1], ft[2], out ux, out uy, out uz);
                    double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = ft[3] * g.Scale / Math.Max(1e-12, l) / Math.Tan(0.45) * _lastH * 0.5, d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && kind != 1 && (kind < 0 || d2 < best)) { best = d2; kind = 5; idx = q; tx = ux; ty = uy; tz = uz; trad = ft[3] * g.Scale; name = names[(int)ft[4]]; pickGal = _near; }
                }
            }
            if (kind < 0)
                for (int i = 0; i < _galaxies.Count; i++)
                {
                    var g = _galaxies[i]; double dx = (g.X - _ax) - _ox, dy = (g.Y - _ay) - _oy, dz = (g.Z - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double sizePx = g.Scale / Math.Max(1e-9, l) / Math.Tan(0.45) * _lastH * 0.5;   // its size on screen
                    double d2 = near(dx, dy, dz), allow = Math.Max(reachPx, sizePx);
                    if (d2 < allow * allow && (kind < 0 || d2 < best)) { best = d2; kind = 0; idx = i; name = g.Name; }
                }
            if (kind < 0 && _glowShown.Length > 0)
            {
                // every point of the web's light far off is a galaxy: a click on one picks it, to fly to; there its galaxies are
                // made in full and the pick goes on to the one at that spot
                double gb = reachPx * reachPx; int gq = -1;
                for (int q = 0; q < _glowShown.Length; q++) { var v = _glowShown[q]; double d2 = near((v.X - _ax) - _ox, (v.Y - _ay) - _oy, (v.Z - _az) - _oz); if (d2 < gb) { gb = d2; gq = q; } }
                if (gq >= 0)
                {
                    var v = _glowShown[gq]; double ly = Math.Sqrt(Math.Pow(v.X - _ax - _ox, 2) + Math.Pow(v.Y - _ay - _oy, 2) + Math.Pow(v.Z - _az - _oz, 2)) * Units.LightYearsPerUnit;
                    kind = 8; idx = gq; tx = v.X; ty = v.Y; tz = v.Z; trad = 50000 / Units.LightYearsPerUnit;
                    name = "a galaxy, seen by its light from " + (ly >= 1e9 ? (ly / 1e9).ToString("0.#") + " billion" : (ly / 1e6).ToString("N0") + " million") + " light-years away";
                }
            }
            if (_cstars.Count > 0)
            {
                // a star of the cluster round you: it takes the click ahead of the cluster itself
                double tight = Math.Pow(12 * Math.Max(1, _lastH / 1080.0), 2), cb = tight; int ci = -1;
                for (int q = 0; q < _cstars.Count; q++) { var cs = _cstars[q]; double d2 = near((cs[0] - _ax) - _ox, (cs[1] - _ay) - _oy, (cs[2] - _az) - _oz); if (d2 < cb) { cb = d2; ci = q; } }
                if (ci >= 0) { var cs = _cstars[ci]; bool old = cs[3] == 0; kind = 1; tx = cs[0]; ty = cs[1]; tz = cs[2]; trad = Units.Km(696000 * (old && cs[4] < 0.1 ? 20 : 1)); name = old ? (cs[4] < 0.1 ? "a red giant of the cluster" : cs[4] < 0.4 ? "an old orange star of the cluster" : "an old yellow star of the cluster") : "a young blue-white star of the cluster"; }
            }
            if (kind < 0 && _near >= 0 && NearShare() > 0.5)
            {
                // nothing there but the space between the stars: that point, at the depth of the nearest thing, to fly to and into its gas
                double depth = Math.Max(Units.Km(1000), NearestDistance());
                _tKind = 5; _tIndex = -1; _tGal = _near; _tx = _ax + _ox + dir.X * depth; _ty = _ay + _oy + dir.Y * depth; _tz = _az + _oz + dir.Z * depth; _tRadius = 0;
                _tName = "a point in the space between the stars"; return;
            }
            if (kind < 0) return;
            var layHere = LayerHere(); var starHere = StarLayerHere();
            if (layHere == null && starHere != null && (kind < 0 || kind == 1))
            {
                // inside a star, a click is a point in the layer you are in, just ahead: the wheel flies to it and on into that layer's plasma
                double reachS = Units.Km(1000); kind = 7; idx = 0; tx = _ax + _ox + dir.X * reachS; ty = _ay + _oy + dir.Y * reachS; tz = _az + _oz + dir.Z * reachS; trad = 0; name = "a point in its " + starHere.Name.ToLowerInvariant();
            }
            else if (layHere != null && layHere.Matter != "core" && (kind < 0 || kind == 3))
            {
                // inside a giant, a click is a point in the layer you are in, just ahead where you clicked (its gas, its plasma, its
                // hot dense water): the wheel flies to it and on into that layer's own matter
                double reachU = Units.Km(1000); kind = 7; idx = 0; tx = _ax + _ox + dir.X * reachU; ty = _ay + _oy + dir.Y * reachU; tz = _az + _oz + dir.Z * reachU; trad = 0; name = "a point in its " + layHere.Name.ToLowerInvariant();
            }
            _tKind = kind; _tIndex = idx; _tx = tx; _ty = ty; _tz = tz; _tRadius = trad; _tName = name; if (kind == 5) _tGal = pickGal;
            if (kind >= 0 && !_hud.Visible) { _hud.Visible = true; MenuChanged = true; }   // (a click that picks something shows its card: cards hidden with H come back for it)
        }

        /// <summary>One camera point of the universe clip: the plot location (the anchor and the offset from it), the facing, the four
        /// scales (out to the cosmic web, the space round a body, the matter's depth), the universe's clock, the matter view's own
        /// camera, and, as a check, which galaxy, star system and ground were made round it.</summary>
        /// <summary>The camera as it is this frame, from the universe's own equations: its right, up and forward (the view's own axes,
        /// as the picture is drawn with them) and how far away what it looks at is (km). The clip's encoder works out from two of
        /// these where every block of the picture came from.</summary>
        public double[] CameraNow()
        {
            var f = Look.Normalized; var u = ViewUp(); var x = Vec3.Cross(u, f).Normalized; var y = Vec3.Cross(f, x);
            return new double[] { x.X, x.Y, x.Z, y.X, y.Y, y.Z, f.X, f.Y, f.Z, ViewDistanceKm() };
        }
        public void WritePoint(System.IO.BinaryWriter o)
        {
            o.Write(_ax); o.Write(_ay); o.Write(_az); o.Write(_ox); o.Write(_oy); o.Write(_oz);
            o.Write((float)_fx); o.Write((float)_fy); o.Write((float)_fz); o.Write(_camUp.X); o.Write(_camUp.Y); o.Write(_camUp.Z);
            o.Write(_farScale); o.Write(_spaceScale); o.Write(_spaceTop); o.Write(_matterScale); o.Write(_simYears);
            _matter.WriteFocus(o);
            o.Write(_near); o.Write(_sysStar); o.Write(_planet);
        }

        /// <summary>A camera point of a universe clip set (the exact reverse of <see cref="WritePoint"/>).</summary>
        public void ApplyPoint(byte[] d)
        {
            using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(d)))
            {
                _ax = r.ReadDouble(); _ay = r.ReadDouble(); _az = r.ReadDouble(); _ox = r.ReadDouble(); _oy = r.ReadDouble(); _oz = r.ReadDouble();
                _fx = r.ReadSingle(); _fy = r.ReadSingle(); _fz = r.ReadSingle(); _camUp = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                _farScale = r.ReadDouble(); _spaceScale = r.ReadDouble(); _spaceTop = r.ReadDouble(); _matterScale = r.ReadDouble(); _simYears = r.ReadDouble();
                _matter.ReadFocus(r);
            }
        }
        /// <summary>A scene call of a universe clip made again on the matter view, by its name, with its recorded arguments.</summary>
        public void ApplyMatterCall(string name, object[] args)
        {
            foreach (var m in typeof(MatterView).GetMethods())
            {
                if (m.Name != name) continue; var ps = m.GetParameters(); if (ps.Length != args.Length) continue;
                bool fit = true; for (int i = 0; i < ps.Length; i++) if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) fit = false;
                if (!fit) continue;
                try { m.Invoke(_matter, args); } catch (Exception ex) { Universe.Program.WriteLog("universe clip: " + name + " could not be made again: " + (ex.InnerException ?? ex).Message); }
                return;
            }
            Universe.Program.WriteLog("universe clip: no scene call " + name + " with " + args.Length + " arguments");
        }
        /// <summary>The galaxy, star system and ground made round the camera now (the universe clip's check).</summary>
        public int NearIndex { get { return _near; } }
        public int SystemIndex { get { return _sysStar; } }
        public int GroundIndex { get { return _planet; } }
        /// <summary>A line for the title while a universe clip plays (null: none).</summary>
        public string ClipNote;

        public bool Update(Input input, double dt, int w, int h)
        {
            Mark();
            LoadProgress(dt);
            if (Loading) input.Hold();                                                  // (the controls wait while the universe is made)
            if (_pausedTour != null)
            {
                // a paused tour: T carries it on (the T key and the menu, as they start it), R starts it again, Backspace lets it go
                if (input.WasPressed('R')) StartTour(_tourName);
                else if (input.WasPressed(0x08)) { _pausedTour = null; _pausedWhy = null; MenuChanged = true; }
            }
            if (_tour != null && !Loading) TourStep(input, dt, w, h);   // (the tour begins once the universe is made)
            _markDt = dt; if (!Loading) { if (_fly != null) FlyStep(input, dt, w, h); MarkKeys(input); }   // (bookmarks: UniverseView.Bookmarks.cs)
            _matter.Glide = _tour != null ? 1.2 : 4;                                     // on the tour the matter glides as a film camera would                                  // a tour gives the controls what each stop asks
            // a click (pressed and let go without dragging) picks what is under the mouse
            if (input.LeftClicked) { _pressed = true; _dragged = false; _pressX = input.MouseX; _pressY = input.MouseY; }   // (the click that brings the window to the front picks too: it stops the tour first, so it never sets a spot off to the side as the tour's target)
            if (_pressed && input.LeftDown && Math.Abs(input.MouseX - _pressX) + Math.Abs(input.MouseY - _pressY) > 6) _dragged = true;   // dragging circles round the pick (or looks around if nothing is picked)
            if (_pressed && !input.LeftDown) { _pressed = false; if (!_dragged)
                {
                    if (_matterScale > 0 || _spaceScale > 0) { if (_matter.Pick(input.MouseX, input.MouseY) && !_hud.Visible) { _hud.Visible = true; MenuChanged = true; } }   // in the matter or the gas: what is there (its card shown, even if hidden with H)
                    else if (_ground != null && AboveGround() < Units.Km(MatterView.OverlayKm) && _matter.Pick(input.MouseX, input.MouseY))
                    { if (!_hud.Visible) { _hud.Visible = true; MenuChanged = true; } }   // a grain over the ground: picked, its card shown; the ground itself carries you on down to it (never a second layer of grains)   // a grain over the ground: on into the matter, the same picture, flying to it
                    else
                    {
                        // a click that picks while the tour runs pauses it first, always: the tour's shot had gone on round what the
                        // click picked (a galaxy seen by its light 2.8 billion light-years off), flinging the camera across the universe
                        if (_tour != null) PauseTour("you clicked");
                        Pick(MouseRay(input.MouseX, input.MouseY, w, h), input.MouseX, input.MouseY);
                    }
                } }
            LevelCamera(dt);
            // the camera turns smoothly to face what you picked
            double tdx, tdy, tdz, trad;
            bool amongGrains = _ground != null && _matterScale <= 0 && AboveGround() < Units.Km(MatterView.OverlayKm);
            if (amongGrains && !(_pressed && _dragged) && !TourLooking)
            {
                // among the grains: the view looks down onto the point you dive into (the way you face kept), so it, and what you
                // pick there (which eases to it), sits in the middle of the screen
                double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                double ux = x / r, uy = y / r, uz = z / r; var lk0 = Look; double d0 = lk0.X * ux + lk0.Y * uy + lk0.Z * uz;
                double hx = lk0.X - ux * d0, hy = lk0.Y - uy * d0, hz = lk0.Z - uz * d0, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                if (hl < 1e-6) { hx = -uz; hy = 0; hz = ux; hl = Math.Sqrt(hx * hx + hz * hz); if (hl < 1e-6) { hx = 1; hl = 1; } }
                double dn = Math.Sin(88 * Math.PI / 180), fl = Math.Cos(88 * Math.PI / 180);
                double wx = hx / hl * fl - ux * dn, wy = hy / hl * fl - uy * dn, wz = hz / hl * fl - uz * dn;
                EaseLook(wx, wy, wz, 1 - Math.Exp(-dt * 4));
            }
            else if (_tKind >= 0 && _matterScale <= 0 && _spaceScale <= 0 && !(_pressed && _dragged) && !TourLooking && TargetFromCamera(out tdx, out tdy, out tdz, out trad))   // (in the matter or the gas, an earlier pick out in the world does not turn you)
            {
                double l = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
                if (l > 0 && AtGroundBody())
                {
                    // coming down to the picked planet: from a tenth of its radius the view levels out from its middle toward
                    // its horizon (about 15 degrees below it), keeping the way you face, so you see the land ahead, not your feet
                    double alt = AboveGround() / Units.Km(1), lev = Math.Max(0, Math.Min(1, 1.2 - alt / (_ground.Radius * 0.5)));   // (from about half its radius up: the world's curve and the land ahead, never straight down)
                    lev = lev * lev * (3 - 2 * lev);
                    double ux = -tdx / l, uy = -tdy / l, uz = -tdz / l;                 // up: from the planet's middle to you
                    var lk0 = Look; double dot = lk0.X * ux + lk0.Y * uy + lk0.Z * uz;
                    double hx = lk0.X - ux * dot, hy = lk0.Y - uy * dot, hz = lk0.Z - uz * dot, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    if (hl < 1e-6) { hx = -uz; hy = 0; hz = ux; hl = Math.Sqrt(hx * hx + hz * hz); if (hl < 1e-6) { hx = 1; hl = 1; } }
                    double down = Math.Sin(20 * Math.PI / 180), flat = Math.Cos(20 * Math.PI / 180);
                    double wx = hx / hl * flat - ux * down, wy = hy / hl * flat - uy * down, wz = hz / hl * flat - uz * down;
                    tdx = tdx / l * (1 - lev) + wx * lev; tdy = tdy / l * (1 - lev) + wy * lev; tdz = tdz / l * (1 - lev) + wz * lev;
                    l = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
                }
                if (l > 0 && _tour != null)
                {
                    // on the tour, a slow eased turn, as a camera operator would: eased in as well as out (the turn's speed itself
                    // brought up from rest over its first second and a half whenever a new pick lies off the view, so the last thing
                    // never flies out of view in a snap) and never over about twenty degrees a second, so a pick behind you is
                    // turned to calmly, never whipped round to
                    var lk = Look; double ang = Math.Acos(Math.Max(-1, Math.Min(1, (lk.X * tdx + lk.Y * tdy + lk.Z * tdz) / l)));
                    if (ang < 0.05) _faceT = 0; else _faceT += dt;
                    double fr = Math.Min(1, _faceT / 1.5); fr = fr * fr * (3 - 2 * fr);
                    double rate = 0.6 * (fr + (1 - fr) * Math.Min(1, 0.05 / Math.Max(1e-6, ang)));   // (a small offset is kept closed at full rate; a big one starts slow)
                    double k = 1 - Math.Exp(-dt * rate); if (ang > 1e-6) k = Math.Min(k, 20 * Math.PI / 180 * dt / ang);
                    EaseLook(tdx, tdy, tdz, k);
                }
                else if (l > 0) EaseLook(tdx, tdy, tdz, 1 - Math.Exp(-dt * 5));
            }
            if (AutoNebula)
            {
                // the test path into a nebula: to the galaxy, pick its nearest emission nebula, and roll the wheel on in
                if (_tKind != 5 && _near >= 0 && _features.Count > 0 && NearShare() > 0.3)
                {
                    var g = _galaxies[_near]; int bi = -1; double bd = double.MaxValue;
                    for (int q = 0; q < _features.Count; q++)
                    {
                        if (_features[q][4] != 2) continue;
                        double ux, uy, uz; ToUniverse(g, _features[q][0], _features[q][1], _features[q][2], out ux, out uy, out uz);
                        double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, d = dx * dx + dy * dy + dz * dz;
                        if (d < bd) { bd = d; bi = q; }
                    }
                    if (bi >= 0) { double ux, uy, uz; ToUniverse(g, _features[bi][0], _features[bi][1], _features[bi][2], out ux, out uy, out uz); _tGal = _near; _tKind = 5; _tIndex = bi; _tx = ux; _ty = uy; _tz = uz; _tRadius = _features[bi][3] * g.Scale; _tName = "an emission nebula"; }
                }
                if (_tKind != 5) AimNext();
                input.Wheel += 2;
                _autoLogT += dt;
                if (_autoLogT > 0.5) { _autoLogT = 0; ZoomLog("auto: space " + ScaleHud.Words(_spaceScale * 0.6) + " pick " + _tKind); }
            }
            if (AutoPath)
            {
                // the test path: pick a planet as a click would, roll the wheel in, and write where the camera is each second
                if (_system != null && _tKind != 2 && _ground == null)
                {
                    int pick = -1; for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0 && (pick < 0 || _system.Bodies[i].Interest > _system.Bodies[pick].Interest)) pick = i;   // (the world of most interest, by its template)
                    if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; }
                }
                if (_tKind != 2) AimNext();
                input.Wheel += 1;
                _autoLogT += dt;
                if (_autoLogT > 1) { _autoLogT = 0; try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), PreviewText.AppName.ToLowerInvariant().Replace("-", "") + "_autopath.log"), Title + Environment.NewLine); } catch { } }
            }
            if (input.RightClicked) { _matter.Unpick(); if (_spaceScale <= 0) { _tKind = -1; _tName = ""; } }   // lets go of everything picked (in a nebula's gas the nebula stays where you are)                    // right click: let go of what you picked
            if (input.WasPressed(0x48)) { _hud.Visible = !_hud.Visible; MenuChanged = true; }   // H: the scale on screen, on or off
            if (input.WasPressed(0x42) && (_matterScale > 0 || _spaceScale > 0)) _matter.PickNearest("bonded-atom");   // B: glide across the bond to the next atom
            if (input.WasPressed(Win32.VK_ESCAPE)) return false;
            if (input.LeftDown && (input.MouseDX != 0 || input.MouseDY != 0))
            {
                // one rule for dragging at every scale: the camera goes the way the mouse goes (drag down, the view pans down;
                // drag left, it turns left), by as much as the mouse moved across the view; with something picked, the camera
                // circles round it the same way. The View menu can flip either way.
                int dxm = Settings.Get("invert-left-right", false) ? -input.MouseDX : input.MouseDX, dym = Settings.Get("invert-up-down", false) ? -input.MouseDY : input.MouseDY;
                var d1 = MouseRay(input.MouseX, input.MouseY, w, h); var d0 = MouseRay(input.MouseX - dxm, input.MouseY - dym, w, h);
                var axis = Vec3.Cross(d1, d0); float al = axis.Length;
                if (al > 1e-9f)
                {
                    axis = new Vec3(axis.X / al, axis.Y / al, axis.Z / al);
                    double ang = -Math.Atan2(al, Vec3.Dot(d1, d0)) * Window.DragSpeed;   // the camera goes the way the mouse goes, as fast as View > Drag speed sets
                    var f = Turn(Look, axis, ang);
                    double odx, ody, odz, orad;
                    bool circle = _tKind >= 0 && _tKind != 3 && _dragged && !(_ground != null && (_matterScale > 0 || AboveGround() < Units.Km(MatterView.OverlayKm)))
                                  && !(_spaceScale > 0 && _matter.SpaceKind == 12)   // (in the fall a drag looks round the well: up at the mouth, down the spike to the point, out at the walls; never a circle round the hole, which snapped the view back to its middle)
                                  && TargetFromCamera(out odx, out ody, out odz, out orad) && odx * odx + ody * ody + odz * odz > 0;
                    if (circle)
                    {
                        // round the pick: the camera's place from it turned the same way, still facing it
                        TargetFromCamera(out odx, out ody, out odz, out orad);
                        var o = new Vec3((float)-odx, (float)-ody, (float)-odz); double ol = Math.Sqrt(odx * odx + ody * ody + odz * odz);
                        var on = Turn(new Vec3((float)(o.X / ol), (float)(o.Y / ol), (float)(o.Z / ol)), axis, ang);
                        _ox += on.X * ol + odx; _oy += on.Y * ol + ody; _oz += on.Z * ol + odz;
                        f = new Vec3(-on.X, -on.Y, -on.Z);
                    }
                    SetLook(f);
                }
            }
            Jumped("dragging");
            SpawnRegions(); TakeGalaxies(); HoldInside();                                   // the galaxies round you (their stars made in the background, a few taken to the card each frame), and the edge of the visible universe
            double reach = NearestDistance();
            if (_matterScale <= 0 && _depthKm > 0) { _depthKm = 0; _matterSpot = ulong.MaxValue; _matter.Enclosed = false; }   // (out of the matter: back at the ground)
            if (_matterScale <= 0)
            {
                // W A S D, Q and E: fly like a first-person game, at a speed scaled to the nearest thing (half its distance a
                // second, Shift four times faster), so it feels the same among galaxies as a metre above the ground
                double mf = (input.IsDown(0x57) ? 1 : 0) - (input.IsDown(0x53) ? 1 : 0), mr = (input.IsDown(0x44) ? 1 : 0) - (input.IsDown(0x41) ? 1 : 0), mu = (input.IsDown(0x45) ? 1 : 0) - (input.IsDown(0x51) ? 1 : 0);
                if (mf != 0 || mr != 0 || mu != 0)
                {
                    _tKind = -1;                                                       // flying yourself lets go of the pick
                    var f = Look; var rt = _haveAxes ? _viewRight : Vec3.Cross(f, ViewUp()).Normalized; var upv = _haveAxes ? _viewUp : Vec3.Cross(rt, f).Normalized;
                    double sp = Math.Max(reach, _farScale) * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1);   // at the scale you are zoomed out to
                    _ox += (f.X * mf + rt.X * mr + upv.X * mu) * sp; _oy += (f.Y * mf + rt.Y * mr + upv.Y * mu) * sp; _oz += (f.Z * mf + rt.Z * mr + upv.Z * mu) * sp;
                    double fk = _ground != null ? Math.Max(5e-7, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 0.6 : 0) : 0;
                    if (_ground != null && AboveGround() < Units.Km(MatterView.OverlayKm)) fk = 5e-7;
                    if (_ground != null && AboveGround() < Units.Km(fk))
                    {
                        // never through the ground, nor into its drawn pieces: back up above them
                        double x, y, z; CamFromPlanet(out x, out y, out z);
                        double r = Math.Sqrt(x * x + y * y + z * z), g = _ground.Solid ? Surface(x / r, y / r, z / r) : 0;
                        double want = _ground.Radius + g + fk, k = Units.Km(1);
                        _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    }
                }
            }
            Jumped("W A S D Q E");
            if (_spaceScale > 0)
            {
                // in a nebula's or cluster's gas: the same zoom, kept finely enough for atoms; out past where it began, back to space
                bool falling = _matter.SpaceKind == 12 && _fallHole != null;
                if (falling)
                {
                    // the fall is a travel first and a zoom last: the way in from the horizon to the point (millions of light-years, the
                    // fall's own measure) is run with the view held at the horizon's own scale, the walls of the well closing in round you
                    // as it goes, each notch of the wheel a step of the way; only once the walls have met at the point does the wheel
                    // zoom the view on into it, down to the quarks' size, where the singularity shows. Back out the same way
                    const double NotchTenfolds = 0.060480747;                                     // a notch: 13% of the scale, in tenfolds
                    if (input.Wheel != 0)
                    {
                        bool zooming = _fallShare >= 1 && (input.Wheel > 0 || _spaceScale < _fallScale0 * 0.999);
                        if (zooming) _spaceScale = Math.Min(_fallScale0, Math.Max(MatterView.BottomKm, _spaceScale * Math.Pow(0.87, input.Wheel * 3)));
                        else _fallShare = Math.Max(-0.001, Math.Min(1, _fallShare + input.Wheel * NotchTenfolds / MatterView.FallTenfolds));
                        if (_fallShare < 1) _spaceScale = _fallScale0;
                    }
                    if (_fallShare < 0) { _spaceScale = 0; _fallHole = null; falling = false; }   // climbed back out past the horizon
                }
                else
                {
                    if (input.Wheel != 0) _spaceScale *= Math.Pow(0.87, input.Wheel);
                    if (input.Wheel > 0) _matter.Landmarks(_spaceScale);                      // (on a body's ground: the wheel comes down onto an atom, its nucleus, a nucleon, a quark, as on a world's)
                    if (_spaceScale > _spaceTop) _spaceScale = 0; else _spaceScale = Math.Max(MatterView.BottomKm, _spaceScale);
                }
                if (falling)
                {
                    // the fall's clock: the whole way in takes two thirds of rs over c of your own time, run with the way
                    double rsKm0 = _matter.HoleRs, tauEnd = 2.0 / 3 * rsKm0 / 299792.458;
                    _fallTau = tauEnd * Math.Max(0, _fallShare);
                    { double rK = rsKm0 * Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds); _matter.SetFallClock(_fallTau, _fallRate, Math.Sqrt(rsKm0 / Math.Max(1e-300, rK))); _matter.SetFallShare(Math.Max(0, _fallShare)); }
                    // the fall is travel as well as a zoom: from the horizon to the middle the camera itself goes down the axis it came in
                    // on, always at the view's own distance from the middle, the scale shrinking with it (both at once); climbing out it
                    // comes back up the same way, and leaves the dive just outside the horizon
                    double rr = Units.Km(Math.Max(_spaceScale, MatterView.BottomKm) * 0.6);
                    if (_spaceScale <= 0) rr = Units.Km(_spaceTop * 0.6);
                    _ox = RelX(_fallHole) - _fallAxis.X * rr; _oy = RelY(_fallHole) - _fallAxis.Y * rr; _oz = RelZ(_fallHole) - _fallAxis.Z * rr;
                    if (_spaceScale <= 0) _fallHole = null;
                }
                if (input.WasPressed(0x20)) TogglePause();
                if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);
                if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);
                _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);
                if (TimeStep > 0) _matterClock += dt;
                return true;
            }
            if (_matterScale > 0)
            {
                // below a millimetre or so: the same zoom, with your height kept finely enough for atoms (the wheel moves
                // you the same share of the way each notch; W A S D Q E fly at the same speed for the scale; time runs on)
                if (input.Wheel != 0) _matterScale *= Math.Pow(0.87, input.Wheel);
                if (input.Wheel > 0) _matter.Landmarks(_matterScale, _tour == null);                     // (the wheel goes down onto the landmark at each size: an atom, its nucleus, a nucleon, a quark; never between them into nothing)
                {
                    double mf = (input.IsDown(0x57) ? 1 : 0) - (input.IsDown(0x53) ? 1 : 0), mr = (input.IsDown(0x44) ? 1 : 0) - (input.IsDown(0x41) ? 1 : 0), mu = (input.IsDown(0x45) ? 1 : 0) - (input.IsDown(0x51) ? 1 : 0);
                    if (mf != 0 || mr != 0 || mu != 0)
                    {
                        double sp = _matterScale * 0.6 * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1);   // half your height a second, as above the ground
                        double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                        var upL = new Vec3((float)(x / r), (float)(y / r), (float)(z / r));
                        var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
                        var lk = Look; double fe = Vec3.Dot(lk, east), fn = Vec3.Dot(lk, north), fl = Math.Sqrt(fe * fe + fn * fn);
                        if (fl < 1e-6) { fe = 1; fn = 0; fl = 1; }
                        fe /= fl; fn /= fl;
                        _matter.Move((fe * mf + fn * mr) * sp, (fn * mf - fe * mr) * sp);
                        if (mu != 0) _matterScale *= Math.Exp(mu * 0.5 * dt * (input.IsDown(0x10) ? 4 : 1));
                    }
                }
                DigStep(input, dt);                                                       // Page Down / Page Up: down through the world's layers to its middle, and back
                if (input.WasPressed(0x20)) TogglePause();
                if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);
                if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);
                _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);
                if (TimeStep > 0) _matterClock += dt;
                if (_matterScale > _matterTop)
                {
                    // high enough again for the plain height: the camera goes on from exactly the height the fine one had
                    double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z), k = Units.Km(1);
                    double want = _ground.Radius + (_ground.Solid ? Surface(x / r, y / r, z / r) : 0) + _matterScale * 0.6;
                    _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    _matterScale = 0;
                }
                else _matterScale = Math.Max(MatterView.BottomKm, _matterScale);
                ChooseNear(); LocalStars(); ChooseSystem(); FollowBody(); ChoosePlanet();
                return true;
            }
            if (_ground != null && input.Wheel > 0 && AboveGround() < Units.Km(HandoffKm()))   // (the ground itself, its grains its own, carries you down to about 2 micrometres: on one grain's face)
            {
                // at the finest ground: on down into what it is made of
                double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
                bool sea = _ground.HasWater && _ground.GroundAt(x / r, y / r, z / r) < 0, ice = _ground.IcyAt(x, y, z, double.NaN);
                SetMatter();
                _matterScale = Math.Max(MatterView.BottomKm, AboveGround() / Units.Km(1) / 0.6);   // the same picture carries on: you are 0.6 of the scale above it
                _matterTop = Math.Max(MatterView.TopKm, _matterScale * 1.1);
                return true;
            }
            if (_tKind == 7 && input.Wheel > 0 && _spaceScale <= 0 && _matterScale <= 0)
            {
                double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && Math.Sqrt(qx * qx + qy * qy + qz * qz) < Units.Km(1))
                {
                    var lay = LayerHere(); ulong lspot = (ulong)(_tx / Units.Km(1)) ^ (ulong)(_tz / Units.Km(1)) * 31UL;
                    string air; if (!PlanetTerrain.Air.TryGetValue(_ground != null ? _ground.Kind : "", out air)) air = "hydrogen 90%, helium 10%";
                    var slh = StarLayerHere();
                    if (slh != null) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), shr = sdK / Math.Max(1e-30, StarRadiusNow); _matter.SetPlasma(lspot, slh.MakeUp ?? SystemStarMakeUp(), LayerDensity(slh, shr), slh.Name.ToLowerInvariant(), LayerTempK(slh, shr)); }
                    else if (lay != null && lay.Matter == "plasma") _matter.SetPlasma(lspot);
                    else if (lay != null && lay.Matter == "ice") { _matter.SetAir("water vapour 70%, ammonia 15%, methane 15%", lspot); }   // hot dense water, ammonia and methane
                    else _matter.SetAir(air, lspot);
                    _spaceScale = Math.Max(MatterView.BottomKm, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;
                    return true;
                }
            }
            if (_system != null && _ground == null && input.Wheel > 0 && _spaceScale <= 0 && Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) < StarRadiusNow * 1.002)
            {
                // into the star's surface: its plasma, the same zoom carrying on
                ulong sspot = (ulong)(long)(_ox / Units.Km(1e3)) * 73856093UL ^ (ulong)(long)(_oy / Units.Km(1e3)) * 19349663UL ^ (ulong)(long)(_oz / Units.Km(1e3)) * 83492791UL;
                {
                    // its layer at this depth (content/stars.unt @layer): its own make-up, heat and density there, the photosphere at the face
                    double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), shr = Math.Min(1, sdK / Math.Max(1e-30, StarRadiusNow)); var sl = StarLayerAt(shr);
                    if (sl != null) _matter.SetPlasma(sspot, sl.MakeUp ?? SystemStarMakeUp(), LayerDensity(sl, shr), sl.Name.ToLowerInvariant(), LayerTempK(sl, shr));
                    else _matter.SetPlasma(sspot, SystemStarMakeUp());                      // (its own make-up: content/stars.unt)
                }
                _spaceScale = 1.0 / 0.6; _spaceTop = _spaceScale * 1.1;                   // from a kilometre: a fog of plasma, down to its nuclei
                return true;
            }
            if (_tKind == 6 && _spaceScale <= 0 && _matterScale <= 0 && _tIndex >= 0 && _tIndex < _extras.Count && (int)(_lastDrawTime * 2) != _diveLogTick)
            {
                // the dive's state, twice a second, so a dive that does not come says why in the log: the body, whether it is the one
                // picked, how far its middle is against its radius and its stone's radius here, the wheel, the course, whether it is drawn as its stone
                _diveLogTick = (int)(_lastDrawTime * 2); var exl = _extras[_tIndex]; double lx = RelX(exl) - _ox, ly = RelY(exl) - _oy, lz = RelZ(exl) - _oz, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                double lrD = exl.Hole || exl.Shape.Length == 0 ? exl.R : BodyRadiusToward(exl, _lastDrawTime, -lx / Math.Max(1e-300, ll), -ly / Math.Max(1e-300, ll), -lz / Math.Max(1e-300, ll));
                ZoomLog("DIVE check: " + exl.Name + ", same as the pick " + (exl == _tExtra) + ", middle " + ScaleHud.Words(ll / Units.Km(1)) + " off, nominal R " + ScaleHud.Words(exl.R / Units.Km(1)) + " (pick R " + ScaleHud.Words(_tRadius / Units.Km(1)) + "), stone R here " + ScaleHud.Words(lrD / Units.Km(1)) + ", wheel " + input.Wheel.ToString("0.00") + ", on course " + OnCourse().ToString("0.00") + ", over pick " + OverPick().ToString("0.00") + ", drawn as stone " + ShapeShown(exl, _lastH) + ", bodies " + _extras.Count);
            }
            if (_tKind == 6 && input.Wheel > 0 && _spaceScale <= 0 && _matterScale <= 0 && _tIndex >= 0 && _tIndex < _extras.Count)
            {
                // at a picked body: on in to what it is made of, the same zoom. A small body (an asteroid, a comet's nucleus, a Kuiper
                // body, a rogue planet): onto its surface, its own grains, as a world's ground is dived into (from a centimetre, where
                // the grains show). A neutron star: into its crust. A black hole: through the horizon, the fall to the singularity
                var exd = _extras[_tIndex]; double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && qr > 0)
                {
                    double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                    double cxv = RelX(exd) - _ox, cyv = RelY(exd) - _oy, czv = RelZ(exd) - _oz, cl = Math.Max(1e-300, Math.Sqrt(cxv * cxv + cyv * cyv + czv * czv));   // (from the body's middle: a spot picked on it only steers the way down; the ground is what is under you)
                    double qrD = exd.Hole || exd.Shape.Length == 0 ? qr : BodyRadiusToward(exd, _lastDrawTime, -cxv / cl, -cyv / cl, -czv / cl);   // the stone's real surface under you, not its nominal sphere
                    ulong dspot = (ulong)(long)(_ox / Units.Km(1)) * 73856093UL ^ (ulong)(long)(_oy / Units.Km(1)) * 19349663UL ^ (ulong)(long)(_oz / Units.Km(1)) * 83492791UL;
                    if (ql < (exd.Hole && exd.Mass > 0 ? Units.Km(Physics.HorizonKm(exd.Mass)) * 1.25 : double.MaxValue) && (exd.Hole || cl < Math.Max(qr, qrD) * 1.3))   // (within 1.3 of its nominal radius, where the zoom's own pacing, which stops at 1.2 of it, can reach: measured against the drawn stone alone, a rock squashed along this side was never reached, and the dive never fired)
                    {
                        if (exd.Hole && exd.Mass > 0)
                        {
                            double rsKm = Physics.HorizonKm(exd.Mass);
                            _matter.SetHole(dspot, rsKm, exd.Mass, exd.Spin, _wayInLy * Math.Pow(Math.Max(1e-6, exd.Mass) / _wayInMass, _wayInPower) * 9.4607e12); _bodyUp = null; _spaceScale = rsKm / 0.6; _spaceTop = _spaceScale * 1.1; _fallHole = exd;
                            // the fall's clock: from the horizon to the middle takes two thirds of rs over c of your own time (26 seconds for a
                            // four-million-solar-mass hole, 70 microseconds for a stellar one, eleven minutes for a hundred-million-solar-mass one); it is
                            // shown over about thirty seconds, slowed or sped as that needs, and the card says by how much
                            { double tauEnd = 2.0 / 3 * rsKm / 299792.458; _fallTau = 0; _fallRate = Math.Max(1, tauEnd / 50.0); _fallShare = 0; _fallScale0 = _spaceScale; }   // (the stretch on the hole's own clock, from the horizon to a thousandth of it, shown in about eight seconds whatever the hole: a big hole's seven hundred seconds took a minute; never slower than real)
                            { double ql2 = Math.Max(1e-300, ql); _fallAxis = new Vec3((float)(qx / ql2), (float)(qy / ql2), (float)(qz / ql2)); _fallTh = -1; }   // the axis you came in on: the well's grid is drawn down it; the look is left as it was (at the hole: down the axis), never snapped round (the hole had flown out of view in a snap)
                            ZoomLog("DIVE through the horizon of " + exd.Name + " (" + ScaleHud.Words(rsKm * 2) + " across)"); return true;
                        }
                        if (exd.Pl != null && (exd.Pl.Id.Contains("neutron") || exd.Pl.Id.Contains("pulsar") || exd.Pl.Id.Contains("magnetar")))
                        {
                            _matter.SetNeutronCrust(dspot); _bodyUp = null; _spaceScale = 1e-3 / 0.6; _spaceTop = _spaceScale * 1.1;
                            ZoomLog("DIVE into the crust of " + exd.Name); return true;
                        }
                        if (exd.Mix != null && (exd.Shape.Length == 0 || _ground == null || _planet != -2))
                        {
                            double ux = -cxv / cl, uy = -cyv / cl, uz = -czv / cl;                 // from the body's middle to you: its surface here, under you
                            double sx = _ax - exd.X, sy = _ay - exd.Y, sz = _az - exd.Z, sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);   // its star, for the light on the grains
                            _matter.Set(exd.Mix, false, false, exd.Cr, exd.Cg, exd.Cb, dspot);
                            _matter.SetGround(qrD / Units.Km(1), ux, uy, uz, exd.Mix, dspot * 0x9E3779B97F4A7C15UL + 0x51UL, 0, 0); exd.SpinHold = BodySpin(exd, _lastDrawTime);   // (its ground at the drawn stone's own height here; its spin held while you are on it)
                            _matter.SetAirAbove(null);
                            _bodyUp = new[] { ux, uy, uz }; _bodyStar = sl > 0 ? new[] { sx / sl, sy / sl, sz / sl } : new[] { ux, uy, uz }; _bodyUpGen = _matter.Generation;
                            double hgt = Math.Max(cl - qrD, Units.Km(MatterView.OverlayKm * 2)); _ox = RelX(exd) + ux * (qrD + hgt); _oy = RelY(exd) + uy * (qrD + hgt); _oz = RelZ(exd) + uz * (qrD + hgt);   // (the camera set just over the drawn stone under it, never inside a stone that reaches past its nominal sphere)
                            _spaceScale = Math.Max(MatterView.OverlayKm / 0.6, hgt / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;   // (from your real height over its skin, the rock still drawn behind: the zoom goes on down to the grains at its own pace, no jump to a centimetre)
                            ZoomLog("DIVE onto " + exd.Name + ": its ground " + exd.Mix + " from " + ScaleHud.Words(_spaceScale * 0.6) + " up"); return true;
                        }
                    }
                }
            }
            if (_tKind == 5 && input.Wheel > 0)
            {
                // into a picked nebula or cluster: once within a kilometre of the point you fly to, on into its gas, the same zoom
                double qx, qy, qz, qr;
                if (TargetFromCamera(out qx, out qy, out qz, out qr) && Math.Sqrt(qx * qx + qy * qy + qz * qz) < Units.Km(1))
                {
                    var gq = _galaxies[_near];
                    if (_tIndex < 0 || _tGal != _near) _matter.SetSpace(9, (ulong)(_tx / Units.Km(1e6)) ^ gq.Seed, 1);   // the space between the stars
                    else
                    {
                        int fk = (int)_features[_tIndex][4];
                        var fq = _features[_tIndex]; double cux, cuy, cuz; ToUniverse(gq, fq[0], fq[1], fq[2], out cux, out cuy, out cuz);
                        double fromC = Math.Sqrt((_tx - cux) * (_tx - cux) + (_ty - cuy) * (_ty - cuy) + (_tz - cuz) * (_tz - cuz)) / Math.Max(1e-30, fq[3] * gq.Scale);   // how far from its middle you dive in
                        _matter.SetSpace(fk, (ulong)(_tIndex * 7919 + 1) ^ gq.Seed, Math.Min(1, fromC), fq.Length > 5 ? ((fk == 2 || fk == 4 || fk == 5) && NebulaGas.Loaded ? NebulaGas.Atoms(fk, fq[5]) : SpaceGen.Composition(fk, fq[5])) : null);   // (a glowing gas's atoms by count, every element: src/gen/NebulaGas.cs)
                    }
                    _spaceScale = Math.Max(MatterView.BottomKm, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1) / 0.6); _spaceTop = _spaceScale * 1.1;
                    return true;
                }
            }
            if (input.Wheel != 0)
            {
                // fly along the line under the mouse, by a share of the distance to the nearest thing: slower as it comes close, never ending;
                // the tour's own zooms fly along the view itself (where the mouse happened to rest had steered them: with nothing picked
                // after standing on the ground, the zoom down to the grains crept sideways at the height of a hair and never came down)
                var dir = Touring && !TourPaused ? Look : MouseRay(input.MouseX, input.MouseY, w, h);
                double fdx, fdy, fdz, frad;
                if (_tKind >= 0 && TargetFromCamera(out fdx, out fdy, out fdz, out frad))
                {
                    // flying to what you picked: straight at it, a share of the way to its surface each notch
                    double fl = Math.Sqrt(fdx * fdx + fdy * fdy + fdz * fdz);
                    // the steps go by the distance to what you picked only: stars you fly past no longer hold them back
                    if (fl > 0)
                    {
                        dir = new Vec3((float)(fdx / fl), (float)(fdy / fl), (float)(fdz / fl));
                        // at the picked planet's ground, the steps go by the height above the ground, all the way down;
                        // from afar, by the distance to just outside it
                        bool atIt = AtGroundBody();
                        reach = atIt ? AboveGround() : Math.Max(fl - (_tKind == 5 ? 0 : _tKind == 6 && _tExtra != null && _tExtra.Hole && _tExtra.Mass > 0 ? Units.Km(Physics.HorizonKm(_tExtra.Mass)) * 1.08 : frad * 1.2), fl * 1e-6);   // clusters and nebulae are clouds: flown into, not stopped at; a black hole: down to just outside its horizon
                    }
                }
                // a star close to the line you point along (within about 4 degrees) is flown at straight, not past
                if (_tKind < 0) { double tdist; var tdir = AimStar(dir, out tdist); if (input.Wheel > 0 && tdist > 0) { dir = tdir; reach = Math.Min(reach, tdist); } }
                double step = reach * (1 - Math.Pow(0.87, Math.Abs(input.Wheel))) * Math.Sign(input.Wheel);
                // out: each notch a share of how far out you have already zoomed (not only of the nearest thing), so going on out
                // grows without end, to the whole universe; in: that scale comes back down toward the nearest thing
                if (input.Wheel < 0)
                {
                    double basis = Math.Max(Math.Max(reach, Units.Km(1e-9)), _farScale);
                    double most = _uRadius * 0.1;                                                  // never so far out that the universe is one ball: always inside it, among the web
                    if (basis >= most) { step = 0; _farScale = most; }
                    else { double nb = Math.Min(most, basis * Math.Pow(1 / 0.87, -input.Wheel)); step = -(nb - basis); _farScale = nb; }
                }
                else { _farScale *= Math.Pow(0.87, input.Wheel); if (_farScale < reach) _farScale = 0; }
                double altBefore = _ground != null ? AboveGround() / Units.Km(1) : 0;
                                if (WheelLogDue()) ZoomLog("wheel " + input.Wheel.ToString("0.###") + " off " + (OffTarget() * 180 / Math.PI).ToString("0.#") + " deg step " + Units.Describe(Math.Abs(step)) + " reach " + Units.Describe(reach) + " pick " + _tKind + ":" + _tIndex + " planet " + _planet + " nearest-body " + _cpBest + " at " + _cpBd.ToString("0.##") + " radii follow " + _followBody + (_ground != null ? " ground-ready " + (_ground.ReadyStep < 1e30 ? ScaleHud.Words(_ground.ReadyStep) : "none") + " height " + ScaleHud.Words(AboveGround() / Units.Km(1)) + " base " + BaseHeightWords() + " piece " + (_ground.CamPieceEdge > 0 ? ScaleHud.Words(_ground.CamPieceEdge) + " L" + _ground.CamPieceLevel : "none") + " [" + _ground.BuildStats + "; " + _ground.FrameStats + "]" : ""));
                _ox += dir.X * step; _oy += dir.Y * step; _oz += dir.Z * step;
                if (_ground != null)
                {
                    // slow down near the finest ground already made under you (so the rough first pieces are not stretched
                    // across the screen), but never push you back and always let each notch close in a tenth, so the finer
                    // ground is asked for and catches up; never below half a millimetre
                    double ready = _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 3 : 0;
                    // ...but never inside the drawn ground: its pieces are flat triangles about ReadyStep across that can stand above
                    // the true ground, so stay at least 0.6 of a step above it until finer pieces are made (close enough to ask for them)
                    double floorKm = Math.Max(5e-7, Math.Min(ready, Math.Max(altBefore * 0.9, ready / 3 * 0.6)));
                    if (altBefore < MatterView.OverlayKm) floorKm = Math.Max(1e-9, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 1.2 : 1e-9);   // below a centimetre the grains cover the ground, but the drawn pieces are flat facets a cell across that can stand above the true ground: never under them (the sky showed through the planet), the matter takes over first                 // below a centimetre the grains cover the ground: no waiting, on down past the 30 micrometres where the matter takes over
                    {
                        // over the sea: no lower than a metre above the water, until the dive under the sea is in the universe view
                        double cx0, cy0, cz0; CamFromPlanet(out cx0, out cy0, out cz0); double r0 = Math.Sqrt(cx0 * cx0 + cy0 * cy0 + cz0 * cz0);
                        // over the sea: the water's surface is the floor until you are a metre from it, then the sea bed is (the dive)
                        if (_ground.HasWater && !_underSea && _ground.GroundAt(cx0 / r0, cy0 / r0, cz0 / r0) < 0) floorKm = Math.Max(floorKm, 5e-7);
                    }
                    if (AboveGround() < Units.Km(floorKm))
                    {
                        double x, y, z; CamFromPlanet(out x, out y, out z);
                        double r = Math.Sqrt(x * x + y * y + z * z), g = _ground.Solid ? Surface(x / r, y / r, z / r) : 0;
                        double want = _ground.Radius + g + floorKm, k = Units.Km(1);
                        _ox = _px + x / r * want * k; _oy = _py + y / r * want * k; _oz = _pz + z / r * want * k;
                    }
                }
            }
            Jumped("the wheel");
            if (input.WasPressed(0x20)) TogglePause();                                 // Space: pause, or carry on
            if (input.WasPressed(0xBC)) SetTime(TimeStep - 1);                        // , slower
            if (input.WasPressed(0xBE)) SetTime(TimeStep + 1);                        // . faster
            _simYears += dt * TimeSpeeds[TimeStep]; SpaceGen.StarSwell = StarSwell; _matter.SetClock(_simYears * 31557600.0, TimeSpeeds[TimeStep] * 31557600.0, TimeNames[TimeStep]);                                    // time runs at the speed chosen
            if (TimeStep > 0) _matterClock += dt;
            ChooseNear(); Jumped("ChooseNear"); ClusterStars();
            LocalStars(); Jumped("LocalStars");
            ChooseSystem(); Jumped("ChooseSystem");
            // ride along with the body first, then see which one you are at: planets move about a hundred thousand km a frame,
            // so choosing before riding along saw you many radii behind (a moon never got its ground, and flickered)
            FollowBody(); Mark();                                                // riding along is meant to move you far: not logged as a jump
            ChoosePlanet(); Jumped("ChoosePlanet");
            return true;
        }

        // the star nearest the line along dir (local stars, or the open system's star): its direction and distance (else 0)
        private Vec3 AimStar(Vec3 dir, out double dist)
        {
            dist = 0; var best = dir; double bestCos = Math.Cos(4 * Math.PI / 180);
            if (_system != null)
            {
                // in a system: the star or any planet or moon near the line you point along (the one nearest the line wins)
                PlaceSystem();
                for (int i = 0; i < _bodyCount; i++)
                {
                    double r = i == 0 ? StarRadiusNow : _system.Bodies[i - 1].RadiusUnits;
                    double dx = _bodyVerts[i].X - _ox, dy = _bodyVerts[i].Y - _oy, dz = _bodyVerts[i].Z - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (l <= 0) continue;
                    double c = (dx * dir.X + dy * dir.Y + dz * dir.Z) / l;
                    if (c > bestCos) { bestCos = c; best = new Vec3((float)(dx / l), (float)(dy / l), (float)(dz / l)); dist = Math.Max(l - r * 1.5, r * 0.05); }
                }
                return best;
            }
            if (_near < 0) return best;
            var g = _galaxies[_near];
            for (int k = 0; k < _localCount; k++)
            {
                var s0 = _localList[k]; double ux, uy, uz; ToUniverse(g, s0.X + _localOX, s0.Y + _localOY, s0.Z + _localOZ, out ux, out uy, out uz);
                double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (l <= 0) continue;
                double c = (dx * dir.X + dy * dir.Y + dz * dir.Z) / l;
                if (c > bestCos) { bestCos = c; best = new Vec3((float)(dx / l), (float)(dy / l), (float)(dz / l)); dist = l; }
            }
            return best;
        }

        // v turned by angle a (radians) round the unit axis k
        // the tour's own turn: by exact fractions of a degree each frame, smooth (the mouse moves in whole pixels, which at a
        // slow turn would jump a little every few frames: a judder that makes people sick); round the pick as a drag circles it
        private void TourTurn(double yawRad, double pitchRad)
        {
            var lk = Look; var upv = LevelUp(); var rt = Vec3.Cross(lk, ViewUp()).Normalized;
            // close over the picked world (under half its radius up) the view turns where you are, never circling the whole world
            bool overIt = AtGroundBody() && AboveGround() / Units.Km(1) < _ground.Radius * 0.5;
            for (int pass = 0; pass < 2; pass++)
            {
                double ang = pass == 0 ? yawRad : pitchRad; if (Math.Abs(ang) < 1e-12) continue;
                double odx, ody, odz, orad;
                bool circle = _tKind >= 0 && _tKind != 3 && !overIt && !(_ground != null && (_matterScale > 0 || AboveGround() < Units.Km(MatterView.OverlayKm)))
                              && TargetFromCamera(out odx, out ody, out odz, out orad) && odx * odx + ody * ody + odz * odz > 0
                              && Math.Sqrt(odx * odx + ody * ody + odz * odz) <= Math.Max(orad * 12, NearestDistance() * 3);   // (a film circles only what is close and fills the view; far off, circling would sweep the camera across whole stretches of space, everything near streaming past: there it turns where it is)
                // circling round a pick: about the view's own up (always square to the line to it, so it never rolls in place when
                // seen from straight above); turning where you are: about the level up (the horizon stays level), or the view's own up
                // when looking nearly along it
                var axis = pass == 1 ? rt : circle || Math.Abs(Vec3.Dot(lk, upv.Normalized)) > 0.95f ? ViewUp() : upv.Normalized;
                var f = Turn(Look, axis, ang);
                if (circle)
                {
                    TargetFromCamera(out odx, out ody, out odz, out orad);
                    var o = new Vec3((float)-odx, (float)-ody, (float)-odz); double ol = Math.Sqrt(odx * odx + ody * ody + odz * odz);
                    var on = Turn(new Vec3((float)(o.X / ol), (float)(o.Y / ol), (float)(o.Z / ol)), axis, ang);
                    _ox += on.X * ol + odx; _oy += on.Y * ol + ody; _oz += on.Z * ol + odz;
                    f = new Vec3(-on.X, -on.Y, -on.Z);
                }
                SetLook(f);
            }
        }
        // how far (radians) the view is from facing what is picked; 0 with nothing picked
        // how far on course the tour's flight is (1 looking at what it flies to, easing to 0 by 0.12 radians off): it slows as
        // it turns and speeds as it comes round, never starting or stopping all at once
        // in the matter, how far over what is picked the view is: the tour slows as it lines up (to a quarter, never stopped,
        // so it never looks frozen) and speeds again once over it
        // below a giant's cloud tops (it has no ground: inside, only its gas)
    }
}
