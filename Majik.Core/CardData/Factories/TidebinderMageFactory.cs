using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tidebinder Mage (Magic 2014, {U}{U}).
///
/// Creature — Merfolk Wizard 2/2. Oracle text (Scryfall, verified 2026-06-02):
///   "When this creature enters, tap target red or green creature an opponent
///    controls. That creature doesn't untap during its controller's untap step
///    for as long as you control this creature."
///
/// The base shape (name / Creature — Merfolk Wizard / {U}{U} / 2/2) is
/// materialised from the embedded JSON definition (<c>tidebinder-mage.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB triggered ability is
/// layered on here — the JSON ability schema doesn't express targeted taps /
/// untap-locks (same posture as <see cref="FrostLynxFactory"/> and
/// <see cref="FloodpitsDrownerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>2/2 Creature — Merfolk Wizard, {U}{U}</b>, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> fired by
///   <see cref="CardMovedEvent"/> into <see cref="ZoneType.Battlefield"/>.
///   - 1..1 TargetRequest "target red or green creature an opponent controls"
///     (mandatory — no printed "may"). CandidateGatherer enumerates creatures
///     controlled by an opponent (CR 109.5) whose colour set
///     (<see cref="CardColors.GetColors"/>) contains Red or Green (CR 105).
///   - On resolution (CR 608.2b legality re-check): if the target is still a
///     red/green creature on the battlefield, it is tapped (CR 701.20) and
///     registered with <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>
///     so it skips its controller's untap step (CR 502.1).
/// - <b>Duration — "for as long as you control this creature" (CR 611.2b)</b>:
///   unlike Frost Lynx's one-shot "next untap step", Tidebinder's lock holds
///   until Tidebinder Mage leaves its controller's battlefield. When an
///   <see cref="IEventBus"/> is supplied, a <see cref="CardMovedEvent"/>
///   subscription removes the skip-untap rider the moment the source leaves
///   <see cref="ZoneType.Battlefield"/>. Without a bus the rider persists in
///   the registry and tests clear it via
///   <see cref="UntapStepRestrictions.Clear"/> in their fixture teardown
///   (same test-isolation posture as <see cref="FrostLynxFactory"/>).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger; no bus wiring
///   (the skip-untap persists until the caller clears
///   <see cref="UntapStepRestrictions"/>). This is the
///   <see cref="NamedCardFactory"/> dispatch overload.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — full wiring:
///   the event bus drives the source-leaves-battlefield cleanup, and the
///   TriggerManager registers the ETB trigger for production game setup.
/// </summary>
[CardName("Tidebinder Mage")]
public static class TidebinderMageFactory
{
    public const string CardName = "Tidebinder Mage";
    public const string Slug = "tidebinder-mage";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Tidebinder Mage with the ETB trigger attached for shape
    /// inspection. No bus wiring — the skip-untap persists until the caller
    /// clears <see cref="UntapStepRestrictions"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Tidebinder Mage.
    ///
    /// When <paramref name="eventBus"/> is supplied, the ETB trigger is
    /// registered with <paramref name="triggers"/> for automatic firing on
    /// <see cref="CardMovedEvent"/> to the battlefield, and a
    /// <see cref="CardMovedEvent"/> subscription removes the per-permanent
    /// untap-skip the moment Tidebinder Mage leaves the battlefield
    /// (CR 611.2b — "for as long as you control this creature").
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for bus-driven ETB trigger firing and
    /// source-leaves-battlefield cleanup. May be null — the trigger is
    /// structurally attached only and the skip-untap persists until
    /// <see cref="UntapStepRestrictions.Clear"/> is called.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — the trigger is attached to the card shape
    /// only.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        // "When this creature enters, tap target red or green creature an
        //  opponent controls. That creature doesn't untap during its
        //  controller's untap step for as long as you control this creature."
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Tidebinder Mage — tap target red/green opponent creature; it doesn't untap while you control this",
            () => ResolveEtbTrigger(card, etbTrigger, eventBus));

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
                    Description: "target red or green creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.5 — creatures controlled by a player OTHER than
                    // Tidebinder's controller, whose colour set (CR 105 /
                    // CardColors.GetColors) contains Red or Green.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(IsRedOrGreen)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // --- ETB body (CR 608.2b / 701.20 / 502.1 / 611.2b) -------------------

    private static void ResolveEtbTrigger(Creature source, TriggeredAbility? etbTrigger, IEventBus? eventBus)
    {
        if (etbTrigger == null) return;

        var chosen = etbTrigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // CR 608.2b — re-check legality at resolution: the target must still be
        // a red/green creature on the battlefield. (If it changed zones or lost
        // its red/green colour, the ability does nothing.)
        if (target.Zone != ZoneType.Battlefield) return;
        if (!target.HasType(CardType.Creature)) return;
        if (!IsRedOrGreen(target)) return;

        // CR 701.20 — tap the target creature.
        target.Tap();

        // CR 502.1 — "doesn't untap during its controller's untap step".
        // CR 611.2b — duration is "for as long as you control this creature",
        // so the rider is removed when the source leaves the battlefield, not
        // after a single untap step.
        var skipToken = new object();
        UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, target);

        ScheduleSourceLeavesCleanup(source, skipToken, eventBus);
    }

    private static void ScheduleSourceLeavesCleanup(Creature source, object skipToken, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        // CR 611.2b — remove the untap-skip the moment Tidebinder Mage leaves
        // the battlefield ("for as long as you control this creature"). A
        // change of control would also end the lock; in v1 there is no
        // control-change event surface, so we key the cleanup on the source
        // leaving the battlefield, which covers death / bounce / exile.
        Action<CardMovedEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (!ReferenceEquals(ev.Card, source)) return;
            if (ev.FromZone != ZoneType.Battlefield) return;

            UntapStepRestrictions.RemoveAll(skipToken);
            if (cleanupHandler != null)
                eventBus.Unsubscribe(cleanupHandler);
        };
        eventBus.Subscribe(cleanupHandler);
    }

    // --- colour predicate (CR 105 / CardColors.GetColors) -----------------

    private static bool IsRedOrGreen(ICard card)
    {
        var colors = CardColors.GetColors(card);
        return colors.Contains(ManaColor.Red) || colors.Contains(ManaColor.Green);
    }
}
