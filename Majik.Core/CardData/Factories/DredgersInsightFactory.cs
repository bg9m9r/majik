using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dredger's Insight (Modern Horizons 3, {1}{G}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-06-01):
///   "Whenever one or more artifact and/or creature cards leave your
///    graveyard, you gain 1 life.
///    When this enchantment enters, mill four cards. You may put an artifact,
///    creature, or land card from among the milled cards into your hand.
///    (To mill four cards, put the top four cards of your library into your
///    graveyard.)"
///
/// ## Pure-JSON factory (no C# layering)
/// Unlike <see cref="ArdentPleaFactory"/> (whose Cascade / Exalted keywords the
/// JSON schema cannot express), every ability of Dredger's Insight is already
/// expressible declaratively, so this factory is a thin
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> wrapper over
/// <c>dredgers-insight.json</c>, on the same plan as
/// <see cref="BirdsOfParadiseFactory"/>. Both triggered abilities are built
/// by <see cref="CardDefRuntime"/> from the embedded definition:
///
/// - <b>ETB mill-and-pick</b> (CR 603.6e — "When this enchantment enters"):
///   <c>etb_self</c> trigger →
///   <c>mill_then_pick_first_matching_to_hand</c> effect (amount 4, matching
///   Artifact / Creature / Land). CR 701.13 mill = top four cards of library
///   to graveyard; the first matching milled card may then be moved to hand
///   (v1 takes the first qualifying card in mill order — the "you may" opt-out
///   awaits the agent prompt system, same queue as Malevolent Rumble /
///   Ancient Stirrings).
/// - <b>Lifegain-on-graveyard-leave</b> (CR 603.2 — leaves-the-zone trigger):
///   <c>card_leaves_your_graveyard</c> trigger (cardTypes Artifact / Creature,
///   restricted to the controller's own graveyard) →
///   <c>gain_life_self</c> effect (amount 1). The "one or more" batch wording
///   (CR 603.3b) collapses to a single trigger per
///   <see cref="Majik.Core.Events.CardMovedEvent"/> in v1.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Dredger's Insight")]
public static class DredgersInsightFactory
{
    public const string CardName = "Dredger's Insight";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "dredgers-insight";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Dredger's Insight as an <see cref="Enchantment"/> owned by
    /// <paramref name="owner"/>, with both triggered abilities materialised
    /// from the embedded JSON definition.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }
}
