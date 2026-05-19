using System.Net.Http.Headers;
using System.Text.Json;

namespace Majik.Core.CardData.Import;

/// <summary>
/// Pulls card data from the Scryfall HTTP API.
///
/// Two entry points:
/// - <see cref="DownloadBulkAsync"/> — fetches one of Scryfall's named bulk
///   exports (oracle-cards, default-cards, all-cards). The bulk-data
///   manifest at https://api.scryfall.com/bulk-data lists each available
///   download with a stable URL; we resolve by type name, then stream
///   the JSON to disk.
/// - <see cref="DownloadSetAsync"/> — paginates through
///   https://api.scryfall.com/cards/search?q=set:CODE&amp;unique=prints,
///   accumulates results into a single JSON array file. Useful when a
///   new set drops and the full bulk file is overkill.
///
/// Scryfall's API guidance (https://scryfall.com/docs/api):
/// - identify with a User-Agent
/// - throttle to ~50-100ms between requests
/// - cache responses where possible
/// </summary>
public sealed class ScryfallDownloader
{
    private const string ApiBase = "https://api.scryfall.com";
    private const int ThrottleMs = 100;

    private readonly HttpClient _http;
    private readonly IProgress<string>? _log;

    public ScryfallDownloader(HttpClient? http = null, IProgress<string>? log = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Majik", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _log = log;
    }

    /// <summary>Resolve the named bulk export, download its JSON payload
    /// to <paramref name="destPath"/>. Overwrites if already present.</summary>
    public async Task DownloadBulkAsync(
        string bulkType, string destPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bulkType)) throw new ArgumentException("bulkType required", nameof(bulkType));
        if (string.IsNullOrWhiteSpace(destPath)) throw new ArgumentException("destPath required", nameof(destPath));

        Log($"Fetching bulk-data manifest from {ApiBase}/bulk-data");
        using var manifest = await _http.GetAsync($"{ApiBase}/bulk-data", ct);
        manifest.EnsureSuccessStatusCode();
        var manifestJson = await manifest.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(manifestJson);
        var match = doc.RootElement.GetProperty("data").EnumerateArray()
            .FirstOrDefault(e => string.Equals(
                e.GetProperty("type").GetString(), bulkType, StringComparison.OrdinalIgnoreCase));
        if (match.ValueKind == JsonValueKind.Undefined)
        {
            var available = string.Join(", ", doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(e => e.GetProperty("type").GetString()));
            throw new InvalidOperationException(
                $"Bulk type '{bulkType}' not found. Available: {available}");
        }

        var url = match.GetProperty("download_uri").GetString()
            ?? throw new InvalidOperationException("Manifest entry missing download_uri");
        var sizeBytes = match.TryGetProperty("size", out var s) ? s.GetInt64() : -1L;

        Log($"Downloading {bulkType} from {url}" +
            (sizeBytes > 0 ? $" ({sizeBytes / (1024.0 * 1024.0):F1} MB)" : ""));

        await Throttle(ct);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct);

        Log($"Saved → {destPath}");
    }

    /// <summary>Paginate /cards/search for one set; accumulate every
    /// printing into a JSON array file (same shape ScryfallJsonImporter
    /// expects).</summary>
    public async Task DownloadSetAsync(
        string setCode, string destPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(setCode)) throw new ArgumentException("setCode required", nameof(setCode));
        if (string.IsNullOrWhiteSpace(destPath)) throw new ArgumentException("destPath required", nameof(destPath));

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var dst = File.Create(destPath);
        await using var writer = new Utf8JsonWriter(dst, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();

        var url = $"{ApiBase}/cards/search?q=set%3A{Uri.EscapeDataString(setCode.ToLowerInvariant())}&unique=prints";
        var page = 1;
        var total = 0;

        while (!string.IsNullOrEmpty(url))
        {
            Log($"Page {page} → {url}");
            await Throttle(ct);
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log($"No cards found for set '{setCode}'.");
                break;
            }
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            foreach (var card in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                card.WriteTo(writer);
                total++;
            }

            url = doc.RootElement.TryGetProperty("has_more", out var more) && more.GetBoolean()
                ? doc.RootElement.GetProperty("next_page").GetString()
                : null;
            page++;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        Log($"Wrote {total} cards from set '{setCode}' → {destPath}");
    }

    private async Task Throttle(CancellationToken ct) =>
        await Task.Delay(ThrottleMs, ct);

    private void Log(string msg) => _log?.Report(msg);
}
