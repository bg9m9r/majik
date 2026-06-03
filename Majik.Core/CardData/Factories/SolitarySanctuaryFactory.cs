using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Solitary Sanctuary (Bloomburrow, {2}{W}).
///
/// Enchantment. Oracle text (Scryfall, verified 2026-06-02):
///   "When this enchantment enters, tap target creature an opponent controls
///    and put a stun counter on it.
///    Whenever you tap an untapped creature an opponent controls, put a
///    +1/+1 counter on target creature you control."
///
/// This factory is the closure for the
/// <c>tap-event-and-whenever-you-tap-trigger</c> deferral: it consumes the new
/// <see cref="PermanentTappedEvent"/> (CR 701.21, published by
/// <see cref="Permanent.Tap(Player?)"/> at every tap site) via
/// <see cref="Triggers.OnYouTapCreatureAnOpponentControls(Player)"/>.
///
/// The base shape (name / Enchantment / {2}{W}) is materialised from the
/// embedded JSON definition (<c>solitary-sanctuary.json</c>); the two
/// triggered abilities are layered on here (the JSON ability schema expresses
/// neither — same posture as <see cref="FloodpitsDrownerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Enchantment, {2}{W}</b>, owner / controller wired.
/// - <b>ETB trigger (CR 603.6a)</b>: "tap target creature an opponent controls
///   and put a stun counter on it." 1..1 opponent-scoped TargetRequest
///   (mandatory — no printed "may"); on resolution (CR 608.2b legality
///   re-check) the target is tapped — attributed to this card's controller
///   (CR 603.2, so the tap fires the "whenever you tap" ability below) — and
///   one <see cref="CounterType.Stun"/> counter is placed (CR 122.1c). The
///   stun counter is honoured by the untap-step replacement in
///   <c>TurnDriver.UntapStep</c> (CR 122.1g). Same ETB shape as Floodpits
///   Drowner.
/// - <b>"Whenever you tap …" trigger (CR 603.2)</b>: fires on a
///   <see cref="PermanentTappedEvent"/> whose <see cref="PermanentTappedEvent.CausedBy"/>
///   is this card's controller, when the tapped permanent is a creature an
///   opponent controls (the deferral's headline mechanic). On resolution it
///   puts a +1/+1 counter on a chosen "target creature you control"
///   (1..1 TargetRequest scoped to the controller's creatures, CR 109.5).
///   Note the ETB tap above is itself a "you tap" event, so it ALSO triggers
///   this ability (correct CR behaviour — Solitary Sanctuary's own ETB tap
///   grows one of your creatures).
///
/// ## Deferred (v1 gaps)
/// - <b>Untapped-precondition on the tap event</b>: the printed text reads
///   "tap an <i>untapped</i> creature". <see cref="Permanent.Tap(Player?)"/>
///   only fires the event on a real state change (it throws if already
///   tapped), so the event is only ever published for a creature that WAS
///   untapped — the "untapped" qualifier is structurally satisfied and not
///   re-checked in the condition.
/// - <b>Live target prompt for the +1/+1 trigger</b>: the triggered ability
///   carries a 1..1 "target creature you control" TargetRequest with a
///   controller-scoped CandidateGatherer; the production trigger-resolution
///   path supplies the chosen target the same way it does for any other
///   targeted triggered ability (Floodpits' ETB). Shape-only construction
///   leaves <see cref="TriggeredAbility.ChosenTargets"/> empty → the counter
///   placement is a clean no-op.
/// </summary>
[CardName("Solitary Sanctuary")]
public static class SolitarySanctuaryFactory
{
    public const string CardName = "Solitary Sanctuary";
    public const string Slug = "solitary-sanctuary";
    public const int StunCountersPlaced = 1;
    public const int PlusOnePlusOneCountersPlaced = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Solitary Sanctuary owned and controlled by
    /// <paramref name="owner"/>. The base shape is materialised from the
    /// embedded JSON definition; the ETB trigger and the "whenever you tap"
    /// trigger are layered on here. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        card.AddAbility(BuildEtbTrigger(card, owner));
        card.AddAbility(BuildTapPayoffTrigger(card, owner));

        return card;
    }

    // --- ETB: tap target opponent creature + one stun counter --------------

    private static TriggeredAbility BuildEtbTrigger(Enchantment card, Player owner)
    {
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Solitary Sanctuary — tap target opponent creature and put a stun counter on it",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal target at resolution = no effect.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.21 — tap, attributed to this card's controller so the
                // tap-payoff trigger below sees a "you tap". CR 122.1c — one
                // stun counter. Fx.Tap no-ops if already tapped (CR 701.21a),
                // which also suppresses the redundant tap event.
                Fx.Tap(target, causedBy: card.Controller ?? owner);
                target.Counters.Add(CounterType.Stun, StunCountersPlaced);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.5 — creatures controlled by a player OTHER than
                    // Solitary Sanctuary's controller.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        return etbTrigger;
    }

    // --- Whenever you tap an opponent's creature: +1/+1 on your creature ----

    private static TriggeredAbility BuildTapPayoffTrigger(Enchantment card, Player owner)
    {
        TriggeredAbility? payoffTrigger = null;

        // CR 603.2 — fires on PermanentTappedEvent caused by this card's
        // controller, on a creature an opponent controls. The "untapped"
        // qualifier is structurally satisfied (Tap only fires on a real
        // state change). Scoped to the LIVE controller so a control change
        // (CR 720) reads correctly.
        var payoffCondition = new EventTriggerCondition<PermanentTappedEvent>(
            (e, _) =>
            {
                var youController = card.Controller ?? owner;
                return ReferenceEquals(e.CausedBy, youController)
                    && e.Permanent.HasType(CardType.Creature)
                    && e.Permanent.Controller != null
                    && !ReferenceEquals(e.Permanent.Controller, youController);
            });

        var payoffEffect = new Effect(
            "Solitary Sanctuary — put a +1/+1 counter on target creature you control",
            () =>
            {
                if (payoffTrigger == null) return;
                var chosen = payoffTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — re-check legality at resolution.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 122.1 — one +1/+1 counter.
                target.Counters.Add(CounterType.PlusOnePlusOne, PlusOnePlusOneCountersPlaced);
            });

        payoffTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: payoffCondition,
            effects: new[] { payoffEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // CR 109.5 — creatures controlled by Solitary Sanctuary's
                    // controller ("you control").
                    CandidateGatherer: ctx => (card.Controller ?? owner)
                        .Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        return payoffTrigger;
    }
}
