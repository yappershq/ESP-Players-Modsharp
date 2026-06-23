using Microsoft.Extensions.DependencyInjection;
using EspPlayers.Commands;
using EspPlayers.Configuration;
using EspPlayers.Managers;

namespace EspPlayers;

internal static class ModuleDependencyInjection
{
    internal static IServiceCollection AddModules(this IServiceCollection services)
    {
        // Config (constructs ConVars on instantiation — not an IModule)
        services.AddSingleton<IEspConfig, EspConfig>();

        // Per-player toggle store (clientprefs-backed + bool[64] cache)
        services.AddSingleton<EspPreferenceManager>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<EspPreferenceManager>());

        // Glow entity create/destroy + per-viewer transmit
        services.AddSingleton<EspGlowManager>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<EspGlowManager>());

        // Toggle command (depends on preference + glow managers)
        services.AddSingleton<EspCommandHandler>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<EspCommandHandler>());

        return services;
    }
}
