// ODM cables drawn over Elden Ring: a click-through, always-on-top layered window covering Elden
// Ring's client area, showing a line from the Tarnished's waist to each active hook. AoTTG2 knows
// where its hooks are; its camera is Elden Ring's camera (same pose and vertical FOV), so each point
// is projected with that camera and Elden Ring's aspect ratio. Like CrosshairWindow it never touches
// Elden Ring's rendering. No occlusion: cables show through walls. Own thread with a message loop.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Aoer
{
    internal static class CableOverlay
    {
        public struct Segment { public float X0, Y0, X1, Y1; }

        private static volatile Segment[] _segments = Array.Empty<Segment>();
        private static volatile bool _visible;
        private static volatile int _x, _y, _w, _h;
        private static Thread _thread;

        /// <summary>Game thread: cables for this frame, in Elden Ring client-area pixels.</summary>
        public static void Set(int winX, int winY, int winW, int winH, Segment[] segments)
        {
            _x = winX; _y = winY; _w = winW; _h = winH;
            _segments = segments;
            _visible = segments.Length > 0;
            if (_thread == null && _visible)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "AoER cables" };
                _thread.Start();
            }
        }

        public static void Hide() => _visible = false;

        /// <summary>
        /// Projects a world point (AoTTG2 space) to Elden Ring client pixels through AoTTG2's camera
        /// pose with Elden Ring's aspect ratio. False if behind the camera.
        /// </summary>
        public static bool Project(Transform cam, float fovDeg, int w, int h, Vector3 p, out float sx, out float sy)
        {
            Vector3 v = p - cam.position;
            float z = Vector3.Dot(v, cam.forward);
            sx = sy = 0;
            if (z < 0.05f) return false;
            float t = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)w / h;
            sx = (Vector3.Dot(v, cam.right) / (z * t * aspect) * 0.5f + 0.5f) * w;
            sy = (0.5f - Vector3.Dot(v, cam.up) / (z * t) * 0.5f) * h;
            return true;
        }

        // ---- window thread (same Win32 approach as CrosshairWindow) ----
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
        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src,
                                                       ref POINT srcPos, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr name);

        private static WndProc _proc;

        private static unsafe void Run()
        {
            try
            {
                _proc = (h, m, w, l) => DefWindowProcW(h, m, w, l);
                IntPtr inst = GetModuleHandleW(IntPtr.Zero);
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = "AoERCables" };
                RegisterClassExW(ref wc);
                const uint ex = 0x80000 | 0x20 | 0x8 | 0x80 | 0x08000000;  // layered, transparent, topmost, toolwindow, noactivate
                IntPtr hwnd = CreateWindowExW(ex, "AoERCables", "", 0x80000000 /* WS_POPUP */, 0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Plugin.L.LogWarning("cables: could not create the overlay window"); return; }
                IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), dib = IntPtr.Zero;
                uint* bits = null;
                int bw = 0, bh = 0;
                bool shown = false, dirty = false;
                Plugin.L.LogMessage("cables: overlay window ready");
                while (true)
                {
                    while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
                    int w = _w, h = _h;
                    if (!_visible || w <= 0 || h <= 0)
                    {
                        if (shown) { ShowWindow(hwnd, 0); shown = false; }
                        Thread.Sleep(20);
                        continue;
                    }
                    if (w != bw || h != bh)
                    {
                        if (dib != IntPtr.Zero) DeleteObject(dib);
                        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                        dib = CreateDIBSection(screen, ref bi, 0, out IntPtr p, IntPtr.Zero, 0);
                        SelectObject(mem, dib);
                        bits = (uint*)p; bw = w; bh = h; dirty = true;
                    }
                    if (dirty || _segments.Length > 0)
                    {
                        new Span<uint>(bits, bw * bh).Clear();
                        foreach (var s in _segments) Line(bits, bw, bh, s.X0, s.Y0, s.X1, s.Y1);
                        dirty = _segments.Length > 0;
                        var pos = new POINT { X = _x, Y = _y };
                        var size = new SIZE { W = bw, H = bh };
                        var src = new POINT();
                        var blend = new BLENDFUNCTION { Alpha = 255, Format = 1 };
                        UpdateLayeredWindow(hwnd, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                    }
                    if (!shown) { ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */); shown = true; }
                    SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
                    Thread.Sleep(15);
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError("cables: " + e.Message);
            }
        }

        // A 2.5 px steel-grey line with a dark edge, premultiplied BGRA, clipped to the bitmap.
        private static unsafe void Line(uint* px, int w, int h, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f || len > 20000f) return;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, x1) - 3)), maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(x0, x1) + 3));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, y1) - 3)), maxY = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(y0, y1) + 3));
            if (minX > maxX || minY > maxY) return;
            float nx = -dy / len, ny = dx / len;
            // Walk along the line in 1 px steps and stamp a small disc: cheap and fine for a few cables.
            int steps = (int)len + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps, cx = x0 + dx * t, cy = y0 + dy * t;
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        int x = (int)cx + ox, y = (int)cy + oy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        float d = MathF.Abs((x + 0.5f - cx) * nx + (y + 0.5f - cy) * ny);
                        uint c = d < 1.0f ? 0xFF8A9096u : d < 1.9f ? 0xCC1E2024u : 0u;  // core grey, dark edge
                        if (c == 0) continue;
                        uint old = px[y * w + x];
                        if ((c >> 24) >= (old >> 24)) px[y * w + x] = c;
                    }
            }
        }
    }
}
