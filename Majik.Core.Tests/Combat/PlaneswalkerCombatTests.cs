using FluentAssertions;
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
using Creature = Majik.Core.Cards.Creature;
using Planeswalker = Majik.Core.Cards.Planeswalker;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 508.1a — attackers choose between defending player and a planeswalker
/// they control. Damage to a planeswalker removes loyalty counters (CR 120.3c).
/// </summary>
public class PlaneswalkerCombatTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PlaneswalkerCombatTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task AttackPlaneswalker_UnblockedAttacker_RemovesLoyalty()
    {
        var pw = new Planeswalker("Jace", "2UU", startingLoyalty: 4) { Owner = _bob, Controller = _bob };
        pw.Zone = ZoneType.Battlefield;
        _bob.Zones.Battlefield.AddCard(pw);

        var bear = NewCreature("Bear", 2, 2, _alice);
        bear.Zone = ZoneType.Battlefield;
        bear.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(bear);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, pw),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { bear }, Array.Empty<Creature>(), ctx);

        pw.Loyalty.Should().Be(2); // 4 - 2 from bear
        _bob.LifeTotal.Should().Be(20); // life unchanged
    }

    [Fact]
    public async Task AttackPlaneswalker_LethalDamage_PutsItInGraveyard()
    {
        var pw = new Planeswalker("Jace", "2UU", startingLoyalty: 3) { Owner = _bob, Controller = _bob };
        pw.Zone = ZoneType.Battlefield;
        _bob.Zones.Battlefield.AddCard(pw);

        var giant = NewCreature("Giant", 5, 5, _alice);
        giant.Zone = ZoneType.Battlefield;
        giant.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(giant);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(giant, pw),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { giant }, Array.Empty<Creature>(), ctx);

        pw.Loyalty.Should().Be(0);
        pw.Zone.Should().Be(ZoneType.Graveyard);
    }

    private static Creature NewCreature(string name, int p, int t, Player owner) =>
        new(name, "1", p, t) { Owner = owner, Controller = owner };
}
