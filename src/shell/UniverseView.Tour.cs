// Universe · shell · the universe view, its part: tour (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── a tour: a flight of stops read from content/tours/<name>.unt, flown with the real controls (the wheel's zoom at one
        // steady rate, the drag's circling and looking, the same picks a click makes) so it shows the universe as it is ──
        private sealed class TourStop { public string Name, Do, Target, Caption; public double Rate, UntilKm, Turn, Tilt, Seconds, Pan, ToRadii, ToKm, Sweep, Rise, MaxRate, FarLo, FarHi; public bool Through, Out; }   // Out: a zoom out that ends once back out of the matter (a body's ground, a hole) into space   // Through: flown on through without slowing or stopping (into the next zoom); FarLo..FarHi: a far-galaxy pick's crossing, light-years
        private List<TourStop> _tour; private int _tourAt; private double _tourClock, _tourStall, _tourLastD, _tourT, _tourRate;
        /// <summary>Starts the tour of that name (content/tours/name.unt); false if there is none.</summary>
        public bool StartTour(string name)
        { _tourVisited = 0;
            var list = new List<TourStop>(); var ci = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
            Func<string, double, double> num = (t, f) => { double v; return double.TryParse(t, fs, ci, out v) ? v : f; };
            foreach (var e in _content.OfKind("tours/" + name + ".unt", "stop"))
            {
                list.Add(new TourStop { Name = e.Id, Do = e.Get("do", "orbit"), Target = e.Get("target", "none"), Caption = e.Get("caption", ""), Rate = num(e.Get("rate", "0.3"), 0.3),
                    UntilKm = num(e.Get("until-km", "0"), 0), Turn = num(e.Get("turn", "6"), 6), Tilt = num(e.Get("tilt", "0"), 0), Seconds = num(e.Get("seconds", "10"), 10), Through = e.Get("through", "no") == "yes", Out = e.Get("out", "no") == "yes", Pan = num(e.Get("pan", "0"), 0),
                    ToRadii = num(e.Get("to-radii", "0"), 0), ToKm = num(e.Get("to-km", "0"), 0), Sweep = num(e.Get("sweep", "0"), 0), Rise = num(e.Get("rise", "0"), 0), MaxRate = num(e.Get("max-rate", "0.35"), 0.35) });
                { double lo, hi; if (e.TryRange("far-ly", out lo, out hi)) { list[list.Count - 1].FarLo = lo; list[list.Count - 1].FarHi = hi; } }   // (a far-galaxy pick: how far the crossing is, light-years)
            }
            if (list.Count == 0) return false;
            _tour = list; _tourName = name; _pausedTour = null; _pausedWhy = null; MenuChanged = true; _tourAt = 0; _tourT = 0; _tourRate = 0; _tourClock = 0; _shotRng = new Rng((ulong)Environment.TickCount ^ _seed); NewShot();
            if (TimeStep == 0) SetTime(1);                                              // time runs on the tour (the planets turn, the electrons move)
            return true;
        }
        public bool Touring { get { return _tour != null; } }
        /// <summary>Stops the tour; the controls are yours again.</summary>
        public void StopTour() { _tour = null; _pressed = false; _pausedTour = null; _pausedWhy = null; }
        // a tour you took the controls from is paused, not lost: it says so, and why, and carries on (T) or starts again (R)
        // from where it was; Backspace lets it go
        private string _tourName = "grand-tour"; public string TourName { get { return _tourName; } }
        private List<TourStop> _pausedTour; private int _pausedAt; private string _pausedWhy;
        public bool TourPaused { get { return _pausedTour != null; } }
        /// <summary>The tour ran to its end (it offers itself again).</summary>
        public bool TourFinished { get { return _pausedTour != null && _pausedWhy == null; } }
        private void PauseTour(string why) { _pausedTour = _tour; _pausedAt = _tourAt; _pausedWhy = why; _tour = null; _pressed = false; _tourRate = 0; _shotT = -1; MenuChanged = true; }
        /// <summary>Carries a paused tour on from the stop it was at (the shot under way taken again from where the camera is now).</summary>
        public void ResumeTour()
        {
            if (_pausedTour == null) return;
            _tour = _pausedTour; _tourAt = _pausedAt >= _pausedTour.Count ? 0 : _pausedAt; _tourT = 0; _tourClock = 0; _tourRate = 0; _shotT = -1; _pausedTour = null; _pausedWhy = null; NewShot();
            if (TimeStep == 0) SetTime(1); MenuChanged = true;
        }
        private static string KeyName(int k)
        {
            if ((k >= 0x30 && k <= 0x39) || (k >= 0x41 && k <= 0x5A)) return "the " + (char)k + " key";
            if (k >= 0x70 && k <= 0x87) return "F" + (k - 0x6F);
            switch (k) { case 0x20: return "Space"; case 0x0D: return "Enter"; case 0x1B: return "Esc"; case 0x08: return "Backspace"; case 0x09: return "Tab"; case 0x25: case 0x26: case 0x27: case 0x28: return "an arrow key"; case 0xBC: return "the , key"; case 0xBE: return "the . key"; }
            return "a key";
        }
        private double _tourH0, _tourLookFrom; private bool _tourWaiting, _pickFailed;   // (a pick that found nothing: the stops that depend on it are skipped, never shown with the wrong thing in view)
        // the director's choices for the shot under way, made afresh for each shot of each viewing (so no two tours are the same):
        // which way it circles, how fast (a little either side of the script's own), and a slow crane up or down as it goes;
        // the last galaxy's kind, so the next is another kind
        private Rng _shotRng = new Rng(1); private double _shotDir = 1, _shotMul = 1, _shotCrane; private string _lastGalaxyType = "";
        private double[] _shotE1, _shotE2, _shotUp, _shotLook0; private double _waitSpin, _shotTurnT = 2, _shotSweep;   // (_shotSweep: how far round the shot sweeps, radians, signed: the stop's sweep, or round to a small body's lit side)
        private double[] _shotP0; private double _shotR0;   // where the shot began (universe units) and the size of what was in view there: the departure is paced from it
        private float _localLf = -1; private int _localVersion, _localWritten = -1, _localWrittenN = -1; private double _shotD0, _shotD1, _shotEl0, _shotEl1, _shotLen, _shotT = -1, _shotELast, _shotAllowLog;   // the shot under way: its frame round the subject, its distances and heights, its length
        private void NewShot()
        {
            _shotDir = _shotRng.NextDouble() < 0.5 ? -1 : 1; _shotMul = 0.75 + 0.5 * _shotRng.NextDouble(); _shotCrane = (_shotRng.NextDouble() * 2 - 1) * 1.5;
        }
        // how fast the tour may go now: never faster than the world round you is made (slower when the frames a second drop below
        // about 45, and while many pieces of ground are still waiting to be built), eased so it never jerks
        private double _tourPace = 1;
        // out at the cosmic web's scale (beyond the galaxies made in full round you) while its light is still being made
        private bool WebNotMade { get { return _glowAll == null && _farScale > _nearUnits * 0.5; } }
        private double TourPace
        {
            get
            {
                double want = Math.Max(0.5, Math.Min(1, Window.Fps / 40.0));
                if (_ground != null && _matterScale <= 0 && _ground.Backlog > 80 && AboveGround() > Units.Km(MatterView.OverlayKm)) want *= Math.Max(0.6, 80.0 / _ground.Backlog);   // (below a centimetre the grains cover the ground: nothing is waited for, on down to the hand-over)
                if (WebNotMade) want = 0.05;                                            // out among the web before its light is made: wait for it
                if (_matterScale > 0 || _spaceScale > 0) want = 1;                     // in the matter nothing is waited for (the ground's building behind you never slows it)
                _tourPace += (want - _tourPace) * 0.05; return _tourPace;
            }
        }
        // the tour is looking round (up at the sky, round the view, gliding): the view is its own, never turned back to the pick
        private bool TourLooking { get { if (_tour == null || _tourAt >= _tour.Count) return false; var d = _tour[_tourAt].Do; return d == "shot" || (_ground != null && (d == "look" || d == "glide" || (d == "orbit" && _tKind != 2))); } }
        // the caption: the stop's own (a pick, over in a moment, shows the next stop's, so every caption stays on through its flight)
        // what is really up in the sky from where you stand (for a caption written as "{sky}"): the star only if it is above the
        // horizon and in view, the other worlds and this world's moons only if they are, the galaxy's stars by night, the day sky by day
        private string SkyWords()
        {
            if (_ground == null || _planet < 0 || _system == null) return "the sky";
            double cx, cy, cz; CamFromPlanet(out cx, out cy, out cz); double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz); if (cl <= 0) return "the sky";
            double ux = cx / cl, uy = cy / cl, uz = cz / cl;
            Func<double, double, double, bool> seen = (dx, dy, dz) => { double l = Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l <= 0 || (dx * ux + dy * uy + dz * uz) / l < 0.02) return false; double sx, sy; return ToScreen(dx, dy, dz, out sx, out sy) && sx >= 0 && sx <= _lastW && sy >= 0 && sy <= _lastH; };
            bool star = seen(-_ox, -_oy, -_oz), day = (-_ox * ux - _oy * uy - _oz * uz) > 0;
            int worlds = 0, moons = 0;
            for (int i = 0; i < _system.Bodies.Count && i + 1 < _bodyCount; i++)
            {
                if (i == _planet) continue; var b = _system.Bodies[i];
                if (!seen(_bodyVerts[i + 1].X, _bodyVerts[i + 1].Y, _bodyVerts[i + 1].Z)) continue;   // (from the camera: the system has placed them)
                if (b.Parent == _planet) moons++; else if (b.Parent < 0) worlds++;
            }
            var parts = new List<string>();
            if (star) parts.Add("its star");
            if (moons > 0) parts.Add(moons == 1 ? "its moon" : "its " + moons + " moons");
            if (worlds > 0) parts.Add(worlds == 1 ? "another of its star's worlds" : worlds + " of its star's other worlds");
            parts.Add(day ? "the day sky, its star's light through the air" : "the galaxy's stars beyond");
            string w = string.Join(", ", parts.ToArray()); w = char.ToUpperInvariant(w[0]) + w.Substring(1);
            // the air, and what it does to the light: its make-up (the world's own, from its kind), the sky's colour (the air's own
            // colour, as the game draws it), and the star's colour from its surface heat, seen through that air or not at all
            string airHere = null; bool hasAir = _ground.AirKm > 0 && PlanetTerrain.Air.TryGetValue(_ground.Kind, out airHere) && !string.IsNullOrEmpty(airHere);
            if (!hasAir) airHere = null;
            string starCol = StarColourWords(_system.TempK), starKind = _system.StarName != null ? _system.StarName.ToLowerInvariant() : "its star";
            if (!hasAir) return w + ". No air here: the sky is black by day, the stars shine beside its star, and its light comes unchanged, " + starCol + " (a " + starKind + ", " + _system.TempK.ToString("N0") + " K)";
            string gases = AirGasesWords(airHere); string skyCol = ColourWords(_ground.AirR, _ground.AirG, _ground.AirB);
            bool clear = airHere.ToLowerInvariant().StartsWith("nitrogen") || airHere.ToLowerInvariant().StartsWith("oxygen");
            return w + ". The air, " + gases + ", " + (clear ? "scatters the blue of its star's light, so the sky is " + skyCol + " by day and reddens toward the horizon, where the light crosses the most air" : "tints the sky " + skyCol + " by day, its haze and dust scattering the light") + "; its star, a " + starKind + " at " + _system.TempK.ToString("N0") + " K, is " + starCol + ", and shows " + (clear ? "a little yellower" : "dimmer and redder") + " through the air than from space";
        }
        // the colour of a star from its surface heat (a black body: blue-white when hottest, red when coolest)
        private static string StarColourWords(double k) { return k >= 10000 ? "blue-white" : k >= 7500 ? "white" : k >= 6000 ? "yellow-white" : k >= 5000 ? "yellow-orange" : k >= 3700 ? "orange" : k >= 2500 ? "red" : "a dim deep red"; }
        // a colour in words, from its red, green and blue
        private static string ColourWords(float r, float g, float b)
        {
            float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)); if (mx <= 0.08f) return "nearly black";
            if (mx - mn < 0.08f) return mx > 0.7f ? "white" : "grey";
            if (b >= r && b >= g) return g > b * 0.8f ? "pale blue" : r > b * 0.6f ? "violet" : "blue";
            if (r >= g && r >= b) return g > r * 0.75f ? "yellow" : g > r * 0.45f ? "orange, butterscotch" : "red";
            return b > g * 0.7f ? "blue-green" : "green";
        }
        // an air's main gases in words, from its make-up line ("nitrogen 78%, oxygen 21%, ...": the first three, with their shares)
        private static string AirGasesWords(string air)
        {
            var names = new List<string>(); foreach (var bit in air.Split(',')) { string t = bit.Trim(); int pc = t.IndexOf('%'); if (pc < 0) { int sp = t.IndexOf("  ("); if (sp > 0) t = t.Substring(0, sp); if (t.Length > 0 && names.Count < 3) names.Add(t); continue; } int sp2 = t.LastIndexOf(' ', pc); string nm = sp2 > 0 ? t.Substring(0, sp2).Trim() : t; string share = sp2 > 0 ? t.Substring(sp2 + 1, pc - sp2) : ""; if (names.Count < 3) names.Add(nm + (share.Length > 0 ? " " + share : "")); }
            return names.Count == 0 ? "thin" : string.Join(", ", names.ToArray());
        }
        // a galaxy far across the universe (the stop's far-ly: a crossing of billions of light-years): a destination ahead of you,
        // inside the visible universe; the regions round it hold the same galaxies they always will (each from its own place), so
        // they are made now, whole, and held until you arrive. One whose heart is feeding first, and another kind than the last
        private long _heldRegion; private double _heldX, _heldY, _heldZ;
        private bool FarGalaxy(TourStop st)
        {
            var rng = new Rng(_seed ^ (ulong)(_tourAt * 7919 + Environment.TickCount)); var lk = Look; double R = _regionUnits;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                double d = (st.FarLo + (Math.Max(st.FarLo, st.FarHi) - st.FarLo) * rng.NextDouble()) / Units.LightYearsPerUnit;
                // ahead of you, within about 25 degrees of where you look
                double ax = lk.X + (rng.NextDouble() - 0.5) * 0.9, ay = lk.Y + (rng.NextDouble() - 0.5) * 0.9, az = lk.Z + (rng.NextDouble() - 0.5) * 0.9, al = Math.Sqrt(ax * ax + ay * ay + az * az);
                double px = _cx + ax / al * d, py = _cy + ay / al * d, pz = _cz + az / al * d;
                if (px * px + py * py + pz * pz > Math.Pow(_uRadius * 0.9, 2)) continue;    // (inside the visible universe, well in from its edge)
                long ci = (long)Math.Floor(px / R), cj = (long)Math.Floor(py / R), ck = (long)Math.Floor(pz / R);
                int best = -1; double bs = double.MinValue; var keys = new HashSet<long>();
                for (long i = ci - 1; i <= ci + 1; i++) for (long j = cj - 1; j <= cj + 1; j++) for (long k = ck - 1; k <= ck + 1; k++) { SpawnRegion(i, j, k); keys.Add(i * 73856093L ^ j * 19349663L ^ k * 83492791L); }   // (the regions' own galaxies, made if they are not yet)
                // the tour flies by a feeding hole's disc: a galaxy whose heart is feeding is required (a quiet one only if sixteen
                // tries round the aim find none, which with a third of hearts feeding they do not); every galaxy of the regions
                // counts, not only the ones made this moment
                for (int g = 0; g < _galaxies.Count; g++) { var gg = _galaxies[g]; if (!keys.Contains(gg.Region)) continue; if (!HeartFeeds(gg) && attempt < 15) continue; double score = rng.NextDouble() + (HeartFeeds(gg) ? 10 : 0) + (gg.Type != _lastGalaxyType ? 1 : 0); if (score > bs) { bs = score; best = g; } }
                if (best < 0) continue;
                var gb = _galaxies[best]; _tKind = 0; _tIndex = best; _tName = gb.Name; _lastGalaxyType = gb.Type; _heldRegion = gb.Region; _heldX = gb.X; _heldY = gb.Y; _heldZ = gb.Z;
                ZoomLog("far galaxy " + gb.Name + " at " + Units.Describe(Math.Sqrt((gb.X - _cx) * (gb.X - _cx) + (gb.Y - _cy) * (gb.Y - _cy) + (gb.Z - _cz) * (gb.Z - _cz))) + (HeartFeeds(gb) ? " (its heart feeding)" : ""));
                return true;
            }
            return false;
        }
        // whether a galaxy's central black hole is feeding: the same place-hash and share the hole itself is made with (MakeExtra, from
        // content/bodies.unt), so the tour can choose a galaxy with a feeding heart before it is made
        private bool HeartFeeds(Galaxy g)
        {
            foreach (var pl in _places) if (pl.Where == "galaxy-heart") { double u2 = Math.Abs(Math.Sin(g.X * 3.7 + g.Y * 91.1 + g.Z * 13.3) * 24634.6345) % 1; return u2 < pl.FeedShare; }
            return false;
        }
        // the black hole you picked, for a caption written as "{hole}": its jets if it fires them, its disc, or quiet; its horizon and its mass
        private string HoleWords()
        {
            if (_tKind != 6 || _tIndex < 0 || _tIndex >= _extras.Count || !_extras[_tIndex].Hole) return "A black hole";
            var ex = _extras[_tIndex]; string rs = ScaleHud.Words(Physics.HorizonKm(ex.Mass) * 2), mass = (ex.Mass >= 1e4 ? ex.Mass.ToString("0.#E+0") : ex.Mass.ToString("0.#")) + " solar masses";
            string eat = ex.Eating ? "; a star torn into a stream and eaten" : "";
            return ex.Feeding == 2 ? "Two jets from its poles, flung out at nearly the speed of light, reach far beyond the galaxy's heart: a horizon " + rs + " across, " + mass + eat
                 : ex.Feeding == 1 ? "Feeding, without jets: a disc of gas going down into a horizon " + rs + " across, " + mass + eat
                 : "Quiet: no disc, only its shadow and the light it bends; a horizon " + rs + " across, " + mass;
        }
        // your horizon, for a caption written as "{horizon}": how far you see each way, and why it ends there (the expansion of space, sped by dark energy)
        private string HorizonWords() { return "It ends only at your horizon, " + Units.Describe(_uRadius) + " each way, which moves with you: past it space stretches away faster than light can cross it, sped by dark energy (" + (_darkEnergy * 100).ToString("0") + "% of everything, " + _hubble.ToString("0") + " km/s a megaparsec)"; }
        // a stop that only means anything inside a body's matter (its crystal, lattice, atoms, electrons, nucleus, nucleons, quarks)
        private static bool MatterStop(string name)
        {
            if (name == null) return false; string n = name.ToLowerInvariant();
            foreach (var w in new[] { "crystal", "lattice", "atom", "electron", "shell", "bond", "nucleus", "proton", "quark", "let-go" }) if (n.Contains(w)) return true;
            return false;
        }
        private string TourCaption { get { int at = _tourAt; while (_tour != null && at < _tour.Count - 1 && _tour[at].Do == "pick") at++; return _tour != null && at < _tour.Count ? _tour[at].Caption.Replace("{sky}", SkyWords()).Replace("{hole}", HoleWords()).Replace("{horizon}", HorizonWords()).Replace("{supernova}", SupernovaWords()).Replace("{seen}", SeenWords()).Replace("{body}", BodyWords()) + (_tourWaiting ? "   (making what is ahead...)" : "") + "   (the view: " + ScaleHud.Words(ViewDistanceKm()) + ")" : ""; } }   // the scale is what the tour shows
        // one frame of the tour: what the stop asks, given to the controls; on to the next when it is done
        private void TourStep(Input input, double dt, int w, int h)
        {
            _tourClock += dt;
            if (_tourClock > 2)
            {
                // you take the controls: a click, the wheel or a key pauses it (it says so, and why); never in its first moments
                // (only on purpose: the click that brings the window back to the front, Shift, Ctrl, Alt and the Windows keys, and
                // shortcuts such as Alt+Tab or Win+Shift+S never stop it; nor do screenshots and recording)
                if (input.LeftClicked || input.RightClicked) { PauseTour("you clicked"); return; }   // (the click that brings the window to the front too: a click is a click, it stops the tour and picks as any other; it had picked nothing, and nothing seemed clickable)
                if (input.Wheel != 0 && !input.JustActivated) { PauseTour("you turned the mouse wheel"); input.Wheel = 0; return; }
                for (int k = 8; k < 256; k++) if (k != 0x78 && k != 0x7B && !(k >= 0x70 && k <= 0x87) && input.WasPressed(k) && input.Deliberate(k)) { PauseTour("you pressed " + KeyName(k)); return; }
            }
            if (_tourAt >= _tour.Count) { PauseTour(null); return; }   // (the end: it says so, and offers it again)
            var st = _tour[_tourAt]; _tourT += dt; bool done = false;
            if (_pickFailed && st.Do != "pick") { ZoomLog("TOUR stop " + st.Name + " skipped: its pick found nothing"); _tourAt++; _tourT = 0; NewShot(); return; }   // (never a shot of the wrong thing with the caption of the right one)
            if (MatterStop(st.Name) && _matterScale <= 0 && _spaceScale <= 0) { ZoomLog("TOUR stop " + st.Name + " skipped: not in any body's matter (the dive did not come)"); _tourAt++; _tourT = 0; NewShot(); return; }   // (a lattice's caption over open sky, a fly across six million km: never)
            input.MouseX = w / 2; input.MouseY = h / 2;                                    // the controls act at the middle of the view
            switch (st.Do)
            {
                case "zoom":
                    {
                        // one steady rate in scale (each tenfold the same time), eased in and out
                        double d = ViewDistanceKm(), want = st.UntilKm, sign = st.Rate >= 0 ? 1 : -1;
                        // a zoom out of a body's matter ends where the view pops out to space (its written until is a stand-in): that is
                        // its real end, and it is eased to, as every end is (it had run at full rate until the pop, and stopped dead)
                        if (st.Out && want >= 1e30) want = _spaceScale > 0 ? _spaceTop * 0.6 : _matterScale > 0 ? _matterTop * 0.6 : want;
                        double left = Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, want)));
                        // in a black hole the way is the fall's own measure (forty tenfolds of the way in, then the zoom): what is left
                        // of a climb out is the share of the way still to climb and the zoom back out, not the view's scale against the
                        // top (the scale sits just under the top the whole way up, so the climb had crawled at a sixth of its rate for
                        // minutes while the fall ran at full rate)
                        if (_spaceScale > 0 && _matter.SpaceKind == 12 && _fallHole != null) left = (sign < 0 ? Math.Max(0, _fallShare) : Math.Max(0, 1 - _fallShare)) * MatterView.FallTenfolds + Math.Abs(Math.Log10(Math.Max(1e-40, _spaceScale) / Math.Max(1e-40, sign < 0 ? _fallScale0 : d)));
                        // one movement, however many stops it is written as: a zoom followed by another the same way runs straight on
                        // (no easing out at the join, no fresh start after it); the last of them slows to a crawl over its final tenfold
                        // and a half, a twentieth of its rate at the end, and ends at that crawl, as every move does (it had dropped
                        // to an eighth and then stopped dead at its until)
                        var nx = _tourAt + 1 < _tour.Count ? _tour[_tourAt + 1] : null; bool chain = !st.Out && nx != null && nx.Do == "zoom" && (nx.Rate >= 0 ? 1 : -1) == sign;
                        double target = Math.Abs(st.Rate) * (st.Through || chain ? 1 : Math.Max(0.05, Math.Sqrt(Math.Min(1, left / 1.5)))) * TourPace;   // (never faster than the world is made)
                        double ramp = Math.Min(1, _tourT / 2.5); ramp = ramp * ramp * (3 - 2 * ramp);                     // brought up from rest over the first two and a half seconds (an exponential ease-in was fastest in its first frame: a jerk away from the last thing)
                        double aim = Math.Max(target * ramp, Math.Min(target, _tourRate));                                 // (a rate carried in from the stop before is kept, never dropped to ramp again)
                        _tourRate += (aim - _tourRate) * (1 - Math.Exp(-dt * 1.5));
                        double notches = _tourRate * Math.Log(10) / Math.Log(1 / 0.87) * dt;       // a wheel notch is 13% of the way
                        input.Wheel = OnCourse() * OverPick() * sign * notches;   // never moving one way while looking another; in the matter, never past what is picked (only once over it)
                        // a zoom back out of the matter (and on up off the ground, to a kilometre) looks down at what it leaves, eased back to
                        // the way the dive came in (85 degrees down): the circling at the electron, the nucleus and the quark had left the view
                        // facing wherever it stopped, often across or up into the dark, and the atoms, the crystal and the grain went by unseen
                        if (sign < 0 && _ground != null && _spaceScale <= 0 && (_matterScale > 0 || AboveGround() < Units.Km(1))) TourPitchToward(-85 * Math.PI / 180, 1 - Math.Exp(-dt * 1.2));
                        // never stuck off course: a zoom that finds what it flies to away from the look (a glide before it flew on past the
                        // spot it picked, and left it behind) turns the view toward it, eased, and moves again once it is on course (it had
                        // sat still for good: the wheel held at nothing while off course, and nothing turning the look back)
                        if (OnCourse() < 1 && !(_spaceScale > 0 && _matter.SpaceKind == 12) && _tKind >= 0)
                        {
                            double tdx, tdy, tdz, trad;
                            if (TargetFromCamera(out tdx, out tdy, out tdz, out trad))
                            {
                                double tl = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz), k = 1 - Math.Exp(-dt * 0.8); var lk = Look;
                                if (tl > 0) SetLook(lk.X + (tdx / tl - lk.X) * k, lk.Y + (tdy / tl - lk.Y) * k, lk.Z + (tdz / tl - lk.Z) * k);
                            }
                        }
                        if (_spaceScale > 0 && _matter.SpaceKind == 12)
                        {
                            // the fall, on the tour: looking up at the sky through the mouth as you enter (from the first frame, no easing
                            // into it), held for the first seconds, then turned down along the funnel to its absolute point, and held
                            // there for the rest of the fall, the walls closing in round the view and meeting at it; a slow wheel, so
                            // the grid turns past; the fall itself about a minute
                            double tf = Math.Max(0, Math.Min(1, _fallShare));
                            var ax = _fallAxis; var e1 = Vec3.Cross(ax, Math.Abs(ax.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0)).Normalized; var e2 = Vec3.Cross(ax, e1).Normalized;
                            double cyc = _tourT, thAim = sign < 0 ? 0.2 : cyc < 6 ? 0.2 : Math.PI - 0.08;   // (0: up, the mouth; pi: down, the point; climbing out: up at the mouth the whole way, never down at the point)
                            if (_fallTh < 0)
                            {
                                // from the way you were looking as you came in (at the hole: down its axis), the turn's speed brought up from
                                // rest over its first second and a half: the look swings up to the mouth, never snaps to it
                                var l0 = Look; _fallTh = Math.Acos(Math.Max(-1, Math.Min(1, -(l0.X * ax.X + l0.Y * ax.Y + l0.Z * ax.Z))));
                                _fallPh0 = Math.Atan2(l0.X * e2.X + l0.Y * e2.Y + l0.Z * e2.Z, l0.X * e1.X + l0.Y * e1.Y + l0.Z * e1.Z); _fallTurnT = 0;
                            }
                            _fallTurnT += dt; double fr = Math.Min(1, _fallTurnT / 1.5);
                            _fallTh += (thAim - _fallTh) * (1 - Math.Exp(-dt * (cyc < 6 || sign < 0 ? 1.1 : 0.6) * fr));   // (up to the mouth in the first seconds, then down the funnel, and held; up again for the climb out)
                            double th = Math.Max(0.06, Math.Min(Math.PI - 0.06, _fallTh)), ph = _fallPh0 + _tourT * 0.12;
                            var side = e1 * (float)Math.Cos(ph) + e2 * (float)Math.Sin(ph);
                            var lk2 = (ax * (float)(-Math.Cos(th)) + side * (float)Math.Sin(th)).Normalized; _fx = lk2.X; _fy = lk2.Y; _fz = lk2.Z;
                        }
                        else if (st.Pan != 0 && (_tKind >= 0 || _matterScale > 0 || _spaceScale > 0)) TourTurn(st.Pan * Math.PI / 180 * dt * Math.Min(1, _tourT / 2), 0);   // circling what it flies to as it goes (in the matter too, where the eye goes round the point you dive into)
                        // going nowhere (nothing nearer to fly at) for six seconds: on to the next stop
                        if (Math.Abs(Math.Log10(Math.Max(1e-40, d) / Math.Max(1e-40, _tourLastD))) > 0.05 || (_spaceScale > 0 && _matter.SpaceKind == 12 && _fallShare < 1) || (_ground != null && _ground.Backlog > 0 && AboveGround() > Units.Km(MatterView.OverlayKm)) || WebNotMade) { _tourLastD = d; _tourStall = 0; } else _tourStall += dt;   // (below the overlay the ground's queue means nothing: the grains cover it)   // (held back by ground still being built, or the web's light: waiting, never given up on)
                        // a zoom in aimed below the hand-over into the matter is not over until you are in the matter: a step onto the
                        // ground had read the height as a nanometre for one frame, the stop called itself done at its two micrometres, the
                        // floor lifted the camera back before the hand-over could come, and every stop below (the crystal, the lattice,
                        // the atoms, the quark) was skipped as not in any body's matter
                        bool intoMatter = sign > 0 && _ground != null && _matterScale <= 0 && _spaceScale <= 0 && want < HandoffKm();
                        if ((sign > 0 && d <= want * 1.02 && !intoMatter) || (sign < 0 && !st.Out && d >= want * 0.98) || _tourT > 240 || (_tourStall > 20 && _tourT > 20) || (st.Out && _tourT > 0.5 && _spaceScale <= 0 && _matterScale <= 0)) { done = true; if (!chain) _tourRate = 0; input.Wheel = 0; _tourStall = 0; _tourLastD = 0; }
                    }
                    break;
                case "shot":
                    {
                        // a film's shot of what is picked: the camera moves in its own frame round the subject (how far, in the
                        // subject's own sizes or in km; how far round it sweeps; how far it rises or sinks), always facing it, the
                        // distance gliding evenly in scale and every move eased in and out; long enough that the scale never changes
                        // faster than max-rate tenfolds a second, so nothing streams past
                        double tdx, tdy, tdz, trad;
                        if (!TargetFromCamera(out tdx, out tdy, out tdz, out trad)) { done = true; break; }
                        double tl = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz); if (tl <= 0) { done = true; break; }
                        if (_tourT <= dt * 1.5 || _shotT < 0)
                        {
                            // its frame, from where the camera is: up the level up, round from where it stands now
                            var up = ShotUp(); double ux = up.X, uy = up.Y, uz = up.Z;
                            double cx = -tdx / tl, cy = -tdy / tl, cz = -tdz / tl, ce = cx * ux + cy * uy + cz * uz;
                            double hx = cx - ux * ce, hy = cy - uy * ce, hz = cz - uz * ce, hl = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                            if (hl < 1e-6) { hx = Math.Abs(ux) < 0.9 ? 1 : 0; hy = 0; hz = Math.Abs(ux) < 0.9 ? 0 : 1; double d0 = hx * ux + hz * uz; hx -= ux * d0; hy -= uy * d0; hz -= uz * d0; hl = Math.Sqrt(hx * hx + hy * hy + hz * hz); }
                            _shotE1 = new[] { hx / hl, hy / hl, hz / hl }; _shotUp = new[] { ux, uy, uz };
                            _shotE2 = new[] { uy * _shotE1[2] - uz * _shotE1[1], uz * _shotE1[0] - ux * _shotE1[2], ux * _shotE1[1] - uy * _shotE1[0] };
                            _shotSweep = st.Sweep * _shotDir * Math.PI / 180;
                            if (_tKind == 6 && _system != null && _tIndex >= 0 && _tIndex < _extras.Count && !_extras[_tIndex].Hole)
                            {
                                // a small body lit by its star: the shot sweeps round from where the camera is to the body's lit side (the
                                // short way round), so it comes to rest with the star behind the camera, never on a dark rock against the stars
                                double sx = _ax - _tx, sy = _ay - _ty, sz = _az - _tz, se = sx * ux + sy * uy + sz * uz; sx -= ux * se; sy -= uy * se; sz -= uz * se; double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                                if (sl > 1e-9) _shotSweep = Math.Atan2((sx * _shotE2[0] + sy * _shotE2[1] + sz * _shotE2[2]) / sl, (sx * _shotE1[0] + sy * _shotE1[1] + sz * _shotE1[2]) / sl);
                            }
                            _shotD0 = tl; _shotEl0 = Math.Asin(Math.Max(-1, Math.Min(1, ce)));
                            _shotD1 = st.ToKm > 0 ? Units.Km(st.ToKm) : st.ToRadii > 0 && trad > 0 ? trad * st.ToRadii : tl;
                            _shotEl1 = Math.Max(-1.3, Math.Min(1.3, _shotEl0 + st.Rise * Math.PI / 180));
                            double decades = Math.Abs(Math.Log10(_shotD1 / _shotD0));
                            _shotLen = Math.Max(st.Seconds, decades / Math.Max(0.05, st.MaxRate) + 2);
                            _shotT = 0; _shotLook0 = new[] { _fx, _fy, _fz }; _shotAllowLog = 0;
                            // the departure is paced from what was in view where the shot began: the nearest thing drawn (the asteroid or comet
                            // you were beside, a world's ground, a star), never the distance to the new subject (its pick is made before the
                            // shot starts, so that was an AU wide while the asteroid filled the view, and the asteroid was gone in a frame)
                            // (the start kept in the subject's own frame: a world moves on its orbit as the shot runs, and a start fixed in space drifted
                            // out of the pace's allowance, so the camera snapped back to it on the frames the glide crossed the allowance; and the pace
                            // from what is in view, never from a thing drawn a few hundred km off that the view never held)
                            _shotP0 = new[] { -tdx, -tdy, -tdz }; _shotR0 = Math.Max(Math.Min(Units.Km(ViewDistanceKm()), Math.Max(NearestDrawn(), Units.Km(ViewDistanceKm()) * 0.05)), Units.Km(1e-6)); _shotELast = 0;
                            // a turn takes as long as it needs at no more than about 20 degrees a second (eased in and out), and the shot
                            // is long enough to hold it: a pick far behind you is turned to calmly, never whipped round to
                            double turnDeg = Math.Acos(Math.Max(-1, Math.Min(1, -(_fx * tdx + _fy * tdy + _fz * tdz) / tl * -1))) * 180 / Math.PI;
                            _shotTurnT = Math.Max(1.5, turnDeg / 20 * 1.5); _shotLen = Math.Max(_shotLen, _shotTurnT + 3);   // (where it looks as the shot begins: it turns from there to the subject, never snapping)
                        }
                        if (!WebNotMade) _shotT += dt;                                          // (held while the web's light is made)
                        // the pan before the travel: the camera holds where it is while the look turns to the new subject (seven tenths of
                        // the turn's time), and only then sets off, so it is never pulled away from the last thing while still facing it
                        double hold = _shotLook0 != null ? Math.Max(1.5, _shotTurnT) * 0.7 : 0;
                        double u = Math.Min(1, Math.Max(0, _shotT - hold) / Math.Max(0.1, _shotLen - hold)), e = u * u * (3 - 2 * u);
                        // where the shot has the camera at a point e of its glide: the way from the subject and the distance
                        Func<double, double[]> frame = ee =>
                        {
                            double Dd = Math.Exp(Math.Log(_shotD0) + (Math.Log(_shotD1) - Math.Log(_shotD0)) * ee);
                            double az2 = _shotSweep * ee, el2 = _shotEl0 + (_shotEl1 - _shotEl0) * ee, ca2 = Math.Cos(az2), sa2 = Math.Sin(az2), ce3 = Math.Cos(el2), se3 = Math.Sin(el2);
                            return new[] { ce3 * (ca2 * _shotE1[0] + sa2 * _shotE2[0]) + se3 * _shotUp[0], ce3 * (ca2 * _shotE1[1] + sa2 * _shotE2[1]) + se3 * _shotUp[1], ce3 * (ca2 * _shotE1[2] + sa2 * _shotE2[2]) + se3 * _shotUp[2], Dd };
                        };
                        Func<double[], double> fromStart = f => { double px = f[0] * f[3] - _shotP0[0], py = f[1] * f[3] - _shotP0[1], pz = f[2] * f[3] - _shotP0[2]; return Math.Sqrt(px * px + py * py + pz * pz); };   // (from the start, in the subject's frame: zero at the start itself, growing along the glide)
                        // the departure paced from what was in view where the shot began: the camera is never farther from there than
                        // its pace allows (max-rate tenfolds a second, from that size), so an asteroid you were beside pulls away and
                        // shrinks smoothly instead of being left in one frame on the way to a comet across the system; once the
                        // shot's own glide is within that, it goes on as written, and it is not over until the camera has arrived
                        // the pace itself eased: brought up from rest over two and a half seconds once the pan is done, and slowed to a crawl
                        // over the last tenfold and a half of the way, a twentieth of its rate at the end (it had run at the stop's full rate
                        // from the first frame and stopped dead on arrival, so every long crossing jerked away and slammed to a halt: the
                        // black hole in one frame, another system in the next)
                        {
                            double tm = Math.Max(0, _shotT - hold), rampIn = Math.Min(1, tm / 2.5); rampIn = rampIn * rampIn * (3 - 2 * rampIn);
                            double endFrom = _shotP0 != null ? fromStart(frame(1)) : 0, allowNow = _shotR0 * Math.Pow(10, _shotAllowLog);
                            double leftDec = endFrom > 0 ? Math.Log10(Math.Max(1e-300, endFrom) / Math.Max(1e-300, allowNow)) : 0;
                            double easeOut = Math.Max(0.05, Math.Min(1, Math.Sqrt(Math.Max(0, leftDec) / 1.5)));
                            if (!WebNotMade && tm > 0) _shotAllowLog += Math.Max(0.05, st.MaxRate) * rampIn * easeOut * dt;
                        }
                        double allow = _shotP0 != null ? _shotR0 * Math.Pow(10, _shotAllowLog) : double.MaxValue, eUse = e;
                        var fr0 = frame(e);
                        if (_shotP0 != null && fromStart(fr0) > allow)
                        {
                            double lo = 0, hi = e;
                            for (int it = 0; it < 28; it++) { double mid = (lo + hi) * 0.5; if (fromStart(frame(mid)) > allow) hi = mid; else lo = mid; }
                            eUse = Math.Max(lo, _shotELast); fr0 = frame(eUse);                  // (never back along the glide: a shot only goes forward)
                        }
                        _shotELast = eUse;
                        double vx = fr0[0], vy = fr0[1], vz = fr0[2], D = fr0[3];
                        // the camera to where the shot has it now (the subject where it is now: worlds move on their orbits)
                        _ox += tdx + vx * D; _oy += tdy + vy * D; _oz += tdz + vz * D;
                        // the look glides from where it was to the subject over the shot's first part (eased both ends), never a snap
                        double lb = Math.Min(1, _shotT / Math.Max(1.5, _shotTurnT)); lb = lb * lb * (3 - 2 * lb);
                        if (_shotLook0 != null && lb < 1)
                        {
                            double ax = _shotLook0[0], ay = _shotLook0[1], az0 = _shotLook0[2], bx = -vx, by = -vy, bz = -vz;
                            if (ax * bx + ay * by + az0 * bz < -0.9) { ax += _shotUp[0] * 0.3; ay += _shotUp[1] * 0.3; az0 += _shotUp[2] * 0.3; }   // (turning right round: by way of up, never through nothing)
                            SetLook(ax + (bx - ax) * lb, ay + (by - ay) * lb, az0 + (bz - az0) * lb);
                        }
                        else SetLook(-vx, -vy, -vz);
                        if (u >= 1 && eUse >= e - 1e-9) { done = true; _shotT = -1; }     // (arrived: the glide done, and the departure's pace no longer holding it back)
                    }
                    break;
                case "orbit":
                case "look":
                    {
                        // never circling while looking near straight up or down (the turn would spin round the pole): ease the view
                        // toward level first, gently
                        { double lp = LocalPitch(); if (st.Do == "orbit" && Math.Abs(lp) > 0.9) TourPitchToward(Math.Sign(lp) * 0.9, 1 - Math.Exp(-dt * 0.8)); }
                        double ease = Math.Min(1, _tourT / 2.5) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 2.5); ease = ease * ease * (3 - 2 * ease);
                        double yaw = st.Turn * _shotDir * _shotMul * Math.PI / 180 * dt * ease;
                        if (st.Do == "look")
                        {
                            // a look: up to its tilt above the horizon (on a world, the sky with its stars and galaxies) or down below it,
                            // a definite aim, eased in over the stop
                            if (_tourT <= dt * 1.5) _tourLookFrom = LocalPitch();
                            double t01 = Math.Min(1, _tourT / Math.Max(0.5, st.Seconds * 0.8)), ee = t01 * t01 * (3 - 2 * t01);
                            double aim = Math.Max(-1.45, Math.Min(1.45, (_ground != null ? 0 : _tourLookFrom) + st.Tilt * Math.PI / 180));
                            TourTurn(yaw, 0); TourPitchToward(_tourLookFrom + (aim - _tourLookFrom) * ee, 0.5);
                        }
                        else TourTurn(yaw, (_ground == null ? _shotCrane : Math.Sin(_tourClock * 0.2) * 0.25) * Math.PI / 180 * dt * ease);   // out in space a slow crane up or down; on a world its gaze drifts barely
                        if (_tourT >= st.Seconds) done = true;
                    }
                    break;
                case "arc":
                    {
                        // the cinematic move: a slow dolly in (or out) while circling the subject, both eased in and out, turned smoothly
                        // with a distance to reach it circles on till it is there (easing out over the last few tenths of a tenfold),
                        // else for its seconds
                        double sign = st.Rate >= 0 ? 1 : -1, d = ViewDistanceKm();
                        bool there = st.UntilKm > 0 && ((sign > 0 && d <= st.UntilKm) || (sign < 0 && d >= st.UntilKm));
                        double left = st.UntilKm > 0 && Math.Abs(st.Rate) > 0 ? Math.Abs(Math.Log10(Math.Max(1e-40, d) / st.UntilKm)) / 0.6 : Math.Max(0, st.Seconds - _tourT) / 3;
                        double ease = Math.Min(1, _tourT / 3) * Math.Min(1, Math.Max(0.15, left)); ease = ease * ease * (3 - 2 * ease);
                        if (!there) input.Wheel = OnCourse() * OverPick() * sign * Math.Abs(st.Rate) * ease * TourPace * Math.Log(10) / Math.Log(1 / 0.87) * dt;   // only moving where it looks
                        { double lp = LocalPitch(); if (Math.Abs(lp) > 0.9) TourPitchToward(Math.Sign(lp) * 0.9, 1 - Math.Exp(-dt * 0.8)); }
                        TourTurn(st.Turn * _shotDir * _shotMul * Math.PI / 180 * dt * Math.Max(ease, 0.4 * Math.Min(1, _tourT / 3)), (_ground == null && _matterScale <= 0 ? _shotCrane : 0) * Math.PI / 180 * dt * ease);
                        if (_tourT >= st.Seconds && (there || st.UntilKm <= 0 || Math.Abs(st.Rate) <= 0 || _tourT >= st.Seconds * 2)) done = true;   // (circling on to its distance for at most twice its seconds: never held there; the next stop carries on)
                    }
                    break;
                case "fly":
                    {
                        // a steady flight forward, past what is ahead (a star, a world): the same share of the distance to the
                        // nearest thing each second, eased in and out, never a sudden speed-up; turning slowly as it goes
                        double ease = Math.Min(1, _tourT / 2) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 2);
                        var f = Look;
                        if (_matterScale > 0)
                        {
                            // in the matter: on through it, the way you face, at the height you are (among the crystal's atoms, past
                            // them, rate heights a second), as W A S D fly you
                            double msp = _matterScale * 0.6 * st.Rate * dt * ease;
                            double x0, y0, z0; CamFromPlanet(out x0, out y0, out z0); double r0 = Math.Sqrt(x0 * x0 + y0 * y0 + z0 * z0);
                            var upL = new Vec3((float)(x0 / r0), (float)(y0 / r0), (float)(z0 / r0));
                            var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
                            double fe = Vec3.Dot(f, east), fn = Vec3.Dot(f, north), fl = Math.Sqrt(fe * fe + fn * fn); if (fl < 1e-6) { fe = 1; fn = 0; fl = 1; }
                            _matter.Unpick(); _matter.Move(fe / fl * msp, fn / fl * msp);
                        }
                        else { double sp = Math.Max(NearestDistance(), _farScale) * st.Rate * dt * ease; _ox += f.X * sp; _oy += f.Y * sp; _oz += f.Z * sp; }
                        SetLook(Turn(Look, LevelUp(), st.Turn * Math.PI / 180 * dt * ease));
                        if (_tourT >= st.Seconds) done = true;
                    }
                    break;
                case "glide":
                    {
                        // a flight low over the land: level, at the height it began at, rising and falling with the ground, rate
                        // heights a second (a walk at head height, a glide over hills from higher), looking a little ahead and
                        // down, turning slowly; eased in and out
                        if (_ground == null) { done = true; break; }
                        double ease = Math.Min(1, _tourT / 3) * Math.Min(1, Math.Max(0, st.Seconds - _tourT) / 3); ease = ease * ease * (3 - 2 * ease);
                        double hNow = AboveGround(); if (_tourT <= dt * 1.5 || _tourH0 <= 0) _tourH0 = hNow;
                        double cx, cy, cz; CamFromPlanet(out cx, out cy, out cz); double cl = Math.Sqrt(cx * cx + cy * cy + cz * cz); if (cl <= 0) { done = true; break; }
                        double ux = cx / cl, uy = cy / cl, uz = cz / cl; var f = Look; double fu = f.X * ux + f.Y * uy + f.Z * uz;
                        double tx = f.X - ux * fu, ty = f.Y - uy * fu, tz = f.Z - uz * fu, tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        if (tl > 1e-6)
                        {
                            double sp = _tourH0 * st.Rate * dt * ease; _ox += tx / tl * sp; _oy += ty / tl * sp; _oz += tz / tl * sp;
                        }
                        double dh = (_tourH0 - AboveGround()) * Math.Min(1, dt * 3); _ox += ux * dh; _oy += uy * dh; _oz += uz * dh;   // the same height over the ground
                        TourPitchToward(-0.22, 1 - Math.Exp(-dt * 0.6));                         // looking a little ahead and down (against the world's own horizon)
                        TourTurn(st.Turn * Math.PI / 180 * dt * ease, 0);
                        if (_tourT >= st.Seconds) { done = true; _tourH0 = 0; }
                    }
                    break;
                case "pick":
                    {
                        // a pick that finds nothing yet (the stars round you, the star's worlds still being made) tries again each
                        // frame for up to twenty seconds, never going on with nothing chosen (every later stop would be lost)
                        int k0 = _tKind, i0 = _tIndex, p0 = _matter.PickCount; double x0 = _tx; TourPick(st.Target, st);
                        bool got = st.Target == "none" || (st.Target == "planet" && _tKind == 2 && _system != null) || (st.Target == "land" && _tKind == 3) || (_tKind >= 0 && (_tKind != k0 || _tIndex != i0 || _tx != x0)) || _matter.PickCount != p0;
                        // while it looks (what it wants still being made round you, or none there at all) it never sits still: it looks
                        // round slowly, eased in, and goes on after a few seconds at most
                        if (!got && _ground == null) TourTurn(4 * _shotDir * Math.PI / 180 * dt * Math.Min(1, _tourT / 1.5), 0);   // (out in space only: by a world, among its stones and grains, it holds still)
                        bool matterPick = st.Target == "grain" || st.Target == "spot" || st.Target == "atom" || st.Target == "bonded-atom" || st.Target == "electron" || st.Target == "nucleus" || st.Target == "proton" || st.Target == "quark";
                        if (got || _tourT > (matterPick ? 0.6 : st.Target == "asteroid" || st.Target == "comet" ? 2 : 2.5))   // (a pick that has not come in a couple of seconds is not coming: no five-second waits; a matter pick is answered at once from what is drawn, so one that is not there (a bond in a gas, an electron in a plasma) is given up in under a second)
                        {
                            done = true; _pickFailed = !got && st.Target != "none"; if (got) _tourVisited++;
                            ZoomLog("TOUR pick " + st.Target + (got ? ": " + _tName + " (kind " + _tKind + ")" : " FAILED after " + _tourT.ToString("0.0") + " s: " + _extras.Count + " bodies round you, " + _localCount + " stars, system " + (_system != null ? "open" : "none") + ", galaxy " + _near + "; its stops are skipped"));
                        }
                    }
                    break;
                default: done = true; break;
            }
            // no pauses: each stop goes straight on into the next (its caption stays on through the flight); the looking round is
            // done by the circling stops at the points that matter
            if (done && WebNotMade) { done = false; input.Wheel = 0; }               // (never on past the web before it shows)
            // held back while what is ahead is still being made (the web's light, the ground): never still, so it never looks frozen;
            // it keeps circling what it shows, and the caption says what it is waiting for
            _tourWaiting = WebNotMade || (st.Do == "zoom" && _ground != null && _matterScale <= 0 && _ground.Backlog > 80 && AboveGround() > Units.Km(MatterView.OverlayKm));   // (in the matter the ground is not being made: its old count means nothing)
            _waitSpin += ((WebNotMade ? 1 : 0) - _waitSpin) * (1 - Math.Exp(-dt * 0.8));   // (eased in and out, never a sudden start or stop)
            if (_waitSpin > 0.002) TourTurn(6 * _waitSpin * Math.PI / 180 * dt, 0);                   // (out among the galaxies only: by a world it never circles while it waits)
            if (done) { _tourAt++; _tourT = 0; NewShot(); if (_tourAt < _tour.Count) ZoomLog("TOUR stop " + _tour[_tourAt].Name + " (" + _tour[_tourAt].Do + ") begins at " + Units.Describe(Units.Km(ViewDistanceKm())) + " pick " + _tKind + ":" + _tIndex + " " + _tName + (_matterScale > 0 ? " matter " + _matter.Card()[0] + " pick-off " + _matter.PickOff.ToString("0.00") : "")); }
        }
        // what a stop picks, as a click would
        private void TourPick(string what, TourStop st = null)
        {
            switch (what)
            {
                case "none": _tKind = -1; _tName = ""; _matter.Unpick(); break;
                case "black-hole":
                case "asteroid":
                case "comet":
                    {
                        // one of the bodies round you (content/bodies.unt): the nearest black hole, asteroid of the belt, or comet of
                        // the Oort cloud
                        int bi = -1; double bd = double.MaxValue, nearestHole = double.MaxValue;
                        // a black hole: this galaxy's own heart (or one within a few galaxy-widths), a feeding one before a quiet one; never a
                        // feeding heart ten million light-years off outscoring the quiet one you are at (the shot would leave the galaxy
                        // with the caption saying it was going down to its heart)
                        double holeReach = what == "black-hole" && _near >= 0 && _near < _galaxies.Count ? _galaxies[_near].Scale * 4 : double.MaxValue;
                        if (what == "black-hole" && _near >= 0 && _near < _galaxies.Count)
                        {
                            // this galaxy's own heart, first and surest: every galaxy has one at its middle; if the bodies round you do not
                            // hold it yet (they are made again as you move, and a far galaxy's may lag a frame), it is made here from its
                            // place, the same one the galaxy would get
                            var g = _galaxies[_near]; double tol = g.Scale * 0.02;
                            for (int q = 0; q < _extras.Count; q++) { var ex = _extras[q]; if (!ex.Hole) continue; double hx = ex.X - g.X, hy = ex.Y - g.Y, hz = ex.Z - g.Z; if (hx * hx + hy * hy + hz * hz < tol * tol) { bi = q; break; } }
                            if (bi < 0 && _places != null) foreach (var pl in _places) if (pl.Where == "galaxy-heart") { _extras.Add(MakeExtra(pl, g.X, g.Y, g.Z)); bi = _extras.Count - 1; ZoomLog("TOUR the heart of " + g.Name + " made for the pick (it was not among the " + (_extras.Count - 1) + " bodies round you)"); break; }
                            if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; _tSpot = null; break; }
                        }
                        for (int q = 0; q < _extras.Count; q++)
                        {
                            var ex = _extras[q]; string nm = ex.Name.ToLowerInvariant();
                            bool fits = what == "black-hole" ? ex.Hole : what == "asteroid" ? ex.Shape == "rock" : ex.Shape == "comet" || nm.Contains("comet");   // (an asteroid: any rocky body here, of a belt, the Kuiper belt or the Oort cloud, as the system has)
                            if (!fits) continue;
                            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = dx * dx + dy * dy + dz * dz;
                            if (what == "black-hole" && d > holeReach * holeReach) { if (d < bd * 1e12) nearestHole = Math.Min(nearestHole, Math.Sqrt(d)); continue; }
                            if (what == "black-hole" && ex.Feeding > 0) d *= 1e-6;
                            if (what == "asteroid" && nm.Contains("asteroid")) d *= 1e-4;          // (the belt's own, before a far icy body)             // (a feeding one, its disc glowing, before a quiet one)
                            if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; _tSpot = null; }
                        else if (what == "black-hole") ZoomLog("TOUR no black hole within " + Units.Describe(holeReach) + " (the nearest " + (nearestHole < double.MaxValue ? Units.Describe(nearestHole) : "none") + " away; galaxy " + _near + " scale " + (_near >= 0 && _near < _galaxies.Count ? Units.Describe(_galaxies[_near].Scale) : "-") + ")");
                    }
                    break;
                case "supernova":
                    { var sn = NearestSupernova() ?? YoungRemnant(); if (sn != null) PickSupernova(sn); else ZoomLog("TOUR no supernova, going or young, in the " + _galaxies.Count + " galaxies made round you"); }
                    break;
                case "giant":
                    if (_system != null)
                    {
                        // a giant of this star's worlds (gas or ice), by chance among them, weighted as the planets are
                        var rng = new Rng(_seed ^ (ulong)(Environment.TickCount + 47)); var opts = new List<int>();
                        for (int i = 0; i < _system.Bodies.Count; i++) { var b = _system.Bodies[i]; if (b.Parent < 0 && b.Giant) opts.Add(i); }
                        double total = 0; var wts = new double[opts.Count];
                        for (int q = 0; q < opts.Count; q++) { wts[q] = Math.Max(0.01, _system.Bodies[opts[q]].Interest); total += wts[q]; }
                        double pickAt = rng.NextDouble() * total; int pick = opts.Count > 0 ? opts[opts.Count - 1] : -1;
                        for (int q = 0; q < opts.Count; q++) { pickAt -= wts[q]; if (pickAt <= 0) { pick = opts[q]; break; } }
                        if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; } else ZoomLog("TOUR no giant among the " + _system.Bodies.Count + " bodies of this system");
                    }
                    break;
                case "neutron":
                    {
                        // the nearest neutron star, pulsar or magnetar among the bodies round you (placed among the stars, or left at a
                        // supernova's middle): a body like any other, picked and dived into
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < _extras.Count; q++)
                        {
                            var ex = _extras[q]; if (ex.Pl == null) continue; string id = ex.Pl.Id;
                            if (!(id.Contains("neutron") || id.Contains("pulsar") || id.Contains("magnetar"))) continue;
                            double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { var ex = _extras[bi]; _tKind = 6; _tIndex = bi; _tx = ex.X; _ty = ex.Y; _tz = ex.Z; _tRadius = ex.R; _tName = ex.Name; _tExtra = ex; _tSpot = null; }
                        else ZoomLog("TOUR no neutron star among the " + _extras.Count + " bodies round you");
                    }
                    break;
                case "nebula":
                    if (_near >= 0 && _near < _galaxies.Count && _features != null)
                    {
                        // the nearest nebula of the galaxy you are in (emission, reflection, planetary, a supernova's remnant or a dark one)
                        var g = _galaxies[_near]; string[] names = { "a globular cluster", "an open cluster", "an emission nebula", "a reflection nebula", "a planetary nebula", "a supernova remnant", "a dark nebula" };
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < _features.Count; q++)
                        {
                            var fq = _features[q]; int fk = (int)fq[4]; if (fk < 2 || fk > 6) continue;
                            double ux, uy, uz; ToUniverse(g, fq[0], fq[1], fq[2], out ux, out uy, out uz);
                            double dx = (ux - _ax) - _ox, dy = (uy - _ay) - _oy, dz = (uz - _az) - _oz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = q; }
                        }
                        if (bi >= 0) { double ux, uy, uz; ToUniverse(g, _features[bi][0], _features[bi][1], _features[bi][2], out ux, out uy, out uz); _tGal = _near; _tKind = 5; _tIndex = bi; _tx = ux; _ty = uy; _tz = uz; _tRadius = _features[bi][3] * g.Scale; int fk = (int)_features[bi][4]; _tName = fk >= 0 && fk < names.Length ? names[fk] : "a nebula"; }
                        else ZoomLog("TOUR no nebula among the " + _features.Count + " features of " + g.Name);
                    }
                    break;
                case "start-galaxy":
                    {
                        // the galaxy you begin in (a galaxy of this universe, like any other), to open on
                        int bi = -1; double bd = double.MaxValue;
                        for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = dx * dx + dy * dy + dz * dz; if (d < bd) { bd = d; bi = i; } }
                        if (_near >= 0 && _near < _galaxies.Count) bi = _near;                // (the one you are in, whatever else is made round it)
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; }
                    }
                    break;
                case "far-galaxy":
                    if (st != null && st.FarLo > 0 && FarGalaxy(st)) break;                // (a true crossing: billions of light-years, to a galaxy made ahead from its own region)
                    {
                        // across the universe: the farthest galaxy made round you that lies ahead (in view), never a near one
                        int bi = -1; double bs = double.MinValue; var lk = Look; var rng = new Rng(_seed ^ (ulong)Environment.TickCount);
                        for (int i = 0; i < _galaxies.Count; i++)
                        {
                            var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (d < 3 || d > _nearUnits * 1.7) continue;
                            double ahead = (dx * lk.X + dy * lk.Y + dz * lk.Z) / d; if (ahead < 0.5) continue;
                            double score = d * (0.85 + 0.3 * rng.NextDouble()) * (g.Type == _lastGalaxyType ? 0.6 : 1);   // (another kind than the last, where it can)
                            if (HeartFeeds(g)) score += 1e6;                                       // (one whose heart is feeding: the tour flies by its disc; a quiet one only if none in view feeds)
                            if (score > bs) { bs = score; bi = i; }
                        }
                        if (bi >= 0 && !HeartFeeds(_galaxies[bi])) { int fb = -1; double fd = double.MaxValue; for (int i = 0; i < _galaxies.Count; i++) { var g = _galaxies[i]; if (!HeartFeeds(g)) continue; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = dx * dx + dy * dy + dz * dz; if (d > 9 && d < fd) { fd = d; fb = i; } } if (fb >= 0) bi = fb; }   // (none feeding ahead: the nearest feeding one anywhere round you, before a quiet one)
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; _lastGalaxyType = _galaxies[bi].Type; break; }
                    }
                    goto case "galaxy";                                                    // (none far ahead yet: one some way off)
                case "galaxy":
                case "next-galaxy":
                    {
                        // exploring: a galaxy chosen afresh each time (any seed, any run), some way off rather than the nearest,
                        // ahead of you where it can be, never the one just visited
                        var rng = new Rng(_seed ^ (ulong)(_tourAt * 7919 + Environment.TickCount));
                        int bi = -1; double bs = double.MinValue; int skip = _tKind == 0 ? _tIndex : -1; var lk = Look;
                        for (int i = 0; i < _galaxies.Count; i++)
                        {
                            if (i == skip) continue; var g = _galaxies[i]; double dx = g.X - _cx, dy = g.Y - _cy, dz = g.Z - _cz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (d < 3 || d > _nearUnits * 1.4) continue;
                            double ahead = (dx * lk.X + dy * lk.Y + dz * lk.Z) / d, score = ahead + rng.NextDouble() * 1.5 + Math.Min(1, d / 60);
                            if (score > bs) { bs = score; bi = i; }
                        }
                        if (bi >= 0) { _tKind = 0; _tIndex = bi; _tName = _galaxies[bi].Name; }
                    }
                    break;
                case "star":
                    {
                        // exploring: one of the stars round you, chosen afresh, not simply the nearest
                        var rng = new Rng(_seed ^ (ulong)(_tourAt * 104729 + Environment.TickCount));
                        if (_localCount > 0)
                        {
                            int k = rng.Below(_localCount); double ux, uy, uz; StarPos(1000000 + k, out ux, out uy, out uz);
                            _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star";
                        }
                        else { double d; int k = NearestStar(out d); if (k >= 0) { double ux, uy, uz; StarPos(k, out ux, out uy, out uz); _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star"; } }
                        if (_tKind != 1 && _near >= 0 && _nearLocal != null && _nearLocal.Length > 0)
                        {
                            // still among the galaxy's far stars (the ones close round you not made yet): one of the stars you can
                            // see and click, the same ones a click would take
                            var s0 = _nearLocal[rng.Below(_nearLocal.Length)]; double ux, uy, uz; ToUniverse(_galaxies[_near], s0.X, s0.Y, s0.Z, out ux, out uy, out uz);
                            _tKind = 1; _tx = ux; _ty = uy; _tz = uz; _tRadius = Units.Km(696000); _tName = "a star";
                        }
                    }
                    break;
                case "next-planet":
                    if (_system != null)
                    {
                        // the next of the star's planets in turn (for a fly-by of each)
                        int start = _tKind == 2 ? _tIndex : -1;
                        for (int q = 1; q <= _system.Bodies.Count; q++) { int i = (start + q + _system.Bodies.Count) % _system.Bodies.Count; if (_system.Bodies[i].Parent < 0) { _tKind = 2; _tIndex = i; _tName = _system.Bodies[i].Kind; break; } }
                    }
                    break;
                case "land":
                    if (_ground != null && _ground.Solid)
                    {
                        // a spot of dry land to come down on, the nearest to under you (the seas are not dived into yet): rings of
                        // directions round the point under you, wider and wider, out to the far side if need be
                        double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); if (r <= 0) break;
                        double ux = x / r, uy = y / r, uz = z / r;
                        Func<double, double, double, bool> dry = (vx, vy, vz) => !_ground.HasWater || _ground.GroundAt(vx, vy, vz) > 0.02;
                        if (dry(ux, uy, uz)) { _tKind = 3; _tx = ux; _ty = uy; _tz = uz; _tName = "land"; break; }
                        double ex = Math.Abs(uy) < 0.9 ? 0 : 1, ey = Math.Abs(uy) < 0.9 ? 1 : 0, ez = 0;                     // a frame round the point under you
                        double e1x = ey * uz - ez * uy, e1y = ez * ux - ex * uz, e1z = ex * uy - ey * ux, e1l = Math.Sqrt(e1x * e1x + e1y * e1y + e1z * e1z); e1x /= e1l; e1y /= e1l; e1z /= e1l;
                        double e2x = uy * e1z - uz * e1y, e2y = uz * e1x - ux * e1z, e2z = ux * e1y - uy * e1x;
                        bool found = false;
                        for (double ang = 0.01; ang < Math.PI && !found; ang *= 1.4)
                            for (int q = 0; q < 16 && !found; q++)
                            {
                                double th = q * Math.PI * 2 / 16 + ang, ca = Math.Cos(ang), sa = Math.Sin(ang), ct = Math.Cos(th), st2 = Math.Sin(th);
                                double vx = ux * ca + (e1x * ct + e2x * st2) * sa, vy = uy * ca + (e1y * ct + e2y * st2) * sa, vz = uz * ca + (e1z * ct + e2z * st2) * sa;
                                if (dry(vx, vy, vz)) { _tKind = 3; _tx = vx; _ty = vy; _tz = vz; _tName = "land"; found = true; }
                            }
                    }
                    break;
                case "planet":
                    if (_system != null)
                    {
                        // exploring: one of the star's worlds, chosen afresh (a solid one where there is one, to go down to; never a
                        // world that is all sea, until the dive under the sea is built)
                        var rng = new Rng(_seed ^ (ulong)(Environment.TickCount + 31)); var opts = new List<int>();
                        for (int i = 0; i < _system.Bodies.Count; i++)
                        {
                            var b = _system.Bodies[i]; if (b.Parent >= 0 || b.Giant) continue;
                            var tk = b.Terrain != null ? PlanetTerrain.TerrainKind(b.Terrain) : null;
                            if (tk != null && tk.Water && tk.LandShare <= 0) continue;
                            opts.Add(i);
                        }
                        if (opts.Count == 0) for (int i = 0; i < _system.Bodies.Count; i++) if (_system.Bodies[i].Parent < 0) opts.Add(i);
                        // the director's eye: a world with seas and air most of all, then any solid world, by chance among them
                        double total = 0; var wts = new double[opts.Count];
                        for (int q = 0; q < opts.Count; q++) { wts[q] = _system.Bodies[opts[q]].Interest; total += wts[q]; }   // (each kind's interest, from its template)
                        double pickAt = rng.NextDouble() * total; int pick = opts.Count > 0 ? opts[opts.Count - 1] : -1;
                        for (int q = 0; q < opts.Count; q++) { pickAt -= wts[q]; if (pickAt <= 0) { pick = opts[q]; break; } }
                        if (pick >= 0) { _tKind = 2; _tIndex = pick; _tName = _system.Bodies[pick].Kind; }
                    }
                    break;
                default: _matter.PickNearest(what); break;                                     // a grain, a spot on its face, an atom, electron, nucleus, proton, quark
            }
        }
        /// <summary>True when a planet's ground is being drawn (what the GPU use tunes, so far).</summary>
        public bool AtGround { get { return _ground != null; } }
        /// <summary>How wide the view is where you are looking (km): the distance to the nearest thing.</summary>
        public double ViewKm { get { return ViewDistanceKm(); } }
        /// <summary>The scale in the matter (km), or 0 above it: for the test runs, so a wheel can end on the last landmark.</summary>
        public double MatterKm { get { return _matterScale > 0 ? _matterScale : _spaceScale > 0 ? _spaceScale : 0; } }
        /// <summary>How many ground pieces are waiting (0 when not at a planet).</summary>
        public int GroundBacklog { get { return _ground != null ? _ground.Backlog : 0; } } private double _autoLogT; private DateTime _wheelLogAt;
        // the zoom log's wheel line twice a second at most (a line a frame filled the file in a minute and lost what came before a crash)
        private bool WheelLogDue() { if ((DateTime.Now - _wheelLogAt).TotalSeconds < 0.5) return false; _wheelLogAt = DateTime.Now; return true; }
    }
}
