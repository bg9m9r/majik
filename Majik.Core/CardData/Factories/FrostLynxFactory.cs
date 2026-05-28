using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frost Lynx (Magic Origins / reprints, {2}{U}).
///
/// Creature — Elemental Cat 2/2. Oracle text:
///   "When this creature enters, tap target creature an opponent controls.
///    That creature doesn't untap during its controller's next untap step."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elemental Cat, mana cost {2}{U}, owner/controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> fired by
///   <see cref="CardMovedEvent"/> into <see cref="ZoneType.Battlefield"/>.
///   - TargetRequest declares 1..1 "target creature an opponent controls"
///     (mandatory — no printed "may"). CandidateGatherer enumerates creature
///     permanents controlled by all opponents for the bot's ranker.
///   - On resolution (CR 608.2b): if the target is still on the battlefield,
///     it is tapped (CR 701.20) and then registered with
///     <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> so it
///     skips its controller's next untap step (CR 502.1). The skip token is
///     keyed by the TriggeredAbility instance; because Frost Lynx's wording
///     says "next untap step" rather than "as long as Frost Lynx is on the
///     battlefield", the restriction is a one-shot effect — it must be
///     removed after one untap step fires for the target's controller.
///   - "Next untap step" cleanup: when an <see cref="IEventBus"/> is
///     supplied, a one-shot <see cref="StepStartedEvent"/> subscription
///     watches for the target controller's next Untap step and removes the
///     restriction (CR 502.1 / CR 611.2b). Without a bus the skip-untap
///     persists in the registry and tests must call
///     <see cref="UntapStepRestrictions.Clear"/> in their fixture teardown
///     (this matches the test-isolation posture shared by ManaVaultFactory
///     and ChokeFactory tests).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger; no bus wiring
///   (skip-untap persists until registry cleared). For shape tests and the
///   <see cref="NamedCardFactory"/> dispatcher.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — full wiring:
///   event bus drives automatic trigger firing and "next untap step" cleanup;
///   TriggerManager registers the ETB trigger for production game setup.
/// </summary>
[CardName("Frost Lynx")]
public static class FrostLynxFactory
{
    public const string CardName = "Frost Lynx";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Frost Lynx with the ETB trigger attached for shape
    /// inspection. No bus wiring — skip-untap persists until the caller
    /// clears <see cref="UntapStepRestrictions"/>. Suitable for shape tests
    /// and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Frost Lynx.
    ///
    /// When <paramref name="eventBus"/> is supplied, the ETB trigger is
    /// registered with <paramref name="triggers"/> for automatic firing on
    /// <see cref="CardMovedEvent"/> to the battlefield, and a one-shot
    /// <see cref="StateMachine.PhaseStateType.Untap"/> step subscription
    /// removes the per-permanent untap-skip after the target controller's
    /// next untap step (CR 502.1 / "next untap step" wording).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for bus-driven ETB trigger firing and
    /// skip-untap cleanup. May be null — trigger is structurally attached
    /// only and the skip-untap persists until <see cref="UntapStepRestrictions.Clear"/>
    /// is called.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is attached to the card shape only.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        // "When this creature enters, tap target creature an opponent controls.
        //  That creature doesn't untap during its controller's next untap step."
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Frost Lynx — tap target opponent creature; it skips its controller's next untap step",
            () => ResolveEtbTrigger(etbTrigger, eventBus));

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
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);

        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // --- ETB body (CR 608.2b / 701.20 / 502.1 / 611.2b) -------------------

    private static void ResolveEtbTrigger(TriggeredAbility? etbTrigger, IEventBus? eventBus)
    {
        if (etbTrigger == null) return;

        var chosen = etbTrigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // CR 608.2b — illegal target at resolution = no effect.
        if (target.Zone != ZoneType.Battlefield) return;

        // CR 701.20 — tap the target creature.
        target.Tap();

        // CR 502.1 — "doesn't untap during its controller's next untap step".
        var skipToken = new object();
        UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, target);

        ScheduleSkipUntapCleanup(target, skipToken, eventBus);
    }

    private static void ScheduleSkipUntapCleanup(Permanent target, object skipToken, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        // One-shot: remove the skip on the FIRST Untap step that belongs
        // to the target's current controller (CR 502.1 / "next untap step").
        var targetController = target.Controller;
        Action<GameEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (ev is not StepStartedEvent sse) return;
            if (sse.StepType != PhaseStateType.Untap) return;
            if (!ReferenceEquals(sse.Player, targetController)) return;

            UntapStepRestrictions.RemoveAll(skipToken);
            if (cleanupHandler != null)
                eventBus.UnsubscribeAll(cleanupHandler);
        };
        eventBus.SubscribeAll(cleanupHandler);
    }
}
