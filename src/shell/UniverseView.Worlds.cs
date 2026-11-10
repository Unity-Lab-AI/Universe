// Universe · shell · the universe view, its part: worlds (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── rings: round any world whose template gives it them (content/terrain.unt rings-chance and its rings), made once
        // for each world of the system you are in, in the world's own radii, placed and scaled to it where it is now ──
        private readonly Dictionary<int, UploadBuffer> _ringBufs = new Dictionary<int, UploadBuffer>(); private readonly Dictionary<int, int> _ringCounts = new Dictionary<int, int>(); private object _ringSystem;
        private void DrawRings(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            if (_system == null) return;
            if (_ringSystem != _system) { foreach (var b0 in _ringBufs.Values) b0.Dispose(); _ringBufs.Clear(); _ringCounts.Clear(); _ringSystem = _system; }
            for (int i = 0; i < _system.Bodies.Count && i + 1 < _bodyCount; i++)
            {
                var b = _system.Bodies[i]; if (!b.Rings || b.Terrain == null) continue;
                double dx = _bodyVerts[i + 1].X, dy = _bodyVerts[i + 1].Y, dz = _bodyVerts[i + 1].Z, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);   // (already from the camera: drawn after the system has placed its bodies)
                if (dl <= 0 || b.RadiusUnits / dl * h < 3) continue;                       // (too small on screen to show)
                UploadBuffer buf;
                if (!_ringBufs.TryGetValue(i, out buf))
                {
                    string nm; var rings = PlanetGen.Build(_content, b.Terrain, WorldSeed(i), out nm);
                    float[] col = PlanetGen.RingColour(_content, b.Terrain);
                    var v = new StarVertex[Math.Max(1, rings.Points.Count)];
                    for (int q = 0; q < rings.Points.Count; q++) { var p = rings.Points[q]; float s = rings.Shade[q] * 2; v[q] = new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0.012f, R = col[0] * s, G = col[1] * s, B = col[2] * s, A = 1 }; }
                    buf = new UploadBuffer(_gpu.Device, (long)v.Length * Stride); buf.Write(v, rings.Points.Count);
                    _ringBufs[i] = buf; _ringCounts[i] = rings.Points.Count;
                }
                if (_ringCounts[i] == 0) continue;
                float sc = (float)b.RadiusUnits; var m = Mat4.Identity; m.M11 = sc; m.M22 = sc; m.M33 = sc; m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                DrawPoints(buf, _ringCounts[i], m * vp, right, up, 1f, 0.0008f, w, h, time, 0f, 1f, 0f);
            }
        }
        // a small body's spin: it turns about its star line, except while you are on it (a dive holds it, so the ground under you is
        // the stone that is drawn and does not sweep away at a kilometre a second)
        // a small body's turn now (radians): its own day on the universe's clock, the same clock its ground's day runs on (the stone
        // had spun on the screen's clock at a fixed rate, its ground on the universe's at eight hours, and neither from its kind)
        private double BodySpin(Extra ex, double time) { return double.IsNaN(ex.SpinHold) ? 2 * Math.PI * ((_simYears * 8766.0 / Math.Max(1, ex.DayH > 0 ? ex.DayH : 8)) % 1) + ex.Turn : ex.SpinHold; }
        private double _rockDayLo = -1, _rockDayHi;
        private double RockDayHours(double x, double y, double z)
        {
            if (_rockDayLo < 0) { _rockDayLo = 6; _rockDayHi = 30; foreach (var e in _content.OfKind("terrain.unt", "terrain")) if (e.Id == "asteroid") { double lo, hi; if (e.TryRange("day-hours", out lo, out hi)) { _rockDayLo = lo; _rockDayHi = hi; } } }
            double u = Math.Abs(Math.Sin(x * 5.9 + y * 23.3 + z * 71.7) * 7919.311) % 1;
            return _rockDayLo + (_rockDayHi - _rockDayLo) * u;
        }
        // the body's real surface along a direction from its middle (unit, in the universe's frame): the mesh's own shape in the body's
        // frame at this moment, so a dive lands on the stone that is drawn, not on a sphere kilometres inside or above it (a rock is
        // half again as long as it is wide, with bumps and craters besides: "a metre above it" had been nothing but stars)
        // which of the rock shapes a body has: its band by its size against the size gravity rounds a body at (its place's, else
        // 300 km), its seed its own; at or over that size it is the round shape
        private int RockIx(Extra ex)
        {
            double roundKm = ex.Pl != null && ex.Pl.RoundKm > 0 ? ex.Pl.RoundKm : 300, share = ex.R / Units.Km(1) / roundKm;
            int band = share < 0.08 ? 0 : share < 0.3 ? 1 : share < 0.65 ? 2 : 3;
            return band * RockSeeds + ((ex.ShapeIx % RockSeeds) + RockSeeds) % RockSeeds;
        }
        private bool RockRound(Extra ex) { double roundKm = ex.Pl != null && ex.Pl.RoundKm > 0 ? ex.Pl.RoundKm : 300; return ex.R / Units.Km(1) >= roundKm; }
        private double BodyRadiusToward(Extra ex, double time, double dx, double dy, double dz)
        {
            if (_rockSolid == null) MakeSmallBodies();
            double bx = RelX(ex), by = RelY(ex), bz = RelZ(ex), bl = Math.Sqrt(bx * bx + by * by + bz * bz);
            var xs = bl > 0 ? new Vec3((float)(-bx / bl), (float)(-by / bl), (float)(-bz / bl)) : new Vec3(1, 0, 0);
            var ys = Vec3.Cross(xs, Math.Abs(xs.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var zs = Vec3.Cross(xs, ys).Normalized;
            double spin = BodySpin(ex, time); float c = (float)Math.Cos(spin), sn = (float)Math.Sin(spin);
            var y2 = new Vec3(ys.X * c + zs.X * sn, ys.Y * c + zs.Y * sn, ys.Z * c + zs.Z * sn); var z2 = Vec3.Cross(xs, y2);
            if (ex.Shape == "comet") { y2 = ys; z2 = zs; }
            double lx = dx * xs.X + dy * xs.Y + dz * xs.Z, ly = dx * y2.X + dy * y2.Y + dz * y2.Z, lz = dx * z2.X + dy * z2.Y + dz * z2.Z, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); if (ll <= 0) return ex.R;
            bool comet = ex.Shape == "comet", round = !comet && RockRound(ex);
            var shape = comet ? _nucShape : round ? _roundShape : _rockShapes != null ? _rockShapes[RockIx(ex)] : _rockShape; if (shape == null) return ex.R;
            var p = shape(new[] { lx / ll, ly / ll, lz / ll });
            return ex.R * Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
        }
        // a small body's place against the anchor (the star you are at), precise: its star's offset from the anchor (nothing, in its
        // own system) and its own precise offset from its star. Its absolute coordinates are doubles at billions of light-years,
        // where a double's step is about a million kilometres: taken against the anchor they snapped every asteroid to a
        // million-kilometre grid, the rows of bodies set out in straight lines seen in every belt
        private double RelX(Extra ex) { return (ex.OrbCx - _ax) + ex.OffX; }
        private double RelY(Extra ex) { return (ex.OrbCy - _ay) + ex.OffY; }
        private double RelZ(Extra ex) { return (ex.OrbCz - _az) + ex.OffZ; }
        private void DrawSmallBodies(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 || !ShapeShown(ex, h)) continue;
                if (_ground != null && _planet == -2 && ex == _groundExtra) continue;   // (its ground is made and drawn as a world's: the mesh would be a second stone over it)
                if (_rockSolid == null) MakeSmallBodies();
                double bx = RelX(ex), by = RelY(ex), bz = RelZ(ex), dx = bx - _ox, dy = by - _oy, dz = bz - _oz, bl = Math.Sqrt(bx * bx + by * by + bz * bz);
                // its frame: +x toward its star (the anchor of the system you are in), spinning about that line
                var xs = bl > 0 ? new Vec3((float)(-bx / bl), (float)(-by / bl), (float)(-bz / bl)) : new Vec3(1, 0, 0);
                var ys = Vec3.Cross(xs, Math.Abs(xs.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var zs = Vec3.Cross(xs, ys).Normalized;
                double spin = BodySpin(ex, time); float c = (float)Math.Cos(spin), sn = (float)Math.Sin(spin);   // (held still while you are on it)
                var y2 = new Vec3(ys.X * c + zs.X * sn, ys.Y * c + zs.Y * sn, ys.Z * c + zs.Z * sn); var z2 = Vec3.Cross(xs, y2);
                if (ex.Shape == "comet") { y2 = ys; z2 = zs; }                                  // (its tails do not spin)
                float sc = (float)ex.R;
                var m = Mat4.Identity;
                m.M11 = xs.X * sc; m.M12 = xs.Y * sc; m.M13 = xs.Z * sc;
                m.M21 = y2.X * sc; m.M22 = y2.Y * sc; m.M23 = y2.Z * sc;
                m.M31 = z2.X * sc; m.M32 = z2.Y * sc; m.M33 = z2.Z * sc;
                m.M41 = (float)dx; m.M42 = (float)dy; m.M43 = (float)dz;
                // a comet's coma first: thin gas, bright from afar, only a faint haze once you are inside it (its points stacked
                // into a white blaze that hid the nucleus); its dark nucleus is drawn over it
                if (ex.Shape == "comet")
                {
                    // the coma and the tails, each scaled to its own real size round the nucleus, the same frame (the tails straight
                    // away from the star); the coma thins to a faint haze as you come into it
                    double dc = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Func<double, Mat4> sized = km => { float k = (float)Units.Km(km); var mm = m; mm.M11 = xs.X * k; mm.M12 = xs.Y * k; mm.M13 = xs.Z * k; mm.M21 = y2.X * k; mm.M22 = y2.Y * k; mm.M23 = y2.Z * k; mm.M31 = z2.X * k; mm.M32 = z2.Y * k; mm.M33 = z2.Z * k; return mm; };
                    float inC = (float)Math.Max(0.06, Math.Min(1, Math.Sqrt(dc / Math.Max(1e-30, Units.Km(ex.ComaKm) * 1.5))));
                    // (the smallest a point may be is a share of the view's distance, in the points' own units: divided by their scale)
                    DrawPoints(_cometBuf, _comaN, sized(ex.ComaKm) * vp, right, up, 1f, 0.0008f / (float)Units.Km(ex.ComaKm), w, h, time, 0f, inC, 0f);
                    DrawPoints(_tailBuf, _tailN, sized(ex.TailKm) * vp, right, up, 1f, 0.0008f / (float)Units.Km(ex.TailKm), w, h, time, 0f, 1f, 0f);
                }
                // the body itself, solid stone lit from its star (its camera's place in its own frame, for which faces show)
                {
                    var mvp = m * vp; var mc = new float[BillboardPipeline.ConstantCount];
                    mc[0] = mvp.M11; mc[1] = mvp.M12; mc[2] = mvp.M13; mc[3] = mvp.M14; mc[4] = mvp.M21; mc[5] = mvp.M22; mc[6] = mvp.M23; mc[7] = mvp.M24;
                    mc[8] = mvp.M31; mc[9] = mvp.M32; mc[10] = mvp.M33; mc[11] = mvp.M34; mc[12] = mvp.M41; mc[13] = mvp.M42; mc[14] = mvp.M43; mc[15] = mvp.M44;
                    mc[16] = 1; mc[17] = 0; mc[18] = 0;                                           // its star along its own +x
                    double ix = -dx, iy = -dy, iz = -dz;                                           // the camera from the body, into its frame
                    mc[20] = (float)((ix * xs.X + iy * xs.Y + iz * xs.Z) / sc); mc[21] = (float)((ix * y2.X + iy * y2.Y + iz * y2.Z) / sc); mc[22] = (float)((ix * z2.X + iy * z2.Y + iz * z2.Z) / sc);
                    bool comet = ex.Shape == "comet", round = !comet && RockRound(ex);
                    _gpu.ClearDepth();
                    _meshes.Solid.Draw(comet ? _nucSolid : round ? _roundSolid : _rockSolids[RockIx(ex)], comet ? _nucSolidN : round ? _roundSolidN : _rockSolidNs[RockIx(ex)], mc, 0, 0, w, h);
                }

            }
        }
        private UploadBuffer _extraBuf; private StarVertex[] _extraV = new StarVertex[0]; private readonly float[] _lens = new float[4], _web = new float[5];
        private void LoadPlaces()
        {
            _places = new List<Place>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            foreach (var e in _content.OfKind("bodies.unt", "place"))
            {
                double v; var pl = new Place { Id = e.Id, Name = e.Name, Where = e.Get("where", ""), Entry = e.Get("entry", ""), Says = e.Get("says", ""), Shape = e.Get("shape", "") };
                { double lo, hi; if (e.TryRange("coma-km", out lo, out hi)) { pl.ComaLo = lo; pl.ComaHi = hi; } if (e.TryRange("tail-km", out lo, out hi)) { pl.TailLo = lo; pl.TailHi = hi; } }   // (a comet's coma and tail at their real sizes)
                if (double.TryParse(e.Get("per-cubic-ly", "0"), fs, ci, out v)) pl.PerLy3 = v; if (double.TryParse(e.Get("feeding-share", "0"), fs, ci, out v)) pl.FeedShare = v; if (double.TryParse(e.Get("jets-share", "0"), fs, ci, out v)) pl.JetShare = v; pl.SizeKm = 1000;
                { double lo, hi; if (e.TryRange("size-km", out lo, out hi)) { pl.SizeKm = lo; pl.SizeLo = lo; pl.SizeHi = hi; } if (double.TryParse(e.Get("size-power", "1"), fs, ci, out v)) pl.SizePow = Math.Max(0.1, v); if (double.TryParse(e.Get("round-above-km", "0"), fs, ci, out v)) pl.RoundKm = v; }   // (a size range: each body its own size between them, by the power; round-above-km: the size gravity has pulled a body round at)
                int n; if (int.TryParse(e.Get("shown", "0"), out n)) pl.Shown = n;
                if (double.TryParse(e.Get("feeding-eddington", "0.1"), fs, ci, out v) && v > 0) pl.FeedEdd = v;   // (its feeding rate, a share of the Eddington rate: with its mass, the disc's heat)
                { double lo, hi; if (e.TryRange("disc-rs", out lo, out hi)) { pl.DiscLo = Math.Max(1.5, lo); pl.DiscHi = Math.Max(pl.DiscLo + 1, hi); } if (e.TryRange("swarm-rs", out lo, out hi)) { pl.SwarmLo = Math.Max(2, lo); pl.SwarmHi = Math.Max(pl.SwarmLo + 1, hi); } }
                if (int.TryParse(e.Get("swarm-stars", "0"), out n)) pl.Swarm = n; { double lo, hi; if (e.TryRange("eating-stars", out lo, out hi)) { pl.EatLo = Math.Max(1, lo); pl.EatHi = Math.Max(pl.EatLo, hi); } } if (double.TryParse(e.Get("eating-share", "0"), fs, ci, out v)) pl.EatShare = v; if (double.TryParse(e.Get("inner-orbit-seconds", "8"), fs, ci, out v)) pl.OrbitSeconds = Math.Max(0.5, v);
                var c = e.Get("colour", "1 1 1").Split(' '); float r = 1, g = 1, b = 1; if (c.Length == 3) { float.TryParse(c[0], fs, ci, out r); float.TryParse(c[1], fs, ci, out g); float.TryParse(c[2], fs, ci, out b); }
                pl.R = r; pl.G = g; pl.B = b; _places.Add(pl);
            }
        }
        // a small body's size (km) from its place's range, by a power law on its position's seed: many small, few large (a belt is
        // mostly boulders, its largest a few hundred km across); a place with one size keeps it (a rock with the old spread round it)
        private static double SizeOf(Place pl, double x, double y, double z)
        {
            double su = Math.Abs(Math.Sin(x * 3.1 + y * 5.7 + z * 1.9) * 7919.1) % 1;
            if (pl.SizeHi > pl.SizeLo && pl.SizeLo > 0) return Math.Exp(Math.Log(pl.SizeLo) + Math.Pow(su, pl.SizePow) * (Math.Log(pl.SizeHi) - Math.Log(pl.SizeLo)));
            return pl.SizeKm * (pl.Shape == "rock" ? Math.Exp(-1.6 + 3.2 * Math.Pow(su, 2.5)) : 1);
        }
        private Extra MakeExtra(Place pl, double x, double y, double z)
        {
            var ex = new Extra { X = x, Y = y, Z = z, R = Units.Km(SizeOf(pl, x, y, z)), ShapeIx = (int)(Math.Abs(Math.Sin(x * 9.3 + y * 2.9 + z * 7.7) * 3313.7) % 1 * RockSeeds), Name = pl.Name, Shape = pl.Shape ?? "", Turn = (float)(Math.Abs(Math.Sin(x * 7.1 + z * 3.3) * 9187.3) % (Math.PI * 2)), Cr = pl.R, Cg = pl.G, Cb = pl.B, Lines = new List<string>(), Pl = pl };
            ex.DayH = RockDayHours(x, y, z);                                             // its own day, one turn of its spin (content/terrain.unt @terrain asteroid day-hours), the one clock its stone, its ground and its light keep
            ex.OrbCx = _ax; ex.OrbCy = _ay; ex.OrbCz = _az; ex.OffX = x - _ax; ex.OffY = y - _ay; ex.OffZ = z - _az;   // (its place from the anchor; a body on an orbit gets its precise offset below)
            ex.Lines.Add(pl.Name);
            {
                // its id, as a star's: a code for its kind, then six digits from where it is (the same body, the same id, every time)
                string kid = pl.Id.Contains("comet") ? "CM" : pl.Id.Contains("asteroid") ? "AS" : pl.Id.Contains("neutron") || pl.Id.Contains("pulsar") ? "NS" : pl.Id.Contains("rogue") ? "RP" : pl.Id.Contains("hole") || pl.Id.Contains("black") ? "BH" : pl.Id.Contains("kuiper") ? "KO" : pl.Id.Contains("intergalactic") ? "IS" : "B";
                long hid = ((long)(x / Units.Km(1e6)) * 73856093L ^ (long)(y / Units.Km(1e6)) * 19349663L ^ (long)(z / Units.Km(1e6)) * 83492791L) & 0xFFFFFF;
                ex.Lines.Add("id: " + kid + "-" + hid.ToString("X6"));
            }
            if (pl.Says.Length > 0) ex.Lines.Add(Short(pl.Says, 70));                        // (a short line: the card is a card, not a page)
            if (pl.Shape == "comet")
            {
                // its coma and tail, each comet its own size in its kind's ranges (from where it is): real comets' comas a hundred
                // thousand km to over a million, their tails tens of millions of km
                double uc = Math.Abs(Math.Sin(x * 1.7 + y * 2.9 + z * 4.3) * 4321.7) % 1, ut = Math.Abs(Math.Sin(x * 3.3 + y * 1.1 + z * 2.7) * 8765.3) % 1;
                ex.ComaKm = pl.ComaLo > 0 ? pl.ComaLo + (pl.ComaHi - pl.ComaLo) * uc : 1e4; ex.TailKm = pl.TailLo > 0 ? pl.TailLo + (pl.TailHi - pl.TailLo) * ut : 3e5;
                ex.Lines.Add("coma about " + ex.ComaKm.ToString("N0") + " km across, tail about " + (ex.TailKm / 1e6).ToString("0.#") + " million km");
            }
            // what it is made of: an asteroid of the belt draws its kind (content/small-bodies.unt @asteroid) by the kinds' belt-share at
            // its place in the belt (stony inside, carbon-rich outside, metal and basalt rare, dark icy ones at the outer edge); any
            // other body is its entry's kind; each kind's mix of minerals, its make-up, its density (so its mass) and its albedo go on
            // the card, with the elements the minerals hold down to the traces, and its matter is dived into at its surface
            {
                ContentEntry made = null;
                if (pl.Where == "belt" && _system != null && _system.BeltOutAu > _system.BeltInAu)
                {
                    double rr = Math.Sqrt((x - _ax) * (x - _ax) + (y - _ay) * (y - _ay) + (z - _az) * (z - _az)) / Units.Au(1), t = Math.Max(0, Math.Min(1, (rr - _system.BeltInAu) / (_system.BeltOutAu - _system.BeltInAu)));
                    double u = Math.Abs(Math.Sin(x * 5.3 + y * 17.9 + z * 7.7) * 31415.9265) % 1, sum = 0; var cands = new List<KeyValuePair<ContentEntry, double>>();
                    foreach (var e in _content.OfKind("small-bodies.unt", "asteroid")) { double lo, hi; if (!e.TryRange("belt-share", out lo, out hi)) continue; double wgt = lo + (hi - lo) * t; if (wgt <= 0) continue; cands.Add(new KeyValuePair<ContentEntry, double>(e, wgt)); sum += wgt; }
                    double pick = u * sum; foreach (var c in cands) { pick -= c.Value; if (pick <= 0) { made = c.Key; break; } }
                    if (made == null && cands.Count > 0) made = cands[cands.Count - 1].Key;
                }
                if (made == null) { var en0 = pl.Entry.Split(' '); if (en0.Length == 2) foreach (var e in _content.Entries) if (e.Kind == en0[0] && e.Id == en0[1] && e.Props.ContainsKey("mix")) { made = e; break; } }
                if (made == null && pl.Shape == "rock")
                {
                    // a kind by the kinds' shares at mid-belt, from where the body is: a rock always has a kind, its minerals and a ground
                    double u2 = Math.Abs(Math.Sin(x * 3.7 + y * 11.3 + z * 5.9) * 27182.8) % 1, sum2 = 0; var c2 = new List<KeyValuePair<ContentEntry, double>>();
                    foreach (var e in _content.OfKind("small-bodies.unt", "asteroid")) { double lo, hi; if (!e.TryRange("belt-share", out lo, out hi) || !e.Props.ContainsKey("mix")) continue; double wgt = (lo + hi) / 2; if (wgt <= 0) continue; c2.Add(new KeyValuePair<ContentEntry, double>(e, wgt)); sum2 += wgt; }
                    double pick2 = u2 * sum2; foreach (var c in c2) { pick2 -= c.Value; if (pick2 <= 0) { made = c.Key; break; } }
                    if (made == null && c2.Count > 0) made = c2[c2.Count - 1].Key;
                }
                if (made != null) Compose(ex, made);
            }
            var en = pl.Entry.Split(' ');
            if (en.Length == 2) foreach (var e in _content.Entries) if (e.Kind == en[0] && e.Id == en[1])
            {
                if (e.Props.ContainsKey("mass-sol"))
                {
                    // its own mass, from the range its kind has (spread evenly in scale), from its place
                    var rr = e.Get("mass-sol", "").Split(new[] { ".." }, StringSplitOptions.None); double lo, hi; var ci2 = System.Globalization.CultureInfo.InvariantCulture;
                    if (rr.Length >= 1 && double.TryParse(rr[0], System.Globalization.NumberStyles.Float, ci2, out lo))
                    {
                        if (rr.Length < 2 || !double.TryParse(rr[1], System.Globalization.NumberStyles.Float, ci2, out hi)) hi = lo * 10;
                        double u = Math.Abs(Math.Sin(x * 12.9898 + y * 78.233 + z * 37.719) * 43758.5453) % 1;
                        ex.Mass = Math.Exp(Math.Log(lo) + (Math.Log(hi) - Math.Log(lo)) * u);
                    }
                    ex.Lines.Add("mass: " + (ex.Mass > 0 ? ex.Mass.ToString(ex.Mass >= 1e4 ? "0.##E+0" : "0.#") : e.Get("mass-sol", "")) + " solar masses");
                }
                if (e.Kind == "star" && e.Id == "neutron-star" && ex.Mass > 0)
                {
                    // a neutron star is an atom's nucleus the size of a city: its mass in a ball about 11 km in radius, as dense as
                    // nuclear matter (about 2.3e17 kg a cubic metre), made almost all of neutrons pressed together (size-km is the radius,
                    // as every body's size is: the same number the body is drawn at)
                    double rm = pl.SizeKm * 1000, kg = ex.Mass * 1.989e30, rho = kg / (4.0 / 3 * Math.PI * rm * rm * rm);
                    ex.Lines.Add("density: " + rho.ToString("0.#E+0") + " kg a cubic metre, " + (rho / 2.3e17).ToString("0.#") + " times an atom's nucleus");
                    ex.Lines.Add("made of: neutrons pressed together, as in a nucleus; a teaspoon would weigh " + (rho * 5e-6 / 1e12).ToString("0.#") + " billion tonnes");
                }
                if (e.Kind == "blackhole" && ex.Mass > 0)
                {
                    // a black hole is its mass: its event horizon the Schwarzschild radius 2GM/c^2 (2.95 km a solar mass), the shadow round it
                    // about 2.6 times that, the photon ring where light circles it
                    ex.Hole = true; double rs = 2.953 * ex.Mass; ex.R = Units.Km(rs * 2.6);
                    ex.Lines.Add("event horizon: " + ScaleHud.Words(rs * 2) + " across");
                    ex.Lines.Add("shadow: " + ScaleHud.Words(rs * 5.2) + " across");
                    // feeding or quiet, by its kind's shares (content/bodies.unt), from its place
                    double u2 = Math.Abs(Math.Sin(x * 3.7 + y * 91.1 + z * 13.3) * 24634.6345) % 1, u3 = Math.Abs(Math.Sin(x * 17.9 + y * 5.3 + z * 47.1) * 15731.743) % 1;
                    ex.Feeding = u2 < pl.FeedShare ? (u3 < pl.JetShare ? 2 : 1) : 0; ex.Pl = pl;
                    { double u4 = Math.Abs(Math.Sin(x * 7.3 + y * 29.7 + z * 61.1) * 9173.513) % 1, u5 = Math.Abs(Math.Sin(x * 11.3 + y * 3.7 + z * 41.9) * 6571.771) % 1; ex.Eating = ex.Feeding > 0 && u4 < pl.EatShare; ex.EatN = ex.Eating ? (int)Math.Round(pl.EatLo + (pl.EatHi - pl.EatLo) * u5) : 0; }   // (a star being torn apart and eaten now, by its kind's share, from its place; a supermassive one may have several in its disc at once)
                    ex.Tilt = (float)(u3 * Math.PI); ex.Turn = (float)(u2 * 37 % (Math.PI * 2));
                    {
                        // its spin, from its place (content/physics.unt @law frame-dragging): its horizon pulled in, its ergosphere, space
                        // swept round at its horizon, and how far in its disc reaches (the innermost stable orbit, by the spin)
                        double u6 = Math.Abs(Math.Sin(x * 19.3 + y * 7.7 + z * 23.9) * 4391.317) % 1; ex.Spin = _spinLo + u6 * (_spinHi - _spinLo);
                        double rsM = rs * 1000, M = rsM / 2, rp = M * (1 + Math.Sqrt(Math.Max(0, 1 - ex.Spin * ex.Spin))), omega = ex.Spin * Physics.C / (2 * rp);
                        ex.Lines.Add("spin " + ex.Spin.ToString("0.00") + " of the most; horizon " + ScaleHud.Words(rp * 2 / 1000) + " across; ergosphere " + ScaleHud.Words(rs * 2) + " at the equator");
                        ex.Lines.Add("frame dragging: space at the horizon round " + TurnsWords(omega / (2 * Math.PI)) + "; disc's inner edge " + IscoRs(ex.Spin).ToString("0.##") + " horizon radii");
                    }
                    ex.Lines.Add(ex.Feeding == 0 ? "quiet: not feeding, only its shadow and the light it bends" : ex.Feeding == 1 ? "feeding: a disc of gas spiralling in, heated to millions of degrees" : "feeding, and firing jets from its poles at nearly the speed of light");
                    if (ex.Eating) ex.Lines.Add(ex.EatN > 1 ? "stars being eaten now: " + ex.EatN + ", each stretched along its orbit into a stream of gas winding into the disc" : "a star being eaten now: stretched along its orbit into a stream of gas winding into the disc");
                    { double gm = Physics.GmSol * ex.Mass, rsM = rs * 1000; ex.Lines.Add("escape speed: light's at the horizon; the fall from there to the middle: " + Secs(2.0 / 3 * Math.Pow(rsM, 1.5) / Math.Sqrt(2 * gm)) + " of your own time"); }
                }
                if (e.Props.ContainsKey("made-by")) ex.Lines.Add("made by: " + e.Get("made-by", ""));
                if (e.Props.ContainsKey("example")) ex.Lines.Add("for example: " + e.Get("example", ""));
                break;
            }
            return ex;
        }
        // the bodies round you, made again when where you are changes enough (each always the same, from its own place)
        private void UpdateExtras()
        {
            if (_places == null) LoadPlaces();
            if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count && _tExtra != _extras[_tIndex]) _tExtra = _extras[_tIndex];   // (a fresh pick: its body held, before the list may change)
            bool inGalaxy = _near >= 0 && NearShare() > 0.5, between = !inGalaxy && (_near < 0 || NearShare() < 0.05);
            double lyU = 1.0 / Units.LightYearsPerUnit;
            string key = (inGalaxy ? "g" : between ? "b" : "-") + (_system != null ? "s" + _system.StarName : "") + ":" + (long)Math.Floor(_cx / 0.02) + "," + (long)Math.Floor(_cy / 0.02) + "," + (long)Math.Floor(_cz / 0.02) + ":" + _galaxies.Count;
            // every body on an orbit moves on it every frame (the list itself is remade only when you move to a new cell); the one
            // you are at moves you with it, so you stay beside it, or on its ground, as it goes round (at a minute a second an
            // asteroid at three AU would leave you in half a second)
            if (_tKind != 6 || _tExtra == null) _tSpot = null;
            // the one body the camera rides with: the body picked, else the body whose ground you are on (never both: were they two
            // objects for one body the camera would move twice its step a frame and run off it)
            var rider = _tExtra != null && _tExtra.Orbits && _extras.Contains(_tExtra) ? _tExtra : _groundExtra;
            foreach (var ex in _extras) { if (!double.IsNaN(ex.SpinHold) && ((ex != _tExtra && ex != _groundExtra) || _spaceScale <= 0)) ex.SpinHold = double.NaN; if (ex.Orbits) { Orbit(ex); if (ex == rider) { _ox += ex.OffX - ex.LOffX; _oy += ex.OffY - ex.LOffY; _oz += ex.OffZ - ex.LOffZ; } } }   // (a held spin let go once you are off the body)
            // the small body's ground has its centre where the body is NOW, after it has moved on its orbit and you with it: taken
            // once a frame before the move, the ground was drawn one orbital step behind the stone (at a minute a second tens of km,
            // and a different step every frame), so it flapped about by tens of km, the height above it jumped, and the zoom down to
            // it chased a point that was never there and froze
            if (_planet == -2 && _groundExtra != null) { _px = RelX(_groundExtra); _py = RelY(_groundExtra); _pz = RelZ(_groundExtra); }
            if (key == _extraKey) return; _extraKey = key; _extras.Clear();
            // the compact remnants of this galaxy's past supernovae: a neutron star (from a star of eight to about twenty solar masses) or a
            // stellar black hole (heavier) born at each one's middle, from the blast; the ones near you, picked like any body
            if (inGalaxy && _near >= 0 && _near < _galaxies.Count)
            {
                var g = _galaxies[_near]; var c = Supernovae(g, true); Place pN = null, pB = null;
                foreach (var pl in _places) { if (pl.Id == "neutron-star") pN = pl; if (pl.Id == "stellar-black-hole") pB = pl; }
                double reach = g.Scale * 0.15; int made = 0;
                foreach (var sn in c.Events)
                {
                    double years = _simYears - sn.T; if (sn.K.Leaves == "nothing" || years < 0 || made >= 64) continue;   // (from the blast: it is born in the collapse, inside the fireball until the ejecta thin)
                    double dx = sn.Ux - _cx, dy = sn.Uy - _cy, dz = sn.Uz - _cz; if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
                    bool mag = sn.K.Leaves == "magnetar"; var pl = mag || sn.Mass < _snNsMax ? pN : pB; if (pl == null) continue;
                    var ex = MakeExtra(pl, sn.Ux, sn.Uy, sn.Uz); if (mag) { ex.Lines[0] = "A magnetar"; ex.Name = "A magnetar"; }
                    ex.Lines.Insert(2, "born in the " + sn.K.Name.ToLowerInvariant() + " here " + Years(years) + " ago, from a star of " + sn.Mass.ToString("0") + " solar masses" + (mag ? "; its magnetic field a thousand times a pulsar's" : "") + (mag || sn.Mass < _snNsMax ? "; spinning about " + Math.Min(700, 30 * Math.Sqrt(970 / Math.Max(1, years))).ToString("0") + " times a second (the youngest spin fastest, slowing as they age)" : "") + "; its shell now " + SnShellLy(sn, years).ToString("0.#") + " light-years out, moving at " + SnShellKmS(sn, years).ToString("N0") + " km/s");
                    _extras.Add(ex); made++;
                }
            }
            foreach (var pl in _places)
            {
                if (pl.Where == "galaxy-heart")
                {
                    foreach (var g in _galaxies) { double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz; if (dx * dx + dy * dy + dz * dz < Math.Pow(60 * g.Scale, 2)) _extras.Add(MakeExtra(pl, g.X, g.Y, g.Z)); }
                }
                else if ((pl.Where == "galaxy" && inGalaxy) || (pl.Where == "between-galaxies" && between))
                {
                    if (pl.PerLy3 <= 0) continue;
                    // about two dozen of each round you: the reach that holds them at this density, in cells each from its own place
                    double reachLy = Math.Pow(24 / (4.0 / 3 * Math.PI * pl.PerLy3), 1 / 3.0), cellLy = reachLy / 2, cell = cellLy * lyU;
                    long ci0 = (long)Math.Floor(_cx / cell), cj0 = (long)Math.Floor(_cy / cell), ck0 = (long)Math.Floor(_cz / cell);
                    for (long i = ci0 - 2; i <= ci0 + 2; i++) for (long j = cj0 - 2; j <= cj0 + 2; j++) for (long k = ck0 - 2; k <= ck0 + 2; k++)
                    {
                        var rng = new Rng((ulong)(i * 73856093L ^ j * 19349663L ^ k * 83492791L) ^ _seed ^ (ulong)pl.Id.GetHashCode());
                        double lambda = pl.PerLy3 * cellLy * cellLy * cellLy; int n = (int)Math.Floor(lambda) + (rng.NextDouble() < lambda - Math.Floor(lambda) ? 1 : 0);
                        for (int q = 0; q < n && q < 8; q++) _extras.Add(MakeExtra(pl, (i + rng.NextDouble()) * cell, (j + rng.NextDouble()) * cell, (k + rng.NextDouble()) * cell));
                    }
                }
                else if (_system != null && (pl.Where == "belt" || pl.Where == "kuiper" || pl.Where == "oort" || pl.Where == "inner"))
                {
                    double a0 = pl.Where == "belt" ? _system.BeltInAu : pl.Where == "kuiper" ? _system.KuiperInAu : pl.Where == "inner" ? 0.8 : _system.OortInAu, a1 = pl.Where == "belt" ? _system.BeltOutAu : pl.Where == "kuiper" ? _system.KuiperOutAu : pl.Where == "inner" ? 3.0 : _system.OortOutAu;
                    if (a1 <= 0) continue;
                    var rng = new Rng(_seed ^ (ulong)pl.Id.GetHashCode() ^ (ulong)_system.StarName.GetHashCode());
                    for (int q = 0; q < pl.Shown; q++)
                    {
                        double au = a0 + (a1 - a0) * rng.NextDouble(), ang = rng.NextDouble() * Math.PI * 2, u = pl.Where == "oort" ? rng.NextDouble() * 2 - 1 : (rng.NextDouble() - 0.5) * 0.1, rr = Math.Sqrt(1 - u * u);
                        double d = Units.Km(au * Units.KmPerAu);
                        // on its own orbit round the star (Kepler's law: its period from its distance and the star's mass), made at its
                        // closest point at the clock's zero, so the same body is the same body every time (its id, its size and its
                        // make-up from that place); the asteroids and Kuiper bodies on near-circles, the comets on long ellipses that
                        // sweep in close and far out (they had all stood still, placed once and never moved)
                        var ex = MakeExtra(pl, _ax + Math.Cos(ang) * rr * d, _ay + u * d, _az + Math.Sin(ang) * rr * d);
                        double ecc = pl.Shape == "comet" ? 0.5 + 0.45 * rng.NextDouble() : pl.Where == "kuiper" ? 0.3 * rng.NextDouble() : 0.15 * rng.NextDouble();
                        ex.Orbits = true; ex.OrbE = ecc; ex.OrbQ = d; ex.OrbCx = _ax; ex.OrbCy = _ay; ex.OrbCz = _az;
                        ex.OrbPeriod = Math.Sqrt(Math.Pow(au / (1 - ecc), 3) / Math.Max(0.05, _system.MassSol));
                        { double hx = Math.Cos(ang) * rr, hy = u, hz = Math.Sin(ang) * rr; ex.OrbE1 = new[] { hx, hy, hz }; double sx = -hz, sz = hx, sl = Math.Sqrt(sx * sx + sz * sz); if (sl < 1e-9) { sx = 1; sz = 0; sl = 1; } ex.OrbE2 = new[] { sx / sl, 0, sz / sl }; }   // (e1: from the star to its closest point; e2: along the orbit from there, level with the system)
                        ex.Lines.Add("its orbit: closest " + au.ToString("0.##") + " AU, farthest " + (au * (1 + ecc) / (1 - ecc)).ToString("0.##") + " AU, round the star in " + Years(ex.OrbPeriod));
                        Orbit(ex); ex.LX = ex.X; ex.LY = ex.Y; ex.LZ = ex.Z; ex.LOffX = ex.OffX; ex.LOffY = ex.OffY; ex.LOffZ = ex.OffZ; _extras.Add(ex);
                    }
                }
            }
            // the body picked, found again in the new list by its place (the same body is made at the same place every time); if it
            // is not among them (you have moved on), it is kept in the list itself, so the pick, its card and its dive stay its own
            if (_tKind == 6 && _tExtra != null)
            {
                int found = -1;
                for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (ex == _tExtra || (Math.Abs(ex.X - _tExtra.X) <= 1e-9 * Math.Max(1, Math.Abs(ex.X)) && Math.Abs(ex.Y - _tExtra.Y) <= 1e-9 * Math.Max(1, Math.Abs(ex.Y)) && Math.Abs(ex.Z - _tExtra.Z) <= 1e-9 * Math.Max(1, Math.Abs(ex.Z)) && ex.Hole == _tExtra.Hole)) { found = q; break; } }
                if (found < 0) { _extras.Add(_tExtra); found = _extras.Count - 1; }
                _tIndex = found; _tExtra = _extras[found];
            }
            // the body whose ground you are on, in the list made afresh: the same body (its name and make-up, the nearest of them),
            // so its ground rides on with it; else the ground is let go by distance, as a planet's is. On every remake, picked or not
            // (it had run only while a body was picked, and with the pick cleared the ground was left behind as its body went on)
            if (_groundExtra != null && !_extras.Contains(_groundExtra))
            {
                Extra gb = null; double gd = double.MaxValue; double gx = RelX(_groundExtra), gy = RelY(_groundExtra), gz = RelZ(_groundExtra);
                foreach (var ex in _extras) { if (ex.Name != _groundExtra.Name || ex.Mix != _groundExtra.Mix) continue; double dx = RelX(ex) - gx, dy = RelY(ex) - gy, dz = RelZ(ex) - gz, d = dx * dx + dy * dy + dz * dz; if (d < gd) { gd = d; gb = ex; } }
                if (gb != null) _groundExtra = gb;
            }
        }

        // where a body on its orbit is now (Kepler's equation from its mean anomaly, as the planets), and where it was last frame;
        // with gravity switched off (content/physics.unt @law gravity-off) it carries straight on from where it was when it went off
        private void Orbit(Extra ex)
        {
            if (!ex.Orbits || ex.OrbPeriod <= 0) return;
            ex.LX = ex.X; ex.LY = ex.Y; ex.LZ = ex.Z; ex.LOffX = ex.OffX; ex.LOffY = ex.OffY; ex.LOffZ = ex.OffZ;
            double off = SpaceGen.GravityOffYear, t = _simYears, lx, ly, lz;
            if (!double.IsNaN(off) && t > off)
            {
                double x0, y0, z0, x1, y1, z1; OrbitAt(ex, off - 1e-4, out x0, out y0, out z0); OrbitAt(ex, off, out x1, out y1, out z1);
                lx = x1 + (x1 - x0) / 1e-4 * (t - off); ly = y1 + (y1 - y0) / 1e-4 * (t - off); lz = z1 + (z1 - z0) / 1e-4 * (t - off);
            }
            else OrbitAt(ex, t, out lx, out ly, out lz);
            ex.OffX = lx; ex.OffY = ly; ex.OffZ = lz; ex.X = ex.OrbCx + lx; ex.Y = ex.OrbCy + ly; ex.Z = ex.OrbCz + lz;   // (the precise offset kept beside the absolute place)
        }
        private static void OrbitAt(Extra ex, double t, out double x, out double y, out double z)
        {
            double e = ex.OrbE, M = (2 * Math.PI * t / ex.OrbPeriod) % (2 * Math.PI), E = M + e * Math.Sin(M);
            for (int it = 0; it < 12; it++) { double dE = (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E)); E -= dE; if (Math.Abs(dE) < 1e-12) break; }
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2)), a = ex.OrbQ / (1 - e), r = a * (1 - e * Math.Cos(E));
            double cn = Math.Cos(nu), sn = Math.Sin(nu);
            x = r * (cn * ex.OrbE1[0] + sn * ex.OrbE2[0]); y = r * (cn * ex.OrbE1[1] + sn * ex.OrbE2[1]); z = r * (cn * ex.OrbE1[2] + sn * ex.OrbE2[2]);   // (its offset from its star, precise)
        }

        private void LookAt(double x, double y, double z)
        {
            double dx = x - _cx, dy = y - _cy, dz = z - _cz, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l > 0) SetLook(dx, dy, dz);
        }
        private void SetLook(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z); if (l <= 0 || double.IsNaN(l)) return;
            _fx = x / l; _fy = y / l; _fz = z / l;
        }
        private void SetLook(Vec3 f) { SetLook(f.X, f.Y, f.Z); }
        // turns the view a share k of the way to a direction, round the shortest way (never through angles)
        private void EaseLook(double wx, double wy, double wz, double k)
        {
            double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz); if (wl <= 0) return; wx /= wl; wy /= wl; wz /= wl;
            var f = Look; double d = Math.Max(-1, Math.Min(1, f.X * wx + f.Y * wy + f.Z * wz)), a = Math.Acos(d);
            if (a < 1e-7) { SetLook(wx, wy, wz); return; }
            var axis = Vec3.Cross(f, new Vec3((float)wx, (float)wy, (float)wz)); if (axis.Length < 1e-6f) axis = _camUp;
            SetLook(Turn(f, axis.Normalized, a * k));
        }

        private Vec3 Look { get { return new Vec3((float)_fx, (float)_fy, (float)_fz); } }

        // a galaxy's own frame placed in the universe: its size, tilt and turn (as the universe always placed them)
        private static void ToUniverse(Galaxy g, double x, double y, double z, out double ux, out double uy, out double uz)
        {
            x *= g.Scale; y *= g.Scale; z *= g.Scale;
            double ct = Math.Cos(g.Tilt), st = Math.Sin(g.Tilt), cu = Math.Cos(g.Turn), su = Math.Sin(g.Turn);
            double y2 = y * ct - z * st, z2 = y * st + z * ct;
            ux = x * cu - z2 * su + g.X; uy = y2 + g.Y; uz = x * su + z2 * cu + g.Z;
        }

        // the same placing as a matrix, relative to the camera (the translation is the galaxy's centre minus the camera, in double first)
        private Mat4 Placement(Galaxy g)
        {
            float ct = (float)Math.Cos(g.Tilt), st = (float)Math.Sin(g.Tilt), cu = (float)Math.Cos(g.Turn), su = (float)Math.Sin(g.Turn), s = g.Scale;
            var m = Mat4.Identity;
            // rows: where the galaxy's x, y and z axes go (row vectors, as the rest of the renderer uses)
            // worked from ToUniverse: x' = x·cu − (y·st + z·ct)·su, y' = y·ct − z·st, z' = x·su + (y·st + z·ct)·cu
            m.M11 = cu * s; m.M12 = 0; m.M13 = su * s;
            m.M21 = -st * su * s; m.M22 = ct * s; m.M23 = st * cu * s;
            m.M31 = -ct * su * s; m.M32 = -st * s; m.M33 = ct * cu * s;
            m.M41 = (float)((g.X - _ax) - _ox); m.M42 = (float)((g.Y - _ay) - _oy); m.M43 = (float)((g.Z - _az) - _oz);
            return m;
        }

        // how close the nearest thing is (universe units): a star of the near galaxy, or the edge of the nearest galaxy
        // the nearest thing drawn round you: the nearest galaxy, star, world or ground, and the bodies drawn close (asteroids, comets,
        // black holes' discs), so a shot's departure is paced from the one you were beside
        private double NearestDrawn()
        {
            double best = NearestDistance();
            foreach (var ex in _extras)
            {
                if (ex.Shape.Length == 0 && !ex.Hole) continue;
                double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - ex.R * 1.6;
                if (d <= 0) d = ex.R * 0.01;
                if (d < best) best = d;
            }
            return best;
        }
        private double NearestDistance()
        {
            if (_matterScale > 0) return Units.Km(_matterScale) * 0.5;
            double best = double.MaxValue;
            foreach (var g in _galaxies)
            {
                double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) - g.Scale * 1.0;
                best = Math.Min(best, Math.Max(d, g.Scale * 0.02));
            }
            if (_system != null) best = Math.Min(best, PlaceSystem());
            if (_ground != null) best = Math.Min(best, AboveGround());
            if (_near >= 0 && _nearLocal != null) { double ds; if (NearestStar(out ds) >= 0) best = Math.Min(best, ds); }
            return best;
        }

        private static int Cell(double v) { return (int)Math.Floor((v + 1.6) / 3.2 * GridN); }

    }
}
