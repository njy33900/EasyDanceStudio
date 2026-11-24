using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MmdStudio.Poc
{
    // 2026-11-24: minimum window size. The UI shrinks with the window (PanelScreenMatchMode.Shrink),
    // so below 960x540 client pixels it would get too small to use. Windows asks a window for its
    // limits with WM_GETMINMAXINFO before every resize (edge drags and SetResolution alike); a hook
    // on the Player window answers it. Windows Player only; removed again when the app quits, so no
    // message reaches managed code during shutdown.
    internal static class MmdWindowLimits
    {
        public const int MinClientWidth = 960, MinClientHeight = 540;
        public static bool Installed { get; private set; }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int GWLP_WNDPROC = -4, GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const uint WM_GETMINMAXINFO = 0x0024, WM_WINDOWPOSCHANGING = 0x0046, SWP_NOSIZE = 0x0001;
        private const int WS_THICKFRAME = 0x00040000, WS_MAXIMIZE = 0x01000000;
        [StructLayout(LayoutKind.Sequential)] private struct WindowPos { public IntPtr hwnd, insertAfter; public int x, y, cx, cy; public uint flags; }
        private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumProc(IntPtr hWnd, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point reserved, maxSize, maxPosition, minTrackSize, maxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int left, top, right, bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr CallWindowProcW(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hWnd, int index);
        [DllImport("user32.dll")] private static extern bool AdjustWindowRectEx(ref Rect32 rect, uint style, bool menu, uint exStyle);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect32 rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        internal static int MessagesSeen, Clamped;

        // Probe hooks (Win32 directly, independent of Unity's resize path): the limits the window
        // answers, whether it is the visible Player window, and its client size after a plain
        // SetWindowPos shrink (put back afterwards).
        internal static Vector2Int AskMinimumTrack()
        {
            if (!Installed) return default;
            var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
            try
            {
                Marshal.StructureToPtr(new MinMaxInfo(), buffer, false);
                SendMessageW(window, WM_GETMINMAXINFO, IntPtr.Zero, buffer);
                var info = Marshal.PtrToStructure<MinMaxInfo>(buffer);
                return new Vector2Int(info.minTrackSize.x, info.minTrackSize.y);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal static Vector2Int ClientSize() { if (!Installed) return default; GetClientRect(window, out var r); return new Vector2Int(r.right - r.left, r.bottom - r.top); }
        internal static bool VisibleWindow => Installed && IsWindowVisible(window);
        internal static Vector2Int ShrinkAndRestore(int outerWidth, int outerHeight)
        {
            if (!Installed) return default;
            GetWindowRect(window, out var original);
            SetWindowPos(window, IntPtr.Zero, 0, 0, outerWidth, outerHeight, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            var client = ClientSize();
            SetWindowPos(window, IntPtr.Zero, 0, 0, original.right - original.left, original.bottom - original.top, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            return client;
        }
        private static readonly WndProc HookProc = Hook;        // kept alive: Windows holds a pointer to it
        private static readonly EnumProc FindProc = Find;
        private static IntPtr window, previous;
        private static uint process;

        public static void Install()
        {
            if (Installed) return;
            try
            {
                process = GetCurrentProcessId(); window = IntPtr.Zero;
                EnumWindows(FindProc, IntPtr.Zero);
                if (window == IntPtr.Zero) { Debug.LogWarning("Minimum window size: Player window not found"); return; }
                previous = SetWindowLongPtrW(window, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(HookProc));
                if (previous == IntPtr.Zero) { Debug.LogWarning("Minimum window size: hook not installed"); return; }
                Installed = true;
                Application.quitting += Uninstall;
            }
            catch (Exception ex) { Debug.LogWarning("Minimum window size not available: " + ex.Message); }
        }
        public static void Uninstall()
        {
            if (!Installed) return;
            SetWindowLongPtrW(window, GWLP_WNDPROC, previous);
            Installed = false;
        }

        [AOT.MonoPInvokeCallback(typeof(EnumProc))]
        private static bool Find(IntPtr hWnd, IntPtr data)
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner != process) return true;
            var name = new System.Text.StringBuilder(64);
            GetClassNameW(hWnd, name, name.Capacity);
            if (name.ToString() != "UnityWndClass") return true;
            window = hWnd;
            return false;
        }

        // Client minimum -> outer window minimum for this window's frame.
        private static Point MinimumOuter(IntPtr hWnd, int style)
        {
            var frame = new Rect32 { right = MinClientWidth, bottom = MinClientHeight };
            AdjustWindowRectEx(ref frame, (uint)style, false, (uint)GetWindowLongW(hWnd, GWL_EXSTYLE));
            return new Point { x = frame.right - frame.left, y = frame.bottom - frame.top };
        }

        [AOT.MonoPInvokeCallback(typeof(WndProc))]
        private static IntPtr Hook(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            // Every resize (edge drags and SetResolution) passes WM_WINDOWPOSCHANGING: clamp the size
            // of the normal resizable window (not maximized, not the borderless full screen).
            if (message == WM_WINDOWPOSCHANGING || message == WM_GETMINMAXINFO) MessagesSeen++;
            if (message == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
            {
                var style = GetWindowLongW(hWnd, GWL_STYLE);
                var position = Marshal.PtrToStructure<WindowPos>(lParam);
                if ((position.flags & SWP_NOSIZE) == 0 && (style & WS_THICKFRAME) != 0 && (style & WS_MAXIMIZE) == 0)
                {
                    var minimum = MinimumOuter(hWnd, style);
                    if (position.cx < minimum.x || position.cy < minimum.y)
                    {
                        position.cx = Math.Max(position.cx, minimum.x); position.cy = Math.Max(position.cy, minimum.y);
                        Marshal.StructureToPtr(position, lParam, false);
                        Clamped++;
                    }
                }
            }
            var result = CallWindowProcW(previous, hWnd, message, wParam, lParam);
            // Interactive sizing asks for the limits first (the drag stops at the minimum).
            if (message == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
            {
                var minimum = MinimumOuter(hWnd, GetWindowLongW(hWnd, GWL_STYLE));
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                info.minTrackSize.x = Math.Max(info.minTrackSize.x, minimum.x);
                info.minTrackSize.y = Math.Max(info.minTrackSize.y, minimum.y);
                Marshal.StructureToPtr(info, lParam, false);
            }
            return result;
        }
#else
        public static void Install() { }
        public static void Uninstall() { }
        internal static int MessagesSeen, Clamped;
        internal static Vector2Int AskMinimumTrack() => default;
        internal static Vector2Int ClientSize() => default;
        internal static bool VisibleWindow => false;
        internal static Vector2Int ShrinkAndRestore(int outerWidth, int outerHeight) => default;
#endif
    }
}
