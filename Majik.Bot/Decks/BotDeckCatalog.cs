namespace Majik.Bot.Decks;

/// <summary>
/// Looks up an archetype's deck list by name.
/// </summary>
public static class BotDeckCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<string>> _decks = new()
    {
        ["Burn"]              = BurnDeck.Cards,
        ["Prowess"]           = ProwessDeck.Cards,
        ["BorosEnergy"]       = BorosEnergyDeck.Cards,
        ["Yawg"]              = YawgDeck.Cards,
        ["Affinity"]          = AffinityDeck.Cards,
        ["RubyStorm"]         = RubyStormDeck.Cards,
        ["Belcher"]           = BelcherDeck.Cards,
        ["GoryoVengeance"]    = GoryoVengeanceDeck.Cards,
        ["LivingEnd"]         = LivingEndDeck.Cards,
        ["EldraziTron"]       = EldraziTronDeck.Cards,
        ["GrixisReanimator"]  = GrixisReanimatorDeck.Cards,
        ["DimirMidrange"]     = DimirMidrangeDeck.Cards,
        ["EldraziRamp"]       = EldraziRampDeck.Cards,
        ["Neobrand"]          = NeobrandDeck.Cards,
        ["EsperBlink"]        = EsperBlinkDeck.Cards,
        ["SultaiMidrange"]    = SultaiMidrangeDeck.Cards,
        ["MonoBlackMidrange"] = MonoBlackMidrangeDeck.Cards,
    };

    public static IReadOnlyCollection<string> Archetypes => _decks.Keys;

    public static IReadOnlyList<string> Get(string archetype)
        => _decks.TryGetValue(archetype, out var list)
            ? list
            : throw new ArgumentException($"Unknown bot archetype: {archetype}", nameof(archetype));

    public static string DisplayName(string archetype) => archetype switch
    {
        "Burn"              => "Bot — Burn",
        "Prowess"           => "Bot — Prowess",
        "BorosEnergy"       => "Bot — Boros Energy",
        "Yawg"              => "Bot — Yawgmoth",
        "Affinity"          => "Bot — Affinity",
        "RubyStorm"         => "Bot — Ruby Storm",
        "Belcher"           => "Bot — Belcher",
        "GoryoVengeance"    => "Bot — Goryo's Vengeance",
        "LivingEnd"         => "Bot — Living End",
        "EldraziTron"       => "Bot — Eldrazi Tron",
        "GrixisReanimator"  => "Bot — Grixis Reanimator",
        "DimirMidrange"     => "Bot — Dimir Midrange",
        "EldraziRamp"       => "Bot — Eldrazi Ramp",
        "Neobrand"          => "Bot — Neobrand",
        "EsperBlink"        => "Bot — Esper Blink",
        "SultaiMidrange"    => "Bot — Sultai Midrange",
        "MonoBlackMidrange" => "Bot — Mono-Black Midrange",
        _ => $"Bot — {archetype}",
    };
}
