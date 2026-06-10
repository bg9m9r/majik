namespace Majik.Bot.OpponentModel;

using Majik.Bot.Decks;

/// <summary>
/// Infers a normalized belief over the curated archetypes from an opponent's PUBLIC
/// cards (no peeking at hidden zones). Posterior(a) ∝ MetagamePrior(a) × likelihood(a),
/// where likelihood weights each observed card by its inverse archetype-frequency (idf)
/// so signature cards discriminate and basics/staples barely move the belief. Pure
/// function of the observed public card names; constructed once (idf precomputed).
/// </summary>
public sealed class ArchetypeInferencer
{
    private readonly IReadOnlyList<string> _archetypes;
    private readonly IReadOnlyDictionary<string, HashSet<string>> _lists; // archetype -> distinct card names
    private readonly IReadOnlyDictionary<string, double> _idf;            // card -> idf
    private const double Alpha = 0.5;                                     // Laplace smoothing

    public ArchetypeInferencer()
    {
        _archetypes = BotDeckCatalog.Archetypes.ToList();
        var lists = new Dictionary<string, HashSet<string>>();
        var freq = new Dictionary<string, int>(); // card -> #archetypes containing it
        foreach (var a in _archetypes)
        {
            var set = BotDeckCatalog.Get(a).ToHashSet();
            lists[a] = set;
            foreach (var card in set) freq[card] = freq.TryGetValue(card, out var c) ? c + 1 : 1;
        }
        _lists = lists;
        var n = _archetypes.Count;
        _idf = freq.ToDictionary(kv => kv.Key, kv => Math.Log((double)n / kv.Value));
    }

    public IReadOnlyList<ArchetypeWeight> Infer(IReadOnlyList<string> opponentPublicCardNames)
    {
        double observedMass = opponentPublicCardNames.Where(_idf.ContainsKey).Sum(c => _idf[c]);
        var scores = new Dictionary<string, double>();
        foreach (var a in _archetypes)
        {
            double matched = opponentPublicCardNames
                .Where(c => _lists[a].Contains(c) && _idf.ContainsKey(c)).Sum(c => _idf[c]);
            double likelihood = (matched + Alpha) / (observedMass + Alpha);
            scores[a] = MetagamePrior.Weight(a) * likelihood;
        }
        var total = scores.Values.Sum();
        return _archetypes
            .Select(a => new ArchetypeWeight(a, total > 0 ? scores[a] / total : MetagamePrior.Weight(a)))
            .ToList();
    }
}
