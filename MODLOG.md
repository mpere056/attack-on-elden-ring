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

## 2026-10-07: milestone 2, AoTTG2 plugin
- `guest/`: AoerBridge BepInEx plugin (Plugin.cs, Bridge.cs, Protocol.cs, OfflineGuard.cs).
- `tools/build_guest.py`, `Play-AoTTG2.bat`. Inspector for interop names lives in `.local/` (not tracked).
- Built and installed into AoTTG2. Not tested in game.

## 2026-10-07: milestone 2 passed
- Both games ran together; positions logged in AoTTG2's BepInEx log; 17 connect methods patched;
  AoTTG2 single player made no connect attempts.

## 2026-10-07: milestone 3, puppet link
- `guest/src/Link.cs`: F7 links the Tarnished to AoTTG2's hero via the control block.
- `Bridge.WriteControl` (seqlock writer), protocol offsets for the control block and hostLife.
- Plugin 0.3.0 built and installed. Not tested in game.

## 2026-10-07: milestone 3 passed; milestones 4 and 5 swapped
- Puppet link works (see STATUS). Collision moved ahead of long-range hooks: with Elden Ring's
  collision copied into AoTTG2, walking and nearby hooks work through AoTTG2's own code.

## 2026-10-07: milestone 4, collision mirror
- `TerrainMirror.cs`, `Rays.cs`; Link gains a preparing phase. Plugin 0.4.0 installed. Not tested.

## 2026-10-07: milestone 4, first test failed (fell through)
- Rays fine: 3x3 tiles in 0.2-0.35 s, 49-56 tiles kept, batches 35-153 ms. Hero still fell
  through: mirror was on layer MapObjectEntities (23), copied from AoTTG2's trees.
- 0.4.1: mirror on `Utility.PhysicsLayer.MapObjectAll`; ground check before switching (ray down
  must hit a tile and the hero's layer must collide with it, otherwise the link is cancelled
  before anything moves); floor/top hit and triangle counts logged.

## 2026-10-07: milestone 4, second test: safety check cancelled correctly
- Copy was right (18 tiles, ~4460 floor hits, ~28k triangles, ground 0.00 m under the hero), but
  hero layer Human (10) does not collide with MapObjectAll (24) in AoTTG2's collision matrix.
- 0.4.2: `ChooseLayer` asks AoTTG2 (Physics.GetIgnoreLayerCollision with the hero's layer, and the
  static `Characters.Hook.HookMask`) and takes the first map layer the hero collides with and
  hooks can catch (MapObjectCharacters first). Candidates are logged.

## 2026-10-07: milestone 4, third test: still cancelled
- Layer matrix says Human (10) collides with none of the map layers, yet the hero stands on
  branches: AoTTG2 must use per-collider/rigidbody include/exclude overrides (Unity 2022.2+).
  Note: the very first test built its first tiles on layer 0 (layer was only set later), so
  MapObjectEntities was never really tried.
- 0.4.3: layer = whatever the hero is standing on (RaycastAll under the feet) if it passes
  the full collide test (matrix + include/exclude of the hero's rigidbody and colliders), then
  map layers, then any layer. Hero physics settings logged. If the ground check still fails,
  F7 links in puppet mode (milestone 3 behaviour) instead of doing nothing.

## 2026-10-07: milestone 4, fourth test: puppet fallback worked
- Hero: one CapsuleCollider on Human (10), no include/exclude overrides. Standing on `tree0`,
  layer MapObjectEntities (23), although the matrix says 10 ignores 23. So AoTTG2 grounds its
  hero with its own queries against map layers; the matrix check was the wrong test, and the
  check ray hit tree0 (same layer, AoTTG2 map still on) instead of our tile.
- 0.4.4: ground check passes if the copy uses the layer under the hero's feet (or the matrix
  collides) and a copied tile (not an AoTTG2 object) is within -1.5..3 m under the hero.

## 2026-10-07: milestone 4, first full-mode run
- Ground check passed (copy on MapObjectEntities, tile 0.02 m under the hero). ~70 s linked:
  climbed from y 94 to 158 over ~140 m on Elden Ring's terrain; 60-80 tiles kept; batches
  7-195 ms; peak hero speed 108 m/s; sent vs reported within a few cm.
- Then fell through while descending a slope near (10974, 128, 9725): quads with corners more
  than 1.2 m apart were skipped, leaving holes on steep rock. Elden Ring reloaded the Tarnished
  (hostLife), which ended the link as designed. User closed both games.
- 0.4.5: no height limit on ground quads; floor falls back to the top ray when that is below the
  player's level; tiles re-sampled at 5 m height change (was 12); rescue net puts the hero back on
  the copy if it is 3-40 m under it (counted and logged).

## 2026-10-07: milestone 4, second full-mode run
- Long run with ODM (peak 57 m/s, 271 batches, 0 rescues), unlinked with F7 at the end.
- Fall-through analysed live with erctl rays: a pit (ground ~64) beside ground at ~92. From the
  pit, the ceiling ray started inside the hill and hit the hill's top from below, so the pit wall
  became a 1 m slab at the top (hollow wall); the hero walked into the hill. The rescue net used
  the same wrong floor, so it never fired.
- 0.4.6: a column rising above the player's level is solid from far below to its top unless
  there is open ground at the player's level with >1.8 m headroom under the ceiling hit (a real
  overhang); solid columns use their top as the floor (mesh + rescue). Solid columns counted.
- 0.4.6: Elden Ring's camera follows AoTTG2's camera while linked (ERMC_CTRL_OVERRIDE_CAMERA:
  eye, eye + forward, up, vertical FOV), so steering matches the view. User feedback that drove
  this: controls felt clunky because the views differed.
- Known, by design until milestone 6: the Tarnished glides (puppet, no animation); it will be
  hidden and the AoTTG2 soldier drawn instead.
- Play-AoTTG2.bat now copies dist/guest/AoerBridge.dll into the plugins folder before starting
  (the DLL is locked while AoTTG2 runs).

## 2026-10-07: milestone 4, third full-mode run (0.4.6): good
- User: "everything is working way better", camera sync "so much better", lots of ODM use, no
  fall-through. 5 rescues at ~(11135, 9679): false positives in a cave. The area was sampled from
  outside (higher yRef) so its columns were solid; walking down into the cave put the hero 26-28 m
  "under" them. Peak speed 502 m/s is the rescue teleport, not real movement.
- Live entity table check: 24 enemies within ~80 m, world-aligned hitboxes, hp/maxHp present.
- 0.4.7: rescue only while falling (vertical speed below -6 m/s).
- 0.4.7: `EnemyProxies.cs`: a BoxCollider per published Elden Ring enemy (hitbox centre and size,
  rotation unless world-aligned), on the terrain copy's layer so hooks catch them, updated every
  Elden Ring tick, removed when gone or dead. Not tested.
- Still by design until milestone 6: the Tarnished glides (puppet, no animation).

## 2026-10-07: milestone 4, enemies hookable; caves still wrong
- User: hooking enemies works. Caves better but: "walking up an invisible wall" and rising through
  terrain in enclosed spaces; unlinked with F7.
- Live analysis in the cave (Tarnished at 11120.9, 86.9, 9680.0): roof ~91-94, floor dropping to
  64-82 in hollows, real rock walls 4-6 m away. Old rule: 315 of 625 columns "solid" (whole cave
  floors), and solid columns were joined to the floor by ramps the hero walked up. Two-step rule
  (ceiling ray starting 0.2 m above the found floor): 21 solid.
- 0.4.8/0.4.9:
  - tiles: floor + top first, then ceilings from just above each floor; a column is solid only
    with no floor, or with < 1.8 m headroom; no ramps from a solid column to a neighbour > 1.2 m
    lower; 4 tiles per batch.
  - `WallProbe.cs`: every 0.1 s, 48 directions x 3 heights (0.7/1.3/1.9 m), 20 m; a hit is a wall
    when the next ray up stops < 0.35 m further (top ray: compared with the one below, or nothing
    below); each wall hit becomes a thin pooled BoxCollider panel. Live check in the cave: 66 wall
    hits (walls 2-16 m around), 45 slope hits skipped. A first version that compared hits with the
    copied floor would have skipped every hit (floor of a rock column is its top, above the hit).

## 2026-10-07: caves good; milestone 6 started (host); milestone 5 built
- User: caves "much better", one wall clip somewhere; asked for animation, crosshair and visible
  hooks next, then for milestone 5.
- Milestone 6, host: compositor on (AOER_COMPOSITOR=1); frames via named mapping
  `Local\AoER_frames_v1` created by the guest, capped at 2560x1440 (~177 MB, was 3840x2160 file).
  Hot-reloaded into the running game with new `erctl.py reload`: "native Windows D3D12 Present
  and command queue hooks installed", swapchain 1280x720. `erctl.py testpattern on` + `erctl.py
  shot` (PIL screenshot of the Elden Ring window rect): checkerboard visible top-left; ~94 fps,
  composite 0.37 ms. build_host.py now relinks the core first and tolerates a locked loader.
- Milestone 5 (0.5.0): `LongHooks.cs` postfix on Characters.Hook.SetHooking; ray along
  base + relative velocity, 150 m, through the debug single-ray channel (ERMC_CMD_RAYCAST, custom
  filter 0x5D); a 2 x 2 x 0.5 m anchor on the copy's layer at the hit, 8 s life. Channel tested live:
  11-16 ms per ray. Not tested in play.

## 2026-10-07: milestone 6, guest side; first test crashed AoTTG2
- 0.6.0 `Compositor.cs`: on link (full mode) AoTTG2's camera clears to magenta, fog and post
  effects off, existing non-hero renderers hidden, window resized to Elden Ring's back buffer;
  WaitForEndOfFrame coroutine reads the back buffer (HUD included), keys out magenta into
  premultiplied BGRA, writes the GUI layer of `Local\AoER_frames_v1` (world + depth zeroed);
  control gets COMPOSITE | HIDE_HUNTER; F6 toggles. Tarnished hidden while compositing.
- Test: link, collision, hiding (77 renderers, 1 post effect) and 800x600 -> 1280x720 all fine,
  then AoTTG2 crashed in `Texture2D.GetRawTextureData_Injected(_tex.Pointer)`: AccessViolation.
  The injected call takes Unity's native object handle, not the interop pointer.
- 0.6.1: `GetPixels32()` (managed Il2Cpp Color32 array, same RGBA byte layout) instead.
- User asked whether the Tarnished could stay, with Elden Ring's own animations. Answer: possible
  (needs reverse engineering how Elden Ring requests animations; none in the bridge yet), but Elden
  Ring has no ODM/blade animations; plan a "Tarnished mode" toggle after soldier mode works.

## 2026-10-07: milestone 6, second test: linked, but nothing reached Elden Ring
- 0.6.1 linked fine; AoTTG2 showed only the soldier, HUD and minimap on magenta (user screenshot).
  But "composited 0": BepInEx warned the WaitForEndOfFrame coroutine had an "unsupported return
  type"; it never ran. Host: "missing 300" frames. SetResolution also re-centred AoTTG2's window
  over Elden Ring.
- 0.6.2: no coroutine. Each Update publishes the previous frame's capture and calls
  ScreenCapture.CaptureScreenshotIntoRenderTexture for this frame (one frame late, HUD included);
  rows flipped by default (Direct3D capture is upside down per Unity docs; to verify by screenshot).
- 0.6.2 `Overlay.cs`: while compositing, AoTTG2's window becomes borderless, layered at 1/255
  opacity, topmost, exactly over Elden Ring's client area (follows it), and gets the focus, so
  input goes to AoTTG2 while you look at Elden Ring. Restored on F6/F7.

## 2026-10-07: milestone 6 third test; switched to Tarnished mode
- 0.6.2: overlay worked (AoTTG2 window invisible over Elden Ring, input fine), guest published
  6670+ frames (read 2.7 ms, key 1.4 ms), but the host showed "missing" for every frame: it had
  mapped `Local\AoER_frames_v1` at 15:50 during the crashed 0.6.0 session and kept that orphaned
  section; the restarted AoTTG2 created a new one under the same name (live peek: slots valid,
  poseIds 8047-8049, control mcFrame 8051; host gpu word 0 = never uploaded). Fix not done yet:
  re-open the mapping when latestFrameId stalls for 2 s and reset g_lastUploaded.
- User decision: work on Tarnished mode instead (no toggle for now; soldier mode later).
- Tarnished mode v1 (host core hot-reloaded, plugin 0.7.0):
  - protocol: ErmcControl grows to 0x70 with stickX, stickY, padButtons; flag
    ERMC_CTRL_VIRTUAL_PAD (1<<10).
  - host `input.cpp`: MinHook on xinput1_4!XInputGetState (Elden Ring has XINPUT1_4, DINPUT8,
    HID loaded); while the flag is set and control frames are fresh (< 500 ms), pad 0 gets the
    virtual left stick added (reported connected if no real pad).
  - stand_in in pad mode: NoMove cleared (the game's locomotion runs and animates), position still
    pinned every tick, facing left to the game.
  - guest: stick = hero ground velocity relative to the camera's flattened forward; tilt 0.35 at
    0.5 m/s rising to 1.0 at 5 m/s; centred in the air or below 0.5 m/s. Soldier drawing no longer
    starts on link; F6 removed (Compositor/Overlay code kept, unused).

## 2026-10-07: Tarnished mode v1 test: idle only, facing frozen; a display driver reset
- User: Tarnished stays in idle and never turns; at the end something crashed, the monitor went
  dark and came back (likely a display driver reset; host.log has no fault, nothing in runtime/).
- Logs: guest sent stick tilts (0.55 at 2 m/s, 1.0 running) with VIRTUAL_PAD; host never logged
  "virtual controller active": Elden Ring never called the hooked XInputGetState during the link.
  eldenring.exe imports xinput1_4 by ordinal 2 and 3 = XInputGetState / XInputSetState (verified
  same addresses as the named exports). No real controller is connected (pad 0 -> 1167).
- Facing froze because pad mode stopped writing the quaternion while the game got no input.
- Now (core only, not yet tested): XInputGetCapabilities hooked to report pad 0 connected while the
  virtual pad is wanted; 5 s diagnostics (calls per pad, caps calls, virtual stick served, whether
  Elden Ring's window has the focus); pad mode (NoMove off, no facing write) only once the game
  actually reads the virtual pad.

## 2026-10-07: virtual pad diagnosis; crosshair
- Diagnostics: Elden Ring called XInputGetState only at start-up (pad0 x4, pads1-3 x12, i.e. 4
  probe rounds), then 0 calls per 5 s for the whole link; GetCapabilities 0; its window was never
  focused. So it stops polling when no pad is present at start-up. Facing is fine again (pad mode
  now only once the game reads the pad).
- Host (built, needs an Elden Ring restart): while the pad is wanted and unpolled, post
  WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to the game window every 5 s, so it re-probes; with
  GetCapabilities reporting pad 0 connected it should start reading it. Untested.
- Crosshair: ERMC_CTRL_CROSSHAIR (1<<11) and aimDist (control 0x74 now); the compositor draws
  four ticks and a dot at the screen centre without needing any AoTTG2 frames (same path as the
  test pattern), green when aimDist > 0. Guest 0.7.1 raycasts from the camera centre against the
  collision copy and enemy boxes (120 m). Shaders compile-checked offline with d3dcompiler_47
  (.local/shadercheck.py). First attempt used `cross` as a variable name (an HLSL intrinsic).
- A hot reload at 16:29:26 got "core: shutdown" and no more log; Elden Ring was not running
  afterwards. The log shows "alive -> none" at 16:29:18, so the game may already have been closing.
  Unclear whether the reload caused it. No crash file.

## 2026-10-07: two NVIDIA driver resets traced; compositor off; crosshair as an overlay window
- Windows event log: "Display driver nvlddmkm stopped responding and has successfully recovered"
  (4101, with nvlddmkm 153) at 16:15:30 and 17:11:02; Elden Ring then faulted in nvwgf2umx.dll
  (17:11:42). Both happened only after the D3D12 compositor was enabled (15:30); ~2 h of earlier
  two-game testing with it off had none. The second came as the user spawned in AoTTG2 (VRAM
  pressure on an RTX 2060 may contribute). Suspect: the compositor's per-frame hooks on Elden
  Ring's command queue (ExecuteCommandLists / ResourceBarrier / Reset, depth tracking).
- Elden Ring also faulted in eldenring.exe at 16:29:26, the exact second of the hot reload: the
  reload caused that crash. Policy now: no hot reloads into a running game; restart instead.
- Earlier eldenring.exe faults at 14:49, 14:53, 15:45 line up with the user closing the game:
  likely a crash on exit with the bridge loaded. To look into.
- AOER_COMPOSITOR back to 0. Plugin 0.7.2: `CrosshairWindow.cs`, a 33 px layered, click-through,
  topmost, no-activate window of our own over the centre of Elden Ring's client area (white, green
  when the camera-centre ray hits the copy or an enemy box within 120 m). It never touches Elden
  Ring's rendering. Untested.

## 2026-10-07: crosshair works; virtual pad dead end; animation by request; cables overlay
- User: crosshair window works (white/green correct), ODM controls "great". No blackouts with the
  compositor off. Still idle-only animation; no controller prompts.
- Host log: 38 WM_DEVICECHANGE announcements, still 0 XInputGetState calls. The virtual pad route
  is dropped (code kept, inert).
- New route from fromsoftware-rs (vswarte, 59fbd3b, MIT; cloned to ref/): ChrInsModuleContainer
  (data 0x00, time_act 0x18, event 0x58, physics 0x68 = matches the host's kModPhysics),
  CSChrEventModule.request_animation_id (+0x18, "override animation to play next frame") and
  idle_anim_id (+0x1C), CSChrTimeActModule anim queue (+0x20, 10 x 16 bytes) and read_idx (+0xC4).
- Host (needs a restart): state grows to 0x120 with animId / animRequest / idleAnimId; control grows
  to 0x78 with requestAnim (>0 play, 0 back to idle, -1 leave alone). drive_animation in the
  stand-in re-requests only when the current animation differs. Every module pointer is validated
  by its owner field (+0x08 == the ChrIns) before use.
- `tools/erctl.py anim` + `Anim-Recorder.bat`: logs each animation change with ground and vertical
  speed, to learn this character's walk/run/sprint/jump/fall ids. Plugin sends -1 for now.
- Plugin 0.7.3: `CableOverlay.cs`, a click-through layered window over Elden Ring's client area
  drawing grey lines from the hero's waist to each hook that is flying or attached
  (HookUseable.IsHooking/IsHooked, GetHookPosition), projected with the shared camera and
  Elden Ring's aspect. Untested.

## 2026-10-07: cables work; animation ids recorded; Tarnished animations by request
- User: grey cable lines go out correctly (CableOverlay works).
- `runtime/anim-20261007-175157.csv` (80 changes): standing 20120 (0 at start); jog 22100
  (~3 m/s); sprint 20220 (held 4 s); roll 27120; jump 202020 -> 202040 (air) -> 202115 (land);
  ledge fall 4050 -> 4000 -> 4220 (hard landing). Event idle_anim_id is 63000 (never seen while
  standing in the world).
- Plugin 0.8.0 `TarnishedAnim.cs`: grounded speed bands with hysteresis (stand < 0.4, jog,
  sprint > 5.5 / back to jog < 4.5 m/s); airborne after 0.15 s off the ground -> 202040. Requests
  are sent in control.requestAnim; the host re-requests only when the current animation differs.
  Untested: whether the event override plays while the stand-in sets NoMove, and whether the
  locomotion animations loop on their own.

## 2026-10-07: event-animation route does not drive locomotion
- 0.8.0 test: plugin picked Stand/Jog/Sprint/Air correctly; host wrote 48 requests; every log line
  says "was 0" and the user saw only idle.
- `tools/ermem.py` (debug mailbox READ/WRITE, player modules validated by owner): event and
  time-act modules found where fromsoftware-rs says. Unlinked, a written request (27120, roll) is
  consumed (field back to -1) and copied into the next field (+0x1C, "idle_anim_id"), while the
  time-act queue keeps showing anim 0 (length 3.0, the standing idle after loading). The +0x1C
  value had become 20120 after the link: our Stand requests. So this field pair behaves like an
  event/idle override, not "play this animation now"; locomotion ids (2xxxx) are not played.
  Earlier during normal play +0x1C was 63000.
- Conclusion: forcing Elden Ring's locomotion animations needs more reverse engineering (the pad
  manipulator's movement vector, or the behaviour/HKS request path). Options put to the user.

## 2026-10-07: option B feasibility build (play from Elden Ring's window)
- User chose option B for a small feasibility test and wants Elden Ring's attacks and rolls kept.
- Plugin 0.9.0 `BackgroundInput.cs`: active while linked (full mode) and the foreground window
  belongs to Elden Ring's process (Bridge.HostPid). Harmony prefixes on Settings.InputKey
  GetKey/GetKeyDown/GetKeyUp (all AoTTG2 keybinds) answer from GetAsyncKeyState with per-frame
  edges and modifier support (Unity KeyCode -> VK table); Input.GetAxis/GetAxisRaw for
  "Mouse X/Y/ScrollWheel" answer from raw mouse input (message-only window, RIDEV_INPUTSINK, 0.1
  per pixel / per notch, Y negated); Input.GetMouseButton*/Application.isFocused answer as focused.
  Once the raw reader runs, mouse axes always come from it (one raw-input registration per process).
- Event-animation requests switched off (requestAnim -1).
- Host (needs a restart): ERMC_CTRL_GAME_INPUT (1<<12): stand-in leaves NoMove off and stops
  writing facing, so Elden Ring's own movement animates the Tarnished; position still pinned.
- Untested: whether AoTTG2's keybinds and camera really follow, whether pinning fights Elden Ring's
  root motion (jitter), how jump/ODM keys interact with Elden Ring's own bindings.

## 2026-10-07: option B works
- 0.9.0: patches installed and switching worked, but the hero didn't move from Elden Ring's window.
- 0.9.1 counters (5 s windows, Elden Ring in front): KeybindSetting 10k-13k calls (110-636
  answered pressed), InputKey 253-300, Unity Input.GetKey 759-900, axes 759-900, mouse buttons 0,
  isFocused 0. So AoTTG2 reads keybinds through KeybindSetting.GetKey/Down/Up(bool), with InputKey
  largely inlined into it; patching that layer made movement and hooks work.
- User: Tarnished animations play while the character moves; hero up to 36 m/s; 9 hooks fired
  from Elden Ring's window. "Kinda clunky but working". Elden Ring attacks/rolls left on.
- Likely clunkiness: Elden Ring's root motion moves the Tarnished between our pins (sawtooth),
  run animation slower than AoTTG2's speed (foot sliding), ground animations while airborne.
  Candidate fixes from fromsoftware-rs: CSChrBehaviorDataModule.hks_root_motion_mult (0 = no root
  motion) and hks_animation_speed_multiplier (match AoTTG2 speed).

## 2026-10-07: option B feel: foot sliding, running in mid-air, facing
- User: foot sliding (hero much faster than Elden Ring's run cycle), running/walking animation in
  mid-air, and odd facing from Elden Ring's window (fine when AoTTG2 is focused).
- Facing cause (likely): Elden Ring turns toward its input relative to its own internal camera; we
  only replace the rendered camera, so its "forward" drifts. Fix: the stand-in always writes the
  hero's facing in game-input mode.
- fromsoftware-rs offsets, cross-checked (physics 0x50/0x70/0x91 match the host's constants):
  physics motion_multiplier 0x1C4, is_falling 0x1D0; behaviour module (slot 0x28)
  animation_speed 0x17C8; fall module (slot 0x70) fall_timer 0x18.
- Host (restart needed): game-input mode sets motion_multiplier 0 (animations don't move the
  Tarnished), animation_speed = control.animSpeed on the ground; with ERMC_CTRL_AIRBORNE (1<<13):
  NoMove back on (no ground locomotion), is_falling = 1, fall_timer kept at 0. Restored to 1 on
  release. Control grows to 0x7C with animSpeed.
- Plugin 0.9.2: animSpeed = clamp(hero ground speed / 3.2 m/s, 0.6, 3.0); airborne after 0.15 s
  off the ground. Untested: whether these writes survive Elden Ring's own per-frame updates.

## 2026-10-07: walking/running/facing good; mid-air via Elden Ring's own jump
- User: walk/run animations "look great", facing fixed. In the air the Tarnished just idles (the
  is_falling write doesn't take), landing has no animation, no unexpected deaths. Pressing F
  (Elden Ring's jump) mid-ODM plays a jump animation that "looks very fitting".
- Plugin 0.9.3: while Elden Ring has the focus and the hero is airborne, tap F into Elden Ring
  with SendInput (scan code 0x21) when the hero leaves the ground and again whenever the
  Tarnished's animation is outside the jump range (202000-202999) while still airborne, at most
  once per second. The tap is masked from AoTTG2's background key reader for ~160 ms. Elden
  Ring's own landing should follow from its jump state. Only AoTTG2 needs a restart.
