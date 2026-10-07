// Tarnished mode: which Elden Ring animation the Tarnished should play for what AoTTG2's hero is
// doing. The host plays it through the character's event-animation override (game.cpp
// drive_animation). Ids recorded on this save with tools/erctl.py anim (MODLOG, 2026-10-07):
//   20120 standing          22100 jog (~3 m/s)          20220 sprint (~4-6 m/s)
//   202020 jump take-off    202040 jump, in the air     202115 jump landing
//   4050 / 4000 falling     4220 hard landing           27120 roll
// They may depend on the equipped weapons and stance.
using UnityEngine;

namespace Aoer
{
    internal static class TarnishedAnim
    {
        public const int Stand = 20120, Jog = 22100, Sprint = 20220, Airborne = 202040;

        private enum Move { Stand, Jog, Sprint, Air }
        private static Move _move = Move.Stand;
        private static float _airSince = -1f;
        public static string Current => _move.ToString();

        /// <summary>The animation to request this frame.</summary>
        public static int Pick(Component hero)
        {
            var character = hero.TryCast<Characters.BaseCharacter>();
            var rb = hero.GetComponent<Rigidbody>();
            Vector3 v = rb != null ? rb.velocity : Vector3.zero;
            float ground = new Vector2(v.x, v.z).magnitude;
            bool grounded = character == null || character.Grounded;

            // In the air for a moment (not just a bump or a step down): the airborne pose.
            if (!grounded)
            {
                if (_airSince < 0f) _airSince = Time.unscaledTime;
                if (Time.unscaledTime - _airSince > 0.15f) _move = Move.Air;
            }
            else
            {
                _airSince = -1f;
                // Bands with some overlap, so the animation doesn't flicker at the edges.
                switch (_move)
                {
                    case Move.Stand: if (ground > 0.8f) _move = ground > 5.5f ? Move.Sprint : Move.Jog; break;
                    case Move.Jog: if (ground < 0.4f) _move = Move.Stand; else if (ground > 5.5f) _move = Move.Sprint; break;
                    case Move.Sprint: if (ground < 0.4f) _move = Move.Stand; else if (ground < 4.5f) _move = Move.Jog; break;
                    default: _move = ground < 0.4f ? Move.Stand : ground > 5.5f ? Move.Sprint : Move.Jog; break;
                }
            }
            return _move switch
            {
                Move.Jog => Jog,
                Move.Sprint => Sprint,
                Move.Air => Airborne,
                _ => Stand,  // the standing state recorded in play (not the event idle, 63000)
            };
        }

        // Elden Ring's run cycle covers about this many metres per second at normal speed (jog 22100
        // was ~3 m/s in the recording); the Tarnished's animation is sped up by hero speed / this, so
        // its feet keep up with how fast AoTTG2's hero really moves. Capped so it doesn't look absurd.
        public const float ElderRunSpeed = 3.2f;
        public static float Speed(Component hero)
        {
            var rb = hero.GetComponent<Rigidbody>();
            if (rb == null) return 1f;
            Vector3 v = rb.velocity;
            float ground = new Vector2(v.x, v.z).magnitude;
            if (ground < 0.5f) return 1f;
            return Mathf.Clamp(ground / ElderRunSpeed, 0.6f, 3.0f);
        }

        /// <summary>Off the ground for more than a moment (jump, fall, ODM).</summary>
        public static bool InAir(Component hero)
        {
            Pick(hero);
            return _move == Move.Air;
        }

        public static void Reset()
        {
            _move = Move.Stand;
            _airSince = -1f;
        }
    }
}
