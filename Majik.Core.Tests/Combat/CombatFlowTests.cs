using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

public class CombatFlowTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CombatFlowTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task NoAttackers_DefenderTakesNoDamage()
    {
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(CombatPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: new DeterministicBotAgent(),
            attackers: Array.Empty<Creature>(), blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public async Task OneAttackerNoBlockers_DefenderLosesPowerLife_AttackerTapped()
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new Majik.Core.Players.Agents.AttackerDeclaration(bear, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(BlockPlan.None);

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { bear }, blockers: Array.Empty<Creature>(),
            ctx: NewContext());

        _bob.LifeTotal.Should().Be(18);
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public async Task TwoTwoVsTwoTwo_BlockedAttacker_BothCreaturesTakeLethalDamage()
    {
        var atk = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        var blk = (Creature)NamedCardFactory.Create("Grizzly Bears", _bob);
        atk.SetOwner(_alice); atk.SetController(_alice); atk.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(atk);
        blk.SetOwner(_bob); blk.SetController(_bob); blk.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blk);
        atk.HasSummoningSickness = false;
        var flow = new CombatFlow(_bus, _sba);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueAttackers(new CombatPlan(new[] { new Majik.Core.Players.Agents.AttackerDeclaration(atk, _bob) }));
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueBlockers(new BlockPlan(new[] { new Majik.Core.Players.Agents.BlockerDeclaration(blk, atk) }));

        await flow.RunCombatAsync(
            attacker: _alice, defender: _bob,
            attackerAgent: aliceAgent, defenderAgent: bobAgent,
            attackers: new[] { atk }, blockers: new[] { blk },
            ctx: NewContext());

        // Both 2/2 → both take 2 damage → both die via SBA → graveyard.
        _bob.LifeTotal.Should().Be(20);
        atk.Zone.Should().Be(ZoneType.Graveyard);
        blk.Zone.Should().Be(ZoneType.Graveyard);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());
}
