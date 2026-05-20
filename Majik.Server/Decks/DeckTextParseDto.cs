namespace Majik.Server.Decks;

public sealed record ParseDeckRequest(string Text);

public sealed record ParseDeckResultDto(
    IReadOnlyList<DeckCardEntryDto> Mainboard,
    IReadOnlyList<DeckCardEntryDto> Sideboard,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> Warnings);
