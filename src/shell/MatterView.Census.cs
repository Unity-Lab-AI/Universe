// Universe · shell · the matter view, its part: the census (one class in several files: src/shell/MatterView.cs is its start)
using System;
using System.Collections.Generic;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        // ── what there is to pick at this size, counted from what was laid out this frame: said on the card, written to the zoom log
        // at every tenfold, so a size with nothing real in it shows as one (a click there still flies, to a spot on the face or a
        // point ahead, but that is a place, not a thing) ──
        private readonly int[] _census = new int[9];
        private static readonly string[] CensusNames = { "grains", "atoms", "", "electrons", "quarks", "protons and neutrons", "molecules", "nuclei", "things on the surface" };
        private void Count()
        {
            Array.Clear(_census, 0, _census.Length);
            foreach (var b in _bodies) if (b.Kind >= 0 && b.Kind < _census.Length) _census[b.Kind]++;
        }
        /// <summary>How many real things are laid out to pick at this size (the face's spots, a grid of places, not counted).</summary>
        public int RealPickable { get { int n = 0; for (int k = 0; k < _census.Length; k++) if (k != 2) n += _census[k]; return n; } }
        /// <summary>The census in words: what there is to pick here, or that there is nothing but the face itself.</summary>
        public string CensusWords()
        {
            var parts = new List<string>();
            for (int k = 0; k < _census.Length; k++) if (k != 2 && _census[k] > 0) parts.Add(_census[k].ToString("N0") + " " + CensusNames[k]);
            if (parts.Count > 0) return "to pick here: " + string.Join(", ", parts);
            return _census[2] > 0 ? "to pick here: nothing real at this size, only places on the face" : "to pick here: nothing at this size";
        }
    }
}
