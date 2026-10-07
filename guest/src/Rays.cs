// Asks Elden Ring's own collision (CSPhysWorld::CastRay, terrain filter: map and props, no
// characters) about rays, through the ray block of the shared memory. One batch in flight at a
// time; Elden Ring answers within a frame or two (measured in milestone 1).
// CastRay gives hit positions but no surface normals, and can miss when a ray starts inside
// solid geometry.
using System.Threading;

namespace Aoer
{
    internal struct Ray3
    {
        public float SX, SY, SZ, EX, EY, EZ;
        public Ray3(float sx, float sy, float sz, float ex, float ey, float ez)
        {
            SX = sx; SY = sy; SZ = sz; EX = ex; EY = ey; EZ = ez;
        }
    }

    internal struct RayHit
    {
        public bool Hit;
        public float X, Y, Z;
    }

    internal static unsafe class Rays
    {
        public const int MaxRays = 8192;
        private const int OffRays = 0x100000;
        private const int OffRayArr = OffRays + 0x20;
        private const int OffHitArr = OffRayArr + MaxRays * 24;

        private static uint _pendingSeq;
        private static int _pendingCount;
        public static bool Busy { get; private set; }

        /// <summary>Starts a batch. Returns false if one is still in flight or the host isn't idle.</summary>
        public static bool Submit(Ray3[] rays, int count)
        {
            if (Busy || !Bridge.IsOpen || count <= 0 || count > MaxRays) return false;
            byte* b = Bridge.Base;
            uint req = Volatile.Read(ref *(uint*)(b + OffRays));
            uint resp = Volatile.Read(ref *(uint*)(b + OffRays + 4));
            if (req != resp) return false;  // somebody else's batch (tools/erctl.py) is running
            float* dst = (float*)(b + OffRayArr);
            for (int i = 0; i < count; i++)
            {
                ref Ray3 r = ref rays[i];
                dst[0] = r.SX; dst[1] = r.SY; dst[2] = r.SZ; dst[3] = r.EX; dst[4] = r.EY; dst[5] = r.EZ;
                dst += 6;
            }
            *(uint*)(b + OffRays + 8) = (uint)count;  // count
            *(uint*)(b + OffRays + 12) = 0;           // flags: terrain filter
            *(uint*)(b + OffRays + 16) = 0;           // processed
            Interlocked.MemoryBarrier();
            _pendingSeq = req + 1;
            _pendingCount = count;
            Volatile.Write(ref *(uint*)(b + OffRays), _pendingSeq);  // publish last
            Busy = true;
            return true;
        }

        /// <summary>Copies the answers once the batch is done. Returns false while still pending.</summary>
        public static bool TryCollect(RayHit[] hits)
        {
            if (!Busy) return false;
            byte* b = Bridge.Base;
            if (Volatile.Read(ref *(uint*)(b + OffRays + 4)) != _pendingSeq) return false;
            byte* src = b + OffHitArr;
            for (int i = 0; i < _pendingCount; i++, src += 32)
            {
                uint hit = *(uint*)(src + 24);
                hits[i].Hit = hit != 0;
                hits[i].X = *(float*)src;
                hits[i].Y = *(float*)(src + 4);
                hits[i].Z = *(float*)(src + 8);
            }
            Busy = false;
            return true;
        }

        /// <summary>Forget a batch (for example when the link drops). The host still finishes it.</summary>
        public static void Abandon() => Busy = false;
    }
}
