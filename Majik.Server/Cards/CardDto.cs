namespace Majik.Server.Cards;

public sealed record CardDto(
    string Name,
    string ManaCost,
    IReadOnlyList<string> Types,
    int? Power,
    int? Toughness,
    bool IsImplemented,
    int? Cmc,
    IReadOnlyList<string> Colors,
    string? OracleText);

public sealed record CardsError(string Error, string? Detail = null);
