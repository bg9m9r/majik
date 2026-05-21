namespace Majik.Bot.Decks;

/// <summary>
/// Burn archetype deck list. v1: placeholder — user will provide the
/// real 60-card list at hookup time. Every card name here MUST have
/// IsImplemented=true in cards.db; BotDeckValidator enforces this at boot.
/// </summary>
internal static class BurnDeck
{
    public static IReadOnlyList<string> Cards { get; } = new[]
    {
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
    };
}
