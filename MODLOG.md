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

## S2 — puppet: Silksong drives, CU follows (2026-10-03, VERIFIED)
Protocol bumped to `Version = 4`; adds a `PlayerState` region (Silksong -> CU) carrying Hornet's state
**already mapped into CU coordinates**, plus facing/grounded/active.
- **The mapping (fixed on apply):** `cu(silk) = _cuOrigin + (silk - _silkOrigin)/k`,
  `silk(cu) = _silkOrigin + k*(cu - _cuOrigin)`, with `k = HornetH/CuPlayerH`. `_cuOrigin` = CU player
  pos at apply, `_silkOrigin` = Hornet pos at apply — so nothing jumps on activation.
- **Terrain rects are now absolute CU coords** (was relative). With the fixed mapping, the mirrored floor
  stays put in Silksong while the CU window slides with the player.
- **Silksong (`TerrainMirror`)** now keeps the mapping and `Poll()`s each frame: when CU's terrain
  revision changes it rebuilds the proxy (no teleport) via a cheap rect hash. `LiveLink` publishes
  `PlayerState` (mapped pos + velocity, facing, grounded, active) every frame.
- **CU (`src/LiveLink.cs` + `src/Plugin.cs`):** on `active`, `EnablePuppet()` sets every `Rigidbody2D`
  under `Body` to Kinematic (**17** frozen) and a Harmony **prefix on `Body.FixedUpdate`** returns false,
  so CU's own ragdoll/controller stops. `PuppetTo()` then sets the body transform (and `rb.position`) to
  the mapped pos each `LateUpdate`; the camera and Hornet's composited sprite follow. On `active=0`
  (F3), `DisablePuppet()` restores the original body types.
- **Verified in the real games:**
  - F4 → Silksong `applied 8 boxes k=0.416`; CU `LiveLink: S2 puppet enabled (17 rigidbodies frozen)`.
  - Hold **D** → Hornet ran `pos 20.3 → 36.1` grounded (`vel=(8.3,0)`); the CU screenshot shows the
    sandbox camera scrolled with her and Hornet centered (composited). So the full M1 loop runs:
    CU input → Silksong controller/physics on CU-mirrored terrain → mapped pos back → CU body/camera/render.
  - F3 → Silksong `restored vanilla terrain`; CU `S2 puppet disabled`; Hornet landed safely on vanilla
    ground (`pos 43.0 Idle vel=0`).
- **Known issues:** occasional `Land To Run` flicker (micro-falls at proxy rect seams / rebuild timing);
  the frame capture briefly returns tiny frames during room fades (already ignored by the size guard);
  Silksong's real room-transition triggers still fire as she crosses the map; the mapping is fixed, so a
  long run eventually leaves CU's actual floor and the proxy correctly disappears.
- **Next (S3):** merge proxy rects (kill seams), widen/stream terrain more smoothly, add a watchdog
  (Silksong gone → auto-unpuppet), then test dash/jump/wall/needle against CU geometry and the showcase.

## S3 — seams, watchdogs, moveset (2026-10-03, VERIFIED; showcase skipped)
- **Seams:** CU inflates every terrain rect by 0.1 CU units (`margin`) so adjacent colliders overlap and
  no sub-pixel seam can drop her. Silksong no longer destroys/recreates proxy objects each rebuild — it
  keeps a `BoxCollider2D` pool, toggles/repositions them, and calls `Physics2D.SyncTransforms()`.
  Verified: a 2 s run is clean `Turn → Idle To Run → Run …` with `vel.y = 0` throughout (the earlier
  `Land To Run` flapping is gone).
- **Watchdogs (both directions):**
  - CU: `active` from Silksong is honored only while `SilkAlive(1000)`; otherwise the puppet is released
    (and stays released — the first cut re-puppeted from the stale `active` flag). Verified by killing
    Silksong: `Silksong inactive/heartbeat lost; releasing puppet` → `S2 puppet disabled`, no re-enable.
  - Silksong: if `!HostAlive(1500)` while the mirror is active, it restores vanilla terrain. Verified by
    killing CU: `host heartbeat lost; restoring vanilla terrain` → `restored vanilla terrain`.
- **Moveset against CU geometry:** the moves are Silksong's real controller, reached via forwarded input.
  - Jump: `Double Jump`/`Land`. Dash (K): moved `39.8 → 43.6`, `Dash To Idle`.
  - Slash (CU attack = Mouse0): `in=0x120`, `SlashAlt`.
  - Needle/harpoon (L): **corrected mapping** — `QuickCast` is the tool/spell (gave `AirSphere Attack`);
    Silksong's needle/harpoon is `SuperDash` (`HarpoonDash`). Now `in=0x180` → `Harpoon Catch`, dash-ran
    `20.3 → 30.6`.
  - Not yet exercised: wall cling/jump and pogo — the CU sandbox has no suitable walls/enemies, and
    enemies aren't mirrored yet.
- **Next:** mirrors only static `Ground` geometry; to get wall/pogo gameplay we'd mirror more layers and
  entities. Otherwise the milestone set is done (showcase intentionally skipped).

## S4 scope written (2026-10-03)
Human wants the next milestone scoped (not built yet): pogo off CU enemies, damage sync both ways, a
clean "blank slate" Hornet-only visual passthrough, and correct scaling. Full plan + decomp mechanics in
**`S4-SCOPE.md`**. Highlights: entity proxies over a new `Entities` (CU→Silk) + `Events` (Silk→CU)
channel; pogo via a lightweight layer-17/19 proxy hitting `HeroDownAttack.ContinueBounceTrigger`
(no heavyweight `HealthManager`); Hornet damage via proxy `DamageHero`; dedicated capture camera for the
blank slate (biggest unknown is lighting); one fixed capture PPU driving CU's display scale from `k`.

## S4 phase 1 — pogo off an entity proxy (2026-10-03, VERIFIED)
Protocol v5 adds an `Entities` region (CU -> Silksong): up to 64 fixed records (`id, x, y, w, h, hp,
maxHp, flags`), revisioned.
- **CU (`src/LiveLink.cs`):** every 0.25 s publishes nearby actors — real `BuildingEntity`s
  (`Physics2D.OverlapCircleNonAlloc`, radius 30) plus debug **pogo dummies** (F7 spawns one just below
  the player; a visible red square, id 9000+).
- **Silksong (`silksong/src/EntityProxies.cs`):** maps each entity through the S2 fixed mapping and pools
  trigger `BoxCollider2D` proxies. Layer is adaptive: **19 (`INTERACTIVE_OBJECT`)** when the physics
  matrix allows `17<->19` (it does here; 17 = the attack layer), else 17. Layer 19/17 makes
  `HeroDownAttack.ContinueBounceTrigger` run its direct path — **no `HealthManager` required**.
- **Evidence (`silksong/src/PogoProbe.cs`):** Harmony postfix on `HeroController.DownspikeBounce` logs
  `POGO! DownspikeBounce fired.`
- **Verified:** the human pogoed on the streamed dummy; log shows `POGO! DownspikeBounce fired.` ×8 —
  Hornet's genuine down-spike bounce triggers off a CU-streamed entity.
- **Notes:** the training sandbox has no `BuildingEntity` (SandboxCourse only spawns a terminal), so the
  test used the synthetic dummy; pogo off a *real* CU enemy still needs a run. Auto-testing the pogo by
  timed keys was unreliable (jump timing), but manual play proves the path.
- **Next (phase 2):** `Events` ring (Silk→CU) so Hornet's hits apply CU damage — relay via
  `DamageEnemies.HitResponded` or a proxy hit relay, plus CU damage numbers.

### S4 follow-up — real-run terrain alignment (2026-10-03, FIXED)
- **Symptom (human):** in a *real run*, activating the mirror made Hornet fall through the floor for a
  while before settling. The sandbox was fine.
- **Root cause 1 (collider shape):** real-run ground is `TilemapCollider2D` and `collider.bounds` there is
  a **64x64 chunk AABB**, not solid tiles. The exporter was shipping giant phantom boxes, so the proxy
  floor was wrong. (Diagnostic: `col0=TilemapCollider2D 64.0x64.0`.)
- **Root cause 2 (origin):** terrain rects are absolute and anchored at CU's **ground contact**, but the
  S2 mapping origin was CU's **body transform** (`CuState`), which sits above the floor → the floor
  mapped below her feet.
- **Fix (protocol v7):**
  - `PublishTerrain` now samples CU's **block grid** (`WorldGeneration.world.GetBlock`/`GetBlockInfo`,
    1 block = 1 world unit via `BlockToWorldPos`) over ±48 units and emits **row run-length merged**
    rects. `MaxRects` 256 → 2048. No collider bounds involved.
  - The terrain header carries the anchor (CU collider-centre x, ground-contact y); Silksong sets
    `_cuOrigin = anchor`, `_silkOrigin = (Hornet collider-centre x, feet y)`; the player state maps
    Hornet's collider centre, and CU places its body so its collider centre lands there (`- col.offset`).
    Apply no longer drops her 3 units.
- **Verified (lifepod real run):** F4 → `applied 441 boxes k=0.416 cuAnchor=(0.0,483.0)`; the puppet is
  **stable** (`got=(0.0,485.5)` held, `pos=20.5,5.0 vel=0`), no fall; CU shows her standing in the pod.
- CU logs the terrain anchor/count on change and the puppet target every 0.5 s; Silksong's state log now
  includes Y, so any future drift is directly visible.

## S4 phase 2 — Hornet → actor damage (2026-10-03, VERIFIED dummy path)
Protocol v8 adds an **`Events` ring** (Silksong -> CU): single writer/reader, monotonic index, records
`(type, id, a, b, c)`, `EventHitEntity` = `a` damage.
- **Silksong (`silksong/src/ProxyRelay.cs`):** each entity proxy gets a relay. When Hornet's attack
  collider (layer 17) enters/stays, it pushes `HitEntity(id, HitDamage=5)`, debounced 0.3 s so one swing
  is one hit.
- **CU (`src/LiveLink.cs`):** `DrainEvents()` each frame applies hits. Debug dummies (id 9000+) lose
  tracked HP and pop at 0; real actors are looked up by streamed id (`GetInstanceID() & 0xFFFFFF`) and
  take `be.health -= damage` plus `WorldGeneration.CreateDamageNumber`. Logs
  `Entity/Dummy <id> hit for N -> hp H`.
- **Verified (sandbox, down-slash on the F7 dummy):** Silksong `Hit entity 9000 for 5`; CU
  `Dummy 9000 hit for 5 -> hp 95`. A real streamed entity (`16759678`) also registered a hit in Silksong.
- **Fix after first test:** the id map was cleared every entity publish, so a real-entity event arriving
  outside the window was dropped. Now the map is persistent and pruned of dead entries (`PruneEntityMap`).
  Real-entity damage is the same code path as the dummy; re-verify with a console-spawned enemy.
- **Note:** damage is a fixed 5 for now; reading Hornet's real nail damage comes later. No actor->Hornet
  damage yet (next slice).
- **Real-entity damage (2026-10-03, VERIFIED):** the first cut used dummy ids `9000+`, but real instance
  ids (masked to 24 bits) are ~16M — so `id >= 9000` **swallowed every real hit** into the dummy branch
  (index out of range → silent return). Dummies now use **negative ids** (`-(i+1)`); `ApplyHit` treats
  `id < 0` as a dummy. Verified by spawning a `shadecrawler` via CU's console and slashing it:
  `Entity 16736012 hit for 5 -> hp 1.25 -> -3.75` (killed), and `Entity 16759044 -> hp 99990`.
- **Jump fix (2026-10-03, VERIFIED):** with injected (held) input, the ground jump's `HeroJump` was
  immediately followed by the held-button double-jump path, so the wing double jump fired on every jump
  and the real double jump was consumed. Wall jumps were fine (different path). Fix: after a ground jump,
  block `DoDoubleJump` until Jump has been **released** (`silksong/src/JumpGating.cs`; postfixes on
  `HeroJump`/`HeroJump(bool)`/`HeroJumpNoEffect` clear the gate, `DoDoubleJump` prefix checks it, and
  `InputInjector` re-arms on release). Wall jumps don't set the gate. Verified by the human: ground jump
  is clean, a second press gives the wings, wall-jump→double-jump still works.

## S4 phase 3 — actor → Hornet damage (2026-10-03, VERIFIED)
Protocol unchanged (still v8): this reuses the existing `EntFlagContactDamage` bit, so no layout bump.
- **Key fact (from the deployed Silksong log):** Hornet's `HeroBox` is on **layer 20**, and the physics
  matrix is **not** ignored between 20 and the proxy layer **19** (`ignored vs 19` lists 0,1,2,4,5,7,9,
  10,11,12,13,14,15,16,18,19,21,23,24,25,26,27,30,31 — 20 and 17 are absent).
- **CU (`src/LiveLink.cs`):** `PublishEntities` sets `EntFlagContactDamage` when the actor carries a
  `SpiderHandler` (CU's limb-biting animal class; `SpiderHandlerTBE` derives from it). It also now
  **dedupes by actor id** and unions the actor's colliders into one proxy — previously a creature's
  body+limbs each became a proxy (log showed `1..4 proxies` for one animal), multiplying hits.
- **Silksong (`silksong/src/ContactDamage.cs`):** on Hornet's `HeroBox` *entering* the proxy, call
  `HeroController.TakeDamage(proxy, side, 1, ENEMY)` ourselves — once, only if `hero.CanTakeDamage()`,
  behind a 0.6 s re-arm cooldown, with no i-frame drain. Pogo is unaffected (HeroDownAttack bounces off
  the proxy with or without a `DamageHero`).
- **Root-cause lesson (cost several sessions):** the first cut put a `DamageHero` on the proxy and let
  `HeroBox` apply it. `HeroBox.TakeDamageFromDamager`'s buffered `damageDealt` is **sticky**, so once a
  hit registered, every `OnTriggerStay2D` re-applied it; the log showed only 2 `dmg=1` triggers but **9
  masks lost** (9→0 instantly). A gate that disarmed `DamageHero.damageDealt` didn't help. Do **not**
  rely on `DamageHero` for streamed proxy contact damage; call `TakeDamage` explicitly.
- **Nail damage (resume item 2):** `silksong/src/ProxyRelay.cs` reports Hornet's real damage: reads the
  attack collider's `DamageEnemies`; if `useNailDamage`, uses
  `PlayerData.instance.nailDamage * nailDamageMultiplier`; otherwise `damageDealt`; falls back to 5.
- **Evidence hooks:** `silksong/src/HeroHurtProbe.cs` logs `HURT!` (HeroController.TakeDamage),
  `HEALTH!` (PlayerData.TakeHealth — catches any source), and quiet `SRC!` probes for chomp/special.
- **Verified (human):** running into a console-spawned `shadecrawler` costs exactly **1 mask per
  contact** with knockback; the log shows 1:1 `HURT! dmg=1 hp A->(A-1)/9` ↔ `HEALTH! amount=1`.
  The F7 pogo dummy is *not* a biter, so it still pogoes without hurting her.

## S4 visual — blank-slate capture + the CU camera/terrain fixes (2026-10-03, VERIFIED)

### Blank-slate passthrough (`silksong/src/HornetCapture.cs`, `LiveLink.cs`, `Plugin.cs`)
- Hornet is captured **alone on a transparent background** using the game's **own main camera** with
  its `cullingMask` narrowed to `Player(9) | Hero Only(28)` and `clearFlags=SolidColor`, alpha 0.
  Using the same camera instance is what makes lighting correct (the old offscreen camera came back
  dark). Do **not** OR in every layer her effect renderers touch — many live on Default(0) and dragged
  the whole environment in.
- Her **light/glow/dust children are hidden for the capture** (`HideEffects`), leaving the lit body
  only; their renderer list is cached (rebuilt when the Hornet instance changes).
- The camera is temporarily **re-centred on Hornet** for the render so the crop can never be clamped
  by the screen edge (the earlier "cut off at edges" bug).
- The published frame is a **fixed 320×320 crop**, so CU allocates its texture/sprite once and never
  reallocates (this removed the stutter — the old tight-to-alpha crop resized on every needle/pose).
- Silksong keys: **F1** = blank PNG, **F8** = diff PNG, **F2** = toggle blank/diff live.

### CU display (`src/LiveLink.cs`)
- Removed the second facing flip. CU's `Body.Flip()` flips the body **root transform scale**, which the
  `HornetLive` child inherited on top of the facing already in the captured pixels. The display now
  divides out the parent scale so its world scale is always positive.
- Frame-size guard self-heals (accepts a new stable size after ~20 rejects) instead of latching.
- **Ragdoll limbs are pinned to the root** in `PuppetTo` (captured offsets). CU's `PlayerCamera`
  follows the **average limb position**, so when only the root was moved the limbs (and the camera)
  sank below while the sprite stayed up — the "CU camera falls through the floor at a step" bug.
- `Talker.LateUpdate` positions the speech text at `body.limbs[0] + up*4`; patched to anchor to the
  body root while puppeting so it stops drifting with movement. (Needs `Unity.TextMeshPro` +
  `UnityEngine.UI` refs in the csproj.)

### Terrain mirror (`src/LiveLink.cs` `PublishTerrain`)
- Rectangles are now **greedily merged into maximal rectangles** (horizontal runs extended downward
  while identical), not one box per row. A wall/column becomes one tall collider, killing the
  internal-edge snags that popped Hornet off multi-block walls.
- Also mirrors **non-tilemap ground colliders** (placed structures/props/steps) clipped to the window —
  these are **not in `worldBlocks`**, so a block-only sampler left holes and she fell through.
- Emitted rects get a small `TerrainMargin`.

### Diagnostics (trim before shipping)
- CU: `LiveLink terrain: anchor/rects/minY/blockTop/geom/psSeq/pup`, `LiveLink camY/limbAvgY/limb0Y/rootY`,
  `LiveLink displays/avatars/visibleBodySprites`, wall-clock prefixed.
- Silksong: `TM diag` / `TM LOST GROUND` (with `floorTop/gap/k`), `TerrainMirror: re-disabled N ...`.

### Still open / known
- **Scale unification** (S4 §scaling) not done: CU still draws with manual `Scale=1.6`, `Ppu=64`; the
  capture PPU is still the main camera's, not a fixed one derived from Hornet's height.
- Vanilla terrain streaming is swept every 0.5 s (`SweepVanilla`), but the fall wasn't caused by it.
- The `k` used for the mapping is captured on F4 apply from the CU collider height; **do not apply F4
  while CU's player is crouched** (it was seen applying with `cuH=2.5` → `k=0.832`, world half-scale).

---

## RESUME HERE — next session (written 2026-10-03)

**Branch:** `passthrough-live`. Both games were **closed** at the end of the prior session; nothing is
left running. **S4 phase 3 is verified** (contact damage = 1 mask/contact; real nail damage).

**Working end-to-end today (all human-verified unless noted):**
input round-trip (S0) → terrain mirror on CU's real block grid (S1 + real-run fix) → CU puppet (S2) →
seams/watchdogs/moveset (S3) → **pogo off CU entities** (S4 P1) → **Hornet damages CU actors** (S4 P2) →
**actor → Hornet contact damage + real nail damage** (S4 P3) → **ground jump / double jump fixed** (S4) →
**blank-slate visual passthrough + stutter/flip/terrain-step/camera-text fixes** (S4 visual).
Real-run fall-through is fixed.

**Exact run recipe:**
1. Launch CU (`um win launch --steam 4576510`) and Silksong (`--steam 1030300`).
2. CU: content warning → Ctrl; `F9` tutorial → `F8` sandbox (or `F10` for a real run).
3. Silksong: START GAME → profile 1. In gameplay press **F4** to apply the mirror.
4. Play in CU. **F7** in CU spawns a pogo dummy below the player. CU's console (backquote) `spawn
   <id>` (e.g. `shadecrawler`) spawns real actors; slash/pogo/take contact from them.
5. **F3** in Silksong restores vanilla terrain. **F2** toggles blank/diff capture; **F1**/**F8** dump
   blank/diff PNGs. Watchdogs auto-release both ways.
- Automated `um win drive`: plain VKs work; `scanmode` only for the CU content-warning Ctrl. Synthetic
  jump timing is unreliable — prefer the human for jump verification.

**Immediate next steps (in priority order):**
1. **Scale unification** (S4 §scaling): fix the capture PPU from Hornet's height and derive CU's display
   scale from `k`; drop the manual `AvatarScale`/`Ppu` (currently `Scale=1.6`, `Ppu=64`).
2. Optional polish: pogo/hit FX feedback (`Bounce(id)` event), wall-cling test (needs CU walls), increase
   entity stream radius/size. **Trim the verbose diagnostics** added for the visual/step hunt
   (`LiveLink terrain`, `camY/limbAvgY`, `TM diag`, wall-clock prefixes) before showing off or publishing.

**Gotchas a new session must know:**
- The shared protocol is at **`Version = 8`** (`shared/PassthroughProtocol.cs`); both plugins compile it,
  so bump it whenever the layout changes. Region order: Header, CuState, FrameMeta, Pixels, Input,
  Terrain (header holds the mapping anchor), PlayerState, Entities, Events.
- **Entity ids:** real = `GetInstanceID() & 0xFFFFFF` (≥0); **debug dummies use negative ids**
  (`-(i+1)`). Do not reintroduce an `id >= 9000` style check.
- **Mapping origin** is CU ground-contact ↔ Hornet feet (`_cuOrigin`/`_silkOrigin` stored in
  `TerrainMirror`); keep it consistent for terrain, entities, and the puppet.
- Diagnostics currently enabled: Silksong state line every 30 frames (incl. `gnd`), `POGO!` on
  `DownspikeBounce`, `HURT!` on `HeroController.TakeDamage`, `Hit entity <id>` relay, CU damage lines.
  Trim when done.
- Every code change needs a **game restart** (BepInEx loads DLLs at startup). CU builds deploy to
  `%STEAM_LIBRARY%\...\BepInEx\plugins\HornetInCasualties`; Silksong to
  `%STEAM_DIR%\...\BepInEx\plugins\HornetExporter`.


## S5 task 1 - slash-arc VFX passthrough (2026-10-03, VERIFIED)

Protocol unchanged (v8). Slash arcs were missing for two reasons: the blank capture's culling mask
only had Player(9)|Hero Only(28), and `HideEffects` disabled every enabled non-body renderer under
Hornet (the NailSlash `MeshRenderer` is a child of the hero). Layer 17 is `HERO_ATTACK`
(`GlobalEnums/PhysLayers.cs`), hero-only, so adding it cannot drag in environment.

- **`silksong/src/HornetCapture.cs`:**
  - `_heroMask` now includes `1 << AttackLayer` (`AttackLayer = 17`).
  - `HideEffects` skips enabled renderers on layer 17, so an active slash survives the effect
    suppression (light/glow/dust still hidden).
  - Framing unchanged: the camera still recentres on Hornet's transform, so the pivot stays exactly
    `(0.5, 0.5)` and CU placement is unaffected. The arc fits inside the fixed 320 crop.
- **CU unchanged:** with the camera staying on Hornet's transform the published pivot is always exactly
  `(0.5, 0.5)`, so `ApplyFrame` needs no pivot handling.
- `BlankCrop` stays **320** on purpose: the protocol's single-buffer seqlock tears at 384² (S4 lesson
  3). The arc fits 320, so no bump needed.
- **First attempt regressed and was reverted:** recentring the camera on the union of body + *all*
  enabled layer-17 renderers moved the camera away from Hornet. Hornet has ~25 layer-17 renderers
  (every crest's Slash/AltSlash/UpSlash/… variants, many on inactive objects whose `bounds` sit
  elsewhere), so the fixed crop missed her entirely (`capture returned no frame xN`, body=1 effects=417).
  Do **not** frame on "all enabled layer-17 renderers"; keep the camera on her transform. The
  HeroAttack mask + `HideEffects` exception alone is what surfaces the arc.
- **Verified:** drove CU (hold right + left-click) and recorded the window; Hornet's log shows
  `SlashAlt`/`Slash` from the forwarded `BtnAttack`, and CU frames show the full white crescent around
  her only during the swing (needle/idle before and after). Fits the 320 crop, no clipping.
- Builds clean, both plugins deployed.

## S5 task 1 follow-up - 640 crop + double-buffered Hornet pixels (2026-10-03, VERIFIED)

The 320 crop still clipped the slash crescent on the human's display, so the fixed crop was raised
**320 -> 480 (+50%)** and then **-> 640**, because at 480 a side slash still reached the edge. The
extra ring around Hornet is transparent (she renders the same pixel size) and the camera stays on her
transform, so CU's display scale/placement is unchanged.

A 640^2 RGBA frame is ~1.6 MB, past the S4 lesson-3 tearing threshold, so **protocol v9 double-buffers
the Hornet pixels**:
- `shared/PassthroughProtocol.cs`: `Version 9`; `FrameMetaSize` 64 -> 80; `FO_BufIndex = 64`;
  `PixelsOffset` follows FrameMeta (now 208); `MaxWidth/Height = 640`; `PixelsSize` is one buffer and
  `InputOffset = PixelsOffset + PixelsSize * 2` (`PixelsBuffers = 2`).
- `WriteFrame` writes pixels into the **off** buffer *outside* the meta lock, then publishes the
  metadata and flips `FO_BufIndex` under a short seqlock. The reader (`ReadFrame`) reads the committed
  buffer, then re-checks the seq. The big copy no longer sits inside the lock, so the reader is not
  rejected mid-copy.
- `src/LiveLink.cs` (CU): frame guard uses `Proto.MaxWidth/MaxHeight` instead of a hard 320.
- `silksong/src/HornetCapture.cs`: `BlankCrop = 640`; new **edge diagnostic** logs a warning whenever
  alpha content reaches within 2px of the crop edge (idle Hornet sits deep inside, so only a slash can
  trigger it).

**Do NOT try to auto-frame the arc.** Two attempts to recentre the camera on the union of body + layer
17 renderers failed: Hornet carries dozens of Slash/crest variants as children, and even filtering to
`enabled && activeInHierarchy` still left attack renderers pulling the union off her, so the capture
returned no frame (`capture returned no frame xN`). A big fixed crop centred on her transform is the
reliable fix.

**Verified:** restarted both games; drove left/right/up/down slashes. Silksong logs
`publishing isolated frames 640x640 (blank)` with no `no frame` and **no `crop edge` warning**; CU logs
`received live Hornet frame 640x640`, `w=640 hasPx=1`, `fid` advancing, no size rejects.
