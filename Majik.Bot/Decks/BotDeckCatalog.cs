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
        ["AzoriusBlink"]        = AzoriusBlinkDeck.Cards,
        ["AzoriusControl"]      = AzoriusControlDeck.Cards,
        ["BorosLandDestruction"] = BorosLandDestructionDeck.Cards,
        ["Rhinos"]              = RhinosDeck.Cards,
        ["DomainZoo"]           = DomainZooDeck.Cards,
        ["GruulBroodscale"]     = GruulBroodscaleDeck.Cards,
        ["EldraziBroodscale"]   = EldraziBroodscaleDeck.Cards,
    };

    public static IReadOnlyCollection<string> Archetypes => _decks.Keys;

    public static IReadOnlyList<string> Get(string archetype)
        => _decks.TryGetValue(archetype, out var list)
            ? list
            : throw new ArgumentException($"Unknown bot archetype: {archetype}", nameof(archetype));

    /// <summary>
    /// CR 100.4 / CR 408 — the archetype's 15-card sideboard (a.k.a. the
    /// wishboard). Defined per archetype in <see cref="BotDeckSideboards"/>;
    /// every card name resolves in the embedded seed (audited by
    /// <c>DeckBindingAuditTests</c>). Wish-tutor effects and nominated
    /// companions read this pile via <see cref="Majik.Core.Players.Player.Wishboard"/>
    /// once it is populated at match start.
    ///
    /// <para>Returns <see cref="System.Array.Empty{T}"/> for any
    /// <em>known</em> archetype that does not (yet) declare a sideboard, so
    /// the caller always gets a usable list and never a crash. Throws
    /// <see cref="ArgumentException"/> only for an <em>unknown</em> archetype,
    /// mirroring <see cref="Get"/>.</para>
    /// </summary>
    public static IReadOnlyList<string> GetSideboard(string archetype)
    {
        if (!_decks.ContainsKey(archetype))
            throw new ArgumentException($"Unknown bot archetype: {archetype}", nameof(archetype));

        return BotDeckSideboards.ByArchetype.TryGetValue(archetype, out var sb)
            ? sb
            : Array.Empty<string>();
    }

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
        "AzoriusBlink"        => "Bot — Azorius Blink",
        "AzoriusControl"      => "Bot — Azorius Control",
        "BorosLandDestruction" => "Bot — Boros Land Destruction",
        "Rhinos"              => "Bot — Rhinos",
        "DomainZoo"           => "Bot — Domain Zoo",
        "GruulBroodscale"     => "Bot — Gruul Broodscale",
        "EldraziBroodscale"   => "Bot — Eldrazi Broodscale",
        _ => $"Bot — {archetype}",
    };

    /// <summary>Human-friendly archetype name with spaces, WITHOUT the
    /// "Bot — " prefix — for client dropdowns where the field is already
    /// labelled "Bot Archetype". Derived from <see cref="DisplayName"/>.</summary>
    public static string Label(string archetype)
    {
        const string prefix = "Bot — ";
        var name = DisplayName(archetype);
        return name.StartsWith(prefix, StringComparison.Ordinal)
            ? name[prefix.Length..]
            : name;
    }
}
