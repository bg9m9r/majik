using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cystbearer (Mirrodin Besieged, {2}{G}).
///
/// Creature — Phyrexian Beast 2/3. Oracle text (verified against Scryfall 2026-06-23):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)"
/// — its entire printed body is one evergreen keyword, so the BODY is a
/// vanilla 2/3 with no triggers, statics, or activated abilities.
///
/// The card's entire shape — name, Phyrexian Beast subtypes, {2}{G} mana cost,
/// 2/3, AND the Infect keyword marker — is materialised from the embedded JSON
/// definition (<c>cystbearer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// (<c>["Infect"]</c>) is turned into an <see cref="Abilities.KeywordAbility"/>
/// marker by the build pipeline (CardDefRuntime).
///
/// Infect (CR 702.90) is attached as a structurally-correct keyword marker —
/// the same shape <see cref="GlistenerElfFactory"/>, <see cref="BlightedAgentFactory"/>,
/// and <see cref="IchorclawMyrFactory"/> use. The combat-damage replacement
/// primitive (poison counters on players, -1/-1 counters on creatures) is
/// engine-side; this factory contributes the marker so the damage pipeline can
/// consult it once that replacement lands.
///
/// There is no behaviour to layer on top — the factory is the thin
/// <see cref="SkyTerrorFactory"/>-shaped wrapper. Adding this <c>[CardName]</c>
/// factory flips <c>IsImplemented</c> on automatically via
/// <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Cystbearer")]
public static class CystbearerFactory
{
    public const string CardName = "Cystbearer";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cystbearer");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
