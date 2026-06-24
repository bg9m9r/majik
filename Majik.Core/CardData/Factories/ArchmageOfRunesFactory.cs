using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Archmage of Runes (Modern Horizons 3, {3}{U}{U}).
///
/// Creature — Giant Wizard 3/6 (identity verified against the embedded
/// modern-cards seed, 2026-06-24). Oracle text:
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever you cast an instant or sorcery spell, draw a card."
///
/// ## Shape source
/// Card identity (name, {3}{U}{U}, Creature — Giant Wizard, blue, 3/6) is
/// materialised from the embedded JSON definition
/// (<c>archmage-of-runes.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-driven posture as
/// <see cref="BurrowguardMentorFactory"/>. The two abilities below are layered
/// on in code (the JSON <c>AbilityDefinition</c> schema expresses neither a
/// spell-cost-reduction static nor a cast trigger).
///
/// ## Implemented (v1)
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> — "Instant and
///   sorcery spells you cast cost {1} less to cast." Wired via
///   <see cref="SpellCostReductionAbility"/> with the same predicate + flat-1
///   generic reduction as <see cref="GoblinElectromancerFactory"/> /
///   <see cref="BaralChiefOfComplianceFactory"/>. Scoped to the controller's
///   battlefield by <see cref="CostReduction.GetEffectiveCost"/> ("spells YOU
///   cast"); coloured pips are untouched (CR 117.7c) and the generic bucket
///   floors at zero. Multiple reducers stack additively.
/// - <b>Instant/sorcery-cast draw trigger (CR 603.1)</b> — "Whenever you cast
///   an instant or sorcery spell, draw a card." A
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> whose
///   predicate gates on the spell's controller being Archmage's controller
///   ("you cast") AND the spell's card having the <see cref="CardType.Instant"/>
///   or <see cref="CardType.Sorcery"/> card type (CR 300.1 / 307.1). Effect
///   draws one card via <see cref="Majik.Core.Primitives.Fx.DrawCards"/> under
///   the controller (routes through the controller's replacement bus when one
///   is attached — CR 614). Same shape as
///   <see cref="SramSeniorEdificerFactory"/>'s cast-draw trigger.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The cost-reduction static (no
///   live services needed) is always attached; the cast-draw trigger is
///   attached for inspection but not registered (no trigger manager). This is
///   the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired;
///   the cast-draw trigger is registered when <paramref name="triggers"/> is
///   supplied so <see cref="SpellCastEvent"/>s on the bus route through it.
/// </summary>
[CardName("Archmage of Runes")]
public static class ArchmageOfRunesFactory
{
    public const string CardName = "Archmage of Runes";
    public const string Slug = "archmage-of-runes";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Archmage of Runes with no live trigger wiring. The
    /// instant/sorcery cost-reduction rider is static and always attached; the
    /// cast-draw trigger is attached to the card shape but not registered (no
    /// trigger manager). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Archmage of Runes with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the cast-draw trigger is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus route
    /// through it.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: Creature — Giant Wizard,
        // {3}{U}{U}, blue, 3/6.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Instant and sorcery spells you cast cost {1} less to
        // cast." Predicate gates on the spell's card type; reduction is a flat
        // 1 generic. CostReduction.GetEffectiveCost scans only the caster's
        // battlefield for this ability shape, so the "you cast" scope is
        // enforced by the cost-calc helper. Coloured pips untouched (CR 117.7c);
        // generic floors at zero. Same shape as Goblin Electromancer / Baral.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            reduction: (_, _) => 1,
            description: "Instant and sorcery spells you cast cost {1} less to cast."));

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, draw a
        // card." "You cast" → the spell's controller is this card's controller.
        // Card-type gate: Instant or Sorcery (CR 300.1 / 307.1). One event →
        // one match → one stack object (CR 603.3). Same shape as Sram's
        // cast-draw trigger.
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            var spellCard = e.Spell.Card;
            return spellCard.HasType(CardType.Instant)
                || spellCard.HasType(CardType.Sorcery);
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (whenever you cast an instant or sorcery spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
