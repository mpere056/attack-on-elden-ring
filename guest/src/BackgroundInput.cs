// Play from Elden Ring's window (feasibility test, option B). While linked and Elden Ring's window
// has the focus, Elden Ring gets the real keyboard and mouse (so it animates the Tarnished itself)
// and AoTTG2, in the background, reads the same input through this file:
//   - keybinds: Settings.InputKey.GetKey/GetKeyDown/GetKeyUp answer from the global key state
//     (GetAsyncKeyState works for any window), edges computed once per frame;
//   - mouse look: UnityEngine.Input.GetAxis/GetAxisRaw("Mouse X"/"Mouse Y"/"Mouse ScrollWheel")
//     answer from raw mouse input (WM_INPUT with RIDEV_INPUTSINK reaches us in the background);
//   - Input.GetMouseButton* and Application.isFocused answer as if AoTTG2 were focused.
// AoTTG2 still runs all the movement and ODM physics, exactly as before.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Aoer
{
    internal static class BackgroundInput
    {
        /// <summary>Linked (full mode) and Elden Ring's window is in front.</summary>
        public static bool Active { get; private set; }
        private static bool _wasActive;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        // ---------------- Elden Ring's jump, pressed for the player ----------------
        // In the air (ODM, falls, jumps) the Tarnished would just stand; Elden Ring's own jump animation
        // looks right there (user test: pressing F mid-swing). So when the hero leaves the ground, and
        // again whenever Elden Ring is back to a ground animation while the hero is still airborne, we
        // tap Elden Ring's jump key into its window. Only while Elden Ring has the focus.
        public const int JumpVk = 0x46;          // F, Elden Ring's default jump on keyboard
        private const ushort JumpScan = 0x21;    // scan code of F (Elden Ring reads scan codes)
        private static float _jumpUntil = -1f, _nextJump = -1f;
        private static bool _jumpDown;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
        [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

        private static void SendJump(bool down)
        {
            var i = new INPUT[1];
            i[0].type = 1;  // INPUT_KEYBOARD
            i[0].ki.wScan = JumpScan;
            i[0].ki.dwFlags = 0x8 | (down ? 0u : 0x2u);  // KEYEVENTF_SCANCODE (| KEYUP)
            SendInput(1, i, Marshal.SizeOf<INPUT>());
        }

        /// <summary>Game thread, every frame while linked. airborne: the hero is in the air.</summary>
        public static void AirJump(bool airborne, int erAnim)
        {
            float now = Time.unscaledTime;
            if (_jumpDown && now >= _jumpUntil)
            {
                SendJump(false);
                _jumpDown = false;
            }
            if (!Active || !airborne || _jumpDown || now < _nextJump) return;
            bool inJump = erAnim >= 202000 && erAnim < 203000;  // jump take-off / air / landing ids
            if (inJump) return;
            SendJump(true);
            _jumpDown = true;
            _jumpUntil = now + 0.06f;
            _nextJump = now + 1.0f;
            JumpsPressed++;
        }
        public static int JumpsPressed;

        // Our own F tap must not reach AoTTG2 as if the player pressed F.
        private static bool Masked(int vk) => vk == JumpVk && Time.unscaledTime < _jumpUntil + 0.1f;

        /// <summary>Game thread, every frame.</summary>
        public static void Update(bool linked)
        {
            bool erFront = false;
            if (linked)
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                erFront = pid != 0 && pid == Bridge.HostPid;
            }
            Active = linked && erFront;
            if (Active != _wasActive)
            {
                Plugin.L.LogMessage(Active ? "input: Elden Ring has the focus; AoTTG2 now follows the global keyboard and raw mouse"
                                           : "input: AoTTG2 reads its own window's input again");
                _wasActive = Active;
                RawMouse.Take(out _, out _, out _);  // drop movement collected while inactive
            }
            if (Active) RawMouse.Start();
        }

        // ---------------- keys ----------------
        private static readonly bool[] _cur = new bool[256], _prev = new bool[256];
        private static int _frame = -1;

        private static void Refresh()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            for (int vk = 1; vk < 256; vk++)
            {
                _prev[vk] = _cur[vk];
                _cur[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0 && !Masked(vk);
            }
        }

        private static bool Held(KeyCode k) { int vk = Vk(k); if (vk == 0) return false; Refresh(); return _cur[vk]; }
        private static bool Down(KeyCode k) { int vk = Vk(k); if (vk == 0) return false; Refresh(); return _cur[vk] && !_prev[vk]; }
        private static bool Up(KeyCode k) { int vk = Vk(k); if (vk == 0) return false; Refresh(); return !_cur[vk] && _prev[vk]; }

        // Unity KeyCode -> Windows virtual key. 0 = not mapped (falls back to Unity).
        private static int Vk(KeyCode k)
        {
            int c = (int)k;
            if (c >= 97 && c <= 122) return 0x41 + (c - 97);    // A..Z
            if (c >= 48 && c <= 57) return 0x30 + (c - 48);     // Alpha0..9
            if (c >= 256 && c <= 265) return 0x60 + (c - 256);  // Keypad0..9
            if (c >= 282 && c <= 293) return 0x70 + (c - 282);  // F1..F12
            switch (c)
            {
                case 8: return 0x08; case 9: return 0x09; case 13: return 0x0D; case 27: return 0x1B; case 32: return 0x20;
                case 39: return 0xDE; case 44: return 0xBC; case 45: return 0xBD; case 46: return 0xBE; case 47: return 0xBF;
                case 59: return 0xBA; case 61: return 0xBB; case 91: return 0xDB; case 92: return 0xDC; case 93: return 0xDD;
                case 96: return 0xC0; case 127: return 0x2E;
                case 273: return 0x26; case 274: return 0x28; case 275: return 0x27; case 276: return 0x25;   // arrows
                case 277: return 0x2D; case 278: return 0x24; case 279: return 0x23; case 280: return 0x21; case 281: return 0x22;
                case 301: return 0x14;                                                                          // CapsLock
                case 303: return 0xA1; case 304: return 0xA0; case 305: return 0xA3; case 306: return 0xA2;    // shifts, ctrls
                case 307: return 0xA5; case 308: return 0xA4;                                                  // alts
                case 323: return 0x01; case 324: return 0x02; case 325: return 0x04; case 326: return 0x05; case 327: return 0x06; // mouse
            }
            return 0;
        }

        // ---------------- patches ----------------
        public static void Patch(Harmony h)
        {
            int n = 0;
            n += P(h, typeof(Settings.InputKey), "GetKey", nameof(KeyHeld));
            n += P(h, typeof(Settings.InputKey), "GetKeyDown", nameof(KeyDown));
            n += P(h, typeof(Settings.InputKey), "GetKeyUp", nameof(KeyUp));
            n += P(h, typeof(Input), "GetAxis", nameof(Axis), typeof(string));
            n += P(h, typeof(Input), "GetAxisRaw", nameof(Axis), typeof(string));
            n += P(h, typeof(Input), "GetMouseButton", nameof(MouseHeld), typeof(int));
            n += P(h, typeof(Input), "GetMouseButtonDown", nameof(MouseDown), typeof(int));
            n += P(h, typeof(Input), "GetMouseButtonUp", nameof(MouseUp), typeof(int));
            n += P(h, typeof(Application), "get_isFocused", nameof(Focused));
            // The keybind layer above InputKey (in case IL2CPP inlined InputKey into it), and Unity's own
            // key functions below it (in case a script reads keys directly).
            n += P(h, typeof(Settings.KeybindSetting), "GetKey", nameof(BindHeld), typeof(bool));
            n += P(h, typeof(Settings.KeybindSetting), "GetKeyDown", nameof(BindDown), typeof(bool));
            n += P(h, typeof(Settings.KeybindSetting), "GetKeyUp", nameof(BindUp), typeof(bool));
            n += P(h, typeof(Input), "GetKey", nameof(UnityHeld), typeof(KeyCode));
            n += P(h, typeof(Input), "GetKeyDown", nameof(UnityDown), typeof(KeyCode));
            n += P(h, typeof(Input), "GetKeyUp", nameof(UnityUp), typeof(KeyCode));
            Plugin.L.LogInfo($"input: {n} of 15 input methods patched for playing from Elden Ring's window");
        }

        private static int P(Harmony h, Type t, string name, string prefix, params Type[] args)
        {
            try
            {
                var m = args.Length > 0 ? AccessTools.Method(t, name, args) : AccessTools.Method(t, name, Type.EmptyTypes);
                if (m == null) { Plugin.L.LogWarning($"input: {t.Name}.{name} not found"); return 0; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(BackgroundInput), prefix));
                return 1;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"input: cannot patch {t.Name}.{name}: {e.Message}");
                return 0;
            }
        }

        // InputKey: the key (and its modifier, if any) from the global state. Mouse wheel "keys"
        // (special keys) still go to AoTTG2's own handling.
        private static bool Key(Settings.InputKey k, Func<KeyCode, bool> test, ref bool result)
        {
            if (!Active || k == null || k._isSpecial) return true;
            if (Vk(k._key) == 0) return true;
            _cInputKey++;
            result = test(k._key) && (!k._isModifier || Held(k._modifier));
            return false;
        }
        // How often each layer is asked while Elden Ring has the focus (logged every 5 s): shows which
        // layer AoTTG2's movement and hooks really go through.
        private static int _cInputKey, _cBind, _cUnityKey, _cAxis, _cMouse, _cFocus, _cBindTrue;
        public static void LogCounters()
        {
            if (!Active) { _cInputKey = _cBind = _cUnityKey = _cAxis = _cMouse = _cFocus = _cBindTrue = 0; return; }
            Plugin.L.LogInfo($"input: 5 s with Elden Ring in front: InputKey {_cInputKey}, KeybindSetting {_cBind} " +
                             $"({_cBindTrue} answered pressed), Unity Input.GetKey {_cUnityKey}, axes {_cAxis}, mouse buttons {_cMouse}, " +
                             $"isFocused {_cFocus}; raw mouse running {RawMouse.Running}");
            _cInputKey = _cBind = _cUnityKey = _cAxis = _cMouse = _cFocus = _cBindTrue = 0;
        }

        // KeybindSetting: any of its keys, the same way InputKey does it.
        private static bool Bind(Settings.KeybindSetting b, Func<KeyCode, bool> test, ref bool result)
        {
            if (!Active || b == null) return true;
            _cBind++;
            var keys = b.InputKeys;
            if (keys == null) return true;
            bool any = false, handled = false;
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                if (k == null || k._isSpecial || Vk(k._key) == 0) continue;
                handled = true;
                if (test(k._key) && (!k._isModifier || Held(k._modifier))) { any = true; break; }
            }
            if (!handled) return true;  // only special/unmapped keys: AoTTG2's own code
            result = any;
            if (any) _cBindTrue++;
            return false;
        }
        private static bool BindHeld(Settings.KeybindSetting __instance, ref bool __result) => Bind(__instance, Held, ref __result);
        private static bool BindDown(Settings.KeybindSetting __instance, ref bool __result) => Bind(__instance, Down, ref __result);
        private static bool BindUp(Settings.KeybindSetting __instance, ref bool __result) => Bind(__instance, Up, ref __result);

        private static bool Unity(KeyCode key, Func<KeyCode, bool> test, ref bool result)
        {
            if (!Active || Vk(key) == 0) return true;
            _cUnityKey++;
            result = test(key);
            return false;
        }
        private static bool UnityHeld(KeyCode key, ref bool __result) => Unity(key, Held, ref __result);
        private static bool UnityDown(KeyCode key, ref bool __result) => Unity(key, Down, ref __result);
        private static bool UnityUp(KeyCode key, ref bool __result) => Unity(key, Up, ref __result);

        private static bool KeyHeld(Settings.InputKey __instance, ref bool __result) => Key(__instance, Held, ref __result);
        private static bool KeyDown(Settings.InputKey __instance, ref bool __result) => Key(__instance, Down, ref __result);
        private static bool KeyUp(Settings.InputKey __instance, ref bool __result) => Key(__instance, Up, ref __result);

        private static bool MouseHeld(int button, ref bool __result) { if (!Active) return true; _cMouse++; __result = Held(KeyCode.Mouse0 + button); return false; }
        private static bool MouseDown(int button, ref bool __result) { if (!Active) return true; __result = Down(KeyCode.Mouse0 + button); return false; }
        private static bool MouseUp(int button, ref bool __result) { if (!Active) return true; __result = Up(KeyCode.Mouse0 + button); return false; }
        private static bool Focused(ref bool __result) { if (!Active) return true; _cFocus++; __result = true; return false; }

        // Unity's default axes: 0.1 per pixel of mouse movement, 0.1 per wheel notch.
        private static int _axisFrame = -1;
        private static float _mx, _my, _wheel;
        private static bool Axis(string axisName, ref float __result)
        {
            // Once our raw-mouse reader runs it may replace Unity's own raw-input registration (one per
            // process), so from then on mouse movement always comes from it, focused or not.
            if ((!Active && !RawMouse.Running) || axisName == null) return true;
            if (axisName != "Mouse X" && axisName != "Mouse Y" && axisName != "Mouse ScrollWheel") return true;
            if (Active) _cAxis++;
            if (Time.frameCount != _axisFrame)
            {
                _axisFrame = Time.frameCount;
                RawMouse.Take(out int dx, out int dy, out int wheel);
                _mx = dx * 0.1f; _my = -dy * 0.1f; _wheel = wheel / 120f * 0.1f;
            }
            __result = axisName == "Mouse X" ? _mx : axisName == "Mouse Y" ? _my : _wheel;
            return false;
        }
    }

    /// <summary>Raw mouse movement, collected even while another window has the focus.</summary>
    internal static class RawMouse
    {
        private static long _dx, _dy, _wheel;
        private static Thread _thread;
        public static volatile bool Running;

        public static void Take(out int dx, out int dy, out int wheel)
        {
            dx = (int)Interlocked.Exchange(ref _dx, 0);
            dy = (int)Interlocked.Exchange(ref _dy, 0);
            wheel = (int)Interlocked.Exchange(ref _wheel, 0);
        }

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "AoER raw mouse" };
            _thread.Start();
        }

        private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h,
                                                     IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint header);
        [DllImport("user32.dll")] private static extern int GetMessageW(out MSG m, IntPtr h, uint a, uint b);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr name);

        private static WndProc _proc;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private const uint WM_INPUT = 0x00FF, RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x100;

        private static unsafe void Run()
        {
            try
            {
                byte* buf = stackalloc byte[256];
                _proc = (h, m, w, l) =>
                {
                    if (m == WM_INPUT)
                    {
                        uint size = 256;
                        int header = IntPtr.Size == 8 ? 24 : 16;  // RAWINPUTHEADER
                        if (GetRawInputData(l, RID_INPUT, (IntPtr)buf, ref size, (uint)header) != uint.MaxValue &&
                            *(uint*)buf == 0 /* RIM_TYPEMOUSE */)
                        {
                            byte* mouse = buf + header;                    // RAWMOUSE
                            ushort flags = *(ushort*)mouse;
                            ushort buttonFlags = *(ushort*)(mouse + 4);
                            short buttonData = *(short*)(mouse + 6);
                            int lx = *(int*)(mouse + 12), ly = *(int*)(mouse + 16);
                            if ((flags & 1) == 0)                          // relative movement
                            {
                                Interlocked.Add(ref _dx, lx);
                                Interlocked.Add(ref _dy, ly);
                            }
                            if ((buttonFlags & 0x0400) != 0) Interlocked.Add(ref _wheel, buttonData);  // RI_MOUSE_WHEEL
                        }
                    }
                    return DefWindowProcW(h, m, w, l);
                };
                IntPtr inst = GetModuleHandleW(IntPtr.Zero);
                var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = "AoERRawMouse" };
                RegisterClassExW(ref wc);
                IntPtr hwnd = CreateWindowExW(0, "AoERRawMouse", "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, inst, IntPtr.Zero);
                var dev = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = hwnd } };
                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                {
                    Plugin.L.LogWarning("input: raw mouse registration failed; mouse look won't follow Elden Ring's window");
                    return;
                }
                Running = true;
                Plugin.L.LogMessage("input: raw mouse ready");
                while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
            }
            catch (Exception e)
            {
                Plugin.L.LogError("input: raw mouse: " + e.Message);
            }
        }
    }
}
