using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Branching Evolution (Jumpstart, {2}{G}).
///
/// Enchantment. Oracle text:
///   "If one or more +1/+1 counters would be put on a creature you control,
///    twice that many +1/+1 counters are put on that creature instead."
///
/// ## Shape source
///
/// Card identity (name, {2}{G}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/branching-evolution.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The replacement behaviour is wired in
/// code below.
///
/// ## Implementation
///
/// CR 614 / CR 121.2 replacement on counter placement — the +1/+1-only,
/// creature-only "doubler" sibling of <see cref="DoublingSeasonFactory"/>'s
/// counter half. Where Doubling Season doubles counters of <i>any</i> kind on
/// any permanent you control, Branching Evolution is scoped to:
///   - +1/+1 counters specifically (CR 122 / the printed "+1/+1 counters");
///   - a <b>creature</b> you control (CR 205.3 type check); and
///   - "twice that many" — the amount is multiplied by two, not bumped by one
///     (contrast <see cref="HardenedScalesFactory"/>, which adds one).
///
/// The implementation intercepts <see cref="CounterAddIntent"/> routed through
/// <see cref="Majik.Core.Services.CountersService.Add"/> and replaces the
/// amount with <c>Amount * 2</c> whenever:
///   - Branching Evolution is on the battlefield (the source's <c>Zone</c>);
///   - the intent already carries one or more counters (the printed "one or
///     more" floor — CR 614 only replaces an actual would-be placement);
///   - the counter kind is <see cref="CounterType.PlusOnePlusOne"/>; and
///   - the target is a creature controlled by Branching Evolution's controller
///     ("a creature you control").
///
/// Multiple doublers / Hardened Scales each fire once per intent (CR 616.1c).
/// When Branching Evolution overlaps another counter replacement (Doubling
/// Season, Hardened Scales, Vorinclex) the affected player chooses the order
/// (CR 616.1); the bus applies in registration order today — the same
/// affected-player ordering prompt gap noted on Doubling Season / Hardened
/// Scales, deferred to v2.
///
/// ## Caller integration
///
/// Counter placement must route through
/// <see cref="Majik.Core.Services.CountersService.Add"/> for the doubling to
/// fire, same contract as Hardened Scales / Doubling Season / Winding
/// Constrictor. The single-arg <see cref="Create(Player)"/> overload registers
/// no replacement (shape / dispatch tests).
/// </summary>
[CardName("Branching Evolution")]
public static class BranchingEvolutionFactory
{
    public const string CardName = "Branching Evolution";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("branching-evolution");

    /// <summary>
    /// Constructs a Branching Evolution with card identity only — no
    /// replacement registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Constructs a Branching Evolution. When <paramref name="replacements"/>
    /// is supplied, a <see cref="BranchingEvolutionReplacement"/> is registered
    /// so every matching <see cref="CounterAddIntent"/> routed via
    /// <see cref="Majik.Core.Services.CountersService.Add"/> doubles the placed
    /// +1/+1 counters.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        replacements?.Register<CounterAddIntent>(new BranchingEvolutionReplacement(card));

        return card;
    }
}

/// <summary>
/// CR 614 / CR 121.2 replacement: when a counter-placement intent would put one
/// or more +1/+1 counters on a creature controlled by Branching Evolution's
/// controller, double the amount ("twice that many").
/// </summary>
public sealed class BranchingEvolutionReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Enchantment _source;

    public BranchingEvolutionReplacement(Enchantment source)
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
        intent with { Amount = intent.Amount * 2 };
}
