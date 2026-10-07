// Milestone 5: long-range hooks. The collision copy only covers ~50 m around the player (walls 20 m,
// 60 m above), so a hook fired at a distant cliff or tower would fly through empty space in AoTTG2.
// When AoTTG2 launches a hook (Characters.Hook.SetHooking, patched below), we ask Elden Ring for the
// first surface along the hook's path (single-ray debug channel, terrain filter, up to Range m) and
// put a small invisible anchor there on the copy's layer. AoTTG2's own hook then flies into it and
// catches it normally: no change to how AoTTG2 hooks work.
using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Aoer
{
    internal static unsafe class LongHooks
    {
        private const float Range = 150f;
        private const float AnchorLife = 8f;            // seconds an anchor stays for its hook
        private const int OffCmd = 0x1000, OffCmdResp = 0x2000;
        private const uint CmdRaycast = 9;
        private const uint RaysCustomFilter = 1u << 1;
        private const uint TerrainFilter = 0x5D;        // host kTerrainRayFilter: map and props, no characters

        private struct Request { public Vector3 Start, End; }
        private static readonly Queue<Request> _queue = new();
        private static bool _inFlight;
        private static uint _seq;
        private static float _sentAt;
        private static Request _current;
        private static readonly List<(GameObject go, float until)> _anchors = new();
        public static int Fired, Anchored, Missed;
        public static float LastDistance;

        public static void Patch(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(Characters.Hook), "SetHooking", new[] { typeof(Vector3), typeof(Vector3) });
            if (target == null)
            {
                Plugin.L.LogWarning("long hooks: Hook.SetHooking not found; long-range hooks disabled");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(LongHooks), nameof(OnSetHooking)));
            Plugin.L.LogInfo("long hooks: watching Hook.SetHooking");
        }

        // Runs whenever a hook is launched. Only acts while linked with the collision copy on.
        private static void OnSetHooking(Characters.Hook __instance, Vector3 baseVelocity, Vector3 relativeVelocity)
        {
            try
            {
                if (!Link.CollisionActive || __instance == null) return;
                Vector3 v = baseVelocity + relativeVelocity;
                if (v.sqrMagnitude < 1e-4f) return;
                Vector3 start = __instance.transform.position - TerrainMirror.Offset;  // Elden Ring frame
                Vector3 dir = v.normalized;
                _queue.Enqueue(new Request { Start = start, End = start + dir * Range });
                Fired++;
            }
            catch (Exception e)
            {
                Plugin.L.LogDebug("long hooks: " + e.Message);
            }
        }

        /// <summary>Every frame while linked: send queued rays, place anchors, expire old ones.</summary>
        public static void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _anchors.Count - 1; i >= 0; i--)
                if (now > _anchors[i].until || _anchors[i].go == null)
                {
                    if (_anchors[i].go != null) UnityEngine.Object.Destroy(_anchors[i].go);
                    _anchors.RemoveAt(i);
                }
            if (!Bridge.IsOpen) return;
            byte* c = Bridge.Base + OffCmd;
            if (_inFlight)
            {
                if (Volatile.Read(ref *(uint*)(c + 4)) != _seq)
                {
                    if (now - _sentAt > 1.5f) { _inFlight = false; Missed++; }  // no answer (loading?)
                    return;
                }
                _inFlight = false;
                int status = *(int*)(c + 0x10);
                byte* r = Bridge.Base + OffCmdResp;
                if (status == 0 && *(uint*)(r + 24) != 0)
                {
                    var hit = new Vector3(*(float*)r, *(float*)(r + 4), *(float*)(r + 8));
                    LastDistance = Vector3.Distance(_current.Start, hit);
                    PlaceAnchor(hit, (_current.End - _current.Start).normalized);
                    Anchored++;
                }
                else Missed++;
            }
            if (_queue.Count == 0) return;
            if (Volatile.Read(ref *(uint*)c) != Volatile.Read(ref *(uint*)(c + 4))) return;  // mailbox busy
            _current = _queue.Dequeue();
            float* a = (float*)(c + 0x18);
            a[0] = _current.Start.x; a[1] = _current.Start.y; a[2] = _current.Start.z;
            a[3] = _current.End.x; a[4] = _current.End.y; a[5] = _current.End.z;
            uint* u = (uint*)(c + 0x18);
            u[6] = RaysCustomFilter; u[7] = TerrainFilter; u[8] = 0; u[9] = 0;
            *(uint*)(c + 0x08) = CmdRaycast;
            *(uint*)(c + 0x0C) = 40;
            Interlocked.MemoryBarrier();
            _seq = Volatile.Read(ref *(uint*)c) + 1;
            Volatile.Write(ref *(uint*)c, _seq);
            _inFlight = true;
            _sentAt = now;
        }

        // A 2 x 2 m plate, 0.5 m thick, set into the surface and facing the incoming hook.
        private static void PlaceAnchor(Vector3 erHit, Vector3 dir)
        {
            Transform root = TerrainMirror.Root;
            if (root == null) return;
            var go = new GameObject("AoER hook anchor");
            go.transform.SetParent(root, false);
            go.layer = TerrainMirror.Layer;
            go.transform.localPosition = erHit + dir * 0.25f;
            go.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
            go.transform.localScale = new Vector3(2f, 2f, 0.5f);
            go.AddComponent<BoxCollider>();
            _anchors.Add((go, Time.unscaledTime + AnchorLife));
        }

        public static void End()
        {
            foreach (var (go, _) in _anchors) if (go != null) UnityEngine.Object.Destroy(go);
            _anchors.Clear();
            _queue.Clear();
            _inFlight = false;
        }
    }
}
