using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using EspPlayers.Configuration;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.HookParams;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace EspPlayers.Managers;

/// <summary>
/// Spawns a relay + glow <c>prop_dynamic</c> pair per alive player (cloning the player's
/// model, team-coloured) and drives per-viewer transmit so each glow is visible ONLY to
/// viewers who enabled ESP and pass the configured gating. Mirrors the prophunt
/// per-viewer technique: entities are hooked hidden-by-default
/// (<c>AddEntityHooks(..., false)</c>) then opened up per viewer controller via
/// <c>ITransmitManager.SetEntityState</c>. Non-enabled viewers never receive the entities.
///
/// <para>Everything is event-driven, no per-tick scan:
/// <list type="bullet">
///   <item>owner <c>PlayerSpawnPost</c> → create glow + refresh all viewers against it.</item>
///   <item>owner <c>PlayerKilledPost</c> / round restart / disconnect → kill glow.</item>
///   <item>viewer toggle / spawn / death / team change → refresh that viewer against all glows.</item>
/// </list></para>
///
/// <para>Entity references are never stored — only <see cref="EntityIndex"/>. An
/// <see cref="IEntityListener"/> clears a slot when the engine deletes a tracked entity.</para>
/// </summary>
internal sealed class EspGlowManager : IModule, IEntityListener, IGameListener, IEventListener
{
    private const int MaxSlots = 64;

    int IEntityListener.ListenerVersion  => IEntityListener.ApiVersion;
    int IEntityListener.ListenerPriority => 0;

    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    int IEventListener.ListenerVersion  => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    private readonly InterfaceBridge        _bridge;
    private readonly IEspConfig             _config;
    private readonly EspPreferenceManager   _prefs;
    private readonly ILogger<EspGlowManager> _logger;

    /// <summary>Relay + glow entity index per owner slot (-1 when no glow exists).</summary>
    private readonly int[] _relayIndex = new int[MaxSlots];
    private readonly int[] _glowIndex  = new int[MaxSlots];

    private Action<IPlayerSpawnForwardParams>?  _spawnForward;
    private Action<IPlayerKilledForwardParams>? _killedForward;

    // player_team game event covers ALL team changes (manual jointeam, autoteam, scrambles).
    private bool _teamEventHooked;

    public EspGlowManager(
        InterfaceBridge bridge,
        IEspConfig config,
        EspPreferenceManager prefs,
        ILogger<EspGlowManager> logger)
    {
        _bridge = bridge;
        _config = config;
        _prefs  = prefs;
        _logger = logger;

        for (var i = 0; i < MaxSlots; i++)
        {
            _relayIndex[i] = -1;
            _glowIndex[i]  = -1;
        }
    }

    public bool Init()
    {
        var hm = _bridge.HookManager;

        _spawnForward = OnPlayerSpawnPost;
        hm.PlayerSpawnPost.InstallForward(_spawnForward);

        _killedForward = OnPlayerKilledPost;
        hm.PlayerKilledPost.InstallForward(_killedForward);

        _bridge.EntityManager.InstallEntityListener(this);
        _bridge.ModSharp.InstallGameListener(this);

        // player_team fires for ALL team changes (manual jointeam, autoteam, scrambles, moves) —
        // a viewer's team flip changes enemy-team filtering even without a respawn.
        var em = _bridge.EventManager;
        em.InstallEventListener(this);
        em.HookEvent("player_team");

        // Refresh a viewer's glows the moment they flip their preference.
        _prefs.OnToggleChanged += RefreshAllOwnersForViewer;

        return true;
    }

    public void Shutdown()
    {
        var hm = _bridge.HookManager;
        if (_spawnForward is not null)  hm.PlayerSpawnPost.RemoveForward(_spawnForward);
        if (_killedForward is not null) hm.PlayerKilledPost.RemoveForward(_killedForward);

        _bridge.EntityManager.RemoveEntityListener(this);
        _bridge.ModSharp.RemoveGameListener(this);
        _bridge.EventManager.RemoveEventListener(this);
        _prefs.OnToggleChanged -= RefreshAllOwnersForViewer;

        ClearAllGlows();
    }

    // ===== IEventListener =====

    public void FireGameEvent(IGameEvent gameEvent)
    {
        if (gameEvent.GetName() != "player_team")
            return;

        var ctrl = gameEvent.GetPlayerController("userid");
        if (ctrl is not { IsValidEntity: true })
            return;

        var client = ctrl.GetGameClient();
        if (client is null)
            return;

        var slot = (int)(byte)client.Slot;
        // Viewer's team changed → re-evaluate their visibility against all glows. Also rebuild
        // the player's own glow against the team color if they swapped sides.
        RefreshAllOwnersForViewer(slot);

        if (_glowIndex[slot] >= 0)
            CreateGlowForOwner(slot);
    }

    // ===== IGameListener =====

    // On round restart, clear every glow. Players who respawn rebuild theirs via
    // PlayerSpawnPost; players who don't (e.g. moved to spectator) shed their stale glow.
    public void OnRoundRestart() => ClearAllGlows();

    // Map change / level shutdown — drop all entities so nothing dangles across the boundary.
    public void OnGameDeactivate() => ClearAllGlows();

    // ===== IEntityListener =====

    void IEntityListener.OnEntityDeleted(IBaseEntity entity)
    {
        var idx = (int)entity.Index;
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            if (_relayIndex[slot] == idx) _relayIndex[slot] = -1;
            if (_glowIndex[slot]  == idx) _glowIndex[slot]  = -1;
        }
    }

    // ===== Lifecycle forwards =====

    private void OnPlayerSpawnPost(IPlayerSpawnForwardParams param)
    {
        if (!_config.Enabled)
            return;

        var client = param.Client;
        if (client is null)
            return;

        var slot = (int)(byte)client.Slot;
        if ((uint)slot >= MaxSlots)
            return;

        // The spawning player is both a potential glow owner AND a viewer whose alive/team
        // state just changed. Handle both: (re)create their glow, and refresh their own view.
        CreateGlowForOwner(slot);
        RefreshAllOwnersForViewer(slot);
    }

    private void OnPlayerKilledPost(IPlayerKilledForwardParams param)
    {
        var victim = param.Client;
        if (victim is null)
            return;

        var slot = (int)(byte)victim.Slot;
        if ((uint)slot >= MaxSlots)
            return;

        // Dead owner has no glow (the cloned prop would otherwise float at the corpse).
        DestroyGlowForOwner(slot);

        // Dead viewer may now qualify (ShowEspFor dead/spec) — refresh their view of all glows.
        RefreshAllOwnersForViewer(slot);
    }

    // ===== Glow creation =====

    private void CreateGlowForOwner(int ownerSlot)
    {
        // Replace any prior pair first (re-spawn path).
        DestroyGlowForOwner(ownerSlot);

        var owner = _bridge.ClientManager.GetGameClient(new PlayerSlot((byte)ownerSlot));
        if (owner is not { IsInGame: true } || owner.IsFakeClient)
            return;

        var controller = owner.GetPlayerController();
        if (controller is not { IsValidEntity: true })
            return;

        var pawn = controller.GetPlayerPawn();
        if (pawn is not { IsValidEntity: true, IsAlive: true })
            return;

        var model = pawn.GetBodyComponent()
            ?.GetSceneNode()
            ?.AsSkeletonInstance?.GetModelState()
            ?.ModelName ?? "";

        if (string.IsNullOrEmpty(model))
        {
            _logger.LogWarning("[EspPlayers] No model for slot {Slot}; skipping glow", ownerSlot);
            return;
        }

        var origin   = pawn.GetAbsOrigin();
        var angles   = pawn.GetAbsAngles();
        var color    = controller.Team == CStrikeTeam.CT ? _config.GlowColorCT : _config.GlowColorT;
        var colorStr = color.ToGlowString();

        // RELAY — invisible proxy that follows the pawn.
        var relay = _bridge.EntityManager.SpawnEntitySync<IBaseModelEntity>(
            "prop_dynamic",
            new Dictionary<string, KeyValuesVariantValueItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"]                 = new KeyValuesVariantValueItem(model),
                ["origin"]                = new KeyValuesVariantValueItem($"{origin.X} {origin.Y} {origin.Z}"),
                ["angles"]                = new KeyValuesVariantValueItem($"{angles.X} {angles.Y} {angles.Z}"),
                ["spawnflags"]            = new KeyValuesVariantValueItem(256),
                ["rendermode"]            = new KeyValuesVariantValueItem("kRenderNone"),
                ["renderamt"]             = new KeyValuesVariantValueItem(0),
                ["solid"]                 = new KeyValuesVariantValueItem(0),
                ["disableshadows"]        = new KeyValuesVariantValueItem(1),
                ["disablereceiveshadows"] = new KeyValuesVariantValueItem(true),
            });

        if (relay is not { IsValidEntity: true })
        {
            _logger.LogWarning("[EspPlayers] Failed to spawn relay for slot {Slot}", ownerSlot);
            return;
        }

        // GLOW — visible outline that follows the relay.
        var glow = _bridge.EntityManager.SpawnEntitySync<IBaseModelEntity>(
            "prop_dynamic",
            new Dictionary<string, KeyValuesVariantValueItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"]                 = new KeyValuesVariantValueItem(model),
                ["origin"]                = new KeyValuesVariantValueItem($"{origin.X} {origin.Y} {origin.Z}"),
                ["angles"]                = new KeyValuesVariantValueItem($"{angles.X} {angles.Y} {angles.Z}"),
                ["spawnflags"]            = new KeyValuesVariantValueItem(256),
                ["renderamt"]             = new KeyValuesVariantValueItem(1),
                ["glowstate"]             = new KeyValuesVariantValueItem(3),
                ["glowcolor"]             = new KeyValuesVariantValueItem(colorStr),
                ["glowrange"]             = new KeyValuesVariantValueItem(_config.GlowRange),
                ["glowrangemin"]          = new KeyValuesVariantValueItem(0),
                ["glowteam"]              = new KeyValuesVariantValueItem(-1),
                ["solid"]                 = new KeyValuesVariantValueItem(0),
                ["disableshadows"]        = new KeyValuesVariantValueItem(1),
                ["disablereceiveshadows"] = new KeyValuesVariantValueItem(true),
            });

        if (glow is not { IsValidEntity: true })
        {
            _logger.LogWarning("[EspPlayers] Failed to spawn glow for slot {Slot}", ownerSlot);
            KillEntity(relay.Index);
            return;
        }

        // Follow chain: relay follows pawn, glow follows relay. FollowEntity preserves offsets;
        // SetParent is the fallback.
        if (!TryFollow(relay, pawn) || !TryFollow(glow, relay))
        {
            _logger.LogWarning("[EspPlayers] Failed to build follow chain for slot {Slot}", ownerSlot);
            KillEntity(glow.Index);
            KillEntity(relay.Index);
            return;
        }

        // Hook BOTH entities hidden-by-default; per-viewer state opened below.
        _bridge.TransmitManager.AddEntityHooks(relay, false);
        _bridge.TransmitManager.AddEntityHooks(glow, false);

        _relayIndex[ownerSlot] = (int)relay.Index;
        _glowIndex[ownerSlot]  = (int)glow.Index;

        // Open visibility for every connected viewer that qualifies right now.
        foreach (var viewer in _bridge.ClientManager.GetGameClients(inGame: true))
            ApplyViewerStateToGlow((int)(byte)viewer.Slot, ownerSlot);
    }

    private bool TryFollow(IBaseModelEntity follower, IBaseEntity target)
    {
        try
        {
            follower.AcceptInput("FollowEntity", target, follower, "!activator");
            return true;
        }
        catch
        {
            try
            {
                follower.AcceptInput("SetParent", target);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EspPlayers] FollowEntity/SetParent both failed");
                return false;
            }
        }
    }

    // ===== Per-viewer transmit =====

    /// <summary>Re-evaluate one viewer against every active glow. Used on viewer state changes.</summary>
    private void RefreshAllOwnersForViewer(int viewerSlot)
    {
        for (var owner = 0; owner < MaxSlots; owner++)
        {
            if (_glowIndex[owner] < 0)
                continue;
            ApplyViewerStateToGlow(viewerSlot, owner);
        }
    }

    /// <summary>
    /// Set the relay + glow transmit state for one viewer against one owner's glow, based on
    /// the viewer's preference and the configured gating.
    /// </summary>
    private void ApplyViewerStateToGlow(int viewerSlot, int ownerSlot)
    {
        if ((uint)viewerSlot >= MaxSlots || (uint)ownerSlot >= MaxSlots)
            return;

        var relayIdx = _relayIndex[ownerSlot];
        var glowIdx  = _glowIndex[ownerSlot];
        if (relayIdx < 0 || glowIdx < 0)
            return;

        var viewer = _bridge.ClientManager.GetGameClient(new PlayerSlot((byte)viewerSlot));
        if (viewer is not { IsInGame: true })
            return;

        var viewerCtrl = viewer.GetPlayerController();
        if (viewerCtrl is not { IsValidEntity: true })
            return;

        var ownerCtrl = _bridge.ClientManager
            .GetGameClient(new PlayerSlot((byte)ownerSlot))?.GetPlayerController();

        var canSee = ShouldSeeGlow(viewerSlot, viewer, viewerCtrl, ownerSlot, ownerCtrl);

        // SetEntityState(entIdx, viewerControllerIdx, transmit, channel=-1). The relay is
        // invisible (kRenderNone) but must transmit too so the followed glow stays attached
        // client-side.
        _bridge.TransmitManager.SetEntityState(new EntityIndex(relayIdx), viewerCtrl.Index, canSee, -1);
        _bridge.TransmitManager.SetEntityState(new EntityIndex(glowIdx),  viewerCtrl.Index, canSee, -1);
    }

    private bool ShouldSeeGlow(
        int viewerSlot,
        IGameClient viewer,
        IPlayerController viewerCtrl,
        int ownerSlot,
        IPlayerController? ownerCtrl)
    {
        // Never show a player their OWN glow.
        if (viewerSlot == ownerSlot)
            return false;

        // Viewer must have ESP toggled on.
        if (!_prefs.IsEnabled(viewerSlot))
            return false;

        // GOTV / HLTV gating.
        if (_config.DisableGlowOnGotv && viewer.IsHltv)
            return false;

        // Show_ESP_For gating: 0=any, 1=dead only, 2=spectators only.
        if (!PassesShowEspFor(viewerCtrl))
            return false;

        // Enemy-team-only gating.
        if (_config.ShowOnlyEnemyTeam && ownerCtrl is { IsValidEntity: true })
        {
            var vTeam = viewerCtrl.Team;
            var oTeam = ownerCtrl.Team;
            // Only enforce when both sides are on a real playing team. Spectators see everyone.
            if (IsPlayingTeam(vTeam) && IsPlayingTeam(oTeam) && vTeam == oTeam)
                return false;
        }

        return true;
    }

    private bool PassesShowEspFor(IPlayerController viewerCtrl)
    {
        var mode = _config.ShowEspFor;
        if (mode <= 0)
            return true; // any

        var isSpec = viewerCtrl.Team == CStrikeTeam.Spectator;
        var pawn   = viewerCtrl.GetPlayerPawn();
        var isDead = pawn is null || !pawn.IsValidEntity || !pawn.IsAlive;

        return mode switch
        {
            1 => isDead,            // dead only
            2 => isSpec,            // spectators only
            _ => true,
        };
    }

    private static bool IsPlayingTeam(CStrikeTeam team)
        => team == CStrikeTeam.TE || team == CStrikeTeam.CT;

    // ===== Teardown =====

    private void DestroyGlowForOwner(int ownerSlot)
    {
        if ((uint)ownerSlot >= MaxSlots)
            return;

        if (_glowIndex[ownerSlot] >= 0)
        {
            KillEntity(new EntityIndex(_glowIndex[ownerSlot]));
            _glowIndex[ownerSlot] = -1;
        }

        if (_relayIndex[ownerSlot] >= 0)
        {
            KillEntity(new EntityIndex(_relayIndex[ownerSlot]));
            _relayIndex[ownerSlot] = -1;
        }
    }

    /// <summary>Public hook so the command/lifecycle layer can re-evaluate everyone at once.</summary>
    public void RefreshAllViewers()
    {
        foreach (var viewer in _bridge.ClientManager.GetGameClients(inGame: true))
            RefreshAllOwnersForViewer((int)(byte)viewer.Slot);
    }

    private void ClearAllGlows()
    {
        for (var i = 0; i < MaxSlots; i++)
            DestroyGlowForOwner(i);
    }

    private void KillEntity(EntityIndex idx)
    {
        var ent = _bridge.EntityManager.FindEntityByIndex(idx);
        if (ent is not { IsValidEntity: true })
            return;

        try { _bridge.TransmitManager.RemoveEntityHooks(ent); } catch { /* best-effort */ }

        try { ent.AcceptInput("Kill"); } catch { /* best-effort */ }
    }
}
