// Universe · shell · the universe view, its part: bodies (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── every other kind of body, made round you from content/bodies.unt, drawn and clickable: a supermassive black hole at
        // each galaxy's heart; stellar black holes, neutron stars and rogue planets among a galaxy's stars; stars and rogue
        // planets between the galaxies; asteroids, Kuiper-belt bodies and Oort-cloud comets round a star ──
        private sealed class Place { public string Id, Name, Where, Entry, Says, Shape; public double FeedEdd = 0.1, PerLy3, SizeKm, SizeLo, SizeHi, SizePow = 1, RoundKm, FeedShare, JetShare, ComaLo, ComaHi, TailLo, TailHi, DiscLo = 3, DiscHi = 60, SwarmLo = 12, SwarmHi = 120, EatShare, OrbitSeconds = 8, EatLo = 1, EatHi = 1; public int Shown, Swarm; public float R, G, B; }   // EatLo..EatHi: how many stars a feeding hole tears apart at once   // DiscLo..DiscHi: a black hole's disc, in horizon radii; Swarm stars on orbits of SwarmLo..SwarmHi radii; EatShare: how many feeding holes are tearing a star apart now; OrbitSeconds: the inner edge's turn, as watched
        private sealed class Extra { public double X, Y, Z, R, Mass, ComaKm, TailKm, DayH; public string Name; public List<string> Lines; public float Cr, Cg, Cb; public bool Hole; public int Feeding; public float Tilt, Turn; public string Shape = ""; public Place Pl; public bool Eating; public string Mix, KindName; public double Density, Albedo; public int EatN; public double Spin; public double SpinHold = double.NaN; public double OffX, OffY, OffZ, LOffX, LOffY, LOffZ; public bool Orbits; public ulong MakeSeed; public double OffAt = double.NaN, GoX, GoY, GoZ, GoVx, GoVy, GoVz; public double OrbE, OrbQ, OrbPeriod, OrbCx, OrbCy, OrbCz, LX, LY, LZ; public double[] OrbE1, OrbE2; public int ShapeIx; }   // ShapeIx: which of the rock shapes is its (by its seed; its band by its size)   // Spin: a share of the most a hole could have (content/physics.unt @law frame-dragging). EatN: the stars being torn apart at once; Mix: the ground its matter is (content/minerals.unt @mix); Density g/cc; Albedo its share of light reflected   // Feeding: 0 quiet, 1 a glowing disc, 2 a disc and jets   // OffAt, Go: gravity off, its place and velocity when it went
        private List<Place> _places; private readonly List<Extra> _extras = new List<Extra>(); private string _extraKey = "";
        // a feeding black hole's accretion disc and its jets, made once in its own units (Schwarzschild radii), drawn for each,
        // tilted its own way, turning, sized to it

        // a feeding black hole's parts, made once for each kind of hole (content/bodies.unt: its disc's reach in horizon radii, its
        // swarm of stars, how fast its inner edge turns as watched), in its own frame (its horizon's radius 1, its disc level); every
        // motion in them is worked out on the card each frame (Pipeline: the gas on Kepler orbits, the stars on their ellipses, the
        // torn star's stream flowing)
        private sealed class HoleParts { public StarVertex[] SwarmRecs; public UploadBuffer Disc, Jets, Swarm, Stream; public int DiscN, JetsN, SwarmN, StreamN; public float K; }
        private readonly Dictionary<string, HoleParts> _holeParts = new Dictionary<string, HoleParts>();
        /// <summary>Every black hole's shadow: a black cone of the view round it, its half-angle by the law for where the camera
        /// stands (sin^2 = (27/4)(rs/r)^2 (1 - rs/r)): 2.6 horizon radii across from afar, half the sky at the photon sphere, nearly
        /// all of it just outside the horizon. Drawn over the stars and galaxies behind the hole; the disc, swarm and streams are drawn
        /// after it, their own captured light dropped on the card.</summary>
        private void DrawShadows(int w, int h)
        {
            if (!_haveAxes) return; var lk = Look;
            foreach (var ex in _extras)
            {
                if (!ex.Hole || ex.Mass <= 0) continue;
                double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz), r = d / Math.Max(1e-300, rs);
                if (r <= 1.0005) continue;
                double s2 = 6.75 / (r * r) * Math.Max(0, 1 - 1 / r), th = r >= 1.5 ? Math.Asin(Math.Sqrt(Math.Min(1, s2))) : Math.PI - Math.Asin(Math.Sqrt(Math.Min(1, s2)));
                if (th < Math.PI / 2 && Math.Tan(th) * h / (2 * Math.Tan(0.45)) < 0.6) continue;   // (smaller than a pixel: nothing to draw)
                double ux = dx / d, uy = dy / d, uz = dz / d;
                _hud.DrawCone(_meshes, w, h, ux * _viewRight.X + uy * _viewRight.Y + uz * _viewRight.Z, ux * _viewUp.X + uy * _viewUp.Y + uz * _viewUp.Z, ux * lk.X + uy * lk.Y + uz * lk.Z, th);
                // its photon ring: the thin circle of bent light at the shadow's edge, where light from everything behind and around
                // it has looped the hole on its way to you (the ring the Event Horizon Telescope photographed): faint, there on every
                // hole, feeding or quiet, so a quiet hole on an empty sky is still there to be seen, a black disc in a ring of light
                if (th < Math.PI / 2)
                {
                    var u = new Vec3((float)ux, (float)uy, (float)uz); var e1 = Vec3.Cross(u, Math.Abs(u.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(u, e1).Normalized;
                    double thr = th * 1.04; float ct = (float)Math.Cos(thr), st = (float)Math.Sin(thr); const int N = 240; float dist = (float)Math.Min(d, 1e-3);
                    if (_photonV.Length < N) _photonV = new StarVertex[N];
                    for (int q = 0; q < N; q++) { double a = q * Math.PI * 2 / N; var dir = u * ct + (e1 * (float)Math.Cos(a) + e2 * (float)Math.Sin(a)) * st; _photonV[q] = new StarVertex { X = dir.X * dist, Y = dir.Y * dist, Z = dir.Z * dist, Size = 0, R = 0.55f, G = 0.6f, B = 0.7f, A = 1 }; }
                    if (_photonBuf == null) _photonBuf = new UploadBuffer(_gpu.Device, (long)N * Stride); _photonBuf.Write(_photonV, N);
                    var vp0 = _lastVp; DrawPoints(_photonBuf, N, vp0, _viewRight, _viewUp, 1f, 0.0016f, w, h, 0, 0.0016f, 0.45f);
                }
            }
        }
        private UploadBuffer _photonBuf; private StarVertex[] _photonV = new StarVertex[0];
        private Vec3 _fallAxis = new Vec3(0, 0, 1); private UploadBuffer _fallBuf; private int _fallCap; private Extra _fallHole; private double _fallTau, _fallRate = 1; private double _fallShare, _fallScale0;   // the way in, 0 at the horizon to 1 at the point (the fall's own measure: the view held at the horizon's scale till the walls meet, then the zoom into the point)   // the fall's clock: proper time since the horizon, its rate against real time; deep: past a thousandth of a radius, the zoom carries on to the quarks   // the hole you are falling into: the camera itself travels down the axis toward its middle
        /// <summary>Inside the horizon, the fall's one reference: the gravity well itself, in the world. The sheet of space round the
        /// hole as a square grid (the rubber sheet), bent into Flamm's paraboloid outside the horizon (its depth 2 root(rs (r - rs)),
        /// the exact shape of a still slice of Schwarzschild space, flat far out, vertical at the horizon's throat) and, inside the
        /// throat, into the surface the same equation gives there with its sign flipped (space and time swapped: a timelike depth),
        /// 2 root(rs (rs - r)), from the throat down to the point, the singularity, two radii below it.
        /// You are on the axis at your depth, the walls round you, every line of the grid running down to the point ahead; the axis is
        /// the way you came in, fixed in space, so turning the view turns round the well. (A reference, not light: never bent by the
        /// hole's lens.) Flamm 1916, from the Schwarzschild metric; the free-faller's own slices are flat, so this is the still slice's shape.</summary>
        private void DrawFallGrid(Mat4 vp, Vec3 right, Vec3 up, int w, int h, double time)
        {
            var ax = _fallAxis; var e1 = Vec3.Cross(ax, Math.Abs(ax.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(ax, e1).Normalized;
            // where you are down the well: the fall runs from the horizon (r = rs) to the quarks' size, 25 tenfolds; the cone inside
            // the throat narrows to the point over that run
            // the surface, from the metric itself (rs = 1): outside the horizon Flamm's, its height above the throat 2 root(r - 1)
            // (z'^2 = g_rr - 1 = 1/(r - 1)), a bowl opening upward, flat far out, vertical at the throat; inside the horizon the same
            // equation with its sign flipped (there g_rr is negative: space and time have swapped roles, and the depth is timelike),
            // 2 root(1 - r), running from the throat down to the point at r = 0, two radii below it. Down is toward the point: the
            // far sheet is above and behind you, the throat's ring at depth 0, the point at depth 2. You are at your real r
            double rhoCam = Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds);   // your depth: the way in, in the fall's own measure
            // the well inside the horizon is the dilation itself, not the hole's size: the funnel is drawn in the fall's own measure,
            // each tenfold of the radius two views of length, forty tenfolds from the horizon to the point for every hole (the way
            // in is not the hole's size: inside there is near-infinite space and no space at once), so it is far longer than the space the hole takes
            // up, and it is complete at every depth: an inward spike, fixed in your view, that you travel down. The mouth (the
            // horizon's ring, the big opening) is a view and a half wide; the cone squeezes straight down from it, linear, to the
            // point eighty views below, where it ends in no space at all. As you fall the walls beside you close in and get tighter,
            // and the point is a pin point below you the whole way, nearer and nearer, until the walls meet at it. Looking down you
            // see the whole run to the point; looking up, the whole run back to the opening
            const double PerTenfold = 2.0;
            double T = MatterView.FallTenfolds;                                                         // tenfolds from the horizon to the end: the same immense run for every hole
            double tCam = Math.Min(T - 0.01, -Math.Log10(Math.Max(1e-300, rhoCam)));                   // your own depth in tenfolds below the horizon
            Func<double, double> radiusAt = t => 1.5 * Math.Max(0, T - t) / T;                          // the cone's radius at a depth, in views: a view and a half at the mouth, nothing at the point; at your own depth tighter the deeper you are
            double Smouth = radiusAt(0);                                                                 // the mouth's radius in views: the sheet outside the horizon is drawn at that scale (one horizon radius = Smouth)
            Func<double, double> tw = t => 0;                                                            // a gravity well is rings and straight spokes: no twist, no turning (the drag's twist read as a trippy effect, not a well)
            Func<double, double> along = rho => rho >= 1 ? -tCam * PerTenfold - 2 * Math.Sqrt(rho - 1) * Smouth : (-Math.Log10(Math.Max(1e-300, rho)) - tCam) * PerTenfold;   // how far ahead of you (down) a ring of that radius lies; negative: above
            Func<double, double> width = rho => rho >= 1 ? rho * Smouth : radiusAt(-Math.Log10(Math.Max(1e-300, rho)));   // its radius in views
            var pts = new List<StarVertex>();
            Action<double, double, float, float, float, float> put = (u, v, br, cr, cg, cb) =>
            {
                double rho = Math.Sqrt(u * u + v * v), wv = rho > 0 ? width(rho) / rho : 0;
                var p = e1 * (float)(u * wv) + e2 * (float)(v * wv) + ax * (float)along(rho);
                pts.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0, R = cr * br, G = cg * br, B = cb * br, A = 1 });   // (no size of its own: a point of the line, the same few pixels at any distance)
            };
            // the sheet outside the horizon: its square grid every half radius out to eight, bent into Flamm's bowl down to the throat,
            // at the mouth's own scale (a view and a half), always: the opening you look back up at from any depth
            {
                // rings of the sheet at every radius out to eight, and spokes from the throat out: a gravity well, round from the top
                // (a square grid read as a square at the mouth)
                double[] sheetRings = { 1.08, 1.2, 1.4, 1.7, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8 };
                foreach (double rho in sheetRings)
                {
                    float br = (float)(0.45 * Math.Max(0.25, 1 - Math.Abs(rho - 1) * 0.08)); int n = (int)Math.Max(96, Math.Min(2400, rho * Smouth * 2 * Math.PI / 0.02));
                    for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n; put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
                }
                for (int sp = 0; sp < 36; sp++)
                {
                    double a = sp * Math.PI * 2 / 36; int n = (int)Math.Max(100, Math.Min(1200, 7 * Smouth / 0.02));
                    for (int q = 0; q <= n; q++) { double rho = 1 + 7.0 * q / n; float br = (float)(0.4 * Math.Max(0.25, 1 - Math.Abs(rho - 1) * 0.08)); put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
                }
            }
            // inside: the whole cone, a ring every sixth of a tenfold from the mouth to the point and spokes the whole way down
            // between them, every one ending at the point. Every line carries a point about every fiftieth of a view of its own
            // length, as the sheet's lines do, so it reads as a line right beside you (a ring of a few dozen points, a spoke of a
            // point every eighth of a view, was a scatter of single pixels at arm's length: the walls were not there to be seen,
            // only the mouth's ring and the sheet)
            const double Along = 0.02;                                                      // a point every fiftieth of a view along a line
            int kEnd = (int)Math.Ceiling(T * 6);
            for (int k = 0; k <= kEnd; k++)
            {
                double t = Math.Min(T, k / 6.0), rho = Math.Pow(10, -t), rw = radiusAt(t); if (rw <= 0) break; if (rw > 200) continue;   // (a ring wider than two hundred views is beyond the picture)
                float br = (float)(0.55 * Math.Max(0.45, 1 - Math.Abs(t - tCam) * 0.15));
                int n = (int)Math.Max(96, Math.Min(1500, rw * 2 * Math.PI / Along));
                for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n + tw(t); put(Math.Cos(a) * rho, Math.Sin(a) * rho, br, 0.35f, 0.9f, 0.6f); }
            }
            {
                int qn = (int)Math.Min(6000, Math.Max(600, T * PerTenfold / Along));
                for (int sp = 0; sp < 36; sp++)
                {
                    double a = sp * Math.PI * 2 / 36;
                    for (int q = 0; q <= qn; q++) { double t = T * q / (double)qn, rho = Math.Pow(10, -t), a2 = a + tw(t); if (radiusAt(t) > 200) continue; put(Math.Cos(a2) * rho, Math.Sin(a2) * rho, 0.45f, 0.35f, 0.9f, 0.6f); }
                }
            }
            { int n = (int)Math.Max(480, Math.Min(3000, Smouth * 2 * Math.PI / Along)); if (Smouth <= 200) for (int q = 0; q < n; q++) { double a = q * Math.PI * 2 / n; put(Math.Cos(a), Math.Sin(a), 0.9f, 0.7f, 0.95f, 1f); } }   // the mouth: the horizon's ring, bright
            { var p = ax * (float)((T - tCam) * PerTenfold); pts.Add(new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = 0.04f, R = 1f, G = 1f, B = 1f, A = 1 }); }   // the point it all squeezes down to, at the cone's end
            if (_fallBuf == null || _fallCap < pts.Count) { if (_fallBuf != null) _fallBuf.Dispose(); _fallCap = pts.Count + 64; _fallBuf = new UploadBuffer(_gpu.Device, (long)_fallCap * Stride); }
            var arr = pts.ToArray(); _fallBuf.Write(arr, arr.Length);
            float lz = _lens[2]; _lens[2] = 0; float sky0 = _sky; _sky = 1;                      // (a reference, not light: never bent by the hole's lens, and never faded with the sky at the very small: the sky's fade below ten microns had taken the grid with it, gone before the atoms' scales)
            // drawn through the matter's own projection (positions in units of your scale, the near plane a thousandth of it): the
            // universe's projection cut the grid off at its near plane once the fall was deep
            var fv = Mat4.LookAt(new Vec3(0, 0, 0), Look, ViewUp()); var fvp = fv * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), 0.001f, 1e9f);
            DrawPoints(_fallBuf, arr.Length, fvp, new Vec3(fv.M11, fv.M21, fv.M31), new Vec3(fv.M12, fv.M22, fv.M32), 1f, 0.0012f, w, h, time, 0, 1f, 2f);
            _lens[2] = lz; _sky = sky0;
        }
        private UploadBuffer _wellBuf; private int _wellN;
        private void MakeWell()
        {
            var l = new List<StarVertex>(); double[] rings = { 1, 1.15, 1.35, 1.6, 2, 2.5, 3.2, 4, 5, 6.5, 8, 10, 13, 16, 20, 25, 32, 40 };
            Func<double, double> depth = r => -2 * Math.Sqrt(Math.Max(0, r - 1));
            foreach (var r in rings) for (int k = 0; k < 160; k++) { double a = k * Math.PI * 2 / 160; float br = (float)(r <= 1 ? 0.8 : 0.45); l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)depth(r), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.5f * br, G = 0.72f * br, B = 0.9f * br, A = 1 }); }
            for (int sp = 0; sp < 36; sp++)
            {
                double a = sp * Math.PI * 2 / 36;
                for (int k = 0; k < 72; k++) { double r = Math.Pow(40, k / 71.0); l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)depth(r), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.17f, G = 0.26f, B = 0.33f, A = 1 }); }
                for (int k = 1; k <= 40; k++) { double t = k / 40.0, r = 1 - t; l.Add(new StarVertex { X = (float)(Math.Cos(a) * r), Y = (float)(-14 * t), Z = (float)(Math.Sin(a) * r), Size = 0.22f, R = 0.5f, G = 0.6f, B = 0.75f, A = 1 }); }   // the step: from the horizon's rim straight down to the point
            }
            l.Add(new StarVertex { X = 0, Y = -14, Z = 0, Size = 0.5f, R = 1f, G = 1f, B = 1f, A = 1 });
            _wellN = l.Count; _wellBuf = new UploadBuffer(_gpu.Device, (long)l.Count * Stride); _wellBuf.Write(l.ToArray(), l.Count);
        }
        // a solid body's inside on its card (content @layer for its ground's kind, src/gen/BodyLayers.cs): its layers from the
        // ground to the middle, and its whole make-up by element (each layer weighed by its volume and density, made of its mix)
        private List<string> InsideWords(string terrainKind, string groundMix, ulong seed)
        {
            var l = new List<string>(); var bl = BodyLayers.Of(_content); var lays = bl.ForWorld(terrainKind); if (lays.Count == 0) return l;
            var names = new List<string>(); foreach (var q in lays) names.Add(q.Name.ToLowerInvariant());
            l.Add("inside: " + string.Join(", ", names));
            var bulk = bl.Bulk(terrainKind, groundMix ?? PlanetTerrain.TerrainKind(terrainKind).Grains ?? terrainKind, seed);   // (its own: scattered from its kind's by its seed)
            if (bulk.Count > 0) l.Add("whole, by mass: " + BodyLayers.Words(bulk, 6));
            return l;
        }
        // what a body is made of, on its card: its kind, its make-up, its minerals and their elements, its density and mass, its albedo
        private void Compose(Extra ex, ContentEntry made)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            ex.KindName = made.Name; ex.Mix = made.Get("mix", null); if (ex.Mix != null && ex.Mix.Length == 0) ex.Mix = null;
            if (ex.Mix != null) ex.Mix = _matter.VaryMix(ex.Mix, (ulong)(Math.Abs(Math.Sin(ex.X * 7.1 + ex.Y * 13.3 + ex.Z * 3.7) * 1e6) % 1e6));   // its own shares of its kind's minerals, by its seed (content/minerals.unt vary): no two bodies of a kind the same
            ex.Lines.Add(made.Name);   // (its kind; its words are in the content and the docs: the card stays short)
            { var parts = made.Get("makeup", "").Split(';'); if (parts.Length > 0 && parts[0].Trim().Length > 0) ex.Lines.Add("made of: " + Short(parts[0].Trim(), 70)); }   // (the first clause of its make-up: the minerals and elements below say the rest)
            if (ex.Mix != null) foreach (var ln in _matter.MakeUpWords(ex.Mix)) ex.Lines.Add(ln);
            if (made.Kind == "asteroid" || made.Kind == "comet") foreach (var ln in InsideWords(made.Kind, made.Get("mix", null), ex.MakeSeed = 1UL + (ulong)(Math.Abs(Math.Sin(ex.X * 7.1 + ex.Y * 13.3 + ex.Z * 3.7) * 1e6) % 1e6))) ex.Lines.Add(ln);   // (its layers, and its whole make-up by element)
            double dens; if (double.TryParse(made.Get("density", ""), fs, ci, out dens) && dens > 0)
            {
                ex.Density = dens; double rM = ex.R / Units.Km(1) * 1000, kg = dens * 1000 * 4.0 / 3 * Math.PI * rM * rM * rM; ex.Mass = kg / 1.989e30;
                ex.Lines.Add("density " + dens.ToString("0.#") + " g/cc, mass " + kg.ToString("0.#E+0") + " kg, " + ScaleHud.Words(ex.R / Units.Km(1) * 2) + " across");
            }
            double alb; if (double.TryParse(made.Get("albedo", ""), fs, ci, out alb)) { ex.Albedo = alb; ex.Lines.Add("albedo " + alb.ToString("0.##") + ": reflects " + (alb * 100).ToString("0") + "% of the light" + (alb < 0.06 ? ", darker than coal" : "")); }
            if (made.Props.ContainsKey("linked-meteorite")) ex.Lines.Add("its meteorites: " + made.Get("linked-meteorite", ""));
        }
        // a card's line cut short at a clause (a comma, a colon, a semicolon) before the limit: the whole sentence lives in the content
        private static string Short(string t, int limit)
        {
            if (t == null || t.Length <= limit) return t; int cut = -1;
            foreach (var sep in new[] { "; ", ": ", ", " }) { int k = t.LastIndexOf(sep, limit); if (k > cut) cut = k; }
            return cut > limit / 2 ? t.Substring(0, cut) : t.Substring(0, limit).TrimEnd();
        }
        private static string Secs(double t)
        {
            if (t < 1e-3) return (t * 1e6).ToString("0.#") + " microseconds";
            if (t < 1) return (t * 1e3).ToString("0.#") + " milliseconds";
            if (t < 90) return t.ToString("0.#") + " seconds";
            if (t < 5400) return (t / 60).ToString("0.#") + " minutes";
            return (t / 3600).ToString("0.#") + " hours";
        }
        // a star's own make-up (content/stars.unt makeup, by count of atoms): its kind's shares, its share of everything past helium
        // drawn for the star itself (a third to three times its kind's: stars differ), the rest renormalised
        private string StarMakeUp(ContentEntry kind, double x, double y, double z, out double metals)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            double u = Math.Abs(Math.Sin(x * 2.3 + y * 9.1 + z * 4.7) * 7331.113) % 1;
            double lifeLo, lifeHi; bool longLived = kind.TryRange("life-years", out lifeLo, out lifeHi) && lifeHi >= 5e9;   // the long-lived kinds (K and M dwarfs): many are old, born when the galaxy was poorer in everything past helium
            metals = longLived ? Math.Exp((u - 0.7) * 3.0) : Math.Exp((u - 0.5) * 2.2);
            var names = new List<string>(); var shares = new List<double>(); double total = 0;
            foreach (var bit in kind.Get("makeup", "").Split(','))
            {
                string t = bit.Trim(); int pc = t.IndexOf('%'), sp = pc > 0 ? t.LastIndexOf(' ', pc) : -1; if (sp < 0) continue;
                double share; if (!double.TryParse(t.Substring(sp + 1, pc - sp - 1), fs, ci, out share)) continue;
                string nm = t.Substring(0, sp).Trim(); if (nm != "hydrogen" && nm != "helium") share *= metals;
                names.Add(nm); shares.Add(share); total += share;
            }
            if (total <= 0) return null;
            var parts = new List<string>();
            for (int q = 0; q < names.Count; q++) { double v = shares[q] / total * 100; parts.Add(names[q] + " " + (v >= 1 ? v.ToString("0.#") : v >= 0.01 ? v.ToString("0.00#") : v >= 0.0001 ? v.ToString("0.00000") : v.ToString("0.#E+0")) + "%"); }
            return string.Join(", ", parts.ToArray());
        }
        private ContentEntry StarKindEntry(string kindName) { foreach (var e in _content.OfKind("stars.unt", "star")) if (e.Name == kindName) return e; return null; }
        private ContentEntry StarKindEntryById(string id) { foreach (var e in _content.OfKind("stars.unt", "star")) if (e.Id == id) return e; return null; }
        // the star of the system you are in: its own plasma's make-up, for the dive into its surface
        private string SystemStarMakeUp()
        {
            if (_sysStar < 0 || _sysStar >= _localCount) return null;
            var e = StarKindEntryById(_kinds[_localKind[_sysStar]].Id); if (e == null) return null;
            double m; return StarMakeUp(e, _ax, _ay, _az, out m);
        }
        // the picked hole's radius from you in horizon radii (outside, within sixty of them; inside, the fall), for the well's graph; 0 for none
        private double HoleRadii()
        {
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return Math.Pow(10, -Math.Max(0, _fallShare) * MatterView.FallTenfolds);
            if (_tKind != 6 || _tIndex < 0 || _tIndex >= _extras.Count || !_extras[_tIndex].Hole || _extras[_tIndex].Mass <= 0) return 0;
            var ex = _extras[_tIndex]; double rs = Units.Km(Physics.HorizonKm(ex.Mass)), dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz) / rs;
            return d < 60 ? d : 0;
        }
    }
}
