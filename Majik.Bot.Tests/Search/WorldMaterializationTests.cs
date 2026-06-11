using System.Diagnostics;
using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Per-world materialized base (sampled-card fidelity): a determinized root is
/// materialized ONCE — cloned and resampled with REAL prod-built cards
/// (<see cref="DeckCardBuilder"/>) — cached on
/// <see cref="SimState.MaterializedWorldPlayers"/>, and every per-sim sandbox
/// clones FROM that base instead of shell-resampling per clone. The fidelity
/// payoff: a GUESSED (sampled) card is a fully-functional prod card that the
/// in-sim opponent can actually CAST, with real consequences for the searched
/// seat (the previously-impossible event).
/// </summary>
public sealed class WorldMaterializationTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    private static EngineSimulator NewSim() => new(ArchetypeWeights.Default);

    /// <summary>
    /// Two-seat board mirroring EngineSimulatorDeterminizationTests: searched
    /// seat with a known hand + library; opponent with distinctive ACTUAL cards
    /// (not in the Burn decklist) so a resample is recognisable.
    /// </summary>
    private static SimState BuildRoot(out Player self, out Player opp, int? worldSeed = null)
    {
        self = new Player("Self", 20);
        opp = new Player("Opp", 20);

        foreach (var n in new[] { "Mountain", "Lightning Bolt" })
            self.Zones.Hand.AddCard(Build(n, self));
        foreach (var n in new[] { "Mountain", "Mountain", "Goblin Guide", "Lightning Bolt" })
            self.Zones.GetZone(ZoneType.Library).AddCard(Build(n, self));

        foreach (var n in new[] { "Island", "Counterspell", "Llanowar Elves" })
            opp.Zones.Hand.AddCard(Build(n, opp));
        foreach (var n in new[] { "Island", "Island", "Counterspell" })
            opp.Zones.GetZone(ZoneType.Library).AddCard(Build(n, opp));

        var players = new[] { self, opp };
        var root = SimState.Capture(
            livePlayers: players,
            activePlayer: self,
            turnNumber: 2,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: self);

        return worldSeed is int seed
            ? root.WithDeterminization(BotDeckCatalog.Get("Burn"), seed)
            : root;
    }

    // ── Materialize-once + reuse ──────────────────────────────────────────────

    [Fact]
    public void DeterminizedRoot_MaterializesOnce_AndReuses()
    {
        var root = BuildRoot(out _, out _, worldSeed: 11);
        root.MaterializedWorldPlayers.Should().BeNull(
            "a fresh root has no materialized world base yet");

        var sim = NewSim();

        var first = sim.DebugSampledOpponentHand(root);
        root.MaterializedWorldPlayers.Should().NotBeNull(
            "the first determinized observation must materialize and cache the world base");
        var cachedRef = root.MaterializedWorldPlayers;

        var second = sim.DebugSampledOpponentHand(root);
        second.Should().Equal(first,
            "the second observation must read the SAME cached world base");
        ReferenceEquals(root.MaterializedWorldPlayers, cachedRef).Should().BeTrue(
            "the cache must be set once and reused, never re-materialized for the same root");
    }

    [Fact]
    public void PerfectInfoRoot_NeverMaterializes()
    {
        var root = BuildRoot(out _, out var opp); // WorldSeed null
        var actual = opp.Zones.Hand.GetCards().Select(c => c.Name).OrderBy(n => n).ToList();

        var sampled = NewSim().DebugSampledOpponentHand(root).OrderBy(n => n).ToList();

        sampled.Should().Equal(actual,
            "the perfect-info path keeps the opponent's ACTUAL hand, byte-identical to before");
        root.MaterializedWorldPlayers.Should().BeNull(
            "perfect-info roots must never pay for (or carry) a materialized world base");
    }

    [Fact]
    public void SameSeed_NewRoot_IdenticalWorldContent()
    {
        var rootA = BuildRoot(out _, out _, worldSeed: 11);
        var rootB = BuildRoot(out _, out _, worldSeed: 11);

        var a = NewSim().DebugSampledOpponentHand(rootA);
        var b = NewSim().DebugSampledOpponentHand(rootB);

        a.Should().Equal(b,
            "the world base is a pure function of (live state, seed): a re-created root "
            + "with the same seed re-materializes to IDENTICAL content");
        ReferenceEquals(rootA.MaterializedWorldPlayers, rootB.MaterializedWorldPlayers)
            .Should().BeFalse("each root instance carries its OWN materialization");
    }

    // ── THE fidelity assert ───────────────────────────────────────────────────

    /// <summary>
    /// A sampled (GUESSED) card is a real prod-built card: castable surface
    /// (runtime type + name + mana cost) AND actually castable in-sim — the
    /// opponent casts the sampled Lightning Bolt and the searched seat's cloned
    /// life drops by 3. Mirrors SandboxCastabilityTests' drive pattern, but the
    /// bolt was never in the real game: the sampler invented it.
    /// </summary>
    [Fact(Timeout = 60_000)]
    public async Task SampledCard_IsCastableInSim()
    {
        var alice = new Player("Alice", 20); // searched seat
        var bob = new Player("Bob", 20);     // opponent — will cast the GUESSED bolt

        // Bob: one wired untapped Mountain to pay {R}; a 1-card real hand whose
        // content is resampled away; library content likewise replaced.
        var mountain = (Land)NamedCardFactory.Create("Mountain", bob);
        mountain.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(mountain);
        bob.Zones.Hand.AddCard(Build("Island", bob));
        foreach (var _ in Enumerable.Range(0, 3))
            bob.Zones.GetZone(ZoneType.Library).AddCard(Build("Island", bob));

        foreach (var _ in Enumerable.Range(0, 20))
            alice.Zones.GetZone(ZoneType.Library).AddCard(Build("Forest", alice));

        // Bolt-only decklist → the 1-card sampled hand is GUARANTEED a bolt.
        var deck = Enumerable.Repeat("Lightning Bolt", 8).ToList();
        var root = SimState.Capture(
                new[] { alice, bob },
                activePlayer: bob,
                turnNumber: 3,
                phase: PhaseStateType.PreCombatMain,
                searchedSeat: alice)
            .WithDeterminization(deck, worldSeed: 5);

        // Materialize the world base (same path the search uses).
        var sampledHand = NewSim().DebugSampledOpponentHand(root);
        sampledHand.Should().OnlyContain(n => n == "Lightning Bolt",
            "the hidden pool is exactly 8 Bolts, so the sampled hand is all-Bolt");

        var world = root.MaterializedWorldPlayers!;
        var worldBob = world.First(p => p.Id == bob.Id);
        var worldAlice = world.First(p => p.Id == alice.Id);

        // Castability surface: the sampled card is a REAL Instant with the
        // right name + mana cost (the surface TurnDriver's cast-time
        // spell-definition resolver keys on).
        var sampledBolt = worldBob.Zones.Hand.GetCards().Single();
        sampledBolt.Should().BeAssignableTo<Instant>(
            "a prod-built Lightning Bolt is a runtime Instant");
        sampledBolt.Name.Should().Be("Lightning Bolt");
        sampledBolt.ManaCost.Should().Be("{R}");

        // The stronger event: drive a sim where BOB casts the sampled bolt.
        SearchAgent? captured = null;
        var bobId = bob.Id;
        var sandbox = SandboxGame.From(
            world,
            new GameRandom(42),
            p => p.Id == bobId
                ? (captured = new SearchAgent(p))
                : (IPlayerAgent)new DeterministicBotAgent(),
            cardRepo: Repo);

        await DriveTurnWithBoltCastAsync(sandbox, captured!, worldBob);

        sandbox.State.PlayerFor(worldAlice).LifeTotal.Should().Be(17,
            "the GUESSED Lightning Bolt must RESOLVE in-sim for 3 damage against the "
            + "searched seat — a sampled card killing for real was previously impossible");

        // The real game is untouched: Alice's live life and Bob's live hand.
        alice.LifeTotal.Should().Be(20);
        bob.Zones.Hand.GetCards().Single().Name.Should().Be("Island");
    }

    /// <summary>
    /// Drives the sandbox through the opponent's turn, supplying the
    /// "Cast:Lightning Bolt" move at the first priority window that offers it
    /// (once) and Pass / empty-attack / first-option for everything else.
    /// Same decision-pump shape as SandboxCastabilityTests.
    /// </summary>
    private static async Task DriveTurnWithBoltCastAsync(
        SandboxGame sandbox, SearchAgent agent, Player worldActive)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var nextDecision = agent.NextDecisionAsync();
        var run = sandbox.ResumeAsync(
            PhaseStateType.PreCombatMain,
            sandbox.State.PlayerFor(worldActive),
            turnNumber: 3,
            maxTurns: 3,
            ct: cts.Token);

        var castSupplied = false;
        while (true)
        {
            var winner = await Task.WhenAny(nextDecision, run);
            if (ReferenceEquals(winner, run)) break;

            var decision = await nextDecision;
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
            "the priority window must offer the sampled Cast:Lightning Bolt move");
    }

    // ── Perf guard ────────────────────────────────────────────────────────────

    /// <summary>
    /// One world materialization (clone + ~30 prod card builds) must stay cheap:
    /// it happens once per world, not per sim. Warm-up call first so the lazily
    /// loaded embedded repo / factory dispatch table are excluded from the
    /// measured run.
    /// </summary>
    [Fact]
    public void WorldMaterialization_PerfGuard_Under250ms()
    {
        // Warm-up: loads the embedded seed + factory dispatch off the clock.
        NewSim().DebugSampledOpponentHand(BuildRoot(out _, out _, worldSeed: 3));

        // ~30 hidden cards to build: opp hand 3 + opp library replaced from a
        // Burn decklist pool minus visible (decklist 60, pool dealt = 3 hand +
        // rest library). To keep the build count ~30, use a 30-card slice.
        var self = new Player("Self", 20);
        var opp = new Player("Opp", 20);
        foreach (var n in new[] { "Mountain", "Lightning Bolt" })
            self.Zones.Hand.AddCard(Build(n, self));
        foreach (var _ in Enumerable.Range(0, 10))
            self.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", self));
        foreach (var _ in Enumerable.Range(0, 3))
            opp.Zones.Hand.AddCard(Build("Island", opp));
        foreach (var _ in Enumerable.Range(0, 10))
            opp.Zones.GetZone(ZoneType.Library).AddCard(Build("Island", opp));

        var deck30 = BotDeckCatalog.Get("Burn").Take(30).ToList();
        var root = SimState.Capture(
                new[] { self, opp }, self, 2, PhaseStateType.PreCombatMain, searchedSeat: self)
            .WithDeterminization(deck30, worldSeed: 17);

        var sw = Stopwatch.StartNew();
        NewSim().DebugSampledOpponentHand(root);
        sw.Stop();

        root.MaterializedWorldPlayers.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(250,
            $"one world materialization (clone + ~30 prod builds) took {sw.ElapsedMilliseconds} ms "
            + "and must stay well under the per-world budget");
    }
}
