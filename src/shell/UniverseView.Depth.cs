// Universe · shell · the universe view, its part: the depth (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;
using Universe.Platform;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── down into a solid world: in its matter, Page Down digs down and Page Up climbs back, through its layers (content @layer,
        // src/gen/BodyLayers.cs) to its middle; wherever you are, the matter is that layer's own (its mix's minerals), and its
        // card says the layer, how deep, and the heat, pressure and density there ──
        private double _depthKm;                                                        // how far below the ground (km); 0 at the ground
        private const double EarthGR = 9.80665 * 6.371e6;                               // Earth's surface gravity times its radius (m2/s2): pressures scale with it

        // the world you are on: its layers, and its own surface gravity times its radius (GM/R)
        private List<BodyLayer> DepthLayers() { return _ground != null && _ground.Solid ? BodyLayers.Of(_content).ForWorld(_ground.Kind) : null; }
        private double WorldGR()
        {
            double rM = _ground.Radius * 1000, kg = 0;
            if (_planet >= 0 && _system != null && _planet < _system.Bodies.Count) kg = _system.Bodies[_planet].MassKg;
            else if (_planet == -2 && _groundExtra != null) kg = _groundExtra.Mass * 1.989e30;
            return kg > 0 ? Physics.G * kg / Math.Max(1, rM) : EarthGR;
        }
        // the seed of the world's own make-up, the same its card uses (src/gen/BodyLayers.cs Bulk): a planet's or moon's by its place in its
        // system, a small body's fixed when it was made
        private ulong WorldMakeSeed()
        {
            if (_planet >= 0) return WorldSeed(_planet) + 1UL;
            if (_planet == -2 && _groundExtra != null) return _groundExtra.MakeSeed;
            return 0;
        }
        // a layer's mix as this world has it: scattered from its kind's by the layer's seed in this world (no scatter where the mix has none)
        private string OwnMix(string mix, string layerId) { ulong s = WorldMakeSeed(); return s != 0 && mix != null ? _matter.VaryMix(mix, BodyLayers.LayerSeed(s, layerId)) : mix; }
        private double DepthShare() { return _ground == null ? 1 : Math.Max(0, 1 - _depthKm / Math.Max(1e-9, _ground.Radius)); }
        private BodyLayer DepthLayer() { var l = DepthLayers(); return l != null && _depthKm > 0 ? BodyLayers.At(l, DepthShare()) : null; }

        // Page Down / Page Up in the matter of a solid world: a tenth of your depth a second (a hundred metres a second at the ground's
        // first metres; Shift four times faster), never above the ground or past the middle
        private void DigStep(Platform.Input input, double dt)
        {
            if (_ground == null || !_ground.Solid || DepthLayers() == null || DepthLayers().Count == 0) { _depthKm = 0; return; }
            int dir = (input.IsDown(0x22) ? 1 : 0) - (input.IsDown(0x21) ? 1 : 0); if (dir == 0) return;
            double before = _depthKm, sp = Math.Max(0.1, _depthKm * 0.6) * dt * (input.IsDown(0x10) ? 4 : 1);
            _depthKm = Math.Max(0, Math.Min(_ground.Radius, _depthKm + dir * sp));
            { var dl0 = DepthLayer(); if (dl0 != null) _matter.TempK = dl0.TempK(DepthShare()); }   // (the heat rising as you go down)
            var a = before > 0 ? BodyLayers.At(DepthLayers(), Math.Max(0, 1 - before / _ground.Radius)) : null; var b = DepthLayer();
            if (a != b) { _matterSpot = ulong.MaxValue; SetMatter(); MenuChanged = true; }   // (into another layer: its own matter)
        }

        // the matter of the layer you are in (called from SetMatter while below the ground); false at the ground itself
        private bool SetDepthMatter(double ux, double uy, double uz)
        {
            var l = DepthLayer(); if (l == null) return false;
            string mix = OwnMix(l.Mix == "ground" || l.Mix == null ? (_ground.MixId ?? _ground.Kind) : l.Mix, l.Id);   // (this world's own)
            ulong spot = (ulong)(long)(ux * 1e7) * 73856093UL ^ (ulong)(long)(uy * 1e7) * 19349663UL ^ (ulong)(long)(uz * 1e7) * 83492791UL ^ (ulong)l.Id.GetHashCode() * 2654435761UL;
            if (_matterSpot == spot && _matterPlanet == _planet) return true;
            _matterSpot = spot; _matterPlanet = _planet; _mox = ux; _moy = uy; _moz = uz;
            bool water = l.Matter == "water";
            _matter.Set(water ? "sea" : mix, water, false, 0.5f, 0.45f, 0.4f, spot);
            _matter.SetGround(_ground.Radius - _depthKm, ux, uy, uz, water ? null : mix, _ground.GrainSeed ^ (ulong)l.Id.GetHashCode(), 0, 0);
            _matter.SetLife(false); _matter.SetAirAbove(null); _matter.TempK = l.TempK(DepthShare());   // (the layer's heat at this depth)                          // (no air down here, and no life in rock this deep)
            return true;
        }

        // the card's lines for where you are below the ground: the layer, how deep, its heat, pressure and density there
        private void DepthWords(List<string> L)
        {
            var l = DepthLayer(); if (l == null) return; double share = DepthShare();
            L.Add("below the ground " + Units.Describe(Units.Km(_depthKm)) + ", in its " + l.Name.ToLowerInvariant() + (l.Matter == "metal" && l.Id == "outer-core" ? " (molten)" : ""));
            double t = l.TempK(share), p = l.PressureGpa(share) * WorldGR() / EarthGR, d = l.Density(share);
            var parts = new List<string>();
            if (t > 0) parts.Add(t.ToString("N0") + " K");
            if (l.PHi > 0) parts.Add((p >= 1 ? p.ToString("N0") + " GPa" : (p * 9869.2).ToString("N0") + " atmospheres") + " pressing");
            if (d > 0) parts.Add(d.ToString("0.#") + " g/cc");
            if (parts.Count > 0) L.Add(string.Join(", ", parts));
            if (l.Says.Length > 0) L.Add(l.Says);
        }
    }
}
