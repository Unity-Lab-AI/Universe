// Universe · shell · the universe view, its part: the climate (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── a world's temperature from its own star and orbit and its own air (src/gen/Climate.cs): its mean, and at any place by latitude ──
        private PlanetTerrain.TerrainKindData WorldKind(int i) { var b = _system.Bodies[i]; return PlanetTerrain.TerrainKind(b.Terrain ?? DeepView.TerrainKind(b.Kind)); }
        /// <summary>World i's mean surface temperature, K (its star-only temperature from its star's light and its distance, its air's greenhouse).</summary>
        private double WorldMeanK(int i)
        {
            if (_system == null || i < 0 || i >= _system.Bodies.Count) return 0;
            var b = _system.Bodies[i]; var top = b.Parent >= 0 ? _system.Bodies[b.Parent] : b; var tk = WorldKind(i);
            double au = Math.Max(1e-4, top.OrbitUnits / Units.Au(1)), te = Climate.EquilibriumK(_system.Luminosity, au, tk.Albedo);
            return Climate.MeanSurfaceK(te, tk.Tau) + (tk.Tau > 0 ? Climate.IceAgeK(_simYears, WorldSeed(i)) : 0);   // (its ice ages: its orbit's cycles swaying it, where it has air and seas to answer)
        }
        /// <summary>The temperature where you are on the ground (K), by your latitude; 0 where it is not worked out.</summary>
        private double GroundHereK()
        {
            if (_ground == null || !_ground.Solid) return 0;
            if (_planet == -2 && _groundExtra != null && _system != null)
            {
                var ex = _groundExtra; double a = ex.OrbQ / Math.Max(1e-6, 1 - ex.OrbE), au = a > 0 ? a / Units.Au(1) : 3;
                return Climate.EquilibriumK(_system.Luminosity, au, ex.Albedo > 0 ? ex.Albedo : 0.1);   // (a small body: no air, its star-only temperature)
            }
            if (_planet < 0) return 0;
            double x, y, z; CamFromPlanet(out x, out y, out z); double r = Math.Sqrt(x * x + y * y + z * z); double lat = r > 0 ? Math.Asin(Math.Max(-1, Math.Min(1, y / r))) : 0;
            double hKm = r > 0 ? Math.Max(0, _ground.GroundAt(x / r, y / r, z / r)) : 0;   // (its height above its sea cools it)
            return NowK(_planet, lat, hKm, LocalHour());                                 // (its year's mean, its season and its hour, and its height)
        }
        private static string Kelvin(double k) { return k.ToString("N0") + " K (" + (k - 273.15).ToString("N0") + " C)"; }
        // a world's card: its mean, its equator and its poles, and what makes them
        private void ClimateLines(List<string> L, int i)
        {
            double mean = WorldMeanK(i); if (mean <= 0) return; var tk = WorldKind(i); bool air = tk.Tau > 0;
            double eq = Climate.SurfaceK(mean, 0, air), pole = Climate.SurfaceK(mean, Math.PI / 2, air);
            L.Add("surface about " + Kelvin(mean) + "; " + eq.ToString("N0") + " K at its equator, " + pole.ToString("N0") + " K at its poles");
            if (air)
            {
                double sway = Climate.IceAgeK(_simYears, WorldSeed(i)), ice = Climate.IceLineSin(mean);
                L.Add((sway < -1.5 ? "in an ice age" : sway > 1.5 ? "in a warm spell between ice ages" : "between its ice ages' extremes") + ": " + (sway >= 0 ? "+" : "") + sway.ToString("0.#") + " K by its orbit's cycles; its ice from " + (ice >= 1 ? "nowhere (none)" : ice <= 0 ? "pole to pole" : (Math.Asin(ice) * 180 / Math.PI).ToString("0") + " degrees"));
            }
            if (air) L.Add("its air's greenhouse adds " + (mean - mean / Math.Pow(1 + 0.75 * tk.Tau, 0.25)).ToString("N0") + " K to its star's light alone");
        }
        // ── its seas: the tides raised by its moons and its star (the equilibrium tide's range, 1.5 (m / M) R^4 / d^3: Earth 0.54 m from
        // the Moon, 0.25 m from the Sun), and the surface current where you are from the wind belts its turning makes (trade winds
        // to about 30 degrees driving the water west, the westerlies to 60 east, the polar easterlies west again) ──
        // the sea water's salt (g in every kg), from its dissolved ions in the content (content/minerals.unt sea-water trace: each
        // ion's share of the water's sites times its mass over the water's 18 g): about 33.5, Earth's 35 less its sulfate's oxygen
        private double _salt = -1;
        private double SaltGPerKg()
        {
            if (_salt >= 0) return _salt; _salt = 0; var mass = new Dictionary<string, double>();
            foreach (var e in _content.OfKind("elements.unt", "element")) { double a, b2; if (e.TryRange("mass-number", out a, out b2)) mass[e.Get("symbol", e.Id)] = a; }
            foreach (var e in _content.OfKind("minerals.unt", "mineral"))
            {
                if (e.Id != "sea-water") continue;
                foreach (var tr in e.All("trace").Count > 0 ? e.All("trace") : new List<string> { e.Get("trace", "") })
                {
                    var bits = tr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int k = 1; k + 1 < bits.Length; k += 2) { double sh, m; if (double.TryParse(bits[k + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sh) && mass.TryGetValue(bits[k], out m)) _salt += sh * m / 18.0 * 1000; }
                }
            }
            return _salt;
        }
        // the sea at a depth (m): the deep water made at its poles (their temperature, never colder than salt water freezes, 271.4 K),
        // the surface's warmth above it falling off through a thermocline about 300 m deep
        private double SeaK(int i, double latRad, double depthM)
        {
            double mean = WorldMeanK(i); bool air = WorldKind(i).Tau > 0; double top = Math.Max(271.4, Climate.SurfaceK(mean, latRad, air)), deep = Math.Max(271.4, Climate.SurfaceK(mean, Math.PI / 2, air));
            deep = Math.Min(deep, top); return deep + (top - deep) * Math.Exp(-Math.Max(0, depthM) / 300.0);
        }
        // each world's own salt: the sea's ions come down its rivers from its weathered land (and up from its floor's hot vents), so a world's
        // salt as the root of its land's share against Earth's (29% land; Earth's sea water from the content), never under the vents' own,
        // about a third of Earth's (the water worked through the floor's rock alone); the land's share measured on the world's own ground
        // (2,000 points evenly over it, the Fibonacci sphere), once, for the world you are at
        private int _saltOf = -1; private double _saltLand = 0.29;
        private double WorldSalt(int i)
        {
            if (_ground != null && _planet == i && _saltOf != i)
            {
                int n = 2000, up = 0;
                for (int k = 0; k < n; k++) { double y = 1 - (k + 0.5) * 2.0 / n, r = Math.Sqrt(1 - y * y), a2 = k * 2.39996323; if (_ground.GroundAt(r * Math.Cos(a2), y, r * Math.Sin(a2)) > 0) up++; }
                _saltLand = up / (double)n; _saltOf = i;
            }
            double land = _saltOf == i ? _saltLand : 0.29;
            return SaltGPerKg() * Math.Max(0.33, Math.Sqrt(land / 0.29));
        }
        private void SeaLines(List<string> L, int i, double latRad, double depthM)
        {
            {
                // what the sea is: water between freezing and boiling, frozen over where it is colder; on a world colder than 100 K with
                // methane in its air, a sea of methane and ethane, as Titan's
                double ts = Climate.SurfaceK(WorldMeanK(i), latRad, WorldKind(i).Tau > 0); string air = WorldAir(i) ?? "";
                if (ts < 100 && air.Contains("methane")) L.Add("a sea of liquid methane and ethane at about " + ts.ToString("N0") + " K");
                else if (ts < 271.4) L.Add("its sea frozen over here (" + Kelvin(ts) + " at the top); liquid below the ice where it is deep");
                else L.Add("salt water, about " + WorldSalt(i).ToString("0.#") + " g of salt in every kg; " + Kelvin(SeaK(i, latRad, 0)) + " at the top, " + Kelvin(SeaK(i, latRad, depthM)) + " at its floor " + depthM.ToString("N0") + " m down");
            }
            if (_system == null || i < 0 || i >= _system.Bodies.Count) return; var b = _system.Bodies[i]; if (b.MassKg <= 0) return;
            double R = b.RadiusUnits / Units.Km(1) * 1000, M = b.MassKg, moonT = 0, starT;
            for (int j = 0; j < _system.Bodies.Count; j++)
            {
                var m = _system.Bodies[j]; if (m.Parent != i || m.MassKg <= 0) continue; double d = m.OrbitUnits / Units.Km(1) * 1000;
                moonT += 1.5 * m.MassKg / M * Math.Pow(R, 4) / Math.Pow(d, 3);
            }
            var top = b.Parent >= 0 ? _system.Bodies[b.Parent] : b; double ds = top.OrbitUnits / Units.Km(1) * 1000, ms = _system.MassSol * 1.989e30;
            starT = b.Parent >= 0 ? 0 : 1.5 * ms / M * Math.Pow(R, 4) / Math.Pow(ds, 3);
            if (moonT + starT > 1e-4)
            {
                // the sea now and its next high water: two bulges, under each moon and the star and opposite, so twice round its sky a day;
                // each one's hour angle from where it is over the world now (the star's by the local hour, a moon's by its angle from the star)
                double hr = LocalHour(); if (hr >= 0)
                {
                    double px, py, pz; BodyPos(i, out px, out py, out pz); double now = 0, rate = 0; var parts = new List<double[]>();
                    parts.Add(new[] { starT, hr });
                    for (int j = 0; j < _system.Bodies.Count; j++)
                    {
                        var m = _system.Bodies[j]; if (m.Parent != i || m.MassKg <= 0) continue; double mx, my, mz; BodyPos(j, out mx, out my, out mz);
                        double ax = mx - px, az = mz - pz, sx = -px, sz = -pz, ang = Math.Atan2(ax * sz - az * sx, ax * sx + az * sz);   // (the moon's angle from the star, round the world's sky)
                        double d = m.OrbitUnits / Units.Km(1) * 1000; parts.Add(new[] { 1.5 * m.MassKg / M * Math.Pow(R, 4) / Math.Pow(d, 3), hr - ang / (2 * Math.PI) * 24 });
                    }
                    double nextH = -1;
                    foreach (var pt in parts) { double ph = 2 * Math.PI * (pt[1] - 12) * 2 / 24; now += 0.5 * pt[0] * Math.Cos(ph); rate -= pt[0] * Math.Sin(ph); }
                    for (double dh = 0.1; dh < 13; dh += 0.1) { double h0 = 0; foreach (var pt in parts) h0 += 0.5 * pt[0] * Math.Cos(2 * Math.PI * (pt[1] + dh - 12) * 2 / 24); double h1 = 0; foreach (var pt in parts) h1 += 0.5 * pt[0] * Math.Cos(2 * Math.PI * (pt[1] + dh + 0.1 - 12) * 2 / 24); if (h1 < h0 && nextH < 0) { nextH = dh; break; } }
                    // shoaling into its shallows (Green's law: a long wave's height as the depth to the minus a quarter, from the open sea's
                    // 4,000 m), so it stands higher here the shallower the water
                    double green = Math.Pow(4000 / Math.Max(10, depthM), 0.25); now *= green;
                    // a bay's narrowing (Green's law again: as its breadth to the minus a half): the sea's breadth here found from the ground round
                    // the spot, the shortest way across of four (north-south, east-west and the two diagonals), each side out to five degrees;
                    // against an open sea's 500 km, at most four times more (Fundy's 16 m)
                    double funnel = 1;
                    if (_ground != null && _planet == i)
                    {
                        double la0 = latRad, lo0 = Math.Atan2(_tz, _tx), Rk = R / 1000, least = double.MaxValue;
                        for (int a2 = 0; a2 < 4; a2++)
                        {
                            double dla = Math.Cos(a2 * Math.PI / 4), dlo = Math.Sin(a2 * Math.PI / 4), across = 0; bool shut = true;
                            for (int sd = -1; sd <= 1 && shut; sd += 2)
                            {
                                bool hit = false;
                                for (int k = 1; k <= 100; k++)
                                {
                                    double t = sd * k * 0.05 * Math.PI / 180, la = la0 + dla * t, lo = lo0 + dlo * t / Math.Max(0.05, Math.Cos(la0));
                                    if (_ground.GroundAt(Math.Cos(la) * Math.Cos(lo), Math.Sin(la), Math.Cos(la) * Math.Sin(lo)) > 0) { across += k * 0.05 * Math.PI / 180 * Rk; hit = true; break; }
                                }
                                if (!hit) shut = false;
                            }
                            if (shut) least = Math.Min(least, across);
                        }
                        if (least < 500) funnel = Math.Min(4, Math.Sqrt(500 / Math.Max(1, least)));
                    }
                    now *= funnel;
                    L.Add("the sea now " + (now >= 0 ? "+" : "") + now.ToString("0.##") + " m, " + (rate > 0 ? "rising" : "falling") + (nextH > 0 ? "; high water in about " + nextH.ToString("0.#") + " hours" : "") + (green > 1.05 ? " (raised " + green.ToString("0.#") + " times coming into " + depthM.ToString("N0") + " m of water)" : "") + (funnel > 1.05 ? " (and " + funnel.ToString("0.#") + " times more by the narrowing of the water here)" : ""));
                }
                L.Add("tides in the open sea: " + (moonT > 0 ? moonT.ToString(moonT >= 10 ? "N0" : "0.##") + " m from its moons, " : "") + starT.ToString(starT >= 10 ? "N0" : "0.##") + " m from its star (higher where the coast funnels them)");
            }
            double lat = Math.Abs(latRad) * 180 / Math.PI;
            string way = lat < 30 ? "west, the trade winds' drift" : lat < 60 ? "east, the westerlies' drift" : "west, the polar easterlies' drift";
            // its basin, found here (render by scale): along its latitude, east and west to the nearest coast; a gyre's water heaped against its
            // western shore runs fast poleward there (westward intensification, Stommel 1948, Munk 1950: the Gulf Stream, the Kuroshio), slow
            // toward the equator along its eastern shore and across its middle
            double west = -1, east = -1;
            if (_ground != null && _planet == i)
            {
                double cy = Math.Sin(latRad), cr = Math.Cos(latRad), lon0 = Math.Atan2(_tz, _tx);
                for (int sd = -1; sd <= 1; sd += 2)
                    for (int k = 1; k <= 180; k++)
                    {
                        double lo = lon0 + sd * k * Math.PI / 180; if (_ground.GroundAt(cr * Math.Cos(lo), cy, cr * Math.Sin(lo)) > 0) { if (sd < 0) west = k; else east = k; break; }
                    }
            }
            string current;
            if (lat < 15 || lat > 65 || west < 0 || east < 0) current = "about 0.1 to 0.3 m/s toward the " + way;
            else
            {
                double share = west / (west + east); bool sub = lat < 45; string pole = latRad >= 0 ? "north" : "south", equator = latRad >= 0 ? "south" : "north";
                current = share < 0.12 ? (sub ? "about 1 to 2 m/s toward the " + pole + ", the gyre's western boundary current, " + west.ToString("0") + " degrees off its western shore" : "a boundary current toward the " + equator + " along its western shore, the cold return of its northern gyre")
                        : share > 0.85 ? "about 0.1 to 0.3 m/s toward the " + equator + ", the slow drift along its eastern shore"
                        : "a few cm/s " + (sub ? "toward the " + equator : "toward the " + pole) + " across the gyre's middle (Sverdrup's flow), under the " + way;
            }
            L.Add("the surface current here: " + current + (west > 0 && east > 0 ? " (its basin " + (west + east).ToString("0") + " degrees across here)" : ""));
            {
                // the water rising and sinking: along a subtropical eastern shore the winds toward the equator push the top water out to sea
                // (Ekman's transport) and the cold deep water rises in its place, 5 to 10 K colder and rich (California, Peru, Benguela); at the
                // equator the trade winds part the water and it rises there too; in a polar sea near freezing the cold salt water sinks, the
                // start of the deep conveyor, about a thousand years round
                double share = west > 0 && east > 0 ? west / (west + east) : -1, topK = SeaK(i, latRad, 0);
                if (share > 0.85 && lat > 15 && lat < 40) L.Add("upwelling here: the deep water rising along this eastern shore, about 5 to 10 K colder than the open sea at this latitude, rich in what the deep holds");
                else if (lat < 5) L.Add("upwelling here: the trade winds parting the water at the equator, the deep water rising between");
                else if (lat > 55 && topK < 275) L.Add("deep water forming here: the cold salt water sinking, the start of the deep conveyor that takes about a thousand years round");
            }
        }
        // ── its air by height: its pressure at the ground (content/terrain.unt air-bar), its scale height H = kT / (m g) from its mean
        // molecule (from its air's own make-up), so the pressure halves every H ln 2; its temperature falling with height at about two
        // thirds of the dry rate g / cp (Earth 6.5 K a km, Mars about 2.5); its tropopause where the pressure is near 0.1 bar, as on every
        // world with a thick air (Robinson and Catling 2014) ──
        private static readonly Dictionary<string, double> MolarG = new Dictionary<string, double> { { "nitrogen", 28.01 }, { "oxygen", 32.00 }, { "argon", 39.95 }, { "carbon dioxide", 44.01 }, { "water vapour", 18.02 }, { "methane", 16.04 }, { "carbon monoxide", 28.01 }, { "hydrogen", 2.016 }, { "helium", 4.003 }, { "neon", 20.18 }, { "ammonia", 17.03 }, { "krypton", 83.80 }, { "xenon", 131.3 }, { "sulfur dioxide", 64.07 } };
        // its air's mean molecule (g/mol), from its make-up's shares; 0 where none is given
        private static double MeanMolar(string air)
        {
            if (string.IsNullOrEmpty(air)) return 0; double sum = 0, w = 0;
            foreach (var part in air.Split('(')[0].Split(','))
            {
                var t = part.Trim(); int pc = t.LastIndexOf(' '); if (pc < 0 || !t.EndsWith("%")) continue;
                string name = t.Substring(0, pc).Trim(); double share; double mg;
                if (!double.TryParse(t.Substring(pc + 1).TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out share)) continue;
                if (!MolarG.TryGetValue(name, out mg)) continue; sum += share * mg; w += share;
            }
            return w > 0 ? sum / w : 0;
        }
        // the dry rate's heat capacity per kilogram: seven halves of the gas constant a mole for an air of two-atom molecules, about 4.45 for one
        // of carbon dioxide's kind; the rate of fall two thirds of g / cp (water's heat given back as it rises: Earth 6.5 K a km)
        private static double LapseKPerKm(double g, double molarG) { double cp = (molarG > 35 ? 4.45 : 3.5) * 8.314 / (molarG / 1000); return 0.66 * g / cp * 1000; }
        private double WorldG(int i) { var b = _system.Bodies[i]; double r = b.RadiusUnits / Units.Km(1) * 1000; return b.MassKg > 0 ? Physics.G * b.MassKg / (r * r) : 9.81; }
        private string WorldAir(int i) { string a; var b = _system.Bodies[i]; return PlanetTerrain.Air.TryGetValue(b.Terrain ?? DeepView.TerrainKind(b.Kind), out a) ? a : null; }
        private void AirLines(List<string> L, int i)
        {
            var tk = WorldKind(i); double p0 = tk.AirBar, mu = MeanMolar(WorldAir(i)); if (p0 <= 0 || mu <= 0) return;
            double g = WorldG(i), T = WorldMeanK(i), H = 1.380649e-23 * T / (mu / 1000 / 6.02214076e23 * g) / 1000, lapse = LapseKPerKm(g, mu);
            double tropo = p0 > 0.1 ? H * Math.Log(p0 / 0.1) : 0;
            L.Add("air " + p0.ToString(p0 >= 10 ? "N0" : p0 >= 0.1 ? "0.###" : "0.####") + " bar at the ground, halving every " + (H * Math.Log(2)).ToString("0.#") + " km up (molecules of " + mu.ToString("0.#") + " g/mol on average)");
            L.Add("cooling " + lapse.ToString("0.#") + " K a km as you rise" + (tropo > 0 ? ", to its tropopause about " + tropo.ToString("0") + " km up" : ""));
            {
                // its gases by height: mixed together up to its homopause (where its air has thinned to about three millionths of a bar,
                // Earth's about 100 km), above that each thinning by its own weight (its own scale height kT / (m g)), so the lightest it
                // has rises to the top
                double homo = p0 > 3e-6 ? H * Math.Log(p0 / 3e-6) : 0; string light = null; double lm = double.MaxValue;
                foreach (var part in (WorldAir(i) ?? "").Split('(')[0].Split(','))
                {
                    var t = part.Trim(); int pc = t.LastIndexOf(' '); if (pc < 0) continue; string nm = t.Substring(0, pc).Trim(); double mg; if (MolarG.TryGetValue(nm, out mg) && mg < lm) { lm = mg; light = nm; }
                }
                if (homo > 0 && light != null) L.Add("its gases mixed to about " + homo.ToString("0") + " km up; above, each by its own weight, " + light + " (" + lm.ToString("0.#") + " g/mol) rising to the top");
            }
        }
        // the ground's height above its sea cools it by its air's rate of fall (a world with air)
        private double HeightCoolingK(int i, double heightKm) { var tk = WorldKind(i); double mu = MeanMolar(WorldAir(i)); return tk.AirBar > 0 && mu > 0 && heightKm > 0 ? LapseKPerKm(WorldG(i), mu) * heightKm : 0; }
        // ── the inside of the world you are at, at work (src/sim/PlanetCore.cs, PlanetPlates.cs, the same as Universe's worlds): its
        // heat, plates or a stagnant lid, its core and magnetic field (flipping now and then), its volcanoes, its plates' edges and their
        // quakes and eruptions, all running with the game's time; the world taken as old as ours, 4.5 billion years, at the start ──
        private Universe.Sim.PlanetImpacts _wImpacts; private int _wCraters; private object _iceFor; private double _iceAt = -1e9; private Universe.Sim.PlanetCore _wCore; private Universe.Sim.PlanetPlates _wPlates; private object _driftFor; private double _driftAt; private int _wFor = int.MinValue; private SpaceGen.StarSystem _wSys;
        private const double WorldAgeAtStart = 4.5e9;
        private void WorldInside()
        {
            if (_ground != null && _ground.Solid)
            {
                // the small streams round you, at the detail your height needs (render by scale): a map forty times your height across
                double sx, sy, sz; CamFromPlanet(out sx, out sy, out sz); double altKm = Math.Max(0.005, AboveGround() / Units.Km(1));
                if (altKm < 100) _ground.AskStreams(sx, sy, sz, altKm * 40);
            }
            if (_ground == null || !_ground.Solid || _planet < 0 || _system == null || _planet >= _system.Bodies.Count) { _wCore = null; _wPlates = null; _wFor = int.MinValue; return; }
            if (_wFor != _planet || _wSys != _system)
            {
                _wFor = _planet; _wSys = _system; ulong sd = WorldSeed(_planet);
                _wImpacts = null; _wCraters = 0; _wCore = new Universe.Sim.PlanetCore(_ground.Radius, _ground.HasWater, Math.Abs(DayHours(_planet)), WorldAgeAtStart + _simYears, sd ^ 0x9911UL);
                _wPlates = new Universe.Sim.PlanetPlates(_ground.Radius, sd ^ 0x9922UL);
            }
            _wCore.Age = WorldAgeAtStart + Math.Max(0, _simYears); _wCore.Update();
            if (_ground.Traits.PolarIce)
            {
                // its ice line from its climate now: set as its ground is made, and the ground remade when it has moved (its ice sheets
                // growing and going with its ice ages)
                double want = Climate.IceLineSin(WorldMeanK(_planet));
                if (!ReferenceEquals(_iceFor, _ground)) { _iceFor = _ground; if (Math.Abs(_ground.IceSin - want) > 1e-3) { _ground.IceSin = want; System.Threading.Interlocked.Increment(ref _ground.Generation); } }
                else if (Math.Abs(_ground.IceSin - want) > 0.02 && _lastDrawTime - _iceAt > 5) { _ground.IceSin = want; _iceAt = _lastDrawTime; System.Threading.Interlocked.Increment(ref _ground.Generation); }   // (at most every five seconds watched: at the fastest times the ice comes and goes faster than the ground can be remade)
            }
            _wPlates.Advance(_wCore, (x, y, z, share) => { });
            if (_wCore.Plates)
            {
                // the plates drawn on its ground and the ground moved with them (src/sim/PlanetPlates.cs Back, as on Universe's worlds): each
                // point's rock found where its plate has carried it from, the edges raised where plates crash, sunk where they spread, in their
                // rock's colours; the ground remade when they have moved a million years more (10 to 100 km, seen from above), at most every
                // five seconds watched
                double moved = Math.Max(0, _simYears);
                if (!ReferenceEquals(_driftFor, _ground)) { _driftFor = _ground; _wPlates.MovedYears = moved; _ground.Drift = _wPlates.Back; System.Threading.Interlocked.Increment(ref _ground.Generation); _driftAt = _lastDrawTime; }
                else if (Math.Abs(moved - _wPlates.MovedYears) >= 1e6 && _lastDrawTime - _driftAt > 5) { _wPlates.MovedYears = moved; _driftAt = _lastDrawTime; System.Threading.Interlocked.Increment(ref _ground.Generation); }
            }
            {
                // the strikes on it with time (src/sim/PlanetImpacts.cs, as on Universe's worlds, no life here to harm), each great one's crater
                // put into its ground (8 km and up), the ground remade when they change, at most every five seconds watched
                if (_wImpacts == null) _wImpacts = new Universe.Sim.PlanetImpacts((ulong)(WorldSeed(_planet) ^ 0x9933UL));
                _wImpacts.Advance(_wCore, null);
                int big = 0; foreach (var c in _wImpacts.Craters) if (c[3] >= 8) big++;
                if (big != _wCraters && _lastDrawTime - _iceAt > 5)
                {
                    _wCraters = big; _iceAt = _lastDrawTime; var nc = new List<double[]>(); foreach (var c in _wImpacts.Craters) if (c[3] >= 8) nc.Add(c);
                    _ground.Craters = nc; _ground.BakeChanges(); System.Threading.Interlocked.Increment(ref _ground.Generation);
                }
            }
        }
        private void InsideAtWorkLines(List<string> L)
        {
            if (_wCore == null) return; var c = _wCore;
            L.Add("inside: heat " + c.Heat.ToString("0.##") + " of Earth's, " + (c.MagmaOcean ? "its face a magma ocean" : c.Plates ? "its plates moving" : "a stagnant lid, no plates") + ", volcanoes " + c.Volcanism.ToString("0.##") + " of Earth's");
            L.Add("its core " + (c.LiquidOuterCore ? "liquid outside, solid " + (c.InnerCore * 100).ToString("0") + "% of the way out" : "solid") + "; its field " + (c.Field > 0 ? c.Field.ToString("0.##") + " of Earth's" + (c.Flips > 0 ? ", flipped " + c.Flips.ToString("N0") + " times" : "") : "gone"));
            if (_wPlates != null && _wPlates.Recent.Count > 0) L.Add("lately: " + _wPlates.Recent[_wPlates.Recent.Count - 1].What);
            if (_wImpacts != null && _wImpacts.Total > 0) L.Add("struck " + _wImpacts.Total.ToString("N0") + " times by a stadium-sized rock or bigger; the last: " + _wImpacts.Last + (_wImpacts.DarkYearsLeft > 0 ? "; its sky darkened by the dust for " + Years(_wImpacts.DarkYearsLeft) + " more" : ""));
        }
        // ── the day and the season where you are ──
        // its day's swing: from its noon point (root 2 times its star-only temperature) to a night near three tenths of it with no air
        // (the Moon's 390 and 100 K), shrunk by its air's radiative time (cp P / (4 g sigma T^3), Earth about 22 days) against its day:
        // Earth's about 12 K, as its land's
        private double DaySwingK(int i)
        {
            var b = _system.Bodies[i]; var tk = WorldKind(i); var top = b.Parent >= 0 ? _system.Bodies[b.Parent] : b;
            double te = Climate.EquilibriumK(_system.Luminosity, Math.Max(1e-4, top.OrbitUnits / Units.Au(1)), tk.Albedo), bare = (Math.Sqrt(2) - 0.3) * te;
            // and the ground's own warmth held through the night: its thermal inertia (about 250 J per m2 per K per root second for rock and
            // soil, Mars's measured mean) over the day's skin, I root(day / pi) / (4 sigma T^3) (the Moon's fine dust about 50: hardly held)
            double T = WorldMeanK(i), day = Math.Abs(DayHours(i)) * 3600, sT3 = 4 * 5.670374e-8 * T * T * T;
            double tau = (tk.Solid ? (tk.AirBar > 0 ? 250 : 50) * Math.Sqrt(day / Math.PI) / sT3 : 0);
            double mu = MeanMolar(WorldAir(i));
            if (tk.AirBar > 0 && mu > 0) { double cp = (mu > 35 ? 4.45 : 3.5) * 8.314 / (mu / 1000); tau += cp * tk.AirBar * 1e5 / (WorldG(i) * sT3); }
            return tau > 0 ? bare * (day / tau) / (1 + day / tau) : bare;
        }
        // its axial tilt (degrees), from its seed by the content's law (content/physics.unt @law axial-tilt)
        private double _tiltLo = 0, _tiltHi = 30, _tiltWild = 0.15, _tiltWLo = 60, _tiltWHi = 180; private bool _tiltRead;
        private double TiltDeg(int i)
        {
            if (!_tiltRead)
            {
                _tiltRead = true; foreach (var e in _content.OfKind("physics.unt", "law")) if (e.Id == "axial-tilt") { double a, b2; if (e.TryRange("tilt-deg", out a, out b2)) { _tiltLo = a; _tiltHi = b2; } if (e.TryRange("wild-share", out a, out b2)) _tiltWild = a; if (e.TryRange("wild-deg", out a, out b2)) { _tiltWLo = a; _tiltWHi = b2; } }
            }
            ulong sd = WorldSeed(i); double u = Hash01((int)(sd & 0x7FFFFFFF), 31), v = Hash01((int)(sd & 0x7FFFFFFF), 32);
            return u < _tiltWild ? _tiltWLo + (_tiltWHi - _tiltWLo) * v : _tiltLo + (_tiltHi - _tiltLo) * v;
        }
        // the temperature now at a latitude (radians): its year's mean, its season (summer near the year's mean of its latitude less its
        // tilt, winter of its latitude plus its tilt, the seas' and ground's stored heat taking about four tenths off), and its hour
        private double NowK(int i, double latRad, double heightKm, double hour)
        {
            double mean = WorldMeanK(i); bool air = WorldKind(i).Tau > 0; var b = _system.Bodies[i]; var top = b.Parent >= 0 ? _system.Bodies[b.Parent] : b;
            double tilt = TiltDeg(i) * Math.PI / 180, eff = Math.Min(tilt, Math.PI - tilt), la = Math.Abs(latRad);
            double summer = Climate.SurfaceK(mean, Math.Max(0, la - eff), air), winter = Climate.SurfaceK(mean, Math.Min(Math.PI / 2, la + eff), air);
            double yearPhase = 2 * Math.PI * (_simYears / Math.Max(1e-6, top.Period)), season = Math.Cos(yearPhase) * (latRad >= 0 ? 1 : -1);
            double t = Climate.SurfaceK(mean, latRad, air) + 0.6 * 0.5 * (summer - winter) * season - HeightCoolingK(i, heightKm);
            if (hour >= 0) t += 0.5 * DaySwingK(i) * Math.Cos(2 * Math.PI * (hour - 14) / 24);   // (warmest about two in the afternoon)
            return t;
        }
    }
}
