// Universe · render · when the graphics card is lost, what it was doing: Windows' own record of each command the card finished
// (DRED breadcrumbs), the address of any read it could not make (a page fault), why the device was removed, and each draw of
// the frame named by what it drew, so the crash log says which draw the card died on.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Render
{
    internal static class GpuLog
    {
        [DllImport("d3d12.dll")] private static extern int D3D12GetDebugInterface(ref Guid iid, out IntPtr debug);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetEnablementFn(IntPtr self, int enablement);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetOutputFn(IntPtr self, IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int RemovedReasonFn(IntPtr self);

        /// <summary>What is being drawn now (set before each part of the frame): each draw is recorded under it.</summary>
        public static string Pass = "";
        private static List<string> _frame = new List<string>(), _submitted = new List<string>();
        private static readonly Dictionary<ulong, KeyValuePair<long, string>> _buffers = new Dictionary<ulong, KeyValuePair<long, string>>();

        /// <summary>Turns on Windows' record of what the card does (before the device is made): its finished commands and any
        /// read it could not make. Asks nothing of a computer that has no such record.</summary>
        public static void Enable()
        {
            try
            {
                Guid iid = new Guid("82BC481C-6B9B-4030-AEDB-7EE3D1DF1E63");               // ID3D12DeviceRemovedExtendedDataSettings
                IntPtr s; if (D3D12GetDebugInterface(ref iid, out s) < 0 || s == IntPtr.Zero) return;
                Com.Method<SetEnablementFn>(s, 3)(s, 1);                                    // the breadcrumbs: forced on
                Com.Method<SetEnablementFn>(s, 4)(s, 1);                                    // the page fault's address: forced on
                Com.Release(ref s);
            }
            catch { }
        }

        public static void Draw(int count) { lock (_frame) _frame.Add(Pass + " (" + count + ")"); }
        /// <summary>The frame's commands are handed to the card: its draws are kept as the ones the card is working on.</summary>
        public static void Submitted() { lock (_frame) { _submitted = _frame; _frame = new List<string>(); } }
        public static void Buffer(ulong address, long bytes) { lock (_buffers) _buffers[address] = new KeyValuePair<long, string>(bytes, Pass); }
        public static void BufferGone(ulong address) { lock (_buffers) _buffers.Remove(address); }

        /// <summary>Why the device was lost and what it was doing, in plain words, for the crash log.</summary>
        public static string Describe(IntPtr device)
        {
            var o = new StringBuilder();
            try
            {
                int why = Com.Method<RemovedReasonFn>(device, 37)(device);
                o.Append("device removed: 0x").Append(why.ToString("X8")).Append(why == unchecked((int)0x887A0006) ? " (hung: a command never finished)" : why == unchecked((int)0x887A0007) ? " (reset)" : why == unchecked((int)0x887A0020) ? " (driver internal error)" : why == unchecked((int)0x887A002B) ? " (a bad command)" : "").Append(Environment.NewLine);
                Guid iid = new Guid("98931D33-5AE8-4791-AA3C-1A73A2934E71");                // ID3D12DeviceRemovedExtendedData
                IntPtr dred; if (Com.Method<QueryInterfaceFn>(device, 0)(device, ref iid, out dred) < 0 || dred == IntPtr.Zero) { o.Append("(no record of what the card was doing on this computer)"); return o.ToString(); }
                // the read it could not make, and the buffer of ours it falls in (or near)
                IntPtr pf = Marshal.AllocHGlobal(24);
                try
                {
                    if (Com.Method<GetOutputFn>(dred, 4)(dred, pf) >= 0)
                    {
                        ulong va = (ulong)Marshal.ReadInt64(pf, 0);
                        o.Append("page fault at 0x").Append(va.ToString("X")).Append(va == 0 ? " (none)" : "");
                        if (va != 0) lock (_buffers) foreach (var b in _buffers) if (va >= b.Key && va < b.Key + (ulong)b.Value.Key + (1UL << 20)) o.Append(va < b.Key + (ulong)b.Value.Key ? " · inside" : " · just past").Append(" a buffer made during \"").Append(b.Value.Value).Append("\" (").Append(b.Value.Key).Append(" bytes)");
                        o.Append(Environment.NewLine);
                    }
                }
                finally { Marshal.FreeHGlobal(pf); }
                // the commands it finished: the draw after the last finished one is the one it died on
                IntPtr ab = Marshal.AllocHGlobal(8);
                try
                {
                    if (Com.Method<GetOutputFn>(dred, 3)(dred, ab) >= 0)
                    {
                        IntPtr node = Marshal.ReadIntPtr(ab, 0); int guard = 0;
                        while (node != IntPtr.Zero && guard++ < 16)
                        {
                            int count = Marshal.ReadInt32(node, 48); IntPtr lastP = Marshal.ReadIntPtr(node, 56), hist = Marshal.ReadIntPtr(node, 64);
                            int last = lastP != IntPtr.Zero ? Marshal.ReadInt32(lastP) : -1;
                            int draws = 0, drawsDone = 0;
                            for (int k = 0; k < count; k++) { int op = Marshal.ReadInt32(hist, k * 4); if (op == 3 || op == 4) { draws++; if (k < last) drawsDone++; } }
                            o.Append("commands: ").Append(count).Append(", finished ").Append(last).Append("; draws: ").Append(draws).Append(", finished ").Append(drawsDone).Append(Environment.NewLine);
                            List<string> names; lock (_frame) names = _submitted;
                            if (drawsDone < draws && drawsDone < names.Count)
                            {
                                o.Append("it died on draw ").Append(drawsDone + 1).Append(": ").Append(names[drawsDone]).Append(Environment.NewLine);
                                for (int q = Math.Max(0, drawsDone - 3); q < Math.Min(names.Count, drawsDone + 4); q++) o.Append(q == drawsDone ? "  >> " : "     ").Append(names[q]).Append(Environment.NewLine);
                            }
                            node = Marshal.ReadIntPtr(node, 72);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(ab); }
                Com.Release(ref dred);
            }
            catch (Exception ex) { o.Append("(reading the record failed: ").Append(ex.Message).Append(")"); }
            return o.ToString();
        }
    }
}
