using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared;

namespace EspPlayers;

/// <summary>
/// ESP Players — CS2 ModSharp port of cs2-ESP-Players-GoldKingZ by oqyh.
/// Original (CounterStrikeSharp): https://github.com/oqyh/cs2-ESP-Players-GoldKingZ
///
/// Server-operator-controlled, per-viewer opt-in glow ESP. A player toggles ESP via a
/// chat command and, only for that player, other players are outlined with a team-coloured
/// glow. Visibility is filtered independently per viewer through the transmit system, so
/// players who have NOT enabled ESP never see the glow entities.
/// </summary>
public sealed class EspPlayersPlugin : IModSharpModule
{
    public string DisplayName   => "EspPlayers";
    public string DisplayAuthor => "yappershq — ported from oqyh (cs2-ESP-Players-GoldKingZ)";

    private readonly ILogger<EspPlayersPlugin> _logger;
    private readonly ServiceProvider           _serviceProvider;

    public EspPlayersPlugin(
        ISharedSystem   sharedSystem,
        string?         dllPath,
        string?         sharpPath,
        Version?        version,
        IConfiguration? coreConfiguration,
        bool            hotReload)
    {
        ArgumentNullException.ThrowIfNull(sharedSystem);
        ArgumentNullException.ThrowIfNull(dllPath);
        ArgumentNullException.ThrowIfNull(sharpPath);

        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<EspPlayersPlugin>();

        _ = new InterfaceBridge(dllPath, sharpPath, sharedSystem, loggerFactory);

        var services = new ServiceCollection();
        services.AddSingleton(sharedSystem);
        services.AddSingleton(InterfaceBridge.Instance);
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(LoggerFactoryLogger<>));

        services.AddModules();

        _serviceProvider = services.BuildServiceProvider();
    }

    public bool Init()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.Init(), "Init");

        return true;
    }

    public void PostInit()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.OnPostInit(), "PostInit");
    }

    public void OnAllModulesLoaded()
    {
        // Resolve optional interfaces in OAM — ModSharp guarantees all PostInits have finished
        // before any OAM fires, so other plugins' published interfaces are live here.
        InterfaceBridge.Instance.InitLocalizer();
        InterfaceBridge.Instance.InitAdminManager();
        InterfaceBridge.Instance.InitClientPreference();

        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.OnAllSharpModulesLoaded(), "OnAllModulesLoaded");

        _logger.LogInformation("[EspPlayers] Plugin loaded successfully");
    }

    public void Shutdown()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.Shutdown(), "Shutdown");

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    public void OnLibraryConnected(string name)  { }
    public void OnLibraryDisconnect(string name) { }

    private void CallSafe(IModule module, Action<IModule> action, string phase)
    {
        try
        {
            action(module);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EspPlayers] Error in {Phase} for {Module}", phase, module.GetType().Name);
        }
    }
}

internal sealed class LoggerFactoryLogger<T>(ILoggerFactory factory) : ILogger<T>
{
    private readonly ILogger _inner = factory.CreateLogger(typeof(T).Name);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}
