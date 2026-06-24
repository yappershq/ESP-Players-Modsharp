<div align="center">
  <h1><strong>EspPlayers</strong></h1>
  <p>Server-controlled, per-viewer player glow / ESP for ModSharp (CS2).</p>
</div>

<p align="center">
  <a href="https://github.com/Kxnrl/modsharp-public"><img src="https://img.shields.io/badge/framework-ModSharp-5865F2?logo=github" alt="ModSharp"></a>
  <img src="https://img.shields.io/badge/game-CS2-orange" alt="CS2">
  <img src="https://img.shields.io/github/license/yappershq/ESP-Players-Modsharp" alt="License">
  <img src="https://img.shields.io/github/stars/yappershq/ESP-Players-Modsharp?style=flat&logo=github" alt="Stars">
</p>

---

EspPlayers gives players a team-coloured glow outline that only viewers who opt in (and pass the configured gating) can see — useful for spectators, GOTV, dead players, or anywhere a server wants controllable wallhack-style ESP. It is a ModSharp/CS2 port of [oqyh/cs2-ESP-Players-GoldKingZ](https://github.com/oqyh/cs2-ESP-Players-GoldKingZ).

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/EspPlayers/` | `<sharp>/modules/EspPlayers/` |
| `.build/locales/esp.json` | `<sharp>/locales/esp.json` |

Restart the server (or change map) to load. The config file `configs/esp.cfg` is auto-generated on first run.

Optional dependencies (all degrade gracefully if absent): **LocalizerManager** (chat messages), **AdminManager** (permission gating — if `esp_toggle_permission` is set but AdminManager is missing, the toggle commands are not registered), **ClientPreferences** (persists each player's toggle state).

## ⌨️ Commands

| Command | Aliases | Description | Permission |
|---------|---------|-------------|------------|
| `esp` | `glow`, `showplayers`, `css_esp`, `css_glow`, `css_showplayers` | Toggle ESP glows on/off for the calling player | `@esp/use` (configurable via `esp_toggle_permission`; empty = everyone) |

## ⚙️ Configuration

`configs/esp.cfg` (auto-generated from ConVar defaults on first run; edits are reapplied each load):

| ConVar | Default | Meaning |
|--------|---------|---------|
| `esp_enabled` | `1` | Enable ESP Players [0=off, 1=on] |
| `esp_glow_color_ct` | `0,190,255` | Glow color for CT team (R,G,B) |
| `esp_glow_color_t` | `243,0,93` | Glow color for T team (R,G,B) |
| `esp_glow_range` | `5000` | Max glow render range in units (0 = always visible) |
| `esp_show_only_enemy_team` | `1` | Viewer sees only enemy-team glows [0=off, 1=on] |
| `esp_show_for` | `3` | Who may see ESP: 0=any, 1=dead only, 2=spectators only, 3=Unassigned/Spectator team only |
| `esp_disable_on_gotv` | `0` | Hide glows from GOTV/HLTV viewers [0=off, 1=on] |
| `esp_default_toggle` | `0` | Default ESP state for new players [0=off, 1=on] |
| `esp_toggle_permission` | `@esp/use` | Admin permission required to toggle ESP (empty = everyone) |
| `esp_hide_command` | `0` | Hide the toggle command from chat [0=off, 1=on] |

## 🔧 How it works

For each alive player the plugin spawns a relay + glow `prop_dynamic` pair that clones the player's model and tints it by team. Per-viewer transmit (`ITransmitManager.SetEntityState`) then exposes each glow only to viewers who enabled ESP and pass the configured gating; everyone else never receives the entities. The whole thing is event-driven — spawn, death, round restart, disconnect, toggle and team change events refresh the relevant glows — with no per-tick scanning.

## 📦 Build

```bash
dotnet build -c Release
```

Outputs `.build/modules/EspPlayers/EspPlayers.dll` and the locale file `.build/locales/esp.json`.

## 🙏 Credits

Port of [oqyh/cs2-ESP-Players-GoldKingZ](https://github.com/oqyh/cs2-ESP-Players-GoldKingZ) by oqyh.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappershq</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>
