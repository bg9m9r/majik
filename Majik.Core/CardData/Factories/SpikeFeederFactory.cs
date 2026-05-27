using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spike Feeder (Urza's Saga, {1}{G}).
///
/// Creature — Spike 0/0. Oracle text:
///   "Spike Feeder enters with two +1/+1 counters on it.
///    {2}, Remove a +1/+1 counter from Spike Feeder: You gain 2 life."
///
/// ## Implemented (v1)
/// - 0/0 Spike with mana cost {1}{G}.
/// - <b>ETB +1/+1 counters (CR 614.1d / CR 122)</b>: registered against
///   the supplied <see cref="ReplacementBus"/> via
///   <see cref="EntersWithCountersReplacement"/> with N = 2 so the
///   <see cref="Services.ZoneService"/> ETB pipeline routes the counts
///   through <see cref="Services.CountersService.Add"/> on landing —
///   Hardened Scales / Doubling Season bumps apply (same plumbing the
///   Modular family uses). When no <see cref="ReplacementBus"/> is
///   supplied, callers can manually stamp the counters via
///   <see cref="MarkEntersWithCounters"/> (shape-only fallback matching
///   the Arcbound family posture).
/// - <b>Activated ability (CR 602.1 / CR 119.1)</b>: <c>{2}, Remove a
///   +1/+1 counter from Spike Feeder: You gain 2 life.</c> The cost is
///   the canonical <see cref="ManaCostCost"/> + <see cref="RemovePlusOnePlusOneCounterCost"/>
///   pair (same shape as Walking Ballista's "remove counter →
///   damage" ability). The effect calls <see cref="Player.GainLife"/>
///   directly, which publishes the <see cref="Events.LifeChangedEvent"/>
///   bus signal that Heliod, Sun-Crowned and the rest of the
///   lifegain-payoff family consume.
///
/// ## Heliod combo
/// Spike Feeder + Heliod, Sun-Crowned is the canonical Modern infinite-life
/// combo (with Heliod's devotion already met). Sequence:
///   1. Activate Spike Feeder: pay {2}, remove a +1/+1 counter, gain 2 life.
///   2. Heliod's lifegain trigger places a +1/+1 counter on Spike Feeder
///      (CR 119.3 / 603.6a) — net counter change is zero.
///   3. Loop.
/// Both halves of the combo are wired end-to-end through the event bus;
/// the loop is observable in tests that subscribe to
/// <see cref="Events.LifeChangedEvent"/> after each activation.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompt for "you gain 2 life"</b>: the gain-life effect is
///   self-targeting (the controller), so no
///   <see cref="Targeting.TargetRequest"/> is needed. No deferred surface
///   here.
/// - <b>Hardened Scales / Doubling Season interaction</b>: covered by the
///   shared <see cref="EntersWithCountersReplacement"/> + ETB pipeline —
///   pass a live <see cref="ReplacementBus"/> at construction.
/// </summary>
[CardName("Spike Feeder")]
public static class SpikeFeederFactory
{
    public const string CardName = "Spike Feeder";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int EntersWithCounters = 2;
    public const string ActivationManaCost = "{2}";
    public const int LifeGained = 2;

    /// <summary>
    /// Construct Spike Feeder with no live wiring. The ETB counter
    /// replacement is NOT registered (no bus supplied); callers can stamp
    /// the counters manually via <see cref="MarkEntersWithCounters"/>.
    /// The activated ability is fully attached and exercisable in unit
    /// tests once counters are stamped on the card.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Spike Feeder with an optional
    /// <see cref="ReplacementBus"/>. When supplied, the ETB +1/+1
    /// counter replacement is registered (CR 614.1d) so a routed
    /// <see cref="Services.ZoneService"/> ETB stamps the counters via
    /// <see cref="Services.CountersService.Add"/> (Hardened Scales /
    /// Doubling Season bumps apply).
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
        // ETB +1/+1 counters (CR 614.1d / CR 122).
        //   "Spike Feeder enters with two +1/+1 counters on it."
        // Registered against ReplacementBus when supplied so the
        // ZoneService ETB pipeline routes the count through
        // CountersService.Add (Hardened Scales bumps apply). When the
        // bus is null, the replacement is omitted — tests can stamp the
        // counters manually via MarkEntersWithCounters.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(card, EntersWithCounters));
        }

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "{2}, Remove a +1/+1 counter from Spike Feeder: You gain 2 life."
        // Cost = ManaCostCost("{2}") + RemovePlusOnePlusOneCounterCost(1).
        // Effect = controller gains 2 life via Player.GainLife (which
        // publishes LifeChangedEvent so Heliod's lifegain trigger and the
        // rest of the payoff family fire — CR 119.3 / 603.6a).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: gain {LifeGained} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGained);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                new RemovePlusOnePlusOneCounterCost(card, 1),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Shape-only fallback — manually stamps Spike Feeder's printed two
    /// +1/+1 counters on <paramref name="feeder"/>. Use this in tests
    /// that put Spike Feeder on the battlefield without routing through
    /// <see cref="Services.ZoneService"/> + <see cref="ReplacementBus"/>.
    /// Idempotent per call; invoke at ETB time exactly once.
    /// </summary>
    public static void MarkEntersWithCounters(Permanent feeder)
    {
        ArgumentNullException.ThrowIfNull(feeder);
        feeder.Counters.Add(CounterType.PlusOnePlusOne, EntersWithCounters);
    }
}
