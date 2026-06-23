# ESP Players (ModSharp / CS2)

Per-viewer, opt-in player **ESP / glow outlines** for Counter-Strike 2 servers running
[ModSharp](https://github.com/Kxnrl/modsharp-public).

A player toggles ESP with a chat command and, **only for that player**, other players are
drawn with a team-coloured glow outline. Visibility is filtered independently for every
viewer through the transmit system, so players who have **not** enabled ESP never receive
the glow entities at all.

> This is a **server-operator-controlled** feature. Whether it is available, who may use it,
> and how it behaves are entirely up to the server admin via ConVars (see below). It can be
> locked behind an admin permission, restricted to dead/spectator viewers, or disabled
> outright.

## Credit

Ported from **[oqyh/cs2-ESP-Players-GoldKingZ](https://github.com/oqyh/cs2-ESP-Players-GoldKingZ)**
(a CounterStrikeSharp plugin) to ModSharp. All original design credit to oqyh / GoldKingZ.

## How it works

For each alive player, two `prop_dynamic` entities cloning the player's model are spawned:

- a **relay** (`rendermode = kRenderNone`, invisible) that follows the player's pawn, and
- a **glow** (`glowstate = 3`, team-coloured) that follows the relay.

Both entities are hooked **hidden from everyone by default**
(`ITransmitManager.AddEntityHooks(entity, false)`). For each viewer the glow is then opened
or closed per-viewer via `ITransmitManager.SetEntityState(entityIndex, viewerControllerIndex,
canSee, -1)`. A viewer only sees a glow when **all** of these hold:

- the viewer is not the glow's owner,
- the viewer has ESP toggled on,
- the viewer passes the `esp_show_for` gating (any / dead-only / spectators-only),
- enemy-team-only filtering allows it (`esp_show_only_enemy_team`), and
- GOTV filtering allows it (`esp_disable_on_gotv`).

Everything is **event-driven** (player spawn / death / team change / toggle), so there is no
per-tick scan.

## Commands

| Command | Description |
| --- | --- |
| `!esp` / `!glow` / `!showplayers` | Toggle ESP for yourself (also `css_*` aliases). |

The per-player choice is persisted via ModSharp's **ClientPreferences** (cookie key
`esp_enabled`), so it survives reconnects and restarts.

## Configuration

ConVars are written to an editable file at `sharp/configs/esp.cfg` on first run; edits there
are re-applied on every restart.

| ConVar | Default | Description |
| --- | --- | --- |
| `esp_enabled` | `1` | Master switch. |
| `esp_glow_color_ct` | `0,190,255` | Glow color for CT (R,G,B). |
| `esp_glow_color_t` | `243,0,93` | Glow color for T (R,G,B). |
| `esp_glow_range` | `5000` | Max glow render range in units (0 = always visible). |
| `esp_show_only_enemy_team` | `1` | Viewer sees only enemy-team glows. |
| `esp_show_for` | `0` | Who may see ESP: 0 = any, 1 = dead only, 2 = spectators only. |
| `esp_disable_on_gotv` | `0` | Hide glows from GOTV / HLTV viewers. |
| `esp_default_toggle` | `0` | Default ESP state for players with no saved preference. |
| `esp_toggle_permission` | `` (empty) | Admin permission required to toggle (empty = everyone). e.g. `@espplayers/use`. |
| `esp_hide_command` | `0` | Suppress the toggle command from chat. |

## Building

```bash
dotnet build EspPlayers.slnx -c Release
```

Output lands in `.build/` (`modules/EspPlayers/EspPlayers.dll` + `locales/esp.json`), ready
for `modsharp-deploy`.

## Dependencies

Optional ModSharp modules, resolved at runtime if present:

- **ClientPreferences** — persists the per-player toggle (without it, the toggle works for
  the current session but does not save).
- **LocalizerManager** — localized chat messages (falls back to keys if absent).
- **AdminManager** — only needed when `esp_toggle_permission` is set.
