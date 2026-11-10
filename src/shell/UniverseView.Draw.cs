// Universe · shell · the universe view, its part: draw (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── the start: held on a loading screen until what you see first is made (the web's light, the galaxies made in full
        // round you with their stars), so the universe never opens half made and filling in; never more than a minute ──
        private int _glowMade, _glowWant = 1; private bool _loadDone; private double _loadT, _loadShare; private string[] _loadLines;
        /// <summary>Still making what is seen first, at the start.</summary>
        public bool Loading { get { return !_loadDone; } }
        /// <summary>The recording notice over the picture (drawn after the frame is taken for the clip, so it is not in it).</summary>
        public void DrawRec(int w, int h, double time, double seconds) { _gpu.ClearDepth(); _hud.DrawRec(_meshes, w, h, time, seconds); }
        private void LoadProgress(double dt)
        {
            if (_loadDone) return; _loadT += dt;
            double web = _glowAll != null ? 1 : Math.Min(0.99, _glowMade / (double)Math.Max(1, _glowWant));
            int near = 0, made = 0;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz;
                if (dx * dx + dy * dy + dz * dz > _nearUnits * _nearUnits * 2.25) continue;
                near++; if (g.Far != null) made++;
            }
            double gal = near == 0 ? (_loadT > 3 ? 1 : 0) : made / (double)near;
            _loadShare = web * 0.65 + gal * 0.35;
            _loadLines = new[] { "Making the universe", web < 1 ? "the cosmic web: " + ((int)(web * 100)) + "% of its light" : "the cosmic web: ready", "the galaxies round you: " + made + " of " + Math.Max(near, made) };
            if ((web >= 1 && gal >= 1 && _loadT > 1.5) || _loadT > 60) { _loadDone = true; MenuChanged = true; }   // (held a moment and a half at the least: a bar that flashes by on a fast machine is a bar never seen)
        }
        private bool InsideGiant { get { if (_ground == null || _ground.Solid || _planet < 0) return false; double x, y, z; CamFromPlanet(out x, out y, out z); return Math.Sqrt(x * x + y * y + z * z) < _ground.Radius + _ground.CloudKm * 0.9; } }
        private double OverPick() { if (_matterScale <= 0 && _spaceScale <= 0) return 1; double u = Math.Max(0, Math.Min(1, (0.6 - _matter.PickOff) / 0.4)); u = u * u * (3 - 2 * u); return 0.25 + 0.75 * u; }
        private double OnCourse() { double u = Math.Max(0, Math.Min(1, (0.12 - OffTarget()) / 0.08)); return u * u * (3 - 2 * u); }
        private double OffTarget()
        {
            if (AtGroundBody()) return 0;          // (coming down to the picked world the view looks ahead to its horizon, not at its middle: that is on course)
            if (_spaceScale > 0) return 0;                                               // (dived into a body or a hole: the zoom goes on wherever you look; the fall is watched looking back at the horizon)
            double dx, dy, dz, r; if (_tKind < 0 || !TargetFromCamera(out dx, out dy, out dz, out r)) return 0;
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0) return 0; var lk = Look;
            double off = Math.Acos(Math.Max(-1, Math.Min(1, (dx * lk.X + dy * lk.Y + dz * lk.Z) / l)));
            if (_tKind == 3 && _ground != null) off = Math.Max(0, off - 0.5);           // a spot on the ground: within about thirty degrees of the look is on course (coming down onto it the view looks down and the spot is under you a little to one side, and that angle grows as the height shrinks: it stood still at a centimetre, the spot a few degrees past the limit)
            return off;
        }

        private static Vec3 Turn(Vec3 v, Vec3 k, double a)
        {
            float c = (float)Math.Cos(a), sn = (float)Math.Sin(a), d = Vec3.Dot(k, v); var x = Vec3.Cross(k, v);
            return new Vec3(v.X * c + x.X * sn + k.X * d * (1 - c), v.Y * c + x.Y * sn + k.Y * d * (1 - c), v.Z * c + x.Z * sn + k.Z * d * (1 - c)).Normalized;
        }

        // the view's up: the universe's up in space; near a planet's ground, the planet's own up (so its horizon is level)
        // on a world, how far above its horizon you look (radians, up from the planet's middle through you); off a world, the
        // view's own pitch
        private double LocalPitch()
        {
            if (_ground == null) return Math.Asin(Math.Max(-1, Math.Min(1, _fy)));
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return Math.Asin(Math.Max(-1, Math.Min(1, _fy)));
            var lk = Look; return Math.Asin(Math.Max(-1, Math.Min(1, (lk.X * x + lk.Y * y + lk.Z * z) / r)));
        }
        // turns the view up or down toward a pitch above the horizon (on a world, its own horizon), eased by k
        private void TourPitchToward(double want, double k) { TourTurn(0, (want - LocalPitch()) * k); }
        // the camera's own up, kept from frame to frame and square to the way it looks: looking straight up or down it simply
        // holds, never flipping (no gimbal lock)
        private Vec3 ViewUp()
        {
            var f = Look; var u = _camUp; float d = Vec3.Dot(u, f);
            var p = new Vec3(u.X - f.X * d, u.Y - f.Y * d, u.Z - f.Z * d);
            if (p.Length < 1e-4f) { var a = Math.Abs(f.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0); float e = Vec3.Dot(a, f); p = new Vec3(a.X - f.X * e, a.Y - f.Y * e, a.Z - f.Z * e); }
            return p.Normalized;
        }
        // each frame the camera's up eases toward level (the universe's up; near a world, its own up), except while looking
        // nearly along it, where it holds as it is
        private void LevelCamera(double dt)
        {
            var f = Look; var want = LevelUp(); float d = Vec3.Dot(want, f);
            var p = new Vec3(want.X - f.X * d, want.Y - f.Y * d, want.Z - f.Z * d); float pl = p.Length;
            var u = ViewUp();
            if (pl > 1e-4f)
            {
                double w = Math.Max(0, Math.Min(1, (pl - 0.05) / 0.25)) * (1 - Math.Exp(-dt * 10)); p = new Vec3(p.X / pl, p.Y / pl, p.Z / pl);
                u = new Vec3(u.X + (p.X - u.X) * (float)w, u.Y + (p.Y - u.Y) * (float)w, u.Z + (p.Z - u.Z) * (float)w);
            }
            _camUp = u; _camUp = ViewUp();
        }
        // the frame a shot is set in: level up, or at a feeding black hole its disc's axis, so a shot's rise is measured from the
        // disc (rise 0 skims it, a small rise grazes over it, a dive below sees its underside)
        private Vec3 ShotUp()
        {
            if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count && _extras[_tIndex].Hole) { var ex = _extras[_tIndex]; return new Vec3(0, (float)Math.Cos(ex.Tilt), (float)Math.Sin(ex.Tilt)); }
            return LevelUp();
        }
        private Vec3 LevelUp()
        {
            var wu = new Vec3(0, 1, 0);
            if (_ground == null) return wu;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return wu;
            double alt = r - _ground.Radius, k = Math.Max(0, Math.Min(1, 1 - alt / (_ground.Radius * 0.1)));
            var lu = new Vec3((float)(x / r), (float)(y / r), (float)(z / r));
            var u = new Vec3(wu.X * (float)(1 - k) + lu.X * (float)k, wu.Y * (float)(1 - k) + lu.Y * (float)k, wu.Z * (float)(1 - k) + lu.Z * (float)k);
            return u.Normalized;
        }

        private Vec3 _viewRight, _viewUp; private bool _haveAxes;
        private Vec3 MouseRay(int mx, int my, int w, int h)
        {
            double nx = mx / (double)Math.Max(1, w) * 2 - 1, ny = 1 - my / (double)Math.Max(1, h) * 2, t = Math.Tan(0.45), aspect = w / (double)Math.Max(1, h);
            // the screen's own right and up, as the view was last drawn (so the line goes exactly through the pixel clicked)
            var f = Look; var right = _haveAxes ? _viewRight : Vec3.Cross(f, ViewUp()).Normalized; var up = _haveAxes ? _viewUp : Vec3.Cross(right, f).Normalized;
            return (f + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
        }

        // the galaxy whose real stars are shown: the nearest one, once the camera is within a few of its sizes
        private void ChooseNear()
        {
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < _galaxies.Count; i++)
            {
                var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale;
                if (d < bd) { bd = d; best = i; }
            }
            if (bd > 8) best = -1;
            EnsureMade(best);                                                          // (the galaxy you come near is made at once)
            if (best != _near)
            {
                _near = best; CloseSystem(); _localCells.Clear(); _localKey = long.MinValue; _localCount = 0;
                if (_nearBuf != null) { _nearBuf.Dispose(); _nearBuf = null; }
                _nearLocal = null;
                if (best >= 0)
                {
                    var g = _galaxies[best];
                    // the same stars as its far glow, at once; the rest of its stars are made in the background and added after them
                    _nearKinds = (int[])g.FarKinds.Clone();
                    _nearLocal = (StarVertex[])g.FarPts.Clone();
                    _nearDrawn = _nearLocal;
                    _nearBuf = new UploadBuffer(_gpu.Device, (long)NearStars * Stride); _nearBuf.Write(_nearLocal, _nearLocal.Length);
                    int forGalaxy = best; var gt = g.Type; var gs = g.Seed ^ 0xE47A5UL;
                    Jobs.Run(() =>
                    {
                        var ek = new int[NearStars - FarStars];
                        var extra = GalaxyGen.Generate(gt, gs, NearStars - FarStars, _kinds, ek);
                        lock (_extraLock) { _extraFor = forGalaxy; _extra = extra; _extraKinds = ek; }
                    });
                    BuildGrid();
                    // its star clusters and nebulae, in place among its stars
                    EnsureStruct(g); _features = g.Features; _structBuf = g.Struct; _structCount = g.StructCount;
                }
            }
            // the rest of the near galaxy's stars, when they are ready: added after the ones already shown (nothing changes on screen)
            StarVertex[] ex = null; int[] exk = null;
            lock (_extraLock) { if (_extra != null && _extraFor == _near) { ex = _extra; exk = _extraKinds; } if (_extra != null) { _extra = null; _extraKinds = null; } }
            if (ex != null && _nearLocal != null && _nearLocal.Length == FarStars)
            {
                var all = new StarVertex[FarStars + ex.Length]; Array.Copy(_nearLocal, all, FarStars); Array.Copy(ex, 0, all, FarStars, ex.Length);
                var allK = new int[all.Length]; Array.Copy(_nearKinds, allK, FarStars); Array.Copy(exk, 0, allK, FarStars, exk.Length);
                _nearLocal = all; _nearKinds = allK; _nearDrawn = all; _nearBuf.Write(all, all.Length);
                BuildGrid();
            }
        }

        // inside a star cluster: its own stars made round you at its real density (a globular cluster's core about 1,000 stars a
        // cubic light-year, thinning outward; an open cluster's a few), stable (each from its own cell), in universe units
        private readonly List<double[]> _cstars = new List<double[]>(); private UploadBuffer _cstarBuf; private StarVertex[] _cstarV = new StarVertex[0];
        private double _cstarCX, _cstarCY, _cstarCZ, _cstarR;   // the cluster's middle (universe units) and size, for its light on screen
        private int _cstarFeature = -1; private double _cgx = double.NaN, _cgy, _cgz, _cstarHalf;
        private void ClusterStars()
        {
            int inF = -1; double ly = Units.Km(9.4607e12);
            if (_near >= 0)
            {
                var g = _galaxies[_near];
                for (int q = 0; q < _features.Count; q++)
                {
                    if (_features[q][4] > 1) continue;                                         // clusters only
                    double ux, uy, uz; ToUniverse(g, _features[q][0], _features[q][1], _features[q][2], out ux, out uy, out uz);
                    double dx = ux - (_ax + _ox), dy = uy - (_ay + _oy), dz = uz - (_az + _oz), R = _features[q][3] * g.Scale;
                    if (dx * dx + dy * dy + dz * dz < R * R * 2.25) { inF = q; break; }
                }
            }
            if (inF < 0) { _cstars.Clear(); _cstarFeature = -1; return; }
            var gg = _galaxies[_near]; var ft = _features[inF]; double cx, cy, cz; ToUniverse(gg, ft[0], ft[1], ft[2], out cx, out cy, out cz);
            double Rc = ft[3] * gg.Scale, core = Rc * (ft[4] == 0 ? 0.08 : 0.3), n0 = ft[4] == 0 ? 1000 : 3;   // stars a cubic light-year at its middle
            double px = _ax + _ox - cx, py = _ay + _oy - cy, pz = _az + _oz - cz, r2 = px * px + py * py + pz * pz;
            double nHere = n0 * Math.Pow(1 + r2 / (core * core), -2.5);                       // thinning outward (a Plummer sphere)
            double half = Math.Max(0.05 * ly, Math.Pow(4000 / Math.Max(1e-6, nHere), 1 / 3.0) * 0.5 * ly);   // about 4,000 stars round you
            if (inF == _cstarFeature && !double.IsNaN(_cgx) && Math.Abs(px - _cgx) + Math.Abs(py - _cgy) + Math.Abs(pz - _cgz) < half * 0.25 && Math.Abs(half / _cstarHalf - 1) < 0.5) return;
            _cstarFeature = inF; _cgx = px; _cgy = py; _cgz = pz; _cstarHalf = half; _cstars.Clear(); _cstarCX = cx; _cstarCY = cy; _cstarCZ = cz; _cstarR = Rc;
            double cell = half / 4; long i0 = (long)Math.Floor(px / cell), j0 = (long)Math.Floor(py / cell), k0 = (long)Math.Floor(pz / cell);
            for (long i = i0 - 4; i <= i0 + 4; i++)
                for (long j = j0 - 4; j <= j0 + 4; j++)
                    for (long k = k0 - 4; k <= k0 + 4; k++)
                    {
                        double mx = (i + 0.5) * cell, my = (j + 0.5) * cell, mz = (k + 0.5) * cell;
                        double expect = n0 * Math.Pow(1 + (mx * mx + my * my + mz * mz) / (core * core), -2.5) * Math.Pow(cell / ly, 3);
                        ulong h = (ulong)(i * 73856093L ^ j * 19349663L ^ k * 83492791L ^ inF * 2654435761L) ^ gg.Seed;
                        var rng = new Rng(h); int count = (int)Math.Floor(expect) + (rng.NextDouble() < expect - Math.Floor(expect) ? 1 : 0);
                        for (int c = 0; c < count && _cstars.Count < 12000; c++)
                        {
                            double sx = cx + (i + rng.NextDouble()) * cell, sy = cy + (j + rng.NextDouble()) * cell, sz = cz + (k + rng.NextDouble()) * cell;
                            double t = rng.NextDouble(); _cstars.Add(new[] { sx, sy, sz, ft[4], t });
                        }
                    }
        }
        // a galaxy's clusters and nebulae, made once and kept with it
        private void EnsureStruct(Galaxy g)
        {
            if (g.Features != null) return;
            int cc, nc; g.Features = new List<double[]>();
            var st = SpaceGen.Structures(g.Type, g.Seed, _kinds, out cc, out nc, g.Features);
            g.StructCount = st.Length; g.Struct = new UploadBuffer(_gpu.Device, (long)Math.Max(1, st.Length) * Stride); if (st.Length > 0) g.Struct.Write(st, st.Length);
        }
        private int _tGal = -1;                                                           // the galaxy of a picked cluster or nebula
        // -- the nebulae close up: a nebula had been only its far marker (eight soft points scattered over it, for the galaxy's sky seen from
        // outside), so flown to, or dived into, there was nothing on the screen; each kind is a cloud of its own gas now, in the colours of
        // what it is made of (its make-up, the same its card and its far marker use), made once when you come within forty of its radii
        private readonly Dictionary<long, UploadBuffer> _nebBufs = new Dictionary<long, UploadBuffer>(); private readonly Dictionary<long, int> _nebNs = new Dictionary<long, int>();
        private readonly Dictionary<long, int> _nebUsed = new Dictionary<long, int>(); private int _nebFrame;
        private void DrawNebulae(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            // only the nebulae you are at: within six of their radii (an emission nebula is hundreds of light-years across, and at a
            // galaxy's scale hundreds lay within forty), the nearest six of those, a cloud built once and kept (the cache had held twelve
            // and, with more in reach, thrown them all away and built them again every frame: three thousand points each, dozens a frame,
            // three hundred milliseconds on the processor and the card idle at four frames a second), at most two new ones a frame
            if (_near < 0 || _near >= _galaxies.Count || _features == null) return;
            var g = _galaxies[_near]; _nebFrame++;
            var near = _nebNear; near.Clear(); var at = _nebAt; at.Clear();   // (kept and emptied each frame)
            for (int q = 0; q < _features.Count; q++)
            {
                var fq = _features[q]; int fk = (int)fq[4]; if (fk < 2 || fk > 6) continue;
                double ux, uy, uz; ToUniverse(g, fq[0], fq[1], fq[2], out ux, out uy, out uz);
                double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, R = fq[3] * g.Scale, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (R <= 0 || d > R * 6) continue;
                near.Add(new KeyValuePair<double, int>(d / R, q)); at[q] = new[] { dx, dy, dz, R, d };
            }
            if (near.Count == 0) return;
            near.Sort((p0, p1) => p0.Key.CompareTo(p1.Key)); int built = 0;
            for (int i = 0; i < near.Count && i < 6; i++)
            {
                int q = near[i].Value; var fq = _features[q]; int fk = (int)fq[4]; var v = at[q]; double dx = v[0], dy = v[1], dz = v[2], R = v[3], d = v[4];
                long key = (long)(g.Seed % 1000003UL) * 100003L + q; UploadBuffer buf; int n;
                if (!_nebBufs.TryGetValue(key, out buf))
                {
                    if (built >= 2) continue; built++;
                    if (_nebBufs.Count >= 48)
                    {
                        // the one unused longest let go, never all at once
                        long old = -1; int oldest = int.MaxValue; foreach (var kv in _nebUsed) if (kv.Value < oldest) { oldest = kv.Value; old = kv.Key; }
                        if (old >= 0) { _nebBufs[old].Dispose(); _nebBufs.Remove(old); _nebNs.Remove(old); _nebUsed.Remove(old); }
                    }
                    var cloud = NebulaCloud(fk, fq.Length > 5 ? fq[5] : 0.5, g.Seed ^ (ulong)(q * 2654435761L));
                    n = cloud.Count; buf = new UploadBuffer(_gpu.Device, (long)Math.Max(1, n) * Stride); if (n > 0) buf.Write(cloud.ToArray(), n);
                    _nebBufs[key] = buf; _nebNs[key] = n;
                }
                _nebUsed[key] = _nebFrame; n = _nebNs[key]; if (n == 0) continue;
                float fade = (float)Math.Max(0, Math.Min(1, (6 - d / R) / 3));   // (coming in from six of its radii to three; the far marker's points stand for it beyond)
                var m = Mat4.Identity; m.M11 = (float)R; m.M22 = (float)R; m.M33 = (float)R; m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                DrawPoints(buf, n, m * vp, right, up, 1f, 0.0012f, w, h, time, 0, fade * (fk == 6 ? 0.07f : 0.15f));   // (a cloud's brightness: its thousands of soft points add up, and at the old brightness their middles burnt out to white)
            }
        }
        // a nebula's cloud in its own units (its radius 1): a planetary nebula a thin shell puffed off its dying star (oxygen's green-blue
        // within, hydrogen's and nitrogen's red at the rim, the white dwarf at its middle); a supernova's remnant a torn shell of filaments;
        // an emission nebula knots and pillars of glowing gas with its hot young stars inside, a reflection nebula smaller and blue round
        // its stars, a dark nebula dim dust; every point's colour from a part of what the nebula is made of, by its share
        private static List<StarVertex> NebulaCloud(int kind, double mix, ulong seed)
        {
            var rng = new Rng(seed); var parts = SpaceGen.Composition(kind, mix); var l = new List<StarVertex>();
            Func<SpaceGen.Part> part = () => { double u = rng.NextDouble(); foreach (var p in parts) { u -= p.Share; if (u <= 0) return p; } return parts[parts.Length - 1]; };
            Func<Vec3> dir = () => { double u = rng.NextDouble() * 2 - 1, a = rng.NextDouble() * Math.PI * 2, s = Math.Sqrt(1 - u * u); return new Vec3((float)(s * Math.Cos(a)), (float)u, (float)(s * Math.Sin(a))); };
            if (kind == 4)
            {
                float squash = (float)(0.6 + 0.4 * rng.NextDouble());
                for (int k = 0; k < 2600; k++)
                {
                    var dv = dir(); double rr = 0.75 + 0.22 * rng.NextDouble() + 0.05 * rng.Gaussian(); var p = part(); bool rim = rr > 0.9;
                    float br = (float)(0.35 + 0.45 * rng.NextDouble()) * (rim ? 1f : 0.7f), cr = rim ? 1f : p.R, cg = rim ? 0.3f : p.G, cb = rim ? 0.3f : p.B;
                    l.Add(new StarVertex { X = (float)(dv.X * rr), Y = (float)(dv.Y * rr * squash), Z = (float)(dv.Z * rr), Size = 0.09f, R = cr * br, G = cg * br, B = cb * br, A = 1 });
                }
                for (int k = 0; k < 600; k++) { var dv = dir(); double rr = Math.Pow(rng.NextDouble(), 0.5) * 0.7; const float br = 0.18f; l.Add(new StarVertex { X = (float)(dv.X * rr), Y = (float)(dv.Y * rr * squash), Z = (float)(dv.Z * rr), Size = 0.14f, R = 0.2f * br, G = 1f * br, B = 0.75f * br, A = 1 }); }
                l.Add(new StarVertex { X = 0, Y = 0, Z = 0, Size = 0.012f, R = 0.8f, G = 0.9f, B = 1f, A = 1 });   // the white dwarf that puffed it off
                return l;
            }
            if (kind == 5)
            {
                int fil = 40; var fd = new Vec3[fil]; for (int f = 0; f < fil; f++) fd[f] = dir();
                for (int k = 0; k < 3200; k++)
                {
                    var f0 = fd[rng.Below(fil)]; var jt = dir(); double rr = 0.85 + 0.15 * rng.NextDouble(); var dv = (f0 * 0.75f + jt * 0.35f).Normalized; var p = part(); float br = (float)(0.3 + 0.5 * rng.NextDouble());
                    l.Add(new StarVertex { X = dv.X * (float)rr, Y = dv.Y * (float)rr, Z = dv.Z * (float)rr, Size = 0.07f, R = p.R * br, G = p.G * br, B = p.B * br, A = 1 });
                }
                for (int k = 0; k < 500; k++) { var dv = dir(); double rr = Math.Pow(rng.NextDouble(), 0.4); const float br = 0.12f; l.Add(new StarVertex { X = dv.X * (float)rr, Y = dv.Y * (float)rr, Z = dv.Z * (float)rr, Size = 0.16f, R = 0.9f * br, G = 0.4f * br, B = 0.35f * br, A = 1 }); }
                return l;
            }
            int knots = 28; var kc = new Vec3[knots]; var ks = new float[knots];
            for (int q = 0; q < knots; q++) { kc[q] = new Vec3((float)rng.Gaussian() * 0.45f, (float)rng.Gaussian() * 0.3f, (float)rng.Gaussian() * 0.45f); ks[q] = (float)(0.12 + 0.3 * rng.NextDouble()); }
            int pts = kind == 6 ? 1800 : 3000;
            for (int k = 0; k < pts; k++)
            {
                int q = rng.Below(knots); var c = kc[q] + new Vec3((float)rng.Gaussian(), (float)rng.Gaussian(), (float)rng.Gaussian()) * ks[q];
                double cl = Math.Sqrt(c.X * c.X + c.Y * c.Y + c.Z * c.Z); if (cl > 1) continue;
                var p = part(); if (p.El == "O" && cl > 0.5) continue;                                   // (oxygen's teal toward the middle, hydrogen's red throughout)
                float br = (float)((0.25 + 0.45 * rng.NextDouble()) * (1 - cl * 0.5)) * (kind == 6 ? 0.45f : 1f);
                l.Add(new StarVertex { X = c.X, Y = c.Y, Z = c.Z, Size = kind == 3 ? 0.11f : 0.15f, R = p.R * br, G = p.G * br, B = p.B * br, A = 1 });
            }
            if (kind == 2 || kind == 3) for (int k = 0; k < (kind == 2 ? 24 : 6); k++) { int q = rng.Below(knots); var c = kc[q] + new Vec3((float)rng.Gaussian(), (float)rng.Gaussian(), (float)rng.Gaussian()) * ks[q] * 0.5f; l.Add(new StarVertex { X = c.X, Y = c.Y, Z = c.Z, Size = 0.01f, R = 0.75f, G = 0.85f, B = 1f, A = 1 }); }   // the hot young stars that light it
            return l;
        }
        private UploadBuffer _structBuf; private int _structCount; private List<double[]> _features = new List<double[]>();   // the near galaxy's clusters and nebulae
        private readonly object _extraLock = new object(); private StarVertex[] _extra; private int[] _extraKinds; private int _extraFor = -1;

        // the grid used to find the nearest star of the near galaxy
        private void BuildGrid()
        {
            {
                {
                    _gridHead = new int[GridN * GridN * GridN]; for (int k = 0; k < _gridHead.Length; k++) _gridHead[k] = -1;
                    _gridNext = new int[_nearLocal.Length];
                    for (int k = 0; k < _nearLocal.Length; k++)
                    {
                        int x = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].X))), y = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].Y))), z = Math.Max(0, Math.Min(GridN - 1, Cell(_nearLocal[k].Z)));
                        int c = (x * GridN + y) * GridN + z; _gridNext[k] = _gridHead[c]; _gridHead[c] = k;
                    }
                }
            }
        }

        // how far into its own stars the near galaxy is (0: only the stars of its far glow, 1: all of its stars), from how close you are
        private double NearShare()
        {
            if (_near < 0) return 0;
            var g = _galaxies[_near]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale;
            double t = (8 - d) / 6.5; t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t);
        }

        // how deep into the near galaxy you are (0 outside, 1 well inside), smoothly
        private double InsideShare()
        {
            if (_near < 0) return 0;
            var gn = _galaxies[_near]; double dx = gn.X - _cx, dy = gn.Y - _cy, dz = gn.Z - _cz, dg = Math.Sqrt(dx * dx + dy * dy + dz * dz) / gn.Scale;
            return Math.Max(0, Math.Min(1, 1.6 - dg));
        }

        // how many of a galaxy's stars to draw: more as it grows on screen, each smaller, so its glow stays the same overall
        private int StarsShown(int i)
        {
            if (i != _near || _nearLocal == null) return _galaxies[i].FarCount;
            return (int)Math.Min(_nearLocal.Length, FarStars + (NearStars - FarStars) * NearShare());
        }

        public void Draw(int w, int h, double time)
        {
            _lastDrawTime = time;
            {
                // the zoom log's census at every tenfold in the matter: what there was to pick at that size (a size with nothing real in it shows)
                double ms = _spaceScale > 0 ? _spaceScale : _matterScale; int dec = ms > 0 ? (int)Math.Floor(Math.Log10(ms)) : int.MinValue;
                if (dec != _levelLogged) { _levelLogged = dec; if (ms > 0) ZoomLog("LEVEL " + ScaleHud.Words(ms) + ": " + _matter.CensusWords() + " · " + _matter.Stage(ms)); }
            }
            {
                double fine = _spaceScale > 0 ? _spaceScale : _matterScale > 0 ? _matterScale : 1;
                _sky = (float)Math.Max(0, Math.Min(1, Math.Log(fine / 1.7e-8) / Math.Log(1.7e-6 / 1.7e-8)));   // from a millimetre down to 10 microns
            }
            var look = Look;
            Mat4 view = Mat4.LookAt(new Vec3(0, 0, 0), look, ViewUp());
            double reach = Math.Max(1e-12, NearestDistance());
            // the bodies drawn close round you (asteroids, comets, black holes' discs) count too, so the nearest drawing distance never
            // cuts away one you are beside
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 && !ex.Hole) continue;
                double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - ex.R * 1.6;
                if (d <= 0) d = ex.R * 0.01;                                        // (beside it or over it: the near plane a hundredth of its radius, never leaping out past the stone; within two radii it had dropped out of the reckoning, the near plane leapt out to the next thing, and the stone was clipped away as you came in)
                if (d < reach) reach = d;
            }
            Mat4 proj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)(reach * 1e-3), (float)(_uRadius * 2.2));   // out to the far side of the visible universe
            Mat4 vp = view * proj;
            if (_ground != null)
            {
                // at a world its ground is held still under you and the sky turns round it, by its spin (the same turn as its
                // starlight on the ground): its star rises and sets, the stars wheel overhead, about the world's middle
                double sp = -SpunSince; float c0 = (float)Math.Cos(sp), s0 = (float)Math.Sin(sp);
                var rot = Mat4.Identity; rot.M11 = c0; rot.M13 = s0; rot.M31 = -s0; rot.M33 = c0;
                float pcx = (float)(_px - _ox), pcy = (float)(_py - _oy), pcz = (float)(_pz - _oz);
                vp = Mat4.Translation(-pcx, -pcy, -pcz) * rot * Mat4.Translation(pcx, pcy, pcz) * vp;
            }
            _lastVp = vp; _lastW = w; _lastH = h; _haveVp = true;
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            _viewRight = right; _viewUp = up; _haveAxes = true;
            double share = NearShare(), within = InsideShare();
            // the world you are falling to fills the whole view: everything behind it (the web, the galaxies, their clusters and
            // nebulae, the stars round you) cannot be seen, so none of it is drawn; what lies in front (its moons, its rings) still is
            bool skyHidden = InsideGiant;                                               // (inside a giant's gas nothing past it shows)
            if (!skyHidden && _ground != null)
            {
                double qx = _px - _ox, qy = _py - _oy, qz = _pz - _oz, qd = Math.Sqrt(qx * qx + qy * qy + qz * qz), pr = _ground.Radius * Units.Km(1) * (_ground.Solid ? 0.999 : 0.97);
                if (qd > pr)
                {
                    var lk = Look; double off = Math.Acos(Math.Max(-1, Math.Min(1, (qx * lk.X + qy * lk.Y + qz * lk.Z) / qd)));
                    double aspect = w / (double)Math.Max(1, h), halfDiag = Math.Atan(Math.Tan(0.45) * Math.Sqrt(1 + aspect * aspect));
                    skyHidden = off + halfDiag < Math.Asin(Math.Min(1, pr / qd));
                }
            }
            // inside a galaxy its own stars fill the sky and the other galaxies show fainter behind them (as Andromeda
            // is barely seen with the eye): the others and the web's glow fade to a sixth as you go in
            float others = (float)(1 - 0.45 * Math.Max(0, Math.Min(1, within)));   // (dimmed a little: the other galaxies still show in its sky)
            // the web's points, each a far galaxy, dimmer and smaller the nearer a galaxy you are, so from a galaxy's edge its own
            // stars (sharp, in their colours) and the web's galaxies behind (soft, faint) are never taken for each other
            float webNear = (float)(1 - 0.75 * Math.Max(0, Math.Min(1, within)));
            {
                // the rest of the universe's galaxies, a glow along the cosmic web (remade without the near ones as you move far)
                if (double.IsNaN(_glowAtX) || _glowReady != null || (Math.Abs(_cx - _glowAtX) + Math.Abs(_cy - _glowAtY) + Math.Abs(_cz - _glowAtZ) > _nearUnits * 0.25 && time - _glowAtT > 0.5)) { MakeGlow(); _glowAtT = time; }   // (only to start it, take it in when made, or make it again far away: the card sifts it each frame)
                var gm = Mat4.Identity; gm.M41 = (float)-_cx; gm.M42 = (float)-_cy; gm.M43 = (float)-_cz;
                float webFade = (float)Math.Max(0, Math.Min(1, (0.98 - within) / 0.45)); webFade = webFade * webFade * (3 - 2 * webFade);   // (inside a galaxy the web's points are not drawn: its sky shows the other galaxies themselves, made in full round you)
                webFade *= (float)Math.Min(1, (System.Diagnostics.Stopwatch.GetTimestamp() - _glowCameAt) / (2.0 * System.Diagnostics.Stopwatch.Frequency));   // (newly made: faded in over two seconds)
            Render.GpuLog.Pass = "the web's light";
                if (webFade > 0.01 && !skyHidden)   // (deep inside a galaxy its stars and dust hide the web, and it is not drawn; it fades in as you rise out of it, never popping in)
                {
                _web[0] = (float)(2.0e9 / Units.LightYearsPerUnit); _web[1] = (float)_cx; _web[2] = (float)_cy; _web[3] = (float)_cz; _web[4] = (float)_nearUnits;   // (from where the galaxies made in full begin to fade out, the web fades in over the same span: they hand over, never both at once, never a gap)
                DrawPoints(_glowBuf, _glowCount, gm * vp, right, up, 1f, 0.0035f * (0.5f + 0.5f * webNear), w, h, time, 0.007f * (0.5f + 0.5f * webNear), others * webFade * webNear, 0f);   // as bright as the galaxies it stands for where they hand over (never a black wall past the galaxies made in full)
                _web[0] = 0;
                }   // each a galaxy at its true size (a pinpoint from afar), faint, so the web shows where they crowd
            }
            Render.GpuLog.Pass = "galaxies";
            for (int i = 0; i < _galaxies.Count && !skyHidden; i++)
            {
                // every galaxy is one field of its own stars: the near one shows more of them as it grows on screen, each point
                // a little smaller so the galaxy's glow stays the same; the first stars are always the same ones, so nothing swaps
                var g = _galaxies[i]; int count = StarsShown(i);
                if (i != _near)
                {
                    // a galaxy only a few pixels across needs only a few of its stars (the first ones are always the same, so nothing
                    // swaps): as many as its size on screen can show, from about a hundred to all of them; its glow stays its own
                    double qx = g.X - _cx, qy = g.Y - _cy, qz = g.Z - _cz, qd = Math.Max(1e-9, Math.Sqrt(qx * qx + qy * qy + qz * qz));
                    double qpx = g.Scale / qd * h / (2 * Math.Tan(0.45));
                    count = (int)Math.Max(Math.Min(count, 100), Math.Min(count, qpx * qpx * 1.5));
                }
                // made in full only near you: past that the web's glow shows them (it stands for the same galaxies); fading out
                // between the near reach and half again, so nothing pops
                double ndx = g.X - _cx, ndy = g.Y - _cy, ndz = g.Z - _cz, nd = Math.Sqrt(ndx * ndx + ndy * ndy + ndz * ndz);
                float nearF = i == _near || (_tKind == 0 && _tIndex == i) ? 1f : (float)Math.Max(0, Math.Min(1, (_nearUnits * 1.5 - nd) / (_nearUnits * 0.5)));
                if (nearF <= 0) continue;
                float shrink = (float)Math.Sqrt(FarStars / (double)Math.Max(FarStars, count));
                var buf = i == _near && _nearBuf != null ? _nearBuf : g.Far; if (buf == null) continue;   // (its stars still being made)
                // inside the galaxy its points ease down from glows to plain points of light, so no nearby one looms as a blob
                float glow = i == _near ? (float)Math.Pow(1 - InsideShare(), 3) : 1f;   // to nothing at all well inside: only points of light remain
                // a galaxy's points never cover more than a small angle on screen: from afar they glow, up close they stay points of light
                // its light, never more than it has: when its stars crowd a small patch of screen they would add up to a white blot,
                // so each is dimmed by how many share the galaxy's patch (its true glow and colour, at any distance)
                double gdx = g.X - _cx, gdy = g.Y - _cy, gdz = g.Z - _cz, gd = Math.Max(1e-9, Math.Sqrt(gdx * gdx + gdy * gdy + gdz * gdz));
                double gpx = g.Scale / gd * h / (2 * Math.Tan(0.45)), dotPx = Math.Max(1.5, 0.0016 * h);
                // (its light at a third of a full point a pixel on the average: its dense heart glows, its thin rim fades to nothing, so
                // from afar it is a soft shape in its own colours with a fine edge, never a solid white disc or a white spiral icon)
                float crowd = (float)Math.Max(0.015, Math.Min(1, Math.PI * gpx * gpx / (count * dotPx * dotPx) * 0.35)) * nearF * (i == _near ? 1f : others);
                var pg = Placement(g); float drift = GalaxyDrift(g, ref pg);
                DrawPoints(buf, count, pg * vp, right, up, 0.006f * g.Scale * shrink * glow, i == _near ? 0.0016f : 0.0012f, w, h, time, 0.012f, crowd, InsideShare() > 0.5 && i == _near ? 2f : 0f, 0, 0, 0, 0, 0, drift);   // a galaxy's stars from afar are soft glows that add up to its light (hard discs would merge into a white outline); among them, points
            }
            double inside = InsideShare();
            {
                // every galaxy's clusters and nebulae, for the 8 nearest within about 60 of their sizes (one made new each frame at most)
                int drawn = 0; bool made = false;
                var order = _galOrder; order.Clear();   // (kept and emptied each frame)
                for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / g.Scale; if (d < 60) order.Add(new KeyValuePair<double, int>(d, i)); }
                order.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (var kv in order)
                {
                    if (drawn >= 12 || skyHidden) break; var g = _galaxies[kv.Value];
                    if (g.Features == null) { if (made) continue; EnsureStruct(g); made = true; }
                    if (g.StructCount > 0) { DrawPoints(g.Struct, g.StructCount, Placement(g) * vp, right, up, 1f, 0.0016f, w, h, time, 0, (float)Math.Max(0, Math.Min(1, (60 - kv.Key) / 20))); drawn++; }   // fading in from 60 to 40 of its sizes
                }
            }
            Render.GpuLog.Pass = "supernovae";
            DrawSupernovae(vp, right, up, w, h, time);
            Render.GpuLog.Pass = "black holes and bodies";
            UpdateExtras();
            DrawShadows(w, h);                                                          // every hole's shadow, black, at its real size from where you stand
            DrawBlackHoles(vp, right, up, w, h, time);                               // the feeding ones' discs and jets
                              // asteroids and comets close in: their own bodies
            {
                // the black hole that bends the most light on screen: its Einstein ring, sqrt(2 Rs / distance) radians (light from far
                // behind it), set for every point drawn
                _lens[2] = 0; double bestE = 0;
                foreach (var ex in _extras)
                {
                    if (!ex.Hole) continue; double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double rs = Units.Km(Physics.HorizonKm(ex.Mass)), thE = Math.Sqrt(2 * rs / Math.Max(d, rs)); if (thE < bestE) continue;
                    float px0 = (float)dx, py0 = (float)dy, pz0 = (float)dz;
                    float cx0 = px0 * vp.M11 + py0 * vp.M21 + pz0 * vp.M31 + vp.M41, cy0 = px0 * vp.M12 + py0 * vp.M22 + pz0 * vp.M32 + vp.M42, cw0 = px0 * vp.M14 + py0 * vp.M24 + pz0 * vp.M34 + vp.M44;
                    if (cw0 <= 0) continue;
                    bestE = thE; _lens[0] = cx0 / cw0; _lens[1] = cy0 / cw0; _lens[2] = (float)(thE / Math.Tan(0.45)); _lens[3] = cw0;
                }
            }
            if (_extras.Count > 0)
            {
                if (_extraV.Length < _extras.Count) _extraV = new StarVertex[_extras.Count + 256];
                int nv = 0;   // (only the bodies not drawn as their own stone: an empty vertex had stood in for each drawn one, size nothing at the camera itself, and the card projected it into a white speckled disc on the screen, the ghosts seen beside a rock)
                for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (ex.Shape.Length > 0 && ShapeShown(ex, h)) continue;   // (drawn as its own body: no point of light over it)
                    if (ex.Hole)
                    {
                        // its glow from afar (so it can be found and picked), fading out within a hundred radii as its real disc, shadow and lens take over
                        double hdx = RelX(ex) - _ox, hdy = RelY(ex) - _oy, hdz = RelZ(ex) - _oz, hrs = ex.Mass > 0 ? Units.Km(Physics.HorizonKm(ex.Mass)) : ex.R / 2.6;
                        float farF = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(hdx * hdx + hdy * hdy + hdz * hdz) / Math.Max(1e-300, hrs) - 100) / 100)) * (ex.Feeding > 0 ? 0.7f : 0.25f);
                        _extraV[nv++] = new StarVertex { X = (float)hdx, Y = (float)hdy, Z = (float)hdz, Size = (float)(ex.R * 3), R = ex.Cr * farF, G = ex.Cg * farF, B = ex.Cb * farF, A = 1 }; continue;
                    }
                    _extraV[nv++] = new StarVertex { X = (float)(RelX(ex) - _ox), Y = (float)(RelY(ex) - _oy), Z = (float)(RelZ(ex) - _oz), Size = (float)ex.R, R = ex.Cr, G = ex.Cg, B = ex.Cb, A = 1 }; }
                if (_extraBuf == null || _extraBuf.Bytes < (long)_extras.Count * Stride) { if (_extraBuf != null) _extraBuf.Dispose(); _extraBuf = new UploadBuffer(_gpu.Device, (long)(_extras.Count + 256) * Stride); }
                if (nv > 0) { _extraBuf.Write(_extraV, nv); DrawPoints(_extraBuf, nv, vp, right, up, 1f, 0.0024f, w, h, time); }
            }
            Render.GpuLog.Pass = "a cluster's stars";
            if (_cstars.Count > 0 && !skyHidden)
            {
                // the cluster's own stars round you: old red and orange giants and yellow stars in a globular, young blue-white in an open one
                if (_cstarV.Length < _cstars.Count) _cstarV = new StarVertex[_cstars.Count + 1024];
                for (int q = 0; q < _cstars.Count; q++)
                {
                    var cs = _cstars[q]; bool old = cs[3] == 0; double t = cs[4];
                    float r = old ? (t < 0.4 ? 1f : 1f) : 0.7f, g2 = old ? (t < 0.4 ? 0.55f : 0.85f) : 0.82f, b2 = old ? (t < 0.4 ? 0.3f : 0.6f) : 1f;
                    _cstarV[q] = new StarVertex { X = (float)((cs[0] - _ax) - _ox), Y = (float)((cs[1] - _ay) - _oy), Z = (float)((cs[2] - _az) - _oz), Size = (float)Units.Km(696000 * (old && t < 0.1 ? 20 : 1)), R = r, G = g2, B = b2, A = 1 };
                }
                if (_cstarBuf == null || _cstarBuf.Bytes < (long)_cstars.Count * Stride) { if (_cstarBuf != null) _cstarBuf.Dispose(); _cstarBuf = new UploadBuffer(_gpu.Device, (long)(_cstars.Count + 1024) * Stride); }
                _cstarBuf.Write(_cstarV, _cstars.Count);
                // its light, never more than it has: from afar its thousands of stars would stack into a solid white ball with a hard
                // rim, so each is dimmed by how many share the cluster's patch of screen (as the galaxies' stars are); among them, points
                double ccx = (_cstarCX - _ax) - _ox, ccy = (_cstarCY - _ay) - _oy, ccz = (_cstarCZ - _az) - _oz, ccd = Math.Max(1e-12, Math.Sqrt(ccx * ccx + ccy * ccy + ccz * ccz));
                double cpx = _cstarR / ccd * h / (2 * Math.Tan(0.45)), cdot = Math.Max(1.5, 0.0016 * h);
                float ccrowd = (float)Math.Max(0.03, Math.Min(1, Math.PI * cpx * cpx / (_cstars.Count * cdot * cdot) * 0.35));
                DrawPoints(_cstarBuf, _cstars.Count, vp, right, up, 1f, 0.0016f, w, h, time, 0f, ccrowd);
            }
            Render.GpuLog.Pass = "the stars round you";
            if (_near >= 0 && _localCount > 0 && inside > 0 && !skyHidden)
            {
                // the real stars of this space at their real sizes (a small point from afar, never smaller than a dot on screen)
                // and only while the view is near their own size: wider than a few times the reach they are made in, the galaxy's
                // own field stands for them (a block of thousands round the eye, each a dot, would be a fuzz of light at those views)
                double viewLy = ViewDistanceKm() / 9.4607e12, reachLy = LocalReach * 50000.0, far = Math.Max(0, Math.Min(1, (viewLy / reachLy - 3) / 12));
                float lf = (float)(inside * inside * (3 - 2 * inside) * (1 - far * far * (3 - 2 * far)));
                if (lf > 0.003f)
                {
                    if (Math.Abs(lf - _localLf) > 0.002f || _localVersion != _localWritten || _localCount != _localWrittenN)   // (put on the card again only when they change)
                    {
                        for (int k = 0; k < _localCount; k++) { var v = _localList[k]; v.A = 1; v.R *= lf; v.G *= lf; v.B *= lf; if (_localDead.Contains(k)) { v.R = 0; v.G = 0; v.B = 0; } _localDrawn[k] = v; }   // (a star that died in a supernova: gone)
                        _localBuf.Write(_localDrawn, _localCount); _localLf = lf; _localWritten = _localVersion; _localWrittenN = _localCount;
                    }
                    // placed from the list's origin: its place in the universe less the camera's worked out in double, so the stars
                    // round you are exact on the card (the galaxy's own frame in float snapped them to a lattice 190 AU wide)
                    var pm = Placement(_galaxies[_near]); double oux, ouy, ouz; ToUniverse(_galaxies[_near], _localOX, _localOY, _localOZ, out oux, out ouy, out ouz);
                    pm.M41 = (float)((oux - _ax) - _ox); pm.M42 = (float)((ouy - _ay) - _oy); pm.M43 = (float)((ouz - _az) - _oz);
                    DrawPoints(_localBuf, _localCount, pm * vp, right, up, 1f, 0.0022f, w, h, time);
                }
            }
            Render.GpuLog.Pass = "the star system";
            if (_system != null)
            {
                // the system around its star (the anchor): orbits and belts, then the star, planets and moons as glows at their real sizes
                _gpu.ClearDepth();
                PlaceSystem();
                var st = _system.Static;
                float stFade = (float)Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));   // orbits and belts fade in with the planets
                // the belts' haze is rubble on its orbits: each point turns about the star at Kepler's rate for its distance (the
                // orbit lines and the Oort cloud stand); the whole haze had stood still
                var sr = _system.StaticRate; double yrs = _simYears;
                for (int i = 0; i < st.Length; i++)
                {
                    var v = st[i]; double px0 = st[i].X, pz0 = st[i].Z; float rate = sr != null && i < sr.Length ? sr[i] : 0;
                    if (rate != 0) { double th = (rate * yrs) % (2 * Math.PI), ct = Math.Cos(th), snt = Math.Sin(th); px0 = st[i].X * ct - st[i].Z * snt; pz0 = st[i].X * snt + st[i].Z * ct; }
                    v.X = (float)(px0 - _ox); v.Y = (float)(st[i].Y - _oy); v.Z = (float)(pz0 - _oz); v.R *= stFade; v.G *= stFade; v.B *= stFade; _staticShifted[i] = v;
                }
                // the belt's points dimmed by how many share its patch of screen (as a galaxy's stars are): from afar three thousand
                // dots in a thin ring added up to a solid white annulus, a machined pattern round the star; a belt is a haze
                double belt = 1;
                if (_system.BeltOutAu > 0)
                {
                    double dsys = Math.Max(1e-30, DistToSysStar()), pxPer = h / (2 * Math.Tan(0.45)) / dsys, ro = Units.Au(_system.BeltOutAu) * pxPer, ri = Units.Au(_system.BeltInAu) * pxPer, dotPx = Math.Max(1.5, 0.0012 * h);
                    belt = Math.Max(0.08, Math.Min(1, Math.PI * (ro * ro - ri * ri) / (3000 * dotPx * dotPx) * 0.5));
                }
                if (st.Length > 0) { _sysStatic.Write(_staticShifted, st.Length); DrawPoints(_sysStatic, st.Length, vp, right, up, 0f, 0.0012f, w, h, time, 0f, (float)belt); }
                for (int i = 0; i < _bodyCount; i++) { _bodyVerts[i].X = (float)(_bodyVerts[i].X - _ox); _bodyVerts[i].Y = (float)(_bodyVerts[i].Y - _oy); _bodyVerts[i].Z = (float)(_bodyVerts[i].Z - _oz); }
                // the planet whose real ground is showing is not drawn as a glow too (it showed through its own see-through sea and clouds as a frosted ghost)
                // the system's planets and moons fade in as you close in on its star (from 2,000 AU to 500 AU), never all at once
                double sysFade = Math.Max(0, Math.Min(1, (SystemRange - DistToSysStar()) / (SystemRange * 0.75)));
                for (int i = 1; i < _bodyCount; i++) { _bodyVerts[i].R *= (float)sysFade; _bodyVerts[i].G *= (float)sysFade; _bodyVerts[i].B *= (float)sysFade; }
                if (_ground != null && _planet >= 0 && _planet + 1 < _bodyCount)   // (a small body's world has no glow among the system's bodies: its slot is not a planet's; indexed at slot minus one it had crashed the first frame its ground was made)
                {
                    // its glow fades into its real ground as you come in (from 40 to 20 of its widths), never swapped
                    double gx0, gy0, gz0; CamFromPlanet(out gx0, out gy0, out gz0);
                    float gk = (float)Math.Max(0, Math.Min(1, (Math.Sqrt(gx0 * gx0 + gy0 * gy0 + gz0 * gz0) / _ground.Radius - 20) / 20));
                    _bodyVerts[_planet + 1].R *= gk; _bodyVerts[_planet + 1].G *= gk; _bodyVerts[_planet + 1].B *= gk; if (gk <= 0) _bodyVerts[_planet + 1].Size = 0;
                }
                _sysBodies.Write(_bodyVerts, _bodyCount);
                DrawPoints(_sysBodies, _bodyCount, vp, right, up, 1f, 0.004f, w, h, time);
                // close enough to see its disc: the star's real body at its real size
                double R = StarRadiusNow, sd = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz); float swellDim = (float)(1 / (StarSwell * StarSwell));   // (blown apart: thinner, dimmer)
                if (_starSurf != null && R / Math.Max(1e-30, sd) > 0.004)
                {
                    var toEye = new Vec3((float)(_ox / sd), (float)(_oy / sd), (float)(_oz / sd));
                    int n = _starSurf.Draw(toEye, time, _starSurfVerts, 0);
                    _surfBuf.Write(_starSurfVerts, n);
                    var m = Mat4.Identity; m.M11 = m.M22 = m.M33 = (float)R; m.M41 = (float)-_ox; m.M42 = (float)-_oy; m.M43 = (float)-_oz;
                    DrawPoints(_surfBuf, n, m * vp, right, up, 1f, 0f, w, h, time, 0, swellDim);
                    DrawPoints(_coronaBuf, _starCorona.Length, m * vp, right, up, 1f, 0f, w, h, time, 0, swellDim);
                }
                {
                    // inside the star: nothing is seen but its own glow, in the colour of the heat of the layer you are in (the plasma's own
                    // picture takes over when you dive into it)
                    var sl = StarLayerHere();
                    if (sl != null && _spaceScale <= 0) { float gr, gg, gb; StarGlow(LayerTempK(sl, sd / Math.Max(1e-30, R)), out gr, out gg, out gb); _hud.DrawFill(_meshes, w, h, gr, gg, gb); _gpu.ClearDepth(); }
                }
            }
            // the solid small bodies (asteroids, comets' nuclei) and worlds' rings last of everything in space: after every star,
            // belt and orbit behind them, so nothing shows through a solid body (the glows are drawn without looking at what is in front)
            Render.GpuLog.Pass = "asteroids and comets";
            DrawNebulae(vp, right, up, w, h, time);                                   // the nebulae near you, each a cloud of its own gas (its far marker stands for it beyond forty of its radii)
            DrawSmallBodies(vp, right, up, w, h, time);
            DrawRings(vp, right, up, w, h, time);                                       // the rings of the worlds that have them
            Render.GpuLog.Pass = "a world's ground";
            if (_ground != null && (_matterScale <= 0 || _matterScale > 1e-7))   // (the ground still drawn behind the matter down to a tenth of a millimetre, so the hand-over to the grains is no cut; below that the grain's own face fills the view)
            {
                // the planet's real ground, in km around its centre, with the same view turn; near and far follow the height
                _gpu.ClearDepth();
                double x, y, z; CamFromPlanet(out x, out y, out z);
                // the height above the real ground (not sea level: on high land that was kilometres, and the nearest drawing
                // distance cut away the ground right under you)
                double alt = Math.Max(1e-9, AboveGround() / Units.Km(1));
                // the depth range starts a little in front of the ground and ends just past the horizon, so near things (the sea
                // and the shore just under it) never fight for the same depth and shimmer
                double altSea = Math.Max(alt, Math.Sqrt(x * x + y * y + z * z) - _ground.Radius);   // the horizon goes by the height above sea level
                double horizon = Math.Sqrt(altSea * (2 * _ground.Radius + altSea)) + Math.Sqrt(2 * _ground.Radius * 30);
                Mat4 kproj = Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), (float)Math.Max(alt * 0.15, 1e-9), (float)(horizon * 1.3 + 100));
                double lx = -_px, ly = -_py, lz = -_pz, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
                if (ll > 0)
                {
                    // the starlight's way, turned back by the planet's own spin, so its days pass over the ground
                    double sp = -SpunSince, c0 = Math.Cos(sp), s0 = Math.Sin(sp), sx = lx / ll, sz = lz / ll;   // (its day where its star really is: lit on the side facing it, turning by its spin)
                    _ground.StarDir[0] = sx * c0 - sz * s0; _ground.StarDir[1] = ly / ll; _ground.StarDir[2] = sx * s0 + sz * c0;
                    if (Settings.Get("always-day", true))
                    {
                        // always day (Time menu): the star high over wherever you are (sixty degrees up, so the ground still shows its
                        // shape), turned back by the spin as the real starlight is
                        double cr = Math.Sqrt(x * x + y * y + z * z), ux = x / cr, uy = y / cr, uz = z / cr;
                        double ex = -uz, ez = ux, el = Math.Sqrt(ex * ex + ez * ez); if (el < 1e-6) { ex = 1; ez = 0; el = 1; }
                        double dx = ux * 0.87 + ex / el * 0.5, dy = uy * 0.87, dz = uz * 0.87 + ez / el * 0.5, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        dx /= dl; dy /= dl; dz /= dl;
                        _ground.StarDir[0] = dx * c0 - dz * s0; _ground.StarDir[1] = dy; _ground.StarDir[2] = dx * s0 + dz * c0;
                    }
                }
                GroundLight();
                // what the ground needs to detail: only what is in view (the view's half-angle, corner to corner)
                var lk = Look; if (_ground.ViewDir == null) _ground.ViewDir = new double[3];
                _ground.ViewDir[0] = lk.X; _ground.ViewDir[1] = lk.Y; _ground.ViewDir[2] = lk.Z;
                _ground.ViewHalfAngle = Math.Atan(Math.Tan(0.45) * Math.Sqrt(1 + (w / (double)Math.Max(1, h)) * (w / (double)Math.Max(1, h))));
                PlanetTerrain.Power = Window.Detail;
                _ground.SandOnCard = true;                                                // (the grains worked out per pixel at every height: on the raised pieces close in too, looked up at the bed's level, so their domes shade smooth, never in steps)
                if (InsideGiant)
                {
                    // inside a giant, below its cloud tops: no shell to see from in here, only its own gas all round, its air's
                    // colour, dimming as you go deeper (sunlight dies away within a few hundred km), lit on its day side
                    double cr = Math.Sqrt(x * x + y * y + z * z), depth = Math.Max(0, _ground.Radius + _ground.CloudKm - cr);
                    double day = cr > 0 ? Math.Max(0.15, Math.Min(1, (x * _ground.StarDir[0] + y * _ground.StarDir[1] + z * _ground.StarDir[2]) / cr * 1.5 + 0.4)) : 1;
                    float lit = (float)(Math.Exp(-depth / 300.0) * day), deep = 0.05f;
                    _hud.DrawFill(_meshes, w, h, _ground.AirR * (deep + 0.55f * lit), _ground.AirG * (deep + 0.55f * lit), _ground.AirB * (deep + 0.55f * lit));
                }
                else
                {
                    // under the sea: the water all round in its own colour, the light dying away with depth (sunlight is gone
                    // within a few hundred metres), everything beyond about thirty metres lost in it; the sea bed drawn through it
                    double cr = Math.Sqrt(x * x + y * y + z * z), seaAlt = cr - _ground.Radius; float fogKm = 0, fr = 0, fg = 0, fb = 0;
                    if (_underSea && seaAlt < 0)
                    {
                        double lit = Math.Exp(-(-seaAlt * 1000) / 80.0) * 0.9 + 0.03;
                        fr = (float)(0.08 * lit); fg = (float)(0.28 * lit); fb = (float)(0.42 * lit); fogKm = 0.03f;
                        _hud.DrawFill(_meshes, w, h, fr, fg, fb); _gpu.ClearDepth(); _ground.Dim = (float)lit;
                    }
                    else _ground.Dim = 1;
                    {
                        // the ground's pieces split no finer than four grains to an edge (a cell a quarter of a grain: the finest the land's
                        // shape goes, the grains themselves take over below that, drawn over the ground from a centimetre up and as the matter
                        // below): it split on down to microbe size under the overlay, a queue of a thousand pieces in flight for detail the
                        // grains already covered, and the frame rate went with it
                        var gmx = _ground.Solid && cr > 0 ? _ground.GrainMix(y / cr) : null;
                        _ground.FinestEdgeKm = gmx != null && gmx.Size > 0 ? Math.Max(1e-7, gmx.Size * 2) : 0;
                    }
                    _ground.Draw(_meshes, x, y, z, view, kproj, time, 3.0 * Math.Max(1, h / 600.0) * Math.Max(0.3, Math.Min(3, Math.Sqrt(Window.Detail))), fogKm, fr, fg, fb, 0, 0, w, h, 400);   // detail scales with the GPU use
                }
            }
            _matterCardDrawn = false;
            _hud.CardTop = _tour != null ? (float)(h * 0.1 + 4) : 4; _hud.CardBottom = _tour != null ? (float)(h * 0.9 - 4) : (float)(h * 0.66);   // (never more than two thirds of the screen: a long card is cut short)   // (every card between the tour's bars: a tall one is cut short, never pushed up under the top bar)
            double overAlt = _ground != null && _matterScale <= 0 ? AboveGround() / Units.Km(1) : 1e30;
            if (overAlt < MatterView.OverlayKm)
            {
                // a centimetre over the ground: its grains begin to show, lying over it, specks that grow as you come down
                SetMatter(); _gpu.ClearDepth();
                Mat4 ov = MatterFrame(look);
                _matter.Floor = false; MatterDrawTimed(ov, Math.Max(MatterView.BottomKm, overAlt / 0.6), w, h, _matterClock, _meshes, true);   // (the planet's ground is the floor here)
                {
                    // a grain picked over the ground: ringed, with its card, as anything picked anywhere
                    _gpu.ClearDepth(); double gqx, gqy, gqr;
                    if (_matter.PickedOnScreen(out gqx, out gqy, out gqr)) { _hud.DrawRing(_meshes, gqx, gqy, Math.Min(gqr, h * 0.45), w, h); if (_hud.Visible) { _hud.DrawCard(_meshes, MatterCard(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; } }   // (the card at the left edge, as every card: beside the ring it ran into the other cards)
                }
            }
            if (_spaceScale > 0)
            {
                // in a nebula's or cluster's gas: its atoms in front of space (its glow round you still drawn behind), and below a tenth
                // of a millimetre only the matter itself, no stars or galaxies behind (a draw distance for the very small)
                _gpu.ClearDepth();
                Mat4 sv;
                if (!_matter.InSpace && _bodyUp != null && _bodyUpGen == _matter.Generation) sv = MatterFrameFor(new Vec3((float)_bodyUp[0], (float)_bodyUp[1], (float)_bodyUp[2]), new Vec3((float)_bodyStar[0], (float)_bodyStar[1], (float)_bodyStar[2]), look);   // on a small body's ground: its own up and its star's light, as on a world's
                else
                {
                    if (_matter.SpaceKind == 12) { _hud.DrawFill(_meshes, w, h, 0, 0, 0); _gpu.ClearDepth(); }   // inside a black hole: pitch black, all the way down, the well's grid the one reference
                    _matter.Light[0] = 0.4f; _matter.Light[1] = 0.7f; _matter.Light[2] = 0.6f;
                    var slk = Look; _matter.SetEye(slk.X, slk.Y, slk.Z);                     // in the gas too: your eye goes round the point you dive into
                    sv = Mat4.LookAt(new Vec3(0, 0, 0), slk, ViewUp());                // (the camera's own up: never flipping)
                }
                _matter.Floor = _matter.InSpace ? false : _spaceScale <= 1e-7;             // (a body's ground: its face is the floor only once you are down at it; in space or a hole, none: it had been drawn over the fall's grid)
                MatterDrawTimed(sv, _spaceScale, w, h, _matterClock, _meshes, false);
                if (_matter.SpaceKind == 12) { _gpu.ClearDepth(); DrawFallGrid(vp, right, up, w, h, time); }   // the fall's well, over everything but the ring and the card
                _gpu.ClearDepth();
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);
                if (_hud.Visible) { _hud.DrawCard(_meshes, MatterCard(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; }   // (on the tour below its top bar, which had hidden the card's first lines)
            }
            if (_matterScale > 0)
            {
                // the matter, in front of everything; above a tenth of a millimetre the ground still shows through behind it
                _gpu.ClearDepth();
                Mat4 mview = MatterFrame(look);
                _matter.Floor = _matterScale <= 1e-7;                                      // (its own floor only once the planet's ground is no longer drawn behind)
                MatterDrawTimed(mview, _matterScale, w, h, _matterClock, _meshes, false);
                _gpu.ClearDepth();                                                     // its ring and card on top of the grains
                double qx, qy, qr; if (_matter.PickedOnScreen(out qx, out qy, out qr)) _hud.DrawRing(_meshes, qx, qy, Math.Min(qr, h * 0.45), w, h);   // what you picked, ringed
                if (_hud.Visible) { _hud.DrawCard(_meshes, MatterCard(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 24 * Math.Max(1, h / 1080.0), w, h); _matterCardDrawn = true; }   // (on the tour below its top bar, which had hidden the card's first lines)   // what you are in, and what you picked
            }
            _gpu.ClearDepth();                                                         // the scale, cards and rings always on top of everything
            DrawScale(w, h);
            // (no graph of the well on screen: the well itself is drawn round you inside, and the card carries the numbers)   // a black hole picked and near, or the fall into it: its gravity well graphed, and where you are on it
            if (_tour != null)
            {
                // the tour is a film: letterbox bars, the caption in the lower one, faded in after a moment and out before the cut
                _hud.DrawBars(_meshes, w, h, 0.1f);
                _gpu.ClearDepth();                                                     // the caption in front of the bar it sits in
                var cs = _tourAt < _tour.Count ? _tour[_tourAt] : null;
                float cap = cs == null ? 0 : (float)Math.Min(1, Math.Max(0, (_tourT - 0.8) / 1.5));
                if (cs != null && cs.Do != "pick" && cap > 0.05 && TourCaption.Length > 0)
                {
                    // the story, and under it what is really in view and in focus now, written from what is there (this galaxy's
                    // own name and kind, this star, this world, this mineral, this atom), so every run tells its own journey
                    var lines = new List<string> { TourCaption };
                    var now = _matterScale > 0 || _spaceScale > 0 ? MatterCard() : (_tKind >= 0 ? Card() : null);
                    if (now != null && now.Count > 0) lines.Add("now: " + now[0] + (now.Count > 1 ? ", " + now[1] : ""));
                    _hud.DrawBarText(_meshes, lines, w * 0.3, w, h, 0.1f, cap);          // in the lower bar, to the right of the scale, across its width (it had been held above two thirds of the screen: a paragraph over the middle of the view)
                }
                _gpu.ClearDepth();                                                     // the cards and the frames a second on top of the bars
                // what is in view and in focus, written as you go, just below the top bar
                // (the matter's card is drawn once, at the left edge, on the tour as off it: a second drawing here drew over the first, a garble)
            }
            else if (_pausedTour != null)
            {
                // a tour you took the controls from, or one that has ended: it says so, why, and how to go on
                var lines = new List<string>();
                if (_pausedWhy == null) { lines.Add("The tour is over"); lines.Add("R: watch it again    Backspace: close this"); }
                else
                {
                    string at = _pausedAt < _pausedTour.Count ? _pausedTour[_pausedAt].Caption : "";
                    lines.Add("Tour paused: " + _pausedWhy);
                    if (at.Length > 0) lines.Add("at: " + at);
                    lines.Add("T: carry on    R: start again    Backspace: close this");
                }
                _hud.DrawBarText(_meshes, lines, w * 0.3, w, h, 0.12f, 1);             // (along the foot of the picture, never over its middle)
            }
            if (Loading) { _gpu.ClearDepth(); _hud.DrawLoading(_meshes, w, h, _loadShare, _loadLines ?? new[] { "Making the universe" }); }   // (the start's loading screen over everything)
            if (Settings.Get("show-fps", true))                                       // frames a second, top right (View > Show FPS)
            {
                // and with it, when asked (View > Show GPU and CPU stats): the processor's and the card's time a frame, the
                // processor's share of every core, the memory, the card's own memory and how busy it is; the card sized to its
                // longest line and flush with the right edge
                bool stats = Settings.Get("show-stats", true);
                var sl = new List<string> { Window.Fps.ToString("0") + " FPS" + (stats && Window.Fps > 0 ? ", " + (1000 / Window.Fps).ToString("0.0") + " ms a frame" : "") };
                if (stats) sl.AddRange(Stats.Lines);
                int longest = 0; foreach (var l in sl) longest = Math.Max(longest, l.Length); longest = Math.Min(longest, _hud.WrapAt);   // (the card wraps its lines: never wider than a wrapped line, else a long line pushed it left over the pick's card)
                double spx = Math.Max(1, h / 540.0), scw = longest * 6.6 * spx + 12 * spx;
                double sy0 = _tour != null ? h * 0.11 : 12 * Math.Max(1, h / 1080.0);
                _hud.DrawCard(_meshes, sl, w - scw - 8 * spx, sy0, w, h);
                _statsLeft = w - scw - 8 * spx; _statsBottom = sy0 + sl.Count * 11 * spx + 12 * spx;   // (where the stats sit: a pick's card never lands on them)
            }
            else { _statsLeft = w; _statsBottom = 0; }
            double mdx, mdy, mdz, mrad, msx, msy;
            if (_tKind >= 0 && _matterScale <= 0 && _spaceScale <= 0 && TargetFromCamera(out mdx, out mdy, out mdz, out mrad) && ToScreen(mdx, mdy, mdz, out msx, out msy))   // (in a dive, the matter's card alone: the two drawn together garbled each other)
            {
                double l = Math.Sqrt(mdx * mdx + mdy * mdy + mdz * mdz);
                double rpx = Math.Max(14 * Math.Max(1, h / 1080.0), mrad / Math.Max(1e-30, l) / Math.Tan(0.45) * h * 0.5 * 1.25);
                bool fills = rpx > h * 0.45;                                           // bigger than the view: no ring, the card sits top left
                if (!fills) _hud.DrawRing(_meshes, msx, msy, rpx, w, h);
                // its card at the left edge, under the top, never over what you look at (beside the ring it sat across the middle of the
                // view, a wall of text over the hole); on the tour too, where the caption at the foot says what the shot is and the card what the thing is
                if (_hud.Visible && !_matterCardDrawn) _hud.DrawCard(_meshes, Card(), 24 * Math.Max(1, h / 1080.0), _tour != null ? h * 0.11 : 72 * Math.Max(1, h / 1080.0), w, h);   // (one card at a time: when the matter's is up, the world's waits; on the tour below its top bar)
            }
        }

        // the scale on screen: how many km one pixel spans at the distance of the nearest thing, in the middle of the view
        // how far away what you are looking at is (km), for the scale: in the gas or the matter, your own fine distance; flying to
        // a point in a nebula, the distance to that point (not to some star nearby); else the nearest thing
        // the matter's draw, timed: its part of the frame's processor time (on the stats card with the ground's)
        private double _matterMs;
        private void MatterDrawTimed(Mat4 view, double scaleKm, int w, int h, double time, MeshPipes pipes, bool overlay) { var sw = System.Diagnostics.Stopwatch.StartNew(); _matter.Draw(view, scaleKm, w, h, time, pipes, overlay); _matterMs = sw.Elapsed.TotalMilliseconds; }
        /// <summary>The processor's part of the frame by phase: the ground's pieces chosen, drawn and taken in, the matter; for the stats card.</summary>
        public string CpuPhases { get { return (_ground != null && _ground.FrameStats != null ? _ground.FrameStats + ", " : "") + "matter " + _matterMs.ToString("0.0") + " ms, bodies " + _extras.Count; } }
        private double ViewDistanceKm()
        {
            if (_spaceScale > 0) return _spaceScale * 0.6;
            if (_matterScale > 0) return _matterScale * 0.6;
            double qx, qy, qz, qr;
            if (_tKind == 5 && TargetFromCamera(out qx, out qy, out qz, out qr)) return Math.Max(1e-30, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1));
            // on the tour, how far it is to what the tour goes to (the picked world's ground; anything else picked, itself), never
            // just the nearest thing (beside a black hole every other zoom would think itself already there)
            if (_tour != null && AtGroundBody()) return Math.Max(1e-30, AboveGround()) / Units.Km(1);
            if (_tour != null && _tKind >= 0 && _farScale <= 0 && TargetFromCamera(out qx, out qy, out qz, out qr)) return Math.Max(1e-30, Math.Sqrt(qx * qx + qy * qy + qz * qz) / Units.Km(1));
            return Math.Max(1e-30, Math.Max(NearestDistance(), _farScale)) / Units.Km(1);   // zoomed out, the view is as wide as how far out you have pulled (out among the web a galaxy is always near)
        }

        private void DrawScale(int w, int h)
        {
            double d = ViewDistanceKm(), viewKm = 2 * d * Math.Tan(0.45) * w / (double)Math.Max(1, h);
            _gpu.ClearDepth();
            _hud.Draw(_meshes, viewKm / Math.Max(1, w), w, h);
        }

        private float _sky = 1;                                                           // how much of space shows behind (fades away at the very small)
        private int _comaN, _tailN; private UploadBuffer _tailBuf;
        private readonly float[] _pointConsts = new float[BillboardPipeline.ConstantCount];
        private int _levelLogged = int.MinValue;
        private readonly List<KeyValuePair<double, int>> _nebNear = new List<KeyValuePair<double, int>>(), _galOrder = new List<KeyValuePair<double, int>>(); private readonly Dictionary<int, double[]> _nebAt = new Dictionary<int, double[]>();
        private void DrawPoints(UploadBuffer buf, int count, Mat4 vp, Vec3 right, Vec3 up, float sizeScale, float minSize, int w, int h, double time, float maxSize = 0, float bright = 1, float style = 2, float holeK = 0, float holeMode = 0, float hx = 0, float hy = 0, float hz = 0, float drift = 0)
        {
            if (buf == null || count <= 0) return;
            float b = bright * _sky; if (b <= 0.003f) return;
            var c = _pointConsts; Array.Clear(c, 0, c.Length);   // (one block, emptied each call: each draw copies its constants when it is recorded)
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = sizeScale; c[26] = style; c[27] = 4; c[28] = w; c[29] = h; c[30] = minSize; c[31] = maxSize; c[32] = 1 - Math.Min(1, b); c[34] = drift;   /* (gravity off: how far a galaxy's stars have flown, in their own frame) */
            c[36] = _lens[0]; c[37] = _lens[1]; c[38] = _lens[2]; c[39] = _lens[3]; c[40] = w / (float)Math.Max(1, h);
            c[43] = _web[0]; c[44] = _web[1]; c[45] = _web[2]; c[46] = _web[3]; c[47] = _web[4];   // the web's light, sifted on the card (0: not the web)
            if (holeK > 0) { c[33] = holeK; c[35] = holeMode; c[43] = 0; c[44] = hx; c[45] = hy; c[46] = hz; c[47] = 0; }   // a feeding black hole's parts: moved on the card in its own frame (the pace, which part, where the camera is in that frame)
            _dots.Draw(buf, count, c, 0, 0, w, h);
        }

    }
}
