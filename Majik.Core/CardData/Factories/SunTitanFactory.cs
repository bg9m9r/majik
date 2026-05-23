using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sun Titan (Magic 2011, {4}{W}{W}).
///
/// Creature — Giant 6/6. Oracle text:
///   "Vigilance
///    Whenever Sun Titan enters or attacks, you may return target permanent
///    card with mana value 3 or less from your graveyard to the battlefield."
///
/// ## Implemented (v1)
/// - 6/6 Creature — Giant, mana cost {4}{W}{W}.
/// - Vigilance wired as a <see cref="KeywordAbility"/> marker (CR 702.20)
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>.
/// - <b>ETB triggered ability (CR 603.1)</b>: When Sun Titan enters, the
///   controller may return target <i>permanent</i> card (any permanent type
///   — creature, artifact, enchantment, land, or planeswalker) with mana
///   value 3 or less from their graveyard to the battlefield. v1 picks
///   the first eligible permanent card deterministically; the "you may"
///   defaults to taking the action when an eligible candidate exists.
///   Movement is funnelled through <see cref="ZoneService.MoveCard"/> when
///   a service is supplied so ETB triggers on the reanimated permanent fire
///   (CR 603.6a — PR #165). Falls back to raw zone manipulation suitable
///   for shape tests when no ZoneService is supplied.
/// - <b>Attack triggered ability (CR 508.1f)</b>: Same reanimate effect on
///   the <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   surface, so each combat where Sun Titan attacks reanimates another
///   permanent card with mana value 3 or less.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: each trigger is faithfully optional in the
///   oracle text. The v1 effect always reanimates the first eligible
///   permanent card when one exists; a first-class yes/no agent prompt
///   is deferred (mirrors Priest of Fell Rites / Primeval Titan).
/// - <b>Target selection</b>: the v1 effect picks the first eligible
///   permanent card deterministically rather than prompting the agent to
///   choose among multiple eligible candidates. Same deferral pattern as
///   <see cref="PriestOfFellRitesFactory"/>.
/// - <b>Lands as permanents</b>: a land card has mana value 0, so it is
///   eligible under the ≤ 3 cap. The factory accepts any printed
///   permanent card type (creature, artifact, enchantment, land,
///   planeswalker — CR 110.4) without further filtering.
/// </summary>
public static class SunTitanFactory
{
    public const string CardName = "Sun Titan";
    public const string PrintedManaCost = "{4}{W}{W}";

    /// <summary>
    /// Construct Sun Titan with no live ZoneService / TriggerManager wiring
    /// (the shape/dispatcher path). The ETB and attack triggers are attached
    /// but not registered; reanimation uses raw zone moves — suitable for
    /// unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sun Titan with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, both triggers route the
    /// graveyard → battlefield move through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers / replacements on the reanimated permanent fire
    /// (CR 603.6a). When <paramref name="triggers"/> is supplied, both
    /// triggers are registered so dispatched events place them on the
    /// stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 6,
            toughness: 6,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Vigilance — CR 702.20. KeywordAbility marker; CombatAbilities
        // .HasVigilance / CombatValidator / Attacker.HasVigilance consume it.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // Shared reanimate effect — target permanent card with mv ≤ 3 in
        // controller's graveyard → battlefield. CR 603.1 (ETB), CR 508.1f
        // (attack). Routed through ZoneService when supplied so ETB
        // triggers on the reanimated permanent fire (CR 603.6a).
        // ----------------------------------------------------------------
        IEffect BuildReanimateEffect(string label) =>
            new Effect(label, () => ReanimatePermanentPick(owner, zoneService, maxManaValue: 3));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "Whenever Sun Titan enters …, you may return target permanent
        //    card with mana value 3 or less from your graveyard to the
        //    battlefield."
        // ----------------------------------------------------------------
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildReanimateEffect("Sun Titan: ETB reanimate target permanent card (mv ≤ 3)") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack triggered ability — CR 508.1f.
        //   "Whenever Sun Titan … attacks, you may return target permanent
        //    card with mana value 3 or less from your graveyard to the
        //    battlefield."
        // Fires on CreatureAttacksEvent matching this card.
        // ----------------------------------------------------------------
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildReanimateEffect("Sun Titan: attack reanimate target permanent card (mv ≤ 3)") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Shared reanimation helper. Picks the first <i>permanent</i> card in
    /// <paramref name="controller"/>'s graveyard whose mana value is less
    /// than or equal to <paramref name="maxManaValue"/> and moves it to
    /// the battlefield under the controller's control. Routes through
    /// <see cref="ZoneService.MoveCard"/> when available so ETB triggers on
    /// the reanimated permanent fire (CR 603.6a / PR #165); falls back to
    /// raw zone manipulation otherwise (shape-only path).
    ///
    /// A "permanent card" (CR 110.4) is a card whose printed types include
    /// any of artifact, creature, enchantment, land, or planeswalker. This
    /// excludes instant and sorcery cards in the graveyard.
    /// </summary>
    private static void ReanimatePermanentPick(
        Player controller,
        ZoneService? zoneService,
        int maxManaValue)
    {
        var pick = controller.Zones.Graveyard.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(c => c.ManaCostValue.TotalValue <= maxManaValue);

        // CR 117.x — a "you may" / target-required effect with no valid
        // target resolves as a no-op.
        if (pick == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Graveyard, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Graveyard.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }
    }
}
