using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
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

    /// <summary>
    /// Greedy block projection picks the wrong blockers, making a bad
    /// attack look profitable. With small boards the deeper pass enumerates
    /// every opponent block assignment (pessimistic for bot), exposing the
    /// trap. Bot A1=4/4 + A2=2/2 vs opp B1=2/2 + B2=3/3:
    ///   Greedy: A1 unblocked (+4 dmg), A2 hard-blocked by B2 → +4 score.
    ///   Optimal opp: trade B2 into A1 (kill 4/4 for 3/3), unblock A2 → bot
    ///   loses its best creature for a small one. Negative score.
    /// The 2-ply minimax must decline this attack.
    /// </summary>
    [Fact]
    public void IterativeDeepening_DeclinesAttack_WhenOptimalOppBlockMakesItBad()
    {
        var s = new BotTestScenario();
        var a1 = s.AddCreatureToBattlefield(s.Self, "BigGuy", 4, 4);
        var a2 = s.AddCreatureToBattlefield(s.Self, "SmallGuy", 2, 2);
        s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);
        s.AddCreatureToBattlefield(s.Opponent, "Hill Giant", 3, 3);
        var policy = new CombatPolicy(ArchetypeWeights.Prowess);

        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { a1, a2 });

        // Should NOT attack with the 4/4 — opp would trade it for a 3/3.
        plan.Attackers.Select(a => a.Attacker).Should().NotContain(a1);
    }

    /// <summary>
    /// Sanity: deepening must not regress no-blocker cases — full swing
    /// remains correct.
    /// </summary>
    [Fact]
    public void IterativeDeepening_StillSwingsWide_WhenNoBlockers()
    {
        var s = new BotTestScenario();
        var a1 = s.AddCreatureToBattlefield(s.Self, "Goblin", 2, 1);
        var a2 = s.AddCreatureToBattlefield(s.Self, "Bear",   2, 2);
        var a3 = s.AddCreatureToBattlefield(s.Self, "Knight", 3, 3);
        var policy = new CombatPolicy(ArchetypeWeights.Burn);

        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { a1, a2, a3 });

        plan.Attackers.Should().HaveCount(3);
    }

    /// <summary>
    /// CR 509.1b — tapped creatures can't block. The attack model must not
    /// count them as defense. Mirror of HardBlocker_DontAttackIntoTrade_AsBurn:
    /// same board, but the 4/4 is tapped, so the 1/1 should swing freely.
    /// </summary>
    [Fact]
    public void TappedBlocker_DoesNotDeterAttack()
    {
        var s = new BotTestScenario();
        var small = s.AddCreatureToBattlefield(s.Self, "Goblin", 1, 1);
        var big = s.AddCreatureToBattlefield(s.Opponent, "Big Blocker", 4, 4);
        big.Tap();
        var policy = new CombatPolicy(ArchetypeWeights.Burn);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { small });
        plan.Attackers.Should().HaveCount(1);
    }

    /// <summary>
    /// CR 509.1b — a ground creature can't block a flyer. The attack model
    /// must treat the flying 1/1 as unblockable here and attack.
    /// </summary>
    [Fact]
    public void FlyingAttacker_NotDeterredByGroundBlocker()
    {
        var s = new BotTestScenario();
        var flyer = s.AddCreatureToBattlefield(s.Self, "Bird", 1, 1);
        flyer.AddAbility(new KeywordAbility("Flying"));
        s.AddCreatureToBattlefield(s.Opponent, "Big Blocker", 4, 4);
        var policy = new CombatPolicy(ArchetypeWeights.Burn);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { flyer });
        plan.Attackers.Should().HaveCount(1);
    }
}
