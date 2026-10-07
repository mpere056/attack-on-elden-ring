// While linked, the hero moves on the ground at the Tarnished's own speeds instead of AoTTG2's much
// faster run, so the Tarnished doesn't race across the Lands Between.
// Main mechanism: AoTTG2's own run speed (Human.Stats.RunSpeed) is set to the Tarnished's speed while
// linked and restored on unlink (SetRunSpeed). The first attempt only trimmed the rigidbody velocity
// after Human.FixedUpdate; AoTTG2 re-applies its own velocity, so speeds stayed at 11-12 m/s.
// The velocity trim below stays as a backstop:
//   - normal running: Elden Ring's jog, about 3.3 m/s (recorded with tools/erctl.py anim);
//   - sprinting: about 6 m/s, while the Tarnished plays its sprint animation (2020x-2029x), i.e.
//     while the player holds Elden Ring's sprint key.
// Only on the ground with no hook flying or attached, and after a short moment on the ground, so
// ODM swings, jumps and falls keep AoTTG2's full speed; landing momentum is eased down, not cut.
using System;
using HarmonyLib;
using UnityEngine;

namespace Aoer
{
    internal static class GroundSpeed
    {
        public const float JogSpeed = 3.3f, SprintSpeed = 6.0f;
        private const float MeasuredRun = 11.5f;  // AoTTG2's unmodified ground speed seen in the logs
        private const float Decel = 25f;          // m/s per second when slowing down to the cap
        private const float SettleTime = 0.2f;    // on the ground this long before the cap applies

        public static volatile int ElderAnim = -1; // the Tarnished's current animation (set by Link)
        private static float _groundedSince = -1f;
        public static bool Capping { get; private set; }

        private static Characters.Human _statsHero;
        private static float _originalRun = -1f;
        public static float OriginalRun => _originalRun;

        /// <summary>Every frame while linked: AoTTG2's run speed = the Tarnished's (sprint while it sprints).</summary>
        public static void SetRunSpeed(Component hero)
        {
            try
            {
                var h = hero == null ? null : hero.TryCast<Characters.Human>();
                var stats = h?.Stats;
                if (stats == null) return;
                if (_statsHero == null || _statsHero.Pointer != h.Pointer)
                {
                    RestoreRunSpeed();
                    _statsHero = h;
                    _originalRun = stats.RunSpeed;
                    Plugin.L.LogMessage($"speed: AoTTG2 run speed {_originalRun:F2} -> Tarnished speeds ({JogSpeed} / sprint {SprintSpeed} m/s) while linked");
                }
                bool sprinting = ElderAnim >= 20200 && ElderAnim < 20300;
                float wanted = sprinting ? SprintSpeed : JogSpeed;
                // RunSpeed in m/s (original above 4) is set directly; if it is a multiplier instead,
                // scale it by wanted / the ~11.5 m/s AoTTG2 actually ran at (0.9.4 test log).
                stats.RunSpeed = _originalRun > 4f ? wanted : _originalRun * wanted / MeasuredRun;
            }
            catch (Exception e)
            {
                Plugin.L.LogDebug("speed: " + e.Message);
            }
        }

        /// <summary>Unlink: AoTTG2's own run speed back.</summary>
        public static void RestoreRunSpeed()
        {
            try
            {
                if (_statsHero != null && _originalRun > 0f && _statsHero.Stats != null)
                {
                    _statsHero.Stats.RunSpeed = _originalRun;
                    Plugin.L.LogMessage($"speed: AoTTG2 run speed restored to {_originalRun:F2}");
                }
            }
            catch (Exception) { }
            _statsHero = null;
            _originalRun = -1f;
        }

        public static void Patch(Harmony h)
        {
            var m = AccessTools.Method(typeof(Characters.Human), "FixedUpdate", Type.EmptyTypes);
            if (m == null) { Plugin.L.LogWarning("speed: Human.FixedUpdate not found; ground speed not capped"); return; }
            h.Patch(m, postfix: new HarmonyMethod(typeof(GroundSpeed), nameof(After)));
            Plugin.L.LogInfo("speed: ground speed follows the Tarnished's while linked");
        }

        // Jump height while linked: AoTTG2's human jump is hard-coded (no JumpForce stat like titans and
        // horses), so the take-off is caught here. A jump = just left the ground (grounded within
        // JumpWindow), going up faster than JumpMinUp, no hook involved (ODM launches are untouched).
        // Its upward speed is scaled so the jump reaches JumpHeightPercent of AoTTG2's height (height
        // goes with the square of the take-off speed), and kept under that for JumpHold seconds in case
        // AoTTG2 sets it again. JumpHeightPercent comes from the plugin's config file (Plugin.cs).
        public static float JumpHeightPercent = 50f;
        private static float JumpScale => Mathf.Sqrt(Mathf.Clamp(JumpHeightPercent, 5f, 100f) / 100f);
        private const float JumpWindow = 0.25f, JumpMinUp = 2.5f, JumpHold = 0.4f;
        private static float _lastGrounded = -10f, _jumpAt = -10f, _jumpCap;
        private static bool _jumpDone;
        public static int Jumps;

        private static void After(Characters.Human __instance)
        {
            try
            {
                Capping = false;
                if (!Link.CollisionActive || __instance == null || !__instance.IsMine()) return;
                float now = Time.time;
                var rb = __instance.GetComponent<Rigidbody>();
                if (rb == null) return;
                bool hooked = Hooked(__instance);
                bool grounded = __instance.Grounded;
                Vector3 v = rb.velocity;

                // --- jump height ---
                if (grounded && v.y <= 0.5f) _jumpDone = false;
                if (grounded) _lastGrounded = now;
                if (!hooked && !_jumpDone && now - _lastGrounded < JumpWindow && v.y > JumpMinUp)
                {
                    _jumpCap = v.y * JumpScale;
                    _jumpAt = now;
                    _jumpDone = true;
                    Jumps++;
                    rb.velocity = v = new Vector3(v.x, _jumpCap, v.z);
                }
                else if (_jumpDone && !hooked && now - _jumpAt < JumpHold && v.y > _jumpCap)
                {
                    rb.velocity = v = new Vector3(v.x, _jumpCap, v.z);
                }

                // --- ground speed backstop (the main cap is Stats.RunSpeed) ---
                if (!grounded || hooked) { _groundedSince = -1f; return; }
                if (_groundedSince < 0f) _groundedSince = now;
                if (now - _groundedSince < SettleTime) return;
                float h = new Vector2(v.x, v.z).magnitude;
                bool sprinting = ElderAnim >= 20200 && ElderAnim < 20300;
                float cap = sprinting ? SprintSpeed : JogSpeed;
                if (h <= cap) return;
                float target = Mathf.Max(cap, h - Decel * Time.fixedDeltaTime);
                float k = target / h;
                rb.velocity = new Vector3(v.x * k, v.y, v.z * k);
                Capping = true;
            }
            catch (Exception) { }
        }

        private static bool Hooked(Characters.Human h)
        {
            var l = h.HookLeft; var r = h.HookRight;
            return (l != null && (l.IsHooked() || l.IsHooking())) || (r != null && (r.IsHooked() || r.IsHooking()));
        }
    }
}
