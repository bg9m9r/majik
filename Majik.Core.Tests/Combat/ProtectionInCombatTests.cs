using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ProtectionInCombatTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task BlockedByProtectionFromRed_PreventsRedAttackerDamage()
    {
        var redAttacker = new Creature("Red Bear", "1R", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var whiteKnight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        whiteKnight.AddAbility(new ProtectionAbility("red"));

        var bus = new EventBus();
        var sba = new StateBasedActions(bus);
        var combat = new CombatFlow(bus, sba);

        var attackerAgent = new ScriptedAgent();
        attackerAgent.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(redAttacker, _bob),
        }));
        var defenderAgent = new ScriptedAgent();
        defenderAgent.QueueBlockers(new BlockPlan(new[]
        {
            new Majik.Core.Players.Agents.BlockerDeclaration(whiteKnight, redAttacker),
        }));

        await combat.RunCombatAsync(_alice, _bob, attackerAgent, defenderAgent,
            new[] { redAttacker }, new[] { whiteKnight },
            new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, null, new Majik.Core.Stack.Stack()));

        // Red attacker's damage prevented; white knight survives and deals back.
        whiteKnight.Damage.Should().Be(0);
        whiteKnight.Zone.Should().Be(ZoneType.Battlefield);
        redAttacker.Damage.Should().Be(2); // hit by knight's power
    }
}
