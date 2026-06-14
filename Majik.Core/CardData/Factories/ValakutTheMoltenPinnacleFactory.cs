using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Valakut, the Molten Pinnacle (Zendikar / Modern
/// reprints).
///
/// Land. Oracle text:
///   "Valakut, the Molten Pinnacle enters tapped unless you control five
///    or more other Mountains.
///    Whenever a Mountain enters under your control, if you control at
///    least five other Mountains, you may have Valakut, the Molten
///    Pinnacle deal 3 damage to any target.
///    {T}: Add {R}."
///
/// ## Implemented (v1)
/// - <b>Non-basic Land</b> with no printed subtype — Valakut is neither a
///   basic nor a Mountain, so its own ETB never satisfies the "Mountain
///   enters" trigger condition (only OTHER Mountains arriving fire it).
/// - <b>{T}: Add {R}</b> — vanilla <see cref="ManaAbility"/> wired.
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered via
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The predicate counts Mountains on the
///   controller's battlefield, excluding Valakut itself (so the threshold
///   is unaffected even if some Layer-4 retype ever stamps Mountain onto
///   Valakut). Single-arg dispatcher path skips the replacement
///   (replacement bus unavailable) — Valakut just enters untapped in that
///   posture, mirroring how every other ETB-tapped factory
///   (Inspiring Vantage / Boseiju / …) defers the restriction to the
///   binder layer for shape-only construction.
/// - <b>Landfall-style triggered ability (CR 603.1 / 603.6a)</b> over
///   <see cref="CardMovedEvent"/>: fires when any Mountain enters the
///   battlefield under Valakut's controller, gated on the controller
///   already having ≥5 Mountains EXCLUDING the just-entered one (CR
///   614.6 — the trigger samples the live battlefield AFTER the move
///   completes, so the entering Mountain shows up in the battlefield
///   tally and is explicitly excluded by reference equality). The
///   resolved effect deals 3 damage to a chosen "any target" via
///   <see cref="OracleSpellBinder.DealDamage"/> with a 1..1
///   <see cref="TargetRequest"/>. The "you may" is auto-accepted in v1
///   when a target is supplied (mirrors Tireless Tracker's forced
///   trigger posture); absent a chosen target the effect no-ops
///   (CR 608.2b — do as much as possible).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape — the landfall trigger is
/// attached for shape but not registered with a
/// <see cref="TriggerManager"/>; the conditional ETB-tapped is not
/// registered against any <see cref="ReplacementBus"/>. Use the
/// <see cref="Create(Player, ReplacementBus?, TriggerManager?)"/>
/// overload to wire runtime services for end-to-end behaviour.
///
/// ## Prod path (CLOSED — was the valakut-may-targeted-landfall deferral)
/// In real games Valakut is a LAND, so it is bound from oracle text by
/// <see cref="OracleTriggeredAbilityBinder"/> (the only prod path — lands
/// never route through this [CardName] factory). That bound trigger carries
/// the "any target" <see cref="Majik.Core.Players.Agents.TargetRequest"/>
/// with a live <c>CandidateGatherer</c> (every player / creature /
/// planeswalker) and <c>MinTargets: 0</c> modelling the "you may"
/// optionality. The live priority loop drains that trigger on the
/// agent-aware async path
/// (<see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>), so the
/// controller's agent IS prompted for the "any target" and may decline the
/// optional damage by choosing no target. End-to-end prod-path coverage:
/// <c>Majik.Core.Tests/Game/ValakutLandfallDrainTests.cs</c>.
///
/// This named factory keeps the auto-accept / pre-supplied-target shape for
/// the dispatcher-/shape-test posture (no live trigger drain in those
/// harnesses); the prod binding is fully agent-prompted.
/// </summary>
[CardName("Valakut, the Molten Pinnacle")]
public static class ValakutTheMoltenPinnacleFactory
{
    public const string CardName = "Valakut, the Molten Pinnacle";

    /// <summary>
    /// Construct Valakut with no live wiring. The landfall trigger is
    /// attached for shape but is not registered with a
    /// <see cref="TriggerManager"/>; the conditional ETB-tapped
    /// replacement is omitted (shape-only).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Valakut. When <paramref name="replacements"/> is supplied
    /// the conditional ETB-tapped restriction is registered against it
    /// (Valakut enters tapped unless its controller already has ≥5
    /// Mountains). When <paramref name="triggers"/> is supplied the
    /// Mountain-ETB trigger is registered with the bus so a
    /// <see cref="CardMovedEvent"/> matching the predicate auto-queues
    /// the ability.
    /// </summary>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Valakut is just "Land" — no Mountain subtype, no Basic supertype.
        // Its own ETB therefore can never satisfy the trigger predicate.
        var card = new Land(CardName);

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // {T}: Add {R} — vanilla mana ability.
        // --------------------------------------------------------------
        card.AddAbility(new ManaAbility(card, owner, ManaCost.Parse("R")));

        // --------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "enters tapped unless you
        // control five or more other Mountains."
        // "Other" excludes Valakut itself (the `self` parameter excludes
        // it from the tally regardless of any future retype effect).
        // --------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                card,
                (controller, self) => CountOtherMountains(controller, self) >= 5));
        }

        // --------------------------------------------------------------
        // Triggered ability (CR 603.1 / 603.6a) —
        //   "Whenever a Mountain enters under your control, if you
        //    control at least five other Mountains, you may have
        //    Valakut, the Molten Pinnacle deal 3 damage to any target."
        // CR 603.4 — intervening-if checked at trigger time AND on
        // resolution. v1 samples once at event time (good enough for
        // every test posture; the CR-strict double-check awaits the
        // intervening-if surface).
        // --------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var damageEffect = new Effect(
            $"{CardName}: deal 3 damage to any target",
            () =>
            {
                if (trigger == null) return;
                if (trigger.ChosenTargets.Count == 0) return;
                if (trigger.ChosenTargets[0].Count == 0) return;

                var target = trigger.ChosenTargets[0][0];
                OracleSpellBinder.DealDamage(target, 3);
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (!e.Card.HasSubtype(CardSubtype.Mountain)) return false;
                if (!ReferenceEquals(e.Card.Controller, owner)) return false;
                // "if you control at least five other Mountains" —
                // intervening-if (CR 603.4). The just-entered Mountain
                // is on the battlefield at event-publish time, so
                // exclude it via reference equality to honour "OTHER".
                return CountOtherMountains(owner, e.Card) >= 5;
            }),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Count Mountains on <paramref name="controller"/>'s battlefield,
    /// excluding <paramref name="self"/> (the triggering or entering
    /// land — "other Mountains" per CR rules text).
    /// </summary>
    private static int CountOtherMountains(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Mountain));
}
