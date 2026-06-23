using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using EspPlayers.Configuration;
using EspPlayers.Managers;
using EspPlayers.Utils;
using Sharp.Shared.Enums;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;

namespace EspPlayers.Commands;

/// <summary>
/// Registers the ESP toggle commands (<c>esp</c>, <c>glow</c>, <c>showplayers</c> and the
/// <c>css_</c>-prefixed variants for CSS muscle memory). A handler flips the caller's
/// preference, persists it, and prints a localized on/off message. When
/// <c>esp_toggle_permission</c> is non-empty the call is gated through AdminManager.
/// </summary>
internal sealed class EspCommandHandler : IModule
{
    private static readonly string[] CommandNames =
    [
        "esp", "glow", "showplayers",
        "css_esp", "css_glow", "css_showplayers",
    ];

    private readonly InterfaceBridge          _bridge;
    private readonly IEspConfig               _config;
    private readonly EspPreferenceManager     _prefs;
    private readonly ILogger<EspCommandHandler> _logger;

    private readonly List<(string name, IClientManager.DelegateClientCommand cb)> _registered = [];

    public EspCommandHandler(
        InterfaceBridge bridge,
        IEspConfig config,
        EspPreferenceManager prefs,
        ILogger<EspCommandHandler> logger)
    {
        _bridge = bridge;
        _config = config;
        _prefs  = prefs;
        _logger = logger;
    }

    public bool Init()
    {
        foreach (var name in CommandNames)
        {
            IClientManager.DelegateClientCommand cb = OnToggleCommand;
            _bridge.ClientManager.InstallCommandCallback(name, cb);
            _registered.Add((name, cb));
        }

        _logger.LogInformation("[EspPlayers] Registered {Count} toggle command alias(es)", _registered.Count);
        return true;
    }

    public void OnAllSharpModulesLoaded()
    {
        // Register the configured toggle permission with AdminManager. Without this, a custom
        // flag (e.g. "@espplayers/use") is unknown to the global permission index, so wildcard
        // grants like root "*" never expand to cover it — only an admin granted the EXACT flag
        // would pass, silently locking out root admins. Mounting a manifest that declares the
        // permission lets "*" / group wildcards resolve it.
        var flag = _config.TogglePermission;
        if (string.IsNullOrWhiteSpace(flag))
            return;

        _bridge.RegisterAdminPermission(flag);
    }

    public void Shutdown()
    {
        foreach (var (name, cb) in _registered)
            _bridge.ClientManager.RemoveCommandCallback(name, cb);
        _registered.Clear();
    }

    private ECommandAction OnToggleCommand(IGameClient client, StringCommand command)
    {
        if (!_config.Enabled)
            return ECommandAction.Skipped;

        if (client.IsFakeClient)
            return ECommandAction.Skipped;

        // Permission gate — empty flag = everyone allowed.
        if (!HasTogglePermission(client))
        {
            PrintToClient(client, "Esp_No_Permission");
            return Consume();
        }

        var enabled = _prefs.Toggle(client);
        PrintToClient(client, enabled ? "Esp_Enabled" : "Esp_Disabled");

        return Consume();
    }

    private bool HasTogglePermission(IGameClient client)
    {
        var flag = _config.TogglePermission;
        if (string.IsNullOrWhiteSpace(flag))
            return true;

        var adminManager = _bridge.AdminManager;
        if (adminManager is null)
        {
            // No AdminManager but a flag was configured — fail closed so a permission isn't
            // silently bypassed.
            _logger.LogWarning("[EspPlayers] esp_toggle_permission set but AdminManager unavailable — denying toggle");
            return false;
        }

        var admin = adminManager.GetAdmin(client.SteamId);
        return admin is not null && admin.HasPermission(flag);
    }

    private ECommandAction Consume()
        => _config.HideToggleCommand ? ECommandAction.Stopped : ECommandAction.Handled;

    private void PrintToClient(IGameClient client, string key, params object?[] args)
    {
        var prefix = _bridge.LocalizeFor(client, "Esp_Prefix");
        var body   = _bridge.LocalizeFor(client, key, args);
        client.Print(HudPrintChannel.Chat, Format.ProcessColorCodes($"{prefix} {body}"));
    }
}
