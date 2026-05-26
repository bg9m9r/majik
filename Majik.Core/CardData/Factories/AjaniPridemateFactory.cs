using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ajani's Pridemate (Magic 2011, {1}{W}).
///
/// Creature — Cat Soldier 2/2. Oracle text (current Scryfall, post-errata):
///   "Whenever you gain life, put a +1/+1 counter on Ajani's Pridemate."
///
/// Note: an earlier printing read "you may put"; the current Scryfall oracle
/// drops the "may" (no controller choice). This factory implements the
/// non-optional, current oracle.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Cat Soldier at {1}{W}. Owner / controller wired.
/// - <b>Lifegain trigger (CR 603.6a / CR 119.3)</b>: "Whenever you gain
///   life, put a +1/+1 counter on Ajani's Pridemate." Wired via
///   <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> (filtered to Pridemate's controller AND
///   strictly-positive deltas — life *gain*, not life loss). On resolution
///   one <see cref="CounterType.PlusOnePlusOne"/> counter is placed via
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   style replacements (CR 614) can rewrite the count.
/// - CR 122.1 — the trigger fires once per life-gain event regardless of
///   the gained amount (no scaling: a single life gain of 5 still places
///   exactly one counter). Matches the printed wording.
/// - The trigger is active only while Pridemate is on the battlefield
///   (activeZones gate).
///
/// ## Lifecycle
/// The single-arg <see cref="Create(Player)"/> path attaches the trigger for
/// shape tests without <see cref="TriggerManager"/> registration. The
/// (owner, triggers, replacements) overload wires bus-driven firing and
/// routes counter placement through the replacement bus.
/// </summary>
[CardName("Ajani's Pridemate")]
public static class AjaniPridemateFactory
{
    public const string CardName = "Ajani's Pridemate";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Ajani's Pridemate with no live <see cref="TriggerManager"/>
    /// wiring. The lifegain trigger is attached to the card shape for
    /// structural / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Ajani's Pridemate with optional trigger manager and
    /// replacement bus. When <paramref name="triggers"/> is supplied, the
    /// lifegain trigger is registered so a qualifying
    /// <see cref="LifeChangedEvent"/> auto-queues the ability. When
    /// <paramref name="replacements"/> is supplied, the +1/+1 counter
    /// placement is routed through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season style replacements (CR 614) can
    /// rewrite the count.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.6a / CR 119.3 / CR 122.1.
        //   "Whenever you gain life, put a +1/+1 counter on Ajani's
        //    Pridemate."
        //
        // Condition: LifeChangedEvent for Pridemate's controller, strict
        // NewLife > PreviousLife (Triggers.OnLifeGainedByPlayer encodes
        // both filters). One counter regardless of gained amount.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (controller gained life)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        return card;
    }
}
