// While AoTTG2's picture is drawn into Elden Ring, AoTTG2's own window becomes an almost invisible
// (1/255 opacity), borderless, always-on-top layer exactly over Elden Ring's window. You see Elden
// Ring (with the soldier drawn in), while keyboard and mouse still go to AoTTG2 underneath. AoTTG2
// keeps rendering normally; the compositor captures its frames, not what is shown on screen.
// End() puts the window back exactly as it was. F6 (compositing off) or F7 (unlink) call it.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Aoer
{
    internal static class Overlay
    {
        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const long WS_POPUP = 0x80000000L, WS_VISIBLE = 0x10000000L;
        private const long WS_EX_LAYERED = 0x00080000L;
        private const uint LWA_ALPHA = 0x2;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        private delegate bool EnumProc(IntPtr h, IntPtr p);

        private static IntPtr _hwnd;
        private static IntPtr _oldStyle, _oldEx;
        private static RECT _oldRect;
        private static bool _pending, _applied;
        private static float _nextCheck;
        private static int _x, _y, _w, _h;

        public static bool Applied => _applied;

        public static void Begin()
        {
            End();
            _hwnd = FindOwnWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Plugin.L.LogWarning("overlay: AoTTG2's window not found; it stays as a normal window");
                return;
            }
            _pending = true;  // applied once AoTTG2 has switched to Elden Ring's size
        }

        /// <summary>Every frame while compositing: apply once ready, follow Elden Ring's window.</summary>
        public static void Tick()
        {
            if (_hwnd == IntPtr.Zero || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            if (!Bridge.ReadState(out var s) || s.WinW <= 0 || s.WinH <= 0) return;
            if (_pending)
            {
                if (Screen.width != s.WinW && Screen.width != (int)s.BackW) return;  // resolution change not done yet
                _oldStyle = GetWindowLongPtrW(_hwnd, GWL_STYLE);
                _oldEx = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
                GetWindowRect(_hwnd, out _oldRect);
                SetWindowLongPtrW(_hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));
                SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, new IntPtr(_oldEx.ToInt64() | WS_EX_LAYERED));
                SetLayeredWindowAttributes(_hwnd, 0, 1, LWA_ALPHA);
                _pending = false;
                _applied = true;
                _x = int.MinValue;  // force a move below
                SetForegroundWindow(_hwnd);
                Plugin.L.LogMessage($"overlay: AoTTG2's window is now an invisible layer over Elden Ring ({s.WinW}x{s.WinH} at {s.WinX},{s.WinY})");
            }
            if (_applied && (s.WinX != _x || s.WinY != _y || s.WinW != _w || s.WinH != _h))
            {
                _x = s.WinX; _y = s.WinY; _w = s.WinW; _h = s.WinH;
                SetWindowPos(_hwnd, HWND_TOPMOST, _x, _y, _w, _h, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
        }

        public static void End()
        {
            if (_applied && _hwnd != IntPtr.Zero)
            {
                SetWindowLongPtrW(_hwnd, GWL_STYLE, _oldStyle);
                SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, _oldEx);
                SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
                SetWindowPos(_hwnd, HWND_NOTOPMOST, _oldRect.Left, _oldRect.Top,
                             _oldRect.Right - _oldRect.Left, _oldRect.Bottom - _oldRect.Top, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                Plugin.L.LogMessage("overlay: AoTTG2's window restored");
            }
            _applied = false;
            _pending = false;
        }

        private static IntPtr FindOwnWindow()
        {
            IntPtr main = Process.GetCurrentProcess().MainWindowHandle;
            if (main != IntPtr.Zero) return main;
            uint me = (uint)Environment.ProcessId;
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, p) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == me && IsWindowVisible(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
