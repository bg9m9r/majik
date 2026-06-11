namespace Majik.Bot.OpponentModel;

using Majik.Bot.Decks;

/// <summary>
/// Static metagame-popularity prior over the curated BotDeckCatalog archetypes.
/// Hand-authored relative weights (common decks higher, niche combo lower), normalized
/// so the prior over all known archetypes sums to 1. Seeds the cold-start belief before
/// any opponent cards are revealed (see ArchetypeInferencer). The exact values are not
/// load-bearing — they only seed cold start; the idf likelihood dominates once cards reveal.
/// </summary>
public static class MetagamePrior
{
    private static readonly IReadOnlyDictionary<string, double> Raw = new Dictionary<string, double>
    {
        ["Prowess"] = 10, ["Burn"] = 8, ["BorosEnergy"] = 9, ["DomainZoo"] = 6,
        ["Affinity"] = 5, ["Rhinos"] = 5, ["AzoriusControl"] = 6, ["DimirMidrange"] = 6,
        ["SultaiMidrange"] = 4, ["MonoBlackMidrange"] = 3, ["EldraziTron"] = 5,
        ["EldraziRamp"] = 4, ["GruulBroodscale"] = 4, ["EldraziBroodscale"] = 3,
        ["RubyStorm"] = 4, ["LivingEnd"] = 4, ["GoryoVengeance"] = 3, ["Neobrand"] = 2,
        ["Belcher"] = 2, ["GrixisReanimator"] = 3, ["AzoriusBlink"] = 3,
        ["EsperBlink"] = 3, ["Yawg"] = 4, ["BorosLandDestruction"] = 2,
    };

    private static readonly Dictionary<string, double> Normalized = Normalize();

    private static Dictionary<string, double> Normalize()
    {
        const double Floor = 1.0;  // any catalog archetype missing from Raw gets a small floor → never zero
        var all = new Dictionary<string, double>();
        foreach (var a in BotDeckCatalog.Archetypes)
            all[a] = Raw.TryGetValue(a, out var w) ? w : Floor;
        var total = all.Values.Sum();
        foreach (var k in all.Keys.ToList()) all[k] /= total;
        return all;
    }

    public static double Weight(string archetype) =>
        Normalized.TryGetValue(archetype, out var w) ? w : 0.0;

    public static IReadOnlyList<(string Archetype, double Weight)> All =>
        Normalized.Select(kv => (kv.Key, kv.Value)).ToList();
}
