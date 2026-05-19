using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 509.2 — defender chooses order of blockers; attacker assigns lethal
/// damage to each in order before moving on. CR 510.1c — attacker decides
/// how much to assign to each; tests cover the standard "lethal each then
/// stop / overflow" pattern that <see cref="CombatFlow"/> implements.
/// </summary>
public class MultiBlockerOrderingTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MultiBlockerOrderingTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task FourPower_VsTwoTwoBlockers_KillsBothInOrder()
    {
        var attacker = Make("Attacker", 4, 4, _alice);
        var b1 = Make("Blocker1", 2, 2, _bob);
        var b2 = Make("Blocker2", 2, 2, _bob);

        await RunCombat(attacker, new[] { b1, b2 });

        b1.Zone.Should().Be(ZoneType.Graveyard);
        b2.Zone.Should().Be(ZoneType.Graveyard);
        attacker.Zone.Should().Be(ZoneType.Graveyard); // took 2+2 damage back
    }

    [Fact]
    public async Task ThreePower_VsTwoTwoBlockers_KillsFirstOnly_RestIsLost()
    {
        var attacker = Make("Attacker", 3, 5, _alice);
        var b1 = Make("Blocker1", 2, 2, _bob);
        var b2 = Make("Blocker2", 2, 2, _bob);

        await RunCombat(attacker, new[] { b1, b2 });

        b1.Zone.Should().Be(ZoneType.Graveyard); // 2 dmg, lethal
        b2.Zone.Should().Be(ZoneType.Battlefield); // 0 dmg, only 1 left after b1
        b2.Damage.Should().Be(0);
        attacker.Zone.Should().Be(ZoneType.Battlefield); // 2+2=4 toughness, survives
    }

    [Fact]
    public async Task ThreePowerWithTrample_VsOneBlocker_OverflowsToPlayer()
    {
        var attacker = Make("Tramp", 3, 3, _alice, "Trample");
        var blocker = Make("Bear", 2, 2, _bob);

        await RunCombat(attacker, new[] { blocker });

        blocker.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(19); // 1 overflow
    }

    [Fact]
    public async Task DeathtouchOnePower_VsTwoBigBlockers_KillsBoth()
    {
        var attacker = Make("Snake", 2, 1, _alice, "Deathtouch");
        var b1 = Make("Giant1", 5, 5, _bob);
        var b2 = Make("Giant2", 5, 5, _bob);

        await RunCombat(attacker, new[] { b1, b2 });

        b1.Zone.Should().Be(ZoneType.Graveyard); // deathtouch 1 dmg
        b2.Zone.Should().Be(ZoneType.Graveyard); // deathtouch 1 dmg
        attacker.Zone.Should().Be(ZoneType.Graveyard); // 5 dmg back from b1 alone
    }

    private async Task RunCombat(Creature attacker, IReadOnlyList<Creature> blockers)
    {
        attacker.SetZone(ZoneType.Battlefield);
        attacker.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(attacker);
        foreach (var b in blockers)
        {
            b.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(b);
        }

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(attacker, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(new BlockPlan(
            blockers.Select(b => new Majik.Core.Players.Agents.BlockerDeclaration(b, attacker)).ToList()));

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { attacker }, blockers, ctx);
    }

    private static Creature Make(string name, int p, int t, Player owner, params string[] keywords)
    {
        var c = new Creature(name, "1", p, t) { Owner = owner, Controller = owner };
        foreach (var kw in keywords)
        {
            c.AddAbility(new KeywordAbility(kw, c, owner));
        }
        return c;
    }
}
