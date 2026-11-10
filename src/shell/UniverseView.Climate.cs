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
            return Climate.MeanSurfaceK(te, tk.Tau);
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
            return Climate.SurfaceK(WorldMeanK(_planet), lat, WorldKind(_planet).Tau > 0) - HeightCoolingK(_planet, hKm);
        }
        private static string Kelvin(double k) { return k.ToString("N0") + " K (" + (k - 273.15).ToString("N0") + " C)"; }
        // a world's card: its mean, its equator and its poles, and what makes them
        private void ClimateLines(List<string> L, int i)
        {
            double mean = WorldMeanK(i); if (mean <= 0) return; var tk = WorldKind(i); bool air = tk.Tau > 0;
            double eq = Climate.SurfaceK(mean, 0, air), pole = Climate.SurfaceK(mean, Math.PI / 2, air);
            L.Add("surface about " + Kelvin(mean) + "; " + eq.ToString("N0") + " K at its equator, " + pole.ToString("N0") + " K at its poles");
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
        private void SeaLines(List<string> L, int i, double latRad, double depthM)
        {
            {
                // what the sea is: water between freezing and boiling, frozen over where it is colder; on a world colder than 100 K with
                // methane in its air, a sea of methane and ethane, as Titan's
                double ts = Climate.SurfaceK(WorldMeanK(i), latRad, WorldKind(i).Tau > 0); string air = WorldAir(i) ?? "";
                if (ts < 100 && air.Contains("methane")) L.Add("a sea of liquid methane and ethane at about " + ts.ToString("N0") + " K");
                else if (ts < 271.4) L.Add("its sea frozen over here (" + Kelvin(ts) + " at the top); liquid below the ice where it is deep");
                else L.Add("salt water, about " + SaltGPerKg().ToString("0.#") + " g of salt in every kg; " + Kelvin(SeaK(i, latRad, 0)) + " at the top, " + Kelvin(SeaK(i, latRad, depthM)) + " at its floor " + depthM.ToString("N0") + " m down");
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
            if (moonT + starT > 1e-4) L.Add("tides in the open sea: " + (moonT > 0 ? moonT.ToString(moonT >= 10 ? "N0" : "0.##") + " m from its moons, " : "") + starT.ToString(starT >= 10 ? "N0" : "0.##") + " m from its star (higher where the coast funnels them)");
            double lat = Math.Abs(latRad) * 180 / Math.PI;
            string way = lat < 30 ? "west, the trade winds' drift" : lat < 60 ? "east, the westerlies' drift" : "west, the polar easterlies' drift";
            L.Add("the surface current here: about 0.1 to 0.3 m/s toward the " + way + " (gyres along its coasts, the deep currents, remain)");
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
        }
        // the ground's height above its sea cools it by its air's rate of fall (a world with air)
        private double HeightCoolingK(int i, double heightKm) { var tk = WorldKind(i); double mu = MeanMolar(WorldAir(i)); return tk.AirBar > 0 && mu > 0 && heightKm > 0 ? LapseKPerKm(WorldG(i), mu) * heightKm : 0; }
        // ── the inside of the world you are at, at work (src/sim/PlanetCore.cs, PlanetPlates.cs, the same as Universe's worlds): its
        // heat, plates or a stagnant lid, its core and magnetic field (flipping now and then), its volcanoes, its plates' edges and their
        // quakes and eruptions, all running with the game's time; the world taken as old as ours, 4.5 billion years, at the start ──
        private Universe.Sim.PlanetCore _wCore; private Universe.Sim.PlanetPlates _wPlates; private int _wFor = int.MinValue; private SpaceGen.StarSystem _wSys;
        private const double WorldAgeAtStart = 4.5e9;
        private void WorldInside()
        {
            if (_ground == null || !_ground.Solid || _planet < 0 || _system == null || _planet >= _system.Bodies.Count) { _wCore = null; _wPlates = null; _wFor = int.MinValue; return; }
            if (_wFor != _planet || _wSys != _system)
            {
                _wFor = _planet; _wSys = _system; ulong sd = WorldSeed(_planet);
                _wCore = new Universe.Sim.PlanetCore(_ground.Radius, _ground.HasWater, Math.Abs(DayHours(_planet)), WorldAgeAtStart + _simYears, sd ^ 0x9911UL);
                _wPlates = new Universe.Sim.PlanetPlates(_ground.Radius, sd ^ 0x9922UL);
            }
            _wCore.Age = WorldAgeAtStart + Math.Max(0, _simYears); _wCore.Update();
            _wPlates.Advance(_wCore, (x, y, z, share) => { });
        }
        private void InsideAtWorkLines(List<string> L)
        {
            if (_wCore == null) return; var c = _wCore;
            L.Add("inside: heat " + c.Heat.ToString("0.##") + " of Earth's, " + (c.MagmaOcean ? "its face a magma ocean" : c.Plates ? "its plates moving" : "a stagnant lid, no plates") + ", volcanoes " + c.Volcanism.ToString("0.##") + " of Earth's");
            L.Add("its core " + (c.LiquidOuterCore ? "liquid outside, solid " + (c.InnerCore * 100).ToString("0") + "% of the way out" : "solid") + "; its field " + (c.Field > 0 ? c.Field.ToString("0.##") + " of Earth's" + (c.Flips > 0 ? ", flipped " + c.Flips.ToString("N0") + " times" : "") : "gone"));
            if (_wPlates != null && _wPlates.Recent.Count > 0) L.Add("lately: " + _wPlates.Recent[_wPlates.Recent.Count - 1].What);
        }
    }
}
