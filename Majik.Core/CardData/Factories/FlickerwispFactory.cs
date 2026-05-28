using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flickerwisp (Eventide / various reprints, {1}{W}{W}).
///
/// Creature — Elemental 3/1. Oracle text:
///   "Flying.
///    When this creature enters, exile another target permanent. Return
///    that card to the battlefield under its owner's control at the
///    beginning of the next end step."
///
/// ## Implementation
///
/// - 3/1 Creature — Elemental, mana cost {1}{W}{W}. Color identity white
///   (derived from the {W}{W} pips per CR 202.2c). Mana value 3 (CR 202.3).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> for evasion in
///   the combat validator. Same wiring shape as CloudkinSeerFactory.
/// - <b>ETB triggered ability</b> (CR 603.1 / CR 603.6a):
///   "When this creature enters, exile <i>another</i> target permanent.
///   Return that card to the battlefield under its owner's control at the
///   beginning of the next end step."
///
///   Key rules distinctions vs. Charming Prince mode 2:
///   - "another permanent" = any permanent type (Creature, Artifact,
///     Enchantment, Land, Planeswalker) that is a distinct object from
///     Flickerwisp itself. No ownership restriction — "another" is the only
///     filter (CR 115.5b). The gatherer uses <see cref="Permanent"/> as the
///     base type to accept all permanent types.
///   - "under its owner's control" (CR 108.3 / CR 614) — the return routes
///     through the <em>owner's</em> zones, not necessarily the controller's.
///     Important for cards that have changed controllers (e.g. Act of
///     Treason targets) — the card goes back to the original owner.
///
///   Delayed end-step return (CR 603.7): same pattern as
///   <see cref="CharmingPrinceFactory.ExecuteBlink"/> and
///   <see cref="PheliaExuberantShepherdFactory"/>. The
///   <see cref="DelayedTriggeredAbility"/> fires on the first
///   <see cref="StepStartedEvent"/> with <c>StepType == End</c> after
///   the ETB resolved.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached for
///   shape inspection; not registered with a <see cref="TriggerManager"/>.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   ETB trigger registered with <paramref name="triggers"/> so
///   <see cref="CardMovedEvent"/>s on the bus route it to the stack.
/// </summary>
[CardName("Flickerwisp")]
public static class FlickerwispFactory
{
    public const string CardName = "Flickerwisp";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Flickerwisp with no live wiring. The ETB trigger is
    /// attached for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Flickerwisp with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is registered
    /// so <see cref="CardMovedEvent"/>s published on the bus route it to the
    /// stack.
    /// </summary>
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
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Keyword marker — CombatAbilities.HasFlying
        // reads this for evasion in the combat validator. Same wire-up
        // shape as CloudkinSeerFactory and Mulldrifter.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, exile another target permanent.
        //    Return that card to the battlefield under its owner's control
        //    at the beginning of the next end step."
        //
        // "another" = distinct object from Flickerwisp itself (CR 115.5b).
        // "target permanent" = no type filter; all Permanent subtypes are
        //   legal (Creature, Artifact, Enchantment, Land, Planeswalker).
        // "owner's control" = return via target.Owner's zones (CR 108.3),
        //   NOT the controller's — handles control-swapped permanents.
        // Delayed return = DelayedTriggeredAbility on first end-step event
        //   after the ETB (CR 603.7), same as CharmingPrince mode 2.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        // Gather all players visible at resolve time so the candidate list
        // spans both sides of the board. We capture owner (not controller)
        // so that a post-resolve controller change does not break the
        // gatherer — but the actual resolve time re-check uses
        // card.Controller ?? owner for consistency with other factories.
        var etbEffect = new Effect(
            $"{CardName}: exile another target permanent (when this creature enters)",
            () =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;

                ExecuteExileAndDelayedReturn(etbTrigger, card, controller, triggers);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // "another target permanent" — all permanents on the
                // battlefield except Flickerwisp itself (CR 115.5b).
                new TargetRequest(
                    Description: "another target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(p => !ReferenceEquals(p, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // ------------------------------------------------------------------
    // ETB body helper
    // ------------------------------------------------------------------

    /// <summary>
    /// Execute the Flickerwisp ETB: exile the chosen target permanent, then
    /// register a delayed end-step triggered ability that returns it to its
    /// owner's battlefield (CR 603.7).
    ///
    /// "under its owner's control" — the return routes through
    /// <c>target.Owner</c>'s zones (CR 108.3 / CR 614), which may differ
    /// from the current controller if the permanent was stolen.
    /// </summary>
    private static void ExecuteExileAndDelayedReturn(
        TriggeredAbility trigger,
        Creature source,
        Player controller,
        TriggerManager? triggers)
    {
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;
        if (chosen[0][0] is not Permanent target) return;

        // CR 608.2b — resolution-time legality re-checks.
        if (target.Zone != ZoneType.Battlefield) return;
        if (ReferenceEquals(target, source)) return;   // "another"

        var targetOwner = target.Owner ?? controller;

        // CR 701.21 — Exile. Owner-routed zone moves so LTB events fire.
        targetOwner.Zones.Battlefield.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);

        // CR 603.7 — register a delayed end-step return.
        // Skipped when no TriggerManager is wired (shape-only tests).
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var returnEffect = new Effect(
            $"{CardName}: return exiled permanent to owner's battlefield at next end step (CR 603.7)",
            () =>
            {
                // CR 111.8 — tokens cease to exist when they leave the
                // battlefield; guard defensively so a token blink no-ops
                // rather than crashing (same posture as CharmingPrince).
                if (target.Zone != ZoneType.Exile) return;

                // "under its owner's control" (CR 108.3) — route through the
                // owner's zones. This correctly handles permanents whose
                // controller was different from the owner at exile time (e.g.
                // Act of Treason targets go back to their true owner).
                targetOwner.Zones.Exile.RemoveCard(target);
                targetOwner.Zones.Battlefield.AddCard(target);
                target.SetZone(ZoneType.Battlefield);
                target.SetController(targetOwner);   // owner's control, not thief's
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }
}
