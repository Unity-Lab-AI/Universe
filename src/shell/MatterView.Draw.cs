// Universe Â· shell Â· the matter view, its part: draw (one class in several files: src/shell/MatterView.cs is its start)
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
        private void Put(List<StarVertex> l, ref StarVertex[] arr, ref UploadBuffer buf)
        {
            if (arr.Length < l.Count) arr = new StarVertex[l.Count + 2048];
            l.CopyTo(arr);
            long need = (long)Math.Max(1, l.Count) * Marshal.SizeOf(typeof(StarVertex));
            if (buf == null || buf.Bytes < need) { if (buf != null) buf.Dispose(); buf = new UploadBuffer(_gpu.Device, (long)(l.Count + 2048) * Marshal.SizeOf(typeof(StarVertex)) * 2); }
            if (l.Count > 0) buf.Write(arr, l.Count);
        }

        /// <summary>Draws the matter at <paramref name="scaleKm"/> (how far above the point you dive into), in view units.</summary>
        public void Draw(Mat4 view, double scaleKm, int w, int h, double time) { Draw(view, scaleKm, w, h, time, null, false); }

        /// <summary>Draws the matter; with <paramref name="overlay"/>, only its grains, lying over the ground still drawn below.</summary>
        public void Draw(Mat4 view, double scaleKm, int w, int h, double time, MeshPipes pipes, bool overlay)
        {
            _viewH = h;
            Universe.Render.GpuLog.Pass = overlay ? "the grains over the ground" : "the matter (a grain's face, crystals, atoms)";
            _lastH = h;
            // the point you dive into eases across to what you picked
            double dt = _lastTime < 0 ? 0 : Math.Max(0, Math.Min(0.1, time - _lastTime)); _lastTime = time;
            double k = 1 - Math.Exp(-dt * Glide);
            // an electron moves on its path faster than the view could glide after it: once you are almost at it the view rides with
            // it, so zooming in goes to it, never past
            {
                double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz, gd = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                if (_picked != null && (_picked.Kind == 3 || _picked.Kind == 4) && gd < scaleKm * 3) k = 1;
                if (gd < scaleKm * 0.01) k = 1;                                                 // a hundredth of the picture away: there, exactly
                if (k >= 1) { _fx = _tfx; _fz = _tfz; _fy = _tfy; }                             // (exactly: a rounding's width is a whole screen, far enough in)
                else
                {
                    _fx += gx * k; _fz += gz * k; _fy += gy * k;
                    // the zoom never outruns the glide: whatever you picked (an atom, its nucleus, a nucleon, a quark) stays
                    // within the picture however fast the wheel goes, so coming down always comes down onto it, never past
                    // it into black with its name on the card
                    double left = gd * (1 - k), keep = scaleKm * 0.3;
                    if (left > keep) { double pull = (left - keep) / gd; _fx += gx * pull; _fz += gz * pull; _fy += gy * pull; }
                }
            }
            _solidL.Clear(); _glowL.Clear(); _meshL.Clear(); var solid = _solidL; var glow = _glowL; var mesh = pipes != null ? _meshL : null;   // (the frame's lists kept and emptied, never made anew each frame)
            _bodies.Clear();
            _clock = time; Build(scaleKm, time, solid, glow, mesh, overlay); Count();
            if (_picked != null && _picked.Kind == 3 && !overlay)
            {
                // the electron you ride: where it is now, worked out from the pick itself (its atom, its shell and orbital, its phase), the
                // dive point taken to it, and it drawn there as a glowing body a share of the view, whatever its atom's own drawing did
                var ps = (_picked.Sym ?? "").Split(':'); var pe = ps.Length > 1 ? El(ps[1]) : null;
                if (pe != null)
                {
                    var els = ElectronsOf(pe); int kq = (int)_picked.J;
                    if (kq >= 0 && kq < els.Length)
                    {
                        double ox, oy, oz; ElectronAt(els[kq], _picked.Key, ElectronPhase(els[kq], kq, time), out ox, out oy, out oz, ElectronDrift(time)); double ar = AtomRadius(pe);
                        double exx = _picked.AX + ox * ar, eyy = _picked.AY + oy * ar, ezz = _picked.AZ + oz * ar;
                        _picked.X = exx; _picked.Y = eyy + _yOff; _picked.Z = ezz; _tfx = exx; _tfy = eyy + _yOff; _tfz = ezz;
                        double gx = _tfx - _fx, gy = _tfy - _fy, gz = _tfz - _fz; if (gx * gx + gy * gy + gz * gz < scaleKm * scaleKm * 9) { _fx = _tfx; _fy = _tfy; _fz = _tfz; }
                        Add(glow, exx, eyy, ezz, scaleKm * 0.07, 1f, 0.8f, 0.25f, scaleKm);
                    }
                }
            }
            if (mesh != null) { Solidify(solid, mesh, h); solid.Clear(); }
            Put(solid, ref _sv, ref _solidBuf); Put(glow, ref _gv, ref _glowBuf);
            if (mesh != null) Put(mesh, ref _mv, ref _meshBuf);
            Mat4 vp = view * Mat4.Perspective(0.9f, w / (float)Math.Max(1, h), 0.001f, 1e9f);   // out to the grains round you, however close in you are
            _lastVp = vp; _lastW = w; _lastH = h; _haveVp = true; _scale = scaleKm; _lastView = view;
            var right = new Vec3(view.M11, view.M21, view.M31); var up = new Vec3(view.M12, view.M22, view.M32);
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = vp.M11; c[1] = vp.M12; c[2] = vp.M13; c[3] = vp.M14; c[4] = vp.M21; c[5] = vp.M22; c[6] = vp.M23; c[7] = vp.M24;
            c[8] = vp.M31; c[9] = vp.M32; c[10] = vp.M33; c[11] = vp.M34; c[12] = vp.M41; c[13] = vp.M42; c[14] = vp.M43; c[15] = vp.M44;
            c[16] = right.X; c[17] = right.Y; c[18] = right.Z; c[20] = up.X; c[21] = up.Y; c[22] = up.Z;
            c[24] = (float)time; c[25] = 1; c[28] = w; c[29] = h; c[30] = 0.0012f;
            // (solid things are drawn as lit shapes below, never as glowing dots, which add up to white)
            if (mesh != null && mesh.Count > 0)
            {
                // the near grains as lit stones (flat facets), in the starlight of where you are
                var mc = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) mc[q] = c[q];
                mc[16] = Light[0]; mc[17] = Light[1]; mc[18] = Light[2]; mc[20] = 0; mc[21] = 0; mc[22] = 0; mc[25] = 0; mc[26] = 0;
                pipes.Solid.Draw(_meshBuf, mesh.Count, mc, 0, 0, w, h);
            }
            if (pipes != null && _stoneMesh.Count > 0)
            {
                // the near stones, each drawn with where each pixel is on it, so the card works out its detail and stains
                Put(_stoneMesh, ref _stoneArr, ref _stoneBuf);
                var sc0 = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) sc0[q] = c[q];
                sc0[16] = Light[0]; sc0[17] = Light[1]; sc0[18] = Light[2];
                foreach (var st in _stones)
                {
                    double rad = st[6], sr2 = _scale / Math.Max(1e-30, rad);
                    var k2 = _k2; Array.Copy(sc0, k2, k2.Length);
                    double sx0 = (_fx - st[3]) / rad - _ex * sr2, sy0 = (_fy - _yOff - st[4]) / rad - _ey * sr2, sz0 = (_fz - st[5]) / rad - _ez * sr2;
                    k2[40] = (float)sx0; k2[41] = (float)sy0; k2[42] = (float)sz0; k2[43] = (float)sr2; k2[19] = (float)(sx0 - k2[40]); k2[23] = (float)(sy0 - k2[41]); k2[31] = (float)(sz0 - k2[42]);
                    k2[44] = BitConverter.ToSingle(BitConverter.GetBytes((uint)((long)st[2] & 0xFFFFFFFF)), 0); k2[45] = (float)(rad / Math.Max(1e-30, _scale)); k2[46] = 0; k2[47] = 1; k2[36] = Greyness;   // (no atomic terraces or atoms on a stone: that slot is the layer's height, 0 none)
                    pipes.Solid.Draw(_stoneBuf, (int)st[0], (int)st[1], k2, 0, 0, w, h);
                }
            }
            if (pipes != null && _patchMesh.Count > 0)
            {
                // the grain's face: its shape from the processor's coarse points, its detail and stains worked out per pixel on the
                // card from where each pixel is on the grain (its own frame, radius 1)
                var pc = new float[BillboardPipeline.ConstantCount];
                for (int q = 0; q < 16; q++) pc[q] = c[q];
                pc[16] = Light[0]; pc[17] = Light[1]; pc[18] = Light[2];
                double sr = _scale / Math.Max(1e-30, _patchRad);
                double ox0 = (_fx - _patchGX) / _patchRad - _ex * sr, oy0 = (_fy - _yOff - _patchGY) / _patchRad - _ey * sr, oz0 = (_fz - _patchGZ) / _patchRad - _ez * sr;
                pc[40] = (float)ox0; pc[41] = (float)oy0; pc[42] = (float)oz0; pc[43] = (float)sr;
                pc[19] = (float)(ox0 - pc[40]); pc[23] = (float)(oy0 - pc[41]); pc[31] = (float)(oz0 - pc[42]);   // the low halves
                pc[44] = BitConverter.ToSingle(BitConverter.GetBytes((uint)(_patchKey & 0xFFFFFFFF)), 0); pc[45] = (float)(_patchRad / Math.Max(1e-30, _scale)); pc[46] = (float)(3e-13 / Math.Max(1e-30, _patchRad)); pc[47] = 1; pc[36] = Greyness;   // (one atomic layer, 0.3 nm, in the grain's own units: its terraces)
                Put(_patchMesh, ref _patchArr, ref _patchBuf);
                Universe.Render.GpuLog.Pass = "a grain's face, its detail worked out per pixel";
                pipes.Solid.Draw(_patchBuf, _patchMesh.Count, pc, 0, 0, w, h);
            }
            // the glows after every opaque face (the grain's face, its sheet, the stones): they test the depth the faces wrote and
            // never write it, so a glow behind a face is hidden and one in front shows (drawn first, the faces had landed on top of
            // them wherever they covered the screen, and from above that is everywhere: the lattice's level was black)
            c[26] = 0; if (glow.Count > 0) _dots.Draw(_glowBuf, glow.Count, c, 0, 0, w, h);      // electrons, their shells and the nucleus's spark: glows
            if (_orbRecs.Count > 0)
            {
                // the orbitals' clouds: each record drawn as its points, placed by the card
                Put(_orbRecs, ref _orbArr, ref _orbBuf);
                // with many atoms on screen, fewer points to each orbital (the shapes stay): never more than the card can paint in a frame,
                // counted in pixels, not points: each point a glow at least 0.28% of the view across, about 130 pixels on a 4K screen, and
                // 4 million of them (over 500 million pixels a frame) had hung the card the moment the electrons appeared (Windows reset
                // it: 0x887A0005)
                double dotPx = 2 * 0.0028 * h, pxEach = Math.Max(16, dotPx * dotPx), budget = 6e7 * Math.Max(0.2, Math.Min(2, Universe.Platform.Window.Detail));
                int most = (int)Math.Max(20000, Math.Min(4000000, budget / pxEach));
                int per = Math.Max(6, Math.Min(OrbitalPoints, most / Math.Max(1, _orbRecs.Count)));
                var oc = _k2; Array.Copy(c, oc, oc.Length); oc[41] = 1; oc[42] = per; oc[30] = 0.0028f;
                _dots.Draw(_orbBuf, _orbRecs.Count * per, oc, 0, 0, w, h);
            }
        }

        private double _scale = TopKm; private Mat4 _lastView;
        private readonly List<StarVertex> _solidL = new List<StarVertex>(20000), _glowL = new List<StarVertex>(2000), _meshL = new List<StarVertex>(60000);
        private readonly float[] _k2 = new float[BillboardPipeline.ConstantCount];   // (one scratch block of constants: each draw copies its constants when it is recorded)
        private StarVertex[] _patchArr = new StarVertex[0]; private UploadBuffer _patchBuf;

    }
}
