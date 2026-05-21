using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dredger's Insight (Duskmourn).
///
/// Enchantment — {1}{G}. Oracle text:
///   "Whenever one or more artifact and/or creature cards leave your graveyard,
///    you gain 1 life.
///    When this enchantment enters, mill four cards. You may put an artifact,
///    creature, or land card from among the milled cards into your hand."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/dredgers-insight.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Both
/// triggered abilities (ETB mill-pick + card-leaves-graveyard lifegain)
/// are fully JSON.
///
/// ## Deferred (v1 gaps)
/// - "You may put …" is optional — v1 always picks if a qualifying card
///   is present (opt-out awaits agent prompt system).
/// - The lifegain trigger groups multiple simultaneous leavers into one
///   trigger event per the oracle text ("one or more … leave"). v1
///   fires once per individual card move; over-counting is possible if
///   multiple cards leave simultaneously. Batching awaits a
///   zone-change batch event.
/// </summary>
public static class DredgersInsightFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dredgers-insight");

    /// <summary>
    /// Construct Dredger's Insight owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        (Enchantment)CardDefinitionFactory.Build(Definition, owner);
}
