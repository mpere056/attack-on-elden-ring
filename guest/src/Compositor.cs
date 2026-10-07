// Milestone 6: AoTTG2's soldier, ODM cables, effects and HUD drawn into Elden Ring's picture.
//
// While compositing, AoTTG2 shows nothing of its own world: the camera clears to a key colour
// (magenta), post-processing and fog are off, and every map/other-character renderer that existed
// when compositing started is hidden (the local hero, its hooks and anything spawned later, such as
// gas and sparks, keep drawing). At the end of each AoTTG2 frame the finished screen, HUD included,
// is read back, the key colour is turned into transparency, and the result goes into the frames
// mapping as the "GUI" layer: the host (compositor.cpp) draws it over Elden Ring's frame as is.
// First version: no depth (the soldier is never hidden behind Elden Ring walls) and no relighting.
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Threading;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Aoer
{
    internal static unsafe class Compositor
    {
        // host/include/bridge_protocol.h, frame passthrough
        private const string FramesName = @"Local\AoER_frames_v1";
        private const uint FramesMagic = 0x524D484D;   // "MHMR"
        private const uint FramesVersion = 3;
        private const int MaxW = 2560, MaxH = 1440, Slots = 3, SlotHeader = 0x100;
        private const long SlotSize = SlotHeader + (long)MaxW * MaxH * 16;
        private const long FramesSize = 0x1000 + Slots * SlotSize;
        private const uint LayerGui = 1u << 1;

        private static readonly Color Key = new Color(1f, 0f, 1f, 1f);

        private static MemoryMappedFile _file;
        private static MemoryMappedViewAccessor _view;
        private static byte* _frames;
        private static Texture2D _tex;
        private static RenderTexture _rt;
        private static bool _requested;
        private static ulong _requestedPose;
        private static ulong _frameId;
        private static int _slot;

        // What we changed in AoTTG2, to put back.
        private static readonly List<Renderer> _hidden = new();
        private static readonly List<Behaviour> _disabledBehaviours = new();
        private static Camera _cam;
        private static CameraClearFlags _oldClear;
        private static Color _oldBackground;
        private static bool _oldFog;
        private static int _oldW, _oldH;
        private static bool _oldFullScreen;

        public static bool Active { get; private set; }
        public static int Frames;
        public static double LastCaptureMs, LastKeyMs;

        /// <summary>Starts compositing: hides AoTTG2's world, matches Elden Ring's window size.</summary>
        public static bool Begin(Component hero, int erW, int erH)
        {
            End();
            if (!OpenFrames()) return false;
            _cam = Camera.main;
            if (_cam == null)
            {
                Plugin.L.LogWarning("compositor: AoTTG2 has no main camera; not compositing");
                return false;
            }
            _oldClear = _cam.clearFlags;
            _oldBackground = _cam.backgroundColor;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Key;
            _oldFog = RenderSettings.fog;
            RenderSettings.fog = false;
            // Post-processing (bloom, colour grading, vignette) would tint the key colour.
            foreach (var b in _cam.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                string n = b.GetIl2CppType().Name;
                if (n == "PostProcessLayer" || n.Contains("PostProcess") || n.Contains("Bloom"))
                {
                    b.enabled = false;
                    _disabledBehaviours.Add(b);
                }
            }
            HideWorld(hero);
            // Same size as Elden Ring's window, so AoTTG2's picture lines up with Elden Ring's.
            _oldW = Screen.width; _oldH = Screen.height; _oldFullScreen = Screen.fullScreen;
            if (erW > 0 && erH > 0 && erW <= MaxW && erH <= MaxH && (Screen.width != erW || Screen.height != erH))
                Screen.SetResolution(erW, erH, FullScreenMode.Windowed);
            Active = true;
            Frames = 0;
            _requested = false;
            Overlay.Begin();
            Plugin.L.LogMessage($"compositor: ON. Hid {_hidden.Count} AoTTG2 renderers, {_disabledBehaviours.Count} post effects; " +
                                $"AoTTG2 {_oldW}x{_oldH} -> {erW}x{erH} to match Elden Ring");
            return true;
        }

        private static void HideWorld(Component hero)
        {
            var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Renderer>());
            foreach (var o in all)
            {
                var r = o.TryCast<Renderer>();
                if (r == null || !r.enabled) continue;
                if (hero != null && r.transform.IsChildOf(hero.transform)) continue;
                if (r.GetComponentInParent<Characters.Hook>() != null) continue;
                if (r.GetComponentInParent<Characters.HookUseable>() != null) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
        }

        private static bool OpenFrames()
        {
            if (_frames != null) return true;
            try
            {
                _file = MemoryMappedFile.CreateOrOpen(FramesName, FramesSize, MemoryMappedFileAccess.ReadWrite);
                _view = _file.CreateViewAccessor(0, FramesSize, MemoryMappedFileAccess.ReadWrite);
                byte* p = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
                _frames = p + _view.PointerOffset;
                if (*(uint*)_frames != FramesMagic || *(uint*)(_frames + 4) != FramesVersion)
                {
                    *(uint*)(_frames + 4) = FramesVersion;
                    Interlocked.MemoryBarrier();
                    *(uint*)_frames = FramesMagic;
                }
                Plugin.L.LogMessage($"compositor: frames mapping {FramesName} open ({FramesSize >> 20} MB)");
                return true;
            }
            catch (Exception e)
            {
                Plugin.L.LogError("compositor: cannot open the frames mapping: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Called once per AoTTG2 frame (Update). Publishes the screen captured at the end of the
        /// previous frame, then asks Unity to capture this frame's finished screen (HUD included) into
        /// a render texture. No coroutine needed: the plugin system can't run a WaitForEndOfFrame
        /// coroutine here (first compositing test: "unsupported return type", nothing captured).
        /// poseId: the control frame whose camera pose this frame is rendered with.
        /// </summary>
        public static void Tick(ulong poseId)
        {
            if (!Active || _frames == null) return;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0 || w > MaxW || h > MaxH) return;
            if (_requested && _rt != null && _rt.width == w && _rt.height == h) Publish(_requestedPose, w, h);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);  // filled at the end of this frame
            _requested = true;
            _requestedPose = poseId;
        }

        private static void Publish(ulong poseId, int w, int h)
        {
            var t0 = DateTime.UtcNow;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            // A copy of the pixels as Color32 (r, g, b, a bytes), through Unity's ordinary API. (The
            // "_Injected" native call wants Unity's own object handle, not the interop pointer:
            // passing _tex.Pointer crashed AoTTG2 in the first compositing test.)
            var raw = _tex.GetPixels32();
            var t1 = DateTime.UtcNow;
            if (raw == null || raw.Length < w * h) return;

            byte* slot = _frames + 0x1000 + _slot * SlotSize;
            ref uint seq = ref *(uint*)slot;
            uint s = Volatile.Read(ref seq);
            if ((s & 1) != 0) s++;
            Volatile.Write(ref seq, s + 1);                        // odd: writing
            Interlocked.MemoryBarrier();
            long layer = (long)w * h * 4;
            byte* world = slot + SlotHeader;
            if (*(uint*)(slot + 4) != (uint)w || *(uint*)(slot + 8) != (uint)h)
                new Span<byte>(world, (int)(layer * 2)).Clear(); // no world colour or depth yet
            byte* gui = world + layer * 2;
            KeyOut((byte*)raw.Pointer + 4 * IntPtr.Size, gui, w, h, FlipRows);  // il2cpp array data after a 32-byte header
            *(uint*)(slot + 4) = (uint)w;
            *(uint*)(slot + 8) = (uint)h;
            *(uint*)(slot + 0x0C) = LayerGui;
            *(ulong*)(slot + 0x10) = ++_frameId;
            *(ulong*)(slot + 0x18) = poseId;
            *(float*)(slot + 0x20) = _cam != null ? _cam.nearClipPlane : 0.1f;
            *(float*)(slot + 0x24) = _cam != null ? _cam.farClipPlane : 1000f;
            *(float*)(slot + 0x28) = _cam != null ? _cam.fieldOfView : 60f;
            *(float*)(slot + 0x2C) = (float)w / h;
            Interlocked.MemoryBarrier();
            Volatile.Write(ref seq, s + 2);                        // even: done
            Volatile.Write(ref *(uint*)(_frames + 8), (uint)_slot);         // latestSlot
            Volatile.Write(ref *(ulong*)(_frames + 0x10), _frameId);        // latestFrameId
            _slot = (_slot + 1) % Slots;
            if (Frames++ == 0) Plugin.L.LogMessage($"compositor: first frame published ({w}x{h}, rows {(FlipRows ? "flipped" : "as read")})");
            var t2 = DateTime.UtcNow;
            LastCaptureMs = (t1 - t0).TotalMilliseconds;
            LastKeyMs = (t2 - t1).TotalMilliseconds;
        }

        // CaptureScreenshotIntoRenderTexture fills the texture upside down on Direct3D (Unity docs),
        // while the host wants rows bottom-up: so rows are reversed by default. Verified by screenshot.
        public static bool FlipRows = true;

        // RGBA (Unity) over a magenta background -> premultiplied BGRA (host GUI layer).
        // With background M = (1, 0, 1), a pixel p = a*C + (1-a)*M has p.r - p.g = (1-a) + a(C.r - C.g)
        // and p.b - p.g likewise, so for anything not itself magenta-ish 1 - min(r-g, b-g) is a.
        private static void KeyOut(byte* src, byte* dstBase, int w, int h, bool flip)
        {
            for (int y = 0; y < h; y++)
            {
                byte* dst = dstBase + (long)(flip ? h - 1 - y : y) * w * 4;
                for (int x = 0; x < w; x++, src += 4, dst += 4)
                {
                    int r = src[0], g = src[1], b = src[2];
                    int key = Math.Min(r - g, b - g);
                    if (key < 0) key = 0;
                    int a = 255 - key;
                    if (a <= 6)
                    {
                        *(uint*)dst = 0;
                        continue;
                    }
                    int k = 255 - a;                               // background share, 0..255
                    dst[0] = (byte)Math.Max(0, b - k);             // B, magenta removed
                    dst[1] = (byte)g;                              // G
                    dst[2] = (byte)Math.Max(0, r - k);             // R
                    dst[3] = (byte)a;
                }
            }
        }

        public static void End()
        {
            if (!Active && _hidden.Count == 0) return;
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            foreach (var b in _disabledBehaviours) if (b != null) b.enabled = true;
            _disabledBehaviours.Clear();
            if (_cam != null)
            {
                _cam.clearFlags = _oldClear;
                _cam.backgroundColor = _oldBackground;
            }
            RenderSettings.fog = _oldFog;
            if (_oldW > 0 && (Screen.width != _oldW || Screen.height != _oldH))
                Screen.SetResolution(_oldW, _oldH, _oldFullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            _oldW = _oldH = 0;
            Overlay.End();
            _requested = false;
            if (Active) Plugin.L.LogMessage($"compositor: OFF after {Frames} frames; AoTTG2's world restored");
            Active = false;
        }
    }
}
