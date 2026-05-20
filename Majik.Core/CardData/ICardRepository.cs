using Majik.Core.CardData.Database;

namespace Majik.Core.CardData;

/// <summary>
/// Card-data lookup abstraction. Production: <see cref="DbCardRepository"/>
/// against the Scryfall SQLite DB. Tests: in-memory implementations.
/// </summary>
public interface ICardRepository
{
    CardEntity? GetByName(string name);

    /// <summary>Substring (case-insensitive) match on <c>Name</c>. Returns
    /// up to <paramref name="limit"/> rows sorted by name ascending.
    /// Pass <paramref name="q"/> = <c>null</c> or empty for no filter on
    /// name. When <paramref name="implementedOnly"/> is true, excludes
    /// rows where <c>IsImplemented = false</c>.
    /// Optional <paramref name="colors"/>, <paramref name="types"/>, and
    /// <paramref name="cmcBuckets"/> narrow results further; <c>null</c>
    /// means "no filter on that dimension". Colors use Scryfall single-letter
    /// codes (W/U/B/R/G); pass <c>"C"</c> to match colorless cards.
    /// CMC bucket 7 means "7 or more".</summary>
    IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null);

    /// <summary>Read-only check by exact name. Returns false when the
    /// card isn't in the DB.</summary>
    bool IsImplemented(string name);

    /// <summary>Toggles the <c>IsImplemented</c> flag for an existing
    /// card. Throws <see cref="ArgumentException"/> when the name has no
    /// row in the DB.</summary>
    void SetImplemented(string name, bool value);
}
