// Universe · shell · the universe view, its part: the tree (one class in several files: src/shell/UniverseView.cs is its start)
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Gen;

namespace Universe.Shell
{
    internal sealed partial class UniverseView
    {
        // ── the one tree (src/core/Thing.cs): where you are, from the universe down to the smallest thing you are at — the galaxy,
        // the star, the world or the hole, the layer of it you are in, and below that the matter view's own (the gas or the grain,
        // its crystal, the atom, what is inside it) ──
        private Thing Here()
        {
            var t = new Thing(Rung.Universe, "the universe", null, null);
            if (_near >= 0 && _near < _galaxies.Count) t = t.In(Rung.Galaxy, _galaxies[_near].Name);
            bool inMatter = _matterScale > 0 || _spaceScale > 0; int sk = inMatter ? _matter.SpaceKind : 0;
            if (_spaceScale > 0 && sk == 12) return t.In(Rung.Hole, _tName.Length > 0 ? _tName : "a black hole");
            if (_spaceScale > 0 && sk >= 0 && sk <= 6) t = t.In(sk <= 1 ? Rung.Cluster : Rung.Nebula, _tName.Length > 0 ? _tName : sk <= 1 ? "a star cluster" : "a nebula");
            if (_system != null) t = t.In(Rung.Star, "a " + _system.StarName.ToLowerInvariant());
            if (_ground != null) t = t.In(Rung.World, GroundKind());
            var dl = DepthLayer(); if (dl != null) t = t.In(Rung.Layer, "its " + dl.Name.ToLowerInvariant(), dl.Mix);   // (below a solid world's ground)
            var lay = LayerHere(); if (lay != null) t = t.In(Rung.Layer, "its " + lay.Name.ToLowerInvariant(), lay.Matter);
            var sl = StarLayerHere(); if (sl != null && _spaceScale <= 0) t = t.In(Rung.Layer, "its " + sl.Name.ToLowerInvariant(), sl.MakeUp);
            if (inMatter) t = _matter.Below(t);
            return t;
        }
        // the card's line saying where in the tree you are (one short clause: the chain itself)
        private string HereWords() { var w = Here().Words(); return w.Length > 0 ? "you are in: " + w : null; }
        // the matter's card with where it is in the tree under its first line
        private List<string> MatterCard()
        {
            var L = _matter.Card(); var w = HereWords(); if (w != null && L.Count > 0) L.Insert(1, w);
            if (L.Count > 0) { var dw = new List<string>(); DepthWords(dw); L.InsertRange(Math.Min(2, L.Count), dw); }   // (below the ground: the layer, how deep, its heat and pressure)
            return L;
        }
    }
}
