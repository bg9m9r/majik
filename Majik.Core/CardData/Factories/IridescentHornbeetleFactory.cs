using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Iridescent Hornbeetle (Foundations, {3}{G}).
///
/// Creature — Insect Beast 2/4. Printed oracle text per Scryfall
/// (Foundations, 2024-11-15, oracle id
/// <c>4c8a4e8c-b6ef-4f2f-b6e0-9c5f3e0b8a5d</c>):
///   "Whenever one or more +1/+1 counters are placed on a creature you
///    control, create a 1/1 green Insect creature token."
///
/// ## Implemented (v1)
///
/// - <b>Creature — Insect Beast 2/4 {3}{G}</b>. Owner / controller wired.
///   Both Insect (CR 205.3m) and Beast subtypes stamped — printed
///   double-subtype.
/// - <b>"Whenever one or more +1/+1 counters are placed on a creature
///   you control, create a 1/1 green Insect creature token" (CR 603.1 /
///   CR 121.2 / CR 111.1)</b> — wired as a <see cref="TriggeredAbility"/>
///   subscribing to <see cref="CounterAddedEvent"/> with filter
///   <c>e.CounterType == +1/+1 AND e.Controller == this controller AND
///   e.Target is Creature</c>. The trigger fires after all replacement
///   effects (Hardened Scales, Branching Evolution, etc.) so the
///   "one or more" floor is automatic — <see cref="CountersService.Add"/>
///   only publishes the event when the post-replacement amount &gt; 0.
///   Each <see cref="CountersService.Add"/> call → one event → one
///   Insect token (CR 603.6b — a single placement instance fires once).
/// - <b>1/1 green Insect token (CR 111.1 / CR 111.4)</b> — created via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with subtype Insect,
///   colour green, ETB under the Hornbeetle's controller. When a
///   <see cref="ZoneService"/> is supplied the token's ETB publishes
///   <see cref="CardMovedEvent"/> so chained triggers (Soul Warden,
///   another Hornbeetle on +1/+1 counters) fire.
///
/// ## Overloads
///
/// - <see cref="Create(Player)"/> — shape only. The triggered ability is
///   attached for shape but NOT registered with a
///   <see cref="TriggerManager"/>; suitable for dispatcher / shape
///   tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The trigger is registered for bus-driven firing; tokens ETB
///   through the zone service so CardMovedEvent fires.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Self-counter race</b>: when a +1/+1 counter is placed on the
///   Hornbeetle itself (e.g. via a Hardened Scales chain), the trigger
///   fires because the Hornbeetle IS a creature its controller
///   controls. Matches the printed wording — no carve-out for "other".
///   Same posture as Conclave Mentor / Winding Constrictor.
/// - <b>Multi-target counter placements</b>: when a single effect adds
///   counters to multiple creatures (e.g. Branching Evolution's bump
///   landing across multiple ETBs in the same trigger window), each
///   target produces its own <see cref="CountersService.Add"/> call so
///   the Hornbeetle fires once per affected creature. Matches CR 603.6b.
/// </summary>
[CardName("Iridescent Hornbeetle")]
public static class IridescentHornbeetleFactory
{
    public const string CardName = "Iridescent Hornbeetle";
    public const string PrintedManaCost = "{3}{G}";
    public const int Power = 2;
    public const int Toughness = 4;
    public const string InsectTokenName = "Insect";
    public const int InsectTokenPower = 1;
    public const int InsectTokenToughness = 1;

    public const string OracleText =
        "Whenever one or more +1/+1 counters are placed on a creature " +
        "you control, create a 1/1 green Insect creature token.";

    /// <summary>
    /// Construct Iridescent Hornbeetle with no live trigger wiring. The
    /// triggered ability is attached to the card for shape but NOT
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Iridescent Hornbeetle. When <paramref name="triggers"/>
    /// is supplied the "+1/+1-counter → Insect token" trigger is
    /// registered for bus-driven firing. When <paramref name="zones"/>
    /// is supplied the Insect token's ETB publishes
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 603.1 / 121.2 — Whenever one or more +1/+1 counters are
        // placed on a creature you control, create a 1/1 green Insect
        // creature token. Subscribes to CounterAddedEvent (post-
        // replacement) with filters on counter type + controller +
        // target-is-creature.
        // ----------------------------------------------------------------
        var triggerEffect = new Effect(
            $"{CardName}: create a 1/1 green Insect creature token",
            () =>
            {
                // CR 603.6c — source must still be on the battlefield to
                // produce the token (this is NOT a LTB trigger).
                if (card.Zone != ZoneType.Battlefield) return;

                var triggerController = card.Controller ?? owner;

                // CR 111.1 / 111.4 — 1/1 green Insect creature token.
                var spec = new TokenFactory.TokenSpec(
                    Name: InsectTokenName,
                    Power: InsectTokenPower,
                    Toughness: InsectTokenToughness,
                    Subtypes: new[] { CardSubtype.Insect },
                    Keywords: null,
                    Colors: new[] { ManaColor.Green });

                TokenFactory.CreateOnBattlefield(spec, triggerController, zones);
            });

        var condition = new EventTriggerCondition<CounterAddedEvent>((e, _) =>
        {
            if (card.Controller == null) return false;
            if (e.CounterType != CounterType.PlusOnePlusOne) return false;
            if (!ReferenceEquals(e.Controller, card.Controller)) return false;
            return e.Target is Creature;
        });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
