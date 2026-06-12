using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Xunit;

namespace Majik.Bot.Tests;

public class BlockCombatEvalLegalityTests
{
    [Fact]
    public void EnumeratePlans_EmitsOnlyLegalPairs()
    {
        var s = new BotTestScenario();
        var flyer = s.AddCreatureToBattlefield(s.Opponent, "Drake", 2, 2);
        flyer.AddAbility(new KeywordAbility("Flying"));
        var tapped = s.AddCreatureToBattlefield(s.Self, "Knight", 3, 3);
        tapped.Tap();
        var ground = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        var birdBlocker = s.AddCreatureToBattlefield(s.Self, "Bird", 1, 1);
        birdBlocker.AddAbility(new KeywordAbility("Flying"));

        var plans = BlockCombatEval.EnumeratePlans(
            new Creature[] { flyer },
            new Creature[] { tapped, ground, birdBlocker });

        plans.Should().NotBeEmpty();
        foreach (var plan in plans)
            foreach (var d in plan.Blockers)
                BlockLegality.CanBlock(d.Blocker, d.Attacker, out _).Should().BeTrue(
                    $"{d.Blocker.Name} cannot legally block {d.Attacker.Name}");
        // The legal flyer-on-flyer block must still be offered.
        plans.Should().Contain(p => p.Blockers.Any(d => ReferenceEquals(d.Blocker, birdBlocker)));
    }
}
