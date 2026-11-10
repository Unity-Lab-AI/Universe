// Universe · core · the one tree of things, from the universe down to a quark: each thing knows what it is in (its parent),
// so wherever you are, the chain from the universe to what you are at is one list, the same at every scale.
using System;
using System.Collections.Generic;

namespace Universe.Core
{
    /// <summary>The rungs of the one ladder, biggest first: each thing is in one of the rung above it.</summary>
    internal enum Rung { Universe, Galaxy, Cluster, Nebula, Hole, Star, World, Layer, Gas, Grain, Crystal, Molecule, Atom, Electron, Nucleus, Nucleon, Quark }

    /// <summary>One thing of the tree: its rung, its name, what it is made of (when known), and what it is in.</summary>
    internal sealed class Thing
    {
        public readonly Rung Rung; public readonly string Name; public readonly string MakeUp; public readonly Thing Parent;
        public Thing(Rung rung, string name, string makeUp, Thing parent) { Rung = rung; Name = name ?? ""; MakeUp = makeUp; Parent = parent; }
        /// <summary>A thing inside this one.</summary>
        public Thing In(Rung rung, string name, string makeUp = null) { return new Thing(rung, name, makeUp, this); }
        /// <summary>The chain from the top down to this thing.</summary>
        public List<Thing> Chain() { var l = new List<Thing>(); for (var t = this; t != null; t = t.Parent) l.Insert(0, t); return l; }
        /// <summary>The chain written short (the universe left out, as it holds everything): galaxy › star › world › … › this.</summary>
        public string Words()
        {
            var parts = new List<string>();
            foreach (var t in Chain()) if (t.Rung != Rung.Universe && t.Name.Length > 0) parts.Add(t.Name);
            return string.Join(" › ", parts);
        }
    }
}
