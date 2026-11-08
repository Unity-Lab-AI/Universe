// Universe · entry point.
using System;
using Universe.Core;
using Universe.Platform;
using Universe.Render;
using Universe.Shell;

namespace Universe
{
    internal static class Program
    {
        static long _lastFrameT; static double _tuneMs;        // the GPU use: the last frame's time, and time since the detail was last nudged
        static long _frameT0;                                  // when the frame being made began (the processor's time on it, for the stats on screen)

        private static ulong Seed;
        private static UploadBuffer _waveBuf;                // the stored waveforms on the card (kept for the whole run)                 // this launch's universe (new each launch; --seed N plays one again)
        [STAThread]
        private static int Main(string[] args)
        {
            // any crash, on any thread, is written to the log with where it happened, before the program goes
            Universe.Platform.Watchdog.BeforeExit = () => { if (OpenClip != null) OpenClip.Dispose(); };   // (a safety exit finishes a clip being filmed first: a clip cut off without its index would not play)
            AppDomain.CurrentDomain.UnhandledException += (o, ev) => { try { if (OpenClip != null) OpenClip.Dispose(); } catch { } try {   // (a clip being filmed is finished first, so what was filmed still plays)
                                                                                     System.IO.Directory.CreateDirectory("build"); System.IO.File.AppendAllText(System.IO.Path.Combine("build", "last-run.log"), "crash · " + (ev.ExceptionObject.ToString().Contains("887A0005") || ev.ExceptionObject.ToString().Contains("887A0006") ? "the graphics card stopped answering (a frame took longer than Windows allows, so it reset the driver) · " : "") + ev.ExceptionObject + Environment.NewLine + (Universe.Render.Gpu.DeviceForLog != IntPtr.Zero ? Universe.Render.GpuLog.Describe(Universe.Render.Gpu.DeviceForLog) + Environment.NewLine : "")); } catch { } };
            // --smoke <ms>: a timed test run that closes itself after <ms> milliseconds.
            int smokeMs = -1; bool testStall = false; int testDeep = -1; double testZoom = -1; bool testVisit = false, testCore = false; int openSet = 0; bool openEncoders = false; int openLook = 0; double testEvolve = 0; int testPlanet = -1; double clickAt = -1, clickX = 0.5, clickY = 0.5; double wheelFrom = -1, wheelFor = 0, backFrom = -1, backFor = 0; int pressKey = -1; double pressAt = -1; int testDyson = -1; bool testInside = false; int testJumps = 0; bool testFieldView = false; double testAlt = -1; double[] tour = null; int tourStep = 0; string tourName = "tour"; double shotAt = -1; string shotPath = null; int testSpeed = -1; double tryEvery = -1; int wheelEvery = 1; bool aimCentre = false; double journeyAt = -1, deepSince = -1; double clipAt = -1, clipFor = 10; string clipPath = null; Universe.Render.GifWriter clip = null; int clipFrame = 0; bool wantShot = false, wantRecord = false, wantTour = false; Universe.Render.IClipWriter rec = null; double shotsEvery = -1, shotsNext = 0; int shotsN = 0; string shotName = null; string recordTour = null; bool recForTour = false;
            double shotsBelowKm = -1; bool shotsBmp = false;   // --shotsbelow <km>: shots only once the view is narrower than that; --bmp: shots as BMP (fast to write, so a test's frames stay quick)
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--smoke" && i + 1 < args.Length) int.TryParse(args[i + 1], out smokeMs);
                if (args[i] == "--shotsbelow" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shotsBelowKm);
                if (args[i] == "--bmp") shotsBmp = true;
                if (args[i] == "--test-stall") testStall = true; // proves the watchdog fires
                if (args[i] == "--deep" && i + 1 < args.Length) int.TryParse(args[i + 1], out testDeep);       // open deep zoom on a row
                if (args[i] == "--zoom" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out testZoom);
                if (args[i] == "--visit") testVisit = true;  // visit the middle star
                if (args[i] == "--set2") openSet = 2;       // open one sample set on its own
                if (args[i] == "--set3") openSet = 3;
                if (args[i] == "--set4") openSet = 4;
                if (args[i] == "--set5") openSet = 5;
                if (args[i] == "--set6") openSet = 6;
                if (args[i] == "--set7") openSet = 7;
                if (args[i] == "--set8") openSet = 8;
                if (args[i] == "--style") openSet = 9;       // the style sample for living and built things
                if (args[i] == "--family") openSet = 10;     // families of every kind of life, built from their genes
                if (args[i] == "--civ") openSet = 11;        // what an intelligent species builds, from its genes
                if (args[i] == "--encoders") openEncoders = true;   // the encoders' sample: the frame, our .cdf97w and our H.264, VP9 and AV1 side by side, over the universe
                if (args[i] == "--look" && i + 1 < args.Length) { int lk; if (int.TryParse(args[i + 1], out lk)) openLook = lk - 1; }
                if (args[i] == "--wheelback" && i + 2 < args.Length)   // --wheelback <start s> <for s>: roll the wheel out, for test runs
                {
                    double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out backFrom);
                    double.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out backFor);
                }
                if (args[i] == "--click" && i + 3 < args.Length)   // --click <x 0-1> <y 0-1> <s>: a left click at that place on screen, for test runs
                {
                    var ic = System.Globalization.CultureInfo.InvariantCulture; var nf = System.Globalization.NumberStyles.Float;
                    double.TryParse(args[i + 1], nf, ic, out clickX); double.TryParse(args[i + 2], nf, ic, out clickY); double.TryParse(args[i + 3], nf, ic, out clickAt);
                }
                if (args[i] == "--wheel" && i + 2 < args.Length)   // --wheel <start s> <for s>: roll the wheel in once a frame, for test runs
                {
                    double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wheelFrom);
                    double.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wheelFor);
                }
                if (args[i] == "--wheelevery" && i + 1 < args.Length) { int.TryParse(args[i + 1], out wheelEvery); wheelEvery = Math.Max(1, wheelEvery); }   // with --wheel: one notch every N frames, for a gentler zoom
                if (args[i] == "--aimcentre") aimCentre = true;   // test runs: the mouse rests in the middle of the picture, so the wheel zooms straight in
                if (args[i] == "--journeyat" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out journeyAt);   // seconds after entering a galaxy: fly to a star and pick its temperate world
                if (args[i] == "--shots" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shotsEvery);   // --shots <seconds>: a PNG of the game every so often (captures/shot_N.png), for test runs
                if (args[i] == "--record" && i + 1 < args.Length) recordTour = args[i + 1];   // --record <file.mp4>: with --tour, film the whole tour as an MP4 clip
                if (args[i] == "--clip" && i + 3 < args.Length)   // --clip <start s> <seconds> <file.gif>: save a looping clip of the game
                {
                    var ic = System.Globalization.CultureInfo.InvariantCulture; var nf = System.Globalization.NumberStyles.Float;
                    double.TryParse(args[i + 1], nf, ic, out clipAt); double.TryParse(args[i + 2], nf, ic, out clipFor); clipPath = args[i + 3];
                }
                if (args[i] == "--press" && i + 2 < args.Length)   // --press <key letter> <seconds>: press a key once, for test runs
                {
                    pressKey = char.ToUpperInvariant(args[i + 1][0]);
                    double.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pressAt);
                }
                if (args[i] == "--dyson" && i + 1 < args.Length) int.TryParse(args[i + 1], out testDyson);   // fly to Dyson sample N
                if (args[i] == "--inside") testInside = true;
                if (args[i] == "--jump" && i + 1 < args.Length) int.TryParse(args[i + 1], out testJumps);   // fly to the Nth kind of field
                if (args[i] == "--fieldview") testFieldView = true;
                if (args[i] == "--planet" && i + 1 < args.Length) int.TryParse(args[i + 1], out testPlanet);
                if (args[i] == "--tour" && i + 2 < args.Length)   // --tour <name> <km,km,...>: pictures at each height, 4 s apart
                {
                    tourName = args[i + 1];
                    var parts = args[i + 2].Split(',');
                    tour = new double[parts.Length];
                    for (int k = 0; k < parts.Length; k++) double.TryParse(parts[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tour[k]);
                }
                if (args[i] == "--evolve" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out testEvolve);   // --evolve <years>: start life and run it forward
                if (args[i] == "--alt" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out testAlt);
                if (args[i] == "--shot" && i + 2 < args.Length)   // --shot <seconds> <file.bmp>: save the game's own picture once
                {
                    double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shotAt);
                    shotPath = args[i + 2];
                }
                if (args[i] == "--core") testCore = true;    // zoom to the core black hole
                if (args[i] == "--speed" && i + 1 < args.Length) int.TryParse(args[i + 1], out testSpeed); // 0-4
                if (args[i] == "--try" && i + 1 < args.Length) double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tryEvery); // keep-or-revert prompt every N s
            }

            try { Win32.SetProcessDpiAwarenessContext(Win32.DPI_PER_MONITOR_AWARE_V2); } catch { }
            // run from the repo root no matter how the game was started (double-click, test.cmd, play.cmd)
            string exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            // the folder holding "content": the exe's own folder or one above it (build\ or build\dev\)
            string root = exeDir;
            for (int up = 0; up < 3 && root != null && !System.IO.Directory.Exists(System.IO.Path.Combine(root, "content")); up++) root = System.IO.Path.GetDirectoryName(root);
            if (root == null) root = exeDir;
            System.IO.Directory.SetCurrentDirectory(root);
            var clock = new Clock();
            // Safety: force-exit if the main loop stalls 6 s, or if a test overruns its time by 5 s.
            var watchdog = new Watchdog(clock, 6.0, smokeMs >= 0 ? smokeMs / 1000.0 + 5.0 : -1);

            ContentLibrary content = ContentLibrary.Load("content");
            bool big = Array.IndexOf(args, "--big") >= 0;                      // a full-size check, for pictures at the size the player sees
            bool native = Array.IndexOf(args, "--native") >= 0;                // a check at the screen's own full resolution, in a normal window
            var window = native ? new Window(Universe.Shell.PreviewText.AppName, 0, 0, false, true) : new Window(Universe.Shell.PreviewText.AppName, 1600, 900, smokeMs >= 0 && !big);   // timed checks open small and out of the way
            { int sci = Array.IndexOf(args, "--scale"); int pct; if (sci >= 0 && sci + 1 < args.Length && int.TryParse(args[sci + 1], out pct)) { window.RenderScale = pct / 100.0; window.Resized = false; } }   // --scale <percent>: render scale
            window.Input.IgnoreReal = smokeMs >= 0;          // timed checks never take the person's keyboard or mouse
            Gpu gpu;
            try { gpu = new Gpu(window.Handle, window.RenderWidth, window.RenderHeight); Universe.Render.ClipWriters.Device = gpu.Device; var g0 = gpu; Universe.Render.ClipWriters.BeforeCardRelease = () => { g0.Recorder = null; g0.WaitIdle(); }; }   // (the .cdf97w's wavelet runs on the same card)
            catch (Exception ex) { WriteLog("gpu error · " + ex.Message); window.Close(); return 2; }
            // the default sharpness from the graphics card: cards with little video memory of their own start softer, so they stay smooth
            if (Array.IndexOf(args, "--scale") < 0 && gpu.AdapterMemoryMB > 0 && gpu.AdapterMemoryMB < 3000)
            {
                window.RenderScale = gpu.AdapterMemoryMB < 1500 ? 0.5 : 0.75;
                gpu.Resize(window.RenderWidth, window.RenderHeight); window.Resized = false;
            }
            // how much of the card to use: kept from last time, else from its video memory (small cards start light, strong ones higher)
            Window.GpuName = gpu.AdapterName ?? ""; Window.GpuMB = gpu.AdapterMemoryMB;
            {
                int def = gpu.AdapterMemoryMB <= 0 ? 40 : gpu.AdapterMemoryMB < 1500 ? 25 : gpu.AdapterMemoryMB < 4000 ? 40 : 60;
                Window.GpuUse = Math.Max(10, Math.Min(90, Settings.Get("gpu-use", def))) / 100.0;
            }
            BillboardPipeline pipeline; SampleViewer viewer; MeshPipes meshes = null;
            try
            {
                // a new universe every launch: its seed from the clock and a fresh GUID, unless --seed N asks for one again
                int si = Array.IndexOf(args, "--seed"); ulong given;
                Seed = si >= 0 && si + 1 < args.Length && ulong.TryParse(args[si + 1], out given) ? given
                    : (ulong)DateTime.UtcNow.Ticks ^ BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                WriteLog("universe seed " + Seed + " (play it again with --seed " + Seed + ")");
                pipeline = new BillboardPipeline(gpu.Device);
                meshes = new MeshPipes(gpu, pipeline.RootSignature);
                Universe.Gen.PlanetTerrain.LoadFields(content);
                {
                    // the stored waveforms onto the card, once: the detail worked out per pixel reads them there
                    var wd0 = Universe.Core.Waveform.All(); var pal = Universe.Gen.GrainField.Palette(); var wd = new float[wd0.Length + pal.Length]; Array.Copy(wd0, wd, wd0.Length); Array.Copy(pal, 0, wd, wd0.Length, pal.Length);   // and after them every ground mix's minerals, for the grains worked out on the card
                    var packed = new StarVertex[(wd.Length + 7) / 8];
                    for (int q = 0; q < packed.Length; q++)
                    {
                        Func<int, float> at = k => q * 8 + k < wd.Length ? wd[q * 8 + k] : 0f;
                        packed[q] = new StarVertex { X = at(0), Y = at(1), Z = at(2), Size = at(3), R = at(4), G = at(5), B = at(6), A = at(7) };
                    }
                    _waveBuf = new UploadBuffer(gpu.Device, (long)packed.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof(StarVertex)));
                    _waveBuf.Write(packed, packed.Length); MeshDrawer.WaveAddress = _waveBuf.GpuAddress;
                }
                viewer = new SampleViewer(gpu, pipeline, meshes, content, Seed);
            }
            catch (Exception ex) { WriteLog("setup error · " + ex.Message); gpu.Dispose(); window.Close(); return 2; }

            double titleTimer = 0; int frames = 0; bool firstFrame = true;
            double nextAsk = tryEvery > 0 ? tryEvery : double.MaxValue;
            int menuSet = -1; bool menuDirty = true; string lastTitle = "";
            string endReason = "closed normally";
            DeepView deep = null;
            PlanetView planet = null;
            if (openSet > 0) viewer.ShowSet(openSet);
            bool journey = Array.IndexOf(args, "--journey") >= 0;
            ElementView elements = openSet == 7 ? new ElementView(gpu, meshes, content) : null; bool showElements = false;   // (from the universe, View > The periodic table opens it over the world; Escape comes back)
            MicrobeView microbes = openSet == 8 ? new MicrobeView(gpu, pipeline, Seed) : null;
            StyleView style = openSet >= 9 && openSet <= 11 ? new StyleView(gpu, meshes, Seed, openLook, openSet == 10, openSet == 11) : null;
            if (openSet == 6 && testDyson < 0)
            {
                var one = viewer.DysonAt(5).Enterable;
                planet = new PlanetView(gpu, meshes, pipeline, one, one, false); planet.Extras = viewer.DysonAt(5).Pieces;
            }
            if (testDyson >= 0 && viewer.DysonAt(testDyson).Enterable != null)
            {
                var sh = viewer.DysonAt(testDyson).Enterable;
                planet = new PlanetView(gpu, meshes, pipeline, sh, sh, false); planet.Extras = viewer.DysonAt(testDyson).Pieces;
                if (testInside) planet.GoInside();
                if (Array.IndexOf(args, "--centre") >= 0) planet.Centre();
                if (testAlt > 0) planet.SetAltitude(testAlt);
            }
            if (testPlanet >= 0) { planet = new PlanetView(gpu, meshes, pipeline, viewer.TerrainAt(testPlanet), viewer.FieldTerrainAt(testPlanet), viewer.Set == 5 || testFieldView); planet.TestEvolveYears = testEvolve; planet.TestGoToCiv = Array.IndexOf(args, "--tociv") >= 0; int sli = Array.IndexOf(args, "--savelife"); if (sli >= 0 && sli + 1 < args.Length) planet.TestSaveLife = args[sli + 1]; int lli = Array.IndexOf(args, "--loadlife"); if (lli >= 0 && lli + 1 < args.Length) planet.TestLoadLife = args[lli + 1]; int csi = Array.IndexOf(args, "--civstep"); if (csi >= 0 && csi + 1 < args.Length) int.TryParse(args[csi + 1], out planet.TestCivStep); for (int k = 0; k < testJumps; k++) planet.JumpToNextField(); if (testAlt > 0) planet.SetAltitude(testAlt); int si = Array.IndexOf(args, "--undersea"); if (si >= 0 && si + 1 < args.Length) planet.GoUnderSea(double.Parse(args[si + 1], System.Globalization.CultureInfo.InvariantCulture)); }
            GalaxiesView universe = journey ? new GalaxiesView(gpu, pipeline, content, Seed) : null;
            // a .cdf97w handed over (double-clicked in Explorer): the clip of the universe itself plays through the universe made again
            // from its seed, straight away; a waveform clip plays in the program's own player, then the landing page
            string handed = args.Length == 1 && args[0].EndsWith(".cdf97w", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(args[0]) ? args[0] : null;
            if (handed != null && Universe.Render.UniverseClipReader.Is(handed)) { try { var rc = new Universe.Render.UniverseClipReader(handed); if (rc.Seed.HasValue) Seed = rc.Seed.Value; Universe.Platform.Launcher.ReplayClip = handed; WriteLog("universe clip handed over: " + handed + ", seed " + Seed); } catch (Exception ex) { WriteLog("universe clip: " + ex.Message); handed = null; } }
            bool waveHanded = false;
            if (handed != null && Universe.Platform.Launcher.ReplayClip == null) { Universe.Platform.ClipPlayer.Play(window.Handle, handed, () => watchdog.Beat()); waveHanded = true; }
            bool landing = args.Length == 0 || waveHanded; landing = landing && PreviewBuild;                            // a plain launch: the landing page first
            Universe.Shell.FrontmanCompare frontman = null; bool frontWanted = false; Universe.Render.UniverseClipPlayer clipPlayer = null;   // (a clip of the universe itself, played through the universe made again)
            Universe.Platform.Launcher.Choice landed = Universe.Platform.Launcher.Choice.Begin;
            if (landing)
            {
                gpu.BeginFrame(0.005f, 0.006f, 0.012f); gpu.EndFrame();                 // (the game's window dark behind the page, never a blank white)
                ulong? chosen; landed = Universe.Platform.Launcher.Run(window.Handle, () => DocViewer.Show(window.Handle, PreviewText.Docs(), 0), () => watchdog.Beat(), out chosen);
                if (landed == Universe.Platform.Launcher.Choice.Quit) { window.Close(); gpu.Dispose(); return 0; }
                if (chosen.HasValue) { Seed = chosen.Value; WriteLog("universe seed " + Seed + " (chosen on the landing page)"); }
            }
            Universe.Shell.ScaleHud firstHud = null;
            if (Array.IndexOf(args, "--world") >= 0 || PreviewBuild || openEncoders)
            {
                // the loading screen from the very first frame: the universe's making begins before its own screen can be drawn,
                // and those seconds were a black window
                try { firstHud = new Universe.Shell.ScaleHud(gpu.Device); gpu.BeginFrame(0.005f, 0.006f, 0.012f); firstHud.DrawLoading(meshes, gpu.Width, gpu.Height, 0, new[] { "Making the universe", "the first galaxies, from the seed" }); gpu.EndFrame(); } catch { }
            }
            UniverseView world = Array.IndexOf(args, "--world") >= 0 || PreviewBuild || openEncoders ? new UniverseView(gpu, pipeline, meshes, content, Seed) { AutoPath = Array.IndexOf(args, "--autopath") >= 0, AutoNebula = Array.IndexOf(args, "--autonebula") >= 0 } : null;   // the universe: one camera through every scale
            if (world != null && Universe.Platform.Launcher.ReplayClip != null) { try { clipPlayer = new Universe.Render.UniverseClipPlayer(new Universe.Render.UniverseClipReader(Universe.Platform.Launcher.ReplayClip)); } catch (Exception ex) { WriteLog("universe clip: " + ex.Message); } }
            if (openEncoders && world != null) frontman = new Universe.Shell.FrontmanCompare(gpu.Device) { On = true };   // (a sample, opened on its own; never part of the game)
            if (landing && landed == Universe.Platform.Launcher.Choice.Tour && world != null)
            {
                world.StartTour(Universe.Platform.Launcher.ReplayTour ?? "grand-tour");
                if (Universe.Platform.Launcher.RecordTour && Universe.Platform.Launcher.ReplayTour == null)
                {
                    // filmed: the whole tour as the clip of the universe itself in the captures folder, finished when the tour ends
                    try { System.IO.Directory.CreateDirectory(Universe.Platform.Launcher.Captures); } catch { }
                    recordTour = System.IO.Path.Combine(Universe.Platform.Launcher.Captures, "tour " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4");
                }
            }
            { int ti = Array.IndexOf(args, "--tour"); if (world != null && ti >= 0) world.StartTour(ti + 1 < args.Length && !args[ti + 1].StartsWith("--") ? args[ti + 1] : "grand-tour"); }   // a tour (content/tours/name.unt)
            if (journey && Array.IndexOf(args, "--test-system") >= 0) { deep = new DeepView(gpu, pipeline, content, viewer.TypeOf(0), viewer.NameOf(0), Seed); deep.TestJourney(gpu.Width, gpu.Height); }
            if (testDeep >= 0) deep = new DeepView(gpu, pipeline, content, viewer.TypeOf(testDeep), viewer.NameOf(testDeep), Seed + (ulong)testDeep * 7919UL);
            while (window.Pump())
            {
                clock.Tick();
                watchdog.Beat();

                if (deep == null && planet == null && window.Input.WasPressed(Win32.VK_ESCAPE)) window.Close();
                // the review menu: rebuilt whenever the set or the keepers change
                if (world != null) { if (menuDirty || world.MenuChanged) { Window.TimeNames = UniverseView.TimeNames; Window.TimeStep = world.TimeStep; window.SetWorldMenu(world.ScaleOn); menuDirty = false; world.MenuChanged = false; } }
                else if (viewer.Set != menuSet || menuDirty) { window.SetReviewMenu(viewer.KeeperLabels(), viewer.KeeperTicks()); menuSet = viewer.Set; menuDirty = false; }
                int cmd = window.MenuCommand; window.MenuCommand = 0;
                bool askNow = false;
                if (cmd == Window.CmdExit) { endReason = "closed with the Exit button"; window.Close(); }
                else if (cmd == Window.CmdLanding)
                {
                    // back to the start page: the program opens afresh on it (a new universe, the tour, the clips) and this one closes
                    if (rec != null) { rec.Dispose(); rec = null; }
                    // in this same window: this universe is let go, the start page shown again, and the universe it asks for made
                    if (world != null) { world.Dispose(); world = null; }
                    showElements = false; recForTour = false; recordTour = null; wantTour = false;
                    ulong? chosen2; var landed2 = Universe.Platform.Launcher.Run(window.Handle, () => DocViewer.Show(window.Handle, PreviewText.Docs(), 0), () => watchdog.Beat(), out chosen2);
                    if (landed2 == Universe.Platform.Launcher.Choice.Quit) { endReason = "closed from the start page"; window.Close(); }
                    else
                    {
                        Seed = chosen2.HasValue ? chosen2.Value : (ulong)DateTime.UtcNow.Ticks ^ BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                        WriteLog("universe seed " + Seed + (chosen2.HasValue ? " (chosen on the start page)" : " (new, from the start page)"));
                        try { if (firstHud == null) firstHud = new Universe.Shell.ScaleHud(gpu.Device); gpu.BeginFrame(0.005f, 0.006f, 0.012f); firstHud.DrawLoading(meshes, gpu.Width, gpu.Height, 0, new[] { "Making the universe", "the first galaxies, from the seed" }); gpu.EndFrame(); } catch { }
                        world = new UniverseView(gpu, pipeline, meshes, content, Seed);
                        clipPlayer = null; if (Universe.Platform.Launcher.ReplayClip != null) { try { clipPlayer = new Universe.Render.UniverseClipPlayer(new Universe.Render.UniverseClipReader(Universe.Platform.Launcher.ReplayClip)); } catch (Exception ex) { WriteLog("universe clip: " + ex.Message); } }
                        if (landed2 == Universe.Platform.Launcher.Choice.Tour)
                        {
                            world.StartTour(Universe.Platform.Launcher.ReplayTour ?? "grand-tour");
                            if (Universe.Platform.Launcher.RecordTour && Universe.Platform.Launcher.ReplayTour == null) { try { System.IO.Directory.CreateDirectory(Universe.Platform.Launcher.Captures); } catch { } recordTour = System.IO.Path.Combine(Universe.Platform.Launcher.Captures, "tour " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4"); }
                        }
                        menuDirty = true;
                    }
                }
                else if (cmd == Window.CmdAskNow) askNow = true;
                else if (cmd == Window.CmdLegend) window.Tell(style != null ? style.Legend() : microbes != null ? microbes.Legend() : elements != null ? elements.Legend() : planet != null ? planet.Legend() : viewer.Legend(), Universe.Shell.PreviewText.AppName + " · Legend");
                else if (cmd == Window.CmdPause && world != null) world.TogglePause();
                else if (cmd >= Window.CmdTimeBase && cmd < Window.CmdTimeBase + 16 && world != null) world.SetTime(cmd - Window.CmdTimeBase);
                else if (cmd > Window.CmdDragBase && cmd <= Window.CmdDragBase + 10) { Settings.Set("drag-speed", cmd - Window.CmdDragBase); menuDirty = true; }
                else if (cmd >= Window.CmdGpuBase && cmd < Window.CmdGpuBase + Window.GpuPercents.Length) { Window.GpuUse = Window.GpuPercents[cmd - Window.CmdGpuBase] / 100.0; Settings.Set("gpu-use", Window.GpuPercents[cmd - Window.CmdGpuBase]); menuDirty = true; }
                else if (cmd >= Window.CmdScaleBase && cmd < Window.CmdScaleBase + Window.ScalePercents.Length) { window.RenderScale = Window.ScalePercents[cmd - Window.CmdScaleBase] / 100.0; menuDirty = true; }
                // the documents open in their own viewer window, beside the game
                else if (cmd == Window.CmdDocs) DocViewer.Show(window.Handle, PreviewText.Docs(), 0);
                else if (cmd == Window.CmdAbout) DocViewer.Show(window.Handle, PreviewText.Docs(), 1);
                else if (cmd == Window.CmdReadme) DocViewer.Show(window.Handle, PreviewText.Docs(), 2);
                else if (cmd == Window.CmdHowItWorks) DocViewer.Show(window.Handle, PreviewText.Docs(), 3);
                else if (cmd == Window.CmdCompared) DocViewer.Show(window.Handle, PreviewText.Docs(), 5);
                else if (cmd == Window.CmdElements && world != null) { if (elements == null) elements = new ElementView(gpu, meshes, content); showElements = !showElements; }
                else if (cmd == Window.CmdCredits) DocViewer.Show(window.Handle, PreviewText.Docs(), 8);
                else if (cmd == Window.CmdStats) { Settings.Set("show-stats", !Settings.Get("show-stats", true)); menuDirty = true; }
                else if (cmd == Window.CmdInvertX) { Settings.Set("invert-left-right", !Settings.Get("invert-left-right", false)); menuDirty = true; }
                else if (cmd == Window.CmdShot) wantShot = true;
                else if (cmd == Window.CmdRecord) wantRecord = true;
                else if (cmd == Window.CmdTour) wantTour = true;
                else if (cmd == Window.CmdFps) { Settings.Set("show-fps", !Settings.Get("show-fps", true)); menuDirty = true; }
                else if (cmd == Window.CmdGravity && world != null) { world.ToggleGravity(); menuDirty = true; }
                else if (cmd == Window.CmdAlwaysDay) { Settings.Set("always-day", !Settings.Get("always-day", true)); menuDirty = true; }
                else if (cmd == Window.CmdInvertY) { Settings.Set("invert-up-down", !Settings.Get("invert-up-down", false)); menuDirty = true; }
                else if (cmd >= Window.CmdClipFormatBase && cmd <= Window.CmdClipFormatBase + 2) { Settings.Set("clip-format", cmd - Window.CmdClipFormatBase); menuDirty = true; }   // the clip's format: MP4 or .cdf97w (takes effect on the next clip)
                else if (cmd >= Window.CmdClipBase && cmd <= Window.CmdClipBase + 4) { Settings.Set("clip-down", cmd - Window.CmdClipBase); menuDirty = true; }   // the clip's size (takes effect on the next clip)
                else if (cmd == Window.CmdScaleHud && world != null) { world.ScaleOn = !world.ScaleOn; menuDirty = true; }
                else if (cmd == Window.CmdClearMarks) { viewer.ClearMarks(); menuDirty = true; }
                else if (cmd >= Window.CmdKeeperBase && cmd < Window.CmdKeeperBase + 9) { if (style != null) style.ToggleMark(cmd - Window.CmdKeeperBase); else viewer.ToggleMark(cmd - Window.CmdKeeperBase); menuDirty = true; }
                // keep-or-revert safety pop-up: Yes keeps the test going, No or no answer in 15 s closes it safely
                if (askNow || (tryEvery > 0 && clock.Total >= nextAsk))
                {
                    int answer = window.Ask("Keep this test running?\n\nYes = keep going\nNo = revert (close the game safely)\n\nNo answer in 15 seconds = revert.", Universe.Shell.PreviewText.AppName + " · Keep or revert", 15);
                    if (answer == Win32.IDYES) nextAsk = tryEvery > 0 ? clock.Total + tryEvery : double.MaxValue;
                    else { endReason = answer == Win32.MB_TIMEDOUT ? "closed safely: no answer to the keep-or-revert pop-up" : "reverted with the No button"; window.Close(); }
                }
                if (testStall && clock.Total > 0.5) System.Threading.Thread.Sleep(10000);

                _frameT0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (window.Resized) { window.Resized = false; gpu.Resize(window.RenderWidth, window.RenderHeight); }
                if (deep != null && testDeep >= 0 && frames == 2)
                {
                    if (testCore) deep.TestCore(); if (testVisit) deep.TestVisit(gpu.Width, gpu.Height);
                    if (testZoom > 0) deep.TestZoom(testZoom);
                    if (testSpeed >= 0) deep.TestSpeed(testSpeed);
                    testDeep = -1;
                }
                if (pressKey >= 0 && clock.Total >= pressAt) { window.Input.Press(pressKey); pressKey = -1; }
                if (aimCentre) { window.Input.MouseX = gpu.Width / 2; window.Input.MouseY = gpu.Height / 2; }
                if (deep != null && deepSince < 0) deepSince = clock.Total;
                if (deep != null && journeyAt >= 0 && clock.Total >= deepSince + journeyAt) { deep.TestJourney(gpu.Width, gpu.Height); journeyAt = -1; }
                if (wheelFrom >= 0 && clock.Total >= wheelFrom && clock.Total < wheelFrom + wheelFor && frames % wheelEvery == 0 && (world == null || world.MatterKm <= 0 || world.MatterKm > Universe.Shell.MatterView.KnownKm)) window.Input.Notch(1);   // (the test's wheel ends on the last landmark, the quark, never on into nothing)
                if (backFrom >= 0 && clock.Total >= backFrom && clock.Total < backFrom + backFor) window.Input.Notch(-1);
                if (clickAt >= 0 && clock.Total >= clickAt) { window.Input.MouseX = (int)(clickX * gpu.Width); window.Input.MouseY = (int)(clickY * gpu.Height); window.Input.LeftClicked = true; clickAt = -1; }
                window.Input.Glide(clock.Delta, window.Input.IsDown(0x11));      // the wheel glides in smoothly; hold Ctrl: eight times as fast, in every view
                if (showElements && window.Input.WasPressed(0x1B)) showElements = false;   // Escape: back from the periodic table to the universe, where you were
                if (world != null && !showElements) { if (clipPlayer != null) { clipPlayer.Before(world, clock.Delta); world.ClipNote = clipPlayer.Note; } if (!world.Update(window.Input, clock.Delta, gpu.Width, gpu.Height)) window.Close(); if (clipPlayer != null) { clipPlayer.After(world); world.ClipNote = clipPlayer.Note; if (clipPlayer.Finished) { WriteLog("universe clip played to its end (" + clipPlayer.Mismatches + " frames whose scene was not the one recorded)"); clipPlayer = null; world.ClipNote = null; } } var uc = rec as Universe.Render.UniverseClipWriter; if (uc != null && !(recForTour && (world.TourPaused || world.Loading))) uc.Point(world); }   // (the universe clip: the camera's point each frame, no pixels)
                else if (style != null) style.Update(window.Input, clock.Delta, gpu.Width, gpu.Height);
                else if (microbes != null) microbes.Update(window.Input, clock.Delta);
                else if (elements != null) elements.Update(window.Input, clock.Delta);
                else if (planet != null) { if (!planet.Update(window.Input, clock.Delta))
                    {
                        if (viewer.Set == 6) window.Close();          // the Dyson sphere is the whole set: leaving it closes the sample
                        else { planet.Dispose(); planet = null; if (deep != null) deep.ReturnFromPlanet(); }   // the journey: back up to the solar system
                    } }
                else if (deep == null && universe == null)
                {
                    viewer.Update(window.Input, clock.Delta);
                    if (window.Input.RightClicked) { viewer.ToggleMark(window.Input.MouseX, window.Input.MouseY, gpu.Width, gpu.Height); menuDirty = true; }
                    else if ((window.Input.LeftClicked || window.Input.Wheel > 0) && viewer.Set == 6)
                    {
                        int col = Math.Max(0, Math.Min(2, window.Input.MouseX / Math.Max(1, gpu.Width / 3)));
                        var ds = viewer.DysonAt(viewer.RowAt(window.Input.MouseY, gpu.Height) * 3 + col);
                        if (ds.Enterable != null) { planet = new PlanetView(gpu, meshes, pipeline, ds.Enterable, ds.Enterable, false); planet.Extras = ds.Pieces; }
                    }
                    else if ((window.Input.LeftClicked || window.Input.Wheel > 0) && (viewer.Set == 4 || viewer.Set == 5))
                    {
                        int col = Math.Max(0, Math.Min(2, window.Input.MouseX / Math.Max(1, gpu.Width / 3)));
                        int pick = viewer.RowAt(window.Input.MouseY, gpu.Height) * 3 + col;
                        planet = new PlanetView(gpu, meshes, pipeline, viewer.TerrainAt(pick), viewer.FieldTerrainAt(pick), viewer.Set == 5);
                    }
                    else if (window.Input.LeftClicked && viewer.Set == 2) viewer.PickFocus(window.Input.MouseX, window.Input.MouseY, gpu.Width, gpu.Height);
                    else if (window.Input.LeftClicked && viewer.Set == 1)
                    {
                        int row = viewer.RowAt(window.Input.MouseY, gpu.Height);
                        deep = new DeepView(gpu, pipeline, content, viewer.TypeOf(row), viewer.NameOf(row), Seed + (ulong)row * 7919UL);
                    }
                }
                else if (deep == null && universe != null)
                {
                    // the start of the journey: the mix of galaxies; close enough to one, fly into it
                    if (!universe.Update(window.Input, clock.Delta, gpu.Width, gpu.Height)) window.Close();
                    else if (universe.HandOff >= 0)
                    {
                        var g = universe.Focus;
                        deep = new DeepView(gpu, pipeline, content, g.Type, g.Name, g.Seed) { Meshes = meshes };
                        float gy, gp; universe.AngleInFocus(out gy, out gp); deep.SetAngle(gy, gp);   // carry on at the angle you came in at
                        // no target is forced: the wheel zooms where the mouse points, or click a star to fly to it
                        universe.HandOff = -1;
                    }
                }
                else if (!deep.Update(window.Input, clock.Delta, gpu.Width, gpu.Height))
                {
                    if (journey && deep.Ascend && universe != null) { deep.Dispose(); deep = null; universe.ReturnFromGalaxy(); }
                    else if (journey) window.Close();
                    else { deep.Dispose(); deep = null; }
                }
                else if (deep.HandOff >= 0)
                {
                    // the journey: close enough to a planet, so fly down to it, carrying on from the same distance
                    int b = deep.HandOff;
                    var body = deep.System.Bodies[b];
                    // carry on with the same 3D planet the solar system view was already drawing, so nothing jumps
                    var t = deep.FocusTerrain ?? new Universe.Gen.PlanetTerrain(gpu.Device, body.Terrain ?? TerrainKind(body.Kind), body.Kind, (uint)(deep.SystemSeed * 977UL + (ulong)b));
                    var tf = deep.FocusFields ?? t;
                    planet = new PlanetView(gpu, meshes, pipeline, t, tf, false) { Journey = true };
                    planet.SetAltitude((deep.Distance / deep.BodyRadiusUnits(b) - 1) * t.Radius);
                    deep.HandOff = -1;
                }
                gpu.BeginFrame(0.005f, 0.006f, 0.012f);
                if (world != null && !showElements) world.Draw(gpu.Width, gpu.Height, clock.Total);
                else if (style != null) style.Draw(gpu, gpu.Width, gpu.Height, clock.Total);
                else if (microbes != null) microbes.Draw(gpu, gpu.Width, gpu.Height, clock.Total);
                else if (elements != null) elements.Draw(gpu, gpu.Width, gpu.Height, clock.Total);
                else if (planet != null) planet.Draw(gpu, gpu.Width, gpu.Height);
                else if (deep == null && universe != null) universe.Draw(gpu.Width, gpu.Height, clock.Total);
                else if (deep == null) viewer.Draw(gpu.Width, gpu.Height, clock.Total); else deep.Draw(gpu.Width, gpu.Height, clock.Total);
                if (frontman != null && frontman.On && world != null && !showElements && rec == null) frontman.Draw(meshes, window.Input, gpu.Width, gpu.Height);   // (the encoders' sample over the universe, never into a clip; after the views' own chain, so the chain stays the universe's)
                if (shotPath != null && clock.Total >= shotAt) { gpu.CaptureNext = shotPath; shotPath = null; }
                // screenshots (F12) and clips (F9), from any view: the finished frame copied back from the card; the panoramic
                // tour on and off (T); with --record, the whole tour filmed and the clip finished when it ends
                {
                    var inp = window.Input; string caps = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captures"), stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                    if (inp.WasPressed(0x7B)) wantShot = true;
                    if (shotsEvery > 0 && clock.Total >= shotsNext && (shotsBelowKm <= 0 || (world != null && world.ViewKm < shotsBelowKm)))
                    {
                        shotsNext = clock.Total + shotsEvery; string nm = "shot_" + (shotsN++).ToString("00");
                        if (shotsBmp) gpu.CaptureNext = System.IO.Path.Combine(caps, nm + ".bmp"); else { wantShot = true; shotName = nm; }
                    }
                    if (inp.WasPressed(0x78)) wantRecord = true;
                    if (inp.WasPressed('T') && world != null && !world.Touring) wantTour = true;
                    if (wantTour && world != null) { if (world.Touring) world.StopTour(); else if (world.TourPaused) world.ResumeTour(); else world.StartTour("grand-tour"); wantTour = false; menuDirty = true; }
                    if (recordTour != null && world != null && world.Touring && rec == null && !recForTour) { try { Universe.Render.ClipWriters.Seed = Seed; Universe.Render.ClipWriters.Tour = "grand-tour"; rec = OpenClip = new Universe.Render.UniverseClipWriter(System.IO.Path.ChangeExtension(recordTour, ".cdf97w")); recForTour = true; /* (a filmed tour is always the clip of the universe itself: the seed, the track, the scene; a few megabytes, never pictures; nothing written beside it) */ } catch (Exception ex) { WriteLog("record error · " + ex.Message); recordTour = null; } }
                    if (recForTour && world != null && !world.Touring && !(world.TourPaused && !world.TourFinished)) { if (rec != null) { rec.Dispose(); WriteLog("clip saved · " + rec.Path); rec = null; } recForTour = false; recordTour = null; menuDirty = true; }
                    if (wantRecord)
                    {
                        wantRecord = false; menuDirty = true;
                        if (rec != null) { rec.Dispose(); WriteLog("clip saved · " + rec.Path); rec = null; recForTour = false; }
                        else try { Universe.Render.ClipWriters.Seed = Seed; Universe.Render.ClipWriters.Tour = null; rec = OpenClip = Universe.Render.ClipWriters.Open(System.IO.Path.Combine(caps, Universe.Shell.PreviewText.AppName + " " + stamp + ".mp4"), gpu.Width, gpu.Height); } catch (Exception ex) { WriteLog("record error · " + ex.Message); rec = null; }
                    }
                    if (gpu.Grabbed != null && clipPath == null && frontman != null && frontWanted && rec == null && !wantShot && gpu.GrabW == gpu.Width / frontman.Down(gpu.Width)) { frontman.Take(gpu.Grabbed, gpu.GrabW, gpu.GrabH, clock.Total); frontWanted = false; }   // (the front man compared: a fresh frame for its tiles)
                    if (gpu.Grabbed != null && clipPath == null)
                    {
                        if (wantShot && gpu.GrabW == gpu.Width) { try { Universe.Render.PngWriter.Save(System.IO.Path.Combine(caps, (shotName ?? Universe.Shell.PreviewText.AppName + " " + stamp) + ".png"), gpu.Grabbed, gpu.GrabW, gpu.GrabH); } catch (Exception ex) { WriteLog("screenshot error · " + ex.Message); } wantShot = false; shotName = null; }
                        // (a frame grabbed at full size for a screenshot is not the clip's size: that one is skipped, the clip goes on)
                        if (rec != null && gpu.GrabW == gpu.Width / Math.Max(1, rec.Down) && !(recForTour && world != null && (world.TourPaused || world.Loading))) { try { rec.Add(gpu.Grabbed, gpu.GrabW, gpu.GrabH); } catch (Exception ex) { WriteLog("record error · " + ex.Message); rec.Dispose(); rec = null; } }
                        gpu.Grabbed = null;
                    }
                    { var cr = rec as Universe.Render.ICardRecorder; bool onCard = cr != null && (rec is Universe.Render.Cdf97wWriter ? ((Universe.Render.Cdf97wWriter)rec).OnCard : rec is Universe.Render.FrontmanWriter && ((Universe.Render.FrontmanWriter)rec).OnCard); gpu.Recorder = onCard ? cr : null;   // (a .cdf97w worked out on the card inside the frame: the picture is never copied back for it)
                    if (frontman != null && rec == null && !wantShot && frontman.Wants(clock.Total) && !frontWanted) { frontWanted = true; gpu.GrabNext = true; gpu.GrabDown = frontman.Down(gpu.Width); }
                    if (wantShot || (rec != null && !onCard && !(rec is Universe.Render.UniverseClipWriter))) { gpu.GrabNext = true; gpu.GrabDown = wantShot ? 1 : rec.Down; } }   // (a screenshot at full size; a clip at its own, half each way on a big screen)
                    // the frame's picture is taken here, before the REC notice goes on: the notice shows on screen (as a camcorder's) and stays out of the clip and the screenshot
                    gpu.CaptureHere();
                    if (rec != null && world != null && !showElements) world.DrawRec(gpu.Width, gpu.Height, clock.Total, rec.Seconds);
                    Universe.Shell.ScaleHud.TextScale = rec != null ? Math.Max(1f, 1080f * rec.Down / Math.Max(1, gpu.Height)) : 1;   // (a clip made smaller than 1080 high: its text drawn larger, so in the clip every letter cell is at least two of its pixels, as on a 1080 screen; a half clip of a 4K screen needs nothing)
                    if (Window.Recording != (rec != null) || (world != null && Window.Touring != world.Touring)) { Window.Recording = rec != null; Window.Touring = world != null && world.Touring; menuDirty = true; }
                }
                if (clipPath != null && clipAt >= 0 && clock.Total >= clipAt)
                {
                    // every fourth frame (15 a second at 60 frames a second) goes into the clip
                    if (gpu.Grabbed != null) { if (clip == null) clip = new Universe.Render.GifWriter(gpu.Width, gpu.Height, 7); if (gpu.Grabbed.Length == gpu.Width * gpu.Height * 3) clip.Add(gpu.Grabbed); gpu.Grabbed = null; }
                    if (clock.Total < clipAt + clipFor) { if (clipFrame++ % 4 == 0) gpu.GrabNext = true; }
                    else { if (clip != null) clip.Save(clipPath); clipPath = null; smokeMs = (int)(clock.Total * 1000) + 300; }
                }
                if (tour != null && planet != null && tourStep < tour.Length * 2 && clock.Total >= 1.0 + tourStep * 3.0)
                {
                    if (tourStep % 2 == 0) planet.SetAltitude(tour[tourStep / 2]);
                    else gpu.CaptureNext = "build/" + tourName + "_" + (tourStep / 2) + ".bmp";
                    tourStep++;
                    if (tourStep == tour.Length * 2) smokeMs = (int)(clock.Total * 1000) + 500;
                }
                Universe.Platform.Stats.CpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _frameT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // (the processor's part of the frame: up to handing it to the card)
                gpu.EndFrame();
                // keep the card as busy as the GPU use asks (never over 90%): its working time against the frame's time, the
                // detail nudged up or down twice a second
                {
                    long nowT = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (_lastFrameT > 0 && gpu.GpuMs > 0)
                    {
                        double frameMs = (nowT - _lastFrameT) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        if (frameMs > 0.5 && frameMs < 500) Window.GpuBusy += (Math.Min(1, gpu.GpuMs / frameMs) - Window.GpuBusy) * 0.1;
                        _tuneMs += frameMs;
                        if (_tuneMs > 500 && world != null && world.AtGround)                   // what it tunes so far: the ground's detail
                        {
                            _tuneMs = 0; double want = Math.Min(0.9, Window.GpuUse); int backlog = world.GroundBacklog;
                            // the card is not the only limit: when the ground cannot be built and put on the card as fast as it is
                            // asked for, the detail comes down (else it would keep asking for more than can ever arrive)
                            // nor is the card the only clock: the processor's part of the frame (the ground's pieces chosen, drawn and
                            // taken in, the matter, the bodies) is a limit of its own: the detail comes down when it runs over about
                            // twenty milliseconds, and goes up only while it is under fourteen (it had climbed to 275% at five frames
                            // a second, the card idle while the processor drowned in a thousand pieces a frame)
                            double cpuMs = Universe.Platform.Stats.CpuSmooth;
                            if (backlog > 1500 || cpuMs > 20) Window.Detail = Math.Max(0.2, Window.Detail * 0.85);
                            else if (Window.GpuBusy < want - 0.05 && backlog < 400 && cpuMs < 14) Window.Detail = Math.Min(4, Window.Detail * 1.08);
                            else if (Window.GpuBusy > want + 0.03) Window.Detail = Math.Max(0.2, Window.Detail * 0.9);
                        }
                    }
                    _lastFrameT = nowT;
                }
                if (firstFrame) { firstFrame = false; watchdog.Running(); }

                frames++; titleTimer += clock.Delta;
                if (titleTimer >= 0.5)
                {
                    lastTitle = string.Format("{0} · {1:0} fps · universe " + Seed, world != null && !showElements ? world.Title : style != null ? style.Title : microbes != null ? microbes.Title : elements != null ? elements.Title : planet != null ? planet.Title : deep == null ? (universe != null ? universe.Title : viewer.Title) : deep.Title, frames / titleTimer);
                    window.SetTitle(lastTitle);
                    Window.Fps = frames / titleTimer; titleTimer = 0; frames = 0;
                }
                Universe.Platform.Stats.Phases = world != null && !showElements ? world.CpuPhases : null;
                Universe.Platform.Stats.Frame(gpu.GpuMs, world != null && world.AtGround, world != null ? world.GroundBacklog : 0, gpu.VideoMemory, gpu.AdapterName);   // (the stats on screen, sampled twice a second)

                window.Input.EndFrame();
                if (smokeMs >= 0 && clock.Total * 1000 >= smokeMs) { window.Close(); smokeMs = -1; }
            }
            if (rec != null) { try { rec.Dispose(); WriteLog("clip saved · " + rec.Path); } catch { } rec = null; }   // a clip being filmed when the window closes is finished, so it plays
            if (deep != null) deep.Dispose();
            if (elements != null) elements.Dispose();
            if (firstHud != null) firstHud.Dispose();
            if (universe != null) universe.Dispose();
            if (microbes != null) microbes.Dispose();
            if (style != null) style.Dispose();
            viewer.Dispose(); if (planet != null) planet.Dispose(); if (meshes != null) meshes.Dispose(); pipeline.Dispose(); gpu.Dispose();
            watchdog.Stop();
            WriteLog(string.Format("ok · {0} content entries from {1} files · ran {2:0.00} s · gpu {3}x{4} on {6} ({7:N0} MB) · {5}", content.Entries.Count, content.Files.Count, clock.Total, gpu.Width, gpu.Height, endReason, gpu.AdapterName, gpu.AdapterMemoryMB) + Environment.NewLine + "last title: " + lastTitle);
            return 0;
        }

        /// <summary>Which kind of ground a planet of the solar system gets in the planet view.</summary>
        private static string TerrainKind(string bodyKind)
        {
            switch (bodyKind)
            {
                case "Temperate world": return "temperate";
                case "Lava planet": return "lava";
                case "Ice world": return "snowball";
                case "Gas giant": return "gas-giant";
                case "Ice giant": return "ice-giant";
                case "Hot Jupiter": return "hot-jupiter";
                default: return "desert";                     // rocky planets and moons: dry rock and dust
            }
        }

        // the universe preview release is built with /define:PREVIEW: it always opens in the universe view
#if PREVIEW
        private const bool PreviewBuild = true;
#else
        private const bool PreviewBuild = false;
#endif

        // the clip being filmed, if any: a crash finishes it first, so it still plays
        private static Universe.Render.IClipWriter OpenClip;
        private static bool _logStarted;
        internal static void WriteLog(string line)
        {
            try
            {
                System.IO.Directory.CreateDirectory("build");
                // every line of this run kept (the first starts the file afresh): an error early on is not wiped by a later line
                string lf = System.IO.Path.Combine("build", "last-run.log");
                if (_logStarted) System.IO.File.AppendAllText(lf, line + Environment.NewLine); else { System.IO.File.WriteAllText(lf, line + Environment.NewLine); _logStarted = true; }
            }
            catch { }
        }
    }
}
