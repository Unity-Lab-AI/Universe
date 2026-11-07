// Universe · render · calling COM objects (Direct3D 12, DXGI) through their method tables. Our own code.
using System;
using System.Runtime.InteropServices;

namespace Universe.Render
{
    internal static class Com
    {
        /// <summary>Gets method number <paramref name="slot"/> of a COM object as a callable delegate.</summary>
        public static T Method<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }

        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException(what + " failed: 0x" + hr.ToString("X8"));
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate uint ReleaseFn(IntPtr self);

        public static IntPtr Query(IntPtr obj, Guid iid, string what)
        {
            IntPtr r; Check(Method<QueryInterfaceFn>(obj, 0)(obj, ref iid, out r), what); return r;
        }

        public static void Release(ref IntPtr obj)
        {
            if (obj != IntPtr.Zero) { Method<ReleaseFn>(obj, 2)(obj); obj = IntPtr.Zero; }
        }
    }
}
