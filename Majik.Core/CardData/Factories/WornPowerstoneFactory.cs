using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worn Powerstone (Antiquities / multiple reprints,
/// {3}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    {T}: Add {C}{C}."
///
/// ## Implemented (v1)
/// - Artifact identity, printed mana cost {3}, owner/controller wiring.
/// - <b>{T}: Add {C}{C}</b> — a single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1 — mana abilities don't use the stack). CR 107.4c — the two
///   {C} pips fold into the generic bucket via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> ("CC" yields
///   <c>Generic == 2</c>), the same colourless-rock shape as Mana Crypt /
///   Eldrazi Temple.
/// - <b>This artifact enters tapped</b> — CR 614.1c. Applied on the
///   production load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>,
///   which matches the seed oracle text and registers an
///   <see cref="Majik.Core.Effects.EntersTappedReplacement"/> on the live
///   <c>ReplacementBus</c>. This factory builds the rock untapped — exactly
///   the same division of labour as the JSON-driven surveil-land cycle
///   (<see cref="CommercialDistrictFactory"/>): the JSON definition models
///   the mana ability, the binder owns the unconditional ETB-tapped.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/worn-powerstone.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>.
/// </summary>
[CardName("Worn Powerstone")]
public static class WornPowerstoneFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("worn-powerstone");

    /// <summary>Construct Worn Powerstone owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
