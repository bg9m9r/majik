using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Game;

/// <summary>
/// PLAN 08 prerequisite — proves the logical-clock swap makes the engine's
/// ORDER-DETERMINING timestamps reproducible (and behaviour-preserving) so a
/// game's DECISIONS replay identically given (seed, command order). These
/// tests target the four ordering paths the fix touches: trigger APNAP order,
/// continuous-effect layer order, legend-rule / planeswalker ETB order, and
/// the relative ordering of game events consumed by delayed-trigger fences.
/// </summary>
public class LogicalClockDeterminismTests
{
    // ── LogicalClock primitive ─────────────────────────────────────────

    [Fact]
    public void LogicalClock_NextOrder_IsStrictlyIncreasing()
    {
        var clock = new LogicalClock();
        var a = clock.NextOrder();
        var b = clock.NextOrder();
        var c = clock.NextOrder();
        b.Should().BeGreaterThan(a);
        c.Should().BeGreaterThan(b);
    }

    [Fact]
    public void LogicalClock_NextTimestamp_IsStrictlyIncreasing_AndUtc()
    {
        var clock = new LogicalClock();
        var t1 = clock.NextTimestamp();
        var t2 = clock.NextTimestamp();
        t2.Should().BeAfter(t1);
        t1.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void LogicalClock_TwoClocksSameConstructionOrder_ProduceIdenticalSequences()
    {
        var a = new LogicalClock();
        var b = new LogicalClock();
        var seqA = new[] { a.NextTimestamp(), a.NextTimestamp(), a.NextTimestamp() };
        var seqB = new[] { b.NextTimestamp(), b.NextTimestamp(), b.NextTimestamp() };
        seqB.Should().Equal(seqA);
    }

    [Fact]
    public void LogicalClockScope_Push_InstallsAmbientClock_AndRestoresOnDispose()
    {
        var outer = LogicalClockScope.Current.NextTimestamp();
        var custom = new LogicalClock();
        using (LogicalClockScope.Push(custom))
        {
            // Under the pushed (fresh) clock, the first read is the pushed
            // clock's FIRST value — independent of how far the fallback has
            // advanced. This is the per-game isolation that makes replay work.
            LogicalClockScope.Current.NextTimestamp()
                .Should().Be(LogicalClock.Epoch.AddTicks(1));
            // And it's the SAME object we pushed.
            LogicalClockScope.Current.Should().BeSameAs(custom);
        }
        // After dispose the fallback clock is active again and keeps moving.
        LogicalClockScope.Current.NextTimestamp().Should().BeAfter(outer);
    }

    // ── Trigger APNAP order (Rule 603.3b) ──────────────────────────────

    [Fact]
    public void TriggeredAbilityTimestamps_AssignedInConstructionOrder_UnderLogicalClock()
    {
        var alice = new Player("Alice", 20);
        using var _ = LogicalClockScope.Push(new LogicalClock());

        var first = MakeTrigger(alice);
        var second = MakeTrigger(alice);
        var third = MakeTrigger(alice);

        // Construction order == timestamp order (the invariant UtcNow used to
        // approximate, now exact + reproducible).
        second.Timestamp.Should().BeAfter(first.Timestamp);
        third.Timestamp.Should().BeAfter(second.Timestamp);

        var ordered = ApnapOrdering.Order(
            new ITriggeredAbility[] { third, first, second }, alice);
        ordered.Should().Equal(first, second, third);
    }

    [Fact]
    public void TriggerOrdering_IsIdenticalAcrossTwoRunsWithFreshClock()
    {
        static IReadOnlyList<string> Run()
        {
            using var _ = LogicalClockScope.Push(new LogicalClock());
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);
            // Interleave construction across controllers; APNAP must place the
            // active player's (alice) triggers first, sub-ordered by the
            // logical clock, identically every run.
            var a1 = MakeTrigger(alice);
            var b1 = MakeTrigger(bob);
            var a2 = MakeTrigger(alice);
            var b2 = MakeTrigger(bob);
            var ordered = ApnapOrdering.Order(
                new ITriggeredAbility[] { b2, a2, b1, a1 }, alice);
            return ordered.Select(t => ReferenceEquals(t.Controller, alice) ? "A" : "B")
                          .ToList();
        }

        var run1 = Run();
        var run2 = Run();
        run1.Should().Equal("A", "A", "B", "B");
        run2.Should().Equal(run1);
    }

    // ── Continuous-effect layer order (Rule 613.7) ─────────────────────

    [Fact]
    public void ControlChangeEffects_LatestTimestampWins_DeterministicAcrossRuns()
    {
        static string Run()
        {
            using var _ = LogicalClockScope.Push(new LogicalClock());
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);
            var carol = new Player("Carol", 20);
            var svc = new ContinuousEffectsService();
            var bear = new Creature("Bear", "1G", 2, 2)
            { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

            // Two control-change effects on the same permanent. CR 613.2 —
            // latest timestamp wins. Construction order decides; with the
            // logical clock that's reproducible. Compare by NAME (player Id is
            // the deferred-nondeterministic Guid we deliberately ignore).
            svc.Register(new ControlChangeEffect(bear, bob));
            svc.Register(new ControlChangeEffect(bear, carol));
            return svc.EffectiveController(bear).Name;
        }

        var run1 = Run();
        var run2 = Run();
        // The later-registered effect (carol) wins every run.
        run1.Should().Be("Carol");
        run2.Should().Be(run1);
    }

    // ── Legend rule ETB order (Rule 704.5k) ────────────────────────────

    [Fact]
    public void LegendRule_SurvivorIsDeterministic_ByLogicalClockEtbOrder()
    {
        static string Run()
        {
            using var _ = LogicalClockScope.Push(new LogicalClock());
            var eventBus = new Mock<IEventBus>().Object;
            var zones = new ZoneService(eventBus);
            var sba = new StateBasedActions(eventBus, zones);
            var alice = new Player("Alice", 20);

            var first = new Creature("Emrakul, the Promised End", "13", 13, 13,
                supertypes: new[] { CardSupertype.Legendary })
            { Owner = alice, Controller = alice };
            var second = new Creature("Emrakul, the Promised End", "13", 13, 13,
                supertypes: new[] { CardSupertype.Legendary })
            { Owner = alice, Controller = alice };

            first.SetZone(ZoneType.Battlefield);
            second.SetZone(ZoneType.Battlefield);
            // ETB order: first enters before second → first has the earlier
            // logical-clock timestamp → the LATER one (second) is kept (skip(1)
            // keeps the newest in the sort)... LegendRuleCheck sorts ascending
            // and sacrifices everything past index 0, so the EARLIEST survives.
            zones.MoveCardTo(first, ZoneType.Battlefield, alice);
            zones.MoveCardTo(second, ZoneType.Battlefield, alice);

            sba.CheckStateBasedActions(
                new[] { alice },
                new ICard[] { first, second });

            // Exactly one survives; identify it by reference-stable owner +
            // zone. Encode which object survived as "first"/"second".
            if (first.Zone == ZoneType.Battlefield && second.Zone != ZoneType.Battlefield)
                return "first";
            if (second.Zone == ZoneType.Battlefield && first.Zone != ZoneType.Battlefield)
                return "second";
            return $"unexpected:{first.Zone}/{second.Zone}";
        }

        var run1 = Run();
        var run2 = Run();
        run1.Should().Be("first"); // earliest ETB survives, deterministically
        run2.Should().Be(run1);
    }

    // ── Event ordering / delayed-trigger fences ────────────────────────

    [Fact]
    public void GameEventTimestamps_AreMonotonicPerGame_SoFencesStayConsistent()
    {
        using var _ = LogicalClockScope.Push(new LogicalClock());
        var alice = new Player("Alice", 20);
        // A "resolvedAt" fence captured from the clock, then a later event.
        var resolvedAt = LogicalClockScope.Current.NextTimestamp();
        var laterEvent = new StepStartedEvent(PhaseStateType.End, alice);
        // The event constructed after the fence has a strictly-greater
        // timestamp, so "e.Timestamp > resolvedAt" fences fire exactly once,
        // reproducibly — the property the ~25 factory delayed triggers rely on.
        laterEvent.Timestamp.Should().BeAfter(resolvedAt);
    }

    // ── Full structural replay (executable definition of done) ─────────

    [Fact]
    public void SameSeedSameSequence_ProducesStructurallyIdenticalGameState()
    {
        var snapshot1 = RunScriptedScenario(seed: 42);
        var snapshot2 = RunScriptedScenario(seed: 42);

        // Structural equivalence: we deliberately do NOT compare the still-
        // nondeterministic instance ids (deferred id-reseeding). We compare
        // zones by card name + order, life totals, computed P/T, the legend
        // survivor, the APNAP trigger order, and the shuffled library order.
        snapshot2.Should().BeEquivalentTo(snapshot1, opts => opts.WithStrictOrdering());
    }

    /// <summary>
    /// A scripted game-ordering scenario exercising every load-bearing path:
    /// a seeded shuffle (RNG), combat life-loss, multiple SIMULTANEOUS triggers
    /// (APNAP ordered), a legend-rule conflict (ETB order), and a layer effect
    /// (P/T modification). Returns a structural snapshot. Run with the same
    /// seed twice → must be identical.
    /// </summary>
    private static ScenarioSnapshot RunScriptedScenario(int seed)
    {
        // Pin BOTH sources of nondeterminism: the RNG (seed) and a fresh
        // logical clock installed as ambient for the whole scenario.
        var rng = new GameRandom(seed);
        using var _ = LogicalClockScope.Push(new LogicalClock());

        var eventBus = new Mock<IEventBus>().Object;
        var zones = new ZoneService(eventBus);
        var sba = new StateBasedActions(eventBus, zones);
        var effects = new ContinuousEffectsService();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // (1) Seeded shuffle — proves RNG determinism feeds zone order.
        var library = new List<string>();
        for (var i = 0; i < 12; i++) library.Add($"Card{i:00}");
        rng.Shuffle(library);

        // (2) Layer effect — an anthem-style +1/+1 on a combatant. The
        //     computed P/T must be reproducible.
        var attacker = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield, ActiveEffects = effects };
        effects.Register(new AnthemPump(attacker, 1, 1));

        // (3) Combat — attacker deals its (boosted) power to bob's life.
        bob.LoseLife(attacker.Power);

        // (4) Multiple simultaneous triggers across both controllers — APNAP
        //     ordered. Construction order is fixed, so the order is fixed.
        var t1 = MakeTrigger(bob);
        var t2 = MakeTrigger(alice);
        var t3 = MakeTrigger(bob);
        var t4 = MakeTrigger(alice);
        var apnap = ApnapOrdering.Order(
            new ITriggeredAbility[] { t1, t2, t3, t4 }, activePlayer: alice);
        var triggerControllerOrder = apnap
            .Select(t => ReferenceEquals(t.Controller, alice) ? "A" : "B")
            .ToList();

        // (5) Legend-rule conflict — two same-name legendaries under alice;
        //     the SBA sends the later-ETB one to the graveyard by ETB order.
        var legendA = new Creature("Kozilek, the Great Distortion", "8", 12, 12,
            supertypes: new[] { CardSupertype.Legendary })
        { Owner = alice, Controller = alice };
        var legendB = new Creature("Kozilek, the Great Distortion", "8", 12, 12,
            supertypes: new[] { CardSupertype.Legendary })
        { Owner = alice, Controller = alice };
        legendA.SetZone(ZoneType.Battlefield);
        legendB.SetZone(ZoneType.Battlefield);
        zones.MoveCardTo(legendA, ZoneType.Battlefield, alice);
        zones.MoveCardTo(legendB, ZoneType.Battlefield, alice);
        sba.CheckStateBasedActions(
            new[] { alice, bob },
            new ICard[] { legendA, legendB });

        var legendSurvivor = legendA.Zone == ZoneType.Battlefield ? "A"
                           : legendB.Zone == ZoneType.Battlefield ? "B"
                           : "none";

        return new ScenarioSnapshot(
            ShuffledLibrary: library,
            AttackerPower: attacker.Power,
            AttackerToughness: attacker.Toughness,
            BobLife: bob.LifeTotal,
            TriggerControllerOrder: triggerControllerOrder,
            LegendSurvivor: legendSurvivor);
    }

    private sealed record ScenarioSnapshot(
        IReadOnlyList<string> ShuffledLibrary,
        int AttackerPower,
        int AttackerToughness,
        int BobLife,
        IReadOnlyList<string> TriggerControllerOrder,
        string LegendSurvivor);

    // ── helpers ────────────────────────────────────────────────────────

    private static TriggeredAbility MakeTrigger(Player controller)
    {
        // A no-op state-trigger; only its Controller + (logical-clock)
        // Timestamp matter for ordering.
        var source = new Creature("Trigger Source", "1", 1, 1)
        { Owner = controller, Controller = controller, Zone = ZoneType.Battlefield };
        return new TriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>((_, _) => false));
    }

    /// <summary>Anthem-style +P/+T at Layer 7c (CR 613.7c).</summary>
    private sealed class AnthemPump : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p;
        private readonly int _t;
        public AnthemPump(Creature target, int p, int t) { _target = target; _p = p; _t = t; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override bool IsActive() => _target.Zone == ZoneType.Battlefield;
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _p;
            chars.Toughness += _t;
        }
    }
}
