// Slower ODM gear while linked: AoTTG2's clock (Time.timeScale) runs at OdmSpeedPercent while the hero
// is on ODM, i.e. a hook is flying or attached, or it is still in the air after one. Reeling, gas
// boosts, gravity and the swing all slow together, so a swing keeps its shape and is just slower.
// Back to normal speed the moment the hero lands; Shift jumps and walking are never slowed.
// Everything in the bridge itself uses unscaled time, so the link isn't affected.
using UnityEngine;

namespace Aoer
{
    internal static class OdmPace
    {
        public static float OdmSpeedPercent = 80f;
        private static bool _onOdm, _applied;

        public static void Update(Component hero)
        {
            var h = hero == null ? null : hero.TryCast<Characters.Human>();
            if (h == null) { Restore(); return; }
            bool hooked = Hooked(h);
            if (hooked) _onOdm = true;
            else if (h.Grounded) _onOdm = false;   // landed: ODM over (a plain jump never sets it)
            float scale = _onOdm ? Mathf.Clamp(OdmSpeedPercent, 20f, 100f) / 100f : 1f;
            if (Time.timeScale < 0.05f) return;  // AoTTG2 itself paused the game (timeScale 0): leave it
            if (!Mathf.Approximately(Time.timeScale, scale))
            {
                Time.timeScale = scale;
                _applied = scale < 1f;
            }
        }

        public static void Restore()
        {
            _onOdm = false;
            if (_applied && Time.timeScale >= 0.05f) Time.timeScale = 1f;
            _applied = false;
        }

        private static bool Hooked(Characters.Human h)
        {
            var l = h.HookLeft; var r = h.HookRight;
            return (l != null && (l.IsHooked() || l.IsHooking())) || (r != null && (r.IsHooked() || r.IsHooking()));
        }
    }
}
