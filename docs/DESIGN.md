# Design

Route 1 (live passthrough) with route 2 (frame compositing), from the AI Game Modding Guides,
guide 14. Both real games run at once.

## The picture

```
 Elden Ring (host, offline via me3)                 AoTTG2 (guest, hidden, offline)
 ┌───────────────────────────────┐                  ┌───────────────────────────────┐
 │ aoer_host.dll (C++)           │   shared memory  │ AoerBridge.dll (BepInEx 6, C#)│
 │  - publishes Tarnished, camera│ <--------------> │  - drives AoTTG2's hero       │
 │  - casts rays on ER collision │  Local\AoER_v1   │  - asks ER for hook anchors   │
 │  - enemies, damage, death     │  Local\AoER_fr_v1│  - builds colliders from rays │
 │  - draws AoTTG2's frame (D3D12│                  │  - renders hero/cables/HUD    │
 │    Present hook, depth test)  │                  │    with ER's camera pose      │
 └───────────────────────────────┘                  └───────────────────────────────┘
```

## Where the code comes from

- `host/` starts from [Minecraft-Ring](https://github.com/siddoff/Minecraft-Ring)'s
  `bridge-base/elden-ring/er-bridge` (commit 711015a), itself a Windows port of
  [minecraft-crossover-bridge](https://github.com/justbustin/minecraft-crossover-bridge)
  (commit d172387). It already has, for `eldenring.exe` 2.7.1.0: signature-checked addresses, a
  game-thread task, camera override, `CSPhysWorld::CastRay`, the enemy table, damage, the hidden
  stand-in, shared death, interactions, and a native D3D12 compositor with depth occlusion.
- Changes we make to it:
  1. **Loaded by me3** (`[[natives]]`), not by a `dinput8.dll` in the game folder. The persistent
     loader keeps its hot-reload role but has no DirectInput exports.
  2. **Named shared memory** (`Local\AoER_v1`) instead of a file, so nothing has to agree on paths.
  3. Minecraft-specific pieces (terrain columns, passages tuned for blocks, block persistence) are
     replaced by what AoTTG2 needs: single hook rays, a collision sampler that runs ahead at ODM
     speeds.
  4. **Gravity off while AoTTG2 drives the Tarnished.** Learned from the CS2 conversion on this
     exact build: writing positions every frame while the game's own gravity runs builds hidden
     fall speed and triggers a scripted fall death on landing. Fix: set
     `CSChrPhysicsModule.gravity_multiplier = 0` and `gravity_disabled = true` while driving,
     restore when Elden Ring drives, apply our own fall damage.
- `guest/` is new: a BepInEx 6 IL2CPP plugin for AoTTG2.

## Milestones

Each ends with a playtest by the user. Do not start the next before the last one passes.

| # | Goal | Proves |
|---|------|--------|
| 0 | Toolchains, me3 and BepInEx installed; vanilla me3 launch and vanilla BepInEx launch both work | Loaders work on this PC |
| 1 | **Raycast range test**: host DLL loads under me3, logs, and answers rays from `tools/erctl.py` | Whether ER has collision far enough away for ODM hooks. Decides if route 1 is viable |
| 2 | AoTTG2 plugin loads, forces offline, opens the shared memory, logs the Tarnished's position | The link works |
| 3 | AoTTG2's hero position drives the Tarnished (gravity off) | Ownership works |
| 4 | AoTTG2 hooks anchor on ER rays | ODM gear in the Lands Between |
| 5 | Collision around and ahead of the player | Flying without clipping |
| 6 | AoTTG2's hero, cables, blades and HUD composited into ER's frame | It looks like one game |
| 7 | Blade hits damage ER enemies; ER hits hurt the AoTTG2 hero | Combat |
| 8 | (Optional) AoTTG2 titans in the Lands Between | The full fantasy |

## Known risks

1. **Collision reach** (milestone 1). Elden Ring only has Havok collision for streamed-in map
   tiles. Distant towers may have none.
2. **ODM speed vs collision sampling** (milestone 5). Sample ahead along velocity; ValCraft's
   hold-and-keep-momentum trick from guide 16 is the fallback.
3. **Hidden Unity rendering and input** (milestone 6). AoTTG2's window must not need focus; input
   comes through shared memory.
4. **Performance.** RTX 2060 + 16 GB running both games. Measure from milestone 6.
