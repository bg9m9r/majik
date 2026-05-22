using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Contracts;

/// <summary>
/// Wire shape for <see cref="CardEntity"/> on the internal cards-service
/// HTTP boundary. Mirrors every gameplay-relevant field on the entity so
/// the engine binder pipeline (ScryfallCardFactory, OracleSpellBinder)
/// can reconstruct a CardEntity from the JSON without reaching into EF.
/// FormatLegalities is omitted intentionally — it's importer-internal.
/// </summary>
public sealed record CardEntityDto(
    int Id,
    string ScryfallId,
    string Name,
    string? ManaCost,
    int? Cmc,
    string TypeLine,
    string? OracleText,
    string? Power,
    string? Toughness,
    int? Loyalty,
    string Colors,
    string ColorIdentity,
    string Keywords,
    string? Set,
    string? CollectorNumber,
    string? Rarity,
    string? ImageUri,
    string Legalities,
    DateTime ImportedAt,
    DateTime? UpdatedAt,
    bool IsImplemented)
{
    public static CardEntityDto From(CardEntity e) => new(
        Id: e.Id,
        ScryfallId: e.ScryfallId,
        Name: e.Name,
        ManaCost: e.ManaCost,
        Cmc: e.Cmc,
        TypeLine: e.TypeLine,
        OracleText: e.OracleText,
        Power: e.Power,
        Toughness: e.Toughness,
        Loyalty: e.Loyalty,
        Colors: e.Colors,
        ColorIdentity: e.ColorIdentity,
        Keywords: e.Keywords,
        Set: e.Set,
        CollectorNumber: e.CollectorNumber,
        Rarity: e.Rarity,
        ImageUri: e.ImageUri,
        Legalities: e.Legalities,
        ImportedAt: e.ImportedAt,
        UpdatedAt: e.UpdatedAt,
        IsImplemented: e.IsImplemented);

    public CardEntity ToEntity() => new()
    {
        Id = Id,
        ScryfallId = ScryfallId,
        Name = Name,
        ManaCost = ManaCost,
        Cmc = Cmc,
        TypeLine = TypeLine,
        OracleText = OracleText,
        Power = Power,
        Toughness = Toughness,
        Loyalty = Loyalty,
        Colors = Colors,
        ColorIdentity = ColorIdentity,
        Keywords = Keywords,
        Set = Set,
        CollectorNumber = CollectorNumber,
        Rarity = Rarity,
        ImageUri = ImageUri,
        Legalities = Legalities,
        ImportedAt = ImportedAt,
        UpdatedAt = UpdatedAt,
        IsImplemented = IsImplemented,
    };
}

public sealed record CardsByNamesRequest(IReadOnlyList<string> Names);

public sealed record SetImplementedRequest(string Name, bool Value);

public sealed record IsImplementedResponse(bool Implemented);
