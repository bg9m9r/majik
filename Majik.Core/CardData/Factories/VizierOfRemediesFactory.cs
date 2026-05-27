using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vizier of Remedies (Amonkhet, {W}{W}).
///
/// Creature — Human Cleric 2/1. Oracle text:
///   "If a -1/-1 counter would be put on a creature you control,
///    prevent that. Instead put no counter on that creature."
///
/// ## Implementation
///
/// CR 614 replacement effect on counter-placement. Vizier intercepts
/// every <see cref="CounterAddIntent"/> targeting a creature its
/// controller controls and the intent's counter type is -1/-1; the
/// replacement rewrites <see cref="CounterAddIntent.Amount"/> to 0 so
/// <see cref="Majik.Core.Services.CountersService.Add"/> commits no
/// counters and returns 0.
///
/// ## Druid Combo
///
/// With Devoted Druid on the battlefield, Vizier of Remedies replaces
/// the cost-side -1/-1 counter from Devoted Druid's untap activated
/// ability with no counter at all (CR 614.1, official rulings).
/// Devoted Druid taps for {G}, pays "put a -1/-1 counter on it" cost
/// (replaced to zero) to untap, and the loop repeats arbitrarily —
/// infinite green mana. Pair with Walking Ballista to win on the spot.
///
/// ## Caller integration
///
/// Callers wanting their -1/-1 counter placement to honour Vizier of
/// Remedies must route through
/// <see cref="Majik.Core.Services.CountersService.Add"/> (or
/// <see cref="Majik.Core.Costs.AddCounterCost"/> wired with a
/// <see cref="ReplacementBus"/>) instead of mutating
/// <see cref="Permanent.Counters"/> directly. Devoted Druid is wired
/// to consult the bus when one is supplied. Other -1/-1 sources
/// (Hapatra triggers, Wither / Infect damage replacement, Persist
/// return) are NOT yet retrofitted — that retrofit is a follow-up.
///
/// ## Lifecycle
///
/// The replacement is gated on Vizier sitting on the battlefield via
/// the <c>Applies</c> check (<see cref="Permanent.Zone"/>).
/// Registration happens once at <see cref="Create"/> time when a
/// <see cref="ReplacementBus"/> is supplied; the gating predicate
/// short-circuits while Vizier is in any other zone, so blink /
/// bounce / destroy don't require explicit deregistration (same
/// posture as <see cref="HardenedScalesFactory"/>).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Replacement ordering prompt</b>: CR 616.1 — when Vizier
///   overlaps with another -1/-1 counter replacement (Solemnity, etc.)
///   the affected player chooses the order. The bus applies in
///   registration order today (same gap as every other replacement).
/// - <b>ZoneMoveIntent ETB -1/-1 counters</b>: not currently
///   intercepted. The engine's ETB-counter intent only tracks +1/+1
///   counters (<see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/>); a
///   future "ETB with -1/-1" intent surface would need to be threaded
///   here. Not currently a coverage gap — Modern's -1/-1 ETB cards
///   (Black Sun's Zenith resolution, Hapatra triggers, etc.) all use
///   direct counter placement rather than the ETB intent.
/// </summary>
[CardName("Vizier of Remedies")]
public static class VizierOfRemediesFactory
{
    public const string CardName = "Vizier of Remedies";
    public const string PrintedManaCost = "{W}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Vizier of Remedies with no replacement-bus wiring.
    /// Suitable for dispatcher / structural tests. The static
    /// replacement is NOT registered.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Vizier of Remedies with optional replacement-bus
    /// wiring. When <paramref name="replacements"/> is supplied, a
    /// <see cref="VizierOfRemediesReplacement"/> is registered so the
    /// printed "-1/-1 → no counter" replacement fires on every matching
    /// <see cref="CounterAddIntent"/> routed via
    /// <see cref="Majik.Core.Services.CountersService.Add"/>.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<CounterAddIntent>(new VizierOfRemediesReplacement(card));
        }

        return card;
    }
}

/// <summary>
/// CR 614 replacement: when a <see cref="CounterAddIntent"/> would
/// put one or more -1/-1 counters on a creature controlled by
/// Vizier of Remedies' controller, rewrite the amount to 0 so the
/// placement commits no counters ("instead put no counter on that
/// creature").
/// </summary>
public sealed class VizierOfRemediesReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Creature _source;

    public VizierOfRemediesReplacement(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — replacement is only active while Vizier is on
        // the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Type != CounterType.MinusOneMinusOne) return false;
        if (intent.Amount < 1) return false;
        if (intent.Target is not Creature) return false;
        return ReferenceEquals(intent.Target.Controller, _source.Controller);
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = 0 };
}
