# Status

**Milestone 0: done** (2026-10-07). me3 launch used the mod save only; BepInEx 6 be.788 loaded in
AoTTG2 1.2.3, generated interop (Assembly-CSharp, Scripts, Photon*), chainloader started cleanly.

**Milestone 1: passed** (2026-10-07 survey, `runtime/survey-20261007-133155.csv`, 9.7 min).
- `host/` = Minecraft-Ring er-bridge (711015a) with: me3 loader (`loader.cpp`, no dinput8
  exports, EAC check kept), named shared memory `Local\AoER_bridge_v1`, magic `AOER`, logs in
  `runtime/host.log`, compositor compiled in but not started (`AOER_COMPOSITOR=0`).
- `tools/build_host.py` builds `dist/aoer_host.dll` + `dist/aoer_core.dll` (warnings are upstream).
- `tools/erctl.py` status/aim/reach/survey. Verified against a fake host with known geometry
  (floor and wall distances correct). **Not tested against the real game yet.**
- `me3/aoer.me3` now loads `dist/aoer_host.dll`.

Results:
- Host loaded under me3, all tasks registered, 60 fps steady, game-thread tick 0.25-0.30 ms,
  two loading screens survived, no faults.
- Rays: answered within one frame (aim ray median 17 ms round trip; a 252-ray sweep 18 ms).
- Reach: hits found out to 355 m (Stormveil, level), 272 m (Stormveil, 20 deg up), 177 m (open
  world, 20 deg up). Most hits are close because both places were enclosed (castle; open-world
  time was 3 min, all within ~60 m near Stormveil's front, eye height ~160).
- Open questions, not blocking: 38 level and 22 downward aim misses inside Stormveil (drops off
  ledges, or collision not loaded beyond?); a long open-field survey (Limgrave plains, trees)
  is still worth doing later.

Verdict: route 1 is viable. Collision exists well past castle scale and costs almost nothing to
query.

**Milestone 2: passed** (2026-10-07). Both positions logged together for ~100 s, 17 connect
methods patched, no connect attempts in single player, no errors either side.
- `guest/` BepInEx 6 plugin `AoerBridge.dll` (net6.0, BepInEx runs .NET 6.0.7), built by
  `tools/build_guest.py --install` into AoTTG2 `BepInEx\plugins\AoerBridge`.
- Offline guard: Harmony prefixes refuse every Photon connect (PhotonNetwork x7,
  LoadBalancingClient x8, PhotonPeer.Connect x2); only ConnectUsingSettings(.., startInOfflineMode:
  true) passes. Once a second it also disconnects if Photon is ever online.
- Opens `Local\AoER_bridge_v1`, writes guest pid/heartbeat, logs the Tarnished's position and the
  AoTTG2 hero's (SceneLoader.CurrentGameManager -> InGameManager.CurrentCharacter) once a second.
- Sets Application.runInBackground so AoTTG2 keeps running unfocused.
- AoTTG2 facts found (interop names only, kept in `.local/`): single player goes through
  `Settings.MultiplayerSettings.ConnectOffline`; hooks are `Characters.Hook` /
  `Characters.HookUseable` (`Human.HookLeft/HookRight`), with `Hook.HookMask` and
  `FixedUpdateHooking` for milestone 4.

**Milestone 3: passed** (2026-10-07). Sent vs reported Tarnished positions agree to a few cm at up
to 38 m/s; clockwise stays clockwise (axes correct); no fall damage; unlink hands back. The user
restarted both games once on purpose after the Tarnished fell through the map (expected: no
collision shared yet).
- F7 (either window, `GetAsyncKeyState`) toggles the link. While on, AoTTG2 writes the control
  block every frame: `MOVE_HUNTER | FLYING`, hunterPos = TarnishedAtLink + (hero - heroAtLink),
  yaw = Unity yaw + 180 (host expects "Minecraft yaw").
- Host side is upstream `stand_in`: gravity off, NoMove, NoDead, HP refill, position + facing
  written to Havok every tick; released when flags go 0 or control stops changing for 1 s.
- Link drops on: F7, host not responding, hero gone/dead, hostLife change, host busy/dead.
- No collision shared yet: the Tarnished will clip through Elden Ring geometry.
- Questions this test answers: are the axes right (does the Tarnished face where it moves, does a
  clockwise circle stay clockwise), do big falls stay safe, does hand-back work.

**Milestone 4 (reordered: collision before long-range hooks): working (0.4.6 user test); 0.4.7 adds
hookable enemies and a falling-only rescue, waiting on a test.**
Full mode ran ~70 s on real Elden Ring terrain before falling through a hole on a steep slope
(fixed in 0.4.5, see MODLOG). Layer: MapObjectEntities (what the hero stands on; AoTTG2 grounds
its hero with its own queries, not the layer matrix). Puppet mode is the fallback if the check fails.
- `guest/src/TerrainMirror.cs`: 16 m tiles, a column per metre, 3 rays per column (floor from
  yRef+2.5, top from yRef+60, ceiling up from yRef+0.3) -> one MeshCollider per tile: ground
  triangles between floors within 1.2 m, 1 m column boxes for anything above the player's level.
  Keeps a 7x7 tile ring (~48 m), prefetches along velocity, re-samples on 12 m height change.
- Link now prepares first (3x3 tiles around the Tarnished), then switches off AoTTG2's map
  colliders (non-character, logged with their layers) and uses the most common map layer for the
  mirror so AoTTG2's hooks and ground checks see it.
- `guest/src/Rays.cs`: async ray batches through the shared ray block.
- Not tested in game. Unknowns: ray throughput for ~7800-ray batches, whether AoTTG2's hook mask
  and grounding include the chosen layer, MeshCollider behaviour at ODM speeds.
