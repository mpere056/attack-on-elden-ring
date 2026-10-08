# Bridge contract

Check every change against this. "UNDECIDED" items are listed at the bottom.

## Route
Live passthrough (state exchange) + frame compositing. See `DESIGN.md`.

## Programs and exact versions
- Host: Elden Ring, Steam, App Ver. 1.17.1, `eldenring.exe` 2.7.1.0; loader me3 v0.13.0
- Guest: AoTTG2 1.2.3 (Unity 2023.1.22, IL2CPP, metadata v29); loader BepInEx 6.0.0-be.788
- OS: Windows 10 19045, native (no translation layer)

## Ownership
| Thing | Owner | Hand-back |
|-------|-------|-----------|
| Player position and physics | AoTTG2 hero | Toggle key gives the Tarnished back for menus, graces, doors, cutscenes; on return AoTTG2 hero is placed on the Tarnished |
| Camera | AoTTG2 (pose sent to ER, ER renders from it) | ER's own camera while ER drives |
| World geometry and rendering | Elden Ring | n/a |
| Collision ER → AoTTG2 | Elden Ring `CastRay`, sampled on request | n/a |
| Collision AoTTG2 → ER | Not bridged | AoTTG2 objects don't block ER enemies |
| Enemies | Elden Ring | n/a |
| Damage dealt | AoTTG2 blade hit → damage event → ER applies via its own health functions | n/a |
| Damage taken | ER hits the hidden stand-in Tarnished → reported to AoTTG2 | n/a |
| Health and death | One life. ER's death screen and grace respawn are used | AoTTG2 hero re-placed on respawn |
| Inventory | Not bridged. ODM gas and blades are AoTTG2's; ER inventory untouched | |
| Saves | ER: me3 separate save file. AoTTG2: nothing persistent | |
| Menus, pause, loading | ER. AoTTG2 hero frozen, nothing composited while ER is busy | |

## Units and axes
- Distance: both in metres (ER `unitsPerMeter` = 1, Unity 1 unit = 1 m)
- Axes: ER positions are published in a stable per-zone frame. ER is Y-up, left-handed; Unity is
  Y-up, left-handed. **Hypothesis:** mapping is identity plus a zone origin offset. Verify in
  milestone 2 by walking the Tarnished north/east and checking signs.
- Angles: quaternions (x, y, z, w) across the bridge
- Time: ER frame rate (60 cap); AoTTG2 runs its own frame rate, physics at Unity fixed step

## Messages
| Channel | Kind | Direction | Rate | If full or late |
|---------|------|-----------|------|-----------------|
| Header + heartbeats | snapshot | both | per frame | Heartbeat stale > 2 s: the other side is gone (see Lifecycle) |
| Game state (Tarnished, camera, zone) | seqlock snapshot | ER → AoTTG2 | per ER frame | Reader keeps last good value |
| Control (hero pose, camera, flags) | seqlock snapshot | AoTTG2 → ER | per AoTTG2 frame | ER keeps last pose |
| Rays | request/response batch | AoTTG2 → ER → AoTTG2 | on demand, ~2 ms ER budget per frame | Partial batches resume next frame |
| Assistant rays | request/response batch, same layout, 1024 rays at `AOER_OFF_ASSIST_RAYS` | AI assistant → ER → assistant | on demand, ~1 ms ER budget per frame | Partial batches resume next frame |
| Entities | seqlock table | ER → AoTTG2 | per ER frame | |
| Damage | ring buffer | AoTTG2 → ER | events | Writer drops and counts when full |
| Frames | triple-buffered images | AoTTG2 → ER | per AoTTG2 frame | Prefer matching pose, then older, then last; count each |

- Protocol: magic `AOER`, version 1, little-endian, `#pragma pack(4)`
- One schema, two languages: `protocol/aoer_protocol.h` and `protocol/AoerProtocol.cs`, with
  size/offset asserts on both sides.
- Restart detection: PIDs + start times in the header.

## Frames (milestone 6)
- Layers: hero/cables/blades colour + depth, HUD
- Pose matching: AoTTG2 writes a slot, then publishes that frame's pose id; ER renders that pose
- Max resolution: 1920×1200 (inherited); above that, no compositing

## Lifecycle
- Start order: either side may start first; Elden Ring through me3, then AoTTG2.
- AoTTG2 dies or closes: ER takes the Tarnished back, gravity restored, compositing stops.
- ER dies or closes: AoTTG2 hero freezes.
- On loading or zone change: AoTTG2 freezes until ER reports the Tarnished usable, then is
  re-placed on it.

## AI assistant ([Game Assistant](https://github.com/mpere056/game-assistant))
- Reads the state block, the entity table and the header; casts rays only through its own block.
- Writes nothing else into the bridge. From phase 3 it sends input, never memory writes.

## Not covered
- Online anything. AoTTG2 multiplayer is blocked.
- ER NPCs walking around AoTTG2 objects.

## UNDECIDED
- Which AoTTG2 map/scene hosts the hero (an empty custom map is likely best)
- Whether ER enemies become AoTTG2 titans (nape-only) or ordinary hitboxes
- Toggle key for hand-back (F8 inherited from the bridges)
