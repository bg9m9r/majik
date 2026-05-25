using Majik.Core.Cards;

namespace Majik.Core.CardData;

/// <summary>
/// Card-data lookup abstraction. Production:
/// <see cref="EmbeddedCardRepository"/> against the embedded
/// <c>modern-cards.json.gz</c> resource. Tests use either an inline
/// dict implementation or the same embedded repo.
/// </summary>
public interface ICardRepository
{
    CardEntity? GetByName(string name);

    /// <summary>Prefix (case-insensitive) match on <c>Name</c>. Returns
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

    /// <summary>Bulk exact-name lookup. Returns all cards whose
    /// <c>Name</c> exactly matches one of the supplied names. Unknown
    /// names are silently omitted.</summary>
    IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names);

    /// <summary>Read-only check by exact name. Returns false when the
    /// card isn't in the pool.</summary>
    bool IsImplemented(string name);

    /// <summary>
    /// Legacy mutator. The implemented flag is now build-time
    /// (baked into the embedded JSON), so the production repository
    /// throws <see cref="NotSupportedException"/>. Kept on the
    /// interface for test fixtures that still call it.
    /// </summary>
    void SetImplemented(string name, bool value);

    /// <summary>
    /// Reads the persisted <see cref="BotIntent"/> for a card.
    /// Default implementation returns <see cref="BotIntent.None"/> —
    /// the compiled-template cache that previously sourced this was
    /// deleted along with the SQLite DB; the bot now falls back to a
    /// live <c>SpellTemplateRegistry</c> walk per cast.
    /// </summary>
    BotIntent IntentFor(string cardName) => BotIntent.None;
}
