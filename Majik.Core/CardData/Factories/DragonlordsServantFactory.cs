using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dragonlord's Servant (Dragons of Tarkir, {1}{R}).
/// Creature — Goblin Shaman 1/3. Oracle text (verified against Scryfall):
///   "Dragon spells you cast cost {1} less to cast."
///
/// The base shape (name, Creature, Goblin + Shaman subtypes, {1}{R}, 1/3) is
/// materialised from the embedded JSON definition
/// (<c>dragonlords-servant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The subtype-scoped cost reducer
/// is layered on here — the JSON <c>AbilityDefinition</c> schema carries no
/// parameterised spell-cost reducer (same posture as
/// <see cref="DanithaCapashenParagonFactory"/> / <see cref="GoblinElectromancerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>"Dragon spells you cast cost {1} less to cast." (CR 117.7)</b> — wired
///   via <see cref="SpellCostReductionAbility"/>, the same subtractive-rider
///   shape Goblin Electromancer / Danitha use. The predicate gates on the spell
///   carrying the Dragon subtype (CR 205.3m via <see cref="ICard.HasSubtype"/>);
///   the reduction is a flat 1 generic per cast.
///   <see cref="Majik.Core.Costs.CostReduction.GetEffectiveCost"/> scans only
///   the caster's battlefield for this ability shape, so the "you cast" scope
///   (CR 117.7) is enforced by the cost-calc helper — only the controller of
///   this Dragonlord's Servant benefits. Coloured pips are untouched
///   (CR 117.7c) and the cost floors at zero inside the cost-calc helper.
///   Multiple copies stack additively.
/// </summary>
[CardName("Dragonlord's Servant")]
public static class DragonlordsServantFactory
{
    public const string CardName = "Dragonlord's Servant";
    public const string Slug = "dragonlords-servant";

    /// <summary>
    /// Construct Dragonlord's Servant. The cost reducer is always attached —
    /// it is a passive battlefield rider read by
    /// <see cref="Majik.Core.Costs.CostReduction"/>; no live runtime service is
    /// needed. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Goblin +
        // Shaman, {1}{R}, 1/3). No abilities in the JSON — the cost reducer is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 117.7 — "Dragon spells you cast cost {1} less to cast." Predicate
        // gates on the Dragon subtype (CR 205.3m); reduction is a flat 1
        // generic. The "you cast" scope is enforced by
        // CostReduction.GetEffectiveCost, which scans only the caster's
        // battlefield for this rider.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasSubtype(CardSubtype.Dragon),
            reduction: (_, _) => 1,
            description: "Dragon spells you cast cost {1} less to cast."));

        return card;
    }
}
