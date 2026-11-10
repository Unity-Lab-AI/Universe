// Universe · gen · gravity as a live force in the open star system: the star and every planet and moon pulling on each other,
// moment by moment (Newton), stepped with a symplectic leapfrog so the orbits keep their energy over millions of turns. It
// starts from each body's Kepler orbit at the moment the system is opened (its place and its speed there), so it begins as the
// orbits were and from then on the bodies' own pulls shape them: orbits precess, resonances tug, a moon's planet carries it.
// With gravity switched off (content/physics.unt @law gravity-off) the pulls are gone from that moment and every body,
// the star too, carries straight on at the velocity it had.
using System;

namespace Universe.Gen
{
    internal sealed class SystemGravity
    {
        // units: astronomical units, years, solar masses, so G M is 4 pi^2 M
        private const double FourPi2 = 4 * Math.PI * Math.PI;
        private readonly int _n;                                                        // the star (0) and its bodies (1..)
        private readonly double[] _x, _y, _z, _vx, _vy, _vz, _gm, _ax, _ay, _az;
        private double _dt;
        /// <summary>The time (years) the state is at.</summary>
        public double T { get; private set; }
        /// <summary>True when time ran too fast to follow step by step and the bodies were set back on their Kepler orbits.</summary>
        public bool StandIn { get; private set; }
        /// <summary>How many steps the last frame took.</summary>
        public int LastSteps { get; private set; }
        private const int MostSteps = 200000;                                           // (a frame's worth of steps at the most: past that, Kepler stands in)

        public SystemGravity(SpaceGen.StarSystem sys, double t)
        {
            _n = sys.Bodies.Count + 1;
            _x = new double[_n]; _y = new double[_n]; _z = new double[_n]; _vx = new double[_n]; _vy = new double[_n]; _vz = new double[_n];
            _ax = new double[_n]; _ay = new double[_n]; _az = new double[_n]; _gm = new double[_n];
            // each one's pull: the star's from its mass; a planet's from its own moons' orbits (4 pi^2 a^3 / P^2), so their orbits are
            // what its pull holds, else from its own mass; a moon's from its mass
            _gm[0] = FourPi2 * Math.Max(1e-3, sys.MassSol);
            double au = Units.Au(1), pMin = double.MaxValue;
            for (int i = 0; i < sys.Bodies.Count; i++)
            {
                var b = sys.Bodies[i]; _gm[i + 1] = FourPi2 * Math.Max(0, b.MassKg) / 1.989e30;
                if (b.Period > 0) pMin = Math.Min(pMin, b.Period * Math.Pow(1 - Math.Min(0.95, Math.Max(0, b.Ecc)), 1.5));   // (its quickest stretch: the pass by its closest point, P (1 - e)^1.5)
            }
            for (int i = 0; i < sys.Bodies.Count; i++)
            {
                var m = sys.Bodies[i]; if (m.Parent < 0 || m.Period <= 0) continue;
                double a = m.OrbitUnits / au; _gm[m.Parent + 1] = Math.Max(_gm[m.Parent + 1], FourPi2 * a * a * a / (m.Period * m.Period) - _gm[i + 1]);
            }
            _dt = pMin < double.MaxValue ? pMin / 60 : 0.01;                            // (sixty steps to the quickest body's closest pass)
            SetFromKepler(sys, t);
        }

        // the bodies on their Kepler orbits at time t: their places, and their velocities from their places a moment either side
        private void SetFromKepler(SpaceGen.StarSystem sys, double t)
        {
            double h = Math.Max(1e-9, _dt * 0.01), au = Units.Au(1);
            var p0 = SpaceGen.KeplerPlaces(sys, t - h); var p1 = SpaceGen.KeplerPlaces(sys, t); var p2 = SpaceGen.KeplerPlaces(sys, t + h);
            _x[0] = _y[0] = _z[0] = _vx[0] = _vy[0] = _vz[0] = 0;
            for (int i = 1; i < _n; i++)
            {
                int k = (i - 1) * 3;
                _x[i] = p1[k] / au; _y[i] = p1[k + 1] / au; _z[i] = p1[k + 2] / au;
                _vx[i] = (p2[k] - p0[k]) / (2 * h * au); _vy[i] = (p2[k + 1] - p0[k + 1]) / (2 * h * au); _vz[i] = (p2[k + 2] - p0[k + 2]) / (2 * h * au);
            }
            // the star moves too: the whole system's momentum is nothing, so the star carries the rest's back (it wobbles round the middle)
            double px = 0, py = 0, pz = 0; for (int i = 1; i < _n; i++) { px += _gm[i] * _vx[i]; py += _gm[i] * _vy[i]; pz += _gm[i] * _vz[i]; }
            _vx[0] = -px / _gm[0]; _vy[0] = -py / _gm[0]; _vz[0] = -pz / _gm[0];
            T = t; Accelerations(true);
        }

        private void Accelerations(bool pull)
        {
            Array.Clear(_ax, 0, _n); Array.Clear(_ay, 0, _n); Array.Clear(_az, 0, _n); if (!pull) return;
            for (int i = 0; i < _n; i++)
                for (int j = i + 1; j < _n; j++)
                {
                    double dx = _x[j] - _x[i], dy = _y[j] - _y[i], dz = _z[j] - _z[i], r2 = dx * dx + dy * dy + dz * dz + 1e-24, inv = 1 / (r2 * Math.Sqrt(r2));
                    double fi = _gm[j] * inv, fj = _gm[i] * inv;
                    _ax[i] += dx * fi; _ay[i] += dy * fi; _az[i] += dz * fi; _ax[j] -= dx * fj; _ay[j] -= dy * fj; _az[j] -= dz * fj;
                }
        }

        /// <summary>Carries the system on to time t (years), its gravity gone from <paramref name="offYear"/> on (NaN: never).
        /// Back in time, or too far for a frame's steps, it is set on its Kepler orbits there instead.</summary>
        public void Advance(SpaceGen.StarSystem sys, double t, double offYear, double onYear = double.NaN)
        {
            LastSteps = 0;
            if (t < T - 1e-12) { SetFromKepler(sys, t); StandIn = true; return; }
            double span = t - T; if (span <= 0) return;
            long need = (long)Math.Ceiling(span / _dt);
            if (need > MostSteps) { if (double.IsNaN(offYear) || t <= offYear) { SetFromKepler(sys, t); StandIn = true; return; } need = MostSteps; }
            StandIn = false; double dt = span / Math.Max(1, need);
            for (long s = 0; s < need; s++)
            {
                bool pull = double.IsNaN(offYear) || T + dt <= offYear || (!double.IsNaN(onYear) && T + dt > onYear);   // (off only between the two)
                // kick, drift, kick (leapfrog: time-reversible, keeps the energy)
                for (int i = 0; i < _n; i++) { _vx[i] += _ax[i] * dt * 0.5; _vy[i] += _ay[i] * dt * 0.5; _vz[i] += _az[i] * dt * 0.5; }
                for (int i = 0; i < _n; i++) { _x[i] += _vx[i] * dt; _y[i] += _vy[i] * dt; _z[i] += _vz[i] * dt; }
                Accelerations(pull);
                for (int i = 0; i < _n; i++) { _vx[i] += _ax[i] * dt * 0.5; _vy[i] += _ay[i] * dt * 0.5; _vz[i] += _az[i] * dt * 0.5; }
                T += dt;
            }
            T = t; LastSteps = (int)need;
        }

        /// <summary>Where body i (0 the star) is against the star now, in universe units.</summary>
        public void Place(int i, out double x, out double y, out double z)
        {
            double au = Units.Au(1); x = (_x[i] - _x[0]) * au; y = (_y[i] - _y[0]) * au; z = (_z[i] - _z[0]) * au;
        }
        /// <summary>Body i's speed against the star now, km/s.</summary>
        public double SpeedKmS(int i)
        {
            double vx = _vx[i] - _vx[0], vy = _vy[i] - _vy[0], vz = _vz[i] - _vz[0];
            return Math.Sqrt(vx * vx + vy * vy + vz * vz) * 1.495978707e8 / 31557600.0;
        }
    }
}
