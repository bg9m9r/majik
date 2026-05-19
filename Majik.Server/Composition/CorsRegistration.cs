namespace Majik.Server.Composition;

/// <summary>
/// CORS for the SPA frontend. Allowed origins come from
/// <c>Cors:AllowedOrigins</c> in config. Same-origin deploys (frontend
/// served by the API) leave it empty and skip the middleware. SignalR
/// needs <c>AllowCredentials</c> so the bearer-token handshake survives.
/// </summary>
public static class CorsRegistration
{
    public const string PolicyName = "MajikSpa";

    public static IServiceCollection AddMajikCors(
        this IServiceCollection services,
        IConfiguration config)
    {
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (origins.Length == 0)
                {
                    return;
                }
                policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static bool HasAnyOrigins(IConfiguration config)
    {
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();
        return origins is { Length: > 0 };
    }
}
