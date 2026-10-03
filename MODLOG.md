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

## Next
- Decide on the Silksong-side frame exporter (recommended) vs continuing to reverse tk2d's atlas packing.
- Then Hornet's moveset (Stage 2).
