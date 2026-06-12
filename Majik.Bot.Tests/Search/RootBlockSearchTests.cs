using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Root-level block search (Task 6): <see cref="SearchStrategy.PickBlockers"/>
/// runs determinized/perfect-info MCTS rooted at the defender's block decision
/// against the REAL declared attack, with the BlockCombatEval path as the
/// fallback (and the <c>RootBlockSearch=false</c> kill switch).
/// </summary>
public class RootBlockSearchTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Block-ask board: Alice's lethal 5/5 is ALREADY DECLARED (tapped); Bob
    /// (the defender, at 4 life) has one untapped 2/2. Any sane search chump
    /// blocks — taking the hit is lethal. Returns the live block-ask inputs
    /// exactly as the engine hands them to PickBlockers.
    /// </summary>
    private static (GameContext ctx, Player self, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible)
        BuildBlockBoard(string[]? oppRealHand = null)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 4);

        var attacker = new Creature("Juggernaut", "{4}", 5, 5);
        attacker.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.ClearSummoningSickness();
        attacker.Tap(); // declared live

        var blocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        blocker.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(blocker);
        blocker.ClearSummoningSickness();

        // Optional REAL hidden hand on the ATTACKER's side (the defender's
        // opponent) — the only per-game difference in the masking test.
        if (oppRealHand != null)
        {
            foreach (var n in oppRealHand)
                alice.Zones.Hand.AddCard(Build(n, alice));
        }

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest"); f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest"); g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        // The live engine asks for blocks while the current step is still
        // DeclareAttackers (the DeclareBlockers transition happens inside the
        // grantStepPriority callback after blocks are declared) with the
        // ATTACKER as both self and active player on the shared ctx.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.DeclareAttackers,
            stack: stack);

        return (ctx, bob, new[] { attacker }, new[] { blocker });
    }

    /// <summary>
    /// Iteration-capped config (wall-clock high so the deterministic iteration
    /// cap governs — the DeterminizationMaskingTests anti-flake pattern).
    /// </summary>
    private static BotConfig Config(
        string? opponentArchetype = null, bool? rootBlockSearch = null) =>
        new BotConfig(
            ArchetypeName: "Burn",
            Strategy: "mcts",
            RandomSeed: 7,
            MaxMctsIterations: 80,
            MaxMctsBudgetMs: 60_000,
            OpponentArchetype: opponentArchetype,
            RootBlockSearch: rootBlockSearch);

    private static List<string> BlockKey(Majik.Core.Players.Agents.BlockPlan plan) =>
        plan.Blockers
            .Select(b => $"{b.Blocker.Name}->{b.Attacker.Name}")
            .OrderBy(k => k)
            .ToList();

    [Fact]
    public void PickBlockers_SearchesBlocks_AgainstTheRealAttack()
    {
        var (ctx, self, attackers, eligible) = BuildBlockBoard();

        var plan = new SearchStrategy(Config())
            .PickBlockers(ctx, self, attackers, eligible);

        plan.Blockers.Should().NotBeEmpty(
            "letting the 5/5 through is lethal at 4 life — any sane search blocks");
        // The returned plan must reference the LIVE creatures (the engine
        // matches block declarations by reference), not sandbox clones.
        plan.Blockers.Should().OnlyContain(b =>
            ReferenceEquals(b.Blocker, eligible[0]) && ReferenceEquals(b.Attacker, attackers[0]));
    }

    [Fact]
    public void PickBlockers_FallsBack_WhenDisabled()
    {
        var (ctx, self, attackers, eligible) = BuildBlockBoard();

        var plan = new SearchStrategy(Config(rootBlockSearch: false))
            .PickBlockers(ctx, self, attackers, eligible);

        // Kill switch → the BlockCombatEval path, byte-for-byte.
        var candidates = BlockCombatEval.EnumeratePlans(attackers, eligible);
        var expected = BlockCombatEval.PickBest(
            candidates, attackers, self.LifeTotal, ArchetypeWeights.ForArchetype("Burn"));

        BlockKey(plan).Should().Equal(BlockKey(expected),
            "RootBlockSearch=false must reproduce today's BlockCombatEval decision exactly");
    }

    [Fact]
    public void PickBlockers_Masking_DeterminizedBlockDecisionIgnoresRealHiddenHand()
    {
        // Two boards identical in every PUBLIC respect, differing ONLY in the
        // attacker's real hidden hand. The determinized block search must
        // sample the opponent's hidden zones from the archetype decklist and
        // never read the real hand — identical BlockPlan on both boards.
        var (ctxA, selfA, attackersA, eligibleA) = BuildBlockBoard(
            new[] { "Lightning Bolt", "Lightning Bolt", "Goblin Guide" });
        var (ctxB, selfB, attackersB, eligibleB) = BuildBlockBoard(
            new[] { "Island", "Island", "Island" });

        var planA = new SearchStrategy(Config(opponentArchetype: "Burn"))
            .PickBlockers(ctxA, selfA, attackersA, eligibleA);
        var planB = new SearchStrategy(Config(opponentArchetype: "Burn"))
            .PickBlockers(ctxB, selfB, attackersB, eligibleB);

        BlockKey(planA).Should().Equal(BlockKey(planB),
            "the determinized block decision samples hidden zones from the decklist "
            + "and never reads the real hidden hand");
    }
}
