using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soulherder (Modern Horizons, {1}{W}{U}).
///
/// Creature — Spirit 1/1. Oracle text:
///   "Flying."
///   "Whenever a creature is exiled from the battlefield, put a +1/+1
///    counter on Soulherder."
///   "At the beginning of your end step, you may exile target creature
///    you control, then return it to the battlefield under its owner's
///    control."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Spirit at {1}{W}{U}, owner / controller wired.
/// - <b>Flying</b> (CR 702.9) wired as a <see cref="KeywordAbility"/>
///   marker — consumed by the combat-validator block restrictions.
/// - <b>Exile-trigger</b> (CR 603.1) — "Whenever a creature is exiled
///   from the battlefield, put a +1/+1 counter on Soulherder." Listens
///   to <see cref="CardMovedEvent"/> filtered to
///   <c>FromZone == Battlefield</c> + <c>ToZone == Exile</c> on a card
///   that has <see cref="CardType.Creature"/>. Fires for ANY creature
///   any player controls (symmetric — printed text has no controller
///   filter); the source creature can be the trigger source itself or
///   any other creature. Counter placement routes through
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///   Season replacements (CR 614) can rewrite the amount.
/// - <b>End-step flicker trigger</b> (CR 500.4 / CR 603.1 + CR 701.20):
///   "At the beginning of your end step, you may exile target creature
///   you control, then return it to the battlefield under its owner's
///   control." Wired via <see cref="Triggers.OnStepBegin"/> filtered to
///   the controller's End step. The "may" decision + the target
///   selection live on the standard <see cref="TargetRequest"/> path —
///   when the controller skips the trigger no target is supplied and
///   the resolve body no-ops. On resolution the chosen creature exits
///   to its owner's exile zone then returns to the battlefield under
///   its owner's control via <see cref="ZoneService.MoveCard"/>
///   (raw-zone fallback when no service is supplied). Mirrors the
///   Ocelot Pride / Restoration Angel flicker shape.
///
/// ## Symmetry of the exile trigger
/// "Whenever a creature is exiled from the battlefield" is a symmetric
/// trigger (CR 603.6): exiling an opponent's creature (Path to Exile,
/// Skyclave Apparition, Soulherder's own end-step flicker) puts a
/// counter on Soulherder. Soulherder's own end-step flicker — which
/// targets a creature YOU control and routes it through exile —
/// likewise feeds its own counter trigger (CR 603.7 — newly-controlled
/// triggers stack independently, the exiled creature returning doesn't
/// retroactively undo the +1/+1 counter).
///
/// ## "New object" semantics on return (CR 400.7)
/// v1 reuses the same <see cref="Card"/> instance across the
/// exile / return cycle — mirrors Ocelot Pride / Sword of Hearth and
/// Home / Restoration Angel. Identity-sensitive riders (auras-via-
/// Animate-Dead, counter accounting across the flicker, etc.) would
/// diverge from paper here.
///
/// ## Deferred (v1 gaps)
/// - Flicker "may" prompt — v1 honours pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; absent a chosen
///   target the resolve body no-ops (matches the "may" optionality
///   without an explicit player prompt).
/// - LTB unregister of the listener — the registered triggers live on
///   the manager across zone changes; <c>activeZones: Battlefield</c>
///   short-circuits firings once Soulherder leaves play.
/// </summary>
[CardName("Soulherder")]
public static class SoulherderFactory
{
    public const string CardName = "Soulherder";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Soulherder with no runtime services. Flying keyword +
    /// both triggers are attached to the card shape; neither trigger is
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Soulherder with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Used by the end-step flicker effect to
    /// route the exile + return through <see cref="ZoneService"/> so
    /// downstream <see cref="CardMovedEvent"/> listeners (including
    /// Soulherder's own exile trigger) fire. Null = raw-zone fallback.</param>
    /// <param name="triggers">When supplied, both triggers are
    /// registered so bus events automatically queue the abilities.</param>
    /// <param name="replacements">When supplied, the +1/+1 counter
    /// placement on Soulherder routes through
    /// <see cref="CountersService.Add"/> so CR 614 replacements
    /// (Hardened Scales, Doubling Season, etc.) can rewrite the
    /// amount.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. KeywordAbility marker consumed by the
        // combat-validator block restrictions.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Exile trigger — CR 603.1.
        //   "Whenever a creature is exiled from the battlefield, put a
        //    +1/+1 counter on Soulherder."
        // Symmetric — no controller filter on the exiled creature.
        // ----------------------------------------------------------------
        var exileEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (a creature was exiled from the battlefield)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var exileTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.FromZone == ZoneType.Battlefield
                && e.ToZone == ZoneType.Exile
                && e.Card.HasType(CardType.Creature)),
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(exileTrigger);
        triggers?.RegisterTriggeredAbility(exileTrigger);

        // ----------------------------------------------------------------
        // End-step flicker trigger — CR 500.4 / CR 603.1 + CR 701.20.
        //   "At the beginning of your end step, you may exile target
        //    creature you control, then return it to the battlefield
        //    under its owner's control."
        // The "may" is honoured by the optional target slot — when the
        // controller passes no target the resolve body no-ops.
        // ----------------------------------------------------------------
        TriggeredAbility? flickerTrigger = null;
        var flickerEffect = new Effect(
            $"{CardName}: end-step exile-and-return target creature you control",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (flickerTrigger == null) return;

                var slots = flickerTrigger.ChosenTargets;
                if (slots.Count == 0 || slots[0].Count == 0) return;
                if (slots[0][0] is not Creature target) return;

                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(target.Controller, controller)) return;
                if (target.Zone != ZoneType.Battlefield) return;

                FlickerToOwner(target, zoneService);
            });

        flickerTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.End),
            effects: new IEffect[] { flickerEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(flickerTrigger);
        triggers?.RegisterTriggeredAbility(flickerTrigger);

        return card;
    }

    /// <summary>
    /// CR 701.20 — exile <paramref name="target"/>, then return it to
    /// the battlefield under its owner's control. Routes through
    /// <see cref="ZoneService"/> when supplied so
    /// <see cref="CardMovedEvent"/> fires for both halves (Soulherder's
    /// own exile trigger feeds back through the same bus). v1 reuses
    /// the same <see cref="Card"/> instance — no "new object" semantics
    /// per CR 400.7.
    /// </summary>
    private static void FlickerToOwner(Creature target, ZoneService? zones)
    {
        var owner = target.Owner;
        if (owner == null) return;

        var controller = target.Controller ?? owner;

        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile, owner);
            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, owner);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Exile.AddCard(target);
            target.SetZone(ZoneType.Exile);

            owner.Zones.Exile.RemoveCard(target);
            owner.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);
        }

        target.SetController(owner);
    }
}
