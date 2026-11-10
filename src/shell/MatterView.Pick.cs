// Universe Â· shell Â· the matter view, its part: pick (one class in several files: src/shell/MatterView.cs is its start)
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed partial class MatterView
    {
        /// <summary>Picks the grain or atom under the mouse (within about 24 pixels): the point you dive into eases to it.
        /// False if nothing is there.</summary>
        public bool Pick(int mx, int my)
        {
            if (!_haveVp) return false;
            // a click always picks something: the thing it is on, else the nearest thing within a good reach of it (a sixth of the
            // screen), else the spot on the surface under it, else the point one view ahead along it (looking out across a face, or
            // up, there was nothing to pick at all)
            double best = Math.Pow(Math.Max(160, _lastH * 0.15), 2); Body pick = null; bool pickInside = false; double pickDepth = 0; var m = _lastVp;
            foreach (var b in _bodies)
            {
                float x = (float)((b.X - _fx) / _scale + _ex), y = (float)((b.Y - _fy) / _scale + _ey), z = (float)((b.Z - _fz) / _scale + _ez);
                float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
                if (cw <= 0) continue;
                double sx = (cx / cw * 0.5 + 0.5) * _lastW, sy = (0.5 - cy / cw * 0.5) * _lastH, d2 = (sx - mx) * (sx - mx) + (sy - my) * (sy - my);
                double rpx = b.Size / _scale / cw / Math.Tan(0.45) * _lastH * 0.5;            // its own size on screen counts too
                // the smallest thing the click is on wins (so a water molecule on a clay plate is the molecule, not the plate);
                // else the nearest within reach
                // of the things the click is on: the front one wins (never one behind it), except a much smaller thing on or in
                // the one in front (an electron of that atom, a molecule on a plate); else the nearest within reach
                bool on = d2 < rpx * rpx, pickOn = pick != null && pickInside;
                double depth = cw * _scale;                                                  // how far in front of you (km)
                bool wins;
                if (!on) wins = !pickOn && d2 < best;
                else if (!pickOn) wins = true;
                else if (b.Size < pick.Size * 0.3) wins = depth <= pickDepth + pick.Size;    // smaller, and on or in front of the pick
                else if (pick.Size < b.Size * 0.3) wins = pickDepth > depth + b.Size;        // bigger: only if the pick is behind it
                else wins = depth < pickDepth;                                               // alike: the one in front
                if (wins) { best = d2; pick = b; pickInside = on; pickDepth = depth; }
            }
            if (pick == null)
            {
                // nothing right there: the spot you clicked on the surface (where the line under the mouse meets it)
                var v = _lastView; var right = new Vec3(v.M11, v.M21, v.M31); var up = new Vec3(v.M12, v.M22, v.M32); var fwd = Vec3.Cross(up, right).Normalized;
                double t = Math.Tan(0.45), nx = mx / (double)Math.Max(1, _lastW) * 2 - 1, ny = 1 - my / (double)Math.Max(1, _lastH) * 2, aspect = _lastW / (double)Math.Max(1, _lastH);
                for (int sign = 0; sign < 2; sign++)
                {
                    var f = sign == 0 ? fwd : new Vec3(-fwd.X, -fwd.Y, -fwd.Z);
                    var d = (f + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
                    float cw = d.X * m.M14 + d.Y * m.M24 + d.Z * m.M34 + m.M44;              // in front of you?
                    if (cw <= 0 || d.Y >= -1e-4f) continue;
                    double k = _ey / d.Y;                                                    // where the line meets the level of the point you dive into
                    _tfx = _fx + (d.X * k - _ex) * _scale; _tfz = _fz + (d.Z * k - _ez) * _scale;
                    _picked = new Body { Kind = 2, X = _tfx, Y = 0, Z = _tfz, Size = _scale * 0.02 }; return true;
                }
                {
                    // the point one view ahead along the line under the mouse
                    var d = (fwd + right * (float)(nx * t * aspect) + up * (float)(ny * t)).Normalized;
                    _tfx = _fx + (d.X - _ex) * _scale; _tfy = _fy + (d.Y - _ey) * _scale; _tfz = _fz + (d.Z - _ez) * _scale;
                    _picked = new Body { Kind = 2, Free = true, X = _tfx, Y = _tfy, Z = _tfz, Size = _scale * 0.02 }; return true;   // (a point in space, not on the face: Free keeps it at its own height)
                }
            }
            _picked = pick; _tfx = pick.X; _tfz = pick.Z; _tfy = pick.Kind == 0 || (pick.Kind == 2 && !pick.Free) ? 0 : pick.Y;   // (a grain: to its top; a spot on the face: on the face, which is height nothing at the focus wherever the focus is. Its stored height is against the face at the focus of the moment it was picked, and as the focus eases over to it the face's reference moves and that height does not: the camera ended a residual height off the face, on a slope a hundred and eighty nanometres, thirty views at six nanometres over a lattice three deep, floating in the solid with nothing round it: black from the crystal to the nucleus, and on the way out until above it)
            return true;   // whatever you pick is gone to, in any layer (a grain: to its top)   // grains and atoms are flown to; electrons, nucleons and quarks are only named
        }

        /// <summary>Puts the point you dive into right under you (km east and north of where this spot's matter began), so the
        /// grains pass by as you fly over them.</summary>
        public void Follow(double east, double north) { _fx = _tfx = east; _fz = _tfz = north; _fy = _tfy = 0; _picked = null; }

        /// <summary>Moves the point under you (km east and north), as W A S D fly you across.</summary>
        public void Move(double east, double north) { _fx += east; _fz += north; _tfx = _fx; _tfz = _fz; }

        /// <summary>Where the picked grain or atom is on screen (pixels) and how big, for its ring; false if nothing is picked.</summary>
        /// <summary>Picks the nearest of a kind of thing to the point you dive into, as a click on it would (atom, electron,
        /// nucleus, proton, quark, a grain, a spot on a grain's face): for the tour.</summary>
        /// <summary>How quickly the point you dive into glides to what is picked (a share a second): slow on the tour, a film's glide.</summary>
        public double Glide = 4; private double _lastLeftX = double.NaN, _lastLeftZ = double.NaN;
        /// <summary>How far the point you dive into still is from what is picked, in shares of the view's size (0 with nothing
        /// picked): the tour flies in only once it is there, so it never flies past what it shows.</summary>
        public double PickOff { get { if (_picked == null) return 0; double dx = _tfx - _fx, dz = _tfz - _fz; return Math.Sqrt(dx * dx + dz * dz) / Math.Max(1e-40, _scale); } }   // (across: how high it sits is the zoom's own)
        /// <summary>Zooming in by hand with nothing finer picked: the view locks onto the landmark at each size (the nearest atom,
        /// then its nucleus, then a proton or neutron, then a quark), so the wheel goes down into matter, never between it into
        /// nothing. Called on each wheel notch in, with the scale.</summary>
        public void Landmarks(double s)
        {
            if (_space) return;
            int k = _picked != null ? _picked.Kind : -1;
            double fx, fy, fz; var fe = Focus(out fx, out fy, out fz); double ar = fe != null ? AtomRadius(fe) : AtomSpacing * 0.5;
            if (s < Nucleon * 0.6) { if (k != 4) PickNearest("quark"); }
            else if (s < Nucleon * 8) { if (k != 5 && k != 4) PickNearest("proton"); }
            else if (s < ar * 0.5) { if (k != 7 && k != 5 && k != 4) PickNearest("nucleus"); }
            else if (s < AtomSpacing * 6) { if (k != 1 && k != 3 && k != 6 && k != 7 && k != 5 && k != 4) PickNearest("atom"); }
        }
        public void PickNearest(string what)
        {
            if (what == "bonded-atom")
            {
                // the atom next to this one: the nearest other atom (its neighbour in the crystal, sharing its outer electrons),
                // never the one just left
                Body nb = null; double nd = double.MaxValue;
                foreach (var b in _bodies)
                {
                    if (b.Kind != 1 || b == _picked) continue;
                    double dx = b.X - _fx, dy = b.Y - _yOff - _fy, dz = b.Z - _fz, d = dx * dx + dy * dy + dz * dz;
                    if (d < AtomSpacing * AtomSpacing * 0.25 || (_lastLeftX == b.X && _lastLeftZ == b.Z)) continue;   // not the one you are on, nor the one you came from
                    if (d < nd) { nd = d; nb = b; }
                }
                if (nb == null) return;
                if (_picked != null) { _lastLeftX = _picked.X; _lastLeftZ = _picked.Z; }
                _picked = nb; _tfx = nb.X; _tfz = nb.Z; _tfy = nb.Y; PickCount++; return;
            }
            int kind = what == "grain" ? 0 : what == "spot" ? 2 : what == "atom" ? 1 : what == "electron" ? 3 : what == "nucleus" ? 7 : what == "proton" ? 5 : what == "quark" ? 4 : -1; if (kind < 0) return;
            // an atom: the mineral's elements in turn (its oxygen is three atoms in five of every silicate and two in three of ice,
            // and the face's top layer is mostly its oxygen, so the nearest atom was oxygen every time); each pick takes the next
            // element the face has, and the nearest atom of that
            string wantSym = null;
            if (kind == 1)
            {
                var syms = new List<string>();
                foreach (var b in _bodies) if (b.Kind == 1 && b.Sym != null && !syms.Contains(b.Sym)) syms.Add(b.Sym);
                if (syms.Count > 1) { syms.Sort(string.CompareOrdinal); wantSym = syms[(int)(((long)PickCount + (long)(_seed & 0xFFFF)) % syms.Count)]; }
            }
            Body best = null; double bd = double.MaxValue;
            foreach (var b in _bodies)
            {
                if (b.Kind != kind || (kind == 5 && b.J != 1) || b == _picked) continue;
                if (wantSym != null && b.Sym != wantSym) continue;
                double dx = b.X - _fx, dy = b.Y - _yOff - _fy, dz = b.Z - _fz, d = dx * dx + dy * dy + dz * dz;
                if (kind == 3) d -= b.I * 1e-40;                                             // (the outer, valence, shell first, among equals)
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null)
            {
                // a spot or a grain where none is laid out to pick (an ice sheet or the sea, which have no grains: a level face), the
                // spot right under the point you dive into is the pick, as a click there would make it; never nothing, since every
                // stop below (the crystal, the atom, the nucleus) waits on it
                if (what == "spot" || what == "grain") { _picked = new Body { Kind = 2, X = _fx, Y = _fy, Z = _fz, Size = _scale * 0.02 }; _tfx = _fx; _tfz = _fz; _tfy = 0; PickCount++; }   // (on the face: height nothing)
                return;
            }
            _picked = best; _tfx = best.X; _tfz = best.Z; _tfy = best.Kind == 0 || (best.Kind == 2 && !best.Free) ? 0 : best.Y; PickCount++;   // (a grain or a spot on the face: on the face, height nothing at the focus; an atom, an electron, a nucleus: its own height in the lattice)
        }

        public bool PickedOnScreen(out double sx, out double sy, out double rpx)
        {
            sx = sy = rpx = 0; if (_picked == null || !_haveVp) return false; var m = _lastVp;
            if (_picked.Kind == 0 && _scale < Grain * 0.35) return false;               // you are at its face: no ring round it
            float x = (float)((_picked.X - _fx) / _scale + _ex), y = (float)((_picked.Y - _fy) / _scale + _ey), z = (float)((_picked.Z - _fz) / _scale + _ez);
            float cx = x * m.M11 + y * m.M21 + z * m.M31 + m.M41, cy = x * m.M12 + y * m.M22 + z * m.M32 + m.M42, cw = x * m.M14 + y * m.M24 + z * m.M34 + m.M44;
            if (cw <= 0) return false;
            sx = (cx / cw * 0.5 + 0.5) * _lastW; sy = (0.5 - cy / cw * 0.5) * _lastH;
            rpx = Math.Max(10, _picked.Size / _scale / cw / Math.Tan(0.45) * _lastH * 0.5 * 1.3); return true;
        }

        /// <summary>Lets go of the pick (the point you dive into stays where it is).</summary>
        public void Unpick() { _picked = null; _tfx = _fx; _tfz = _fz; _tfy = _fy; }

    }
}
