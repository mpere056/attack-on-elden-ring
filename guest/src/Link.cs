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
        public static bool CollisionActive => _phase == Phase.Linked && !_puppet;

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
                if (Plugin.Instance != null) Plugin.ReloadSettings(Plugin.Instance.Config);
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
            LongHooks.Update();
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

        private static void StartCompositing(Component hero, HostState s)
        {
            int w = s.BackW > 0 ? (int)s.BackW : s.WinW, h = s.BackH > 0 ? (int)s.BackH : s.WinH;
            if (!Compositor.Begin(hero, w, h)) Plugin.L.LogWarning("link: compositing unavailable; the Tarnished stays visible");
        }

        // The hero's ground movement as a left-stick position relative to the camera (which Elden Ring
        // renders from, so its locomotion moves the Tarnished the same way). Elden Ring walks at a light
        // tilt and runs at full tilt; above ~5 m/s it is a full tilt. In the air: centred, for now.
        private static void Stick(Component hero, Vector3 camForward, out float sx, out float sy)
        {
            sx = sy = 0f;
            var character = hero.TryCast<Characters.BaseCharacter>();
            if (character != null && !character.Grounded) return;
            var rb = hero.GetComponent<Rigidbody>();
            Vector3 v = rb != null ? rb.velocity : _velocity;
            v.y = 0f;
            float speed = v.magnitude;
            if (speed < 0.5f) return;
            Vector3 f = camForward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 d = v / speed;
            float tilt = Mathf.Clamp(0.35f + (speed - 0.5f) / 4.5f * 0.65f, 0.35f, 1f);
            sx = Vector3.Dot(d, r) * tilt;
            sy = Vector3.Dot(d, f) * tilt;
            StickTilt = tilt;
        }

        // What a hook fired at the screen centre would catch: AoTTG2's camera is Elden Ring's camera,
        // so the centre of Elden Ring's screen is AoTTG2's aim. Tested against the collision copy and
        // the enemy boxes (same layer). 0 = nothing within reach.
        private const float HookReach = 120f;
        private static float AimDistance(Transform cam)
        {
            RaycastHit hit;
            if (Physics.Raycast(cam.position, cam.forward, out hit, HookReach, 1 << TerrainMirror.Layer, QueryTriggerInteraction.Ignore))
                return Mathf.Max(hit.distance, 0.01f);
            return 0f;
        }

        // ODM cables: from the hero's waist (a little to each side) to every hook that is flying or
        // attached, projected onto Elden Ring's screen.
        private static readonly System.Collections.Generic.List<CableOverlay.Segment> _cables = new();
        private static CableOverlay.Segment[] Cables(Component hero, Transform cam, Vector3 eye, float fov, int w, int h)
        {
            _cables.Clear();
            var human = hero.TryCast<Characters.Human>();
            if (human == null) return Array.Empty<CableOverlay.Segment>();
            Transform ht = hero.transform;
            Vector3 waist = ht.position + Vector3.up * 1.0f;
            AddCable(human.HookLeft, waist - ht.right * 0.25f, cam, eye, fov, w, h);
            AddCable(human.HookRight, waist + ht.right * 0.25f, cam, eye, fov, w, h);
            return _cables.ToArray();
        }

        private static void AddCable(Characters.HookUseable hooks, Vector3 from, Transform cam, Vector3 eye, float fov, int w, int h)
        {
            try
            {
                if (hooks == null || !(hooks.IsHooking() || hooks.IsHooked())) return;
                Vector3 to = hooks.GetHookPosition();
                if (CableOverlay.Project(eye, cam, fov, w, h, from, out float x0, out float y0) &&
                    CableOverlay.Project(eye, cam, fov, w, h, to, out float x1, out float y1))
                    _cables.Add(new CableOverlay.Segment { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
            }
            catch (Exception) { }
        }

        // How far behind the hero's head Elden Ring's view sits while linked (config CameraDistance).
        public static float CameraDistance = 4.0f;
        private static float _xhX = -1f, _xhY = -1f;  // smoothed crosshair position
        private const float PivotHeight = 1.6f;   // the head, roughly, above the hero's feet

        // Behind the head along AoTTG2's view direction, pulled in if Elden Ring's collision (the copy)
        // is in the way, so the camera doesn't end up inside a wall or under the ground.
        private static Vector3 FollowEye(Transform cam, Component hero)
        {
            Vector3 pivot = hero.transform.position + Vector3.up * PivotHeight;
            Vector3 back = -cam.forward;
            float dist = CameraDistance;
            RaycastHit hit;
            if (Physics.Raycast(pivot, back, out hit, dist, 1 << TerrainMirror.Layer, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0.4f, hit.distance - 0.25f);
            return pivot + back * dist;
        }

        public static float StickTilt;
        public static int Rescues;
        public static bool CameraSync = true;

        public static void Stop(string reason)
        {
            if (_phase == Phase.Off) return;
            bool wasLinked = _phase == Phase.Linked;
            _phase = Phase.Off;
            Bridge.WriteControl(0, 0, 0, 0, 0);
            CrosshairWindow.Hide();
            _xhX = _xhY = -1f;
            CableOverlay.Hide();
            TarnishedAnim.Reset();
            GroundSpeed.RestoreRunSpeed();
            OdmPace.Restore();
            Compositor.End();
            LongHooks.End();
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
            // Tarnished mode: Elden Ring animates the Tarnished from a virtual stick (host input.cpp).
            uint flags = Protocol.CtrlMoveHunter | Protocol.CtrlFlying | Protocol.CtrlVirtualPad;
            // Crosshair: our own small overlay window (CrosshairWindow), not drawn by Elden Ring.
            // Playing from Elden Ring's window: its own movement (and animations) stay on.
            if (BackgroundInput.Active) flags |= Protocol.CtrlGameInput;
            bool inAir = !_puppet && TarnishedAnim.InAir(hero);
            if (inAir) flags |= Protocol.CtrlAirborne;
            Bridge.ReadState(out var animState);
            BackgroundInput.AirJump(inAir, animState.AnimId);
            GroundSpeed.ElderAnim = animState.AnimId;
            if (!_puppet) OdmPace.Update(hero);
            if (!_puppet) GroundSpeed.SetRunSpeed(hero);
            // While AoTTG2's soldier is drawn into Elden Ring, the gliding Tarnished is hidden.
            if (Compositor.Active) flags |= Protocol.CtrlComposite | Protocol.CtrlHideHunter;
            var cam = Camera.main;
            if (cam != null && CameraSync)
            {
                flags |= Protocol.CtrlOverrideCamera;
                Transform t = cam.transform;
                // Elden Ring's own kind of follow camera: AoTTG2's view direction (so the mouse still steers),
                // but placed behind the hero's head at CameraDistance and centred on it, the way Elden Ring
                // frames the Tarnished. (AoTTG2's camera looks over the hero at the aim point, which put the
                // Tarnished at the bottom edge of the screen.)
                Vector3 unityEye = FollowEye(t, hero);
                Vector3 eye = unityEye - _offset;
                Stick(hero, t.forward, out float sx, out float sy);
                float aim = _puppet ? 0f : AimDistance(t);
                if (!_puppet && Bridge.ReadState(out var hs) && hs.WinW > 0 && hs.WinH > 0)
                {
                    // AoTTG2 aims from its own camera, not from the follow camera: draw the crosshair where that
                    // aim lands on Elden Ring's screen (the hit, or a point far along the aim if nothing is hit).
                    Vector3 aimPoint = t.position + t.forward * (aim > 0f ? aim : HookReach);
                    if (!CableOverlay.Project(unityEye, t, cam.fieldOfView, hs.WinW, hs.WinH, aimPoint, out float ax, out float ay))
                    {
                        ax = hs.WinW * 0.5f; ay = hs.WinH * 0.5f;
                    }
                    // Smoothed: the raw point twitches as the aim hops between near and far surfaces and the
                    // two cameras update a frame apart. ~40 ms to follow, big jumps (a new target) at once.
                    float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.04f);
                    if (_xhX < 0f || Mathf.Abs(ax - _xhX) > 120f || Mathf.Abs(ay - _xhY) > 120f) { _xhX = ax; _xhY = ay; }
                    else { _xhX += (ax - _xhX) * k; _xhY += (ay - _xhY) * k; }
                    CrosshairWindow.Show(hs.WinX + Mathf.RoundToInt(_xhX), hs.WinY + Mathf.RoundToInt(_xhY), aim > 0f);
                    CableOverlay.Set(hs.WinX, hs.WinY, hs.WinW, hs.WinH, Cables(hero, t, unityEye, cam.fieldOfView, hs.WinW, hs.WinH));
                }
                else
                {
                    CrosshairWindow.Hide();
                    CableOverlay.Hide();
                }
                // The event-animation route doesn't play locomotion (MODLOG); kept for the log only.
                TarnishedAnim.Pick(hero);
                int anim = -1;
                Bridge.WriteControl(flags, e.x, e.y, e.z, hero.transform.eulerAngles.y, eye, eye + t.forward, t.up, cam.fieldOfView, sx, sy, 0, aim, anim, TarnishedAnim.Speed(hero));
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
                              $"batches {TerrainMirror.Batches} (last {TerrainMirror.LastBatchMs:F0} ms), wall panels {WallProbe.Panels}, enemies {EnemyProxies.Count}, " +
                              $"anim {TarnishedAnim.Current} (Elden Ring {s.AnimId}, jumps pressed {BackgroundInput.JumpsPressed}, hero jumps lowered {GroundSpeed.Jumps}), long hooks {LongHooks.Fired} fired / {LongHooks.Anchored} anchored (last {LongHooks.LastDistance:F0} m) / {LongHooks.Missed} missed";
                Plugin.L.LogInfo(line);
            }
        }
    }
}
