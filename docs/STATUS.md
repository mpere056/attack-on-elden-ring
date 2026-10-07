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

Next: milestone 2, the AoTTG2 plugin (force offline, open the bridge, log the Tarnished).
