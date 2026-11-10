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
        private double _gravityOffYear = double.NaN;
        public void ToggleGravity() { Window.GravityOn = !Window.GravityOn; _gravityOffYear = Window.GravityOn ? double.NaN : _simYears; SpaceGen.GravityOffYear = _gravityOffYear; MenuChanged = true; }
        private string GravityWords() { return Window.GravityOn || double.IsNaN(_gravityOffYear) ? "" : " · gravity off for " + Years(Math.Max(0, _simYears - _gravityOffYear)) + ": every body straight on, every star blown apart"; }
        // how much the system's star has swollen: its own pressure unopposed grows it at about its sound speed, doubling it in about its
        // sound-crossing time (swell-over-freefall times its free-fall time root(R^3 / GM): about an hour for a star of one solar mass)
        private double StarSwell
        {
            get
            {
                if (Window.GravityOn || double.IsNaN(_gravityOffYear) || _system == null) return 1;
                double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol), tff = Math.Sqrt(R * R * R / gm);
                return Math.Min(1e6, 1 + Math.Max(0, _simYears - _gravityOffYear) * 31557600.0 / (_swellOverFreefall * tff));
            }
        }
        private double StarSwellSpeedKmS() { if (_system == null) return 0; double R = _system.StarRadius / Units.Km(1) * 1000, gm = Physics.GmSol * Math.Max(0.01, _system.MassSol); return R / (_swellOverFreefall * Math.Sqrt(R * R * R / gm)) / 1000; }
        private double StarRadiusNow { get { return _system != null ? _system.StarRadius * StarSwell : 0; } }
        // a galaxy's stars flung off with gravity off: how far each has flown in the galaxy's own frame (the disc's on their tangents at the
        // rim speed, a round galaxy's every way: negative), and the camera's own star's flight taken out of the placing, so you ride with it
        private readonly Dictionary<string, bool> _ballType = new Dictionary<string, bool>();
        private float GalaxyDrift(Galaxy g, ref Mat4 pg)
        {
            if (Window.GravityOn || double.IsNaN(_gravityOffYear)) return 0;
            double years = Math.Max(0, _simYears - _gravityOffYear), kmPerUnit = g.Scale / Units.Km(1);
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
    }
}
