namespace Majik.Core.CardData;

/// <summary>
/// In-memory model for a single Scryfall card. Populated from the
/// embedded <c>modern-cards.json.gz</c> resource at startup; one row per
/// distinct card name across the Modern-legal pool. There is no database
/// behind this type — the previous EF-Core entity has been replaced with
/// a plain POCO. See <see cref="EmbeddedCardRepository"/> for the loader.
///
/// Mutability is preserved (settable properties) so test fixtures that
/// hand-construct entities with the property-initializer syntax keep
/// compiling; production code treats instances as immutable once placed
/// in the in-memory dictionary.
/// </summary>
public sealed class CardEntity
{
    /// <summary>Unique Scryfall ID — retained for traceability when
    /// debugging which printing seeded a name; not used by gameplay.</summary>
    public string ScryfallId { get; set; } = "";

    /// <summary>Card name. Case-sensitive collation matches the
    /// dictionary key in <see cref="EmbeddedCardRepository"/>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Mana cost string (e.g. <c>{1}{R}</c>). Null for lands.</summary>
    public string? ManaCost { get; set; }

    /// <summary>Converted mana cost / mana value.</summary>
    public int? Cmc { get; set; }

    /// <summary>Full type line (e.g. <c>Creature — Human Wizard</c>).</summary>
    public string TypeLine { get; set; } = "";

    /// <summary>Oracle text. Parsers walk this at game-start time.</summary>
    public string? OracleText { get; set; }

    /// <summary>Power (creatures). May be <c>*</c>, <c>X</c>, etc.</summary>
    public string? Power { get; set; }

    /// <summary>Toughness (creatures). May be <c>*</c>, <c>X</c>, etc.</summary>
    public string? Toughness { get; set; }

    /// <summary>Loyalty (planeswalkers).</summary>
    public int? Loyalty { get; set; }

    /// <summary>Colors as JSON array string (e.g. <c>["R"]</c>). Empty
    /// array for colorless.</summary>
    public string Colors { get; set; } = "[]";

    /// <summary>Color identity as JSON array string.</summary>
    public string ColorIdentity { get; set; } = "[]";

    /// <summary>Keywords as JSON array string. Consumed by
    /// <see cref="KeywordBinder"/>; the embedded JSON does not carry the
    /// Scryfall <c>keywords</c> array (parsers re-extract from OracleText
    /// at runtime), so this defaults to an empty array.</summary>
    public string Keywords { get; set; } = "[]";

    /// <summary>Legalities as JSON object string. Retained as an empty
    /// stub — the embedded pool is already filtered to Modern-legal, so
    /// no per-format lookup is needed at runtime. Test fixtures that
    /// initialize this field continue to compile.</summary>
    public string Legalities { get; set; } = "{}";

    /// <summary>Whether the engine has a hand-written factory + binder
    /// pipeline for this card. Baked into the embedded JSON at export
    /// time; <see cref="EmbeddedCardRepository.SetImplemented"/> throws.
    /// </summary>
    public bool IsImplemented { get; set; }

    /// <summary>Scryfall set code. Not populated by the embedded seed
    /// (set-level metadata is irrelevant to gameplay binders) — kept on
    /// the POCO so test fixtures that initialize it via object-initializer
    /// syntax continue to compile.</summary>
    public string? Set { get; set; }

    /// <summary>Scryfall collector number. Same rationale as
    /// <see cref="Set"/> — preserved as a non-load-bearing stub.</summary>
    public string? CollectorNumber { get; set; }
}
