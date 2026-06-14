using System.Threading;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="CrawlingBarrensFactory"/> — a manland whose single
/// "{4}:" ability accumulates two +1/+1 counters and then conditionally
/// animates ("you may have it become a 0/0 Elemental creature").
/// </summary>
public class CrawlingBarrensTests : System.IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public CrawlingBarrensTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    private void SetAgentYesNo(bool yes)
    {
        var agent = new Mock<IPlayerAgent>();
        agent.Setup(a => a.ChooseYesNoAsync(
                It.IsAny<string>(), It.IsAny<BotIntent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(yes);
        AgentRegistry.Set(_alice, agent.Object);
    }

    [Fact]
    public void CrawlingBarrens_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasType(CardType.Artifact).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Crawling Barrens");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CrawlingBarrens()
    {
        var card = NamedCardFactory.Create("Crawling Barrens", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Crawling Barrens");
    }

    [Fact]
    public void CrawlingBarrens_TapForColorless()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();
        var produced = manaAbility.Activate();

        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CrawlingBarrens_HasOneActivatedAbility_AlongsideManaAbility()
    {
        var land = CrawlingBarrensFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .ToList();

        activated.Should().HaveCount(1,
            "the single {4} counter-accumulate conditional-animate ability");
        activated[0].TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_PlacesTwoCounters_ThenAnimatesElemental_OnYes()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        SetAgentYesNo(true);

        var ability = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        ability.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Land, "'It's still a land.' (CR 613.1c)");
        chars.Types.Should().NotContain(CardType.Artifact);
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        ((CreatureCharacteristics)chars).Power.Should().Be(2, "0/0 base + two +1/+1 counters");
        ((CreatureCharacteristics)chars).Toughness.Should().Be(2);
    }

    [Fact]
    public void Resolve_PlacesCounters_ButDoesNotAnimate_OnNo()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        SetAgentYesNo(false);

        var ability = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        ability.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "the counter step is mandatory");
        effects.Compute(land).Types.Should().NotContain(CardType.Creature,
            "the player declined the optional animate");
    }

    [Fact]
    public void Resolve_CountersAccumulate_AcrossActivations()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        SetAgentYesNo(true);

        var ability = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        ability.Resolve();
        ability.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4);
        ((CreatureCharacteristics)effects.Compute(land)).Power.Should().Be(4);
    }

    [Fact]
    public void EndOfTurn_ExpiresAnimate_ButCountersPersist()
    {
        var effects = new ContinuousEffectsService();
        var land = CrawlingBarrensFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        SetAgentYesNo(true);

        var ability = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        ability.Resolve();

        // CR 514.2 — cleanup lifts EOT-scoped effects. Counters are permanent
        // objects (CR 121.5) so they persist past cleanup.
        effects.ExpireEndOfTurn();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Types.Should().NotContain(CardType.Artifact);

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }
}
