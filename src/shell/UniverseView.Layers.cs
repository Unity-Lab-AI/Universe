// Universe · shell · the universe view, its part: layers (one class in several files: src/shell/UniverseView.cs is its start)
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
        // ── a star's layers from its surface down to its core (content/stars.unt @layer): which the system's star has, which you are in ──
        // (one reader for every body's layers: src/gen/BodyLayers.cs)
        private string SystemStarId() { return _sysStar >= 0 && _sysStar < _localCount ? _kinds[_localKind[_sysStar]].Id : ""; }
        private List<BodyLayer> StarLayersFor(string starId) { return BodyLayers.Of(_content).For(BodyLayers.Stars, starId); }
        private BodyLayer StarLayerAt(double share) { return BodyLayers.At(StarLayersFor(SystemStarId()), share); }
        // the layer of the system's star you are in now, by your share of its radius from its middle; null outside it (or at its very face)
        private BodyLayer StarLayerHere()
        {
            if (_system == null || _ground != null || _sysStar < 0) return null;
            double sd = Math.Sqrt(_ox * _ox + _oy * _oy + _oz * _oz), R = StarRadiusNow; if (R <= 0 || sd >= R * 0.9985) return null;
            return StarLayerAt(sd / R);
        }
        // a layer's temperature and density at a depth: each runs from the layer's top to its bottom (the density on a log scale)
        private static double LayerTempK(BodyLayer l, double share) { double t = l.TempK(share); return t > 0 ? t : 6000; }
        private static double LayerDensity(BodyLayer l, double share) { double d = l.Density(share); return d > 0 ? d : 2e-7; }
        private static string DensityWords(double gcc) { return gcc >= 1 ? gcc.ToString(gcc >= 100 ? "N0" : "0.#") + " g/cc" : gcc >= 1e-3 ? (gcc * 1000).ToString("0.#") + " mg/cc" : gcc.ToString("0.#E+0") + " g/cc"; }
        // the glow inside a star, by its heat: orange at a few thousand K, white-yellow at a star's face, blue-white in the millions
        private static void StarGlow(double tempK, out float r, out float g, out float b)
        {
            double t = Math.Max(0, Math.Min(1, Math.Log10(Math.Max(1000, tempK) / 3000.0) / Math.Log10(1e7 / 3000.0)));
            r = 1f; g = (float)(0.45 + 0.55 * Math.Min(1, t * 2.2)); b = (float)(0.15 + 0.85 * Math.Min(1, t * 1.6));
        }
    }
}
