namespace Majik.Bot.Decks;

/// <summary>
/// Looks up an archetype's deck list by name.
/// </summary>
public static class BotDeckCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<string>> _decks = new()
    {
        ["Burn"]        = BurnDeck.Cards,
        ["Prowess"]     = ProwessDeck.Cards,
        ["BorosEnergy"] = BorosEnergyDeck.Cards,
        ["Yawg"]        = YawgDeck.Cards,
    };

    public static IReadOnlyCollection<string> Archetypes => _decks.Keys;

    public static IReadOnlyList<string> Get(string archetype)
        => _decks.TryGetValue(archetype, out var list)
            ? list
            : throw new ArgumentException($"Unknown bot archetype: {archetype}", nameof(archetype));

    public static string DisplayName(string archetype) => archetype switch
    {
        "Burn"        => "Bot — Burn",
        "Prowess"     => "Bot — Prowess",
        "BorosEnergy" => "Bot — Boros Energy",
        "Yawg"        => "Bot — Yawgmoth",
        _ => $"Bot — {archetype}",
    };
}
