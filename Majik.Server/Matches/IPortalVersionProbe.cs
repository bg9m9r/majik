using System.Text.Json;

namespace Majik.Server.Matches;

public interface IPortalVersionProbe
{
    /// <summary>Live portal build time from its version.json, or null if
    /// unreachable / not configured.</summary>
    Task<DateTime?> GetBuildTimeAsync(CancellationToken ct);
}

public sealed class HttpPortalVersionProbe : IPortalVersionProbe
{
    private readonly HttpClient _http;
    private readonly string? _url;

    public HttpPortalVersionProbe(HttpClient http, Majik.Server.Composition.DeploymentOptions opts)
    {
        _http = http;
        _url = opts.PortalVersionUrl;
    }

    public async Task<DateTime?> GetBuildTimeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_url)) return null;
        try
        {
            using var stream = await _http.GetStreamAsync(_url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("buildTime", out var bt)
                && bt.ValueKind == JsonValueKind.String
                && DateTime.TryParse(bt.GetString(), out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }
        catch { /* unreachable — treat as not-yet-deployed */ }
        return null;
    }
}
