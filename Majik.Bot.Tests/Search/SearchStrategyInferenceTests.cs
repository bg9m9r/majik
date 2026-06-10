using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for <see cref="SearchStrategy"/>'s opponent-archetype INFERENCE wiring
/// (Task 6, final). When <see cref="BotConfig.InferOpponentArchetype"/> is true and
/// no explicit <see cref="BotConfig.OpponentArchetype"/> is set, the strategy reads
/// the opponent's PUBLIC cards from the live <see cref="GameContext"/>, infers a
/// belief over archetypes, allocates determinized worlds across the belief, and runs
/// belief-driven determinized search (<see cref="DeterminizedSearch.RunBelief"/>).
///
/// <para>
/// These tests pin three properties on the same lethal-swing board used by
/// <see cref="SearchStrategyDeterminizationTests"/>: inference still finds the lethal
/// swing and the returned attackers are LIVE objects; inference-off leaves the
/// perfect-info path unchanged; and a fixed seed makes the inference path reproducible.
/// </para>
/// </summary>
public class SearchStrategyInferenceTests
{
    /// <summary>
    /// Lethal-swing board: Alice has 2x 2/2 ready, Bob is at 3 life with no blockers,
    /// in Combat / DeclareAttackers. Swinging all-out is lethal regardless of which
    /// opponent hand any sampled world invents, so the all-out attack dominates under
    /// every belief allocation AND under perfect info. Mirrors the determinization
    /// suite's fixture so the inference path is exercised on a proven board.
    /// </summary>
    private static (GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
        BuildLethalSwingBoard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);
        var bears = new List<Creature>();
        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
            bears.Add(c);
        }
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = SearchTestCtx.AtCombat(alice, bob);
        return (ctx, alice, bears);
    }

    [Fact]
    public void PickAttackers_Inference_FindsLethalSwing_WithLiveAttackers()
    {
        var (ctx, self, eligible) = BuildLethalSwingBoard();
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts", InferOpponentArchetype: true));

        var plan = strat.PickAttackers(ctx, self, eligible);

        plan.Attackers.Should().NotBeEmpty(
            "belief-driven determinized search still finds the lethal swing");
        plan.Attackers.Select(a => a.Attacker).Should().OnlyContain(
            c => eligible.Contains(c),
            "the chosen attackers must be reference-equal to LIVE eligible creatures");
    }

    [Fact]
    public void PickAttackers_InferenceOff_PerfectInfo_Unchanged()
    {
        var (ctx, self, eligible) = BuildLethalSwingBoard();
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts")); // both off/null

        strat.PickAttackers(ctx, self, eligible).Attackers.Should().NotBeEmpty(
            "the perfect-info path is unchanged when inference is off");
    }

    [Fact]
    public void PickAttackers_Inference_IsReproducible()
    {
        // SAME live board both runs — InstanceIds are GUIDs minted at construction,
        // so two independent boards never share them; reuse one board (PickAttackers
        // only returns a plan, it doesn't mutate the live state) and pin that the
        // fixed seed yields the identical chosen-attacker set on a repeated call.
        var (ctx, self, eligible) = BuildLethalSwingBoard();
        var cfg = () => new BotConfig("Burn", Strategy: "mcts", RandomSeed: 5, InferOpponentArchetype: true);

        var a = new SearchStrategy(cfg()).PickAttackers(ctx, self, eligible)
            .Attackers.Select(x => x.Attacker.InstanceId).OrderBy(x => x).ToList();
        var b = new SearchStrategy(cfg()).PickAttackers(ctx, self, eligible)
            .Attackers.Select(x => x.Attacker.InstanceId).OrderBy(x => x).ToList();

        a.Should().Equal(b, "fixed seed + same board = same inference-driven choice");
    }
}
