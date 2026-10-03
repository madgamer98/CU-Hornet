# S5 scope — slash VFX, Silksong HUD, and health authority

Plan written 2026-10-03. Start a fresh agent session in `%PROJECT_DIR%` and follow this top to bottom.

## Status snapshot (starting point)

- **Branch `passthrough-live`, commit `b4991bf`** (working tree clean). Everything below starts from
  here. A previous scale-unification attempt was **reverted** — do NOT re-introduce it without
  reading "Lessons" below.
- **Protocol `Version = 8`** (`shared/PassthroughProtocol.cs`). Layout constants at v8:
  `FrameMetaSize=64`, `PixelsOffset=192` (single 384×384 pixel buffer), then Input, Terrain,
  PlayerState, Entities, Events.
- **Working end-to-end (human-verified):** input round-trip → terrain mirror (greedy-merged block
  rects + non-block ground colliders) → CU puppet (root **and limbs** pinned) → pogo → Hornet↔CU
  damage → blank-slate Hornet capture with **manual** display scaling (`AvatarScale=1.6`, sprite PPU=64).
- **Blank-slate capture** (`silksong/src/HornetCapture.cs`): renders Hornet alone on alpha-0 using the
  game's own main camera with `cullingMask = Player(9) | HeroOnly(28)` (`_heroMask`, ~line 275),
  hides all non-body renderers (`HideEffects`, ~line 657), fixed `BlankCrop=320` centred on her
  transform, publishes RGBA + pivot. `LiveLink` streams it every frame; CU draws it on a
  `SpriteRenderer` (`src/LiveLink.cs` `ApplyFrame`).

### Mandatory reference reads first
- `MODLOG.md` — full history; the "S4 visual" section and "RESUME HERE" in particular.
- `S4-SCOPE.md` — original S4 goals (visual passthrough, scale).
- `skills/mod-any-game/SKILL.md` (in the toolkit repo `%TOOLKIT_DIR%`) for the loop.

### Run / build recipe
- Build CU: `dotnet build -c Release` in `%PROJECT_DIR%` (auto-deploys to
  `%CU_GAME_DIR%\BepInEx\plugins\HornetInCasualties`).
- Build Silksong: `dotnet build -c Release` in `%PROJECT_DIR%\silksong` (auto-deploys to
  `%SILKSONG_DIR%\BepInEx\plugins\HornetExporter`).
- **Every code change needs both games restarted** (BepInEx loads DLLs at startup).
- Launch: `um win launch --steam 4576510` (CU), `--steam 1030300` (Silksong); from
  `%TOOLKIT_DIR%` use `.\bin\um ...`. Kill by exact PID (`um win kill <pid>`).
- CU: content warning → Ctrl; `F9` tutorial → `F8` sandbox (or `F10` real run).
- Silksong: START GAME → profile 1; in gameplay **F4** applies the mirror, **F3** restores;
  **F1** dumps `hornet_blank.png`, **F8** dumps the diff PNG.
- Verifying needs the human: synthetic jump/contact timing is unreliable. Use `um win shot`/`um win
  record` + `um video contact` (ffmpeg at `%FFMPEG%`) to capture frames.

### Lessons that must not be relearned (from the reverted attempt)
1. **Manual scale stays until Task 2 is done on purpose.** A "publish capture PPU, derive display PPU"
   scheme was tried and reverted. If you revisit it, do it as its own task with human verification.
2. **Do not shrink or fix the crop naively.** Tight-to-alpha crops changed size every frame and made
   CU reallocate its texture (stutter); the fixed 320 crop fixed that. If the crop must change,
   keep it **fixed-size** and make CU recreate the sprite only on a **large** size/ppu delta.
3. **Seqlock is wrong for a big payload.** With a 384² RGBA frame the writer is mid-copy almost every
   read and CU rejects frames (`ReadFrame miss x360`). Use **double-buffered pixels** (write the off
   buffer, flip an index under a short lock) for any large region (HUD especially).
4. **CU's camera follows the average ragdoll limb position** (`PlayerCamera` ~line 2078). The puppet
   must move the limbs with the root or the camera sinks. `PuppetTo` already pins `_body.limbs`.
5. **CU `Body.Flip()` flips the root transform scale**; the HornetLive child inherits it. The current
   code divides out the parent scale so the display is never mirrored by CU.
6. **Don't apply F4 while CU's player is crouched** — `k` is captured from the CU collider height.

---

## Task 1 — Slash-arc VFX passthrough (simplest of the two, do first)

**Goal:** Hornet's slash arcs render in CU (needle throw is explicitly out of scope for now).

**Why they're missing:** `HideEffects` disables every non-body renderer, and the capture culling mask
only has layers 9 and 28, while slash arcs are on **layer 17 (Attack)**.

**Change (all in `silksong/src/HornetCapture.cs`):**
1. Add layer 17 to `_heroMask` (`1 << 9` → `(1 << 9) | (1 << 17)` plus `1 << 28` if named).
   Layer 17 is hero-only, so no environment bleeds in.
2. Change `HideEffects` so it does **not** hide active layer-17 renderers. Pin the rule to a
   constant (e.g. `AttackLayer = 17`); hide every other non-body renderer that is enabled (keeps the
   light/glow/dust suppression).
   - `_effectCache` is rebuilt in `EnsureHeroMask` when the Hornet instance changes; keep that.
3. **Framing:** frame the crop on the union of body renderers **and enabled layer-17 renderers** so
   the arc isn't clipped, but keep the pivot on Hornet's **collider centre** (the S2 mapping point)
   so placement stays aligned. Keep `BlankCrop` fixed (bump to 384 only if the arc clips; then read
   `MaxWidth/MaxHeight` and raise them together, and raise CU's `ApplyFrame` guard to match).
4. Keep the crop **fixed-size** (lesson 2).

**Verify:** swing in CU and screenshot. The arc should appear around Hornet only during the swing.

**Risks:** if layer 17 somehow includes non-hero objects (unlikely), the arc capture will reveal it.
If the arc clips, enlarge the fixed crop and update CU's guard in the same commit.

---

## Task 2 — Silksong HUD + Silksong health as source of truth

**Goal:** show Silksong's HUD in CU; Silksong's health is the source of truth; dying in Silksong ends
the CU run; CU's own damage handling is inert.

Split into 4 milestones. Protocol changes land per milestone with a `Version` bump; rebuild+restart
both games each time.

### 2A. Vitals channel (proto bump)
- New small region `Vitals` (Silk → CU), seqlock-guarded:
  `health, maxHealth, healthBlue, silk, silkMax, geo, dead` (ints/floats as fits).
  Source: `PlayerData.instance` — `health`, `maxHealth`, `healthBlue`, `silk`, `silkMax`, `geo`
  (`PlayerData.cs` ~129–170); `dead` from `HeroController.instance.cState.dead` or `health == 0`.
- Silksong `LiveLink.Update` writes it every frame (cheap).

### 2B. HUD by capture (matches the Hornet capture approach) — **half-res, publish-on-change**
- Silksong: render `GameCameras.instance.hudCamera` (`GameCameras.cs` ~8; cullingMask = 32 → layer 5)
  to an RT with a transparent clear, same save/restore pattern as `CaptureBlankRgba`. Read pixels.
- **Half-resolution** RT (e.g. `Screen.width/2 × Screen.height/2`) and **publish only when the vitals
  hash changes** (masks/silk/geo), not every frame. Cache the last published frame.
- **Double-buffer** the HUD pixels (lesson 3) — the region is screen-sized.
- CU: new `Hud` overlay (a UI `RawImage`/`Image` on a screen-space canvas, or a camera-anchored
  `SpriteRenderer` in front) drawn over the CU world, scaled to the screen.
- Handle the HUD camera being inactive/off-screen gracefully (skip the frame).

**Bandwidth guardrail:** do NOT stream the HUD every frame at full res — that is the stutter trap.
On-change at half res is the intended shape.

### 2C. Disable CU damage (pin the whole damage surface)
- Harmony **postfix on `Body.Update`** (and `LateUpdate` for safety) while `LiveLink.Puppeting`:
  restore health every frame so no CU source can accumulate damage:
  `brainHealth`, each `limb.skinHealth/muscleHealth/bleedAmount/infectionAmount/pain`,
  `bloodVolume`, `venomTotal`, `internalBleeding`, `hemothorax`, `traumaAmount`,
  `radiationSickness`, `sicknessAmount` to their healthy values.
- **Gate:** if Silk is dead, stop pinning and force `brainHealth = 0` (see 2D) so death isn't undone.
- Decision default: pin the **damage** fields only; leave the survival meters (hunger/thirst/temperature)
  themselves running (their damage is reverted each frame). Flip to "pin everything" if moodles get noisy.
- CU's `Body.FixedUpdate` is already skipped while puppeting (`BodyFixedUpdatePatch`).
- Cosmetic wound/moodle UI can stay; suppress `WoundView` updates later if it flickers.

### 2D. Death sync (Silk → CU run end)
- CU reads `dead` from `Vitals`. On dead: set `body.brainHealth = 0` → `body.alive` false
  (`Body.cs:591`) → `PlayerCamera.HandleDeathScreen()` (~2493) ends the run.
- Stop the damage pin when dead so `brainHealth=0` sticks.
- CU→Silk: with damage pinned CU shouldn't die on its own. If it somehow does, treat it as a run end
  too (and release the puppet).
- Silksong keeps its own death animation; do not attempt to respawn it. Decide/reset strategy for a
  new run in a later task.

### 2E. Neutralize Silksong's own hazards/enemies — **recommended: gate the damage intake**
- **Do NOT load an empty/test scene.** Silksong's `HeroController` is coupled to its scene
  (PlayMaker FSMs, TeamCherry services, its own world/collision — the same reason it can't run
  detached). Loading an empty scene would likely break her.
- **Do NOT enumerate and disable hazards/enemies** — brittle across streamed scenes.
- **Instead**, while the mirror is active, patch the centralized intake points so only our CU proxies
  can hurt her:
  - `HeroController.TakeDamage` (~5280): allow only when `go` carries our proxy marker; else return.
    Add a tiny marker component to our `ContactDamage` proxies (CU-side proxies in Silksong,
    `silksong/src/ContactDamage.cs`) — e.g. `ContactDamage` itself is the marker.
  - `DoSpecialDamage` / `TakeChompDamage` / `TakeFrostDamage` (ambient status) → return while mirrored.
  - `DieFromHazard` / `HazardRespawn` (pits/acid/lava) → return while mirrored.
- This is centralized and thorough for damage regardless of what Silksong's world contains.
- If **physical** interference from Silksong enemies shows up (they collide on layer 11), additionally
  disable layer-11 colliders while mirrored — defer until observed.

---

## Protocol changes (summary)
- Bump `Version` for each of: `Vitals` region (2A) and `Hud` region (2B).
- Add regions **after** `Events` to avoid disturbing existing offsets where possible, or shift the
  derived offset constants consistently.
- **Double-buffer both the HUD pixels and (if ever enlarged) the Hornet pixels.** Single-buffer
  seqlock tearing is a known failure for 384²+ payloads.
- Keep both plugins compiling `shared/PassthroughProtocol.cs` (CU includes it by default; Silksong
  includes it explicitly in its csproj).

## Decomp references
- Silksong: `GameCameras.cs` (`hudCamera`), `HUDCamera.cs`, `PlayerData.cs` (vitals fields),
  `HeroController.cs` (`TakeDamage` ~5280, `DoSpecialDamage` ~5184, `TakeChompDamage` ~5166,
  `DieFromHazard`, `HazardRespawn`, `cState.dead` in `HeroControllerStates.cs:101`).
- CU: `Body.cs` (`alive` :591, damage fields throughout), `PlayerCamera.cs`
  (`HandleDeathScreen` ~2493, camera-follows-limbs ~2078), `Limb.cs` (health fields),
  `WoundView.cs`, plus the many damage sources (BearTrap, BarbedFence, Crystal*, …).

## Milestones (ordered)
1. **Slash arcs visible in CU** (Task 1). Human screenshot verify.
2. **Vitals channel + CU HUD overlay** (2A + 2B). Verify HUD matches Silksong on damage/silk use.
3. **CU damage pinned off + proxy-only damage gate** (2C + 2E). Verify CU spikes/enemies can't hurt
   her and CU stays up; Silksong masks still drop from mirrored biters.
4. **Death sync** (2D). Verify draining masks in Silksong ends the CU run.

## Verification log discipline
- Add each change to `MODLOG.md` (the journal) as you go; it becomes the field note.
- Before finishing, trim the verbose diagnostics added during earlier debugging
  (`LiveLink terrain`, `camY/limbAvgY`, `TM diag`, wall-clock prefixes) — mark them clearly so they
  are easy to remove.
- Commit per milestone on `passthrough-live`.
