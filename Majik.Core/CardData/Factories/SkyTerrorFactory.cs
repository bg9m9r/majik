using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sky Terror (Ixalan, {R}{W}).
///
/// Creature — Dinosaur 2/2. Oracle text (verified against Scryfall 2026-06-23):
///   "Flying, menace"
/// — its entire printed body is two evergreen keywords, so the BODY is a
/// vanilla 2/2 with no triggers, statics, activated abilities, or other text.
///
/// The card's entire shape — name, Dinosaur subtype, multicoloured {R}{W} mana
/// cost, 2/2, AND the two keyword markers — is materialised from the embedded
/// JSON definition (<c>sky-terror.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// (<c>["Flying", "Menace"]</c>) is turned into <see cref="Abilities.KeywordAbility"/>
/// markers by the build pipeline (CardDefRuntime), and both keywords are already
/// engine-supported by combat:
///   - <b>Flying (CR 702.9)</b> — read by the block-legality check
///     (<see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>): only
///     creatures with flying or reach may block a flier.
///   - <b>Menace (CR 702.111)</b> — read by the blocker-count check
///     (<see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/>): the
///     creature can't be blocked except by two or more creatures.
///
/// There is no behaviour to layer on top — the factory is the thin
/// <see cref="WeirdedVampireFactory"/>-shaped wrapper. Adding this
/// <c>[CardName]</c> factory flips <c>IsImplemented</c> on automatically via
/// <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Sky Terror")]
public static class SkyTerrorFactory
{
    public const string CardName = "Sky Terror";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sky-terror");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
