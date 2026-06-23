using System;
using Microsoft.Extensions.Logging;
using EspPlayers.Utils;
using Sharp.Shared.Objects;

namespace EspPlayers.Configuration;

/// <summary>RGB color triple (0-255 per channel). Alpha is ignored — glow has no alpha channel.</summary>
internal readonly record struct EspColor(int R, int G, int B)
{
    /// <summary>
    /// Parse an "R,G,B" or "R G B" (optionally "R,G,B,A") string. Falls back to <paramref name="fallback"/>.
    /// </summary>
    public static EspColor Parse(string? value, EspColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return fallback;

        if (int.TryParse(parts[0], out var r)
         && int.TryParse(parts[1], out var g)
         && int.TryParse(parts[2], out var b))
        {
            return new EspColor(Clamp(r), Clamp(g), Clamp(b));
        }

        return fallback;
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public string ToGlowString() => $"{R} {G} {B}";
}

internal interface IEspConfig
{
    /// <summary>Plugin master switch.</summary>
    bool Enabled { get; }

    /// <summary>Glow color for the Counter-Terrorist team.</summary>
    EspColor GlowColorCT { get; }

    /// <summary>Glow color for the Terrorist team.</summary>
    EspColor GlowColorT { get; }

    /// <summary>Max glow render range in units. 0 = always visible at any distance.</summary>
    int GlowRange { get; }

    /// <summary>Viewer may only see glows on the ENEMY team. Same-team glows hidden.</summary>
    bool ShowOnlyEnemyTeam { get; }

    /// <summary>
    /// Which viewers may see ESP: 0 = any, 1 = dead only, 2 = spectators only,
    /// 3 = viewer's controller team is Unassigned OR Spectator (the default — spectator-only tool).
    /// Mirrors the original Show_ESP_For knob, extended with the authoritative team-based mode.
    /// </summary>
    int ShowEspFor { get; }

    /// <summary>Hide glows from GOTV / HLTV viewers.</summary>
    bool DisableGlowOnGotv { get; }

    /// <summary>Default toggle state for players with no saved preference.</summary>
    bool DefaultToggle { get; }

    /// <summary>
    /// Admin permission flag required to use the toggle command. Empty = everyone allowed.
    /// e.g. <c>@espplayers/use</c> or a standard flag.
    /// </summary>
    string TogglePermission { get; }

    /// <summary>Hide the toggle chat command from chat (don't echo it to other players).</summary>
    bool HideToggleCommand { get; }
}

internal sealed class EspConfig : IEspConfig
{
    private static readonly EspColor DefaultCtColor = new(0, 190, 255);  // cyan
    private static readonly EspColor DefaultTColor  = new(243, 0, 93);   // magenta

    private readonly IConVar? _cvEnabled;
    private readonly IConVar? _cvGlowColorCt;
    private readonly IConVar? _cvGlowColorT;
    private readonly IConVar? _cvGlowRange;
    private readonly IConVar? _cvShowOnlyEnemyTeam;
    private readonly IConVar? _cvShowEspFor;
    private readonly IConVar? _cvDisableGlowOnGotv;
    private readonly IConVar? _cvDefaultToggle;
    private readonly IConVar? _cvTogglePermission;
    private readonly IConVar? _cvHideToggleCommand;

    public EspConfig(InterfaceBridge bridge)
    {
        var cv = bridge.ConVarManager;

        _cvEnabled           = cv.CreateConVar("esp_enabled",              true,  "Enable ESP Players [0=off, 1=on]");
        _cvGlowColorCt       = cv.CreateConVar("esp_glow_color_ct",        "0,190,255", "Glow color for CT team (R,G,B)");
        _cvGlowColorT        = cv.CreateConVar("esp_glow_color_t",         "243,0,93",  "Glow color for T team (R,G,B)");
        _cvGlowRange         = cv.CreateConVar("esp_glow_range",           5000,  "Max glow render range in units (0 = always visible)");
        _cvShowOnlyEnemyTeam = cv.CreateConVar("esp_show_only_enemy_team", true,  "Viewer sees only enemy-team glows [0=off, 1=on]");
        _cvShowEspFor        = cv.CreateConVar("esp_show_for",             3,     "Who may see ESP: 0=any, 1=dead only, 2=spectators only, 3=Unassigned/Spectator team only (default)");
        _cvDisableGlowOnGotv = cv.CreateConVar("esp_disable_on_gotv",      false, "Hide glows from GOTV/HLTV viewers [0=off, 1=on]");
        _cvDefaultToggle     = cv.CreateConVar("esp_default_toggle",       false, "Default ESP state for new players [0=off, 1=on]");
        _cvTogglePermission  = cv.CreateConVar("esp_toggle_permission",    "@esp/use", "Admin permission required to toggle ESP (empty = everyone). Default @esp/use = admin-only");
        _cvHideToggleCommand = cv.CreateConVar("esp_hide_command",         false, "Hide the toggle command from chat [0=off, 1=on]");

        // Generate/load editable config at sharp/configs/esp.cfg so admin edits survive restarts.
        var logger = bridge.LoggerFactory.CreateLogger("EspPlayers.Config");
        IConVar?[] all = [_cvEnabled, _cvGlowColorCt, _cvGlowColorT, _cvGlowRange, _cvShowOnlyEnemyTeam,
                          _cvShowEspFor, _cvDisableGlowOnGotv, _cvDefaultToggle, _cvTogglePermission,
                          _cvHideToggleCommand];
        ConVarConfigFile.Sync(bridge.SharpPath, "esp.cfg", "EspPlayers", logger,
            Array.FindAll(all, c => c is not null)!);
    }

    public bool     Enabled           => _cvEnabled?.GetBool()    ?? true;
    public EspColor GlowColorCT       => EspColor.Parse(_cvGlowColorCt?.GetString(), DefaultCtColor);
    public EspColor GlowColorT        => EspColor.Parse(_cvGlowColorT?.GetString(),  DefaultTColor);
    public int      GlowRange         => _cvGlowRange?.GetInt32() ?? 5000;
    public bool     ShowOnlyEnemyTeam => _cvShowOnlyEnemyTeam?.GetBool() ?? true;
    public int      ShowEspFor        => _cvShowEspFor?.GetInt32() ?? 3;
    public bool     DisableGlowOnGotv => _cvDisableGlowOnGotv?.GetBool() ?? false;
    public bool     DefaultToggle     => _cvDefaultToggle?.GetBool() ?? false;
    public string   TogglePermission  => _cvTogglePermission?.GetString() ?? "@esp/use";
    public bool     HideToggleCommand => _cvHideToggleCommand?.GetBool() ?? false;
}
