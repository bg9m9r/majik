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
/// Tests for the declarative <c>explore_target</c> effect verb
/// (<see cref="ExploreTargetEffectDef"/>, CR 701.40) — "target creature you
/// control explores". Exercises the shared
/// <see cref="CardDefRuntime.BuildJsonEffect"/> build path against a chosen
/// target read off <see cref="ResolutionContext.ChosenTargets"/>, mirroring the
/// other targeted verbs (destroy_target / exile_target).
/// </summary>
public class ExploreTargetEffectDefTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly List<CreatureExploredEvent> _explored = new();

    public ExploreTargetEffectDefTests()
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

    private static readonly ExploreTargetEffectDef Def = new();

    [Fact]
    public void TargetRequest_IsCreatureYouControl_OneToOne()
    {
        var req = Def.ToTargetRequest();
        req.Should().NotBeNull();
        req!.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature you control");
    }

    private async Task ExploreTargetAsync(Creature host, Creature target, IPlayerAgent? agent = null)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            Def, card: host, controller: _alice, replacements: null, targetRequestIndex: 0);
        var ctx = ResolutionContext.For(
            _alice, agent, game: null,
            chosenTargets: new[] { new object[] { target } });
        await effect.ExecuteAsync(ctx);
    }

    [Fact]
    public async Task ExploreTarget_LandOnTop_GoesToHand_NoCounter()
    {
        var land = new Land("Forest");
        _alice.Zones.Library.AddCard(land);

        var target = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        await ExploreTargetAsync(host: target, target: target);

        _alice.Zones.Hand.GetCards().Should().Contain(land,
            "CR 701.40b — a revealed land goes to the controller's hand");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        _explored.Should().ContainSingle(
            "CR 701.40e — the explore event is published so payoffs fire");
    }

    [Fact]
    public async Task ExploreTarget_NonLandOnTop_CounterOnTarget_KeepOnTop()
    {
        var spell = new Creature("Big", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);

        var host = new Creature("Map source", "", 0, 0) { Owner = _alice, Controller = _alice };
        var target = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        await ExploreTargetAsync(host: host, target: target, agent: agent);

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter lands on the exploring (target) creature");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell);
    }

    [Fact]
    public async Task ExploreTarget_TargetOffBattlefield_Fizzles_NoExplore()
    {
        var spell = new Creature("Big", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var target = new Creature("Scout", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Graveyard); // not on the battlefield

        await ExploreTargetAsync(host: target, target: target);

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 608.2b — an illegal target at resolution fizzles the explore");
        _explored.Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(spell);
    }
}
