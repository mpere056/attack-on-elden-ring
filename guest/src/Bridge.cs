// The guest's end of the shared memory: opens (or creates) the named mapping and reads the
// game state Elden Ring publishes. Either game may start first.
using System;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace Aoer
{
    internal struct HostState
    {
        public uint Flags;
        public ulong Frame;
        public float PX, PY, PZ;     // Tarnished position, Elden Ring stable frame, metres
        public float CX, CY, CZ;     // camera eye
        public float TX, TY, TZ;     // camera look-at point
        public uint Stage;           // zone id
        public int WinX, WinY, WinW, WinH; // Elden Ring's client area on screen
        public uint BackW, BackH;    // Elden Ring's swapchain size
        public int AnimId;           // animation the Tarnished is playing, -1 unknown
        public bool PlayerValid => (Flags & Protocol.StatePlayerValid) != 0;
        public bool Busy => (Flags & Protocol.StateHostBusy) != 0;
        public bool Dead => (Flags & Protocol.StatePlayerDead) != 0;
    }

    internal static unsafe class Bridge
    {
        private static MemoryMappedFile _file;
        private static MemoryMappedViewAccessor _view;
        private static byte* _base;
        private static ulong _lastHostBeat;
        private static DateTime _lastHostBeatChange = DateTime.MinValue;

        public static bool IsOpen => _base != null;
        public static byte* Base => _base;

        public static bool Open()
        {
            if (_base != null) return true;
            _file = MemoryMappedFile.CreateOrOpen(Protocol.ShmName, Protocol.ShmSize, MemoryMappedFileAccess.ReadWrite);
            _view = _file.CreateViewAccessor(0, Protocol.ShmSize, MemoryMappedFileAccess.ReadWrite);
            byte* p = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
            _base = p + _view.PointerOffset;
            // Announce ourselves. Never initialise the host's blocks: the host does that when it
            // finds no valid magic, and it may start after us.
            U32(Protocol.HdrGuestPid) = (uint)Environment.ProcessId;
            U64(Protocol.HdrGuestStartMs) = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return true;
        }

        private static ref uint U32(int off) => ref *(uint*)(_base + off);
        private static ref ulong U64(int off) => ref *(ulong*)(_base + off);
        private static float F(byte* p, int off) => *(float*)(p + off);

        /// <summary>True when Elden Ring's side is present and its heartbeat moved in the last 2 s.</summary>
        public static bool HostAlive()
        {
            if (_base == null || Volatile.Read(ref U32(Protocol.HdrMagic)) != Protocol.Magic) return false;
            ulong beat = Volatile.Read(ref U64(Protocol.HdrHostHeartbeat));
            if (beat != _lastHostBeat)
            {
                _lastHostBeat = beat;
                _lastHostBeatChange = DateTime.UtcNow;
            }
            return (DateTime.UtcNow - _lastHostBeatChange).TotalSeconds < 2.0;
        }

        public static uint HostPid => _base == null ? 0 : Volatile.Read(ref U32(Protocol.HdrHostPid));

        /// <summary>Called once per AoTTG2 frame so Elden Ring can tell we are alive.</summary>
        public static void Beat()
        {
            if (_base == null) return;
            Interlocked.Increment(ref *(long*)(_base + Protocol.HdrGuestHeartbeat));
        }

        public static uint HostLife => _base == null ? 0 : Volatile.Read(ref U32(Protocol.HdrHostLife));

        private static ulong _controlFrame;
        public static ulong LastControlFrame => _controlFrame;

        /// <summary>
        /// Publishes where the Tarnished should stand (Elden Ring stable frame, metres) and which way
        /// it faces. flags = 0 hands the Tarnished back to Elden Ring.
        /// </summary>
        public static void WriteControl(uint flags, float x, float y, float z, float unityYawDeg)
            => WriteControl(flags, x, y, z, unityYawDeg, default, default, default, 0f, 0f, 0f, 0);

        /// <summary>
        /// Same, plus a camera for Elden Ring to render from when flags has CtrlOverrideCamera:
        /// eye, look-at point and up vector in Elden Ring's stable frame, vertical FOV in degrees.
        /// </summary>
        public static void WriteControl(uint flags, float x, float y, float z, float unityYawDeg,
                                        UnityEngine.Vector3 camPos, UnityEngine.Vector3 camTarget, UnityEngine.Vector3 camUp, float fovDeg,
                                        float stickX = 0f, float stickY = 0f, uint padButtons = 0, float aimDist = 0f, int requestAnim = -1, float animSpeed = 1f)
        {
            if (_base == null) return;
            byte* c = _base + Protocol.OffControl;
            ref uint seq = ref *(uint*)(c + Protocol.CtSeq);
            uint s = Volatile.Read(ref seq);
            if ((s & 1) != 0) s++;                      // never leave it odd
            Volatile.Write(ref seq, s + 1);             // odd: writing
            Interlocked.MemoryBarrier();
            *(uint*)(c + Protocol.CtFlags) = flags;
            *(ulong*)(c + Protocol.CtFrame) = ++_controlFrame;
            *(float*)(c + Protocol.CtHunterPos) = x;
            *(float*)(c + Protocol.CtHunterPos + 4) = y;
            *(float*)(c + Protocol.CtHunterPos + 8) = z;
            // The host reads "Minecraft yaw": 0 faces -Z in Elden Ring after its Z flip. Unity yaw 0
            // faces +Z, so add 180 degrees. (Assumes Elden Ring and Unity share axes; see CONTRACT.)
            *(float*)(c + Protocol.CtHunterYawDeg) = unityYawDeg + 180f;
            float* cp = (float*)(c + Protocol.CtCamPos);
            cp[0] = camPos.x; cp[1] = camPos.y; cp[2] = camPos.z;
            float* ct = (float*)(c + Protocol.CtCamTarget);
            ct[0] = camTarget.x; ct[1] = camTarget.y; ct[2] = camTarget.z;
            float* cu = (float*)(c + Protocol.CtCamUp);
            cu[0] = camUp.x; cu[1] = camUp.y; cu[2] = camUp.z;
            *(float*)(c + Protocol.CtFov) = fovDeg;
            *(float*)(c + Protocol.CtStickX) = stickX;
            *(float*)(c + Protocol.CtStickY) = stickY;
            *(uint*)(c + Protocol.CtPadButtons) = padButtons;
            *(float*)(c + Protocol.CtAimDist) = aimDist;
            *(int*)(c + Protocol.CtRequestAnim) = requestAnim;
            *(float*)(c + Protocol.CtAnimSpeed) = animSpeed;
            Volatile.Write(ref seq, s + 2);             // even: done
        }

        /// <summary>Consistent copy of Elden Ring's state block (seqlock: retry while odd or changed).</summary>
        public static bool ReadState(out HostState s)
        {
            s = default;
            if (_base == null) return false;
            byte* st = _base + Protocol.OffState;
            byte* copy = stackalloc byte[Protocol.StateSize];
            for (int tries = 0; tries < 64; tries++)
            {
                uint a = Volatile.Read(ref *(uint*)(st + Protocol.StSeq));
                if ((a & 1) != 0) { Thread.SpinWait(20); continue; }
                Buffer.MemoryCopy(st, copy, Protocol.StateSize, Protocol.StateSize);
                Interlocked.MemoryBarrier();
                uint b = Volatile.Read(ref *(uint*)(st + Protocol.StSeq));
                if (a != b) continue;
                s.Flags = *(uint*)(copy + Protocol.StFlags);
                s.Frame = *(ulong*)(copy + Protocol.StFrame);
                s.PX = F(copy, Protocol.StPlayerPos); s.PY = F(copy, Protocol.StPlayerPos + 4); s.PZ = F(copy, Protocol.StPlayerPos + 8);
                s.CX = F(copy, Protocol.StCamPos); s.CY = F(copy, Protocol.StCamPos + 4); s.CZ = F(copy, Protocol.StCamPos + 8);
                s.TX = F(copy, Protocol.StCamTarget); s.TY = F(copy, Protocol.StCamTarget + 4); s.TZ = F(copy, Protocol.StCamTarget + 8);
                s.Stage = *(uint*)(copy + Protocol.StStageId);
                int* win = (int*)(copy + Protocol.StWin);
                s.WinX = win[0]; s.WinY = win[1]; s.WinW = win[2]; s.WinH = win[3];
                s.BackW = *(uint*)(copy + Protocol.StBackBuffer); s.BackH = *(uint*)(copy + Protocol.StBackBuffer + 4);
                s.AnimId = *(int*)(copy + Protocol.StAnimId);
                return a != 0;
            }
            return false;
        }
    }
}
