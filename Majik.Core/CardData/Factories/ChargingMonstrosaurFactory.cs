using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Charging Monstrosaur (Ixalan, {4}{R}).
///
/// Creature — Dinosaur 5/5. Oracle text (verified against Scryfall 2026-06-23):
///   "Trample, haste"
///
/// The body is a vanilla 5/5 with two keyword markers: Trample (CR 702.19) and
/// Haste (CR 702.10). The whole shape (name, Dinosaur subtype, {4}{R}, 5/5,
/// Trample, Haste) is materialised from the embedded JSON definition
/// (<c>charging-monstrosaur.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// carries both keywords, which <see cref="Definitions.CardDefRuntime"/> turns
/// into <c>KeywordAbility</c> markers honoured by the combat layer
/// (<see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> /
/// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>) — so there is no
/// bespoke behaviour to layer on, and the factory is the thin
/// <see cref="RecklessWurmFactory"/>-shaped wrapper.
/// </summary>
[CardName("Charging Monstrosaur")]
public static class ChargingMonstrosaurFactory
{
    public const string CardName = "Charging Monstrosaur";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("charging-monstrosaur");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
