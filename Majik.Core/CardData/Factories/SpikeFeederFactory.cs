using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spike Feeder (Tempest, {1}{G}).
///
/// Creature — Spike 0/0. Oracle text:
///   "Spike Feeder enters with two +1/+1 counters on it.
///    {2}, Remove a +1/+1 counter from Spike Feeder: You gain 2 life."
///
/// ## Implemented (v1)
/// - 0/0 Creature — Spike at {1}{G}.
/// - <b>Enters-with-counters (CR 614.1d)</b>: registers an
///   <see cref="EntersWithCountersReplacement"/> against the supplied
///   <see cref="ReplacementBus"/> so the ZoneService's ETB pipeline
///   rewrites <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> to 2
///   and applies the counters after the permanent lands (so SBAs see
///   the correct 2/2 power/toughness — without the counters Spike
///   Feeder is a 0/0 and CR 704.5f would send it to the graveyard).
///   When no replacement bus is supplied callers can stamp the
///   counters manually via <see cref="MarkEntersWithCounters"/>
///   (shape-only test fallback — mirrors Modular).
/// - <b>{2}, Remove a +1/+1 counter from Spike Feeder: You gain 2
///   life. (CR 602.)</b> Wired as an <see cref="ActivatedAbility"/>
///   with two costs:
///     - <see cref="ManaCostCost"/>("{2}") — generic mana payment.
///     - <see cref="RemovePlusOnePlusOneCounterCost"/>(self, 1) — the
///       first-class counter-pay primitive (same shape Walking
///       Ballista's ping ability uses).
///   The cost primitives' <c>CanPay</c> gates pre-activation legality
///   (CR 119.4 — can't pay a resource you don't have); their
///   <c>Pay</c> runs at activation time. The resolution effect calls
///   <see cref="Fx.GainLife"/>(controller, 2) per CR 119.3 (life
///   gain) — routed through <see cref="Player.GainLife"/> so the
///   life-changed event publishes and life-gain payoffs (Heliod
///   Sun-Crowned, Soul Sisters family) see the bump.
/// - <b>Instant speed</b>: printed activation timing is the default
///   instant-speed (CR 602.5b — no "activate only as a sorcery"
///   clause on Spike Feeder), so the activated ability is freely
///   activable at any priority window.
///
/// ## Deferred (v1 gaps)
/// - <b>Repeated activation</b>: each activation removes one counter
///   and gains two life. The Spike Feeder + Archangel of Thune "gain
///   infinite life" combo emerges naturally once Archangel ships
///   (her life-gain trigger places a counter on every creature
///   Spike Feeder included → restocks the cost). No special wiring
///   needed here.
/// </summary>
[CardName("Spike Feeder")]
public static class SpikeFeederFactory
{
    public const string CardName = "Spike Feeder";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int EntersWithCountersAmount = 2;
    public const string ActivationManaCost = "{2}";
    public const int LifeGainedPerActivation = 2;

    /// <summary>
    /// Construct Spike Feeder with no live replacement-bus wiring.
    /// The activated ability is attached for shape inspection;
    /// enters-with-counters is NOT registered (callers stamp via
    /// <see cref="MarkEntersWithCounters"/>). Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Spike Feeder with optional <see cref="ReplacementBus"/>
    /// wiring. When supplied, registers
    /// <see cref="EntersWithCountersReplacement"/>(this, 2) so the ETB
    /// pipeline applies the printed two +1/+1 counters automatically
    /// (CR 614.1d).
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spike });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Spike Feeder enters with two +1/+1 counters on it." (CR 614.1d.)
        // Registered as a replacement against the ZoneMoveIntent pipeline
        // so SBAs (CR 704.5f) see the 2/2 once the counters apply right
        // after the permanent lands. No replacement bus → callers stamp
        // the counters manually via MarkEntersWithCounters (Modular shape
        // fallback).
        // ----------------------------------------------------------------
        replacements?.Register<ZoneMoveIntent>(
            new EntersWithCountersReplacement(card, EntersWithCountersAmount));

        // ----------------------------------------------------------------
        // {2}, Remove a +1/+1 counter from Spike Feeder: You gain 2 life.
        // CR 602 — activated ability. Costs:
        //   - ManaCostCost("{2}") — generic mana payment.
        //   - RemovePlusOnePlusOneCounterCost(self, 1) — first-class
        //     counter-pay primitive (Walking Ballista's ping shape).
        // Resolution: controller gains 2 life via Fx.GainLife (CR 119.3,
        // life-gain event publishes for downstream payoffs).
        // ----------------------------------------------------------------
        var gainLifeEffect = new Effect(
            $"{CardName}: gain {LifeGainedPerActivation} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.GainLife(controller, LifeGainedPerActivation);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                new RemovePlusOnePlusOneCounterCost(card, 1),
            },
            effects: new IEffect[] { gainLifeEffect });

        card.AddAbility(activated);

        return card;
    }

    /// <summary>
    /// Shape-only fallback — stamps Spike Feeder's printed two +1/+1
    /// counters manually. Use when constructing without a
    /// <see cref="ReplacementBus"/> in tests that need the
    /// counter-removal cost to be payable.
    /// </summary>
    public static void MarkEntersWithCounters(Creature spikeFeeder)
    {
        ArgumentNullException.ThrowIfNull(spikeFeeder);
        spikeFeeder.Counters.Add(CounterType.PlusOnePlusOne, EntersWithCountersAmount);
    }
}
