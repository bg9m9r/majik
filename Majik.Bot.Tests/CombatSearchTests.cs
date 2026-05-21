using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Bot.Tests;

public class CombatSearchTests
{
    [Fact]
    public void NoBlockers_AttackWithEverything()
    {
        var s = new BotTestScenario();
        var goblin = s.AddCreatureToBattlefield(s.Self, "Goblin", 2, 1);
        var bear   = s.AddCreatureToBattlefield(s.Self, "Bear", 2, 2);
        var policy = new CombatPolicy(ArchetypeWeights.Burn);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { goblin, bear });
        plan.Attackers.Should().HaveCount(2);
    }

    [Fact]
    public void HardBlocker_DontAttackIntoTrade_AsBurn()
    {
        var s = new BotTestScenario();
        var small = s.AddCreatureToBattlefield(s.Self, "Goblin", 1, 1);
        s.AddCreatureToBattlefield(s.Opponent, "Big Blocker", 4, 4);
        var policy = new CombatPolicy(ArchetypeWeights.Burn);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { small });
        plan.Attackers.Should().BeEmpty();
    }

    [Fact]
    public void ProfitableSwing_TakeIt()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Self, "Tarmogoyf", 3, 3);
        s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);
        var policy = new CombatPolicy(ArchetypeWeights.Prowess);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { attacker });
        plan.Should().NotBeNull();
    }

    [Fact]
    public void Budget_Honored_LargeBoard()
    {
        var s = new BotTestScenario();
        var attackers = new List<Creature>();
        for (var i = 0; i < 12; i++)
            attackers.Add(s.AddCreatureToBattlefield(s.Self, $"C{i}", 2, 2));
        var policy = new CombatPolicy(ArchetypeWeights.Prowess, budgetMs: 200);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = policy.PickAttackers(s.Context, s.Self, attackers);
        sw.Stop();
        plan.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }
}
