// Universe · shell · the universe view, its part: gravity (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── gravity switched off (the Time menu; content/physics.unt @law gravity-off): the year it went, the star's swelling ──
        private double _gravityOffYear = double.NaN, _gravityOffWatch, _gravityOnYear = double.NaN, _gravityOnWatch;
        // gravity has been off since _gravityOffYear (and, if it is back on, until _gravityOnYear): what it flung off stays flown
        private bool Flown { get { return !double.IsNaN(_gravityOffYear); } }
        // how long it was off: to now while it is off, to when it came back once it is on again (years; and watched seconds)
        private double OffYears { get { return !Flown ? 0 : Math.Max(0, (Window.GravityOn && !double.IsNaN(_gravityOnYear) ? _gravityOnYear : _simYears) - _gravityOffYear); } }
        private double OffWatchFlight { get { return !Flown ? 0 : Math.Max(1e-3, (Window.GravityOn && !double.IsNaN(_gravityOnYear) ? _gravityOnWatch : _lastDrawTime) - _gravityOffWatch); } }   // (_gravityOffWatch: the watched time it went off, for what is moved on the card by watched time: a hole's disc, swarm and stream)
        // how long since it came back (years; 0 while it is off)
        private double OnYears { get { return Flown && Window.GravityOn && !double.IsNaN(_gravityOnYear) ? Math.Max(0, _simYears - _gravityOnYear) : 0; } }
        // gravity back on, a star flown off its path in a pull that grows with its distance from where it was (a galaxy's disc, a
        // cluster's middle) swings back about it, as a spring: its offset x0 cos(k t) + (v / k) sin(k t), from the offset it had flown
        // (v times the time off) and still moving at v; given as the years of flight at v that offset is worth (k: its swing's rate a year)
        private double DriftYears(double kPerYear)
        {
            double off = Math.Max(0, OffYears), t = OnYears; if (t <= 0 || kPerYear <= 0) return off;
            return off * Math.Cos(kPerYear * t) + Math.Sin(kPerYear * t) / kPerYear;
        }
        // gravity back on, a ball of gas swollen to s times its size falls back in on itself in its free-fall time from there
        // (pi / (2 root 2)) s^1.5 times its own free-fall time, as a body dropped: its size 1 + (s - 1)(1 - (t / t_fall)^2)
        private double SwellNow(double tffSec)
        {
            double s = Math.Min(1e6, 1 + Math.Max(0, OffYears) * 31557600.0 / (_swellOverFreefall * tffSec)), t = OnYears * 31557600.0; if (t <= 0) return s;
            double fall = Math.PI / (2 * Math.Sqrt(2)) * Math.Pow(s, 1.5) * tffSec, q = t / fall; return q >= 1 ? 1 : 1 + (s - 1) * (1 - q * q);
        }
        public void ToggleGravity()
        {
            // off: from now everything goes straight on; on again: the open system's bodies fall back under their pulls (live gravity),
            // and what is worked out from its orbit (rubble, rings, the stars round you, a hole's parts, a star's swelling) is held where it flew to
            Window.GravityOn = !Window.GravityOn;
            if (!Window.GravityOn) { _gravityOffYear = _simYears; _gravityOffWatch = _lastDrawTime; _gravityOnYear = double.NaN; }
            else { _gravityOnYear = _simYears; _gravityOnWatch = _lastDrawTime; }
            SpaceGen.GravityOffYear = _gravityOffYear; SpaceGen.GravityOnYear = _gravityOnYear; MenuChanged = true;
        }
        private string GravityWords() { return !Flown ? "" : Window.GravityOn ? " · gravity back on after " + Years(OffYears) + " off: the worlds falling back, the rest held where it flew" : " · gravity off for " + Years(Math.Max(0, OffYears)) + ": every body straight on, every star blown apart"; }
        // how much the system's star has swollen: its own pressure unopposed grows it at about its sound speed, doubling it in about its
        // sound-crossing time (swell-over-freefall times its free-fall time root(R^3 / GM): about an hour for a star of one solar mass)
        private double StarSwell
        {
            get
            {
                if (!Flown || _system == null) return 1;
                double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol), tff = Math.Sqrt(R * R * R / gm);
                return SwellNow(tff);
            }
        }
        private double StarSwellSpeedKmS() { if (_system == null) return 0; double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol); return R / (_swellOverFreefall * Math.Sqrt(R * R * R / gm)) / 1000; }
        private double StarRadiusNow { get { return _system != null ? _system.StarRadius * StarSwell : 0; } }
        // a galaxy's stars flung off with gravity off: how far each has flown in the galaxy's own frame (the disc's on their tangents at the
        // rim speed, a round galaxy's every way: negative), and the camera's own star's flight taken out of the placing, so you ride with it
        private readonly Dictionary<string, bool> _ballType = new Dictionary<string, bool>();
        private float GalaxyDrift(Galaxy g, ref Mat4 pg)
        {
            if (!Flown) return 0;
            double years = Math.Max(0, OffYears), kmPerUnit = g.Scale / Units.Km(1);
            double drift = _rimSpeed * years * 31557600.0 / Math.Max(1e-30, kmPerUnit);
            bool ball; if (!_ballType.TryGetValue(g.Type, out ball)) { ball = false; foreach (var e in _content.OfKind("galaxies.unt", "galaxy")) if (e.Id == g.Type) ball = e.Get("bodies-spread", "disc") == "ball"; _ballType[g.Type] = ball; }
            if (!ball)
            {
                // the camera in the galaxy's frame (the placing's rows are the galaxy's axes, scaled): its own tangent taken out when it is within the galaxy
                double rx = _cx - g.X, ry = _cy - g.Y, rz = _cz - g.Z, s2 = (double)g.Scale * g.Scale;
                double lx = (rx * pg.M11 + ry * pg.M12 + rz * pg.M13) / s2, lz = (rx * pg.M31 + ry * pg.M32 + rz * pg.M33) / s2, lr = Math.Sqrt(lx * lx + lz * lz);
                if (lr > 1e-6 && lr < 1.5) { double tx = -lz / lr * drift, tz = lx / lr * drift; pg.M41 -= (float)(tx * pg.M11 + tz * pg.M31); pg.M42 -= (float)(tx * pg.M12 + tz * pg.M32); pg.M43 -= (float)(tx * pg.M13 + tz * pg.M33); }
            }
            return (float)(ball ? -drift : drift);
        }
        // with gravity off, a star round you carries straight on at its own speed against yours: its peculiar velocity, the spread of
        // the stars' motions about the galaxy's turn (about 20 km/s along each way round the Sun), fixed for each star by its place in
        // the list; its flight so far in the galaxy's own units (the rest of the galaxy's turn they share, as you do)
        private void LocalDrift(int k, out double dx, out double dy, out double dz)
        {
            dx = dy = dz = 0; if (!Flown || _near < 0 || _near >= _galaxies.Count) return;
            double years = DriftYears(Math.Sqrt(2) * _rimSpeed / 2.469e17 * 31557600.0), kmPerUnit = _galaxies[_near].Scale / Units.Km(1), km = 20.0 * years * 31557600.0 / Math.Max(1e-30, kmPerUnit);
            uint h = (uint)StarKey(k) * 2654435761u + 977u;   // (by the star's own lasting id)
            Func<uint, double> g = q => { uint a = h ^ (q * 0x9E3779B9u); a ^= a >> 15; a *= 0x2C1B3C6Du; a ^= a >> 12; double u1 = ((a & 0xFFFFFF) + 1) / 16777217.0, u2 = ((a >> 8) & 0xFFFF) / 65536.0; return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); };
            dx = g(1) * km; dy = g(2) * km; dz = g(3) * km;
        }
        // with gravity off, any star swells as the system's does: by its own pressure, about its sound speed, doubling in about its
        // free-fall time root(R^3 / GM) (swell-over-freefall of it): a star like ours in an hour or so, a white dwarf in seconds, a neutron
        // star in a fraction of a millisecond, from its kind's radius and mass
        private double KindSwell(StarKind k)
        {
            if (!Flown || k == null) return 1;
            double R = k.RadiusSol * 6.957e8, gm = Physics.GmSol * Math.Max(1e-3, k.MassSol), tff = Math.Sqrt(R * R * R / gm);
            return SwellNow(tff);
        }
        // with gravity off, the card's lines at every scale: how long, and what it means where you are (the star swelling, the
        // galaxy's stars flown off as a share of its width and the time they take to cross it at their rim speed)
        private void GravityLines(List<string> L)
        {
            if (!Flown) return; double yrs = Math.Max(0, OffYears);
            if (Window.GravityOn) { L.Add("gravity back on after " + Years(yrs) + " off: this system's worlds fall back under their pulls"); L.Add("rubble and rings falling back on their new orbits; the stars round you swinging back about their paths (the disc's epicycles, about 160 million years round), a cluster's about its middle; swollen stars and giants falling back in on themselves in their free-fall time; a hole's gas and stars falling back on new orbits round it"); return; }
            L.Add("gravity off " + Years(yrs) + ": everything goes straight on at the speed it had");
            if (_system != null && !L.Exists(x => x.Contains("times its size"))) L.Add("its star blown " + (StarSwell >= 100 ? StarSwell.ToString("N0") : StarSwell.ToString("0.#")) + " times its size by its own pressure");
            if (_near >= 0 && _near < _galaxies.Count)
            {
                var g = _galaxies[_near]; double rKm = g.Scale / Units.Km(1), crossYrs = rKm / Math.Max(1, _rimSpeed) / 31557600.0, flown = yrs / crossYrs;
                L.Add("its galaxy's stars flown " + (flown < 0.01 ? "under a hundredth" : (flown * 100).ToString("N0") + "%") + " of its width; they cross it in " + Years(crossYrs));
            }
        }
        // with gravity off, a world: a gas giant swells as a star does (it is a ball of gas held only by its pull), by its own free-fall
        // time root(R^3 / GM); a rocky world keeps its shape (its rock's strength holds it), but its air streams off at about its sound
        // speed and its seas boil, their pressure gone below water's own
        private double WorldSwell(SpaceGen.Body b)
        {
            if (!Flown || b == null || !b.Giant || b.MassKg <= 0) return 1;
            double R = b.RadiusUnits / Units.Km(1) * 1000, tff = Math.Sqrt(R * R * R / (Physics.G * b.MassKg));
            return SwellNow(tff);
        }
        private void WorldGravityLines(List<string> L, SpaceGen.Body b, bool water)
        {
            if (!Flown || b == null) return; double secs = Math.Max(0, OffYears) * 31557600.0;
            if (b.Giant) { L.Add("gravity off: swollen " + WorldSwell(b).ToString(WorldSwell(b) >= 100 ? "N0" : "0.#") + " times its size, its gas flying apart"); return; }
            L.Add("gravity off: its rock holds its shape; its air streams off at about 340 m/s, its edge now " + Units.Describe(Units.Km(0.34 * secs)) + " out");
            if (water) L.Add("its seas boiling away, the pressure on them gone below water's own, and freezing at the top as they boil");
        }
        // how far a disc galaxy has turned (the distance at its rim speed, in its own units), to now, or to when gravity went (from there
        // its stars fly on, not round); and your own radius in it when you are within it (you ride round with your stars), 0 outside
        private float GalaxyTurn(Galaxy g, Mat4 pg, out float camR)
        {
            camR = 0; bool ball; if (!_ballType.TryGetValue(g.Type, out ball)) { ball = false; foreach (var e in _content.OfKind("galaxies.unt", "galaxy")) if (e.Id == g.Type) ball = e.Get("bodies-spread", "disc") == "ball"; _ballType[g.Type] = ball; }
            if (ball) return 0;
            double years = Flown ? _gravityOffYear : _simYears, kmPerUnit = g.Scale / Units.Km(1), d = _rimSpeed * years * 31557600.0 / Math.Max(1e-30, kmPerUnit);
            double rx = _cx - g.X, ry = _cy - g.Y, rz = _cz - g.Z, s2 = (double)g.Scale * g.Scale;
            double lx = (rx * pg.M11 + ry * pg.M12 + rz * pg.M13) / s2, lz = (rx * pg.M31 + ry * pg.M32 + rz * pg.M33) / s2, lr = Math.Sqrt(lx * lx + lz * lz);
            if (lr > 1e-6 && lr < 1.5) camR = (float)lr;
            return (float)(d % (2 * Math.PI * 1e6));   // (kept in float's reach)
        }
        // ── the expansion of space (content/physics.unt @law cosmos: the Hubble rate and dark energy's share): the scale factor through
        // time in a flat universe of matter and dark energy, a(t) proportional to sinh^(2/3)(1.5 root(OmegaL) H0 t), today's age where a
        // is 1; every place here is kept on the stretching grid (comoving), its true distance now that times the scale factor ──
        private double H0PerYear { get { return _hubble / 3.0857e19 * 31557600.0; } }   // (km/s per megaparsec, in one a year)
        private double OmegaL { get { return _darkEnergy > 0 && _darkEnergy < 1 ? _darkEnergy : 0.69; } }
        private double AgeNow { get { double ol = OmegaL; return 2 / (3 * H0PerYear * Math.Sqrt(ol)) * Asinh(Math.Sqrt(ol / (1 - ol))); } }
        private static double Asinh(double x) { return Math.Log(x + Math.Sqrt(x * x + 1)); }
        // the scale factor at the time now in the game (the game's years from today's age), 1 at its start
        private double ScaleNow
        {
            get
            {
                double ol = OmegaL, k = 1.5 * Math.Sqrt(ol) * H0PerYear, t0 = AgeNow, t = Math.Max(1e3, t0 + _simYears);
                return Math.Pow(Math.Sinh(k * t) / Math.Sinh(k * t0), 2.0 / 3.0);
            }
        }
        // the Hubble rate now in the game (km/s per megaparsec): H0 root(Om a^-3 + OmegaL)
        // a galaxy drawn at its true distance now: space stretched by the scale factor between you and it (beyond about ten million light-
        // years: nearer, galaxies' own groups are bound by their pulls and do not stretch apart; between five and ten eased in), its own
        // size kept (a galaxy is held together by its own pull); its place on the grid kept for clicks and flights
        private void Stretched(ref Mat4 pg)
        {
            double a = ScaleNow; if (Math.Abs(a - 1) < 1e-6) return;
            double dx = pg.M41, dy = pg.M42, dz = pg.M43, ly = Math.Sqrt(dx * dx + dy * dy + dz * dz) * Units.LightYearsPerUnit, q = Math.Max(0, Math.Min(1, (ly - 5e6) / 5e6));
            float k = (float)(1 + (a - 1) * q * q * (3 - 2 * q)); pg.M41 *= k; pg.M42 *= k; pg.M43 *= k;
        }
        private double HubbleNow { get { double a = ScaleNow, ol = OmegaL; return _hubble * Math.Sqrt((1 - ol) / (a * a * a) + ol); } }
        // a far thing's card: its distance on the stretching grid, its true distance now, how fast that grows, the universe's age
        private void ExpansionLines(List<string> L, double distUnits)
        {
            double a = ScaleNow, ly = distUnits * Units.LightYearsPerUnit; if (ly < 5e6) return;   // (bound by gravity nearer: galaxies' own groups do not stretch apart)
            double mpc = ly / 3.2616e6 * a, v = HubbleNow * mpc;
            L.Add("its true distance now " + Units.Describe(distUnits * a) + ", growing " + (v >= 1e5 ? (v / 299792.458).ToString("0.##") + " times light's speed" : v.ToString("N0") + " km/s") + " (space stretching)");
            if (Math.Abs(a - 1) > 1e-4) L.Add("space stretched " + a.ToString(a >= 10 ? "N0" : "0.####") + " times since you began; the universe " + Years(AgeNow + _simYears) + " old");
        }
        // with gravity off, a cluster's star flies on at its own speed in its cluster: the spread of their speeds about 10 km/s in an old
        // globular cluster, about 1 km/s in a young open one; its flight so far, universe units
        private void ClusterDrift(int q, bool old, out double dx, out double dy, out double dz)
        {
            dx = dy = dz = 0; if (!Flown) return; double km = (old ? 10.0 : 1.0) * DriftYears(2 * Math.PI / (old ? 1e6 : 1e7)) * 31557600.0, u = Units.Km(km);
            dx = (Hash01(q, 11) * 2 - 1) * 1.7 * u; dy = (Hash01(q, 12) * 2 - 1) * 1.7 * u; dz = (Hash01(q, 13) * 2 - 1) * 1.7 * u;   // (a uniform spread of a root-three width: the same spread as the speeds')
        }
    }
}
