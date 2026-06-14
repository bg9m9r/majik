using System.Threading;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Behavioural verification of the Crawling Barrens activated-ability wiring in
/// <see cref="LandActivatedAbilityBinder"/> — the production path for Zendikar
/// Rising's Crawling Barrens.
///
/// Oracle text (exact, Scryfall):
///   "{T}: Add {C}.
///    {4}: Put two +1/+1 counters on this land. Then you may have it become a
///    0/0 Elemental creature until end of turn. It's still a land."
///
/// Lands are NEVER routed through their [CardName] factory in prod (the factory
/// instance-swap is gated on !shell.HasType(Land)), so the binder is the only
/// live path. This pays down the v1 deferral
/// crawling-barrens-counter-accumulate-conditional-animate: the single {4}
/// ability is a counter-accumulation step (put two +1/+1 counters on THIS land)
/// followed by a CONDITIONAL "you may have it become" animate. The accumulated
/// counters define the animated body's P/T (0/0 base + N counters).
/// </summary>
public class CrawlingBarrensBinderTests : System.IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EmbeddedCardRepository _repo = new();

    public CrawlingBarrensBinderTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    private Land MakeCrawlingBarrens(ContinuousEffectsService effects)
    {
        var entity = _repo.GetByName("Crawling Barrens");
        entity.Should().NotBeNull("Crawling Barrens should exist in the embedded pool");
        var parsed = TypeLineParser.Parse(entity!.TypeLine);
        var land = new Land("Crawling Barrens", parsed.Supertypes, parsed.Subtypes);
        land.SetOwner(_alice);
        land.SetController(_alice);
        land.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        LandActivatedAbilityBinder.Bind(land, entity, _alice, effects);
        return land;
    }

    private static ActivatedAbility CounterAnimateAbility(Land land)
        => land.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e =>
                e.Description.Contains("Elemental", System.StringComparison.OrdinalIgnoreCase)));

    private void SetAgentYesNo(bool yes)
    {
        var agent = new Mock<IPlayerAgent>();
        agent.Setup(a => a.ChooseYesNoAsync(
                It.IsAny<string>(), It.IsAny<BotIntent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(yes);
        AgentRegistry.Set(_alice, agent.Object);
    }

    // -----------------------------------------------------------------------
    // Binding shape — the {4} counter-then-conditional-animate ability binds.
    // -----------------------------------------------------------------------

    [Fact]
    public void CrawlingBarrens_BindsCounterAccumulateConditionalAnimate_NoTap()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeCrawlingBarrens(effects);

        var ability = CounterAnimateAbility(land);

        // {4} — a single ManaCostCost, no Tap cost (the {4} line has no {T}).
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<AdditionalCost>().Should().BeEmpty(
            "the {4} ability does not tap the land");
        ability.TargetRequests.Should().BeEmpty(
            "the animate targets THIS land, not a chosen target");
    }

    // -----------------------------------------------------------------------
    // Resolution — counters accumulate; conditional animate on "yes".
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PutsTwoCounters_ThenAnimatesToElementalZeroZeroBody_WhenAgentSaysYes()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeCrawlingBarrens(effects);
        SetAgentYesNo(true);

        var ability = CounterAnimateAbility(land);
        ability.Resolve();

        // Two +1/+1 counters placed on the land itself.
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Creature, "it became a creature");
        chars.Types.Should().Contain(CardType.Land, "it's still a land (CR 613.1c)");
        chars.Types.Should().NotContain(CardType.Artifact,
            "the body is a plain Elemental creature — not an artifact");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);

        // 0/0 base + two +1/+1 counters = 2/2.
        var cc = chars.Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(2, "0/0 base + two +1/+1 counters = 2/2");
        cc.Toughness.Should().Be(2);
    }

    [Fact]
    public void Resolve_CountersAccumulate_AcrossActivations()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeCrawlingBarrens(effects);
        SetAgentYesNo(true);

        var ability = CounterAnimateAbility(land);
        ability.Resolve();
        ability.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4);
        ((CreatureCharacteristics)effects.Compute(land)).Power.Should().Be(4,
            "0/0 base + four +1/+1 counters = 4/4");
    }

    [Fact]
    public void Resolve_PutsCounters_ButDoesNotAnimate_WhenAgentSaysNo()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeCrawlingBarrens(effects);
        SetAgentYesNo(false);

        var ability = CounterAnimateAbility(land);
        ability.Resolve();

        // The counter step is NOT a "may" — it always happens.
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        // The animate IS a "may" — declined, so the land stays a non-creature.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature,
            "the player declined the optional animate");
    }

    [Fact]
    public void EndOfTurn_LiftsAnimate_ButCountersPersist()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeCrawlingBarrens(effects);
        SetAgentYesNo(true);

        var ability = CounterAnimateAbility(land);
        ability.Resolve();

        effects.ExpireEndOfTurn(); // CR 514.2 cleanup

        var chars = effects.Compute(land);
        chars.Types.Should().NotContain(CardType.Creature, "the until-EOT animate lifted");
        chars.Types.Should().Contain(CardType.Land);

        // Counters are permanent objects (CR 121.5) — they survive cleanup.
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // [CardName] dispatch + IsImplemented.
    // -----------------------------------------------------------------------

    [Fact]
    public void CrawlingBarrens_IsImplemented_InEmbeddedPool()
    {
        var entity = _repo.GetByName("Crawling Barrens");
        entity!.IsImplemented.Should().BeTrue();
    }
}
