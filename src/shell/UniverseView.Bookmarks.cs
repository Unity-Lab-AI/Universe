// Universe · shell · the universe view, its part: bookmarks and waypoints (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── bookmarks: a view kept (M), its place, the way it looks and its scale, at every size from the universe's mesh down to
        // the matter; flown back to (1 to 9, or the Bookmarks menu) by one cinematic move: out from where you are to a scale that
        // holds both places, across, and down onto the kept view, arriving exactly where it was kept, facing the way it faced, at
        // its scale (a view in the matter: on down into it to its size). Every bookmark is a waypoint too: a small ring on screen
        // where it lies, its number, name and how far it is. Kept per universe (its seed) in bookmarks.txt beside the settings.
        // A place on a world is kept against the world (it goes round its star and turns), in its star's system against the star,
        // in a galaxy against the galaxy (by its seed), so each is found again wherever the world, the star and the galaxy are now ──
        private sealed class Bookmark
        {
            public string Name; public ulong Universe, Galaxy; public int Star = -1, Planet = -1;
            public double Ax, Ay, Az, Ox, Oy, Oz;          // the camera as kept: the anchor and the offset from it (universe units)
            public double Rx, Ry, Rz;                      // from its star (universe units), when in a system
            public double Bx, By, Bz;                      // its world's centre from the star when kept (universe units)
            public double Px, Py, Pz;                      // from its world's centre (km), when at a world
            public double Fx, Fy, Fz, Ux, Uy, Uz;          // the way it looked, and its up
            public double ViewKm, MatterKm;                // its scale (how far the view looked), and its size in the matter (0: not in it)
        }
        private static readonly string BookmarkFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PreviewText.AppName, "bookmarks.txt");
        private List<Bookmark> _marks;
        /// <summary>This universe's bookmarks, in the order they were kept (read from the file the first time).</summary>
        private List<Bookmark> Marks { get { if (_marks == null) LoadMarks(); return _marks; } }
        /// <summary>The bookmarks' names, numbered, for the Bookmarks menu.</summary>
        public List<string> BookmarkNames { get { var l = new List<string>(); foreach (var b in Marks) l.Add(b.Name); return l; } }
        /// <summary>A line for the screen: a bookmark kept, a flight begun, a flight ended (fades after a few seconds).</summary>
        private string _markNote; private double _markNoteT, _markDt;

        private void LoadMarks()
        {
            _marks = new List<Bookmark>();
            try
            {
                if (!File.Exists(BookmarkFile)) return;
                var ci = CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(BookmarkFile))
                {
                    var p = line.Split('\t'); if (p.Length < 33) continue;
                    ulong u; if (!ulong.TryParse(p[0], out u) || u != _seed) continue;
                    var b = new Bookmark { Universe = u, Name = p[1] }; ulong g; ulong.TryParse(p[2], out g); b.Galaxy = g;
                    int.TryParse(p[3], out b.Star); int.TryParse(p[4], out b.Planet);
                    var d = new double[28]; for (int i = 0; i < 28; i++) double.TryParse(p[5 + i], NumberStyles.Float, ci, out d[i]);
                    b.Ax = d[0]; b.Ay = d[1]; b.Az = d[2]; b.Ox = d[3]; b.Oy = d[4]; b.Oz = d[5]; b.Rx = d[6]; b.Ry = d[7]; b.Rz = d[8];
                    b.Bx = d[9]; b.By = d[10]; b.Bz = d[11]; b.Px = d[12]; b.Py = d[13]; b.Pz = d[14]; b.Fx = d[15]; b.Fy = d[16]; b.Fz = d[17];
                    b.Ux = d[18]; b.Uy = d[19]; b.Uz = d[20]; b.ViewKm = d[21]; b.MatterKm = d[22];
                    _marks.Add(b);
                }
            }
            catch (Exception ex) { ZoomLog("bookmarks: not read · " + ex.Message); }
        }
        private void SaveMarks()
        {
            try
            {
                // the other universes' bookmarks kept as they were; this one's written afresh
                var keep = new List<string>(); string mine = _seed.ToString(CultureInfo.InvariantCulture) + "\t";
                if (File.Exists(BookmarkFile)) foreach (var l in File.ReadAllLines(BookmarkFile)) if (!l.StartsWith(mine)) keep.Add(l);
                var ci = CultureInfo.InvariantCulture; Func<double, string> f = v => v.ToString("R", ci);
                foreach (var b in _marks)
                {
                    var d = new[] { b.Ax, b.Ay, b.Az, b.Ox, b.Oy, b.Oz, b.Rx, b.Ry, b.Rz, b.Bx, b.By, b.Bz, b.Px, b.Py, b.Pz, b.Fx, b.Fy, b.Fz, b.Ux, b.Uy, b.Uz, b.ViewKm, b.MatterKm, 0, 0, 0, 0, 0 };
                    var parts = new List<string> { b.Universe.ToString(ci), b.Name.Replace('\t', ' '), b.Galaxy.ToString(ci), b.Star.ToString(ci), b.Planet.ToString(ci) };
                    foreach (var v in d) parts.Add(f(v));
                    keep.Add(string.Join("\t", parts));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(BookmarkFile)); File.WriteAllLines(BookmarkFile, keep);
            }
            catch (Exception ex) { ZoomLog("bookmarks: not written · " + ex.Message); }
        }

        /// <summary>Keeps the view as it is now as a bookmark (M, or the Bookmarks menu).</summary>
        public void MarkHere()
        {
            if (Loading) return;
            var b = new Bookmark { Universe = _seed, Ax = _ax, Ay = _ay, Az = _az, Ox = _ox, Oy = _oy, Oz = _oz, Fx = _fx, Fy = _fy, Fz = _fz, Ux = _camUp.X, Uy = _camUp.Y, Uz = _camUp.Z };
            b.ViewKm = ViewDistanceKm(); b.MatterKm = _spaceScale <= 0 ? _matterScale : 0;
            if (_near >= 0 && _near < _galaxies.Count) b.Galaxy = _galaxies[_near].Seed;
            if (_system != null && _sysStar >= 0 && b.Galaxy != 0)
            {
                double sx, sy, sz; StarPos(_sysStar, out sx, out sy, out sz);
                b.Star = _sysStar; b.Rx = (_ax - sx) + _ox; b.Ry = (_ay - sy) + _oy; b.Rz = (_az - sz) + _oz;
                if (_ground != null && _planet >= 0)
                {
                    double k = Units.Km(1); b.Planet = _planet; b.Bx = _px; b.By = _py; b.Bz = _pz;
                    b.Px = (_ox - _px) / k; b.Py = (_oy - _py) / k; b.Pz = (_oz - _pz) / k;
                }
            }
            var here = Here().Words(); int cut = here.LastIndexOf(" › "); string last = cut >= 0 ? here.Substring(cut + 3) : here.Length > 0 ? here : "the universe";
            b.Name = last + ", " + ScaleHud.Words(b.ViewKm) + " across";
            Marks.Add(b); SaveMarks(); MenuChanged = true;
            Note("kept as bookmark " + Marks.Count + ": " + b.Name + (Marks.Count <= 9 ? " (" + Marks.Count + " flies back)" : ""));
        }
        /// <summary>Lets bookmark <paramref name="i"/> (from 0) go.</summary>
        public void Unmark(int i) { if (i < 0 || i >= Marks.Count) return; var n = Marks[i].Name; Marks.RemoveAt(i); SaveMarks(); MenuChanged = true; Note("bookmark let go: " + n); }
        /// <summary>Whether the waypoints (every bookmark ringed where it lies) are shown.</summary>
        public bool WaypointsOn { get { return Settings.Get("show-waypoints", true); } set { Settings.Set("show-waypoints", value); MenuChanged = true; } }
        private void Note(string s) { _markNote = s; _markNoteT = 0; ZoomLog("BOOKMARK " + s); }

        // where a bookmark is now, from the anchor (universe units): against its world if that world's system is the one you are
        // in, against its star if its galaxy is the near one, else where it was kept
        private void MarkFromAnchor(Bookmark b, out double x, out double y, out double z)
        {
            bool gal = b.Galaxy != 0 && _near >= 0 && _near < _galaxies.Count && _galaxies[_near].Seed == b.Galaxy;
            if (gal && b.Star >= 0)
            {
                double sx, sy, sz; StarPos(b.Star, out sx, out sy, out sz); sx -= _ax; sy -= _ay; sz -= _az;
                if (b.Planet >= 0)
                {
                    double k = Units.Km(1), bx = b.Bx, by = b.By, bz = b.Bz;
                    if (_system != null && _sysStar == b.Star && b.Planet < _system.Bodies.Count) { BodyPos(b.Planet, out bx, out by, out bz); if (_ground != null && _planet == b.Planet) { bx = _px; by = _py; bz = _pz; } }
                    x = sx + bx + b.Px * k; y = sy + by + b.Py * k; z = sz + bz + b.Pz * k; return;
                }
                x = sx + b.Rx; y = sy + b.Ry; z = sz + b.Rz; return;
            }
            x = (b.Ax - _ax) + b.Ox; y = (b.Ay - _ay) + b.Oy; z = (b.Az - _az) + b.Oz;
        }

        // ── the flight to a bookmark ──
        private Bookmark _fly; private double _flyT, _flyDur, _flyL0, _flyV0, _flySx, _flySy, _flySz, _flyUx, _flyUy, _flyUz, _flyLAx, _flyLAy, _flyLAz, _flyDive;
        private Vec3 _flyLook0;
        /// <summary>Whether a flight to a bookmark is under way.</summary>
        public bool FlyingToMark { get { return _fly != null; } }
        /// <summary>Flies to bookmark <paramref name="i"/> (from 0).</summary>
        public void FlyToMark(int i)
        {
            if (Loading || i < 0 || i >= Marks.Count) return;
            if (_tour != null) PauseTour("you flew to a bookmark");
            var b = Marks[i]; double dx, dy, dz; MarkFromAnchor(b, out dx, out dy, out dz);
            _flySx = _ox; _flySy = _oy; _flySz = _oz; _flyLAx = _ax; _flyLAy = _ay; _flyLAz = _az;
            double vx = dx - _ox, vy = dy - _oy, vz = dz - _oz; _flyL0 = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double k = Units.Km(1); _flyV0 = Math.Max(1e-12 * k, Math.Min(_flyL0 * 0.5, ViewDistanceKm() * k));
            if (_flyL0 < 1e-12 * k && b.MatterKm <= 0) { Note("already at bookmark " + (i + 1)); return; }
            _flyUx = vx / Math.Max(1e-300, _flyL0); _flyUy = vy / Math.Max(1e-300, _flyL0); _flyUz = vz / Math.Max(1e-300, _flyL0);
            // the flight's length from how many tenfolds of scale it crosses: out from your own scale to half the way, and down again
            // to the kept one (a few seconds next door, a quarter of a minute across the universe)
            double end = Math.Max(1e-12, b.ViewKm) * k, tenfolds = Math.Log10(Math.Max(1, _flyL0 * 0.5 / _flyV0)) + Math.Log10(Math.Max(1, _flyL0 * 0.5 / end));
            _flyDur = Math.Max(3, Math.Min(18, 2 + 0.45 * tenfolds)); _flyT = 0; _flyDive = 0; _flyLook0 = Look;
            if (_matterScale > 0 || _spaceScale > 0) { _matter.Unpick(); }
            _tKind = -1; _tName = ""; _fly = b;
            Note("flying to bookmark " + (i + 1) + ": " + b.Name);
        }

        // each frame of the flight: the camera placed on its path (out from where it began, then down onto the kept place, evenly
        // in scale), facing where it goes and, at the end, the way the kept view faced. Any click, the wheel or a key ends it
        private void FlyStep(Input input, double dt, int w, int h)
        {
            var b = _fly; if (b == null) return;
            if (_flyT > 0.3 && (input.LeftClicked || input.RightClicked || (input.Wheel != 0 && _flyDive <= 0) || AnyKey(input))) { _fly = null; Note("flight stopped"); return; }
            // the anchor moved (into a system, out of one): where the flight began moves with it, as every place is kept
            if (_ax != _flyLAx || _ay != _flyLAy || _az != _flyLAz) { _flySx += _flyLAx - _ax; _flySy += _flyLAy - _ay; _flySz += _flyLAz - _az; _flyLAx = _ax; _flyLAy = _ay; _flyLAz = _az; }
            double dx, dy, dz; MarkFromAnchor(b, out dx, out dy, out dz);
            var saved = new Vec3((float)b.Fx, (float)b.Fy, (float)b.Fz);
            if (_flyDive > 0)
            {
                // arrived over its ground: on down into the matter to its size, at a tenfold or so a second, the view as kept
                _flyDive += dt; SetLook(saved); input.MouseX = w / 2; input.MouseY = h / 2;
                double want = b.ViewKm, d = ViewDistanceKm();
                if ((_matterScale > 0 && d <= want * 1.05) || _flyDive > 40) { _fly = null; Note("at bookmark: " + b.Name); return; }
                input.Wheel = Math.Log(10) / Math.Log(1 / 0.87) * dt * Math.Min(1, Math.Log10(Math.Max(1.0001, d / want)) + 0.1);
                return;
            }
            _flyT += dt; double t = Math.Min(1, _flyT / _flyDur), s = t * t * (3 - 2 * t);
            double px, py, pz, lx, ly, lz;
            if (s < 0.5)
            {
                // out: away from where it began, toward the bookmark, from your own scale up to half the way
                double q = s / 0.5, r = Math.Exp(Math.Log(_flyV0) + (Math.Log(_flyL0 * 0.5) - Math.Log(_flyV0)) * q);
                if (_flyL0 * 0.5 <= _flyV0) r = _flyL0 * 0.5 * q;
                px = _flySx + _flyUx * r; py = _flySy + _flyUy * r; pz = _flySz + _flyUz * r;
            }
            else
            {
                // down: onto the kept place from half the way, evenly in scale, coming in along the way it looked; the last
                // twentieth straight onto it
                double q = (s - 0.5) / 0.5, k = Units.Km(1), end = Math.Max(1e-12 * k, Math.Min(_flyL0 * 0.5, b.ViewKm * k * 0.05));
                double r = q < 0.95 ? Math.Exp(Math.Log(_flyL0 * 0.5) + (Math.Log(end) - Math.Log(_flyL0 * 0.5)) * (q / 0.95)) : end * (1 - (q - 0.95) / 0.05);
                if (_flyL0 * 0.5 <= end) r = _flyL0 * 0.5 * (1 - q);
                double wx = -_flyUx, wy = -_flyUy, wz = -_flyUz, a = q * q;   // (from the way across to behind the kept look)
                wx = wx * (1 - a) - b.Fx * a; wy = wy * (1 - a) - b.Fy * a; wz = wz * (1 - a) - b.Fz * a; double wl = Math.Sqrt(wx * wx + wy * wy + wz * wz);
                if (wl < 1e-9) { wx = -b.Fx; wy = -b.Fy; wz = -b.Fz; wl = 1; }
                px = dx + wx / wl * Math.Max(0, r); py = dy + wy / wl * Math.Max(0, r); pz = dz + wz / wl * Math.Max(0, r);
            }
            _ox = px; _oy = py; _oz = pz;
            // the look: from where it was, turned over the first fifth to where it goes; over the last quarter to the kept look
            lx = dx - px; ly = dy - py; lz = dz - pz; double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            Vec3 toward = ll > 0 ? new Vec3((float)(lx / ll), (float)(ly / ll), (float)(lz / ll)) : saved;
            double a0 = Math.Min(1, t / 0.2), a1 = Math.Max(0, (t - 0.75) / 0.25); a0 = a0 * a0 * (3 - 2 * a0); a1 = a1 * a1 * (3 - 2 * a1);
            var lk = (_flyLook0 * (float)(1 - a0) + toward * (float)a0); lk = (lk * (float)(1 - a1) + saved * (float)a1);
            if (lk.Length > 1e-6f) SetLook(lk.Normalized);
            if (t >= 1)
            {
                _ox = dx; _oy = dy; _oz = dz; SetLook(saved); _camUp = new Vec3((float)b.Ux, (float)b.Uy, (float)b.Uz);
                if (b.MatterKm > 0) _flyDive = 1e-6; else { _fly = null; Note("at bookmark: " + b.Name); }
            }
        }
        private static bool AnyKey(Input input) { for (int k = 8; k < 256; k++) if (k != 0x10 && k != 0x11 && k != 0x12 && !(k >= 0x70 && k <= 0x87) && input.WasPressed(k) && input.Deliberate(k)) return true; return false; }

        // the keys: M keeps the view, 1 to 9 fly to the bookmark of that number
        private void MarkKeys(Input input)
        {
            if (input.WasPressed('M')) MarkHere();
            for (int i = 0; i < 9; i++) if (input.WasPressed('1' + i)) FlyToMark(i);
        }

        // the waypoints: every bookmark ringed where it lies (in front of you), its number, its name and how far it is; and the
        // last bookmark note at the foot of the screen for a few seconds
        private void DrawMarks(int w, int h, double dt)
        {
            // these lines are placed where they belong (each waypoint at its place, your coordinates under the scale bar): the whole
            // screen is their room, never the pick card's (its bottom had pushed your coordinates up into the middle, over the card)
            float keepTop = _hud.CardTop, keepBottom = _hud.CardBottom; _hud.CardTop = 2; _hud.CardBottom = h - 2;
            try { DrawMarksIn(w, h, dt); } finally { _hud.CardTop = keepTop; _hud.CardBottom = keepBottom; }
        }
        private void DrawMarksIn(int w, int h, double dt)
        {
            float u = (float)Math.Max(1, h / 1080.0);
            if (WaypointsOn && _matterScale <= 0 && _spaceScale <= 0)
                for (int i = 0; i < Marks.Count; i++)
                {
                    var b = Marks[i]; double x, y, z, sx, sy; MarkFromAnchor(b, out x, out y, out z);
                    x -= _ox; y -= _oy; z -= _oz; double d = Math.Sqrt(x * x + y * y + z * z);
                    if (d < Units.Km(Math.Max(1e-9, b.ViewKm)) * 0.02 || !ToScreen(x, y, z, out sx, out sy)) continue;   // (at it, or behind you: none)
                    if (sx < 0 || sy < 0 || sx > w || sy > h) continue;
                    _hud.DrawRing(_meshes, sx, sy, 7 * u, w, h);
                    _hud.DrawCard(_meshes, new[] { (i + 1) + ". " + b.Name, Units.Describe(d) + " away" }, sx + 10 * u, sy - 8 * u, w, h, 0.75f);
                }
            // your own coordinates, on screen at the foot of the view every frame (in the frame of the smallest thing you are in;
            // in the matter, where on the world it is): never only in the window's title, which a full screen hides
            {
                string cw = _matterScale > 0 || _spaceScale > 0 ? "you: " + ScaleHud.Words(ViewDistanceKm()) + " across" + (_ground != null && _spaceScale <= 0 ? ", " + CoordWords(_ox, _oy, _oz) : "") : "you: " + CameraCoords();
                _hud.DrawCard(_meshes, new[] { cw }, 24 * u, h - (_tour != null ? h * 0.1 + 34 * u : 34 * u), w, h, 0.85f);
            }
            if (_markNote != null)
            {
                _markNoteT += dt; if (_markNoteT > 5) { _markNote = null; return; }
                float fade = (float)Math.Min(1, (5 - _markNoteT) / 1.5);
                _hud.DrawCard(_meshes, new[] { _markNote }, w * 0.5 - 200 * u, h * 0.82, w, h, fade);
            }
        }
    }
}
