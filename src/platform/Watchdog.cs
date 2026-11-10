// Universe · platform · safety watchdog.
// If the main loop stops answering, or a timed test run overruns, the game shuts itself down
// so the graphics card and the window are always released. The game never changes display
// settings, so exiting the process is a full revert.
using System;
using System.IO;
using System.Threading;

namespace Universe.Platform
{
    internal sealed class Watchdog
    {
        private long _lastBeat;
        private readonly double _stallSeconds;
        private readonly double _hardLimitSeconds; // < 0 means no limit (normal play)
        private readonly Clock _clock;
        private volatile bool _stop;
        private volatile bool _running; // false during start-up
        private const double StartupLimitSeconds = 60.0;   // (a busy machine at a large size can take a while to start; only a true hang is stopped)

        public Watchdog(Clock clock, double stallSeconds, double hardLimitSeconds)
        {
            _clock = clock; _stallSeconds = stallSeconds; _hardLimitSeconds = hardLimitSeconds;
            Beat();
            var t = new Thread(Run) { IsBackground = true, Name = Universe.Shell.PreviewText.AppName + " watchdog" };
            t.Start();
        }

        /// <summary>Call once per frame from the main loop.</summary>
        public void Beat() { Interlocked.Exchange(ref _lastBeat, BitConverter.DoubleToInt64Bits(_clock.Total)); }

        /// <summary>Call once the first frame has been shown: from then on the strict stall limit applies.</summary>
        public void Running() { Beat(); _running = true; }

        public void Stop() { _stop = true; }

        private void Run()
        {
            while (!_stop)
            {
                Thread.Sleep(250);
                double now = _clock.Total;
                double last = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastBeat));
                if (Window.InSizeMove || Window.InMenu || Environment.TickCount - Window.ModalIdleTick < 2000) { Beat(); continue; } // not a stall: the player is moving the window, holding a menu open, or in a dialog or message box (Windows runs its own loop for those and the game's waits: it had been shut down as hung after six seconds of a menu held open)
                double limit = _running ? _stallSeconds : StartupLimitSeconds;
                if (now - last > limit) Bail((_running ? "main loop stalled for " : "start-up took longer than ") + (now - last).ToString("0.0") + " s");
                if (_hardLimitSeconds >= 0 && now > _hardLimitSeconds + (_running ? 0 : StartupLimitSeconds)) Bail("test run passed its hard limit of " + _hardLimitSeconds + " s");
            }
        }

        /// <summary>Run before a safety exit: a clip being filmed is finished first, so it plays.</summary>
        public static Action BeforeExit;
        private static void Bail(string why)
        {
            try { if (BeforeExit != null) BeforeExit(); } catch (System.Exception ex) { Universe.Program.WriteLog("failed in finishing before a safety exit: " + ex.Message); }
            try
            {
                string dir = Universe.Program.LogDir; Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "watchdog.log"), DateTime.Now.ToString("s") + "  SAFETY EXIT: " + why + Environment.NewLine);
                Universe.Program.WriteLog("SAFETY EXIT by the watchdog: " + why + (Window.LastMenuNote != null ? " (" + Window.LastMenuNote + ")" : ""));
            }
            catch { }
            Environment.Exit(3);
        }
    }
}
