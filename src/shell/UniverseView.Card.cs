// Universe · shell · the universe view, its part: card (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── the information card of what you picked: what it is, its id, and the facts that matter about it ──
        private static string Au(double units) { double au = units / Units.Au(1); return au >= 10 ? au.ToString("0") + " AU" : au.ToString("0.##") + " AU"; }
        private static string Km(double units) { double km = units / Units.Km(1); return km >= 10 ? km.ToString("N0") + " km" : km.ToString("0.##") + " km"; }

        // what a kind of planet or moon is made of
        private static string[] MakeUp(SpaceGen.Body b, bool water)
        {
            // from its kind's template (content/terrain.unt makeup, and makeup-dry for one with no seas); a moon is rock and dust
            if (b.MakeUp != null) return !water && b.MakeUpDry != null ? b.MakeUpDry : b.MakeUp;
            if (b.Parent >= 0) return new[] { "rock and dust, iron-poor", "no air", "craters from strikes" };
            return new[] { "rock: silicate crust, iron core", "dust and stone", "air: thin carbon dioxide" };
        }

        // a short log of each turn of the wheel (where you are and why the step is what it is), in the temp folder, to find
        // where a zoom stops; kept small (started over past about 200 KB)
        // for the log: a camera jump of over a million km in one step of the update says which step did it
        private double _mx, _my, _mz;
        private void Mark() { _mx = _cx; _my = _cy; _mz = _cz; }
        private void Jumped(string step)
        {
            double dx = _cx - _mx, dy = _cy - _my, dz = _cz - _mz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > Units.Km(1e6) || double.IsNaN(d)) ZoomLog("JUMP " + Units.Describe(d) + " in " + step);
            Mark();
        }

        private void ZoomLog(string what)
        {
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), PreviewText.AppName.ToLowerInvariant().Replace("-", "") + "_zoom.log");   // (the program's own name: universe_zoom.log)
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 4000000) System.IO.File.Delete(f);   // (a whole tour fits: it is the record of what the tour did)
                System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.f") + "  " + what + "  |  " + Title.Replace(PreviewText.AppName + " · ", "").Split(new[] { " · wheel:" }, StringSplitOptions.None)[0] + Environment.NewLine);
            }
            catch { }
        }

        // the light on the ground: by day the star's own colour (from its temperature) through the air, which takes a little of
        // its own colour out of the direct light the thicker it is (a blue sky leaves the light warmer); by night half as bright,
        // in the colour of the air's glow and scattered starlight (airless worlds: the starlight alone)
        private void GroundLight()
        {
            float sr = _system != null ? _system.SR : 1, sg = _system != null ? _system.SG : 1, sb = _system != null ? _system.SB : 1, m = Math.Max(1e-3f, Math.Max(sr, Math.Max(sg, sb)));   // (a rogue body with no star of its own: lit white, by the galaxy's light)
            sr /= m; sg /= m; sb /= m;
            double air = _ground.AirKm > 0 ? Math.Min(1, _ground.AirKm / 100.0) : 0;
            float ar = _ground.AirR, ag = _ground.AirG, ab = _ground.AirB;
            float dr = sr * (float)(1 - 0.3 * air * ag * ab), dg = sg * (float)(1 - 0.3 * air * ar * ab), db = sb * (float)(1 - 0.3 * air * ar * ag);
            float dm = Math.Max(1e-3f, Math.Max(dr, Math.Max(dg, db))); dr /= dm; dg /= dm; db /= dm;
            float nr = (float)(sr * (1 - air * 0.6) + ar * air * 0.6), ng = (float)(sg * (1 - air * 0.6) + ag * air * 0.6), nb = (float)(sb * (1 - air * 0.6) + ab * air * 0.6);
            float nm = Math.Max(1e-3f, Math.Max(nr, Math.Max(ng, nb)));
            if (_ground.SunColour == null) { _ground.SunColour = new float[3]; _ground.NightColour = new float[3]; }
            _ground.SunColour[0] = dr; _ground.SunColour[1] = dg; _ground.SunColour[2] = db;
            _ground.NightColour[0] = nr / nm * 0.5f; _ground.NightColour[1] = ng / nm * 0.5f; _ground.NightColour[2] = nb / nm * 0.5f;
        }

        // the matter lies under the planet's own up and down: the view turned into that frame (up = away from the planet's
        // middle), and the starlight with it
        private double[] _bodyUp, _bodyStar; private int _bodyUpGen = -1;   // (the dive the frame was made for: any later dive, a gas, a plasma, a world, sets the matter afresh and the frame is no longer its)                                              // on a small body's ground: its up (from its middle to you) and the way to its star
        private Mat4 MatterFrame(Vec3 look)
        {
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z);
            return MatterFrameFor(new Vec3((float)(x / r), (float)(y / r), (float)(z / r)), new Vec3((float)_ground.StarDir[0], (float)_ground.StarDir[1], (float)_ground.StarDir[2]), look);
        }
        // the matter's frame on any ground (a world's, a small body's): up from the middle to you, the starlight from sd
        private Mat4 MatterFrameFor(Vec3 upR, Vec3 sd, Vec3 look)
        {
            var east0 = Vec3.Cross(Math.Abs(upR.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upR).Normalized; var north0 = Vec3.Cross(upR, east0).Normalized;
            var upL = upR;                                                              // (the grains themselves lie along the land's slope)
            var east = Vec3.Cross(Math.Abs(upL.Y) < 0.99f ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0), upL).Normalized; var north = Vec3.Cross(upL, east).Normalized;
            var lk = new Vec3(Vec3.Dot(look, east), Vec3.Dot(look, upL), Vec3.Dot(look, north));
            // in the matter you look down onto the grain's face (it fills the whole view, never its edge and the dark past it); over
            // the ground, never from below it
            float lowest = _matterScale > 0 || _spaceScale > 0 ? -0.7f : -0.15f;
            if (lk.Y > lowest) { float hl = (float)Math.Sqrt(lk.X * lk.X + lk.Z * lk.Z); float keep = (float)Math.Sqrt(1 - lowest * lowest) / Math.Max(1e-6f, hl); lk = hl > 1e-6f ? new Vec3(lk.X * keep, lowest, lk.Z * keep) : new Vec3(0, -1, 0); }
            _matter.Light[0] = Vec3.Dot(sd, east); _matter.Light[1] = Vec3.Dot(sd, upL); _matter.Light[2] = Vec3.Dot(sd, north);
            var lkn = lk.Normalized;
            if (_matterScale > 0 || _spaceScale > 0) _matter.SetEye(lkn.X, lkn.Y, lkn.Z); else _matter.SetEye(0, -1, 0);   // in the matter your eye goes round the point you dive into
            // the camera's own up, carried into the ground's frame (never switched when looking straight down: no flip, no judder)
            var cu = ViewUp(); var upF = new Vec3(Vec3.Dot(cu, east), Vec3.Dot(cu, upL), Vec3.Dot(cu, north));
            float ud = Vec3.Dot(upF, lkn); upF = new Vec3(upF.X - lkn.X * ud, upF.Y - lkn.Y * ud, upF.Z - lkn.Z * ud);
            if (upF.Length < 1e-3f) upF = Math.Abs(lkn.Y) < 0.9f ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1);
            return Mat4.LookAt(new Vec3(0, 0, 0), lkn, upF.Normalized);
        }

        private List<string> Card()
        {
            var L = new List<string>();
            double dx, dy, dz, rad; if (!TargetFromCamera(out dx, out dy, out dz, out rad)) return L;
            double dist = Math.Max(0, Math.Sqrt(dx * dx + dy * dy + dz * dz) - rad);
            if (_tKind == 0)
            {
                var g = _galaxies[_tIndex];
                L.Add(g.Name); L.Add("id: G-" + (g.Seed & 0xFFFFFF).ToString("X6"));
                L.Add("type: " + g.Type); L.Add("width: about " + Units.Describe(g.Scale));
                L.Add("stars: about 100 billion"); L.Add("distance: " + Units.Describe(dist)); ExpansionLines(L, dist);
                { var sc = Supernovae(g, _tIndex == _near); int going = 0; foreach (var sn in sc.Events) { double yrs = _simYears - sn.T; if (yrs >= 0 && yrs * 365.25 < 150) going++; } L.Add("supernovae: about " + (SnRate(g) * 100).ToString("0.#") + " a century here" + (going > 0 ? "; " + going + " going now" : "; none going now")); }
                if (_darkMatter > 0) { L.Add("mass: about " + (1e11 / (1 - _darkMatter)).ToString("0.#E+0") + " solar masses, " + (_darkMatter * 100).ToString("0") + "% of it dark matter"); L.Add("its stars orbit at a flat " + _rimSpeed.ToString("0") + " km/s out to its rim, held by the dark halo (without it they would slow outward like planets)"); }
            }
            else if (_tKind == 1 && _tSn != null && _tName == SnName(_tSn)) SupernovaCard(L, dist);
            else if (SwarmPicked) SwarmCard(L, dist);
            else if (PartPicked) HolePartCard(L, dist);
            else if (_tKind == 1)
            {
                bool open = _system != null && Math.Abs(_tx - _ax) + Math.Abs(_ty - _ay) + Math.Abs(_tz - _az) < Units.Km(1e6);
                L.Add(_tName.StartsWith("a ") ? _tName.Substring(2) : _tName);
                L.Add("id: S-" + (((long)(_tx / Units.Km(1e9)) * 73856093L ^ (long)(_ty / Units.Km(1e9)) * 19349663L ^ (long)(_tz / Units.Km(1e9)) * 83492791L) & 0xFFFFFF).ToString("X6"));
                {
                    // what it is made of, by count of atoms, its own share of the heavier elements (content/stars.unt makeup)
                    var ke = StarKindEntry(_tName.StartsWith("a ") ? _tName.Substring(2) : _tName);
                    if (ke != null) { double metals; string mk = StarMakeUp(ke, _tx, _ty, _tz, out metals); if (mk != null) { var mp = mk.Split(','); L.Add("made of (by atoms): " + string.Join(",", mp, 0, Math.Min(6, mp.Length)) + (mp.Length > 6 ? ", and " + (mp.Length - 6) + " more" : "")); if (ke.Get("makeup", "").StartsWith("hydrogen")) L.Add("past helium " + metals.ToString("0.##") + " times its kind's share; its colour and heat from its mass, not its make-up"); } if (ke.Props.ContainsKey("makeup-note")) L.Add(ke.Get("makeup-note", "")); }
                }
                if (open)
                {
                    var sy = _system;
                    L.Add("surface: " + sy.TempK.ToString("N0") + " K"); L.Add("mass: " + sy.MassSol.ToString("0.##") + " solar masses, width " + sy.RadiusSol.ToString("0.##") + " solar radii");
                    L.Add("brightness: " + sy.Luminosity.ToString(sy.Luminosity < 0.1 ? "0.###" : "0.##") + " solar luminosities");
                    {
                        // its light climbing out of its own well (gravitational redshift, GM/Rc^2 for a star), and its layers from the surface to the core
                        double gmS = Physics.GmSol * sy.MassSol, RS = Math.Max(1, sy.StarRadius / Units.Km(1) * 1000), zS = 1 / Math.Sqrt(Math.Max(1e-30, 1 - 2 * gmS / (RS * Physics.C * Physics.C))) - 1;
                        L.Add("relativity: its light redshifted " + ShiftWords(zS) + " climbing out; surface clocks slower by the same");
                        var lays = StarLayersFor(SystemStarId()); if (lays.Count > 0) { var nm = new List<string>(); foreach (var l in lays) nm.Add(l.Name.ToLowerInvariant()); L.Add("layers: " + string.Join(", ", nm.ToArray())); }
                        var sl = StarLayerHere(); if (sl != null) { double sdK = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz) / Units.Km(1), RK = StarRadiusNow / Units.Km(1); L.Add("inside: " + ScaleHud.Words(RK - sdK) + " down, its " + sl.Name.ToLowerInvariant() + ", about " + LayerTempK(sl, sdK / RK).ToString("N0") + " K, " + DensityWords(LayerDensity(sl, sdK / RK))); }
                        if (Flown) L.Add((Window.GravityOn ? "gravity back on, after it was off " : "gravity off ") + Years(Math.Max(0, OffYears)) + ": " + (StarSwell >= 100 ? StarSwell.ToString("N0") : StarSwell.ToString("0.#")) + " times its size, growing " + (StarSwellSpeedKmS()).ToString("N0") + " km/s");
                    }
                    L.Add("planets and moons: " + sy.Bodies.Count + " (" + sy.Planets + " planets)");
                    L.Add("habitable zone: " + (sy.HabitableAu * 0.75).ToString("0.##") + " to " + (sy.HabitableAu * 1.4).ToString("0.##") + " AU");
                    L.Add("frost line: " + sy.FrostLineAu.ToString("0.##") + " AU");
                    { string sc = ScorchWords(_tx, _ty, _tz); if (sc != null) L.Add("scorched: " + sc); }
                }
                else L.Add("its system opens within 2,000 AU");
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 2)
            {
                var sy = _system; var b = sy.Bodies[_tIndex];
                int moons = 0; for (int i = 0; i < sy.Bodies.Count; i++) if (sy.Bodies[i].Parent == _tIndex) moons++;
                L.Add(b.Kind + (b.Parent >= 0 ? " of " + sy.Bodies[b.Parent].Kind : ""));
                L.Add("id: S-" + _sysStar.ToString("X") + (b.Parent >= 0 ? "-P" + (b.Parent + 1) + "-M" : "-P") + (_tIndex + 1));
                L.Add("width: " + Km(b.RadiusUnits * 2));
                {
                    // its mass (from its size and its kind's density), its pull at its ground and the speed to get away from it
                    double rwM = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), gmw = Physics.G * b.MassKg, gS = gmw / (rwM * rwM), vE = Math.Sqrt(2 * gmw / rwM);
                    L.Add("mass: " + MassWords(b.MassKg));
                    L.Add("ground pull " + gS.ToString("0.##") + " m/s2 (" + (gS / Physics.EarthG).ToString("0.##") + " of Earth's); escape " + (vE >= 1000 ? (vE / 1000).ToString("0.#") + " km/s" : vE.ToString("0") + " m/s"));
                }
                var top = b.Parent >= 0 ? sy.Bodies[b.Parent] : b;
                if (b.Parent < 0)
                {
                    double au = b.OrbitUnits / Units.Au(1);
                    L.Add("orbit: " + Au(b.OrbitUnits) + ", year " + (b.Period >= 1 ? b.Period.ToString("0.#") + " years" : (b.Period * 365.25).ToString("0") + " days"));
                    L.Add("its orbit: " + (b.Ecc < 0.05 ? "near round" : b.Ecc < 0.25 ? "oval" : "a long ellipse") + " (eccentricity " + b.Ecc.ToString("0.00") + "), from " + Au(b.OrbitUnits * (1 - b.Ecc)) + " to " + Au(b.OrbitUnits * (1 + b.Ecc)));
                    if (_tIndex + 1 < _bodyCount)
                    {
                        // its speed, by the laws of motion (vis-viva): now, from how far it is from its star this moment, and at its
                        // closest point; and relativity: its closest point turning round the star, and clocks slowed by the star's pull
                        double rx = _bodyVerts[_tIndex + 1].X - _bodyVerts[0].X, ry = _bodyVerts[_tIndex + 1].Y - _bodyVerts[0].Y, rz = _bodyVerts[_tIndex + 1].Z - _bodyVerts[0].Z;
                        double rM = Math.Max(1, Math.Sqrt(rx * rx + ry * ry + rz * rz) / Units.Km(1) * 1000), aM = b.OrbitUnits / Units.Km(1) * 1000, gm = Physics.GmSol * sy.MassSol;
                        double vNow = Math.Sqrt(Math.Max(0, gm * (2 / rM - 1 / aM))), vPeri = Math.Sqrt(gm * (1 + b.Ecc) / (aM * (1 - b.Ecc))), vApo = Math.Sqrt(gm * (1 - b.Ecc) / (aM * (1 + b.Ecc)));
                        L.Add("speed now: " + (vNow / 1000).ToString("0.#") + " km/s; " + (vPeri / 1000).ToString("0.#") + " at its closest, " + (vApo / 1000).ToString("0.#") + " at its farthest");
                        double arcsecCentury = b.PeriRate * (100 / Math.Max(1e-9, b.Period)) * 206264.8, slow = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gm / (rM * Physics.C * Physics.C)));
                        L.Add("relativity: its closest point turns " + (arcsecCentury < 3600 ? arcsecCentury.ToString("0.#") + " arcseconds" : (arcsecCentury / 3600).ToString("0.#") + " degrees") + " a century; clocks here run slower by 1 part in " + (slow > 0 ? (1 / slow).ToString("0.##E+0") : "(none)"));
                        {
                            // its own pull on clocks: slower on its ground than far away, and faster higher up, so a satellite's clock runs
                            // ahead of the ground's by so many microseconds a day (the correction every satellite clock carries)
                            double gmw = WorldGm(b), rw = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), c2 = Physics.C * Physics.C;
                            double sGround = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gmw / (rw * c2))), sSat = 1 - Math.Sqrt(Math.Max(0, 1 - 2 * gmw / (4.2 * rw * c2)));
                            double usDay = sGround * 86400e6, gain = (sGround - sSat) * 86400e6;
                            L.Add("its pull: ground clocks " + (usDay >= 10 ? usDay.ToString("0") : usDay.ToString("0.##")) + " us a day slow; a satellite 4 radii up " + (gain >= 10 ? gain.ToString("0") : gain.ToString("0.##")) + " us a day faster");
                        }
                        {
                            // the rest of it: its light climbing out of its own well and its star's reaches far away redshifted; its light swinging
                            // blue and red as it comes and goes round its star (Doppler); at its speed its length along its path shorter (special relativity)
                            double gmw = WorldGm(b), rw = Math.Max(1, b.RadiusUnits / Units.Km(1) * 1000), c2 = Physics.C * Physics.C;
                            double z = 1 / Math.Sqrt(Math.Max(1e-30, 1 - 2 * gm / (rM * c2) - 2 * gmw / (rw * c2))) - 1, dop = vNow / Physics.C, gam = 1 / Math.Sqrt(Math.Max(1e-30, 1 - dop * dop)) - 1;
                            L.Add("redshift " + ShiftWords(z) + "; Doppler swing " + ShiftWords(dop) + "; contraction " + ShiftWords(gam));
                        }
                    }
                    L.Add(au >= sy.HabitableAu * 0.75 && au <= sy.HabitableAu * 1.4 ? "in the habitable zone" : au < sy.HabitableAu * 0.75 ? "inside the habitable zone: too hot" : "outside the habitable zone: too cold");
                    L.Add("moons: " + moons);
                    if (b.Locked) L.Add("tidally locked: one face always toward its star, its day as long as its year");
                }
                else L.Add("orbit: " + Km(b.OrbitUnits) + " from its planet" + (b.Ecc >= 0.05 ? ", " + (b.Ecc < 0.25 ? "oval" : "a long ellipse") + " (eccentricity " + b.Ecc.ToString("0.00") + ")" : ""));
                bool here = _ground != null && _planet == _tIndex;
                foreach (var m in MakeUp(b, here ? _ground.HasWater : PlanetTerrain.TerrainKind(b.Terrain).Water)) L.Add(m);
                WorldGravityLines(L, b, here ? _ground.HasWater : PlanetTerrain.TerrainKind(b.Terrain).Water);
                if (!b.Giant) { ClimateLines(L, _tIndex); AirLines(L, _tIndex); if (here) InsideAtWorkLines(L); }   // (its temperatures, its air by height, and at the world you are at, its inside at work)                                       // (its temperatures, from its star, its orbit and its air)   // (gravity off: what it does to this world)
                if (!b.Giant) foreach (var m in InsideWords(b.Terrain ?? DeepView.TerrainKind(b.Kind), null, WorldSeed(_tIndex) + 1UL)) L.Add(m);   // (its layers to its middle, and its whole make-up by element)
                { string sc = ScorchWords(_ax, _ay, _az); if (sc != null) L.Add("scorched: " + sc); }
                if (here) { L.Add("air: " + (_ground.AirKm > 0 ? "about " + _ground.AirKm.ToString("0") + " km deep" : "none") + (_ground.HasClouds ? ", clouds" : "")); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 5 && _tIndex < 0)
            {
                L.Add("a point between the stars of " + (_near >= 0 ? _galaxies[_near].Name : "the galaxy"));
                L.Add("the gas here: about 1 atom a cubic centimetre");
                L.Add("mostly hydrogen, some helium, a little dust");
                L.Add("the nearest star: " + Units.Describe(NearestDistance()) + " away");
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 5 && _tGal >= 0 && _galaxies[_tGal].Features != null && _tIndex < _galaxies[_tGal].Features.Count)
            {
                int fk = (int)_galaxies[_tGal].Features[_tIndex][4]; double ly = _tRadius * 2 / Units.Km(9.4607e12);
                L.Add(_tName.Substring(_tName.IndexOf(' ') + 1, 1).ToUpperInvariant() + _tName.Substring(_tName.IndexOf(' ') + 2));
                L.Add("id: " + (fk < 2 ? "C-" : "N-") + (_galaxies[_tGal].Seed & 0xFFF).ToString("X3") + "-" + (_tIndex + 1)); L.Add("in " + _galaxies[_tGal].Name);
                L.Add("width: about " + ly.ToString("N0") + " light-years");
                var ftg = _galaxies[_tGal].Features[_tIndex];
                if (fk >= 2 && ftg.Length > 5)
                {
                    // what it is made of, its largest parts
                    var parts = SpaceGen.Composition(fk, ftg[5]); Array.Sort(parts, (a, b) => b.Share.CompareTo(a.Share));
                    for (int q = 0; q < Math.Min(parts.Length, fk == 6 ? 6 : 4); q++) L.Add((parts[q].Share * 100).ToString(parts[q].Share < 0.01 ? "0.0" : "0") + "% " + parts[q].Name);
                }
                if (fk == 0) { L.Add("hundreds of thousands of stars"); L.Add("ancient: among the oldest stars of the galaxy"); L.Add("in the halo, round the galaxy's middle"); }
                else if (fk == 1) { L.Add("a few hundred young stars, born together"); L.Add("in the arms, drifting apart over time"); }
                else if (fk == 2) { L.Add("glowing hydrogen, lit by hot young stars"); L.Add("where new stars are being born"); }
                else if (fk == 3) { L.Add("dust lit blue by nearby stars' light"); L.Add("it shines by reflection, not its own glow"); }
                else if (fk == 4) { L.Add("a shell of gas puffed off by a dying star"); L.Add("the star's hot core, a white dwarf, lights it"); L.Add("it fades in some tens of thousands of years"); }
                else if (fk == 5) { L.Add("the expanding shell of an exploded star"); L.Add("hot gas, still racing outward"); L.Add("it seeds space with heavy elements"); }
                else { L.Add("a cold cloud of dust and molecules"); L.Add("dark: it hides the stars behind it"); L.Add("stars are being born deep inside"); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 6 && _tIndex >= 0 && _tIndex < _extras.Count)
            {
                var exk = _extras[_tIndex]; foreach (var ln in exk.Lines) L.Add(ln);
                L.Add("distance: " + Units.Describe(dist));
                double massSol = exk.Mass > 0 ? exk.Mass : exk.Density > 0 && exk.R > 0 ? SpaceGen.MassOf(exk.R, exk.Density) / 1.989e30 : 0;   // (a small body's mass from its size and its density)
                if (massSol > 0)
                {
                    if (exk.Mass <= 0) L.Add("mass: " + (massSol * 1.989e30).ToString("0.##E+0") + " kg");
                    // its pull where you are, falling with the square of the distance; the speed you would need to get away
                    double rM = Math.Max(1, (dist + exk.R) / Units.Km(1) * 1000);                // metres
                    double g = Physics.Pull(massSol, rM), vesc = Physics.Escape(massSol, rM);     // (Newton's gravity, content/physics.unt)
                    L.Add("its pull here: " + (g >= 0.01 ? g.ToString("0.###") : g.ToString("0.##E+0")) + " m/s2 (" + (g / Physics.EarthG).ToString(g / Physics.EarthG >= 0.01 ? "0.###" : "0.##E+0") + " of Earth's)");
                    L.Add("escape speed here: " + (vesc >= Physics.C ? "faster than light: inside, nothing gets out" : vesc >= 1000 ? (vesc / 1000).ToString("N0") + " km/s (" + (vesc / Physics.C * 100).ToString("0.###") + "% of light)" : vesc.ToString("0.#") + " m/s"));
                    { double gmx = Physics.GmSol * massSol, q = 1 - 2 * gmx / (rM * Physics.C * Physics.C); L.Add("relativity: clocks here run " + (q <= 0 ? "to a stop at its horizon" : "slower by " + ((1 - Math.Sqrt(q)) * 100 >= 0.01 ? ((1 - Math.Sqrt(q)) * 100).ToString("0.##") + "%" : "1 part in " + (1 / (1 - Math.Sqrt(q))).ToString("0.##E+0")))); }
                    if (exk.Hole) { double gmx2 = Physics.GmSol * massSol, q2 = 1 - 2 * gmx2 / (rM * Physics.C * Physics.C); L.Add(q2 <= 0 ? "redshift from here: without limit at the horizon" : "redshift from here: " + ShiftWords(1 / Math.Sqrt(q2) - 1)); }
                }
            }
            else if (_tKind == 4 && _system != null)
            {
                var sy = _system; bool ast = _tName == "the asteroid belt", kui = _tName == "the Kuiper belt";
                L.Add(_tName.Substring(4, 1).ToUpperInvariant() + _tName.Substring(5) + " of this star");
                if (ast) { L.Add("from " + sy.BeltInAu.ToString("0.##") + " to " + sy.BeltOutAu.ToString("0.##") + " AU"); L.Add("rocky and metal bodies, dust to dwarf planets"); L.Add("left where a giant's pull kept a planet from forming"); }
                else if (kui) { L.Add("from " + sy.KuiperInAu.ToString("0.#") + " to " + sy.KuiperOutAu.ToString("0.#") + " AU"); L.Add("icy bodies: frozen water, methane, ammonia"); L.Add("where short-period comets come from"); }
                else { L.Add("from about " + sy.OortInAu.ToString("N0") + " to " + sy.OortOutAu.ToString("N0") + " AU, all round"); L.Add("a shell of icy bodies, billions of them"); L.Add("where long-period comets come from"); }
                L.Add("distance: " + Units.Describe(dist));
            }
            else if (_tKind == 3 && _ground != null)
            {
                double gh = _ground.Solid ? _ground.GroundAt(_tx, _ty, _tz) : 0;
                bool sea = _ground.HasWater && gh < 0, ice = Math.Abs(_ty) > 0.86;
                L.Add(sea ? "sea" : ice ? "ice" : "land"); 
                L.Add("on: " + GroundKind());
                L.Add("place: " + (Math.Asin(_ty) * 180 / Math.PI).ToString("0.#") + " lat, " + (Math.Atan2(_tz, _tx) * 180 / Math.PI).ToString("0.#") + " long");
                if (_planet >= 0 && _system != null && _planet < _system.Bodies.Count && !_system.Bodies[_planet].Giant) L.Add("the year's mean here about " + Kelvin(Climate.SurfaceK(WorldMeanK(_planet), Math.Asin(Math.Max(-1, Math.Min(1, _ty))), WorldKind(_planet).Tau > 0) - HeightCoolingK(_planet, Math.Max(0, gh))));   // (by its latitude: UniverseView.Climate.cs)
                L.Add(sea ? "depth: " + (-gh * 1000).ToString("N0") + " m of water" : "height: " + (gh * 1000).ToString("N0") + " m above the sea");
                if (sea && _planet >= 0) SeaLines(L, _planet, Math.Asin(Math.Max(-1, Math.Min(1, _ty))), -gh * 1000);   // (its tides and its current here: UniverseView.Climate.cs)
                L.Add(sea ? "water over sand and mud" : ice ? "ice and packed snow" : gh > 3 ? "bare rock and scree" : gh > 1 ? "rock and thin soil" : "soil, sand and stone");
                L.Add("distance: " + Units.Describe(dist));
            }
            { var w = HereWords(); if (w != null && L.Count > 0) L.Insert(1, w); }   // (where you are in the one tree, under its name)
            if (L.Count > 0) GravityLines(L);                                                 // (gravity off: what it is doing here)
            return L;
        }

        // gravitational time dilation, where you are: your own clock against the clocks far from every mass (the star, the world you
        // are at, the holes near you): dtau/dt = root(1 - sum 2GM/(r c^2)); slower the deeper in a well, stopped at a horizon against
        // far clocks, and past it no far clock sees you at all. The world's own clock (its days, its orbits) runs on far time
        private double ClockFactorHere()
        {
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return 0;
            double sum = 0, c2 = Physics.C * Physics.C;
            if (_system != null) { double d = Math.Max(1, DistToSysStar() / Units.Km(1) * 1000); sum += 2 * Physics.GmSol * _system.MassSol / (d * c2); }
            if (_system != null && _planet >= 0 && _planet < _system.Bodies.Count) { double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Max(1, Math.Sqrt(x * x + y * y + z * z) * 1000); sum += 2 * WorldGm(_system.Bodies[_planet]) / (r * c2); }
            foreach (var ex in _extras) { if (!ex.Hole || ex.Mass <= 0) continue; double dx = RelX(ex) - _ox, dy = RelY(ex) - _oy, dz = RelZ(ex) - _oz, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy + dz * dz) / Units.Km(1) * 1000); sum += 2 * Physics.GmSol * ex.Mass / (d * c2); }
            return Math.Sqrt(Math.Max(0, 1 - sum));
        }
        // a world's GM from its own mass (its size and its kind's density, content/terrain.unt density-g-cc)
        private static double WorldGm(SpaceGen.Body b) { return Physics.G * b.MassKg; }
        private static string MassWords(double kg) { double e = kg / 5.972e24; return (e >= 100 ? e.ToString("N0") : e >= 1 ? e.ToString("0.#") : e >= 0.01 ? e.ToString("0.##") : e.ToString("0.#E+0")) + " Earths (" + kg.ToString("0.##E+0") + " kg)"; }
        private static string TurnsWords(double perSec)
        {
            if (perSec >= 1) return (perSec >= 100 ? perSec.ToString("N0") : perSec.ToString("0.#")) + " times a second";
            if (perSec * 60 >= 1) return (perSec * 60).ToString("0.#") + " times a minute";
            if (perSec * 3600 >= 1) return (perSec * 3600).ToString("0.#") + " times an hour";
            return "once every " + (perSec * 86400 >= 1 ? (1 / (perSec * 3600)).ToString("0.#") + " hours" : (1 / (perSec * 86400)).ToString("0.#") + " days");
        }
        // the innermost stable orbit round a spinning hole, going its way, in horizon radii (Bardeen, Press and Teukolsky 1972): 3 at rest, down to 0.62 near the most
        private static double IscoRs(double a)
        {
            a = Math.Max(0, Math.Min(0.9999, a));
            double z1 = 1 + Math.Pow(1 - a * a, 1 / 3.0) * (Math.Pow(1 + a, 1 / 3.0) + Math.Pow(1 - a, 1 / 3.0)), z2 = Math.Sqrt(3 * a * a + z1 * z1);
            return (3 + z2 - Math.Sqrt((3 - z1) * (3 + z1 + 2 * z2))) / 2;
        }
        private static string ShiftWords(double z) { return z <= 0 ? "not at all" : z >= 0.01 ? "a share of " + z.ToString("0.###") : "1 part in " + (1 / z).ToString("0.##E+0"); }
        private static string SlowWords(double f)
        {
            double slow = 1 - f; if (slow <= 0) return "the same as far away";
            return slow * 100 >= 0.01 ? "slower than far away by " + (slow * 100).ToString("0.##") + "%" : "slower than far away by 1 part in " + (1 / slow).ToString("0.##E+0");
        }
        private string ClockWords()
        {
            double f = ClockFactorHere();
            if (_spaceScale > 0 && _matter.SpaceKind == 12) return " · your clock here: past the horizon, where no far clock sees it";
            if (f <= 0) return " · your clock here: stopped against far clocks";
            return 1 - f < 1e-7 ? "" : " · your clock here: " + SlowWords(f);
        }
    }
}
