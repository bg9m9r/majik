using FluentAssertions;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Formats.Commander;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class CommanderDamageInCombatTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 40);
    private readonly Player _bob = new("Bob", 40);

    public CommanderDamageInCombatTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task CommanderUnblocked_RecordsDamage_OnDefenderState()
    {
        var cmdr = new Creature("Krenko", "3R", 5, 5)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            HasSummoningSickness = false,
            IsCommander = true,
        };
        _alice.Zones.Battlefield.AddCard(cmdr);
        _bob.Commander = new CommanderState(_bob, cmdr); // dummy own commander not relevant

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        { new Majik.Core.Players.Agents.AttackerDeclaration(cmdr, _bob) }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { cmdr }, System.Array.Empty<Creature>(), ctx);

        _bob.Commander.CommanderDamageTaken[cmdr].Should().Be(5);
        _bob.LifeTotal.Should().Be(35);
    }

    [Fact]
    public async Task CommanderDealing21Total_PlayerLoses()
    {
        var cmdr = new Creature("Krenko", "3R", 21, 21)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            HasSummoningSickness = false,
            IsCommander = true,
        };
        _alice.Zones.Battlefield.AddCard(cmdr);
        _bob.Commander = new CommanderState(_bob, cmdr);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        { new Majik.Core.Players.Agents.AttackerDeclaration(cmdr, _bob) }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { cmdr }, System.Array.Empty<Creature>(), ctx);

        _bob.HasLost.Should().BeTrue();
    }
}
