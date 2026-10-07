# Mod log

## 2026-10-07: project created
- Chose route 1 + 2 (passthrough with compositing). Reasons in `docs/DESIGN.md`.
- Cloned references into `ref/` (ignored): Minecraft-Ring 711015a, minecraft-crossover-bridge
  d172387, EldenCraft (wargamereview) 89e0eb4.
- Wrote AGENTS.md, DESIGN.md, CONTRACT.md, whitelist .gitignore.
- Findings carried in: AoTTG2 has `ConnectOffline`/`EnterOfflineRoom`; its `AnticheatManager` is
  room moderation (vote-kick, ban list, chat filter), not client protection. ER `CastRay` has no
  length cap in the bridge; budget ~2 ms per frame.
