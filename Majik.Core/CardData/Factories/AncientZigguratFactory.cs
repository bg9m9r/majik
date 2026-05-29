using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Ziggurat (Conflux).
///
/// Land. Oracle text (Scryfall, verified):
///   "{T}: Add one mana of any color. Spend this mana only to cast a creature
///    spell."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/ancient-ziggurat.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The "any color"
/// mana ability (CR 106.1b) is modeled as five <see cref="Abilities.ManaAbility"/>
/// instances (one per WUBRG) — same posture as <see cref="DelightedHalflingFactory"/>
/// / the Treasure-token pattern; the mana picker can satisfy any single colour
/// pip via this land.
///
/// ## Deferred (v1 gaps)
/// - <b>Usage restriction</b>: "Spend this mana only to cast a creature
///   spell." Enforcement requires per-mana-pool entry tagging and a
///   spend-restriction check in the cast-payment flow. Not yet retrofitted —
///   same deferral as Delighted Halfling's "legendary spell" restriction.
/// </summary>
[CardName("Ancient Ziggurat")]
public static class AncientZigguratFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ancient-ziggurat");

    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
