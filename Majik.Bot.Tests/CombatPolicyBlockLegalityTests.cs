using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Bot.Tests;

public class CombatPolicyBlockLegalityTests
{
    [Fact]
    public void PickBlockers_IgnoresTappedBlocker()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);
        var wall = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        wall.Tap();
        var policy = new CombatPolicy(ArchetypeWeights.Default);
        var plan = policy.PickBlockers(s.Context, s.Self,
            new Creature[] { attacker }, new Creature[] { wall });
        plan.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void PickBlockers_GroundBlockerCannotBlockFlyer()
    {
        var s = new BotTestScenario();
        var flyer = s.AddCreatureToBattlefield(s.Opponent, "Drake", 2, 2);
        flyer.AddAbility(new KeywordAbility("Flying"));
        var wall = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        var policy = new CombatPolicy(ArchetypeWeights.Default);
        var plan = policy.PickBlockers(s.Context, s.Self,
            new Creature[] { flyer }, new Creature[] { wall });
        plan.Blockers.Should().BeEmpty();
    }
}
