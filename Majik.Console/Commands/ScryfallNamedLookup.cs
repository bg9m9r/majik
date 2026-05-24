using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Majik.Core.CardData.Database;

namespace Majik.Console.Commands;

/// <summary>
/// One-shot fetch against <c>https://api.scryfall.com/cards/named?exact=…</c>
/// used as a fallback when <see cref="Majik.Core.CardData.ICardRepository.GetByName"/>
/// returns null. Only used by the <c>scaffold-factory</c> subcommand — we
/// don't want this to grow into a parallel import path.
/// </summary>
public static class ScryfallNamedLookup
{
    private const string ApiBase = "https://api.scryfall.com";

    /// <summary>Fetch a card by exact name. Returns null on 404 / network
    /// failure / parse failure — callers print a clean error and exit.</summary>
    public static async Task<CardEntity?> FetchAsync(
        string name,
        HttpClient? http = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var ownsClient = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            http.DefaultRequestHeaders.UserAgent.Clear();
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Majik-ScaffoldFactory", "1.0"));
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var url = $"{ApiBase}/cards/named?exact={Uri.EscapeDataString(name)}";
            using var resp = await http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseEntity(json);
        }
        catch
        {
            // Defensive: network errors, parse failures, sandbox limits — we
            // surface the missing-card story upstream; the scaffold tool
            // doesn't need to distinguish "no DB row" vs "no network".
            return null;
        }
        finally
        {
            if (ownsClient) http.Dispose();
        }
    }

    /// <summary>
    /// Parse a Scryfall <c>/cards/named</c> JSON payload into the same
    /// <see cref="CardEntity"/> shape the DB repo returns, populating only
    /// the fields the scaffold generator consumes. Public for tests.
    /// </summary>
    public static CardEntity? ParseEntity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string Get(string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "" : "";

            // For double-faced layouts the top-level type_line / mana_cost
            // are joined "Front // Back" so the scaffold still has the
            // primary face's text to work from. Good enough for v1.
            return new CardEntity
            {
                Name = Get("name"),
                ManaCost = Get("mana_cost"),
                TypeLine = Get("type_line"),
                OracleText = Get("oracle_text"),
                Power = root.TryGetProperty("power", out var p) ? p.GetString() : null,
                Toughness = root.TryGetProperty("toughness", out var t) ? t.GetString() : null,
                Loyalty = root.TryGetProperty("loyalty", out var l) && l.ValueKind == JsonValueKind.String
                    && int.TryParse(l.GetString(), out var ln) ? ln : (int?)null,
                Set = Get("set"),
            };
        }
        catch
        {
            return null;
        }
    }
}
