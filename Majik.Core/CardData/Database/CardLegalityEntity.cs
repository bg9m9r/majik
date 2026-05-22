namespace Majik.Core.CardData.Database;

/// <summary>
/// One row per <c>(Card, Format)</c> pair. Stores the raw Scryfall legality
/// status (<c>legal</c>, <c>not_legal</c>, <c>banned</c>, <c>restricted</c>)
/// so coverage queries can filter by format without scanning the
/// <see cref="CardEntity.Legalities"/> JSON blob.
///
/// Adding a new format to the engine costs zero schema change — populate
/// rows under the new <see cref="Format"/> key during import.
/// </summary>
public class CardLegalityEntity
{
    /// <summary>FK to <see cref="CardEntity.Id"/>. Composite PK with <see cref="Format"/>.</summary>
    public int CardId { get; set; }

    /// <summary>Scryfall format key, lowercase (<c>modern</c>, <c>standard</c>, <c>pioneer</c>, …).</summary>
    public string Format { get; set; } = "";

    /// <summary>Raw Scryfall status string (<c>legal | not_legal | banned | restricted</c>).</summary>
    public string Status { get; set; } = "";

    public CardEntity? Card { get; set; }
}

/// <summary>
/// Well-known Scryfall format keys. The <c>CardLegalities</c> table stores raw
/// Scryfall keys verbatim, so callers can use these constants or pass any other
/// key Scryfall reports. Adding a key here is informational only — no schema
/// change required.
/// </summary>
public static class MtgFormat
{
    public const string Standard = "standard";
    public const string Pioneer = "pioneer";
    public const string Modern = "modern";
    public const string Legacy = "legacy";
    public const string Vintage = "vintage";
    public const string Pauper = "pauper";
    public const string Commander = "commander";
    public const string Brawl = "brawl";
    public const string Historic = "historic";
    public const string Alchemy = "alchemy";
    public const string Explorer = "explorer";
    public const string Timeless = "timeless";
    public const string Oathbreaker = "oathbreaker";
    public const string PauperCommander = "paupercommander";
    public const string Penny = "penny";
    public const string Duel = "duel";
    public const string Future = "future";
    public const string Predh = "predh";
    public const string Premodern = "premodern";
    public const string Oldschool = "oldschool";
    public const string Gladiator = "gladiator";
}

/// <summary>Well-known Scryfall legality status values.</summary>
public static class MtgLegalityStatus
{
    public const string Legal = "legal";
    public const string NotLegal = "not_legal";
    public const string Banned = "banned";
    public const string Restricted = "restricted";
}
