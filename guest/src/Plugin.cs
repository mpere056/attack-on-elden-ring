// Attack on Elden Ring: the AoTTG2 side of the bridge (BepInEx 6, IL2CPP).
// Keeps AoTTG2 offline, opens the shared memory, logs both characters once a second
// (milestone 2), and links the Tarnished to AoTTG2's hero with F7 (milestone 3, Link.cs).
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace Aoer
{
    [BepInPlugin(Guid, "Attack on Elden Ring bridge", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "aoer.bridge";
        public const string Version = "0.5.0";
        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            L.LogMessage($"Attack on Elden Ring bridge {Version} loading");
            var harmony = new Harmony(Guid);
            OfflineGuard.Apply(harmony, L);
            LongHooks.Patch(harmony);
            try
            {
                Bridge.Open();
                L.LogMessage($"bridge: opened {Protocol.ShmName}");
            }
            catch (Exception e)
            {
                L.LogError("bridge: could not open shared memory: " + e);
            }
            AddComponent<BridgeBehaviour>();
        }
    }

    /// <summary>Runs on AoTTG2's main thread every frame.</summary>
    public class BridgeBehaviour : MonoBehaviour
    {
        public BridgeBehaviour(IntPtr ptr) : base(ptr) { }

        private float _nextLog;
        private bool _wasAlive;

        private void Update()
        {
            // AoTTG2 must keep simulating while Elden Ring has the focus.
            if (!Application.runInBackground)
            {
                Application.runInBackground = true;
                Plugin.L.LogInfo("AoTTG2 now keeps running when its window is in the background");
            }
            Bridge.Beat();
            bool alive = Bridge.HostAlive();
            var hero = GetHero();
            Link.Update(alive, hero);

            if (Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + 1f;
            OfflineGuard.Check();

            if (alive != _wasAlive)
            {
                Plugin.L.LogMessage(alive ? $"bridge: Elden Ring connected (pid {Bridge.HostPid}). Press F7 to link the Tarnished to the AoTTG2 hero"
                                          : "bridge: Elden Ring not running or not responding");
                _wasAlive = alive;
            }
            if (Link.Active) return;  // the link logs its own lines
            string heroText = hero == null ? null : Describe(hero.transform.position);
            if (!alive)
            {
                if (heroText != null) Plugin.L.LogInfo($"AoTTG2 hero {heroText} | Elden Ring: not connected");
                return;
            }
            if (!Bridge.ReadState(out var s) || !s.PlayerValid)
            {
                Plugin.L.LogInfo($"Elden Ring: no character in the world{(s.Busy ? " (loading)" : "")}" + (heroText != null ? $" | AoTTG2 hero {heroText}" : ""));
                return;
            }
            Plugin.L.LogInfo($"Tarnished x {s.PX,9:F2}  y {s.PY,8:F2}  z {s.PZ,9:F2}  zone {s.Stage:x8}" +
                             (heroText != null ? $" | AoTTG2 hero {heroText}" : " | AoTTG2: no hero spawned"));
        }

        private void OnDestroy() => Link.Stop("AoTTG2 is closing");

        private static string Describe(Vector3 p) => $"x {p.x,8:F2}  y {p.y,8:F2}  z {p.z,8:F2}";

        /// <summary>AoTTG2's local character, or null when none exists.</summary>
        private static Component GetHero()
        {
            try
            {
                var manager = ApplicationManagers.SceneLoader.CurrentGameManager;
                var inGame = manager == null ? null : manager.TryCast<GameManagers.InGameManager>();
                var character = inGame == null ? null : inGame.CurrentCharacter;
                if (character == null || character.Dead) return null;
                return character;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
