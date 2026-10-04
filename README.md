# CU-Hornet

<!--
  Demo GIF is hosted on GitHub rather than committed (keeps the repo lean).

  To add it: open this README in GitHub's web editor (pencil icon), drag the clip in (or upload it in
  an issue comment) to get a https://github.com/user-attachments/assets/... URL, then paste that URL
  below and uncomment the line.

  ![Hornet playing through Casualties: Unknown](PASTE_USER_ATTACHMENTS_URL_HERE)
-->

Hornet from Hollow Knight: Silksong runs inside Casualties: Unknown as the real playable character,
driven by Silksong itself. Silksong stays the authority on Hornet, and Casualties: Unknown hosts the
world. Input round-trips between the two: Hornet's body, attack VFX and HUD are streamed into CU, CU's
terrain and actors are mirrored back into Silksong, and both games keep health and damage in sync.

## What works

- Input round-trip: CU's buttons drive Hornet's real controller.
- Terrain mirror: Hornet collides with CU's world, including blocks, merged walls and non-tile ground.
- Puppet: CU's body, ragdoll and camera follow Hornet.
- Pogo and combat: Hornet pogoes off CU entities and damages them. CU biters damage her, one mask per
  contact with knockback, and hitting CU enemies grants Silk.
- Visual passthrough: a blank-slate capture of Hornet, including her slash arcs, plus Silksong's HUD
  drawn over CU.
- Health authority: Silksong's health, silk and geo win. While mirrored, CU's own damage is pinned off,
  and when Silksong dies the CU run ends.
- Controls: in CU, `J` slashes, `K` dashes, `L` throws the needle and `H` binds. Movement and jump use
  CU's own binds.

## How it works

Two BepInEx plugins talk over a named shared-memory mapping.

- `silksong/` (`HornetExporter`) is the guest. It captures Hornet and the HUD, mirrors CU's terrain and
  actors as colliders, injects the host's input into Hornet's real actions, gates damage, and publishes
  vitals.
- `src/` (`HornetInCasualties`) is the host. It forwards input, publishes terrain and nearby actors,
  puppets the CU body, and draws Hornet and the HUD.

The wire format is `shared/PassthroughProtocol.cs`, compiled into both plugins (currently v11): Header,
CuState, FrameMeta (double-buffered pixels), Input, Terrain, PlayerState, Entities, Events, Vitals, Hud.

The running history and design notes are in [`MODLOG.md`](MODLOG.md).

## Build

You need both games and BepInEx 5 installed.

1. Copy `LocalProps.props.example` to `LocalProps.props` (gitignored) and set your install paths for
   `%CU_GAME_DIR%` and `%SILKSONG_DIR%`.
2. Build the CU plugin from the repo root: `dotnet build -c Release`. It deploys to
   `...\BepInEx\plugins\HornetInCasualties`.
3. Build the Silksong plugin: `dotnet build -c Release` in `silksong/`. It deploys to
   `...\BepInEx\plugins\HornetExporter`.
4. Restart both games. BepInEx loads plugins at startup.

Add `-p:DeployToGame=false` to build without deploying.

## Run

1. Launch Casualties: Unknown and Hollow Knight: Silksong.
2. In CU, start a run or course.
3. In Silksong, during gameplay, press F4 to apply the mirror. F3 restores vanilla terrain, and F7
   dumps Hornet's hierarchy to the log.
4. Play in CU.

## Layout

| Path | What |
|---|---|
| `shared/PassthroughProtocol.cs` | The shared-memory wire format (both plugins). |
| `src/` | The Casualties: Unknown (host) plugin. |
| `silksong/` | The Silksong (guest) plugin. |
| `MODLOG.md` | Running journal and field notes. |

## Notes

- Single-player and offline only. Nothing touches anti-cheat.
- Both games must keep simulating; the plugins enable run-in-background.
- Open items are tracked in [`MODLOG.md`](MODLOG.md).
