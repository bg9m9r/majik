using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Test Conniver — synthetic Connive keyword
/// fixture. No printed Modern-relevant Connive card had a sufficiently
/// isolated trigger ("draw, then discard; +1/+1 counter if nonland was
/// discarded") without additional scope (Ledger Shredder gates on "second
/// spell each turn", Raffine has flying + each-attack riders).
///
/// Creature — Human Rogue {1}{U} 1/1. Oracle text:
///   "When Test Conniver enters, it connives."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/test-conniver.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Single
/// ETB-triggered <c>connive_self</c> ability — JSON only.
///
/// ## Implemented (v1)
/// - Vanilla 1/1 Human Rogue shell with no Modern-relevant rider.
/// - ETB trigger: Connive (CR 701.50). Draws a card, discards a card,
///   adds a +1/+1 counter to itself if the discarded card was nonland.
///   Discard pick uses the deterministic v1 policy
///   (<see cref="Majik.Core.Keywords.ConniveAction"/>, most-recent card
///   in hand) — replaced by an agent prompt when the discard-prompt
///   system lands.
///
/// ## Deferred
/// - Real-card variants (Ledger Shredder cast-second-spell trigger,
///   Raffine attack rider) wait on the spell-cast watcher + combat-step
///   triggers respectively.
/// </summary>
public static class TestConniverFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("test-conniver");

    /// <summary>
    /// Construct Test Conniver owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
