using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Baral, Chief of Compliance (Aether Revolt, {1}{U}).
///
/// Legendary Creature — Human Wizard 1/3. Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever a spell or ability you control counters a spell, you may
///    draw a card. If you do, discard a card."
///
/// ## Implemented
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
/// - <b>Counter → loot trigger (CR 603.1 / CR 701.5)</b>: a
///   <see cref="TriggeredAbility"/> over
///   <see cref="Domain.DomainEvents.SpellCounteredEvent"/> — published from
///   the single counter chokepoint
///   (<see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>)
///   whenever a SPELL is removed from the stack by a counter. The predicate
///   gates on the event's <see cref="Domain.DomainEvents.SpellCounteredEvent.CounteringController"/>
///   being Baral's controller ("a spell or ability YOU control counters a
///   spell"). The countering controller is the controller of the
///   spell/ability that is resolving when the counter happens, threaded onto
///   <see cref="Majik.Core.Stack.Stack.CurrentResolutionController"/> by the
///   resolution entry points (StackResolver / TriggeredAbility /
///   ActivatedAbility). Effect is the canonical "loot 1" —
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> 1 then
///   <see cref="Majik.Core.Primitives.Fx.Discard"/> 1 under Baral's
///   controller (same shape as <see cref="SmugglersCopterFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the printed text is "you may draw a card. If
///   you do, discard a card." v1 takes the loot unconditionally — matches
///   the existing looter family (Smuggler's Copter, Psychic Frog, Faithless
///   Looting). Agent-driven opt-out is deferred to the broader prompt pass.
/// - <b>Discard choice</b>: <see cref="Majik.Core.Primitives.Fx.Discard"/>
///   picks the first card in hand deterministically (same gap as every
///   other looter today).
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

        // CR 603.1 / CR 701.5 — "Whenever a spell or ability you control
        // counters a spell, you may draw a card. If you do, discard a card."
        // Fires on SpellCounteredEvent (published from the counter chokepoint
        // OracleSpellBinder.RemoveFromStack). The predicate gates on the
        // countering controller being THIS Baral's controller — "a spell or
        // ability YOU control". An opponent's counter does not fire it.
        var lootCondition = new EventTriggerCondition<SpellCounteredEvent>((e, _) =>
            e.CounteringController is not null
            && ReferenceEquals(e.CounteringController, card.Controller ?? owner));

        var lootEffect = new Effect(
            $"{CardName}: may draw a card, then discard a card (you counter a spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                // v1: "you may" auto-takes the loot — matches the looter
                // family (Smuggler's Copter, Faithless Looting). "If you do"
                // is honoured: discard only when a card was actually drawn
                // (empty library → no draw → no discard).
                var drawn = Majik.Core.Primitives.Fx.DrawCards(controller, 1);
                if (drawn.Count == 0) return;
                Majik.Core.Primitives.Fx.Discard(controller, 1);
            });

        var lootTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: lootCondition,
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lootTrigger);

        return card;
    }
}
