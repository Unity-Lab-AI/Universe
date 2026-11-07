// DNA-AND · shell · sample set 8: the microscopic world, alive. A patch of water and sediment under the microscope.
// Light falls from above; it is cold on the left and warm on the right. Every microbe has genes and breeds with mutation;
// viruses infect the ones whose surface they fit. Wheel: zoom. Drag: move. Space: pause. 1-5: speed. Legend: what is going on.
using System;
using System.Runtime.InteropServices;
using DnaAnd.Core;
using DnaAnd.Gen;
using DnaAnd.Platform;
using DnaAnd.Render;
using DnaAnd.Sim;

namespace DnaAnd.Shell
{
    internal sealed class MicrobeView : IDisposable
    {
        private readonly MicrobeWorld _world;
        private readonly BillboardDrawer _dots;
        private readonly UploadBuffer _buf;
        private StarVertex[] _v = new StarVertex[(MicrobeWorld.MaxMicrobes * 6) + MicrobeWorld.MaxViruses * 2 + 4000];
        private double _camX = MicrobeWorld.Width / 2, _camY = MicrobeWorld.Height * 0.3, _view = 0.35; // _view: how much of the patch fills the screen
        private double _speed = 1; private bool _paused;
        private double _accum;

        public MicrobeView(Gpu gpu, BillboardPipeline pipe, ulong seed)
        {
            _world = new MicrobeWorld(seed);
            _dots = new BillboardDrawer(gpu.CommandList, pipe);
            _buf = new UploadBuffer(gpu.Device, (long)_v.Length * Marshal.SizeOf(typeof(StarVertex)));
        }

        public string Title
        {
            get
            {
                return "DNA-AND samples · Set 8: The microscopic world · " + _world.Microbes.Count + " microbes in " + _world.Families + " families, " + _world.Viruses.Count + " viruses · " +
                       "births " + _world.Births + ", deaths " + _world.Deaths + ", infections " + _world.Infections + " · thriving: " + _world.Leader +
                       " · " + (_paused ? "PAUSED" : "speed " + _speed + "x") + " · wheel or Q/E: zoom · drag, W A S D or arrows: move · Space: pause · 1-5: speed · Legend";
            }
        }

        public string Legend()
        {
            return "Set 8 of 17: the microscopic world, alive\n\n" +
                   "A patch of water and sediment, about 0.4 mm across, under the microscope. Each microbe you see stands for a crowd of its kind; the game never simulates every single one.\n\nLight falls from the top. It is cold on the left and warm on the right (a warm vent).\n\n" +
                   "Every microbe has genes: size, speed, how it feeds (light, food in the water, or other microbes), poison and defence, a tough coat, the temperature it likes, its shape (ball, rod or spiral), and the shape of its outer surface.\n" +
                   "When it has stored enough energy it splits in two. Copies are made with small mistakes, and now and then a big jump: weird new combinations. Whatever fits survives; the rest die out.\n" +
                   "A family keeps its colour. A big enough change starts a new family with a new colour.\n\n" +
                   "Viruses (small spiky specks) drift about. If one touches a microbe whose surface it fits, it gets in, takes the cell over, and bursts it open as new viruses; the new ones mutate too. Microbes that change their surface escape.\n" +
                   "A red rim on a microbe: infected. Dead microbes become food again.\n\n" +
                   "The window title shows the live counts and which family is thriving right now, and why.\n\n" +
                   "Controls:\n  Mouse wheel: zoom · drag: move\n  Space: pause · 1-5: speed\n  Exit menu or Esc: close";
        }

        public void Update(Input input, double dt)
        {
            if (input.WasPressed(Win32.VK_SPACE)) _paused = !_paused;
            for (int k = 0; k < 5; k++) if (input.WasPressed(0x31 + k)) { _speed = new[] { 1, 2, 4, 8, 16 }[k]; _paused = false; }
            if (input.Wheel != 0) _view = Math.Max(0.01, Math.Min(1.3, _view * Math.Pow(0.8, input.Wheel)));
            if (input.LeftDown) { _camX -= input.MouseDX * _view * 0.3; _camY -= input.MouseDY * _view * 0.3; }
            // keys: W A S D or the arrows move, Q and E (or + and -) zoom
            double pan = MicrobeWorld.Width * 0.5 * _view * dt;
            if (input.IsDown(0x57) || input.IsDown(Win32.VK_UP)) _camY -= pan;
            if (input.IsDown(0x53) || input.IsDown(Win32.VK_DOWN)) _camY += pan;
            if (input.IsDown(0x41) || input.IsDown(Win32.VK_LEFT)) _camX -= pan;
            if (input.IsDown(0x44) || input.IsDown(Win32.VK_RIGHT)) _camX += pan;
            if (input.IsDown(0x45) || input.IsDown(0xBB)) _view = Math.Max(0.01, _view * Math.Pow(0.3, dt));
            if (input.IsDown(0x51) || input.IsDown(0xBD)) _view = Math.Min(1.3, _view / Math.Pow(0.3, dt));
            _camX = Math.Max(0, Math.Min(MicrobeWorld.Width, _camX)); _camY = Math.Max(0, Math.Min(MicrobeWorld.Height, _camY));
            if (_paused) return;
            _accum += dt * _speed;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_accum > 0.05 && sw.Elapsed.TotalMilliseconds < 8) { _world.Step(0.05); _accum -= 0.05; }
            if (_accum > 1) _accum = 0;
        }

        public void Draw(Gpu gpu, int width, int height, double time)
        {
            int n = 0;
            // the water: lighter at the top where the light comes in, and the sediment along the bottom
            for (int i = 0; i < 3000; i++)
            {
                double x = (i * 37 % 400) / 400.0 * MicrobeWorld.Width, y = MicrobeWorld.Height - (i * 7919 % 97) / 97.0 * 12;
                _v[n++] = SpaceGen.Star(new Vec3((float)x, (float)y, 0.5f), 2.2f, 0.32f, 0.27f, 0.2f, 0.25f);
            }
            foreach (var m in _world.Microbes)
            {
                if (n > _v.Length - 8) break;
                var g = m.G; float s = g.Size * 2.2f;   // drawn a little larger than life so each one reads clearly
                float warmTint = (float)MicrobeWorld.Temp(m.X);
                if (g.Shape == 0) _v[n++] = SpaceGen.Star(new Vec3((float)m.X, (float)m.Y, 0), s, g.R, g.G, g.B, 0.9f);
                else
                {
                    // rods and spirals: a short chain of segments along the way it swims
                    double ang = Math.Atan2(m.Vy, m.Vx); int segs = g.Shape == 1 ? 3 : 5;
                    for (int k = 0; k < segs; k++)
                    {
                        double along = (k - (segs - 1) / 2.0) * s * 0.9, wig = g.Shape == 2 ? Math.Sin(k * 1.8 + time * 4) * s * 0.5 : 0;
                        double px = m.X + Math.Cos(ang) * along - Math.Sin(ang) * wig, py = m.Y + Math.Sin(ang) * along + Math.Cos(ang) * wig;
                        _v[n++] = SpaceGen.Star(new Vec3((float)px, (float)py, 0), s * 0.62f, g.R, g.G, g.B, 0.9f);
                    }
                }
                if (g.Photo > 0.5) _v[n++] = SpaceGen.Star(new Vec3((float)m.X, (float)m.Y, -0.1f), s * 0.4f, 0.2f, 0.9f, 0.25f, 0.6f);   // green light-catchers inside
                if (m.Infected >= 0) _v[n++] = SpaceGen.Star(new Vec3((float)m.X, (float)m.Y, 0.1f), s * 1.35f, 1f, 0.15f, 0.1f, 0.35f);  // infected: a red rim
            }
            foreach (var vi in _world.Viruses)
            {
                if (n > _v.Length - 2) break;
                _v[n++] = SpaceGen.Star(new Vec3((float)vi.X, (float)vi.Y, -0.2f), 0.7f, 0.95f, 0.9f, 0.85f, 0.8f);
            }
            _buf.Write(_v, n);
            // looking straight down at the patch, like a microscope
            double halfW = MicrobeWorld.Width * 0.55 * _view, aspect = width / (double)Math.Max(1, height), halfH = halfW / aspect;
            var c = new float[BillboardPipeline.ConstantCount];
            c[0] = (float)(1 / halfW); c[5] = (float)(-1 / halfH); c[10] = 0.5f; c[15] = 1;
            c[12] = (float)(-_camX / halfW); c[13] = (float)(_camY / halfH); c[14] = 0.5f;
            c[16] = 1; c[21] = 1; c[24] = (float)time; c[25] = 1; c[26] = 2; c[27] = 4; c[28] = width; c[29] = height;
            // the background: deep, light-filled water, warmer in colour on the right
            gpu.ClearNow(0.03f, 0.08f, 0.12f);
            _dots.Draw(_buf, n, c, 0, 0, width, height);
        }

        public void Dispose() { _buf.Dispose(); }
    }
}
