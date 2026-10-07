# Attack on Elden Ring

Attack on Titan Tribute Game 2's ODM gear inside Elden Ring. Both real games run at the same time:
AoTTG2 runs alongside and handles your movement, hooks and blades; Elden Ring draws the world and
runs its enemies, health and menus. A passthrough mod in the sense of the
[AI Game Modding Guides](https://github.com/trevaintdead/ai-game-modding-guides) (route 1 + 2).

**Status: experimental, work in progress.** Unofficial fan project, not affiliated with
FromSoftware, Bandai Namco or the AoTTG2 team. Built with AI assistance (Claude Code). No game
files are included: you need your own copies of both games.

## What works (tested by playing)
- AoTTG2's hero drives the Tarnished (F7 to link and unlink); Elden Ring's camera follows AoTTG2's.
- Elden Ring's ground, walls and caves are copied into AoTTG2 around you, so you walk, climb and
  ODM-swing on Elden Ring's world. AoTTG2 hooks catch Elden Ring terrain and enemies.
- Falling, fall damage and death are handled; a safety net catches falls through the copy.
- AoTTG2 is kept offline: every multiplayer connection is blocked.

## Not working yet
- The Tarnished glides (no walk animation) and AoTTG2's soldier, cables and HUD aren't drawn into
  Elden Ring yet (milestone 6, in progress). You play with both windows side by side.
- Long-range hooks (milestone 5) are built but not yet tested in play.
- Blade hits on Elden Ring enemies (milestone 7). Occasional wall clipping.

See `docs/DESIGN.md` for the plan and `docs/STATUS.md` / `MODLOG.md` for test results.

## Requirements
- Windows x64
- Elden Ring, Steam, App Ver. **1.17.1** (`eldenring.exe` 2.7.1.0). Other versions are refused.
- AoTTG2 **1.2.3**, run directly from `Aottg2.exe`
- [me3](https://github.com/garyttierney/me3) v0.13.0 and [BepInEx 6](https://builds.bepinex.dev/projects/bepinex_be)
  bleeding edge build 788 (IL2CPP x64), installed into AoTTG2's folder
- To build: [LLVM-MinGW](https://github.com/mstorsjo/llvm-mingw) and the .NET 8 SDK (both can live in `.tools/`)

**Offline only.** Elden Ring starts through me3 without Easy Anti-Cheat and with its own save file
(`ER0000.aoer.sl2`). Never take a modded game online.

## Setup (short version)
1. Put LLVM-MinGW in `.tools/compiler/`, me3 in `.tools/me3/`, the .NET SDK in `.tools/dotnet/`.
2. Write the path of AoTTG2's folder (the one with `Aottg2.exe`) on the first line of
   `.local/aottg2_dir.txt`.
3. `python tools/build_host.py` and `python tools/build_guest.py`
4. Start Elden Ring with `Play-EldenRing.bat`, load in; start AoTTG2 with `Play-AoTTG2.bat`,
   spawn a soldier in single player, press F7.

## Credits
Built on [Minecraft-Ring](https://github.com/siddoff/Minecraft-Ring) and
[minecraft-crossover-bridge](https://github.com/justbustin/minecraft-crossover-bridge) (MIT).
Lessons from [EldenCraft](https://github.com/wargamereview-ship-it/EldenCraft) and the
[universal-modder](https://github.com/rehan-remade/universal-modder) Elden Ring notes.
See `THIRD-PARTY-NOTICES.md`. MIT licensed (`LICENSE`).
