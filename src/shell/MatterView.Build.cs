// Universe Â· shell Â· the matter view, its part: build (one class in several files: src/shell/MatterView.cs is its start)
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
        private void Build(double s, double time, List<StarVertex> solid, List<StarVertex> glow, List<StarVertex> mesh, bool overlay)
        {
            _yOff = 0; _patchMesh.Clear(); _orbRecs.Clear(); _stoneMesh.Clear(); _stones.Clear();
            if (_space && _spaceKind == 12) { Singularity(glow, s, time); return; }          // inside a black hole: the fall is black, down to the singularity                                                 // (the grain's face: made afresh each frame from its kept points)
            if (!_space && !_noGrains)
            {
                long fgi, fgj; double fgx, fgy, fgz, frad, top; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
                if (GrainTop(fgi, fgj, fgx, fgy, fgz, frad, _fx, _fz, CoarseCell(s, frad), out top)) _yOff = -top;   // you stay the same height above it (on the face: above the coarse shape the face is drawn from, its finer detail only shading; measured against the fine shape the camera sat a few views under the drawn face at nanometres, looking up through it)
            }
            if (_space && s < _gasSpacing * 10 && s > AtomSpacing * 2)
            {
                // the gas round you: its atoms, each a point (far too small to see but as a speck), about a millimetre apart
                double a = _gasSpacing; long n = (long)Math.Min(16, Math.Ceiling(2 * s / a));
                for (long i = -n; i <= n; i++)
                    for (long j = -n; j <= n; j++)
                        for (long k = -n; k <= n; k++)
                        {
                            if (i == 0 && j == 0 && k == 0) continue;
                            var ge = GasAtom(i, j, k); double jx = (Hash(i, j, k + 91, _seed) - 0.5) * a, jy = (Hash(i, j, k + 92, _seed) - 0.5) * a, jz = (Hash(i, j, k + 93, _seed) - 0.5) * a;
                            float gf = (float)Fade(s, _gasSpacing * 10, _gasSpacing * 4);       // fading in as you come within about ten spacings
                            Add(glow, i * a + jx, k * a + jy, j * a + jz, s * 0.002, ge.R * 0.6f * gf, ge.G * 0.6f * gf, ge.B * 0.6f * gf, s);
                        }
                // the atom you dive at, the one ahead of you: a speck like the rest until you are within eight widths of it, where
                // its inside view takes over (it had been drawn only from there: from a micrometre down to a nanometre the card
                // named it and the screen was black), and a body at every size, so it can be picked: an atom in a gas, a bare
                // nucleus in a plasma, and so the way down to it on any body (a nebula's gas, a star's or a neutron star's plasma) begins
                {
                    var g0 = GasAtom(0, 0, 0);
                    if (g0 != null && s >= Math.Max(AtomRadius(g0), AtomSpacing) * 8)
                    {
                        float gf0 = (float)Math.Max(0.35, Fade(s, _gasSpacing * 10, _gasSpacing * 4));
                        Add(glow, 0, 0, 0, s * 0.0025, g0.R * 0.8f * gf0, g0.G * 0.8f * gf0, g0.B * 0.8f * gf0, s);
                        int nb = _bodies.Count; AddBody(_plasma ? 7 : 1, 0, 0, 0, 0, 0, Math.Max(AtomRadius(g0), s * 0.01), s); if (_bodies.Count > nb) _bodies[nb].Sym = g0.Symbol;
                    }
                }
            }
            // grains: lumpy stones, each its own mineral from the mix, scattered (not on a grid) in two layers, the top one
            // resting with its tops at the level you dive into; near ones are lit 3D stones, far ones specks of their colour
            // (once you are down at the grain's own surface it is no longer drawn as a stone: you are at its face)
            if (!_noGrains)
            {
                var focusG = FocusRec;
                var nearG = NearGrains(_fx, _fz, Math.Max(Grain * 12, Math.Min((overlay ? 30 : 110) * Grain, 2.2 * s) + Grain));   // always the grains round you, to the horizon
                // the biggest on screen first, so the grains right in front of you always get their real 3D bodies (never a flat
                // eight-sided stand-in, which is only for grains a few pixels across)
                Func<GrainRec, double> onScreen = gq => gq.Rad / Math.Max(1e-30, Math.Sqrt((gq.X - _fx) * (gq.X - _fx) + (gq.Z - _fz) * (gq.Z - _fz) + (0.6 * s - gq.Y) * (0.6 * s - gq.Y)));
                nearG.Sort((p1, p2) => onScreen(p2).CompareTo(onScreen(p1)));
                foreach (var gr in nearG)
                {
                    var m = gr.M; long gi = gr.Key, gj = 0;
                    bool under = gr == focusG && s < FocusRad * 1.5;                             // close in: its fine face is drawn round you (below); its body carries it on out to its curve
                    double rad = gr.Rad, gx = gr.X, gy = gr.Y, gz = gr.Z;
                    float sh = gr.Shade * (gr.Layer == 0 ? 1f : 0.7f);
                    double dx = gx - _fx, dz = gz - _fz, dist = Math.Sqrt(dx * dx + dz * dz + (0.6 * s - gy) * (0.6 * s - gy));
                    double px = rad / Math.Max(1e-30, dist) * _lastH / (2 * Math.Tan(0.45));    // its size on screen, in pixels
                    if (gr.Layer == 0) { int nb = _bodies.Count; AddBody(0, gi, gj, gx, gy, gz, rad, s); if (_bodies.Count > nb) _bodies[nb].Sym = m.Id; }
                    if (overlay || px < 1.5) continue;                                           // over the ground: the ground itself is their top
                    // the grain you are on keeps its whole 3D body while its shape shows (its fine face lies over it round you, in round
                    // rings); closer in than a twentieth of its size its face and atoms are all there is to see
                    if (s < FocusRad * 0.05) continue;                                       // closer than a twentieth of the grain: its face and atoms are all there is; the grains round it are below and behind it, and their coarse bodies, which stand off their true shape by far more than the view, poked through as floating slabs
                    // (no grain bodies of its own: the grains are the ground's, drawn by the ground; here only the one you are on, its face)
                }
            }
            if (overlay) return;                                                         // over the ground: the ground itself is its grains; nothing is laid over it (only the grains to click)
            if (!_noGrains && !_space && mesh != null && s < FocusRad * 1.5 && (s > AtomSpacing * 12 || !Structured(FocusMineral))) GrainPatch(mesh, s);   // (once its crystal's atoms are in, they are the face: no sheet among them)
            if (!_noGrains && !_space && s < FocusRad * 1.5 && s > AtomSpacing * 1.5)
            {
                // the grain's face can be picked anywhere, at the size you are looking at: a grid of spots round you (each its card:
                // the mineral, its make-up, its terraces close in, how high you are above it), sitting on the face's real height
                long fgi, fgj; double fgx, fgy, fgz, frad; FocusGrain(out fgi, out fgj, out fgx, out fgy, out fgz, out frad);
                double step = s * 0.8; long a0 = (long)Math.Round(_fx / step), b0 = (long)Math.Round(_fz / step);
                for (long a = a0 - 2; a <= a0 + 2; a++)
                    for (long b = b0 - 2; b <= b0 + 2; b++)
                    {
                        double sx = a * step, sz = b * step, sy;
                        if (!GrainTop(fgi, fgj, fgx, fgy, fgz, frad, sx, sz, Math.Max(step, CoarseCell(s, frad)), out sy)) continue;   // (on the same coarse shape the face is drawn from and the camera is held over: a picked spot is on the drawn face, never nanometres off it with the lattice whole views away)
                        AddBody(2, a, b, sx, sy, sz, step * 0.45, s);
                    }
            }
            if (!_noGrains && !_space && !Enclosed && s < FocusRad * 1.5) SurfaceThings(solid, glow, mesh, s);   // (inside the rock nothing lies on a face: no silt, no film, no life)
            AirAbove(glow, s);                                                               // the world's air over the ground, molecule by molecule
            if (!overlay) Photons(glow, s, time);                                            // the matter's own light, by its temperature (MatterView.Photons.cs)
            if (overlay) return;                                                        // over the ground: only its grains so far
            // the focus grain's surface: detail at every size, fixed in place (cells of powers of ten, two at a time, blended),
            // in the grain's colour; its finest cells take on the colours of its atoms
            var fm = FocusMineral;
            if (fm != null && _noGrains && s > AtomSpacing * 8)                          // (only where there are no grains: the sea; a grain's face is its own surface above)
            {
                double c1 = Math.Pow(10, Math.Floor(Math.Log10(s / 30))), frac = Math.Log10(s / 30) - Math.Floor(Math.Log10(s / 30));
                double atomish = Fade(s, AtomSpacing * 200, AtomSpacing * 20);
                for (int lv = 0; lv < 2; lv++)
                {
                    double c = lv == 0 ? c1 : c1 * 10, wgt = lv == 0 ? 1 - frac : frac;
                    if (wgt < 0.5) continue;                                               // solid tiles: only the main size of the two (they would fight)
                    long n = (long)Math.Ceiling(1.6 * s / c), ci0 = (long)Math.Round(_fx / c), cj0 = (long)Math.Round(_fz / c);
                    if (n > 70) continue;
                    for (long i = ci0 - n; i <= ci0 + n; i++)
                        for (long j = cj0 - n; j <= cj0 + n; j++)
                        {
                            double x = i * c, z = j * c;
                            if ((x - _fx) * (x - _fx) + (z - _fz) * (z - _fz) > 2.6 * s * s) continue;
                            long ci = (long)Math.Floor(x / c), cj = (long)Math.Floor(z / c);
                            float sh = (float)(0.9 + 0.2 * Hash(ci, cj, (long)Math.Round(Math.Log10(c) * 10), _seed));   // gentle, so it carries on from the stone
                            // below about 300 nm the face shows its atomic terraces: flat patches tens of nm wide, each a layer
                            // (0.3 nm) above or below its neighbours
                            double ty = 0;
                            if (c < 3e-10) { const double tw = 4e-11; long ti = (long)Math.Floor(x / tw), tj = (long)Math.Floor(z / tw); ty = -Math.Floor(Hash(ti, tj, 60, _seed) * 4) * 3e-13; sh *= (float)(1 + ty / 3e-13 * 0.08); }
                            float fsh = FocusShade, r = fm.R * fsh, g = fm.G * fsh, b = fm.B * fsh;             // the stone's own colour, carried on
                            if (atomish > 0) { var ae = El(fm.Atoms[(int)(Hash(ci, cj, 5, _seed) * fm.Atoms.Length) % fm.Atoms.Length]); if (ae != null) { r = (float)(r * (1 - atomish) + ae.R * atomish); g = (float)(g * (1 - atomish) + ae.G * atomish); b = (float)(b * (1 - atomish) + ae.B * atomish); } }
                            // never faded out: the tiles are the crystal itself, and once its atoms are drawn one by one (from forty spacings)
                            // they are its body below the drawn layers, the floor the atoms' clouds lie over (a few spacings down, which from
                            // above barely moves it)
                            bool under = Structured(fm) && s < AtomSpacing * 40;
                            { Add(solid, x, ty - c * 0.05 - (under ? AtomSpacing * 9.5 : 0), z, c * 0.72, r * sh, g * sh, b * sh, s); if (solid.Count > 0) { var tv = solid[solid.Count - 1]; tv.A = 0.5f; solid[solid.Count - 1] = tv; } }   // a flat tile of the face
                        }
                }
            }
            // the mineral's atoms, rows about 0.25 nm apart, in its real ratio; one atom right at the point you dive into
            double fax, fay, faz; var focus = Focus(out fax, out fay, out faz);
            if (fm != null && s < AtomSpacing * 40 && Structured(fm))
            {
                // the lattice level's probe, once a second in the log: the scale in spacings, how many atoms the crystal gives, how many
                // glows and pick bodies come of them (the level had been black and its atoms unpickable between about six and twenty
                // spacings, and nothing read in the code said why)
                int probeTick = (int)time; bool probe = Log != null && probeTick != _latticeLogTick; if (probe) _latticeLogTick = probeTick;
                int glow0 = glow.Count, bodies0 = _bodies.Count; var probeAtoms = probe ? CrystalKept(fm, 2.4 * s, s < AtomSpacing * 14 ? AtomSpacing * 9 : AtomSpacing * 3, 25000) : null;
                if (probe) { int inView = 0; double px0 = 2.0 * s / Math.Max(1, _viewH); foreach (var pa in probeAtoms) if (AtomRadius(pa.E) >= px0 * 0.5) inView++; Log("LATTICE probe: " + fm.Id + " at " + (s / AtomSpacing).ToString("0.0") + " spacings, crystal " + probeAtoms.Count + " atoms (" + inView + " wide enough), focus " + (focus != null ? focus.Symbol : "none") + ", reach " + ScaleHud.Words(2.4 * s) + ", kept reach " + ScaleHud.Words(_keptReach) + ", cell area " + (fm.Lat != null ? fm.Lat.Area.ToString("0.0E+0") : "?") + ", yoff " + ScaleHud.Words(_yOff) + ", fy " + ScaleHud.Words(_fy)); }
                // from about 40 atom spacings (some 10 nm) the crystal's real lattice fills the whole view, coming in as a fine
                // grain of atoms over the surface and opening into its rows, layers and bonds as you come in
                // no fading in: an atom is there at its real size, and is drawn once it spans about a pixel (the face's own dots,
                // worked out per pixel on the card, stand for the atoms too small to draw); a bond once it spans a few
                double px = 2.0 * s / Math.Max(1, _viewH); float f = 1;
                var drawnAt = new List<CAtom>(); double bondReach = Math.Min(1.2 * s, AtomSpacing * 22);
                double depth = s < AtomSpacing * 14 ? AtomSpacing * 9 : AtomSpacing * 3;     // close in, deep: solid every way you look
                // one picture at a level, never two at once, and neither the picture from before the quantum theory: the lattice's
                // level (down to about a nanometre, four spacings) is every atom as its electron shells at its real size, its nucleus a
                // spark at the middle, the bonds the shared electrons between neighbours; the atom's level below it is each atom's
                // nucleus and its electrons on their paths and in their orbitals
                // the levels hand over by degrees, never in one frame: the lattice's clouds (every atom its soft cloud) fade out and
                // the atoms' own systems fade in between seven spacings and four; the atom you are at hands over to its inside view
                // between eight widths and four, its cloud fading as its electrons' shells and orbitals come up (it had been one
                // switch at four spacings and another at four radii: black, then orbs in a frame, then the orbitals in a frame)
                foreach (var at0 in CrystalKept(fm, 2.4 * s, depth, 25000))
                {
                    var at = at0; double dSince; Element dPar; { var dE = Daughter(at0.E, AtomKey(at0.X, at0.Y, at0.Z), out dSince, out dPar); if (dE != at0.E) at = new CAtom { E = dE, X = at0.X, Y = at0.Y, Z = at0.Z }; }   // (as it is now: decayed, if its moment has come)
                    double ar = AtomRadius(at.E), ay = at.Y - ar - _yOff;                     // on the grain's real surface
                    if (dSince >= 0) DecayFlash(glow, solid, dPar, at.X, ay, at.Z, dSince, s);   // (its particle still flying out)
                    float sh = (float)(1 + 0.6 * at.Y / AtomSpacing * 0.25); sh = Math.Max(0.45f, sh);
                    // from afar a ball in its colour, growing up out of the face; close in the ball gives way to the atom's own system
                    // (its nucleus and its electrons on their paths), as the electrons are what an atom's size really is
                    if (ar < px * 0.5) continue;                                                 // (too small to show yet: the face's dots stand for it)
                    long akey = (long)(at.X / 1e-14) ^ (long)(at.Z / 1e-14) * 31;   // each atom at its own size, its colour coming in
                    if ((at.X - _fx) * (at.X - _fx) + (at.Z - _fz) * (at.Z - _fz) < bondReach * bondReach) drawnAt.Add(new CAtom { E = at.E, X = at.X, Y = ay, Z = at.Z });
                    // between the face and the atom's own system (from about ten nanometres in to about an atom's width), each atom as
                    // its electron cloud: a soft glow of its element's colour at its real size (the cloud of where its electrons may be,
                    // densest at the middle), its nucleus a spark at the middle
                    Density(glow, fm, at.E, at.X, ay, at.Z, s, f * sh);                             // (one model at every size: its electron density, overlapping its neighbours'; DESIGN 740)
                    { int nb = _bodies.Count; AddBody(1, 0, 0, at.X, ay, at.Z, ar, s); if (_bodies.Count > nb) _bodies[nb].Sym = at.E.Symbol; }   // every layer's atoms
                }
                if (AtomSpacing > px * 3) Bonds(glow, drawnAt, s, time, 0.7f);   // (the shared electrons between neighbours, the same at every size)
                if (probe) Log("LATTICE probe: drawn " + drawnAt.Count + " near, glows " + (glow.Count - glow0) + ", bodies " + (_bodies.Count - bodies0)+ "");   // (bonds once they span a few pixels: the shared pairs, dimmer among the electrons at the atom's level)
            }
            else if (fm != null && s < AtomSpacing * 40)
            {
                double a = SiteA(fm); long n = (long)Math.Min(27, Math.Ceiling(2.4 * s / a) + 1), ai0 = FocusAI, aj0 = FocusAJ;   // (no more sites than a crystal's atoms: sixty a side was ninety thousand sites at six nanometres, three atoms each, and the card stopped answering)
                double px = 2.0 * s / Math.Max(1, _viewH); float f = 1;                               // (no fading in: drawn once big enough to see)
                for (long i = ai0 - n; i <= ai0 + n; i++)
                    for (long j = aj0 - n; j <= aj0 + n; j++)
                        for (long k = 0; k >= -7; k--)
                        {
                            var el = SiteAtom(fm, i, j, k);
                            if (el == null) continue;
                            double ar = AtomRadius(el);
                            float sh = (float)(1 - 0.25 * -k);
                            double sx, sy, sz; SitePos(fm, i, j, k, out sx, out sy, out sz);                // its place (a liquid's: loose and moving)
                            var mol = fm.Molecule;
                            if (mol != null && el.Symbol == mol[0].Key)
                            {
                                // a whole molecule on this site, in its real shape, each turned its own way (a dissolved ion in a molecule's place stands alone):
                                // each of its atoms drawn as a crystal's atoms are, its cloud at the lattice's level and its own system only
                                // close in and near where you look (every atom of every molecule had been its full system, every electron and
                                // its path, at every size below forty spacings: millions of points in a frame at six nanometres, and the card
                                // stopped answering: the driver reset, and the program went down with it)
                                for (int q = 0; q < mol.Length; q++)
                                {
                                    var me = q == 0 ? el : El(mol[q].Key); if (me == null) continue; double ox = 0, oy = 0, oz = 0; if (q > 0) Orient(i, j, k, mol[q].Value, out ox, out oy, out oz);
                                    double mx = sx + ox, my = sy - ar + oy - _yOff, mz = sz + oz, mar = AtomRadius(me); long mk = i * 7919 ^ j * 104729 ^ k * 31 + q;
                                    if (mar < px * 0.5) continue;
                                    Density(glow, fm, me, mx, my, mz, s, sh * f);                   // (one model at every size: DESIGN 740)
                                }
                                { int nb = _bodies.Count; AddBody(6, i, j, sx, sy - ar - _yOff, sz, ar * 1.6, s); if (_bodies.Count > nb) _bodies[nb].Sym = fm.Id; }   // the whole molecule
                            }
                            else
                            {
                                if (ar < px * 0.5) continue;
                                Density(glow, fm, el, sx, sy - ar - _yOff, sz, s, sh * f);                   // (one model at every size: DESIGN 740)
                            }
                            { int nb = _bodies.Count; AddBody(1, i, j, sx, sy - ar - _yOff, sz, ar, s); if (_bodies.Count > nb) _bodies[nb].Sym = el.Symbol; }   // every layer's atoms
                        }
            }
            // the atom under you, from inside: its electrons in their shells, moving round it, and its nucleus at the middle
            if (focus != null && s < Math.Max(AtomRadius(focus), AtomSpacing) * 8)
            {
                double ar = AtomRadius(focus), cy = fay, nr = NucleusRadius(focus), ax0 = fax, az0 = faz;
                // (no hand-over: the atom you are at is its density like every atom, and from here its own electrons and orbitals are drawn on it)
                // its electrons, the same as every atom's system: each a clear dot on its own path in its shells, the valence shell
                // in gold (the nucleus is drawn below, for real)
                Cloud(glow, focus, ax0, cy, az0, s, time, 777, 1f);   // fully drawn from as far as every atom's
                if (s < ar * 0.5)
                {
                    // inside its electron cloud it is not empty: the innermost electrons (1s) are densest at the nucleus itself,
                    // |psi|^2 falling as exp(-2Zr/a0), so all the way down to the nucleus you are inside their cloud: a shimmer of
                    // where they may be found, as bright as that density, sampled afresh a few times a second
                    double a1s = 5.29e-14 / Math.Max(1, focus.Z); long tick = 0;                                         // (steady: never a flicker)
                    for (int k = 0; k < 400; k++)
                    {
                        double u = Hash(k, tick, 401, _seed), v = Hash(k, tick, 402, _seed), wq = Hash(k, tick, 403, _seed);
                        double rr = s * Math.Exp(Math.Log(0.15) + wq * Math.Log(4 / 0.15)), th = Math.Acos(1 - 2 * u), ph = v * 2 * Math.PI;
                        double dens = Math.Exp(-2 * rr / a1s); if (dens < 0.02) continue;
                        float b = (float)(0.7 * dens);   // (bright enough to read: at a picometre the shimmer had been too dim to see)
                        Add(glow, ax0 + rr * Math.Sin(th) * Math.Cos(ph), cy + rr * Math.Cos(th), az0 + rr * Math.Sin(th) * Math.Sin(ph), s * 0.004, b * 0.6f, b * 0.75f, b, s);
                    }
                }
                // the nucleus: its real protons and neutrons, each its three quarks in its gluon glow, packed by the forces, growing as you come in; while
                // it is too small to see, a small spark marks where it is
                if (nr / s < 0.004) Add(glow, ax0, cy, az0, s * 0.0014, 1f, 0.95f, 0.9f, s);   // a sharp point of light until its protons and neutrons can be seen
                var nuc = NucleonsOf(focus); int A = nuc.Length;                                   // every one of its protons and neutrons, where the forces put them
                if (_focusSince >= 0) DecayFlash(glow, solid, _focusParent, ax0, cy, az0, _focusSince, s);   // (just decayed: its alpha, electron or other half still flying out)
                for (int k = 0; k < A; k++)
                {
                    bool proton = nuc[k][3] > 0;
                    double nx = ax0 + nuc[k][0] * 1e-18, ny = cy + nuc[k][1] * 1e-18, nz = az0 + nuc[k][2] * 1e-18;
                    // every proton and neutron is its three quarks in the glow of the gluons that hold them, at every size: from afar
                    // each a small clump of three colour charges in its gluon glow (a proton's warm, a neutron's cool, so they tell
                    // apart), close in the three quarks, the flux tubes of gluon field between them and the sea's virtual pairs. The
                    // quarks are points as far as measured, moving in about 0.84 fm (slowed to be watched: a quark crosses its nucleon
                    // in about half a minute). One drawing at every size, never a solid ball handed over to quarks part way in
                    bool close = s < Nucleon * 8; double nearF = Fade(s, Nucleon * 30, Nucleon * 3); float gf = (float)Math.Max(0.5, nearF);
                    double tight = 0.4;   // the three sit tight at the nucleon's middle in one clump at every size, never pulled apart into three to count (the gluon glow and the sea fill the nucleon round them)
                    Add(glow, nx, ny, nz, Nucleon, (proton ? 0.32f : 0.14f) * gf, (proton ? 0.12f : 0.16f) * gf, (proton ? 0.08f : 0.26f) * gf, s);   // the gluon field's glow, the nucleon's size, tinted by its kind
                    string q = proton ? "uud" : "udd"; float[][] colour = { new[] { 1f, 0.2f, 0.2f }, new[] { 0.2f, 1f, 0.2f }, new[] { 0.3f, 0.4f, 1f } };   // their three colour charges
                    double[][] qp = new double[3][];
                    for (int qi = 0; qi < 3; qi++)
                    {
                        double a1 = time * (0.07 + qi * 0.02) + qi * 2.1 + k * 0.77, a2 = time * (0.05 + qi * 0.016) + qi + k * 1.3, qr = Nucleon * (0.35 + 0.2 * Math.Sin(time * 0.05 + qi + k)) * tight;
                        qp[qi] = new[] { nx + qr * Math.Sin(a2) * Math.Cos(a1), ny + qr * Math.Cos(a2), nz + qr * Math.Sin(a2) * Math.Sin(a1) };
                    }
                    if (close)
                    {
                        // the strong field between them: flux tubes of gluon field joining the three quarks (it does not spread out
                        // like the electric field, it pulls into strings), and quark-antiquark pairs of the sea popping in and out
                        double ycx = (qp[0][0] + qp[1][0] + qp[2][0]) / 3, ycy = (qp[0][1] + qp[1][1] + qp[2][1]) / 3, ycz = (qp[0][2] + qp[1][2] + qp[2][2]) / 3;
                        for (int qi = 0; qi < 3; qi++)
                            for (int t2 = 1; t2 < 10; t2++)
                            {
                                double tt = t2 / 10.0; float fl = gf * (float)(0.35 + 0.25 * Math.Sin(time * 1.5 + t2 + qi * 2)) * 0.5f;   // (the tubes breathe slowly: never a flicker)
                                Add(glow, qp[qi][0] + (ycx - qp[qi][0]) * tt, qp[qi][1] + (ycy - qp[qi][1]) * tt, qp[qi][2] + (ycz - qp[qi][2]) * tt, Nucleon * 0.12, colour[qi][0] * fl, colour[qi][1] * fl, colour[qi][2] * fl, s);
                            }
                        long qt = (long)Math.Floor(time * 0.7);                                       // (a pair lasts over a second: watchable, never a strobe)
                        for (int sp = 0; sp < 6; sp++)
                        {
                            if (Hash(k, sp, qt, _seed) > 0.5) continue;                       // a virtual pair, there for a moment
                            double ox = (Hash(k, sp, qt + 1, _seed) - 0.5) * Nucleon * 1.4, oy = (Hash(k, sp, qt + 2, _seed) - 0.5) * Nucleon * 1.4, oz = (Hash(k, sp, qt + 3, _seed) - 0.5) * Nucleon * 1.4, sep = Nucleon * 0.12;
                            Add(glow, nx + ox + sep, ny + oy, nz + oz, s * 0.002, 0.7f * gf, 0.7f * gf, 0.9f * gf, s); Add(glow, nx + ox - sep, ny + oy, nz + oz, s * 0.002, 0.9f * gf, 0.6f * gf, 0.6f * gf, s);
                        }
                    }
                    for (int qi = 0; qi < 3; qi++)
                    {
                        double qx = qp[qi][0], qy = qp[qi][1], qz = qp[qi][2];
                        // the quark you picked moves on: the view keeps going to where it is now (as with an electron), and
                        // once it is all but there it rides with it exactly, so the zoom in goes on into it without end
                        if (_picked != null && _picked.Kind == 4 && _picked.Key == k + 1 && _picked.I == qi)
                        {
                            _tfx = qx; _tfy = qy + _yOff; _tfz = qz; _picked.X = qx; _picked.Y = qy + _yOff; _picked.Z = qz;
                            double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < Math.Pow(s * 3, 2)) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                        }
                        // a point as far as anyone has measured: its glow fills the view and stays, however far in you go
                        Add(glow, qx, qy, qz, Math.Min(s * 4, Math.Max(s * 0.003, Nucleon * (0.05 + 0.25 * (1 - nearF)))), colour[qi][0] * gf, colour[qi][1] * gf, colour[qi][2] * gf, s);   // (from afar each a soft glow a quarter of the nucleon wide, the three merging into one clump; never wider than a few screens: a glow hundreds of screens wide was dropped on some frames and kept on others, a strobe)
                        if (close) { int nb = _bodies.Count; AddBody(4, qi, q[qi] == 'u' ? 1 : 0, qx, qy, qz, Nucleon * 0.3, s); if (_bodies.Count > nb) { _bodies[nb].Sym = q[qi] == 'u' ? "up" : "down"; _bodies[nb].Key = k + 1; } }
                    }
                    if (s > Nucleon * 0.6) { int nb = _bodies.Count; AddBody(5, k, proton ? 1 : 0, nx, ny, nz, Nucleon, s); if (_bodies.Count > nb) _bodies[nb].Sym = proton ? "proton" : "neutron"; }   // the nucleon itself, picked as one, down to where its quarks are picked instead
                }
            }
        }

        // an atom's electrons, for the atoms around the one you are in: a faint glow for each shell and its electrons moving
        private void Cloud(List<StarVertex> glow, Element e, double x, double y, double z, double s, double time, long key, float f)
        {
            if (f <= 0.01) return;
            if (_plasma)
            {
                // too hot to keep its electrons: the bare nucleus, and free electrons racing past on their own
                double ar0 = AtomRadius(e), dt0 = s * 0.0035;
                for (int k = 0; k < 4; k++)
                {
                    double ph = _clock * (3 + k) + k * 1.7, rr0 = ar0 * (1.5 + 3 * Hash(k, 1, 7, _seed));
                    Add(glow, x + Math.Cos(ph) * rr0, y + Math.Sin(ph * 0.7) * rr0 * 0.6, z + Math.Sin(ph) * rr0, dt0, 1f * f, 0.8f * f, 0.25f * f, s);
                }
                Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.0014), 1f * f, 0.95f * f, 0.9f * f, s);
                { int nb = _bodies.Count; AddBody(7, 0, 0, x, y, z, ar0 * 0.08, s); if (_bodies.Count > nb) _bodies[nb].Sym = e.Symbol; }
                return;
            }
            key = (long)Math.Round(x / 1e-16) * 73856093L ^ (long)Math.Round(y / 1e-16) * 19349663L ^ (long)Math.Round(z / 1e-16) * 83492791L;   // from where the atom is: the same atom, the same electrons, however it is drawn
            double ar = AtomRadius(e); int shells = e.Shells.Length;
            double dot = s * 0.006;                                                     // electrons: points, shown as a clear dot a few pixels wide
            {
                // every one of its electrons, in its own subshell and orbital (this element's own make-up), each on its path in its
                // orbital's shape, at its subshell's distance, at its own speed; the valence (outermost) shell in gold
                var els = ElectronsOf(e);
                for (int k = 0; k < els.Length; k++)
                {
                    var el = els[k]; bool valence = el.N == shells;
                    // its path, seen (only one of each orbital's pair: they share it), turning on slowly with the electron, so the
                    // orbital's shape is read and is never the same twice
                    if (el.Spin == 0)
                        for (int q = 0; q < 48; q++)
                        {
                            double px, py, pz; ElectronAt(el, key, Math.Floor(ElectronPhase(el, k, time) / (2 * Math.PI)) * 2 * Math.PI + q * Math.PI * 2 / 48, out px, out py, out pz, ElectronDrift(time));   // (the loop of this time round)
                            Add(glow, x + px * ar, y + py * ar, z + pz * ar, dot * 0.4, (valence ? 0.4f : 0.2f) * f, (valence ? 0.3f : 0.3f) * f, (valence ? 0.1f : 0.5f) * f, s);
                        }
                    double ox, oy, oz; ElectronAt(el, key, ElectronPhase(el, k, time), out ox, out oy, out oz, ElectronDrift(time));
                    double exx = x + ox * ar, eyy = y + oy * ar, ezz = z + oz * ar;
                    // the electron you picked moves on its path: the camera keeps going to where it is now, and once all but
                    // there rides with it exactly (a point, however far in you go)
                    if (_picked != null && _picked.Kind == 3 && _picked.Key == key && _picked.J == k)
                    {
                        _tfx = exx; _tfy = eyy + _yOff; _tfz = ezz; _picked.X = exx; _picked.Y = eyy + _yOff; _picked.Z = ezz;
                        double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < Math.Pow(s * 3, 2)) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                    }
                    bool riding = _picked != null && _picked.Kind == 3 && _picked.Key == key && _picked.J == k;
                    Add(glow, exx, eyy, ezz, riding ? Math.Max(dot, s * 0.07) : dot, (valence ? 1f : 0.45f) * f, (valence ? 0.8f : 0.75f) * f, (valence ? 0.25f : 1f) * f, s);   // (the one you ride beside: a glowing body a share of the view, as a comet is when you fly with it; a point has no size, this is the size we give it)
                    { int nb = _bodies.Count; AddBody(3, el.N, k, exx, eyy, ezz, ar * 0.05, s); if (_bodies.Count > nb) { _bodies[nb].Sym = "electron:" + e.Symbol + ":" + shells + ":" + el.N + "spdf"[Math.Min(3, el.L)]; _bodies[nb].Key = key; _bodies[nb].AX = x; _bodies[nb].AY = y; _bodies[nb].AZ = z; } }   // every atom's electrons can be picked
                }
            }
            { double odx = x - _fx, ody = y + _yOff - _fy, odz = z - _fz; if (odx * odx + ody * ody + odz * odz < Math.Pow(AtomSpacing * 0.6, 2)) Orbitals(glow, e, x, y, z, s, time, key, f); }   // and where its electrons may be, as steady clouds: only for the atom you are at
            Add(glow, x, y, z, Math.Max(NucleusRadius(e), s * 0.0014), 1f * f, 0.95f * f, 0.9f * f, s);   // its nucleus: a sharp point of light at the middle
            { int nb = _bodies.Count; AddBody(7, 0, 0, x, y, z, ar * 0.08, s); if (_bodies.Count > nb) _bodies[nb].Sym = e.Symbol; }   // and its nucleus can be picked
        }

    }
}
