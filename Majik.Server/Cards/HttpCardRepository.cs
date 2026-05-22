using System.Net;
using System.Net.Http.Json;
using Majik.Core.CardData;
using Majik.Core.CardData.Contracts;
using Majik.Core.CardData.Database;

namespace Majik.Server.Cards;

/// <summary>
/// <see cref="ICardRepository"/> implementation that proxies to the
/// majik-cards internal HTTP service. Wraps a configured <see cref="HttpClient"/>
/// whose <c>BaseAddress</c> points at <c>Cards:BaseUrl</c>.
///
/// Sync-over-async: ICardRepository is a sync interface (called from the
/// engine binder pipeline which is itself sync). All public methods block on
/// the underlying HTTP task. The caller-side <see cref="CachingCardRepository"/>
/// decorator absorbs hot reads so this only fires on cache misses (cold
/// boot, then ~once per distinct card name per process).
/// </summary>
public sealed class HttpCardRepository : ICardRepository
{
    private readonly HttpClient _http;

    public HttpCardRepository(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var url = $"/internal/cards/by-name?name={Uri.EscapeDataString(name)}";
        using var resp = _http.GetAsync(url).GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var dto = resp.Content.ReadFromJsonAsync<CardEntityDto>().GetAwaiter().GetResult();
        return dto?.ToEntity();
    }

    public IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null)
    {
        var sb = new System.Text.StringBuilder("/internal/cards/search?");
        sb.Append("implementedOnly=").Append(implementedOnly ? "true" : "false");
        sb.Append("&limit=").Append(limit);
        if (!string.IsNullOrWhiteSpace(q))
            sb.Append("&q=").Append(Uri.EscapeDataString(q));
        if (colors != null)
            foreach (var c in colors) sb.Append("&colors=").Append(Uri.EscapeDataString(c));
        if (types != null)
            foreach (var t in types) sb.Append("&types=").Append(Uri.EscapeDataString(t));
        if (cmcBuckets != null)
            foreach (var b in cmcBuckets) sb.Append("&cmc=").Append(b);

        using var resp = _http.GetAsync(sb.ToString()).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        var dtos = resp.Content.ReadFromJsonAsync<IReadOnlyList<CardEntityDto>>().GetAwaiter().GetResult()
                   ?? Array.Empty<CardEntityDto>();
        var rows = new List<CardEntity>(dtos.Count);
        foreach (var d in dtos) rows.Add(d.ToEntity());
        return rows;
    }

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
    {
        var list = names?.ToList() ?? new List<string>();
        if (list.Count == 0) return Array.Empty<CardEntity>();
        var body = new CardsByNamesRequest(list);
        using var resp = _http.PostAsJsonAsync("/internal/cards/by-names", body).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        var dtos = resp.Content.ReadFromJsonAsync<IReadOnlyList<CardEntityDto>>().GetAwaiter().GetResult()
                   ?? Array.Empty<CardEntityDto>();
        var rows = new List<CardEntity>(dtos.Count);
        foreach (var d in dtos) rows.Add(d.ToEntity());
        return rows;
    }

    public bool IsImplemented(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var url = $"/internal/cards/is-implemented?name={Uri.EscapeDataString(name)}";
        using var resp = _http.GetAsync(url).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        var r = resp.Content.ReadFromJsonAsync<IsImplementedResponse>().GetAwaiter().GetResult();
        return r?.Implemented ?? false;
    }

    public void SetImplemented(string name, bool value)
    {
        var body = new SetImplementedRequest(name, value);
        using var resp = _http.PostAsJsonAsync("/internal/cards/set-implemented", body).GetAwaiter().GetResult();
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new ArgumentException($"Card not found: {name}", nameof(name));
        resp.EnsureSuccessStatusCode();
    }
}
