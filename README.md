# CU-Hornet

<!--
  Demo GIF is hosted on GitHub rather than committed (keeps the repo lean).

  To add it: open this README in GitHub's web editor (pencil icon), drag the clip in (or upload it in
  an issue comment) to get a https://github.com/user-attachments/assets/... URL, then paste that URL
  below and uncomment the line.

  ![Hornet playing through Casualties: Unknown](PASTE_USER_ATTACHMENTS_URL_HERE)
-->

Play **Hollow Knight: Silksong**'s Hornet inside **Casualties: Unknown** — the real, playable Hornet,
not a sprite swap. Silksong keeps running as the authority on Hornet; Casualties: Unknown hosts the
world. Input round-trips between them, Hornet's body, attack VFX and HUD are streamed into CU, CU's
terrain and actors are mirrored back into Silksong, and the two games reconcile health and damage.

## What works

- **Input round-trip** — CU's buttons drive Hornet's real controller.
- **Terrain mirror** — Hornet collides with CU's world (blocks, merged walls, non-tile ground).
- **Puppet** — CU's body, ragdoll and camera follow Hornet.
- **Pogo & combat** — Hornet pogoes off CU entities and damages them; CU biters damage her (one mask
  per contact, with knockback); hitting CU enemies grants Silk.
- **Visual passthrough** — a blank-slate capture of Hornet (including her slash arcs) plus Silksong's
  HUD overlaid in CU.
- **Health authority** — Silksong's health/silk/geo is the source of truth. While mirrored, CU's own
  damage is pinned off; when Silksong dies, the CU run ends.
- **Controls** — in CU: `J` slash, `K` dash, `L` needle, `H` bind; movement and jump use CU's own binds.

## How it works

Two BepInEx plugins talk over a named shared-memory mapping:

- **`silksong/`** (`HornetExporter`) — the guest. Captures Hornet and the HUD, mirrors CU's
  terrain/actors as colliders, injects the host's input into Hornet's real actions, gates damage, and
  publishes vitals.
- **`src/`** (`HornetInCasualties`) — the host. Forwards input, publishes terrain and nearby actors,
  puppets the CU body, and draws Hornet and the HUD.

The wire format is `shared/PassthroughProtocol.cs`, compiled into both plugins (currently **v11**):
Header, CuState, FrameMeta (double-buffered pixels), Input, Terrain, PlayerState, Entities, Events,
Vitals, Hud.

The running history and design notes live in [`MODLOG.md`](MODLOG.md); feature scopes are in
[`S4-SCOPE.md`](S4-SCOPE.md) and [`S5-SCOPE.md`](S5-SCOPE.md).

## Build

Requires both games and BepInEx 5 installed.

1. Copy `LocalProps.props.example` to `LocalProps.props` (gitignored) and set your install paths for
   `%CU_GAME_DIR%` and `%SILKSONG_DIR%`.
2. Build the CU plugin from the repo root: `dotnet build -c Release` (deploys to
   `...\BepInEx\plugins\HornetInCasualties`).
3. Build the Silksong plugin: `dotnet build -c Release` in `silksong/` (deploys to
   `...\BepInEx\plugins\HornetExporter`).
4. Restart both games — BepInEx loads plugins at startup.

Add `-p:DeployToGame=false` to build without deploying.

## Run

1. Launch **Casualties: Unknown** and **Hollow Knight: Silksong**.
2. In CU, start a run or course.
3. In Silksong (in gameplay), press **F4** to apply the mirror. **F3** restores vanilla terrain;
   **F7** dumps Hornet's hierarchy to the log.
4. Play in CU.

## Layout

| Path | What |
|---|---|
| `shared/PassthroughProtocol.cs` | The shared-memory wire format (both plugins). |
| `src/` | The Casualties: Unknown (host) plugin. |
| `silksong/` | The Silksong (guest) plugin. |
| `MODLOG.md` | Running journal / field notes. |
| `S4-SCOPE.md`, `S5-SCOPE.md` | Feature scopes. |

## Notes

- Single-player / offline only. No anti-cheat is touched.
- Both games must keep simulating; the plugins enable run-in-background.
- Open items are tracked in [`MODLOG.md`](MODLOG.md).
