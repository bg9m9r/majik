namespace Majik.Server.Decks;

public sealed record DeckDto(
    Guid Id,
    string OwnerSub,
    string Name,
    IReadOnlyList<DeckCardEntryDto> Mainboard,
    IReadOnlyList<DeckCardEntryDto> Sideboard,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record DeckCardEntryDto(string Name, int Count);

public sealed record CreateDeckRequest(
    string Name,
    IReadOnlyList<DeckCardEntryDto> Mainboard,
    IReadOnlyList<DeckCardEntryDto> Sideboard);

public sealed record UpdateDeckRequest(
    string Name,
    IReadOnlyList<DeckCardEntryDto> Mainboard,
    IReadOnlyList<DeckCardEntryDto> Sideboard);

public sealed record DeckError(
    string Error,
    IReadOnlyList<string>? Validation = null,
    string? Detail = null);
