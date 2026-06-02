using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Foundry Inspector (Kaladesh — Artifact Creature {3}).
///
/// Oracle text (verified against Scryfall):
///   "Artifact spells you cast cost {1} less to cast."
///
/// ## Shape source
/// Card identity (name, {3}, 3/2, Artifact Creature — Construct) is loaded from
/// <c>Majik.Core/CardData/Cards/foundry-inspector.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Artifact + Creature types are
/// declared in the JSON <c>types</c> array, so <see cref="CardDefinitionFactory"/>
/// stamps both (CR 301.1 / 302.1) — meaning Foundry Inspector itself qualifies
/// as an artifact spell under another Inspector's discount.
///
/// The static cost-reduction rider is not expressible in the JSON ability
/// schema, so it is attached in code below — the identical shape used by
/// <see cref="EtheriumSculptorFactory"/> (the suggested analogue: same
/// "Artifact spells you cast cost {1} less" static cost reducer).
///
/// ## Implemented (v1)
/// - 3/2 Construct (CR 308 / 205.3m) at {3}, Artifact + Creature types.
/// - <b>Artifact-spell cost reduction rider (CR 117.7 / CR 601.2f)</b> wired via
///   <see cref="SpellCostReductionAbility"/>. The predicate matches any spell
///   carrying <see cref="CardType.Artifact"/>; the reduction is a flat 1 generic
///   per cast. Scoped to the caster's battlefield by
///   <see cref="CostReduction.GetEffectiveCost"/> — only the controller of this
///   Foundry Inspector benefits ("spells you cast"). Coloured pips are untouched
///   (CR 117.7c); floor-at-zero is enforced inside the cost-calc helper so a {1}
///   artifact pays {0}. Multiple copies stack additively.
/// </summary>
[CardName("Foundry Inspector")]
public static class FoundryInspectorFactory
{
    public const string CardName = "Foundry Inspector";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("foundry-inspector");

    /// <summary>
    /// Construct Foundry Inspector with the artifact-spell cost reduction rider
    /// attached as static metadata. Cost-calc scan is handled by
    /// <see cref="CostReduction.GetEffectiveCost"/> at cast time.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Artifact spells you cast cost {1} less to cast."
        // Predicate gates on the spell carrying CardType.Artifact; reduction is
        // a flat 1 generic. CostReduction.GetEffectiveCost scans only the
        // caster's battlefield for this ability shape, so the "you cast" scope
        // is enforced by the cost-calc helper.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Artifact),
            reduction: (_, _) => 1,
            description: "Artifact spells you cast cost {1} less to cast."));

        return card;
    }
}
