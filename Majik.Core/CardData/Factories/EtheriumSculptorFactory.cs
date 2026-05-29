using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Etherium Sculptor (Conflux / Shards of Alara block —
/// Artifact Creature {1}{U}).
///
/// Oracle text (verified against Scryfall):
///   "Artifact spells you cast cost {1} less to cast."
///
/// ## Implemented (v1)
/// - Artifact Creature {1}{U} 1/2 — Vedalken Artificer. The
///   <see cref="CardType.Artifact"/> type is additively stamped via
///   <see cref="Card.AddCardType"/> on top of the Creature shell so HasType
///   lookups + colour identity see both types (CR 301.1 / 302.1, mirrors
///   <see cref="MemniteFactory"/> / Frogmite).
/// - <b>Artifact-spell cost reduction rider (CR 117.7 / CR 601.2f)</b> wired
///   via <see cref="SpellCostReductionAbility"/> — the same shape used by
///   <see cref="GoblinElectromancerFactory"/> (instant/sorcery) and
///   <see cref="BaralChiefOfComplianceFactory"/>, here scoped to artifact
///   spells. The predicate matches any spell with
///   <see cref="CardType.Artifact"/> (so artifact creatures / equipment /
///   etc. all qualify); the reduction is a flat 1 generic per cast. Scoped
///   to the caster's battlefield by <see cref="CostReduction.GetEffectiveCost"/>
///   — only the controller of this Etherium Sculptor benefits ("spells you
///   cast"). Coloured pips are untouched (CR 117.7c); floor-at-zero is
///   enforced inside the cost-calc helper so a {1} artifact pays {0} and a
///   {U}-pipped artifact keeps its {U}.
///
/// Multiple copies stack: two Etherium Sculptors reduce each artifact spell
/// by {2}. Non-artifact spells are untouched.
/// </summary>
[CardName("Etherium Sculptor")]
public static class EtheriumSculptorFactory
{
    public const string CardName = "Etherium Sculptor";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Etherium Sculptor with the artifact-spell cost reduction
    /// rider attached as static metadata. Cost-calc scan is handled by
    /// <see cref="CostReduction.GetEffectiveCost"/> at cast time.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vedalken, CardSubtype.Artificer });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the Artifact
        // type so HasType lookups + colour identity see both types (mirrors
        // Memnite / Frogmite). This also makes Etherium Sculptor itself
        // benefit from another Sculptor's discount when cast.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Artifact spells you cast cost {1} less to cast."
        // Predicate gates on the spell carrying CardType.Artifact; reduction
        // is a flat 1 generic. CostReduction.GetEffectiveCost scans only the
        // caster's battlefield for this ability shape, so the "you cast"
        // scope is enforced by the cost-calc helper.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Artifact),
            reduction: (_, _) => 1,
            description: "Artifact spells you cast cost {1} less to cast."));

        return card;
    }
}
