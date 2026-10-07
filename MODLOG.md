# Mod log

## 2026-10-07: project created
- Chose route 1 + 2 (passthrough with compositing). Reasons in `docs/DESIGN.md`.
- Cloned references into `ref/` (ignored): Minecraft-Ring 711015a, minecraft-crossover-bridge
  d172387, EldenCraft (wargamereview) 89e0eb4.
- Wrote AGENTS.md, DESIGN.md, CONTRACT.md, whitelist .gitignore.
- Findings carried in: AoTTG2 has `ConnectOffline`/`EnterOfflineRoom`; its `AnticheatManager` is
  room moderation (vote-kick, ban list, chat filter), not client protection. ER `CastRay` has no
  length cap in the bridge; budget ~2 ms per frame.

## 2026-10-07: milestone 0, tools and loaders
- Installed LLVM-MinGW, me3 0.13.0 (portable) and .NET 8 SDK into `.tools/`.
- Installed BepInEx 6 be.788 into AoTTG2 `MainApp` (file list in `.local/`).
- Added `me3/aoer.me3` (separate save `ER0000.aoer.sl2`, no online, no arxan override) and
  `Play-EldenRing.bat`. Seeded the mod save from the current save; backed up saves.
- Not tested yet: both launches await the user.

## 2026-10-07: milestone 0 passed
- me3 launch: game wrote only `ER0000.aoer.sl2`. BepInEx: chainloader startup complete, no errors.

## 2026-10-07: milestone 1, host DLL and raycast tool
- Copied Minecraft-Ring's er-bridge into `host/` (MIT notice in `host/LICENSE-upstream.txt`).
- `proxy.cpp` -> `loader.cpp`: loaded by me3 `[[natives]]`; removed DirectInput exports and the
  ERBRIDGE env gate; kept the Easy Anti-Cheat refusal; runtime folder passed via `AOER_DIR`.
- Shared memory: file mapping -> named mapping `Local\AoER_bridge_v1`; magic "MHMC" -> "AOER".
- Compositor not started until milestone 6 (`AOER_COMPOSITOR=0` in core.cpp).
- `tools/build_host.py` (LLVM-MinGW), `tools/erctl.py`, `Survey.bat`.
- Tested: build; erctl against a fake host. Not tested: in game.

## 2026-10-07: milestone 1 passed
- In-game survey: 9.7 min, Stormveil (zone 0xa000000) and open world near Stormveil (0x3c000000).
- Hits up to 355 m; ray round trip one frame; 250-ray sweep in one frame; tick 0.3 ms; 60 fps.
- Inherited behaviour noticed in host.log: upstream patches AtkParam_Pc row 10176000 in memory
  ("poke attack") for hit reactions. In-memory only, offline; keep in mind for combat.
