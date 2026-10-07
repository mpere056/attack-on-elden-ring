// A crosshair over the centre of Elden Ring's window, drawn as a tiny transparent, click-through,
// always-on-top window of our own. AoTTG2's camera is Elden Ring's camera, so the centre of Elden
// Ring's screen is exactly where AoTTG2 aims. White: nothing hookable there; green: a surface or an
// enemy within hook reach. This replaced drawing the crosshair inside Elden Ring's frame: the D3D12
// hooks that needed coincided with two NVIDIA driver resets. This window never touches Elden Ring.
// It runs on its own thread (a window needs a message loop); the game thread only sets the state.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aoer
{
    internal static class CrosshairWindow
    {
        private const int Size = 33;  // pixels, odd so there is a centre pixel

        // State set by the game thread, applied by the window thread.
        private static volatile bool _visible, _green;
        private static volatile int _cx, _cy;
        private static Thread _thread;

        public static void Show(int centreX, int centreY, bool green)
        {
            _cx = centreX; _cy = centreY; _green = green; _visible = true;
            if (_thread == null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "AoER crosshair" };
                _thread.Start();
            }
        }

        public static void Hide() => _visible = false;

        // ---- Win32 ----
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8,
                           WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
        private const uint ULW_ALPHA = 2, PM_REMOVE = 1;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10;

        private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public POINT p; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h,
                                                     IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG m, IntPtr h, uint a, uint b, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src,
                                                       ref POINT srcPos, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr name);

        private static WndProc _proc;  // kept alive: Windows calls it

        private static unsafe void Run()
        {
            try
            {
                _proc = (h, m, w, l) => DefWindowProcW(h, m, w, l);
                IntPtr inst = GetModuleHandleW(IntPtr.Zero);
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = "AoERCrosshair" };
                RegisterClassExW(ref wc);
                IntPtr hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                                              "AoERCrosshair", "", WS_POPUP, 0, 0, Size, Size, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Plugin.L.LogWarning("crosshair: could not create its window"); return; }

                IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
                var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = Size, biHeight = -Size, biPlanes = 1, biBitCount = 32 };
                IntPtr dib = CreateDIBSection(screen, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
                SelectObject(mem, dib);
                Plugin.L.LogMessage("crosshair: overlay window ready");

                bool shown = false, drawnGreen = false, drawn = false;
                int lastX = int.MinValue, lastY = int.MinValue;
                while (true)
                {
                    while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE)) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
                    if (!_visible)
                    {
                        if (shown) { ShowWindow(hwnd, SW_HIDE); shown = false; }
                        Thread.Sleep(30);
                        continue;
                    }
                    int x = _cx - Size / 2, y = _cy - Size / 2;
                    bool green = _green;
                    if (!drawn || green != drawnGreen || x != lastX || y != lastY)
                    {
                        if (!drawn || green != drawnGreen) Draw((uint*)bits, green);
                        var pos = new POINT { X = x, Y = y };
                        var size = new SIZE { W = Size, H = Size };
                        var src = new POINT();
                        var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
                        UpdateLayeredWindow(hwnd, screen, ref pos, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
                        drawn = true; drawnGreen = green; lastX = x; lastY = y;
                    }
                    if (!shown) { ShowWindow(hwnd, SW_SHOWNOACTIVATE); shown = true; }
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | 0x2 /* NOMOVE */);
                    Thread.Sleep(16);
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError("crosshair: " + e.Message);
            }
        }

        // Four ticks and a centre dot with a dark outline; premultiplied BGRA.
        private static unsafe void Draw(uint* px, bool green)
        {
            uint fill = green ? 0xFF59FF73u : 0xFFFFFFFFu;  // ARGB
            uint edge = 0x99000000u;                         // black, 60 % (premultiplied: rgb 0)
            int c = Size / 2;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int ax = Math.Abs(x - c), ay = Math.Abs(y - c);
                    bool core = (ax == 0 && ay >= 5 && ay <= 13) || (ay == 0 && ax >= 5 && ax <= 13) || (ax <= 1 && ay <= 1);
                    bool outline = (ax <= 1 && ay >= 4 && ay <= 14) || (ay <= 1 && ax >= 4 && ax <= 14) || (ax <= 2 && ay <= 2);
                    px[y * Size + x] = core ? fill : outline ? edge : 0u;
                }
        }
    }
}
