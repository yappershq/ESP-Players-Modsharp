using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.ClientPreferences.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace EspPlayers;

internal sealed class InterfaceBridge
{
    internal static InterfaceBridge Instance { get; private set; } = null!;

    // === Paths ===
    internal string SharpPath { get; }
    internal string DllPath   { get; }

    // === Managers ===
    internal IConVarManager      ConVarManager      { get; }
    internal IEventManager       EventManager       { get; }
    internal IClientManager      ClientManager      { get; }
    internal IEntityManager      EntityManager      { get; }
    internal ITransmitManager    TransmitManager    { get; }
    internal IHookManager        HookManager        { get; }
    internal IModSharp           ModSharp           { get; }
    internal ILoggerFactory      LoggerFactory      { get; }
    internal ISharpModuleManager SharpModuleManager { get; }

    // === Optional modules (resolved in OnAllModulesLoaded) ===
    internal ILocalizerManager?  LocalizerManager { get; private set; }
    internal IAdminManager?      AdminManager     { get; private set; }
    internal IClientPreference?  ClientPreference { get; private set; }

    public InterfaceBridge(
        string         dllPath,
        string         sharpPath,
        ISharedSystem  sharedSystem,
        ILoggerFactory loggerFactory)
    {
        Instance = this;

        SharpPath = sharpPath;
        DllPath   = dllPath;

        ConVarManager      = sharedSystem.GetConVarManager();
        EventManager       = sharedSystem.GetEventManager();
        ClientManager      = sharedSystem.GetClientManager();
        EntityManager      = sharedSystem.GetEntityManager();
        TransmitManager    = sharedSystem.GetTransmitManager();
        HookManager        = sharedSystem.GetHookManager();
        ModSharp           = sharedSystem.GetModSharp();
        LoggerFactory      = loggerFactory;
        SharpModuleManager = sharedSystem.GetSharpModuleManager();
    }

    internal void InitLocalizer()
    {
        var iface = SharpModuleManager
            .GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);
        if (iface?.Instance is not { } lm)
            return;

        LocalizerManager = lm;
        lm.LoadLocaleFile("esp", suppressDuplicationWarnings: true);
    }

    internal void InitAdminManager()
    {
        var iface = SharpModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);
        if (iface?.Instance is not { } am)
            return;

        AdminManager = am;
    }

    internal void InitClientPreference()
    {
        var iface = SharpModuleManager
            .GetOptionalSharpModuleInterface<IClientPreference>(IClientPreference.Identity);
        if (iface?.Instance is not { } cp)
            return;

        ClientPreference = cp;
    }

    internal const string AdminModuleIdentity = "EspPlayers";

    /// <summary>
    /// Declare a custom toggle permission with AdminManager so wildcard grants (root "*",
    /// group wildcards) expand over it. A permission that is never registered is invisible to
    /// the wildcard index, so only an admin granted the EXACT flag would resolve it — locking
    /// out root admins. Must be called on the game thread (OnAllModulesLoaded satisfies this).
    /// </summary>
    internal void RegisterAdminPermission(string permission)
    {
        var am = AdminManager;
        if (am is null)
        {
            LoggerFactory.CreateLogger("EspPlayers.Admin")
                .LogWarning("[EspPlayers] esp_toggle_permission '{Flag}' set but AdminManager unavailable — cannot register permission", permission);
            return;
        }

        try
        {
            am.MountAdminManifest(AdminModuleIdentity, () => new AdminTableManifest(
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["espplayers"] = [permission],
                },
                [],
                []));

            LoggerFactory.CreateLogger("EspPlayers.Admin")
                .LogInformation("[EspPlayers] Registered toggle permission '{Flag}' with AdminManager", permission);
        }
        catch (Exception ex)
        {
            LoggerFactory.CreateLogger("EspPlayers.Admin")
                .LogError(ex, "[EspPlayers] Failed to register toggle permission '{Flag}'", permission);
        }
    }

    /// <summary>
    /// Localize a string for a specific client. Falls back to key if localizer is unavailable.
    /// </summary>
    internal string LocalizeFor(IGameClient client, string key, params object?[] args)
    {
        var lm = LocalizerManager;
        if (lm is null)
            return key;

        try
        {
            return lm.For(client).Text(key, args);
        }
        catch
        {
            return key;
        }
    }
}
