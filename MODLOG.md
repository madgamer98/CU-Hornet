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
- [ ] Decompile CU `Assembly-CSharp.dll`; find the player controller, input, sprite/animation + body/species
      system, and any console command registration.
- [ ] Decompile Silksong `Assembly-CSharp.dll`; find `HeroController`, `tk2dSpriteAnimator`, `tK2DSprite`
      collection names, and where Hornet's animation clips/atlases live.
- [ ] Set up the BepInEx plugin csproj against CU's Managed assemblies.
- [ ] Vertical slice: show a Hornet sprite on the CU player (idle), then one animation.
- [ ] Widen: movement set, then moveset.
- [ ] Verify in the demo + record.
