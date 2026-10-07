# AGENTS.md

## Project
Attack on Elden Ring: a passthrough mod that runs Attack on Titan Tribute Game 2 (AoTTG2) hidden
alongside Elden Ring. AoTTG2 supplies the player's ODM gear movement and blade combat; Elden Ring
draws the world, runs the enemies and owns health, death and menus.

## Hard rules, never break these

1. **Never write game assets, decompiled code, or extracted game files into this repository.**
   They stay on this machine, untracked. Generated IL2CPP interop assemblies and dumps go in
   `.local/` (ignored), never in source folders.
2. **`.gitignore` is a whitelist.** It ignores everything and includes only source files. Do not
   switch it to a normal ignore list.
3. **Do not run `git commit` unless the user asked.**
4. **Do not touch anything outside this project folder** unless the user explicitly names the path.
   Game installs are read-only, except where the user has approved installing a loader
   (BepInEx into AoTTG2's `MainApp` folder). Nothing is ever installed into Elden Ring's folder:
   me3 loads our DLL from this project.
5. **Offline only.** Elden Ring runs only through me3 (offline, no Easy Anti-Cheat). AoTTG2 runs
   only in its offline single-player mode; the plugin blocks every Photon connection. Never
   circumvent, patch or probe anti-cheat. AoTTG2's `AnticheatManager` (multiplayer room
   moderation) is not touched.
6. **Never put credentials in the repo.**

## How to work

- **Plan before code.** Design lives in `docs/DESIGN.md`, the bridge rules in `docs/CONTRACT.md`.
  Check every change against the contract.
- **One milestone at a time** (see `docs/DESIGN.md`, "Milestones"). Each one ends with a playtest.
- **Log, don't look.** Both sides log to `runtime/`. Verify from numbers, not screenshots.
- **Tell the user how to test it**: the exact command and what they should see.
- **Explain in plain language.** The user is not the programmer.
- **Credit lineage.** Code adapted from Minecraft-Ring or the CrossOver bridge keeps its MIT notice;
  record the upstream commit in `THIRD-PARTY-NOTICES.md`.

## Honesty

- Untested means "not tested". Never imply verification that didn't happen.
- After two failed real attempts at the same problem, stop and update `docs/STATUS.md`.
- Record dead ends in `MODLOG.md` alongside successes.

## Keep these files updated

- `MODLOG.md`: an entry after every change.
- `docs/STATUS.md`: where things stand, for a fresh session to pick up.
- `README.md`: the "what works / what doesn't" list.

## Environment

- OS: Windows 10 Enterprise 19045, NVIDIA RTX 2060, 16 GB RAM
- Host: Elden Ring, App Ver. 1.17.1 (`eldenring.exe` 2.7.1.0), Steam,
  `C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game`
- Guest: AoTTG2 1.2.3, Unity 2023.1.22, IL2CPP (metadata v29), Photon PUN,
  folder from `.local/aottg2_dir.txt` or `AOTTG2_DIR` (see `tools/paths.py`)
- Host loader: me3 (Mod Engine 3) v0.13.0, `[[natives]]` profile, separate save file
- Guest loader: BepInEx 6 bleeding edge (IL2CPP x64) build 788
- Player authority: AoTTG2 (movement, ODM gear). Elden Ring: world, enemies, HP, death, menus.
- Host language: C++17, built with LLVM-MinGW (clang) in `.tools/`
- Guest language: C# (net6.0), BepInEx 6 IL2CPP plugin, built with a project-local .NET SDK
- Agent: Claude Code
