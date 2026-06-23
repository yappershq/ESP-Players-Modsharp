using System;
using Microsoft.Extensions.Logging;
using EspPlayers.Configuration;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Units;

namespace EspPlayers.Managers;

/// <summary>
/// Owns the per-player ESP toggle state. Backed by ModSharp's <c>IClientPreference</c>
/// (cookie key <c>esp_enabled</c>) so the choice persists across sessions, with a
/// <c>bool[64]</c> slot cache for zero-cost reads inside the transmit refresh path.
///
/// <para>The cookie is loaded INSIDE <c>IClientPreference.ListenOnLoad</c> (or, as a
/// fallback, on <c>OnClientPostAdminCheck</c>) rather than at command-entry time, to dodge
/// the async preload race where the cookie store isn't populated yet when a command fires.</para>
/// </summary>
internal sealed class EspPreferenceManager : IModule, IClientListener
{
    private const int MaxSlots = 64;
    internal const string CookieKey = "esp_enabled";

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    private readonly InterfaceBridge              _bridge;
    private readonly IEspConfig                   _config;
    private readonly ILogger<EspPreferenceManager> _logger;

    private readonly bool[] _enabled = new bool[MaxSlots];

    private IDisposable? _onLoadListener;

    /// <summary>Raised when a slot's toggle flips, so the glow manager can refresh transmit.</summary>
    public event Action<int>? OnToggleChanged;

    public EspPreferenceManager(InterfaceBridge bridge, IEspConfig config, ILogger<EspPreferenceManager> logger)
    {
        _bridge = bridge;
        _config = config;
        _logger = logger;
    }

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnAllSharpModulesLoaded()
    {
        // Cookie load fires through ClientPreference's own load event — register here so the
        // cache is filled the instant the store is ready for a client (avoids the preload race).
        var prefs = _bridge.ClientPreference;
        if (prefs is not null)
        {
            _onLoadListener = prefs.ListenOnLoad(OnPreferenceLoaded);
            _logger.LogInformation("[EspPlayers] ClientPreference available — toggle persistence enabled");
        }
        else
        {
            _logger.LogWarning("[EspPlayers] ClientPreference not available — ESP toggle will not persist across sessions");
        }
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveClientListener(this);
        _onLoadListener?.Dispose();
        _onLoadListener = null;
    }

    // ===== IClientListener =====

    void IClientListener.OnClientPostAdminCheck(IGameClient client)
    {
        if (client.IsFakeClient)
            return;

        var slot = (int)(byte)client.Slot;

        // Seed the default first. If the cookie store has already loaded for this client,
        // overwrite with the saved value; otherwise ListenOnLoad will correct it shortly.
        _enabled[slot] = _config.DefaultToggle;

        var prefs = _bridge.ClientPreference;
        if (prefs is not null && prefs.IsLoaded(client.SteamId))
            _enabled[slot] = ReadCookie(client);
    }

    void IClientListener.OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        var slot = (int)(byte)client.Slot;
        if ((uint)slot < MaxSlots)
            _enabled[slot] = false;
    }

    // ===== ClientPreference load callback =====

    private void OnPreferenceLoaded(IGameClient client)
    {
        if (client.IsFakeClient)
            return;

        var slot = (int)(byte)client.Slot;
        if ((uint)slot >= MaxSlots)
            return;

        var enabled = ReadCookie(client);
        if (_enabled[slot] == enabled)
            return;

        _enabled[slot] = enabled;
        OnToggleChanged?.Invoke(slot);
    }

    private bool ReadCookie(IGameClient client)
    {
        var prefs = _bridge.ClientPreference;
        if (prefs is null)
            return _config.DefaultToggle;

        var cookie = prefs.GetCookie(client.SteamId, CookieKey);
        if (cookie is null)
            return _config.DefaultToggle;

        try
        {
            return cookie.GetNumber() == 1;
        }
        catch
        {
            return _config.DefaultToggle;
        }
    }

    // ===== Public API =====

    /// <summary>Returns whether the given slot currently has ESP enabled.</summary>
    public bool IsEnabled(int slot)
        => (uint)slot < MaxSlots && _enabled[slot];

    /// <summary>
    /// Flips the toggle for a client, persists it, and raises <see cref="OnToggleChanged"/>.
    /// Returns the new state.
    /// </summary>
    public bool Toggle(IGameClient client)
    {
        var slot = (int)(byte)client.Slot;
        var newValue = !((uint)slot < MaxSlots && _enabled[slot]);
        Set(client, newValue);
        return newValue;
    }

    /// <summary>Sets the toggle for a client, persists it, and raises <see cref="OnToggleChanged"/>.</summary>
    public void Set(IGameClient client, bool enabled)
    {
        var slot = (int)(byte)client.Slot;
        if ((uint)slot >= MaxSlots)
            return;

        _enabled[slot] = enabled;

        _bridge.ClientPreference?.SetCookie(client.SteamId, CookieKey, enabled ? 1L : 0L);

        OnToggleChanged?.Invoke(slot);
    }
}
