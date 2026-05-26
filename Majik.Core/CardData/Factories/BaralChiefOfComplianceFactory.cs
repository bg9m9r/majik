using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Baral, Chief of Compliance (Aether Revolt, {1}{U}).
///
/// Legendary Creature — Human Wizard 1/3. Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever a spell or ability an opponent controls counters a spell
///    you cast, you may draw a card. If you do, discard a card."
///
/// ## Implemented (v1)
/// - 1/3 Legendary Human Wizard, mana cost {1}{U}.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> via
///   <see cref="SpellCostReductionAbility"/>. Predicate matches instant or
///   sorcery spells (CR 300.1 / 307.1); reduction is a flat 1 generic per
///   cast. Scoped to the caster's battlefield by
///   <see cref="CostReduction.GetEffectiveCost"/> — only the controller of
///   this Baral benefits ("spells you cast"). Coloured pips are untouched
///   (CR 117.7c); floor-at-zero is enforced inside the cost-calc helper so
///   a {R} instant pays {R} when this is the only reducer in play. Same
///   shape as <see cref="GoblinElectromancerFactory"/> — multiple copies
///   stack additively (a Baral + an Electromancer reduce by {2} total).
///
/// ## Deferred (v1 gaps)
/// - <b>"Whenever a spell or ability an opponent controls counters a
///   spell you cast, you may draw a card. If you do, discard a card."</b>
///   — NOT WIRED. The engine has no <c>SpellCounteredEvent</c> /
///   <c>SpellCountered</c> bus hook today; counterspell-style cards
///   (<see cref="ManaLeakFactory"/>, <see cref="RemandFactory"/>, etc.)
///   currently remove the target via
///   <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>
///   without publishing a "spell countered" event. Once that event exists
///   the trigger plumbs in the same shape as Young Pyromancer's
///   <see cref="Domain.DomainEvents.SpellCastEvent"/> trigger: predicate
///   gates on the countering spell/ability being opponent-controlled AND
///   the countered spell being Baral's controller's spell; effect is a
///   "may draw → if drew, discard 1" via <see cref="Players.Player.DrawCard"/>
///   and an agent-prompted discard. Tracked as the "counter-event trigger"
///   gap in the workflow brief.
/// </summary>
[CardName("Baral, Chief of Compliance")]
public static class BaralChiefOfComplianceFactory
{
    public const string CardName = "Baral, Chief of Compliance";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Baral, Chief of Compliance with the spell-cost reduction
    /// rider attached as static metadata. The counter-rebate trigger is
    /// deferred (see class-level remarks).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Instant and sorcery spells you cast cost {1} less to
        // cast." Predicate gates on the spell's card type; reduction is a
        // flat 1 generic. CostReduction.GetEffectiveCost scans only the
        // caster's battlefield for this ability shape, so the "you cast"
        // scope is enforced by the cost-calc helper. Same wiring shape as
        // Goblin Electromancer.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            reduction: (_, _) => 1,
            description: "Instant and sorcery spells you cast cost {1} less to cast."));

        return card;
    }
}
