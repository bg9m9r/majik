using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Sandbox spell-castability unlock: <see cref="SandboxGame.From"/> historically
/// built its <see cref="GameDriver"/> WITHOUT a spell-definition resolver, so
/// <c>TurnDriver.DispatchCast</c> hit the "no SpellDef for instant/sorcery"
/// branch for EVERY non-permanent spell — instants rotted in hand in every
/// simulation. These tests prove the new optional <c>cardRepo</c> parameter
/// wires the same resolver shape <see cref="Majik.Core.Api.GameFacade"/> wires
/// (via <see cref="SpellDefinitionResolverFactory"/>), making in-sim
/// instants/sorceries actually castable — and pin the resolver-off default
/// (no repo → old rotate-in-hand behaviour).
/// </summary>
public sealed class SandboxCastabilityTests
{
    /// <summary>Shared embedded repo — EmbeddedCardRepository loads its seed lazily.</summary>
    private static readonly EmbeddedCardRepository Repo = new();

    // ── Board builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Alice: real (repo-built) Lightning Bolt in hand + 1 untapped wired
    /// Mountain on the battlefield. Bob: empty board. Both libraries padded
    /// so the sandbox never draw-loses inside the test window.
    /// </summary>
    private static (Player alice, Player bob, ICard bolt) BuildBoltBoard(int bobLife = 20)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", bobLife);

        // NamedCardFactory wires the Mountain with a real ManaAbility the
        // sandbox can tap for {R} (same pattern as CastSearchTests).
        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        mountain.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(mountain);

        // A REAL cloned-path Lightning Bolt: built from the embedded card
        // repo exactly like GameFacade's deck build produces it. The spell
        // definition is NOT carried on the card — it is resolved by NAME at
        // cast time via the spell-definition resolver under test.
        var bolt = new ScryfallCardFactory(Repo).Create("Lightning Bolt", alice);
        alice.Zones.Hand.AddCard(bolt);

        foreach (var _ in Enumerable.Range(0, 20))
        {
            var al = new Land("Forest");
            al.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(al);

            var bl = new Land("Forest");
            bl.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(bl);
        }

        return (alice, bob, bolt);
    }

    // ── Drive harness (mirrors EngineSimulator's Advance loop) ────────────────

    /// <summary>
    /// Drives the sandbox from Alice's pre-combat main on turn 3 to the end of
    /// turn 3, supplying the "Cast:Lightning Bolt" move at the first priority
    /// window that offers it (once), and Pass / empty-attack / first-block for
    /// everything else. Same decision-pump shape as SearchAgentTests /
    /// EngineSimulator.AdvanceCoreUnsafe.
    /// </summary>
    private static async Task DriveTurnWithBoltCastAsync(
        SandboxGame sandbox, SearchAgent agent, Player liveAlice)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Capture the first decision TCS BEFORE starting the engine (the
        // concurrency contract — see SearchAgentTests for the full rationale).
        var nextDecision = agent.NextDecisionAsync();

        var run = sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(liveAlice),
            turnNumber: 3,
            maxTurns: 3, // current turn only — mirrors Rollout's depthTurns: 0
            ct: cts.Token);

        var castSupplied = false;
        while (true)
        {
            var winner = await Task.WhenAny(nextDecision, run);
            if (ReferenceEquals(winner, run)) break;

            var decision = await nextDecision;
            // Capture the NEXT decision TCS BEFORE supplying the move.
            nextDecision = agent.NextDecisionAsync();

            SimMove move;
            if (decision.Kind == SimDecisionKind.Priority)
            {
                var cast = decision.LegalMoves
                    .FirstOrDefault(m => m.Key == "Cast:Lightning Bolt");
                if (!castSupplied && cast != null)
                {
                    castSupplied = true;
                    move = cast;
                }
                else
                {
                    move = decision.LegalMoves.First(m => m.IsPass);
                }
            }
            else if (decision.Kind == SimDecisionKind.DeclareAttackers)
            {
                move = decision.LegalMoves.First(m => m.IsEmptyAttack);
            }
            else
            {
                move = decision.LegalMoves[0];
            }

            agent.SupplyMove(move);
        }

        await run;
        castSupplied.Should().BeTrue(
            "the priority window must offer the Cast:Lightning Bolt move");
    }

    // ── THE unlock proof ──────────────────────────────────────────────────────

    /// <summary>
    /// With a card repo wired into <see cref="SandboxGame.From"/>, an in-sim
    /// Lightning Bolt cast RESOLVES: the opponent's cloned life drops by 3 and
    /// the bolt ends in the graveyard. Previously impossible — the sandbox had
    /// no spell-definition resolver, so the bolt rotated in hand.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task Sandbox_WithCardRepo_BoltResolvesInSim_OpponentLifeDropsBy3()
    {
        var (alice, bob, _) = BuildBoltBoard();

        SearchAgent? captured = null;
        var aliceId = alice.Id;
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(42),
            p => p.Id == aliceId
                ? (captured = new SearchAgent(p))
                : (IPlayerAgent)new DeterministicBotAgent(),
            cardRepo: Repo);

        await DriveTurnWithBoltCastAsync(sandbox, captured!, alice);

        var clonedBob = sandbox.State.PlayerFor(bob);
        clonedBob.LifeTotal.Should().Be(17,
            "the in-sim Lightning Bolt must RESOLVE for 3 damage now that the " +
            "sandbox carries a spell-definition resolver");

        var clonedAlice = sandbox.State.PlayerFor(alice);
        clonedAlice.Zones.Graveyard.GetCards()
            .Should().Contain(c => c.Name == "Lightning Bolt",
                "a resolved instant goes to its owner's graveyard");

        // And the LIVE objects are untouched — the sandbox never leaks.
        bob.LifeTotal.Should().Be(20);
        alice.Zones.Hand.GetCards().Should().Contain(c => c.Name == "Lightning Bolt");
    }

    // ── Resolver-off back-compat ──────────────────────────────────────────────

    /// <summary>
    /// Pins the default: <see cref="SandboxGame.From"/> WITHOUT a
    /// <c>cardRepo</c> keeps today's behaviour — the same drive leaves the
    /// opponent's life unchanged and the bolt rotting in hand (DispatchCast's
    /// "no SpellDef for instant/sorcery" rotate branch).
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task Sandbox_WithoutCardRepo_BoltCastNoOps_LifeUnchanged()
    {
        var (alice, bob, _) = BuildBoltBoard();

        SearchAgent? captured = null;
        var aliceId = alice.Id;
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(42),
            p => p.Id == aliceId
                ? (captured = new SearchAgent(p))
                : (IPlayerAgent)new DeterministicBotAgent());

        await DriveTurnWithBoltCastAsync(sandbox, captured!, alice);

        var clonedBob = sandbox.State.PlayerFor(bob);
        clonedBob.LifeTotal.Should().Be(20,
            "without a resolver the cast no-ops (rotate-in-hand) — the pre-existing default");

        var clonedAlice = sandbox.State.PlayerFor(alice);
        clonedAlice.Zones.Hand.GetCards()
            .Should().Contain(c => c.Name == "Lightning Bolt",
                "the bolt stays in hand when no spell definition resolves");
    }

    // ── EngineSimulator wiring ────────────────────────────────────────────────

    /// <summary>
    /// The search harness itself now threads the shared embedded repo into its
    /// sandboxes: a scripted Cast:Lightning Bolt rollout against a 3-life
    /// opponent is LETHAL (terminal value = +1000 WinValue). Before the fix the
    /// bolt rotated in hand, the turn ended with both players alive, and the
    /// rollout returned a small BoardEval leaf score instead.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void EngineSimulator_ScriptedBoltRollout_IsLethal_AgainstThreeLifeOpponent()
    {
        var (alice, bob, _) = BuildBoltBoard(bobLife: 3); // one bolt is exactly lethal

        var root = SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: alice);
        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        // Get the legal Cast move from the search harness itself.
        var decision = sim.Advance(root, Array.Empty<SimMove>());
        decision.IsTerminal.Should().BeFalse();
        decision.Kind.Should().Be(SimDecisionKind.Priority);
        var castMove = decision.LegalMoves
            .FirstOrDefault(m => m.Key == "Cast:Lightning Bolt");
        castMove.Should().NotBeNull("the enumerator must offer the affordable bolt");

        var value = sim.Rollout(root, new[] { castMove! }, depthTurns: 0);
        value.Should().Be(1_000.0,
            "casting the bolt at a 3-life opponent must win the simulated game");
    }
}
