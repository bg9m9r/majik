using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hardened Scales (Magic Origins, {G}).
///
/// Enchantment. Oracle text:
///   "If one or more +1/+1 counters would be put on a creature you
///    control, that many plus one +1/+1 counters are put on it instead."
///
/// ## Implementation
///
/// CR 614 replacement effect on counter-placement. Two intent pathways
/// are intercepted so Hardened Scales reads its printed text against
/// both the ETB-counter route and the explicit "place counters" route:
///
/// 1. <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> — ETB
///    "this permanent enters the battlefield with N +1/+1 counters"
///    replacements (e.g. Strangleroot Geist, Arcbound Ravager's modular
///    inheritance). When the entering card is a Creature whose
///    incoming controller matches Hardened Scales' controller and the
///    intent already carries one or more +1/+1 counters, the count is
///    bumped by 1. CR 616.1c — multiple Hardened Scales each fire once
///    per intent, so two copies bump by +2 total.
/// 2. <see cref="CounterAddIntent"/> — direct "add N +1/+1 counters
///    to this creature" placements routed through
///    <see cref="CountersService.Add"/>. The factory bumps the amount
///    by 1 when the target is a creature controlled by Hardened
///    Scales' controller AND the counter type is +1/+1.
///
/// ## Caller integration
///
/// Callers wanting their +1/+1 counter placement to honour Hardened
/// Scales must route through <see cref="CountersService.Add"/> instead
/// of mutating <see cref="Permanent.Counters"/> directly. The new
/// helper pushes a <see cref="CounterAddIntent"/> through the supplied
/// <see cref="ReplacementBus"/> first, then commits the final amount
/// to the target. Existing direct <c>Counters.Add</c> call sites
/// (Champion of the Parish, Arcbound Ravager's modular self-bump,
/// etc.) are NOT yet routed — that retrofit is a follow-up. The ETB
/// pathway is wired automatically because <see cref="ZoneService"/>
/// already pumps every ETB through the bus.
///
/// ## Lifecycle
///
/// The replacements are gated on Hardened Scales sitting on the
/// battlefield via the <c>Applies</c> check (<see cref="Permanent.Zone"/>).
/// Registration happens once at <see cref="Create"/> time when a
/// <see cref="ReplacementBus"/> is supplied; the gating predicate
/// short-circuits while the enchantment is in any other zone, so
/// blink / bounce / destroy don't require explicit deregistration.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Direct-add retrofit</b>: existing factories that call
///   <c>Counters.Add(CounterType.PlusOnePlusOne, n)</c> directly
///   (Champion of the Parish, Arcbound Ravager modular, Walking
///   Ballista activations, etc.) won't see Hardened Scales' bump
///   until they route through <see cref="CountersService.Add"/>.
///   Tracked as a separate cleanup PR.
/// - <b>Replacement ordering prompt</b>: CR 616.1 — when Hardened
///   Scales overlaps with another counter replacement (Doubling
///   Season, Branching Evolution, etc.) the affected player chooses
///   the order. The bus applies in registration order today (same
///   gap as every other replacement).
/// </summary>
[CardName("Hardened Scales")]
public static class HardenedScalesFactory
{
    public const string CardName = "Hardened Scales";
    public const string PrintedManaCost = "{G}";

    /// <summary>
    /// Constructs a Hardened Scales with card identity only — no
    /// replacement registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Constructs a Hardened Scales. When <paramref name="replacements"/>
    /// is supplied, two <see cref="HardenedScalesReplacement"/> instances
    /// (one per intent type) are registered so the printed "+1 counter"
    /// bump fires on every matching ETB-counter ZoneMoveIntent + every
    /// <see cref="CounterAddIntent"/> routed via
    /// <see cref="CountersService.Add"/>.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(new HardenedScalesEtbReplacement(card));
            replacements.Register<CounterAddIntent>(new HardenedScalesAddReplacement(card));
        }

        return card;
    }
}

/// <summary>
/// CR 614 replacement: when an ETB-counter intent would put one or
/// more +1/+1 counters on a creature entering under Hardened Scales'
/// controller, bump the count by 1. Reads
/// <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/>; only fires when
/// the count is already &gt;= 1 (the printed "one or more" floor).
/// </summary>
public sealed class HardenedScalesEtbReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly Enchantment _source;

    public HardenedScalesEtbReplacement(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.ToZone != ZoneType.Battlefield) return false;
        if (intent.PlusOneCountersOnEnter < 1) return false;
        if (intent.Card is not Creature) return false;

        // "creature you control" — when intent.Controller is null, fall
        // back to the entering card's current controller (e.g. cards
        // routed through ZoneService without an explicit controller
        // argument keep their existing controller).
        var incomingController = intent.Controller ?? intent.Card.Controller;
        return ReferenceEquals(incomingController, _source.Controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { PlusOneCountersOnEnter = intent.PlusOneCountersOnEnter + 1 };
}

/// <summary>
/// CR 614 replacement: when a direct +1/+1 counter placement intent
/// would put one or more +1/+1 counters on a creature controlled by
/// Hardened Scales' controller, bump the count by 1.
/// </summary>
public sealed class HardenedScalesAddReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Enchantment _source;

    public HardenedScalesAddReplacement(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Type != CounterType.PlusOnePlusOne) return false;
        if (intent.Amount < 1) return false;
        if (intent.Target is not Creature) return false;
        return ReferenceEquals(intent.Target.Controller, _source.Controller);
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = intent.Amount + 1 };
}
