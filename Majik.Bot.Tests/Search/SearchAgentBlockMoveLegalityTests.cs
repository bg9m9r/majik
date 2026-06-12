using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class SearchAgentBlockMoveLegalityTests
{
    /// <summary>
    /// CR 509.1b — the in-tree block move enumeration must not offer MCTS
    /// any (blocker, attacker) pair the engine would reject: tapped blockers
    /// and ground blockers against a flyer are illegal; the flyer-on-flyer
    /// block is legal and must still be offered.
    /// </summary>
    [Fact]
    public void BuildBlockerMoves_EmitsOnlyLegalPairs()
    {
        var s = new BotTestScenario();
        var flyer = s.AddCreatureToBattlefield(s.Opponent, "Drake", 2, 2);
        flyer.AddAbility(new KeywordAbility("Flying"));
        var tapped = s.AddCreatureToBattlefield(s.Self, "Knight", 3, 3);
        tapped.Tap();
        var ground = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        var birdBlocker = s.AddCreatureToBattlefield(s.Self, "Bird", 1, 1);
        birdBlocker.AddAbility(new KeywordAbility("Flying"));

        var moves = SearchAgent.BuildBlockerMoves(
            s.Context,
            new Creature[] { flyer },
            new Creature[] { tapped, ground, birdBlocker });

        moves.Should().NotBeEmpty();
        foreach (var move in moves)
        {
            move.BlockPlan.Should().NotBeNull();
            foreach (var d in move.BlockPlan!.Blockers)
                BlockLegality.CanBlock(d.Blocker, d.Attacker, out _).Should().BeTrue(
                    $"{d.Blocker.Name} cannot legally block {d.Attacker.Name}");
        }
        // The legal flyer-on-flyer move must still be offered.
        moves.Should().Contain(m =>
            m.BlockPlan != null
            && m.BlockPlan.Blockers.Any(d => ReferenceEquals(d.Blocker, birdBlocker)));
    }
}
