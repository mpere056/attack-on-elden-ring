// Invisible stand-ins for Elden Ring's enemies inside AoTTG2, so ODM hooks can catch them (and,
// in milestone 7, blades can hit them). Elden Ring publishes nearby characters every tick in the
// entity table (host game.cpp publish_entities): position, hitbox centre and half size, health.
// Each becomes a box collider on the same layer as the terrain copy (a layer AoTTG2's hooks
// catch), moved every frame to follow its enemy.
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Aoer
{
    internal static unsafe class EnemyProxies
    {
        private const int OffEntities = 0x200000;
        private const int EntitySize = 0x80;
        private const int MaxEntities = 256;

        private sealed class Proxy
        {
            public GameObject Go;
            public BoxCollider Box;
            public bool Seen;
        }

        private static readonly Dictionary<ulong, Proxy> _proxies = new();
        private static readonly byte[] _copy = new byte[0x10 + MaxEntities * EntitySize];
        private static GameObject _root;
        private static ulong _lastFrame;
        public static int Count => _proxies.Count;

        public static void Begin()
        {
            End();
            _root = new GameObject("AoER enemies");
            UnityEngine.Object.DontDestroyOnLoad(_root);
        }

        /// <summary>offset: unity = er + offset. layer: the hookable layer the terrain copy uses.</summary>
        public static void Update(Vector3 offset, int layer)
        {
            if (_root == null || !Bridge.IsOpen || !ReadTable(out int count, out ulong frame)) return;
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            foreach (var p in _proxies.Values) p.Seen = false;
            fixed (byte* t = _copy)
            {
                for (int i = 0; i < count; i++)
                {
                    byte* e = t + 0x10 + i * EntitySize;
                    ulong id = *(ulong*)e;
                    uint flags = *(uint*)(e + 0x4C);
                    if (id == 0 || (flags & 1) != 0) continue;  // dead or being removed
                    float* c = (float*)(e + 0x2C), h = (float*)(e + 0x38), q = (float*)(e + 0x1C);
                    if (!_proxies.TryGetValue(id, out var p))
                    {
                        var go = new GameObject("AoER enemy " + Name(e));
                        go.transform.SetParent(_root.transform, false);
                        go.layer = layer;
                        p = new Proxy { Go = go, Box = go.AddComponent<BoxCollider>() };
                        _proxies[id] = p;
                    }
                    p.Seen = true;
                    p.Box.size = new Vector3(h[0] * 2f, h[1] * 2f, h[2] * 2f);
                    p.Go.transform.position = new Vector3(c[0], c[1], c[2]) + offset;
                    p.Go.transform.rotation = (flags & 2) != 0 ? Quaternion.identity : new Quaternion(q[0], q[1], q[2], q[3]);
                }
            }
            List<ulong> gone = null;
            foreach (var kv in _proxies)
                if (!kv.Value.Seen) (gone ??= new List<ulong>()).Add(kv.Key);
            if (gone == null) return;
            foreach (ulong id in gone)
            {
                UnityEngine.Object.Destroy(_proxies[id].Go);
                _proxies.Remove(id);
            }
        }

        private static string Name(byte* e)
        {
            int n = 0;
            while (n < 48 && e[0x50 + n] != 0) n++;
            return new string((sbyte*)(e + 0x50), 0, n);
        }

        private static bool ReadTable(out int count, out ulong frame)
        {
            count = 0; frame = 0;
            byte* b = Bridge.Base + OffEntities;
            for (int tries = 0; tries < 16; tries++)
            {
                uint a = Volatile.Read(ref *(uint*)b);
                if ((a & 1) != 0) { Thread.SpinWait(20); continue; }
                int n = (int)Math.Min(*(uint*)(b + 4), MaxEntities);
                fixed (byte* dst = _copy) Buffer.MemoryCopy(b, dst, _copy.Length, 0x10 + n * EntitySize);
                Interlocked.MemoryBarrier();
                if (Volatile.Read(ref *(uint*)b) != a) continue;
                count = n;
                frame = *(ulong*)(b + 8);
                return a != 0;
            }
            return false;
        }

        public static void End()
        {
            foreach (var p in _proxies.Values) if (p.Go != null) UnityEngine.Object.Destroy(p.Go);
            _proxies.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _lastFrame = 0;
        }
    }
}
