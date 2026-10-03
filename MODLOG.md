# MODLOG — Hornet in Casualties: Unknown (passthrough content port)

Journal of the build. Anything not written here is lost.

## Idea (one sentence)
Play **Casualties: Unknown** as **Hornet** from **Hollow Knight: Silksong** — her real sprites and
animations, sourced at runtime from the user's own Silksong install, plus her moveset reimplemented on
CU's player controller.

## Intake / decisions (2026-10-02)
- **Route:** staged *content passthrough* (Pattern 1 mashup), NOT a live two-process render pass to start.
  - Stage 1: CU BepInEx plugin reads Hornet sprite atlas + animation clips from the local Silksong
    install at runtime and drives a Hornet sprite on the CU player.
  - Stage 2: reimplement Hornet's moveset (needle throw, dash, cling, pogo, Silk/bind) by Harmony-patching
    CU's player controller.
  - Stage 3 (optional, the true SkyCraft-style live link): hidden Silksong process streams Hornet's real
    animator state / frames over shared memory. Only if Stage 1-2 leave something missing.
- **Host game:** Casualties: Unknown **Demo** (installed), single-player/offline. Full game not out yet.
- **Project dir:** `%PROJECT_DIR%` (this). Decompiled code kept OUTSIDE the repo at `%DECOMP_DIR%\`.
- **Human owns both games.** Silksong and CU Demo are both installed locally. No assets are redistributed;
  the plugin reads the user's own install.
- **Verification:** agent may launch / drive / screenshot CU single-player offline, and read BepInEx logs.
  Always ask before taking over the mouse/keyboard.
- **Done means:** Hornet is playable as the CU player in the real demo, verified on screenshot + video,
  with an installable plugin zip and a field note.

## Environment (verified 2026-10-02)
-  Working from this repo (`%TOOLKIT_DIR%`).
- **Casualties: Unknown Demo**
  - Path: `%CU_GAME_DIR%`
  - Unity **2022.3.62f3**, **Mono** backend (`CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll`, 879 KB).
  - BepInEx **5** already installed (`winhttp.dll`, `doorstop_config.ini`, `BepInEx/plugins`).
  - Other community mods are already installed. Do NOT disturb them; our plugin goes in its own subfolder.
  - 2D pixel art. Managed dir has `Unity.2D.Animation.Runtime`, `Unity.2D.IK.Runtime`,
    `Unity.2D.PixelPerfect`, `Unity.2D.SpriteShape.Runtime`.
  - app.info company/product: `Orsoniks` / `CasualtiesUnknown`
    → log/saves under `%SAVE_DIR%\`.
  - In-game **console** exists (used by other mods: `structure`, `togglemorestructures`, ...).
  - Modding docs: https://casualtiesunknown.miraheze.org/wiki/Modding ; library: CUCoreLib
    (https://cucorelib.jimmyking.dev). Template: Moss-Template.
- **Hollow Knight: Silksong**
  - Path: `%SILKSONG_DIR%`
  - Unity **6000.0.50f1**, **Mono** (`Assembly-CSharp.dll`, 7.0 MB), BepInEx 5 installed.
  - Uses **tk2d** sprite system (`TeamCherry.TK2D.dll`). Hornet's controller class is **`HeroController`**
    with `AnimCtrl` / `tk2dSpriteAnimator`.
  - Referenced precedent: `jakobhellermann/hkmod-HornetInHallownest` — brings Hornet into HK by requiring a
    Silksong install and reusing its assemblies at runtime (our legal model).
- **Toolchain present:** the local toolchain.
  `uv` not on PATH but `bin/um` works from the toolkit repo.
- Disk: **(machine details omitted)** — keep artifacts small; decomp on %DECOMP_DIR% is fine (small).

## References
- SkyCraft design + protocol (studied): `docs/DESIGN.md`, `protocol/skycraft_protocol.h` in
  https://github.com/chasmlol/SkyCraft — shared-memory seqlock + SPSC event rings + shared GPU textures.
- Toolkit passthrough example: `examples/minecraft-gta5-passthrough`; note
  `knowledge/games/gta-v/minecraft-passthrough.md`.

## Log / next steps
- [x] Recon: identify engines, loaders, paths, modding scene.
- [x] Create project dir + this journal.
- [x] Decompile CU + Silksong `Assembly-CSharp.dll` (ilspycmd) to `%DECOMP_DIR%\{cu,silksong}`.
- [x] Set up the BepInEx plugin csproj; builds + auto-deploys to the game.
- [ ] Vertical slice: show a Hornet sprite on the CU player (idle), then one animation.
- [ ] Widen: movement set, then moveset.
- [ ] Verify in the demo + record.

## CU source-of-truth notes (read from decomp)
- `Body` (`Body.cs`, ~3200 lines) is the player. It is a **2D physics ragdoll**: `Limb[] limbs`
  (`Rigidbody2D` + `HingeJoint2D` + `SpriteRenderer` per limb), plus IK (`IKHandle`, `IKSegment`) and
  `Animator bodyAnimator` / `AnimationClip idleClip` / `Animator armsAnimator`.
  - `Body.baseLimb` is the torso/head (LimbNum: Head=0, UpTorso=1, DownTorso=2, ArmF=3, ArmB=6, LegF=9, LegB=12).
  - Facing flag: **`Body.isRight`** (bool). Movement: `Body.moveDir` (Vector2), `Body.rb.velocity`,
    `Body.grounded`, `Body.standing`, `Body.crouching`, `Body.currentClimbable`, `Body.jumpSpeed`,
    `Body.actualMoveForce`, `Body.actualJumpSpeed`.
  - Input lives in **`PlayerCamera.Update()`** (line 1485, private): reads `KeyBinds.GetBind("left"/"right"/
    "up"/"down"/"jump"/"attack"/"throw"/"ragdoll"/"altview"/...)` and writes `body.moveDir`, `body.crouching`.
  - `PlayerCamera.main` is the singleton; `PlayerCamera.main.body` gives the `Body`.
  - **Hook used:** Harmony postfix on `PlayerCamera.Update` attaches/finds `HornetAvatar`.
- `Limb.Awake()` disables `animLimb`'s SpriteRenderer (line 462); limb visuals come from species mods
  (`CustomBodySprites`). There is a whole custom-species ecosystem (`CUCoreLib`, `CustomSpecies`, `SkinDeep`,
  `Tailor`). We deliberately don't fight it: we overlay Hornet on the ragdoll.
- Custom content/console: `ConsoleScript.cs` handles an in-game console (keybind `console`), custom binds,
  and no-clip (`PlayerCamera.main.body.moveDir`). Good oracle for scripted verification.

## Silksong source-of-truth notes
- Unity **6000.0.50f1**, Mono. Content is in **Addressables**: `Hollow Knight Silksong_Data\StreamingAssets\aa\`
  (`.bundle` files) + `resources.assets` (14.3 MB).
- Player class: `HeroController` (`HeroController.cs`), with `ConfigGroup` (NormalSlash, DashStab,
  ChargeSlash, WallSlash, Downspike...). Animations play via `Animator.Play(...)` and tk2d
  (`TeamCherry.TK2D.dll`). Hornet's real move names/timings to be mined next.
- Extraction: **UnityPy 1.25.3 installed** (Python). Plan: an importer that reads the user's Silksong
  bundles and emits Hornet atlas PNG + animation JSON into `BepInEx/plugins/HornetInCasualties/`.
  No Silksong files redistributed.

## Plugin (Stage 1)
- `HornetInCasualties.csproj` — `netstandard2.1`, refs CU `Managed\*.dll` + BepInEx core; auto-copies the
  DLL into `BepInEx\plugins\HornetInCasualties\`.
- `src/Plugin.cs` — BepInEx 5 plugin `dev.cuhornet.hornetincasualties`; Harmony postfix on
  `PlayerCamera.Update`; config `Enable`, `HideVanillaBody`.
- `src/HornetAvatar.cs` — attaches to the `Body`'s GameObject, follows `Body.baseLimb`, flips with
  `Body.isRight`. Stage 1 uses a generated placeholder sprite (`PlaceholderSprite`).
- **Build:** `dotnet build -c Debug` (deploys automatically).

## Verification (Stage 1 — DONE 2026-10-02)
- Plugin loads: `BepInEx\LogOutput.log` → `[Info :Hornet in Casualties] Hornet in Casualties v0.0.1 loaded.`
- Avatar attaches once per player: `[Info :Hornet in Casualties] HornetAvatar attached to the player.`
- **Seen in the real demo:** placeholder Hornet is drawn on the player in the lifepod and in the training
  sandbox (screenshot `shots/world1_zoom2.png`, `shots/sandbox2.png`). The Harmony hook on
  `PlayerCamera.Update` works.
- **Fixed:** component was added to a child but searched on the body, so it respawned every frame. Component
  now lives on the `Body` GameObject itself; sprite is a child.

## Test oracle (dev)
- **F9** (from the main menu): `PreRunScript.instance.StartTutorial()` → loads the tutorial world.
- **F8** (in the tutorial world): `TutorialHandler.main.StartCourse(typeof(SandboxCourse))` and hides the
  course-select overlay → the flat **sandbox** test area.
- **F10** (from the main menu): `PreRunScript.instance.StartRun()` → normal run.
- Keyboard: F8/F9/F10 (VK 0x77/0x78/0x79). The game's own binds (Settings.cs) don't use them.
- **Waits:** start small (~6 s) and only increase if a load is actually slow. Don't hard-code 30 s.
- Menu click map (client px, 1920x1080): content warning → Ctrl; main-menu start = the sitting creature's
  **eyeball** ~ (620, 530); Run settings **Start** ~ (660, 1040). In the course-select screen, right arrow
  ~ (1496, 640), **Start** ~ (960, 874).
- **Launch note:** if launched by exe, prefer `um win launch`.

## Hornet asset extraction (Silksong 6000.0.50f1)
- Silksong content is in **Addressables**: `Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64\**\*.bundle`
  (2068 bundles, 7.7 GB) + `resources.assets`. Use **UnityPy 1.25.3** with
  `UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"` (bundles have no readable version header).
- The player character uses **tk2d** (`TeamCherry.TK2D.dll`). Crest bundles: architect/beast/cloakless/
  reaper/shaman/wanderer/witch — **no "hunter" bundle**, so the Hunter crest is the base/Default
  (`HeroControllerConfig` "Default" has no `heroAnimOverrideLib`).
- Key assets:
  - `herocollections_assets_shared.bundle` → tk2d collections incl. **`Hornet Cln`** (116 sprites,
    cloaked/cloak) and `Knight` (1828, legacy).
  - `herocollections_assets_crest*.bundle` → per-crest weapon collections.
  - `herodynamic_assets_all.bundle` → tk2d `tk2dSpriteAnimation` libraries. The base Hornet lib
    referencing `Hornet Cln` has Idle/Turn/Run/Jump/Fall/Soft Land/Evade/Throw Side/Harpoon Side (cloaked).
    The full 408-clip moveset references `Hornet Cloakless Cln` (black body, Slash/Dash/Bind/...).
  - Frame sprite names encode clip+index (`Hornet_idle_cloakless0000`, `jump_cloakless0000`, ...).
  - tk2d packs sprites **rotated/flipped** in the atlas; `regionW/H` are 0, rect comes from UVs.
- Tools written: `tools/scan_bundle.py`, `probe_hero.py`, `probe_anims.py`, `list_clips.py`,
  `find_cab.py`, `find_cloak.py`, `compare_names.py`, `diag_atlas.py`, `diag_swap.py`,
  `import_hornet.py` (bake frames), `export_hornet_render.py` (atlas+quads).

## BLOCKER (2026-10-02)
- **CU's MeshRenderer is not available at runtime.** `go.AddComponent<MeshRenderer>()` returns **null**
  (logged `init step 1b (renderer null=True)`), so the plan to draw Silksong's quads as meshes fails.
  `VisionMask` declares `[RequireComponent(MeshRenderer)]` but appears to be stripped/dead in this build.
  `SpriteRenderer` is available (the game uses it everywhere).
- **Baking tk2d frames to textures in Python is fighting the atlas packing.** `import_hornet.py` samples
  each frame's UV quad; applying a global 90° rotation makes `Idle` upright but leaves some `Run`/`Jump`
  frames sideways, so our positions↔uvs↔atlas orientation convention is still wrong. Need the exact tk2d
  convention (or let Unity render it).
- **Recommended fix (accuracy + robustness): a one-time exporter *inside Silksong*** — a small BepInEx
  plugin that, on a hotkey, renders each Hornet tk2d frame with Silksong's own Unity renderer to a
  RenderTexture and writes PNG + pivot. Unity handles rotation/material; CU then uses SpriteRenderer.
  This matches the "port the rendering" idea without reimplementing MeshRenderer.

## Route decision (2026-10-02, updated)
- Human chose **live passthrough** (Silksong is the Hornet source). Rationale: both sides are 2D, so
  there is **no depth/3D compositing** (the hard part of GTA/SkyCraft is absent). Silksong's 3D capability
  is irrelevant to Hornet's tk2d sprites. Cost is frame acquisition + a second process.
- Human approved installing a BepInEx plugin into Silksong.
- Plan: Silksong-side plugin renders Hornet (isolated) to a RenderTexture and can (a) dump PNGs (fallback /
  accuracy check) and (b) stream frames + state to CU over shared memory. CU draws Hornet at the player.
- **Silksong plugin scaffolded + built + deployed:** `silksong/` → `HornetExporter.dll` in
  `...\Hollow Knight Silksong\BepInEx\plugins\HornetExporter\`. F7 logs Hornet hierarchy + tk2d clips;
  F8 renders Hornet to `hornet_capture.png`. Camera isolates a `HornetCapture` layer (falls back to the
  whole scene if the layer doesn't exist — may need creating).

## PASSTHROUGH MILESTONE (2026-10-02)
- Silksong-side plugin works: F7 inspects (live hero = `Hero_Hornet(Clone)`, `tk2dSpriteAnimator`,
  current clip `Idle`), F8 captures Hornet isolated, **F6 dumps the live 535-clip table**, **F5 bakes frames**.
- **Live Hornet clips** include the full cloaked moveset: Idle, Run, Airborne, Fall, Land, HardLand, Turn,
  Dash, Slash, SlashAlt, SlashEffect, UpSlash, DownSpike, Wall Scramble, BindCharge Ground, ... The live
  sprite names are `Hornet_*` (e.g. `Hornet_run_new0000`, `Hornet_slashes0009`).
- **Isolation method:** render the game's own main camera twice (Hornet's root `tk2dSprite` enabled vs
  disabled) and use the difference as alpha. No URP/custom-camera issues (Silksong is built-in pipeline;
  a manual camera rendered blank).
- **Bake:** `silksong/` F5 walks 14 clips → **115 PNGs + pivots** in
  `HornetExporter\baked\` (`bake.json`). Copied into CU's `plugins\HornetInCasualties\hornet\`.
- **CU side:** `HornetInCasualties` loads `bake.json` and draws Hornet on a `SpriteRenderer`
  (mesh route abandoned: `MeshRenderer` is stripped). Config `Scale`/`Ppu`/`OffsetX`/`OffsetY`,
  `HideVanillaBody` (now hides all body sprites). Clip chosen from `Body` state (Idle/Run/Airborne) and
  logged on change. **Verified in the CU sandbox** (screenshots `shots/hornet9*.png`).
- **Known issues:** the sandbox ragdoll oscillates so Hornet flips Idle↔Airborne; placement/scale need
  tuning (current Scale 0.8, Ppu 64); Fall/Turn not in the bake; live state link not yet wired.

## Offline-first (chosen 2026-10-02)
Human chose offline-first (option 1); keep the live state link as a fallback if complications pile up.
- Baked an expanded set: **50 clips / 356 frames** (locomotion incl. Fall/Sprint/Turn/Walk, dash, slash/
  needle, wall/mantle, bind, hurt/death). Baker now reads only a cropped region around Hornet (fast).
  Re-copied into CU's `plugins\HornetInCasualties\hornet\` (`bake.json` + `frames/`).
- CU clip mapping now: Idle / Run / Airborne (vy>1) / Fall (vy<-0.5). Verified standings in the sandbox
  (`shots/hornet10*.png`): real Hornet upright, vanilla body hidden.
- **Gotcha:** the baker wrote invalid JSON when a frame diffed to nothing (leading comma) — fixed with a
  per-clip `firstFrame` flag; `tools/repair_bake.py` repairs an existing file. Effect clips (SlashEffect,
  UpSlashEffect) diff to ~nothing because only the root body renderer is toggled.
- **Color note:** diff alpha uses the composited pixel, so Hornet is tinted by the scene light and looks
  washed (pink-ish cloak). Un-premultiplying against the background frame would fix it.
- Silksong plugin version 0.0.5.

## Stage 2 — moveset (chosen: full Hornet moveset, real gameplay, built in CU)
Decision: implement Hornet's moveset in CU (option 1). Live link judged **not** simpler: Silksong's
`HeroController` can't run detached (PlayMaker FSMs, TeamCherry services, its own world/collision), so
driving it headless is the full SkyCraft build.
- **Slice 1 shipped:** `src/HornetController.cs` — Dash (velocity burst + cooldown + gravity off),
  Slash forward / UpSlash (hold W) / DownSpike (hold S in air), and **pogo** (down-slash on an enemy/
  terrain while airborne bounces up). Hits reuse CU's system: `BuildingEntity.health` + `AnimalHit`/
  `BuildingHit`, `WorldGeneration.CreateDamageNumber`. `HornetAvatar.PlayAction` plays the clip once then
  returns to locomotion. Config section `[Moves]` with keys (Slash=J, Dash=K, Needle=L, Bind=H) and
  tuning values.
- **Verified in the CU sandbox:** log shows `move: Slash`, `move: dash dir=1`.
- **Untested/broken:** pogo bounce (not yet triggered in a test), needle throw and bind (keys reserved,
  not implemented), effect clips empty, capture color washed.
- **CU note:** `Body.Attack` is CU's own attack (raycast → BuildingEntity). Hornet moves are additive on
  new keys so CU gameplay isn't broken yet.

## Moveset feedback round (2026-10-02)
- **Facing fixed:** was always right because it used `Body.isRight`. Now tracked from `Body.moveDir.x`
  and applied with `SpriteRenderer.flipX` (localScale stays positive). Verified moving left.
- **Dash ~3x longer:** `DashDuration` 0.18 → 0.55.
- **Needle throw (boomerang):** `src/HornetNeedle.cs` — flies out, damages on contact, stops at
  terrain/range, returns to Hornet, she catches it. Mirrors Silksong's NeedleThrow. Procedural needle
  sprite for now (should be replaced with Silksong's needle art). Verified: log `move: needle throw`,
  `move: needle caught`.
- Keys kept J (slash) / K (dash) / L (needle) / H (bind, not implemented yet).
- Added NeedleThrow/Harpoon clips to the baker's list (need a re-bake to have their animations).

## Moveset fixes (2026-10-02)
- **Dash didn't cross distance:** CU damps horizontal motion, so setting `rb.velocity` was lost. Dash is
  now **positional** (`rb.MovePosition` in FixedUpdate) → real distance = DashSpeed × DashDuration.
- **Pogo too weak:** PogoSpeed 13 → 19, applied to both the root `rb` and `baseLimb.rb`, `grounded=false`.
- **Turn animation:** play "Turn" on facing change while grounded and moving.
- **Double jump:** gated on a tracked time-since-grounded (CU's `timeSinceGrounded` is private), allows one
  air jump; `Wall` refreshes it.
- **Wall slide/jump:** CU already owns this (with the alternate-wall rule). Removed our duplicate physics;
  we now only play Hornet's "Wall Slide"/"Walljump" animations (`Body.timeSlidfor` + vy heuristic). If
  Silksong's repeat-cling behaviour is wanted, patch `Body.Jump`'s `firstWallJump`/`lastJumpedOnRightWall`.
- Needle art is still procedural; capture colors still washed.

## Kinematic takeover (2026-10-02)
- Root cause of weak dash/pogo: CU's `Body.FixedUpdate` **clamps rb.velocity.x to `actualMaxSpeed`**
  while grounded (line 2565) and doubles gravity when `endedJump` (line 2556). Setting velocity was
  therefore undone.
- **Decision (human): do not port Silksong's `HeroController`** — it's PlayMaker-driven and coupled to
  TeamCherry/GameManager/PlayerData; Silksong is Unity 6000 vs CU 2022 (can't load its assembly). Instead
  **port the behaviour**: run a clean controller in CU using Silksong's constants as the spec.
- **Implemented (config `[Moves] KinematicMode`, default true):** Harmony prefix on `Body.FixedUpdate`
  skips CU's movement while Hornet drives; `HornetController.FixedUpdate` does locomotion (moveDir →
  `MoveSpeed` with `MoveAccel`), dash (positional, no clamp), gravity. On init it **freezes the ragdoll**
  (limb rigidbodies → Kinematic, limb colliders disabled, root `col` kept). Wall-jump gate cleared via
  `BodyJumpPatch` (same-wall re-jump allowed).
- **Fall-through fix:** freezing the limb bodies (Kinematic + colliders off) removed the colliders the
  world is actually collided against, so she fell through the floor. Replaced with **puppeting**: limbs
  stay dynamic with colliders enabled; each FixedUpdate their velocity is set to the body's, so collision
  works and the ragdoll can't flop. (`PuppetRagdoll`.)

## LIVE PASSTHROUGH WORKING (branch `passthrough-live`, 2026-10-02)
Both games run at once and exchange state over shared memory `Local\HornetPassthrough_v1`
(`shared/PassthroughProtocol.cs`, compiled into both plugins).
- **Host (CU):** `src/LiveLink.cs` publishes the player's position/velocity/facing/grounded and draws
  Hornet from the published frame. `LiveMode=true` fully disables the old baked port.
- **Guest (Silksong):** `silksong/src/LiveLink.cs` reads host state, drives Hornet's `tk2dSpriteAnimator`
  (Idle/Run/Airborne/Fall), and publishes her frame.
- **Capture:** the **main-camera diff** (Hornet visible vs hidden) with cached buffers (one screen RT +
  one region tex, no per-call allocation), throttled to ~half rate. This is lit (uses the game's own
  camera/lights) and transparent (background cancels). A dedicated Hornet-layer camera was tried first:
  every layer 8-31 is named in Silksong (30="Physical Push React", 31="Attack Detector") and there is
  no free layer, and the offscreen camera excluded the 2D lights, so Hornet rendered dark on an opaque
  clear — abandoned.
- **`Application.runInBackground = true`** on both sides is required: CU pauses unfocused otherwise and
  stops reading frames.
- **Verified:** CU log `received live Hornet frame 215x184`; screenshot shows lit Hornet in the sandbox.
- Silksong layers dump: 0/1/2/4/5/7 Default..Attack Detector (listed in the log).

## Passthrough polish (2026-10-03)
- **Placement** user-tuned: `Scale=1.6 OffsetX=0.2 OffsetY=-0.1` now the defaults; vanilla body hidden;
  feet-box ground test; frame-size clamp + size-jump guard.
- **Old baked import removed:** deleted `HornetData`/`HornetController`/`HornetNeedle`; `HornetAvatar` is
  just a marker that attaches `LiveLink`. CU is passthrough-only. Config keys: Enable, HideVanillaBody,
  LiveMode, Avatar Scale/Offset, DebugKeys; action keys Slash=J/Dash=K/Needle=L.
- **Smoothness:** Silksong publishes every frame (cached diff capture); stable.
- **State sync:** CU publishes a flag bitfield (`FlagAttack/Dash/Needle/Up/Down`) plus pos/vel/grounded/
  facing; Silksong maps it to Hornet clips (Slash/UpSlash/DownSpike/Dash/NeedleThrow Throwing, and
  Airborne/Fall/Run/Idle), flips Hornet to match facing, and scales `tk2dSpriteAnimator.ClipFps` by speed.
  Verified: pressing J/K/L in CU makes Silksong play Slash/Dash/NeedleThrow.
- **Known:** CU's grounded flickers so clips flap Airborne/Fall; wants hysteresis or a better ground test.

## Next (passthrough)
- Stabilise grounded (hysteresis) and movement thresholds; add a watchdog (Silksong gone → restore normal).
- Optional packet/presentation polish; then showcase + publish notes.
- Bind/heal; verify pogo/double jump/wall in game.
- Un-premultiply capture color; tune scale/offset.
- Optional: live state link.

## Authority inversion — M1 (2026-10-03, new session)
Human decision: invert the authority. **Silksong becomes the character controller** (real input →
moveset → physics → animation); **CU becomes the environment + renderer** (feeds collision, keeps
drawing everything, and composites Hornet's frames). This is the "true live link" that the earlier
Stage-2 notes deferred. Chosen shape **M1 = Silksong simulates against a mirrored world**, not M2
(CU integrates Silksong's intent).
- Plan: CU streams local collision geometry around Hornet → Silksong builds proxy colliders → real
  `HeroController`/`HeroBox` runs against them → Hornet's position returns → CU puppets `Body`/camera
  and renders. Scale via one factor `k` (world units per CU unit); tune once to Hornet's jump/dash.
- Hard parts (recorded): input injection; geometry mirror on the exact layers `HeroBox`/`Helper.Raycast2D`
  query; scene/layer isolation from Silksong's real level; CU ragdoll must be puppeted (skip
  `Body.FixedUpdate`); ~1 frame round-trip latency; dynamic world/enemies later.
- Staging: **S0 input round-trip** → S1 terrain mirror (stand on CU's floor at the right scale) →
  S2 return trip (CU puppets to Hornet) → S3 harden + moveset.

## S0 — input round-trip (2026-10-03, VERIFIED)
Built and deployed both plugins; protocol bumped to `Version = 2`.
- **Protocol:** new `Input` region (after pixels) carrying the host's raw button bitfield
  (`BtnLeft/Right/Up/Down/Jump/Attack/Dash/Needle`), seqlock-guarded. CU writes it; Silksong reads it.
- **CU (`src/LiveLink.cs`):** `ReadButtons()` reads CU's own keybinds (`KeyBinds.GetBind("left"/...)`)
  plus the passthrough dash/needle keys, and `WriteInput`s them each `LateUpdate`. `CuState` still published.
- **Silksong (`silksong/src/LiveLink.cs`):** no longer replays clips/facing from CU's velocity. It reads
  the Input region into `LiveLink.InjectedButtons` and still captures/publishes Hornet's frame. Logs her
  own `clip`/`vel`/`pos`/`in` every 30 frames for verification.
- **Silksong (`silksong/src/InputInjector.cs`):** Harmony patches.
  - prefix on `InputManager.UpdateInternal`: sets `SuspendInBackground = false` so InControl keeps
    ticking while the host game is focused.
  - postfix on `PlayerActionSet.Update`: for the `HeroActions` set, asserts the host's pressed buttons
    into `Left/Right/Up/Down/Jump/Attack/Dash/QuickCast` via `PlayerAction.CommitWithValue`.
- **Two bugs found and fixed during verification:**
  1. *Destructive injection:* committing `0` for host-released buttons overrode Silksong's real keyboard,
     breaking its own menus. Injection is now **additive** (only assert presses; releases fall through to
     the real device).
  2. *`MoveVector` never saw host directions:* the set derives `MoveVector` from `Left/Right/Up/Down`
     *before* the postfix runs, so `HeroController`'s horizontal speed stayed 0 (Hornet stood up but
     wouldn't run). The postfix now re-invokes `PlayerTwoAxisAction.Update` via `AccessTools` after
     asserting the directions.
- **Verified in the real games (taskbar restarted both; profiles loaded):**
  - CU sandbox → hold **D**: Silksong log `in=0x102 clip=Turn vel=(8.3,0.0)`, Hornet ran `pos 17.0 → 39.6`
    and splashed into a pond. Hold **Space**: `in=0x110`, `Double Jump`/`Umbrella Float`.
  - CU log `LiveLink diag: fid=8146 w=210 hasPx=1 silkAlive=True` — frames still stream back and CU
    composites her (screenshot `%TEMP%\s0_cu_running*.png`, `s0_ss_running*.png`).
- **Gotcha:** during the first pass we saw spurious-looking Left/Jump/Attack/Down bits; those turned out
  to be the human's own keypresses landing in CU (which held focus) and being forwarded — i.e. the feature
  working, not an artifact. A clean single-key retest (D only) showed exactly `in=0x102`. For `um win drive`
  on CU's legacy-`Input` binds, plain VKs work; `scanmode` was only needed once to click through the content
  warning (it sends scan-code-only events, so prefer plain VKs for anything gameplay reads).
- **S0 leaves CU still simulating its own ragdoll** (input is duplicated), which is expected — puppeting
  CU to Hornet is S2.

## S1 — terrain mirror (2026-10-03, VERIFIED core)
Protocol bumped to `Version = 3`; adds a `Terrain` region (CU -> Silksong): up to 256 ground AABBs
relative to the CU player's ground contact, in CU units, plus the CU player collider height (scale
reference), seqlock-guarded.
- **CU (`src/LiveLink.cs`):** every 0.25 s, `PublishTerrain()` anchors at the ground under the player
  (down-ray on the `Ground` mask, fallback to the collider's min.y), grabs `Ground`-layer colliders in a
  ±96 CU-unit box, clips each to the window, and publishes relative `(x,y,w,h)` rects + player height.
- **Silksong (`silksong/src/TerrainMirror.cs`), F4 = apply / F3 = restore:**
  - Layer choice is the key: Silksong's ground checks and `HeroBox` use mask `0x2100` = layers **8
    ("Terrain")** and 13 ("Hero Detector"), so proxy boxes are put on **layer 8** — no mask patches.
  - Scale `k = HornetColliderHeight / CuPlayerColliderHeight` (measured: 2.08 / 5.0 = **0.416**).
  - Anchor at Hornet's feet; boxes placed at `anchor + k*rect`; then teleport Hornet 3 units above the
    floor and zero the rigidbody.
  - Disables all existing layer-8/13 colliders (16 in Shellwood) so only the proxy can support her;
    `Restore()` re-enables them and destroys the proxy.
- **Verified:** F4 → `applied 8 boxes k=0.416 hornetH=2.08 cuH=5`. Hold D → Hornet ran `pos 20.3 → 42.1`
  (~22 units) with `vel.y = 0.0` and `clip=Run` the whole way — i.e. she is standing on and traversing
  CU's mirrored sandbox floor, with Silksong's own terrain disabled. `Restore` re-enables vanilla terrain.
- **Known limits (expected for one-shot S1):** the proxy does not follow the CU player, so running past
  the window edge (~±40 Silksong units) drops her; room-transition triggers in the real map still fire
  (dark fade) when she crosses them; restoring removes the floor she's standing on, so she drops.
- **Next (S2):** puppet CU's `Body`/camera to Hornet's mapped position, map absolute CU position to
  Silksong (fixed origin + `k`), refresh the terrain window as the player moves, and stop CU's own
  ragdoll/controller so input isn't duplicated.
