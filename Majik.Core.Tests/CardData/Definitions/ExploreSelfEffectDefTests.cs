using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>explore_self</c> effect verb
/// (<see cref="ExploreSelfEffectDef"/>, CR 701.40) — "this creature explores"
/// (optionally Count times). Exercises the shared
/// <see cref="CardDefRuntime.BuildJsonEffect"/> build path with the SOURCE
/// permanent as the exploring permanent (no target), mirroring the C#
/// ETB-explore family (Seekers' Squire / Merfolk Branchwalker / Jadelight).
/// </summary>
public class ExploreSelfEffectDefTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly List<CreatureExploredEvent> _explored = new();

    public ExploreSelfEffectDefTests()
    {
        EventBusRegistry.Set(_alice, _bus);
        _bus.Subscribe<CreatureExploredEvent>(e => _explored.Add(e));
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        EventBusRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private async Task ExploreSelfAsync(Creature source, ExploreSelfEffectDef def, IPlayerAgent? agent = null)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: source, controller: _alice, replacements: null);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);
        await effect.ExecuteAsync(ctx);
    }

    private Creature Source()
    {
        var c = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public async Task ExploreSelf_LandOnTop_GoesToHand_NoCounter()
    {
        var land = new Land("Forest");
        _alice.Zones.Library.AddCard(land);

        var source = Source();
        await ExploreSelfAsync(source, new ExploreSelfEffectDef());

        _alice.Zones.Hand.GetCards().Should().Contain(land,
            "CR 701.40b — a revealed land goes to the controller's hand");
        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        _explored.Should().ContainSingle(
            "CR 701.40e — the explore event is published so payoffs fire");
    }

    [Fact]
    public async Task ExploreSelf_NonLandOnTop_CounterOnSource_KeepOnTop()
    {
        var spell = new Creature("Big", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);

        var source = Source();
        await ExploreSelfAsync(source, new ExploreSelfEffectDef(), agent);

        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter lands on the exploring (source) creature itself");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell);
    }

    [Fact]
    public async Task ExploreSelf_Count2_TwoNonLands_TwoCounters()
    {
        var top = new Creature("First", "{G}", 3, 3);
        var second = new Creature("Second", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(second);
        _alice.Zones.Library.AddCard(top);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        agent.QueueExploreKeepOnTop(true);

        var source = Source();
        await ExploreSelfAsync(source, new ExploreSelfEffectDef { Count = 2 }, agent);

        source.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "CR 701.40 — Count: 2 runs two sequential explores; two non-lands = two counters");
        _explored.Should().HaveCount(2, "one explore event per explore (CR 701.40e)");
    }
}
