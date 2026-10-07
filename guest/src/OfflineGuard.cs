// Keeps AoTTG2 offline. Every way Photon can open a connection to a server is patched to refuse
// and log, at three levels: PUN's PhotonNetwork, Realtime's LoadBalancingClient, and the
// transport's PhotonPeer.Connect underneath them all. Photon's offline mode never calls any of
// these, so AoTTG2's single-player keeps working. The one call allowed through is
// PhotonNetwork.ConnectUsingSettings(settings, startInOfflineMode: true), which goes offline.
//
// This only stops our own copy of AoTTG2 from joining multiplayer. It doesn't touch AoTTG2's
// AnticheatManager or anything else related to anti-cheat.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;

namespace Aoer
{
    internal static class OfflineGuard
    {
        private static ManualLogSource _log;
        public static int Blocked;

        public static void Apply(Harmony harmony, ManualLogSource log)
        {
            _log = log;
            var block = new HarmonyMethod(typeof(OfflineGuard).GetMethod(nameof(Refuse), BindingFlags.Static | BindingFlags.NonPublic));
            var allowOffline = new HarmonyMethod(typeof(OfflineGuard).GetMethod(nameof(RefuseUnlessOffline), BindingFlags.Static | BindingFlags.NonPublic));

            var targets = new (Type type, string[] names)[]
            {
                (typeof(PhotonNetwork), new[] { "ConnectUsingSettings", "ConnectToMaster", "ConnectToBestCloudServer",
                                                 "ConnectToRegion", "Reconnect", "ReconnectAndRejoin" }),
                (typeof(LoadBalancingClient), new[] { "ConnectUsingSettings", "Connect", "ConnectToMasterServer", "ConnectToNameServer",
                                                       "ConnectToRegionMaster", "ReconnectToMaster", "ReconnectAndRejoin" }),
                (typeof(PhotonPeer), new[] { "Connect" }),
            };
            int patched = 0;
            foreach (var (type, names) in targets)
            {
                var wanted = new HashSet<string>(names);
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!wanted.Contains(m.Name) || m.ReturnType != typeof(bool)) continue;
                    bool offlineOverload = type == typeof(PhotonNetwork) && m.Name == "ConnectUsingSettings" &&
                                           Array.Exists(m.GetParameters(), p => p.Name == "startInOfflineMode");
                    harmony.Patch(m, prefix: offlineOverload ? allowOffline : block);
                    patched++;
                    _log.LogInfo($"offline guard: {type.Name}.{m.Name}({m.GetParameters().Length} args) {(offlineOverload ? "allowed only with startInOfflineMode" : "blocked")}");
                }
            }
            _log.LogMessage($"offline guard: {patched} online connection methods patched");
            if (patched < 17) _log.LogWarning("offline guard: fewer methods than expected were found; check the list above");
        }

        private static bool Refuse(MethodBase __originalMethod, ref bool __result)
        {
            Blocked++;
            _log.LogWarning($"offline guard: blocked {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} (online play is disabled by Attack on Elden Ring)");
            __result = false;
            return false;
        }

        private static bool RefuseUnlessOffline(bool startInOfflineMode, ref bool __result)
        {
            if (startInOfflineMode) return true;
            Blocked++;
            _log.LogWarning("offline guard: blocked PhotonNetwork.ConnectUsingSettings (online)");
            __result = false;
            return false;
        }

        /// <summary>Last line of defence: if Photon is somehow connected to a server, disconnect.</summary>
        public static void Check()
        {
            try
            {
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    _log.LogError("offline guard: Photon is connected online; disconnecting");
                    PhotonNetwork.Disconnect();
                }
            }
            catch (Exception e)
            {
                _log.LogDebug("offline guard check failed: " + e.Message);
            }
        }
    }
}
