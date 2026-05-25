using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frogmite (Mirrodin, {4}).
///
/// Artifact Creature — Frog 2/2. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)"
///
/// ## Implementation
///
/// - 2/2 Artifact Creature — Frog with printed mana cost {4}. The
///   Artifact type is layered on via <see cref="Card.AddCardType"/> so
///   <c>HasType(Artifact)</c> + <c>HasType(Creature)</c> both pass —
///   same shape as <see cref="ArcboundRavagerFactory"/>.
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   The cost-reducer scans the caster's battlefield at cast time
///   (<see cref="CostReduction.GetEffectiveCost"/>) and lowers Frogmite's
///   generic-mana requirement by 1 per controller-controlled artifact;
///   floor-at-zero (CR 117.7c). Frogmite has no coloured pips so the
///   reduction can drive cost to {0}. A <see cref="KeywordAbility"/>
///   marker "Affinity" is also attached so keyword-scan callers (combat
///   helpers, bot heuristics, oracle-text inspectors) can see Frogmite
///   carries the keyword without having to inspect the
///   <see cref="CostReductionAbility"/> list.
///
/// The data-driven Scryfall path also picks up the reduction via the
/// <see cref="AffinityBinder"/> regex against the reminder text; this
/// factory wires the same shape so <see cref="NamedCardFactory.Create"/>
/// (test seam, no binder run) returns the fully-equipped card.
/// </summary>
[CardName("Frogmite")]
public static class FrogmiteFactory
{
    public const string CardName = "Frogmite";
    public const string PrintedManaCost = "{4}";
    public const int Power = 2;
    public const int Toughness = 2;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Frog });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Arcbound Ravager / Walking Ballista).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // Affinity for artifacts (CR 702.40 / CR 117.7).
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        return card;
    }
}
