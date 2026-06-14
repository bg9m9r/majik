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
    /// "Whenever you tap an untapped creature an opponent controls" — fires on
    /// a <see cref="PermanentTappedEvent"/> when (a) the tap was caused by
    /// <paramref name="controller"/> (the "you"), (b) the tapped permanent is
    /// a creature, and (c) it is controlled by a player other than
    /// <paramref name="controller"/> (an opponent). Models Solitary Sanctuary
    /// (CR 603.2 — the trigger event is "you tapping", so a tap with no
    /// attributed actor, or a tap of your own / your other opponents'
    /// creatures by someone else, does not fire).
    /// </summary>
    public static ITriggerCondition OnYouTapCreatureAnOpponentControls(Player controller)
    {
        if (controller == null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        return new EventTriggerCondition<PermanentTappedEvent>((e, _) =>
            ReferenceEquals(e.CausedBy, controller)
            && e.Permanent.HasType(CardType.Creature)
            && e.Permanent.Controller != null
            && !ReferenceEquals(e.Permanent.Controller, controller));
    }

    /// <summary>
    /// CR 603.2 — "Whenever ~ becomes tapped, …" self-tap trigger. Fires on a
    /// <see cref="PermanentTappedEvent"/> whose <see cref="PermanentTappedEvent.Permanent"/>
    /// IS <paramref name="source"/> (reference match), regardless of WHO caused
    /// the tap (City of Brass deals 1 damage to its controller whenever it
    /// becomes tapped for ANY reason — its own mana ability, an opponent's "tap
    /// target land", the attack tap, …). Distinct from
    /// <see cref="OnYouTapCreatureAnOpponentControls"/>, which keys on the
    /// <em>tapper</em> ("whenever you tap …"); this keys on the
    /// <em>permanent becoming tapped</em>, so <see cref="PermanentTappedEvent.CausedBy"/>
    /// is not read. <see cref="Permanent.Tap(Player?)"/> only publishes the
    /// event on a real state change (it throws if already tapped), so this never
    /// double-fires for a single tap.
    /// </summary>
    public static ITriggerCondition OnThisBecomesTapped(ICard source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventTriggerCondition<PermanentTappedEvent>(
            (e, _) => ReferenceEquals(e.Permanent, source));
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
    /// CR 601.2i / 603.3 — "When you cast this spell, …" self-cast trigger.
    /// Fires on the <see cref="SpellCastEvent"/> whose spell IS
    /// <paramref name="source"/> (reference match). The source-card match
    /// already implies "you cast" — the card is on the stack as a spell only
    /// under its controller's cast (CR 601.2) — so no controller read is
    /// needed. Self-scoped sibling of <see cref="OnSpellCast"/> /
    /// <see cref="OnNonCreatureSpellCastByController"/> (which match OTHER
    /// spells).
    /// </summary>
    public static ITriggerCondition OnCastSelf(Majik.Core.Cards.ICard source)
    {
        return new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, source));
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
    /// CR 509.1h — "Whenever ~ blocks a creature, …" per-blocker trigger. Fires
    /// on <see cref="Majik.Core.Domain.DomainEvents.CreatureBlocksEvent"/> whose
    /// <c>Blocker</c> IS <paramref name="source"/> (reference match). The
    /// blocked attacker travels on the event so the trigger's effect can act on
    /// that specific creature (Brimaz — "create a token blocking that creature").
    /// </summary>
    public static ITriggerCondition OnBlockSelf(Majik.Core.Cards.ICard source)
    {
        return new EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureBlocksEvent>(
            (e, _) => ReferenceEquals(e.Blocker, source));
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

    /// <summary>
    /// CR 603.3 / CR 603.6e — "Whenever an artifact you control enters, …"
    /// The modern reminder-free wording "enters" means a permanent entering
    /// the battlefield (CR 603.6e). Fires on CardMovedEvent → Battlefield
    /// where the entering card has the Artifact type and its controller is
    /// <paramref name="controller"/>. Models the Ovalchase Daredevil /
    /// Inventors' Fair-style artifact-enters family.
    /// </summary>
    public static ITriggerCondition OnArtifactYouControlEnters(Player controller)
    {
        return new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Artifact)
            && ReferenceEquals(e.Card.Controller, controller));
    }

    /// <summary>CR 500 — "At the beginning of your upkeep / end step / draw
    /// step, …" trigger. Fires on StepStartedEvent matching the requested
    /// phase, restricted to <paramref name="controller"/>'s own turns.</summary>
    public static ITriggerCondition OnStepBegin(
        Player controller, Majik.Core.StateMachine.StepStateType step)
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
    /// CR 119.3 / CR 109.5 — "Whenever an opponent gains life, …" trigger. The
    /// opponent-scoped mirror of <see cref="OnLifeGainedByPlayer"/>: fires on
    /// <see cref="LifeChangedEvent"/> where the player whose life increased is
    /// NOT <paramref name="controller"/> (every other player in the game is an
    /// opponent — CR 102.2) AND the life total strictly increased
    /// (NewLife &gt; PreviousLife — life *gain*, not life loss). Models the
    /// Kavu Predator / "whenever an opponent gains life" punish family.
    /// </summary>
    public static ITriggerCondition OnLifeGainedByOpponent(Player controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        return new EventTriggerCondition<LifeChangedEvent>((e, _) =>
            !ReferenceEquals(e.Player, controller) && e.NewLife > e.PreviousLife);
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
    /// CR 701.8 — "Whenever you discard a card, …" trigger. Fires on
    /// <see cref="DiscardedEvent"/> where <paramref name="player"/> matches
    /// the discarding player (CR 109.5 — "you"). Used by Flameblade Adept,
    /// Horror of the Broken Lands, Curator of Mysteries and the broader
    /// discard-payoff family. The discard is always controller-scoped, so a
    /// "you discard" clause and a "~ discards" self-clause coincide.
    /// </summary>
    public static ITriggerCondition OnDiscard(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        return new EventTriggerCondition<DiscardedEvent>((e, _) =>
            ReferenceEquals(e.Player, player));
    }

    /// <summary>
    /// CR 603.1 + CR 701.16 + CR 109.5 — "Whenever an opponent sacrifices
    /// a [type] permanent, …" trigger. Fires on the dedicated
    /// <see cref="PermanentSacrificedEvent"/> (published by the bus-aware
    /// sacrifice paths — cost / edict / land-binder / token self-sac) when
    /// the <see cref="PermanentSacrificedEvent.SacrificingPlayer"/> is NOT
    /// <paramref name="controller"/> (every other player in the game is an
    /// opponent — CR 102.1). The opponent-scoped mirror of a "whenever you
    /// sacrifice …" clause; this is the producer-side primitive the
    /// Blood-Artist-on-opponent-sac / It-That-Betrays / Vengeful-Tracker
    /// payoff family consumes.
    ///
    /// <para>
    /// When <paramref name="ofType"/> is supplied the sacrificed permanent
    /// must have that card type ("sacrifices an <b>artifact</b>" — Vengeful
    /// Tracker; "sacrifices a <b>creature</b>" — Blood Artist-on-opponent
    /// shape). When null any permanent type matches ("sacrifices a
    /// permanent"). The card type is read off the sacrificed card's
    /// last-known types (it is already in its owner's graveyard by the time
    /// the event publishes, CR 701.16a — but card-type membership is a
    /// printed/characteristic property, not zone-dependent).
    /// </para>
    ///
    /// <para>
    /// This predicate does NOT filter on
    /// <see cref="PermanentSacrificedEvent.WasToken"/> — "an opponent
    /// sacrifices an artifact/permanent" fires on a token just the same
    /// (Vengeful Tracker, Blood Artist). A "nontoken permanent" clause (It
    /// That Betrays — which must pull the card back out of the graveyard,
    /// impossible for a token per CR 111.7) adds that filter on top of this
    /// predicate.
    /// </para>
    /// </summary>
    public static ITriggerCondition OnOpponentSacrifices(
        Player controller, CardType? ofType = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        return new EventTriggerCondition<PermanentSacrificedEvent>((e, _) =>
            !ReferenceEquals(e.SacrificingPlayer, controller)
            && (ofType is null || e.SacrificedCard.HasType(ofType.Value)));
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
