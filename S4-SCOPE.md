# S4 scope — entities, pogo, damage sync, clean passthrough, scale

Forward-looking scope for the next milestone after S0–S3 (see `MODLOG.md`). S0–S3 made the M1 loop
work: CU input → Silksong's real Hornet controller on CU-mirrored static `Ground` terrain → mapped
position back → CU body/camera/render. S4 adds **living things** (enemies/hazards), **damage**, and a
**clean visual passthrough** with correct **scale**.

## Goals
1. **Active pogo off CU enemies** — Hornet's down-spike bounces off a CU enemy, and that enemy takes
   Hornet's damage.
2. **Damage sync, both ways** — Hornet's attacks damage CU actors through CU's own damage system;
   CU hazards/contact damage Hornet through Silksong's own health/i-frame system.
3. **Clean visual passthrough** — Silksong renders *only Hornet's layers* on a transparent background
   (no environment), and CU composites her over CU's world.
4. **Correct scaling** — the streamed Hornet matches CU's world scale deterministically; no manual
   `Scale`/`Ppu` fudging.

## What the decomp says (mechanics we build on)
- **Pogo path:** `HeroDownAttack.OnTriggerEnter2D` and `OnHitResponded` → `ContinueBounceTrigger` →
  `attack.QueueBounce()` → `HeroController.DownspikeBounce()` (public, HeroController.cs:6373).
  - Layer `ENEMIES` (11) only triggers the bounce if `DamageEnemies.DoDamage` succeeds → needs an
    `IHitResponder` (a `HealthManager`).
  - Layers `INTERACTIVE_OBJECT` (19) and `HERO_ATTACK` (17) call `ContinueBounceTrigger` **directly** —
    a collider there pogoes without a `HealthManager`.
  - `NonBouncer.active` or a `BounceBalloon` component suppresses the bounce.
- **Hornet takes damage:** `HeroBox` is a trigger and reacts to `DamageHero` components
  (`HeroController.TakeDamage(...)`, public, :5280). Native i-frames/knockback come for free if a proxy
  carries a `DamageHero`.
- **Hornet deals damage:** her slash/needle carry `DamageEnemies` (`NailAttackBase.EnemyDamager`) which
  raises `HitResponded` / `WillDamageEnemy`.
- **Layers** (`PhysLayers`): TERRAIN=8, ENEMIES=11, HERO_ATTACK=17, INTERACTIVE_OBJECT=19, SOFT_TERRAIN=25.
- **Lighting:** `HeroLight` (sprite-based), `SceneColorManager`, `AmbientLightAnimator`. Sprites likely
  depend on global lighting state the main camera sets — the earlier dedicated capture camera came back
  dark (MODLOG S1/passthrough notes).

## Architecture — entity proxies + a bidirectional event channel
Extend the shared-memory protocol (v5) with two new channels, reusing the S2 fixed mapping
(`silk = _silkOrigin + k*(cu - _cuOrigin)`).

### CU → Silksong: `Entities`
A revisioned array of nearby CU actors (radius around the player), each record:
`id, kind (enemy/hazard/node), x, y, w, h, hp, maxHp, flags, facing`.
Flags: `Bounceable`, `ContactDamage`, `HazardSpikes`, `NonBounce`, `Alive`.
Silksong rebuilds/updates a pool of proxy GameObjects at the mapped positions (same pool pattern as
terrain).

### Silksong → CU: `Events`
An SPSC ring of discrete events, each tagged with the entity `id` and a sequence:
- `HitEntity(id, damage, dirX, dirY, sourceKind)` — Hornet hit it.
- `HeroDamaged(amount, hazardType, dirX, dirY)` — optional, if we don't use a proxy `DamageHero`.
- `Bounce(id)` — feedback for FX.
CU drains the ring each frame and applies to its own systems (`BuildingEntity.health`, `Animal` via
`AnimalHit`, `WorldGeneration.CreateDamageNumber`).

### Pogo proxy design (recommended: lightweight, no real `HealthManager`)
Put the proxy's collider on **layer 17 (`HERO_ATTACK`)** or **19 (`INTERACTIVE_OBJECT`)** so
`HeroDownAttack.ContinueBounceTrigger` fires without a full `HealthManager` (which is heavyweight:
`IInitialisable`, serialized drop tables, FSM). Add:
- optional `DamageHero` (hazardType=ENEMY, `damageDealt` from CU) so Hornet's contact damage/i-frames are
  native; and
- a tiny relay `MonoBehaviour` (`ProxyHitRelay`) that reports "Hornet hit entity id" into the Events ring.

**Open question to prototype first (cheap, isolated):** exact layer + trigger matrix so the proxy and
Hornet's attack reliably generate a bounce and a attributable hit without self-triggering
(Hornet's attack is also layer 17). A one-afternoon probe: spawn a proxy, down-spike it, log
`DownspikeBounce` + which collider. If 17 self-collides are a problem, try 19, or add a real
`HealthManager` as fallback.

## Visual passthrough — the "blank slate"
Goal: a RenderTexture of Hornet's renderers only, correct colors, alpha-0 clear.

- **A. Dedicated capture camera (target).** `cullingMask = Player(9) | Attack(17) | Particle(18) |
  Hero Only(28) | …`; `clearFlags = SolidColor` with alpha 0; orthographic framed on Hornet at a fixed
  PPU. Excludes Terrain(8)/Enemies(11)/Interactive(19). Risks: lit-shader globals and the hero light;
  may need to render during the main camera's pre-render so global lighting is valid, and/or force an
  unlit material for capture.
- **B. Hide environment renderers / blank test scene.** Simple, invasive, can glitch.
- **C. Keep the current main-camera diff** but fix color (un-premultiply against the background frame).
  Lowest risk, not a true blank slate.

Recommendation: prototype **A** to a PNG (existing F8 path) as an isolated experiment; keep **C** as the
fallback. Confirm whether the dark render is a lighting-global issue or a material issue before
committing.

## Scaling — one source of truth
- Today: capture ortho size = Hornet bounds + padding (varies per frame); CU draws with `Ppu=64`,
  `Scale=1.6` (manual).
- Target: fix the capture PPU (ortho size from Hornet's height × a fixed margin; PPU = RTHeight /
  (2·orthoSize)). Then CU derives display scale from `k` and CU's own PPU, and sets the sprite's
  `pixelsPerUnit` so Hornet's rendered height equals her collider height in CU units
  (`k · cuPlayerHeight`). Remove manual `Scale`/`Ppu`; keep one calibration constant.

## Protocol changes (v5)
- `Entities` region (CU→Silk): header (revision, count, seq) + fixed records.
- `Events` ring (Silk→CU): SPSC ring (type, entityId, a/b/c values, seq).
- Bump `Version`; if entity count grows, consider a second mapping file.

## Phased plan
1. **Pogo slice** — ✅ **done/verified** (2026-10-03): entities stream + layer-19 pogo proxies; real enemy pogo confirmed.
2. **Hornet → actor damage** — ✅ **done/verified**: `Events` ring + `ProxyRelay`; CU applied damage/killed a shadecrawler. Follow-up: use real nail damage instead of fixed 5.
3. **Actor → Hornet damage** — ✅ **done/verified** (2026-10-03): explicit `ContactDamage` calls
   `HeroController.TakeDamage` once per HeroBox entry (a proxy `DamageHero` caused a sticky-buffer
   cascade, 9 masks in one contact — do not use it). CU flags biters via `SpiderHandler` and dedupes
   actors. Nail-damage follow-up done (`ProxyRelay` reads `DamageEnemies`/nail damage).
4. **Blank-slate capture** — ⬜ **deferred by human** (do after 3): dedicated camera → PNG; solve lighting; switch CU to consume it.
5. **Scale unification** — ⬜ **deferred**: fixed capture PPU; derive CU display scale from `k`; delete manual tuning.

Protocol is actually at **v8** (v5 + terrain anchor + player state + larger rect/entity caps).

## Risks / unknowns
- Lightweight proxies may not cover parry/charge/status interactions that expect a `HealthManager`.
- Layer/trigger matrix for the pogo proxy (17 vs 19 vs real `HealthManager`).
- Capture-camera lighting fidelity is the biggest visual unknown.
- Entity churn/id stability and moving colliders; refresh cadence vs latency for damage events.
- Performance of streaming entities + terrain every frame.
