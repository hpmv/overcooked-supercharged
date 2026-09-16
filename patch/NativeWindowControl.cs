using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SuperchargedPatch
{
    // Only the Unity main thread's own process window can be addressed. This
    // never sends keys or changes desktop resolution. An explicit minimize may
    // return foreground ownership to the Windows shell if Unity still owns it.
    public static class NativeWindowControl
    {
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        public static string LastRequest { get; private set; }
        public static bool ForegroundReleaseAttempted { get; private set; }
        public static bool ForegroundReleaseSucceeded { get; private set; }
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
            IntPtr window = OwnWindow();
            ShowWindow(window, minimize ? 6 : 4); // SW_MINIMIZE / SW_SHOWNOACTIVATE
            ForegroundReleaseAttempted = false;
            ForegroundReleaseSucceeded = false;
            // SW_MINIMIZE normally transfers foreground ownership, but a fresh
            // Unity startup can remain the Windows foreground owner while its
            // only window is iconic. Since this call is itself an explicit
            // authoring minimize request, release only that stale ownership to
            // the shell. Never disturb another application's foreground.
            if (minimize && BelongsToThisProcess(GetForegroundWindow()))
            {
                IntPtr shell = GetShellWindow();
                if (shell != IntPtr.Zero && !BelongsToThisProcess(shell))
                {
                    ForegroundReleaseAttempted = true;
                    ForegroundReleaseSucceeded = SetForegroundWindow(shell);
                }
            }
            LastRequest = minimize ? "minimize" : "show-without-activation";
        }

        public static void Activate()
        {
            IntPtr window = OwnWindow();
            ShowWindow(window, 9); // SW_RESTORE
            if (!SetForegroundWindow(window))
                throw new InvalidOperationException("Windows refused to activate the verified Unity process window.");
            LastRequest = "activate";
        }

        public static object Observe()
        {
            try
            {
                IntPtr window = OwnWindow();
                return new Dictionary<string, object> {
                    { "minimized", IsIconic(window) },
                    { "foregroundOwned", BelongsToThisProcess(GetForegroundWindow()) },
                    { "foregroundReleaseAttempted", ForegroundReleaseAttempted },
                    { "foregroundReleaseSucceeded", ForegroundReleaseSucceeded },
                    { "lastRequest", LastRequest }
                };
            }
            catch (Exception error) { return new Dictionary<string, object> { { "error", error.Message }, { "lastRequest", LastRequest } }; }
        }
    }
}
