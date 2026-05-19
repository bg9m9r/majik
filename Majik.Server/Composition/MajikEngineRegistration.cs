using Majik.Core.Api;
using Majik.Core.Events;

namespace Majik.Server.Composition;

/// <summary>
/// Registers the Majik engine components in the ASP.NET Core DI
/// container. The engine itself stays free of ASP.NET dependencies —
/// this file is the only place where the two worlds meet.
///
/// Scope decisions (Phase 3 v1):
/// - GameRegistry singleton: one process, many games, in-memory only.
/// - EventBus is per-game (a facade owns its own bus), so no global bus
///   is registered here.
/// </summary>
public static class MajikEngineRegistration
{
    public static IServiceCollection AddMajikEngine(this IServiceCollection services)
    {
        services.AddSingleton<GameRegistry>();
        return services;
    }
}
