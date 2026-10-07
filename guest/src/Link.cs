// The link between AoTTG2's hero and the Tarnished.
//
// F7 (in either game) starts it in two steps:
//   1. Preparing: Elden Ring's collision around the Tarnished is copied into AoTTG2
//      (TerrainMirror) while both characters stay where they are.
//   2. Linked: AoTTG2's own map collision is switched off, the mapping is fixed so the hero's
//      current spot is the Tarnished's spot, and from then on the Tarnished copies the hero every
//      frame (milestone 3) while the hero walks on Elden Ring's ground (milestone 4).
//
// Mapping (docs/CONTRACT.md, confirmed in milestone 3): Elden Ring and Unity are both Y-up,
// left-handed and in metres, so  er = hero - offset,  hero = er + offset.
//
// Safety: the link drops by itself when Elden Ring stops responding, loads, respawns or warps the
// Tarnished (hostLife changes), when AoTTG2's hero disappears, or when F7 is pressed again.
// Dropping it hands the Tarnished straight back to Elden Ring (gravity and death restored) and
// switches AoTTG2's own map collision back on.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Aoer
{
    internal static class Link
    {
        private enum Phase { Off, Preparing, Linked }
        private static Phase _phase = Phase.Off;
        public static bool Active => _phase != Phase.Off;

        private static Vector3 _offset;           // hero = er + offset
        private static uint _lifeAtLink;
        private static bool _puppet;
        private static bool _f7WasDown;
        private static float _nextLog, _prepStart;
        private static float _maxSpeed;
        private static Vector3 _lastHero, _velocity;
        private static float _lastTime;

        private const int VK_F7 = 0x76;
        private const float PrepTimeout = 10f;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>F7 in either game toggles the link.</summary>
        private static bool F7Pressed()
        {
            bool down = (GetAsyncKeyState(VK_F7) & 0x8000) != 0;
            bool pressed = down && !_f7WasDown;
            _f7WasDown = down;
            return pressed;
        }

        public static void Update(bool hostAlive, Component hero)
        {
            bool toggle = F7Pressed();
            HostState s = default;
            bool stateOk = hostAlive && Bridge.ReadState(out s) && s.PlayerValid && !s.Busy && !s.Dead;
            var er = new Vector3(s.PX, s.PY, s.PZ);

            if (_phase == Phase.Off)
            {
                if (!toggle) return;
                if (!hostAlive) { Plugin.L.LogWarning("link: F7 ignored, Elden Ring is not running"); return; }
                if (!stateOk) { Plugin.L.LogWarning("link: F7 ignored, no Tarnished standing in the world"); return; }
                if (hero == null) { Plugin.L.LogWarning("link: F7 ignored, spawn a soldier in AoTTG2 first"); return; }
                _lifeAtLink = Bridge.HostLife;
                _phase = Phase.Preparing;
                _prepStart = Time.unscaledTime;
                TerrainMirror.Begin();
                TerrainMirror.ChooseLayer(hero);
                Plugin.L.LogMessage($"link: preparing. Copying Elden Ring's collision around the Tarnished at {er.x:F2} {er.y:F2} {er.z:F2} (zone {s.Stage:x8})");
                return;
            }

            string reason = toggle ? "F7 pressed" :
                            !hostAlive ? "Elden Ring stopped responding" :
                            hero == null ? "the AoTTG2 hero is gone" :
                            Bridge.HostLife != _lifeAtLink ? "Elden Ring moved the Tarnished (load, respawn or warp)" :
                            !stateOk ? "Elden Ring is busy (menu, loading or death)" : null;
            if (reason != null)
            {
                Stop(reason);
                return;
            }

            if (_phase == Phase.Preparing)
            {
                TerrainMirror.Update(er, Vector3.zero);
                if (TerrainMirror.ReadyAround(er.x, er.z))
                {
                    Vector3 h = hero.transform.position;
                    _offset = h - er;
                    TerrainMirror.SetOffset(_offset);
                    Plugin.L.LogMessage($"link: collision copied in {Time.unscaledTime - _prepStart:F2} s: {TerrainMirror.TileCount} tiles, " +
                                        $"{TerrainMirror.RaysCast} rays, {TerrainMirror.FloorHits} floor hits, {TerrainMirror.TopHits} top hits, " +
                                        $"{TerrainMirror.SolidColumns} solid columns, " +
                                        $"{TerrainMirror.Triangles} triangles");
                    // Safety: never switch the hero over unless the copy is really under its feet.
                    Physics.SyncTransforms();
                    bool ok = TerrainMirror.GroundBelow(h, hero.gameObject.layer, out string report);
                    Plugin.L.LogMessage("link: ground check: " + report);
                    if (!ok)
                    {
                        // Fall back to the milestone 3 puppet link: AoTTG2's own map stays solid.
                        TerrainMirror.End();
                        _puppet = true;
                        Plugin.L.LogWarning("link: Elden Ring ground can't hold the hero yet, so linking WITHOUT shared collision (puppet mode)");
                    }
                    else
                    {
                        _puppet = false;
                        TerrainMirror.DisableAoTTG2Map();
                        EnemyProxies.Begin();
                    }
                    _lastHero = h;
                    _lastTime = Time.unscaledTime;
                    _velocity = Vector3.zero;
                    _maxSpeed = 0;
                    Rescues = 0;
                    _phase = Phase.Linked;
                    Plugin.L.LogMessage(_puppet ? "link: ON (puppet mode). The Tarnished follows the hero; no shared collision"
                                                : "link: ON. The Tarnished now follows the hero, who walks on Elden Ring's ground");
                    Write(hero);
                }
                else if (Time.unscaledTime - _prepStart > PrepTimeout)
                {
                    Stop("Elden Ring's collision didn't arrive in time");
                }
                return;
            }

            Write(hero);
            if (_puppet) return;
            Vector3 erHero = hero.transform.position - _offset;
            TerrainMirror.Update(erHero, _velocity);
            EnemyProxies.Update(_offset, TerrainMirror.Layer);
            Rescue(hero, erHero);
        }

        // Safety net: if the hero ends up more than 3 m under the copied ground (a gap in the copy,
        // or falling faster than the copy could follow), put it back on top and stop the fall.
        private static void Rescue(Component hero, Vector3 er)
        {
            // Only while actually falling: walking through a cave can put the hero under a column the
            // copy still thinks is solid, until that area is re-sampled at the new height.
            if (_velocity.y > -6f) return;
            if (!TerrainMirror.FloorAt(er.x, er.z, out float floor) || er.y > floor - 3f) return;
            if (floor - er.y > 40f) return;  // a floor far above is a different level (a bridge or a roof), not a fall
            Vector3 p = hero.transform.position;
            p.y = floor + _offset.y + 0.5f;
            hero.transform.position = p;
            var rb = hero.GetComponent<Rigidbody>();
            if (rb != null) rb.velocity = Vector3.zero;
            Rescues++;
            Plugin.L.LogWarning($"link: rescue {Rescues}: the hero was {floor - er.y:F1} m under Elden Ring's ground at " +
                                $"{er.x:F1} {er.z:F1}; put back on top");
        }

        public static int Rescues;
        public static bool CameraSync = true;

        public static void Stop(string reason)
        {
            if (_phase == Phase.Off) return;
            bool wasLinked = _phase == Phase.Linked;
            _phase = Phase.Off;
            Bridge.WriteControl(0, 0, 0, 0, 0);
            TerrainMirror.End();
            EnemyProxies.End();
            Plugin.L.LogMessage($"link: OFF ({reason})" + (wasLinked ? $"; fastest hero speed {_maxSpeed:F1} m/s, {Rescues} rescues, " +
                                $"{TerrainMirror.Batches} collision batches, last one {TerrainMirror.LastBatchMs:F0} ms" : ""));
        }

        private static void Write(Component hero)
        {
            Vector3 p = hero.transform.position;
            Vector3 e = p - _offset;
            // Elden Ring renders from AoTTG2's camera, so what you see matches how you steer.
            uint flags = Protocol.CtrlMoveHunter | Protocol.CtrlFlying;
            var cam = Camera.main;
            if (cam != null && CameraSync)
            {
                flags |= Protocol.CtrlOverrideCamera;
                Transform t = cam.transform;
                Vector3 eye = t.position - _offset;
                Bridge.WriteControl(flags, e.x, e.y, e.z, hero.transform.eulerAngles.y, eye, eye + t.forward, t.up, cam.fieldOfView);
            }
            else
            {
                Bridge.WriteControl(flags, e.x, e.y, e.z, hero.transform.eulerAngles.y);
            }

            float now = Time.unscaledTime;
            float dt = now - _lastTime;
            if (dt > 0.05f)
            {
                _velocity = (p - _lastHero) / dt;
                _maxSpeed = Math.Max(_maxSpeed, _velocity.magnitude);
                _lastHero = p;
                _lastTime = now;
            }
            if (now >= _nextLog)
            {
                _nextLog = now + 1f;
                Bridge.ReadState(out var s);
                string line = $"link: hero at ER {e.x,9:F2} {e.y,8:F2} {e.z,9:F2} yaw {hero.transform.eulerAngles.y,5:F0} speed {_velocity.magnitude,5:F1}; " +
                              $"Tarnished reported at {s.PX,9:F2} {s.PY,8:F2} {s.PZ,9:F2}; tiles {TerrainMirror.TileCount}, " +
                              $"batches {TerrainMirror.Batches} (last {TerrainMirror.LastBatchMs:F0} ms), enemies {EnemyProxies.Count}";
                Plugin.L.LogInfo(line);
            }
        }
    }
}
