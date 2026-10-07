# Compatibility and dependencies

Everything Attack on Elden Ring was built and tested with, with exact versions. Other versions are
untested; the Elden Ring side refuses to run on any other game build, because it relies on addresses
in that exact executable.

**Offline only.** Elden Ring is started through me3 without Easy Anti-Cheat and with its own save
file. AoTTG2 runs in single player; the plugin blocks every multiplayer connection.

## Games (you need your own copies)

| Game | Version tested | How to check | Where to get it |
|------|----------------|--------------|-----------------|
| **Elden Ring** (Steam, app 1245620) | **App Ver. 1.17.1**: `eldenring.exe` file/product version **2.7.1.0**, Steam build **25080141** | Right-click `eldenring.exe` > Properties > Details; or the title screen | [Steam](https://store.steampowered.com/app/1245620/ELDEN_RING/) |
| **Attack on Titan Tribute Game 2** (AoTTG2) | **1.2.3** (`Release\MainApp\App_version.data`), launcher **1.0.2**, Unity **2023.1.22f1**, IL2CPP (metadata v29) | `App_version.data` next to `Aottg2.exe` | [aottg2.itch.io/aottg2](https://aottg2.itch.io/aottg2) |

Notes:
- Tested with the Shadow of the Erdtree DLC files installed (`sd_dlc02`); not tested without them.
  Elden Ring's `regulation.bin` SHA-256 began `766521F9508DE3A3`.
- AoTTG2's launcher updates the game. Start `Aottg2.exe` directly (`Play-AoTTG2.bat` does), or an
  update may replace the files BepInEx and the plugin depend on.

## Loaders (installed by you; not included in this repository)

| Project | Version | Download used | SHA-256 | Licence |
|---------|---------|---------------|---------|---------|
| [me3 (Mod Engine 3)](https://github.com/garyttierney/me3) | **v0.13.0** (commit 989b522) | [me3-windows-amd64.zip](https://github.com/garyttierney/me3/releases/tag/v0.13.0) | `a8b693c574106f20532ab1e8b58b2452f37370679b938a884fe75f87f9b68c2b` | MIT / Apache-2.0 |
| [BepInEx](https://github.com/BepInEx/BepInEx) (bleeding edge, IL2CPP) | **6.0.0-be.788** (commit 5b766a3) | [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip) | `f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a` | LGPL-2.1 |

- me3 starts Elden Ring offline and loads `dist/aoer_host.dll` through `me3/aoer.me3`. Nothing is
  copied into Elden Ring's folder.
- BepInEx is extracted into AoTTG2's `Release\MainApp` folder. On first start it downloads Unity's
  base libraries for 2023.1.22 from `unity.bepinex.dev` and generates interop assemblies with its
  bundled Cpp2IL and Il2CppInterop; it runs plugins on its bundled .NET **6.0.7** runtime.

## Build tools (only needed to build from source)

| Tool | Version used | Link | Used for |
|------|--------------|------|----------|
| LLVM-MinGW (UCRT, x86_64) | **20261006** (clang 23.1.3), SHA-256 `317492c456aa27ee607a5919f1d2d38dcdc1112516a24d0bf4b00d078f52d17a` | [mstorsjo/llvm-mingw](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261006) | `tools/build_host.py`: the Elden Ring DLLs |
| .NET SDK | **8.0.425**, installed with Microsoft's [dotnet-install.ps1](https://dot.net/v1/dotnet-install.ps1) (`-Channel 8.0`) | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) | `tools/build_guest.py`: the AoTTG2 plugin (targets net6.0; restores the NuGet reference pack Microsoft.NETCore.App.Ref **6.0.36**) |
| Python | **3.12.10** | [python.org](https://www.python.org/downloads/) | build scripts and `tools/erctl.py`, `tools/ermem.py` |
| Pillow (optional) | **12.3.0** | [pypi.org/project/pillow](https://pypi.org/project/pillow/) | `erctl.py shot` screenshots only |
| Git | 2.55.0 | [git-scm.com](https://git-scm.com/) | |
| GitHub CLI (optional) | 2.102.0 | [cli.github.com](https://cli.github.com/) | publishing only |

The scripts expect LLVM-MinGW in `.tools/compiler/`, the .NET SDK in `.tools/dotnet/`, me3 in
`.tools/me3/`, and AoTTG2's `MainApp` path on the first line of `.local/aottg2_dir.txt` (or
`AOTTG2_DIR`). None of these are part of the repository.

## Libraries and code in this repository

| Project | Version / commit | Licence | Where |
|---------|------------------|---------|-------|
| [Minecraft-Ring](https://github.com/siddoff/Minecraft-Ring) | 711015a | MIT | `host/` started from its `bridge-base/elden-ring/er-bridge` (see `host/LICENSE-upstream.txt`) |
| [minecraft-crossover-bridge](https://github.com/justbustin/minecraft-crossover-bridge) | d172387 | MIT | the original Elden Ring bridge that Minecraft-Ring ports |
| [MinHook](https://github.com/TsudaKageyu/minhook) | v1.3.4 (vendored via Minecraft-Ring) | BSD-2-Clause | `host/third_party/minhook/` |

## Projects used as references (no code copied)

| Project | Version / commit | Licence | What for |
|---------|------------------|---------|----------|
| [fromsoftware-rs](https://github.com/vswarte/fromsoftware-rs) | 59fbd3b | MIT | Elden Ring structure layouts: character module container, event / time-act / behaviour / fall modules, physics `motion_multiplier` and `is_falling` |
| [EldenCraft (wargamereview-ship-it)](https://github.com/wargamereview-ship-it/EldenCraft) | 89e0eb4 | MIT | me3 profile setup, input-hooking approach |
| [universal-modder](https://github.com/rehan-remade/universal-modder) | Elden Ring knowledge notes | | gravity / fall-death behaviour when moving the player by code |
| [AI Game Modding Guides](https://github.com/trevaintdead/ai-game-modding-guides) | | | overall approach (passthrough route 1 + 2) |

## Test machine

Windows 10 Enterprise 22H2 (10.0.19045), NVIDIA GeForce RTX 2060 (driver 32.0.15.6590), 16 GB RAM.
Both games windowed at 1280 x 720 side by side or with Elden Ring in front.

## Not used

- **ReShade** and the D3D12 compositor (soldier mode): the compositor is in `host/` but switched off
  (`AOER_COMPOSITOR 0`), because its hooks coincided with display-driver resets on the test machine.
- **Easy Anti-Cheat**: never touched. The Elden Ring DLL refuses to run if it is loaded.
