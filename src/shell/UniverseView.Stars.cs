// Universe · shell · the universe view, its part: stars (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── the stars of the space you are in ──
        // A real galaxy holds about a hundred billion stars; its sample shows 150,000. Around the camera, the real ones are made:
        // space is cut into cubes about 25 light-years across, and each cube near you gets the stars the galaxy's density gives it
        // there (from how many sample stars share its big grid cell), placed from the cube's own position, so they are always the
        // same stars when you come back.
        private const double RealStars = 1e11;
        private double LocalCell = 25 / 50000.0, LocalReach = 60 / 50000.0;   // the cubes (25 light-years) and how far round you they are made (60), in galaxy units: content/universe.unt real-stars-cube-ly and real-stars-reach-ly
        private readonly Dictionary<long, StarVertex[]> _localCells = new Dictionary<long, StarVertex[]>();
        private readonly List<StarVertex> _localList = new List<StarVertex>();   // in the near galaxy's own frame
        private readonly List<int> _localKind = new List<int>(); private readonly List<long> _localId = new List<long>();
        private UploadBuffer _localBuf; private StarVertex[] _localDrawn = new StarVertex[0]; private int _localCount; private readonly HashSet<int> _localDead = new HashSet<int>();   // (the stars round you that died in supernovae: gone)
        private long _localKey = long.MinValue; private long _localCX, _localCY, _localCZ;
        private double _localOX, _localOY, _localOZ;   // the list's origin (the corner of the cube it was built round), galaxy frame: every star an offset from it, so the float positions are exact to a fraction of an AU and nothing snaps to a lattice

        // the galaxy's density of real stars at a point of its own frame (stars per galaxy unit cubed)
        private double Density(double lx, double ly, double lz)
        {
            int x = Cell(lx), y = Cell(ly), z = Cell(lz);
            if (x < 0 || y < 0 || z < 0 || x >= GridN || y >= GridN || z >= GridN) return 0;
            int n = 0; for (int k = _gridHead[(x * GridN + y) * GridN + z]; k >= 0; k = _gridNext[k]) n++;
            double cellSize = 3.2 / GridN;
            return n / (double)NearStars * RealStars / (cellSize * cellSize * cellSize);
        }

        // refresh the local stars when the camera moves into another cube
        private void LocalStars()
        {
            if (_near < 0 || _nearLocal == null) { _localCount = 0; return; }
            double lx, ly, lz; ToGalaxy(_galaxies[_near], _cx, _cy, _cz, out lx, out ly, out lz);
            long cx = (long)Math.Floor(lx / LocalCell), cy = (long)Math.Floor(ly / LocalCell), cz = (long)Math.Floor(lz / LocalCell);
            long key = (cx * 73856093L) ^ (cy * 19349663L) ^ (cz * 83492791L);
            if (key == _localKey) return;
            // flying through faster than a cube or two a frame (zooming out of the galaxy, crossing it): its real stars are not made
            // (they would be gone before they were seen, and making them every frame stalls it); the galaxy's own stars show it
            bool fast = _localKey != long.MinValue && (Math.Abs(cx - _localCX) > 2 || Math.Abs(cy - _localCY) > 2 || Math.Abs(cz - _localCZ) > 2);
            _localCX = cx; _localCY = cy; _localCZ = cz;
            if (fast) { _localKey = long.MinValue + 1; _localCount = 0; _localCells.Clear(); return; }
            _localKey = key; _localOX = cx * LocalCell; _localOY = cy * LocalCell; _localOZ = cz * LocalCell;
            _localList.Clear(); _localKind.Clear(); _localId.Clear(); _localVersion++;   // (remade: put on the card again)
            var keep = new Dictionary<long, StarVertex[]>();
            // every cube within the reach of where you are (not of your cube: the sky is as deep in stars on every side), the ones
            // already made kept, so moving a cube on makes only the new face
            int nr = (int)Math.Ceiling(LocalReach / LocalCell); double r2 = Math.Pow(LocalReach + LocalCell * 0.87, 2);
            for (long ox = -nr; ox <= nr; ox++) for (long oy = -nr; oy <= nr; oy++) for (long oz = -nr; oz <= nr; oz++)
            {
                long x = cx + ox, y = cy + oy, z = cz + oz, id = (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
                double mx = (x + 0.5) * LocalCell - lx, my = (y + 0.5) * LocalCell - ly, mz = (z + 0.5) * LocalCell - lz;
                if (mx * mx + my * my + mz * mz > r2 || _localList.Count > 600000) continue;
                StarVertex[] cell;
                if (!_localCells.TryGetValue(id, out cell))
                {
                    double vol = LocalCell * LocalCell * LocalCell, expect = Density((x + 0.5) * LocalCell, (y + 0.5) * LocalCell, (z + 0.5) * LocalCell) * vol;
                    var rng = new Rng((ulong)id ^ _galaxies[_near].Seed);
                    int count = (int)Math.Min(4000, Math.Floor(expect + rng.NextDouble()));
                    cell = new StarVertex[count];
                    for (int k = 0; k < count; k++)
                    {
                        var kind = PickKind(rng);
                        var kc = _kinds[kind];
                        cell[k] = new StarVertex { X = (float)(rng.NextDouble() * LocalCell), Y = (float)(rng.NextDouble() * LocalCell), Z = (float)(rng.NextDouble() * LocalCell),   // (within its cube)
                            Size = (float)Units.Km(696000), R = kc.R, G = kc.G, B = kc.B, A = kind };      // A carries the kind's index
                    }
                }
                keep[id] = cell;
                float ox2 = (float)((x - cx) * LocalCell), oy2 = (float)((y - cy) * LocalCell), oz2 = (float)((z - cz) * LocalCell);   // its cube's corner from the origin
                for (int k = 0; k < cell.Length; k++) { var v = cell[k]; v.X += ox2; v.Y += oy2; v.Z += oz2; _localList.Add(v); _localKind.Add((int)cell[k].A); _localId.Add(id * 4096 + k); }
            }
            _localCells.Clear(); foreach (var kv in keep) _localCells[kv.Key] = kv.Value;
            _localCount = _localList.Count;
            // the stars that have died in supernovae (this galaxy's, back a hundred thousand years): each blast is at the star nearest
            // it here (within a few light-years), and that star is gone: not drawn, no system opens at it; what stands there is the
            // neutron star or black hole it left, among the bodies round you, its worlds gone with it
            _localDead.Clear();
            if (_near >= 0 && _near < _galaxies.Count && _localCount > 0)
            {
                var gq = _galaxies[_near]; var c = Supernovae(gq, true); double reach = 8 / (gq.Scale * Units.LightYearsPerUnit), near = LocalCell * 4;
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (years < 0 || years > _snRemnantYears) continue;
                    if (Math.Abs(sn.X - _localOX) > near || Math.Abs(sn.Y - _localOY) > near || Math.Abs(sn.Z - _localOZ) > near) continue;
                    int bi = -1; double bd = reach * reach;
                    for (int k = 0; k < _localCount; k++) { var s0 = _localList[k]; double dx = s0.X + _localOX - sn.X, dy = s0.Y + _localOY - sn.Y, dz = s0.Z + _localOZ - sn.Z, dd = dx * dx + dy * dy + dz * dz; if (dd < bd) { bd = dd; bi = k; } }
                    if (bi < 0) continue;
                    var sv = _localList[bi]; _localDead.Add(bi); sn.X = sv.X + _localOX; sn.Y = sv.Y + _localOY; sn.Z = sv.Z + _localOZ; ToUniverse(gq, sn.X, sn.Y, sn.Z, out sn.Ux, out sn.Uy, out sn.Uz);
                }
            }
            if (_localDrawn.Length < _localCount) _localDrawn = new StarVertex[_localCount + 1024];
            if (_localBuf == null || _localBuf.Bytes < (long)_localCount * Stride) { if (_localBuf != null) _localBuf.Dispose(); _localBuf = new UploadBuffer(_gpu.Device, (long)(_localCount + 1024) * Stride * 2); }
        }

        // a star kind by how common each is (the same shares the galaxy generator uses)
        private int PickKind(Rng rng)
        {
            double total = 0; foreach (var k in _kinds) total += k.Weight;
            double r = rng.NextDouble() * total;
            for (int i = 0; i < _kinds.Count; i++) { r -= _kinds[i].Weight; if (r <= 0) return i; }
            return 0;
        }

        // the near galaxy's star nearest the camera (index, and distance in universe units), or -1
        private int NearestStar(out double dist)
        {
            dist = double.MaxValue; if (_near < 0 || _nearLocal == null) return -1;
            double lx, ly, lz; ToGalaxy(_galaxies[_near], _cx, _cy, _cz, out lx, out ly, out lz);
            // the real stars of this space (index from 1,000,000 up), then the galaxy's sample stars
            int bestL = -1; double bl = double.MaxValue;
            for (int k = 0; k < _localCount; k++) { var s0 = _localList[k]; double dx = s0.X + _localOX - lx, dy = s0.Y + _localOY - ly, dz = s0.Z + _localOZ - lz, d = dx * dx + dy * dy + dz * dz; if (d < bl) { bl = d; bestL = k; } }
            int gx = Cell(lx), gy = Cell(ly), gz = Cell(lz), best = -1; double bd = double.MaxValue;
            for (int ox = -1; ox <= 1; ox++) for (int oy = -1; oy <= 1; oy++) for (int oz = -1; oz <= 1; oz++)
            {
                int x = gx + ox, y = gy + oy, z = gz + oz;
                if (x < 0 || y < 0 || z < 0 || x >= GridN || y >= GridN || z >= GridN) continue;
                for (int k = _gridHead[(x * GridN + y) * GridN + z]; k >= 0; k = _gridNext[k])
                {
                    var s = _nearLocal[k]; double dx = s.X - lx, dy = s.Y - ly, dz = s.Z - lz, d = dx * dx + dy * dy + dz * dz;
                    if (d < bd) { bd = d; best = k; }
                }
            }
            if (bestL >= 0 && bl < bd) { dist = Math.Sqrt(bl) * _galaxies[_near].Scale; return 1000000 + bestL; }
            if (best >= 0) dist = Math.Sqrt(bd) * _galaxies[_near].Scale;
            // a star cluster's own stars round you (index from 2,000,000 up), so each can open into its solar system too
            for (int q = 0; q < _cstars.Count; q++)
            {
                var cs = _cstars[q]; double dx = cs[0] - _cx, dy = cs[1] - _cy, dz = cs[2] - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < dist) { dist = d; best = 2000000 + q; }
            }
            return best;
        }

        // where a star of the near galaxy sits in the universe
        private void StarPos(int k, out double x, out double y, out double z)
        {
            if (k >= 2000000) { var cs = _cstars[k - 2000000]; x = cs[0]; y = cs[1]; z = cs[2]; return; }   // a cluster's star: already in universe units
            if (k >= 1000000) { var sl = _localList[k - 1000000]; double ddx, ddy, ddz; LocalDrift(k - 1000000, out ddx, out ddy, out ddz); ToUniverse(_galaxies[_near], sl.X + ddx + _localOX, sl.Y + ddy + _localOY, sl.Z + ddz + _localOZ, out x, out y, out z); return; }   // (gravity off: flown on from its place)   // (a real star: its offset from the list's origin)
            var s = _nearLocal[k]; double gx = s.X, gz = s.Z;
            {
                // turned as the card turns it (a disc galaxy's stars round its middle, less your own turn: Pipeline.cs up.w)
                var gg = _galaxies[_near]; float cr; float d = GalaxyTurn(gg, Placement(gg), out cr);
                if (d != 0) { double rr = Math.Max(1e-4, Math.Sqrt(gx * gx + gz * gz)), an = d / rr - (cr > 0 ? d / cr : 0), ca = Math.Cos(an), sa = Math.Sin(an); double nx = gx * ca - gz * sa, nz = gx * sa + gz * ca; gx = nx; gz = nz; }
            }
            ToUniverse(_galaxies[_near], gx, s.Y, gz, out x, out y, out z);
        }
        // a cluster star's kind: in a globular, red giants, orange K and yellow G stars; in an open cluster, blue-white B and white A stars
        private int ClusterKind(double[] cs)
        {
            string id = cs[3] == 0 ? (cs[4] < 0.1 ? "red-giant" : cs[4] < 0.4 ? "k-type" : "g-type") : (cs[4] < 0.3 ? "b-type" : "a-type");
            for (int i = 0; i < _kinds.Count; i++) if (_kinds[i].Id == id) return i;
            return 0;
        }
        private int StarKindOf(int k) { return k >= 2000000 ? ClusterKind(_cstars[k - 2000000]) : k >= 1000000 ? _localKind[k - 1000000] : _nearKinds[k]; }
        private ulong StarSeed(int k) { if (k >= 2000000) { var cs = _cstars[k - 2000000]; return (ulong)(long)(cs[0] / Units.Km(1e6)) * 73856093UL ^ (ulong)(long)(cs[2] / Units.Km(1e6)) * 19349663UL ^ 0xC1C1UL; } return k >= 1000000 ? (ulong)_localId[k - 1000000] : (ulong)k; }

        // open the system of the star you have come close to (and anchor the camera on it), or close it when you leave
        private void ChooseSystem()
        {
            double d; int k = NearestStar(out d);
            if (_system != null && k >= 0 && k != _sysStar)
            {
                // the same star can come back under a new number as the stars around you are made again: it is another
                // star only if it sits somewhere else (more than an AU from this system's star, the anchor)
                double sx0, sy0, sz0; StarPos(k, out sx0, out sy0, out sz0);
                if (Math.Abs(sx0 - _ax) + Math.Abs(sy0 - _ay) + Math.Abs(sz0 - _az) < Units.Au(1)) _sysStar = k;
            }
            if (_system != null && (k != _sysStar && d < SystemRange * 0.5 || DistToSysStar() > SystemRange * 1.5))
            {
                ZoomLog("SYSTEM CLOSED: " + (DistToSysStar() > SystemRange * 1.5 ? "left it, " + Units.Describe(DistToSysStar()) + " from its star" : "another star " + k + " (was " + _sysStar + ") within " + Units.Describe(d)));
                CloseSystem();
            }
            if (_system == null && k >= 0 && d < SystemRange && !(k >= 1000000 && _localDead.Contains(k - 1000000)))   // (never at a star that died in a supernova: its remnant stands there, its worlds gone)
            {
                _sysStar = k; ZoomLog("SYSTEM OPENED: star " + k + " at " + Units.Describe(d));
                var kind = _kinds[Math.Max(0, Math.Min(_kinds.Count - 1, StarKindOf(k)))];
                _system = SpaceGen.System(_content, kind, _galaxies[_near].Seed * 31UL + StarSeed(k));
                _bodyVerts = new StarVertex[_system.Bodies.Count + 1];
                _sysBodies = new UploadBuffer(_gpu.Device, (long)_bodyVerts.Length * Stride);
                _staticShifted = new StarVertex[_system.Static.Length];
                _sysStatic = new UploadBuffer(_gpu.Device, (long)Math.Max(1, _system.Static.Length) * Stride);
                double sx, sy, sz; StarPos(k, out sx, out sy, out sz); Anchor(sx, sy, sz);
                // the star's own body: its plasma surface and corona, in its kind's colours (as the star close-ups are made)
                string sname; _starCorona = StarGen.Build(_content, kind.Id, _galaxies[_near].Seed * 77UL + StarSeed(k), out sname, out _starSurf);
                _starSurfVerts = new StarVertex[_starSurf.Normals.Length];
                _coronaBuf = new UploadBuffer(_gpu.Device, (long)Math.Max(1, _starCorona.Length) * Stride); if (_starCorona.Length > 0) _coronaBuf.Write(_starCorona, _starCorona.Length);
                _surfBuf = new UploadBuffer(_gpu.Device, (long)_starSurfVerts.Length * Stride);
            }
            if (_system == null && _near >= 0)
            {
                // between the stars the camera is measured from the galaxy's middle, except when flying to a point in a nebula or a
                // cluster: then from that point itself, as from a star or planet (from tens of thousands of light-years away the
                // finest step a number can hold is tens of kilometres, and the zoom would stall)
                if (_tKind == 5) { if (_ax != _tx || _ay != _ty || _az != _tz) Anchor(_tx, _ty, _tz); }
                else { var g = _galaxies[_near]; if (Math.Abs(_ax - g.X) + Math.Abs(_ay - g.Y) + Math.Abs(_az - g.Z) > 0) Anchor(g.X, g.Y, g.Z); }
            }
        }

        private double DistToSysStar() { return Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz); }    // anchored on the star: the offset is the distance

        private void CloseSystem()
        {
            DropPlanet(); _hadPos = false; _followBody = -1;                       // a new system's bodies are new: ride along afresh
            _system = null; _sysStar = -1;
            if (_sysStatic != null) { _sysStatic.Dispose(); _sysStatic = null; }
            if (_sysBodies != null) { _sysBodies.Dispose(); _sysBodies = null; }
            if (_coronaBuf != null) { _coronaBuf.Dispose(); _coronaBuf = null; }
            if (_surfBuf != null) { _surfBuf.Dispose(); _surfBuf = null; }
            _starSurf = null;
        }

        // a planet or moon's centre from its star, in double precision
        private void BodyPos(int i, out double x, out double y, out double z)
        {
            var tmp = new StarVertex[_system.Bodies.Count + 1]; double fx, fy, fz;
            SpaceGen.BodyVerts(_system, _simYears, 1 / 20.0, 0.05, tmp, i, 1.0, out fx, out fy, out fz);
            x = fx; y = fy; z = fz;
        }

        // the camera from the planet's centre, in km
        private void CamFromPlanet(out double x, out double y, out double z)
        {
            double k = Units.Km(1); x = (_ox - _px) / k; y = (_oy - _py) / k; z = (_oz - _pz) / k;
        }

        // make the ground of the body you have come close to, or let it go once you leave
        private void ChoosePlanet()
        {
            // a small body you are at (an asteroid, a comet's nucleus, a Kuiper body, a rogue world) is a world like any: within forty
            // of its radii its ground is made by the same ground system a planet's is, with its own shape, size, spin, minerals and
            // elements, and everything from there down (the landing, the grains, a grain's face, the crystal, the atoms, the picks on
            // its surface, the way out) is the one code a world uses: nothing special for rocks
            var sb = _tKind == 6 && _tExtra != null && _tExtra.Shape.Length > 0 && !_tExtra.Hole && _tExtra.Mix != null && _tExtra.R > 0 ? _tExtra : _groundExtra;   // (or the body whose ground you are on: its ground lives by your distance from it, as a planet's does, picked or not; the tour's stand stop clears the pick while you stand on the ground, and the ground had gone with the pick, the camera left behind in empty space, every stop after it zooming into nothing)
            if (_system == null && sb == null) { DropPlanet(); return; }
            int best = -1; double bd = double.MaxValue;
            if (_system != null) for (int i = 0; i < _system.Bodies.Count; i++)
            {
                double x, y, z; BodyPos(i, out x, out y, out z);
                double d = Math.Sqrt((x - _ox) * (x - _ox) + (y - _oy) * (y - _oy) + (z - _oz) * (z - _oz)) / _system.Bodies[i].RadiusUnits;
                if (d < bd) { bd = d; best = i; }
            }
            if (sb != null) { double sx = RelX(sb) - _ox, sy = RelY(sb) - _oy, sz = RelZ(sb) - _oz, sd = Math.Sqrt(sx * sx + sy * sy + sz * sz) / sb.R; if (sd < bd) { bd = sd; best = -2; } }
            _cpBest = best; _cpBd = bd;
            if (bd > PlanetRange * 1.2) { DropPlanet(); return; }
            if ((best != _planet || (best == -2 && _groundExtra != sb)) && bd < PlanetRange)
            {
                DropPlanet();
                if (best == -2)
                {
                    _planet = -2; _groundExtra = sb; _spinRef = Spin(-2);
                    uint sbSeed = (uint)(sb.Lines.Count > 1 ? sb.Lines[1].GetHashCode() : sb.Name.GetHashCode());
                    _ground = new PlanetTerrain(_gpu.Device, "asteroid", sb.Name, sbSeed, false, sb.R / Units.Km(1)); _ground.MixId = sb.Mix;   // (its own radius, its own mix of minerals)
                    ZoomLog("GROUND made for " + sb.Name + " (" + ScaleHud.Words(sb.R / Units.Km(1) * 2) + " across, mix " + sb.Mix + "), as a world's");
                }
                else
                {
                var b = _system.Bodies[best];
                _planet = best;
                _spinRef = Spin(best);                                                     // (its day as it is now: the light where its star really is)
                _ground = new PlanetTerrain(_gpu.Device, b.Terrain ?? DeepView.TerrainKind(b.Kind), b.Kind, WorldSeed(best), false, b.RadiusUnits / Units.Km(1)) { Locked = b.Locked || (b.Parent < 0 && PlanetTerrain.TerrainKind(b.Terrain).Locked) };   // (a moon: its own regolith grains)
            }
                }
            if (_planet == -2 && _groundExtra != null) { _px = RelX(_groundExtra); _py = RelY(_groundExtra); _pz = RelZ(_groundExtra); }
            else if (_planet >= 0) BodyPos(_planet, out _px, out _py, out _pz);
        }

        // the camera rides along with the planet or moon you picked (or are at) as it goes round its star
        private void FollowBody()
        {
            int follow = _tKind == 2 && _system != null && _tIndex < _system.Bodies.Count ? _tIndex : _tKind == 6 ? -1 : _planet;
            if (_planet == -2) return;                                                       // (on a small body's ground the ride is that body's own, in UpdateExtras: riding with the nearest planet too moved the camera twice a frame, off the stone)   // (an asteroid, comet or black hole picked: they do not move, and riding with the planet you left shifted the camera off them every frame)
            if (follow < 0 && _system != null)
            {
                // nothing picked: the nearest planet or moon is matched once you are within about 300 of its radii
                double bd = double.MaxValue;
                for (int i = 0; i < _system.Bodies.Count; i++)
                {
                    double x, y, z; BodyPos(i, out x, out y, out z);
                    double d = Math.Sqrt((x - _ox) * (x - _ox) + (y - _oy) * (y - _oy) + (z - _oz) * (z - _oz)) / _system.Bodies[i].RadiusUnits;
                    if (d < bd) { bd = d; follow = i; }
                }
                if (bd > 300) follow = -1;
            }
            if (follow >= 0)
            {
                double fx, fy, fz; BodyPos(follow, out fx, out fy, out fz);
                if (_hadPos && follow == _followBody) { _ox += fx - _fbx; _oy += fy - _fby; _oz += fz - _fbz; }
                _fbx = fx; _fby = fy; _fbz = fz; _followBody = follow; _hadPos = true;
            }
            else { _hadPos = false; _followBody = -1; }
        }
        private int _followBody = -1; private double _fbx, _fby, _fbz;

        private bool _hadPos;
        private int _cpBest = -1; private double _cpBd;                         // for the log: the nearest body in its own radii
        private double _spinRef;   // the world's spin when you came to it: its ground is held still and its sky turns by how far it has spun since
        private double SpunSince { get { return _ground != null && (_planet >= 0 || _planet == -2) ? Spin(_planet) - _spinRef : 0; } }   // (a small body's world too: its day of a few hours)
        private void DropPlanet() { if (_ground != null) { _ground.Dispose(); _ground = null; } _planet = -1; _groundExtra = null; }
        // the ground body's name for a card (a world's kind, or a small body's own name)
        private string GroundKind() { return _planet == -2 && _groundExtra != null ? _groundExtra.Name.ToLowerInvariant() : _system != null && _planet >= 0 && _planet < _system.Bodies.Count ? _system.Bodies[_planet].Kind : "the ground"; }
        // whether the pick is the body whose ground is made: the picked world, or the picked small body
        private bool AtGroundBody() { return _ground != null && ((_tKind == 2 && _tIndex == _planet) || (_tKind == 6 && _planet == -2 && _tExtra != null && _tExtra == _groundExtra)); }

        // the top of what is under a direction, in km from the planet's radius: the sea's surface where there is sea and you are
        // above it; the sea bed once you are at the water (the dive: on down through it to the bed); else the real ground (a dry
        // basin below sea level is ground, not water)
        private bool _underSea;                                                           // at or under the sea's surface: the floor under you is the sea bed
        private double Surface(double x, double y, double z, double altKm = double.NaN)
        {
            double h = _ground.GroundAt(x, y, z, _surfaceCell);                           // the grains only as finely as your height needs
            if (h >= 0 || !_ground.HasWater) { if (!double.IsNaN(altKm)) _underSea = false; return h; }
            if (!double.IsNaN(altKm)) _underSea = altKm < 1e-3;                            // (within a metre of the water: on down through it)
            return _underSea ? h : 0;
        }

        // what the matter under you is made of, set once for each spot (about a metre across), so its grains stay put
        private double _matterTop = MatterView.TopKm;
        private double _spaceScale, _spaceTop;
        private double _matterClock;                                                    // the matter's own clock: the game's time, so pausing freezes every electron and quark                                          // into a nebula's or cluster's gas: the fine height, and where it began                                 // zooming out past this goes back up to the ground
        private ulong _matterSpot = ulong.MaxValue; private double _mox, _moy, _moz; private int _matterPlanet = -1;
        // where you are from the spot the matter began at (km east and north there)
        private void MatterOffset(out double e, out double n)
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            var up = new Vec3((float)_mox, (float)_moy, (float)_moz);
            var east = Vec3.Cross(Math.Abs(up.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), up).Normalized; var north = Vec3.Cross(up, east).Normalized;
            double dx = (x / r - _mox) * _ground.Radius, dy = (y / r - _moy) * _ground.Radius, dz = (z / r - _moz) * _ground.Radius;
            e = dx * east.X + dy * east.Y + dz * east.Z; n = dx * north.X + dy * north.Y + dz * north.Z;
        }
        private void SetMatter()
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            double e, n;
            _matter.TempK = 0;                                                             // (set below where the place's temperature is known)
            _matter.Enclosed = _depthKm > 0 && _ground.Solid && DepthLayer() != null;   // (below the ground: inside the rock, its crystal round you every way)
            if (_matter.Enclosed && SetDepthMatter(x / r, y / r, z / r)) return;   // below the ground: the layer you are in (UniverseView.Depth.cs)
            if (_matterSpot != ulong.MaxValue && _matterPlanet == _planet) { MatterOffset(out e, out n); if (e * e + n * n < 5e-4 * 5e-4) { _matter.Follow(e, n); return; } }   // within half a metre: the same grains, passing by
            if (!_ground.Solid)
            {
                // a world with no ground (a giant): what is there is its air, its own gases at their real density
                ulong gspot = (ulong)(long)(x / r * 1e7) * 73856093UL ^ (ulong)(long)(y / r * 1e7) * 19349663UL ^ (ulong)(long)(z / r * 1e7) * 83492791UL;
                if (_matterSpot == gspot && _matterPlanet == _planet) return;
                _matterSpot = gspot; _matterPlanet = _planet; _mox = x / r; _moy = y / r; _moz = z / r;
                string air; if (!PlanetTerrain.Air.TryGetValue(_ground.Kind, out air)) air = "hydrogen 90%, helium 10%";
                var lay = LayerHere();
                if (lay != null && lay.Matter == "core") { _matter.Set("core", false, false, 0.5f, 0.45f, 0.4f, gspot); _matter.SetGround(_ground.Radius * CoreShare, x / r, y / r, z / r, "core", _ground.GrainSeed, 0, 0); _matter.SetAirAbove(null); return; }   // its core: rock, iron and ice
                if (lay != null && lay.Matter == "plasma") { _matter.SetPlasma(gspot); return; }      // metallic hydrogen: protons in a sea of free electrons
                if (lay != null && lay.Matter == "ice") { _matter.Set("ice", false, true, 0.7f, 0.8f, 0.9f, gspot); return; }   // the ice mantle: hot dense water, ammonia, methane
                _matter.SetAir(air, gspot); return;
            }
            bool sea = _ground.HasWater && _ground.GroundAt(x / r, y / r, z / r) < 0, ice = Math.Abs(y / r) > 0.86;
            ulong spot = (ulong)(long)(x / r * 1e7) * 73856093UL ^ (ulong)(long)(y / r * 1e7) * 19349663UL ^ (ulong)(long)(z / r * 1e7) * 83492791UL;   // the same place, the same grains
            _matterSpot = spot; _matterPlanet = _planet; _mox = x / r; _moy = y / r; _moz = z / r;
            float mr0, mg0, mb0, mglow; _ground.Height(x / r, y / r, z / r, 30, out mr0, out mg0, out mb0, out mglow);
            bool molten = mglow > 0.5;                                                    // glowing molten ground: the matter there is melt
            // under the sea the matter is the sea bed (the ocean mix: silt, sand, the sea floor's minerals), with the water above it

            _matter.TempK = molten ? 1400 : _ground.Traits.BaseK;                        // (the ground's temperature: its kind's, content/terrain.unt base-k; molten rock about 1,100 to 1,200 C)
            _matter.Set(molten ? "melt" : sea ? "ocean" : OwnMix(_ground.MixId ?? _ground.Kind, DepthLayers() != null && DepthLayers().Count > 0 ? DepthLayers()[0].Id : "crust"), false, ice && !sea, sea ? 0.2f : 0.58f, sea ? 0.4f : 0.5f, sea ? 0.6f : 0.4f, spot);
            // the planet's own grains, on the land's slope here: the same ones its ground is the top of
            {
                double ux = x / r, uy = y / r, uz = z / r, ex, ey, ez, nx, ny, nz; GrainField.Axes(ux, uy, uz, out ex, out ey, out ez, out nx, out ny, out nz);
                double d = 5e-4, R = _ground.Radius;
                Func<double, double, double, double> H = (a1, b1, c1) => { double l = Math.Sqrt(a1 * a1 + b1 * b1 + c1 * c1); return _ground.BaseAt(a1 / l, b1 / l, c1 / l); };
                double he = (H(ux + ex * d / R, uy + ey * d / R, uz + ez * d / R) - H(ux - ex * d / R, uy - ey * d / R, uz - ez * d / R)) / (2 * d);
                double hn = (H(ux + nx * d / R, uy + ny * d / R, uz + nz * d / R) - H(ux - nx * d / R, uy - ny * d / R, uz - nz * d / R)) / (2 * d);
                if (double.IsNaN(he) || Math.Abs(he) > 20) he = 0; if (double.IsNaN(hn) || Math.Abs(hn) > 20) hn = 0;
                var gm = _ground.GrainMix(uy);
                _matter.SetGround(R, ux, uy, uz, molten ? null : sea ? "ocean" : gm != null ? gm.Id : null, _ground.GrainSeed, he, hn);
                _matter.SetLife(_ground.HasWater && _ground.Traits.Life);   // microbes on a living world's grains
                string airHere;
                if (sea) _matter.SetAirAbove("water 100%", 3.3e22);                      // the sea above the bed: water molecules, packed as a liquid is (about 0.31 nm apart)
                else _matter.SetAirAbove(_ground.AirKm <= 0 || !PlanetTerrain.Air.TryGetValue(_ground.Kind, out airHere) ? null : airHere);   // its own air (a moon: none)
            }
        }

        // how far above the planet's ground the camera is (universe units)
        private double _surfaceCell, _lastAbove = 1;
        // a giant's layers (content/gas-giants.unt @layer: from-r, to-r, matter), and where its core begins
        // (one reader for every body's layers: src/gen/BodyLayers.cs)
        private List<BodyLayer> Layers() { return BodyLayers.Of(_content).InFile(BodyLayers.Giants); }
        private double CoreShare { get { foreach (var l in Layers()) if (l.Matter == "core") return l.To; return 0.15; } }
        // the layer of a giant you are in now (by your share of its radius from its middle); null above its clouds
        private BodyLayer LayerHere()
        {
            if (_ground == null || _ground.Solid) return null; double x, y, z; CamFromPlanet(out x, out y, out z); double share = Math.Sqrt(x * x + y * y + z * z) / _ground.Radius;
            BodyLayer best = null;
            foreach (var l in Layers())
            {
                if (share < l.From || share > l.To) continue;
                if (l.In != null && !l.In.Contains(_ground.Kind)) continue;              // only the layers its kind has (gas-giants.unt in:)
                if (best == null || l.To - l.From < best.To - best.From) best = l;
            }
            return best;
        }
        // where the ground hands you on into the matter of the grain under you: a third of a grain above it (about 30 micrometres
        // over sand), before the ground's ever finer grain tops would rise to meet the camera and hold it hovering
        private double HandoffKm()
        {
            if (_ground == null || !_ground.Solid) return 2e-9;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); var gm = r > 0 ? (_underSea ? GrainField.For("ocean") ?? _ground.GrainMix(y / r) : _ground.GrainMix(y / r)) : null;   // (under the sea: the bed's own grains)
            return Math.Max(gm != null && gm.Size > 0 ? Math.Max(2e-9, gm.Size * 0.3) : 2e-9, _ground.ReadyStep < 1e30 ? _ground.ReadyStep * 1.6 : 0);   // (and never later than the drawn pieces' own cell: their flat faces are the floor until then)
        }
        private double _agX = double.NaN, _agY, _agZ, _agCell, _agVal; private object _agGround;
        // for the log: the camera's height above the bare rock (the ground without its grains, at a millimetre cell), beside its height above the grains
        private string BaseHeightWords()
        {
            if (_ground == null) return "-";
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return "-";
            double h = r - _ground.Radius - (_ground.Solid ? _ground.GroundAt(x / r, y / r, z / r, 1e-6) : 0);
            return (h < 0 ? "-" : "") + ScaleHud.Words(Math.Abs(h));
        }
        private double AboveGround()
        {
            if (_ground == null) return double.MaxValue;
            _surfaceCell = Math.Max(1e-11, _lastAbove / Units.Km(1) / 30);                   // (from the height last asked: a thirtieth of it)
            double x, y, z; CamFromPlanet(out x, out y, out z);
            if (x == _agX && y == _agY && z == _agZ && _surfaceCell == _agCell && _agGround == _ground) return _agVal;   // (asked dozens of times a frame, and low down each answer is the ground worked out at a fine cell: once for a place)
            double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) return 0;
            _lastAbove = Math.Max(1e-9, (r - _ground.Radius - (_ground.Solid ? Surface(x / r, y / r, z / r, r - _ground.Radius) : -_ground.Radius * (1 - CoreShare)))) * Units.Km(1);   // a giant: down through its clouds and layers to its core
            _agX = x; _agY = y; _agZ = z; _agCell = _surfaceCell; _agGround = _ground; _agVal = _lastAbove; return _lastAbove;
        }

        // the system's planets, moons and star placed around the star (anchor), and the nearest surface to the camera
        private double PlaceSystem()
        {
            if (_system == null) return double.MaxValue;
            double fx, fy, fz;
            _bodyCount = SpaceGen.BodyVerts(_system, _simYears, 1 / 20.0, 0.05, _bodyVerts, -1, 1.0, out fx, out fy, out fz);
            double best = double.MaxValue;
            for (int i = 0; i < _bodyCount; i++)
            {
                double r = i == 0 ? StarRadiusNow : _system.Bodies[i - 1].RadiusUnits;
                double dx = _bodyVerts[i].X - _ox, dy = _bodyVerts[i].Y - _oy, dz = _bodyVerts[i].Z - _oz;
                best = Math.Min(best, Math.Max(r * 0.05, Math.Sqrt(dx * dx + dy * dy + dz * dz) - r));
            }
            return best;
        }

        private static void ToGalaxy(Galaxy g, double ux, double uy, double uz, out double x, out double y, out double z)
        {
            double px = ux - g.X, py = uy - g.Y, pz = uz - g.Z;
            double ct = Math.Cos(g.Tilt), st = Math.Sin(g.Tilt), cu = Math.Cos(g.Turn), su = Math.Sin(g.Turn);
            double x1 = px * cu + pz * su, z1 = -px * su + pz * cu;                  // undo the turn
            double y2 = py * ct + z1 * st, z2 = -py * st + z1 * ct;                  // undo the tilt
            x = x1 / g.Scale; y = y2 / g.Scale; z = z2 / g.Scale;
        }

        /// <summary>Test runs: the camera turns to the next thing on the way down (the galaxy, a star, a planet, its ground) each frame.</summary>
        public bool AutoPath, AutoNebula;

    }
}
