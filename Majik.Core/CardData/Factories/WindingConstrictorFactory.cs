using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Winding Constrictor (Aether Revolt, {B}{G}).
///
/// Creature — Snake 2/2. Oracle text:
///   "If one or more counters would be put on an artifact or creature you
///    control, that many plus one of each of those kinds of counters are
///    put on that permanent instead.
///    If you would get one or more counters, you get that many plus one of
///    each of those kinds of counters instead."
///
/// ## Shape source
///
/// Card identity (name, {B}{G}, 2/2, Creature — Snake) is loaded from
/// <c>Majik.Core/CardData/Cards/winding-constrictor.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The replacement behaviour is wired in
/// code below.
///
/// ## Implementation
///
/// Winding Constrictor is the generalization of <see cref="HardenedScalesFactory"/>
/// (whose doc comment names this card): a CR 614 / CR 121.2 replacement on
/// counter placement. Where Hardened Scales is scoped to +1/+1 counters on
/// creatures, Winding Constrictor applies to <b>any</b> kind of counter
/// ("that many plus one of each of those kinds") on an <b>artifact or
/// creature</b> you control.
///
/// The implementation intercepts <see cref="CounterAddIntent"/> routed through
/// <see cref="Majik.Core.Services.CountersService.Add"/> and bumps the amount
/// by one whenever:
///   - Winding Constrictor is on the battlefield (the source's <c>Zone</c>);
///   - the intent already carries one or more counters (the printed "one or
///     more" floor — CR 614 only replaces an actual would-be placement);
///   - the target permanent is an artifact or creature (CR 205.3 type check);
///     and
///   - the target is controlled by Winding Constrictor's controller
///     ("you control").
///
/// Because <see cref="CounterAddIntent"/> carries the concrete
/// <see cref="Counters.CounterType"/>, the per-intent +1 is automatically
/// "of that kind" — a +1/+1 placement gets +1 more +1/+1, a charge placement
/// gets +1 more charge, etc. Multiple Constrictors / Hardened Scales each fire
/// once per intent (CR 616.1c), stacking additively.
///
/// ## Caller integration
///
/// Counter placement must route through
/// <see cref="Majik.Core.Services.CountersService.Add"/> for the bump to fire,
/// same contract as Hardened Scales / Doubling Season. The single-arg
/// <see cref="Create(Player)"/> overload registers no replacement (shape /
/// dispatch tests).
///
/// ## Deferred (v1 gap)
///
/// - <b>Player-counter rider</b>: the second clause — "If you would get one or
///   more counters, you get that many plus one of each of those kinds of
///   counters instead" — applies to counters the <i>player</i> gets (energy,
///   poison, experience, etc.). The engine has no player-counter-gain
///   replacement intent today: <see cref="Player.GainEnergy"/> /
///   <see cref="Player.AddPoisonCounters"/> mutate fields directly without a
///   <see cref="ReplacementBus"/> hop, so there is nothing to intercept. This
///   rider is therefore not wired in v1; the dominant, format-relevant clause
///   (counter synergy on your artifacts/creatures) is fully implemented above.
///   Tracked as an engine-infra follow-up (a <c>PlayerCounterAddIntent</c>
///   pumped through the bus by the player-counter mutators).
/// </summary>
[CardName("Winding Constrictor")]
public static class WindingConstrictorFactory
{
    public const string CardName = "Winding Constrictor";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("winding-constrictor");

    /// <summary>
    /// Constructs a Winding Constrictor with card identity only — no
    /// replacement registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Constructs a Winding Constrictor. When <paramref name="replacements"/>
    /// is supplied, a <see cref="WindingConstrictorAddReplacement"/> is
    /// registered so every matching <see cref="CounterAddIntent"/> routed via
    /// <see cref="Majik.Core.Services.CountersService.Add"/> gains the printed
    /// "+1 of that kind" bump.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        replacements?.Register<CounterAddIntent>(new WindingConstrictorAddReplacement(card));

        return card;
    }
}

/// <summary>
/// CR 614 / CR 121.2 replacement: when a counter-placement intent would put one
/// or more counters of any kind on an artifact or creature controlled by
/// Winding Constrictor's controller, bump the count by one of that kind.
/// </summary>
public sealed class WindingConstrictorAddReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Creature _source;

    public WindingConstrictorAddReplacement(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Amount < 1) return false;

        // "an artifact or creature you control" — CR 205.3 type check.
        var target = intent.Target;
        var isArtifactOrCreature =
            target.HasType(CardType.Artifact) || target.HasType(CardType.Creature);
        if (!isArtifactOrCreature) return false;

        return ReferenceEquals(target.Controller, _source.Controller);
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = intent.Amount + 1 };
}
