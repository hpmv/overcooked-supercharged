using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SuperchargedPatch
{
    // Only the Unity main thread's own process window can be addressed. This
    // never sends keys, changes desktop resolution, or activates another app.
    public static class NativeWindowControl
    {
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        public static string LastRequest { get; private set; }
        private static IntPtr ownWindow;

        private static bool BelongsToThisProcess(IntPtr window)
        {
            uint process;
            return window != IntPtr.Zero && GetWindowThreadProcessId(window, out process) != 0 && process == GetCurrentProcessId();
        }

        private static IntPtr OwnWindow()
        {
            IntPtr window = GetActiveWindow();
            if (BelongsToThisProcess(window)) ownWindow = window;
            if (!BelongsToThisProcess(ownWindow))
                throw new InvalidOperationException("Unity main thread has no verified process-owned active window.");
            return ownWindow;
        }

        public static void SetMinimized(bool minimize)
        {
            ShowWindow(OwnWindow(), minimize ? 6 : 4); // SW_MINIMIZE / SW_SHOWNOACTIVATE
            LastRequest = minimize ? "minimize" : "show-without-activation";
        }

        public static object Observe()
        {
            try { return new Dictionary<string, object> { { "minimized", IsIconic(OwnWindow()) }, { "lastRequest", LastRequest } }; }
            catch (Exception error) { return new Dictionary<string, object> { { "error", error.Message }, { "lastRequest", LastRequest } }; }
        }
    }
}
