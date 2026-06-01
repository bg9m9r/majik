using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Static factory of common <see cref="ITriggerCondition"/> instances.
/// Composes <see cref="EventTriggerCondition{TEvent}"/> with predicates that
/// encode common Magic trigger phrases ("when X enters", "when X dies", ...).
/// </summary>
public static class Triggers
{
    /// <summary>
    /// A condition that never matches a live game event. Used for abilities
    /// that are placed directly onto the pending queue (CR 603.3 — already
    /// "triggered") rather than evaluated against an event — e.g. a Saga
    /// chapter ability the engine enqueues itself when the lore counter hits
    /// the chapter threshold (CR 714.2b). The ability still resolves off the
    /// stack normally; this condition just guarantees it is never re-fired by
    /// <see cref="TriggerManager.EvaluateTriggers"/>.
    /// </summary>
    public static ITriggerCondition Never()
        => new EventTriggerCondition<GameEvent>((_, _) => false);

    /// <summary>
    /// "When ~ enters the battlefield" — fires when the given source card moves
    /// to the battlefield.
    /// </summary>
    public static ITriggerCondition OnEnterBattlefieldSelf(ICard source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, source) && e.ToZone == ZoneType.Battlefield);
    }

    /// <summary>
    /// "Whenever a creature enters the battlefield" — fires for any creature entering.
    /// </summary>
    public static ITriggerCondition OnAnyCreatureEntersBattlefield()
    {
        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => e.ToZone == ZoneType.Battlefield && e.Card.HasType(CardType.Creature));
    }

    /// <summary>
    /// "Whenever another creature you control enters" — fires on a creature
    /// other than <paramref name="self"/> entering the battlefield under
    /// <paramref name="controller"/>. Models the Soul Warden / Guide of
    /// Souls / Soul Attendant family.
    /// </summary>
    public static ITriggerCondition OnAnotherCreatureYouControlEnters(
        Player controller, ICard self)
    {
        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => e.ToZone == ZoneType.Battlefield
                   && e.Card.HasType(CardType.Creature)
                   && !ReferenceEquals(e.Card, self)
                   && ReferenceEquals(e.Card.Controller, controller));
    }

    /// <summary>
    /// "When ~ dies" — creature moving from battlefield to graveyard (Rule 700.4).
    /// </summary>
    public static ITriggerCondition OnDies(ICard source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, source)
                      && e.FromZone == ZoneType.Battlefield
                      && e.ToZone == ZoneType.Graveyard);
    }

    /// <summary>
    /// "Whenever PLAYER draws a card" — fires when the given player draws.
    /// </summary>
    public static ITriggerCondition OnCardDrawnByPlayer(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        return new EventTriggerCondition<CardDrawnEvent>(
            (e, _) => ReferenceEquals(e.Player, player));
    }

    /// <summary>
    /// "Whenever a player casts a spell" — fires on any spell cast.
    /// </summary>
    public static ITriggerCondition OnSpellCast()
    {
        return new EventTriggerCondition<SpellCastEvent>((_, _) => true);
    }

    /// <summary>
    /// CR 702.50 — Prowess. "Whenever you cast a noncreature spell, this
    /// gets +1/+1 until end of turn." Fires on SpellCastEvent where the
    /// spell's controller is <paramref name="controller"/> AND the spell
    /// is non-creature.
    /// </summary>
    public static ITriggerCondition OnNonCreatureSpellCastByController(Player controller)
    {
        return new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, controller)
            && !e.Spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature));
    }

    /// <summary>
    /// CR 508.1f — "Whenever ~ attacks, …" per-attacker trigger. Fires
    /// on CreatureAttacksEvent matching <paramref name="source"/>.
    /// </summary>
    public static ITriggerCondition OnAttackSelf(Majik.Core.Cards.ICard source)
    {
        return new EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(
            (e, _) => ReferenceEquals(e.Attacker, source));
    }

    /// <summary>
    /// CR 614 / Zendikar — Landfall. "Whenever a land enters the battlefield
    /// under your control, …" Fires on CardMovedEvent → Battlefield where
    /// the card is a Land and its controller is <paramref name="controller"/>.
    /// </summary>
    public static ITriggerCondition OnLandEntersUnderControl(Player controller)
    {
        return new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Land)
            && ReferenceEquals(e.Card.Controller, controller));
    }

    /// <summary>CR 500 — "At the beginning of your upkeep / end step / draw
    /// step, …" trigger. Fires on StepStartedEvent matching the requested
    /// phase, restricted to <paramref name="controller"/>'s own turns.</summary>
    public static ITriggerCondition OnStepBegin(
        Player controller, Majik.Core.StateMachine.PhaseStateType step)
    {
        return new EventTriggerCondition<Majik.Core.Events.StepStartedEvent>((e, _) =>
            e.StepType == step
            && ReferenceEquals(e.Player, controller));
    }

    /// <summary>
    /// CR 119.3 — "Whenever you gain life, …" trigger. Fires on
    /// <see cref="LifeChangedEvent"/> where <paramref name="player"/>
    /// matches the event's player AND the life total strictly increased
    /// (NewLife &gt; PreviousLife). Used by Heliod, Sun-Crowned's lifegain
    /// trigger and other "whenever you gain life" effects.
    /// </summary>
    public static ITriggerCondition OnLifeGainedByPlayer(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        return new EventTriggerCondition<LifeChangedEvent>((e, _) =>
            ReferenceEquals(e.Player, player) && e.NewLife > e.PreviousLife);
    }

    /// <summary>
    /// CR 701.42 — "Whenever you surveil, …" trigger. Fires on
    /// <see cref="SurveilEvent"/> where <paramref name="player"/>
    /// matches the surveiling player. Used by Ledger Shredder's
    /// "Whenever Ledger Shredder surveils, put a +1/+1 counter on it"
    /// (where "you surveil" and "Ledger Shredder surveils" coincide
    /// because the surveil is always controller-scoped) and the
    /// surveil-payoff family generally.
    /// </summary>
    public static ITriggerCondition OnSurveil(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        return new EventTriggerCondition<SurveilEvent>((e, _) =>
            ReferenceEquals(e.Player, player));
    }

    /// <summary>
    /// CR 121 / CR 603.6 — "Whenever one or more +1/+1 counters are put on a
    /// permanent you control, …" trigger. Fires on
    /// <see cref="CounterAddedEvent"/> where the event's
    /// <see cref="CounterAddedEvent.Controller"/> matches
    /// <paramref name="controller"/> AND the placed counter type matches
    /// <paramref name="type"/>. Used by Animation Module's "may pay {1} →
    /// Servo token" rider (CR 603.1) and the broader counters-matter
    /// payoff family (Conclave Mentor, Winding Constrictor's symmetric
    /// rider). Fires once per <see cref="Majik.Core.Services.CountersService.Add"/>
    /// call regardless of count — the printed "one or more" floor is
    /// implicit (the service only publishes the event when amount &gt; 0).
    /// </summary>
    public static ITriggerCondition OnCounterAddedToPermanentYouControl(
        Player controller, Majik.Core.Counters.CounterType type)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (type == null) throw new ArgumentNullException(nameof(type));
        return new EventTriggerCondition<CounterAddedEvent>((e, _) =>
            ReferenceEquals(e.Controller, controller)
            && e.CounterType == type);
    }
}
