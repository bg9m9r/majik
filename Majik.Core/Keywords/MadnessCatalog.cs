using Majik.Core.Cards;
using Majik.Core.ValueObjects;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.35 — Madness. Central name → madness-cost catalog for the
/// Modern-legal pool.
///
/// <para>
/// Madness lives on a card while it is IN HAND (the discard → exile
/// replacement and the cast-for-madness-cost window both fire off a real
/// discard), so it can't ride the per-permanent / per-spell ability wiring
/// the rest of the engine attaches at battlefield / stack time. The
/// production card-build path (<see cref="Majik.Core.CardData.NamedCardFactory"/>
/// single-arg dispatch → binder chain) never calls the
/// <c>Create(owner, ReplacementBus)</c> overload that registers a
/// <see cref="Majik.Core.Effects.MadnessReplacement"/>, so for the common
/// case — a Madness card discarded by an EFFECT or COST — there is no
/// per-card replacement on the bus to rewrite the discard.
/// </para>
///
/// <para>
/// This catalog closes that gap the same way
/// <see cref="Majik.Core.Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>
/// keeps a deterministic name list for cascade discovery: the central
/// discard funnel (<see cref="Majik.Core.Primitives.Fx.DiscardCard"/>) consults
/// the catalog so EVERY Madness card in the pool is recognised on a real
/// discard — routed to exile and offered for its madness cost — without each
/// card needing a hand-rolled factory. The existing
/// <see cref="MadnessReplacement"/> / <see cref="Costs.MadnessAlternativeCost"/>
/// / <see cref="MadnessHelper"/> trio remains valid (a registered replacement
/// is still honoured first); the catalog is the intrinsic fallback that makes
/// the mechanic work on the prod path.
/// </para>
/// </summary>
public static class MadnessCatalog
{
    /// <summary>
    /// Card name → printed madness cost (short-form, parseable by
    /// <see cref="ManaCost.Parse"/>). Derived from the embedded Modern pool's
    /// oracle text (every "Madness {cost}" line). Emrakul, the World Anew is
    /// intentionally omitted — its madness has a non-mana ({special}) rider
    /// that needs more than the shared mechanic (deferred).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Costs =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Abandon Reason"] = "{1}{R}",
            ["Alchemist's Greeting"] = "{1}{R}",
            ["Alms of the Vein"] = "{B}",
            ["Asylum Visitor"] = "{1}{B}",
            ["Avacyn's Judgment"] = "{X}{R}",
            ["Big Game Hunter"] = "{B}",
            ["Biting Rain"] = "{2}{B}",
            ["Blazing Rootwalla"] = "{0}",
            ["Bloodhall Priest"] = "{1}{B}{R}",
            ["Bloodmad Vampire"] = "{1}{R}",
            ["Brain Gorgers"] = "{1}{B}",
            ["Broken Concentration"] = "{3}{U}",
            ["Call to the Netherworld"] = "{0}",
            ["Chilling Grasp"] = "{3}{U}",
            ["Dark Withering"] = "{B}",
            ["Distemper of the Blood"] = "{R}",
            ["Fiery Temper"] = "{R}",
            ["From Under the Floorboards"] = "{X}{B}{B}",
            ["Gibbering Descent"] = "{2}{B}{B}",
            ["Gisa's Bidding"] = "{2}{B}",
            ["Gorgon Recluse"] = "{B}{B}",
            ["Grave Scrabbler"] = "{1}{B}",
            ["Hell Mongrel"] = "{2}{B}",
            ["Ichor Slick"] = "{3}{B}",
            ["Incorrigible Youths"] = "{2}{R}",
            ["Insatiable Gorgers"] = "{3}{R}",
            ["Just the Wind"] = "{U}",
            ["Kitchen Imp"] = "{B}",
            ["Malevolent Whispers"] = "{3}{R}",
            ["Markov Baron"] = "{2}{B}",
            ["Muck Drubb"] = "{2}{B}",
            ["Murderous Compulsion"] = "{1}{B}",
            ["Nagging Thoughts"] = "{1}{U}",
            ["Necrogoyf"] = "{1}{B}{B}",
            ["Nightshade Assassin"] = "{1}{B}",
            ["Psychotic Episode"] = "{1}{B}",
            ["Reckless Wurm"] = "{2}{R}",
            ["Revolutionist"] = "{3}{R}",
            ["Senseless Rage"] = "{1}{R}",
            ["Skophos Reaver"] = "{1}{R}",
            ["Stensia Masquerade"] = "{2}{R}",
            ["Stromkirk Occultist"] = "{1}{R}",
            ["Terminal Agony"] = "{B}{R}",
            ["Twins of Maurer Estate"] = "{2}{B}",
            ["Weirded Vampire"] = "{2}{B}",
            ["Welcome to the Fold"] = "{X}{U}{U}",
        };

    /// <summary>True when <paramref name="card"/> has Madness in the pool.</summary>
    public static bool HasMadness(ICard? card) =>
        card?.Name is { } name && Costs.ContainsKey(name);

    /// <summary>
    /// The madness alternative cost for <paramref name="card"/>, or
    /// <see langword="null"/> when the card has no Madness in the pool.
    /// </summary>
    public static ManaCost? CostFor(ICard? card) =>
        card?.Name is { } name && Costs.TryGetValue(name, out var cost)
            ? ManaCost.Parse(cost)
            : null;

    /// <summary>Number of catalogued Madness cards (test / audit surface).</summary>
    public static int Count => Costs.Count;

    /// <summary>The catalogued Madness card names (test / audit surface).</summary>
    public static IEnumerable<string> Names => Costs.Keys;
}
