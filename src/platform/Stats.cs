// Universe · platform · the numbers shown with the frames a second (View > Show GPU and CPU stats, or the tick on the landing
// page): the processor's and the card's time on each frame, the processor's share of every core, the memory in use, what the
// card's own memory is holding of what it allows, how busy the card is, the detail, and the ground pieces waiting. Sampled
// twice a second from Windows' own counts of this process, never more, so the stats cost nothing to show.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Universe.Platform
{
    internal static class Stats
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr h, out long created, out long exited, out long kernel, out long user);
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr h, ref MemCounters c, uint size);
        [StructLayout(LayoutKind.Sequential)]
        private struct MemCounters { public uint Size, PageFaults; public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile; }

        /// <summary>The processor's time on the frame being made (from its start to handing it to the card), set each frame.</summary>
        public static double CpuMs;
        /// <summary>The lines shown under the frames a second (the card's blocky letters: capitals, digits, . , : % ( ) + - /); none until the first sample.</summary>
        public static string[] Lines = new string[0];

        private static long _wallAt, _cpuAt; private static double _cpuSum, _gpuSum; private static int _n;

        /// <summary>Once a frame: the card's time on the last frame, whether the ground is in view and how many of its pieces
        /// wait to be built, the card's memory (used and allowed, MB; null if it cannot say) and the card's name. The lines are
        /// rewritten twice a second.</summary>
        public static void Frame(double gpuMs, bool atGround, int backlog, Func<long[]> videoMemory, string card)
        {
            _cpuSum += CpuMs; _gpuSum += gpuMs; _n++;
            long now = Stopwatch.GetTimestamp();
            long created, exited, kernel, user;
            if (_wallAt == 0) { _wallAt = now; if (GetProcessTimes(GetCurrentProcess(), out created, out exited, out kernel, out user)) _cpuAt = kernel + user; return; }
            double wall = (now - _wallAt) / (double)Stopwatch.Frequency;
            if (wall < 0.5) return;
            try
            {
                // the processor: this program's share of every core over the last half second (its kernel and user time, in
                // Windows' hundred-nanosecond ticks, against the wall clock and the number of cores)
                double share = 0;
                if (GetProcessTimes(GetCurrentProcess(), out created, out exited, out kernel, out user))
                {
                    long cpu = kernel + user; share = (cpu - _cpuAt) / 1e7 / wall / Math.Max(1, Environment.ProcessorCount); _cpuAt = cpu;
                }
                var mc = new MemCounters { Size = (uint)Marshal.SizeOf(typeof(MemCounters)) };
                long mb = GetProcessMemoryInfo(GetCurrentProcess(), ref mc, mc.Size) ? (long)(mc.WorkingSet.ToUInt64() >> 20) : 0;
                double cpuMs = _n > 0 ? _cpuSum / _n : 0, gpuAvg = _n > 0 ? _gpuSum / _n : 0;
                var lines = new System.Collections.Generic.List<string>();
                lines.Add("CPU " + cpuMs.ToString("0.0") + " ms, " + Math.Round(share * 100) + "% of " + Environment.ProcessorCount + " cores, " + mb + " MB");
                // the card: its time on a frame, how busy it is of the frame's time, and its own memory this program holds of
                // what Windows allows it
                long[] vm = videoMemory != null ? videoMemory() : null;
                string g = "GPU " + gpuAvg.ToString("0.0") + " ms, " + Math.Round(Window.GpuBusy * 100) + "% busy";
                if (vm != null && vm.Length == 2 && vm[1] > 0) g += ", " + Gb(vm[0]) + " of " + Gb(vm[1]);
                lines.Add(g);
                if (!string.IsNullOrEmpty(card)) lines.Add(card.Length > 38 ? card.Substring(0, 38) : card);
                lines.Add("detail " + Math.Round(Window.Detail * 100) + "%" + (atGround && backlog > 0 ? ", " + backlog + " ground pieces waiting" : ""));
                Lines = lines.ToArray();
            }
            catch { }
            _wallAt = now; _cpuSum = 0; _gpuSum = 0; _n = 0;
        }

        private static string Gb(long mb) { return mb >= 1000 ? (mb / 1024.0).ToString("0.0") + " GB" : mb + " MB"; }
    }
}
