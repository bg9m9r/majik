using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Locks the CR-613 layer-pipeline memoization added to
/// <see cref="ContinuousEffectsService"/> (fast path + generation cache).
///
/// These tests assert TWO things at once:
///   1. Correctness — every cache-invalidating surface bumps the generation,
///      so a read after each mutation reflects the new value (no stale P/T).
///   2. The (a) zero-effect fast path is behaviourally identical to the full
///      pipeline under the <c>_effects.Count == 0</c> precondition.
///
/// The invalidation invariant under test: the cache key is
/// <c>(generation, Permanent)</c> ONLY — never keyed by effect type — so any
/// effect mutation auto-invalidates regardless of effect class.
/// </summary>
public class ContinuousEffectsServiceMemoizationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ----------------------------------------------------------------------
    // (1) Fast-path equivalence — counters + printed keywords + printed
    //     subtypes with _effects EMPTY → fast path == full path.
    // ----------------------------------------------------------------------

    [Fact]
    public void FastPath_WithCountersKeywordsSubtypes_EqualsFullPath()
    {
        // Full-path reference: a service with a no-op effect registered so
        // _effects.Count > 0 (forces the full pipeline). Both services see the
        // same printed seed + counter postlude, so results must be identical.
        var fastSvc = new ContinuousEffectsService();
        var fullSvc = new ContinuousEffectsService();

        Creature MakeBear(ContinuousEffectsService svc)
        {
            var bear = new Creature("Air Bear", "1G", 2, 2,
                subtypes: new[] { CardSubtype.Bear })
            {
                Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
                ActiveEffects = svc,
            };
            bear.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", bear, _alice));
            bear.Counters.Add(CounterType.PlusOnePlusOne, 2);
            return bear;
        }

        var fastBear = MakeBear(fastSvc); // _effects empty → fast path
        var fullBear = MakeBear(fullSvc);
        // Register an effect that does not match the bear, so the full path
        // runs but produces the same printed+counter result.
        fullSvc.Register(new NoMatchEffect());

        var fast = (CreatureCharacteristics)fastSvc.Compute(fastBear);
        var full = (CreatureCharacteristics)fullSvc.Compute(fullBear);

        fast.Power.Should().Be(full.Power);
        fast.Toughness.Should().Be(full.Toughness);
        fast.Power.Should().Be(4, "2 base + 2 from +1/+1 counters");
        fast.Keywords.Should().BeEquivalentTo(full.Keywords);
        fast.Keywords.Should().Contain("Flying");
        fast.Subtypes.Should().BeEquivalentTo(full.Subtypes);
        fast.Subtypes.Should().Contain(CardSubtype.Bear);
    }

    [Fact]
    public void FastPath_ReturnsFreshClone_NotSharedMutableState()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var first = (CreatureCharacteristics)svc.Compute(bear);
        first.Power = 999;                 // caller read-mutates the working set
        first.Keywords.Add("Bogus");

        var second = (CreatureCharacteristics)svc.Compute(bear);
        second.Power.Should().Be(2, "a cache hit must hand back a fresh clone");
        second.Keywords.Should().NotContain("Bogus");
    }

    // ----------------------------------------------------------------------
    // (1b) Scalar P/T hot-path cache (ComputePowerToughness) — zero-alloc on
    //      hit + value-parity with the full Compute() layered result.
    // ----------------------------------------------------------------------

    [Fact]
    public void ScalarPt_CacheHit_AllocatesNothing_OnHotPtRead()
    {
        // 30-creature board with counters + a few anthems so the first read
        // populates a non-trivial layered + scalar cache. Subsequent reads in a
        // fresh generation must serve cached ints WITHOUT cloning the layered
        // working set (which would allocate a PermanentCharacteristics + four
        // HashSets per read).
        var svc = new ContinuousEffectsService();
        var board = new List<Creature>();
        for (var i = 0; i < 30; i++)
        {
            var c = new Creature($"C{i}", "1G", 2, 2)
            {
                Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
            };
            c.Counters.Add(CounterType.PlusOnePlusOne);
            board.Add(c);
        }
        foreach (var c in board.Take(3)) svc.Register(new FlatPump(c, 1, 1));

        // Warm both caches (and JIT the read path) outside the measured window.
        long warm = 0;
        for (var pass = 0; pass < 50; pass++)
        {
            foreach (var c in board) warm += c.GetPower() + c.GetToughness();
        }
        warm.Should().BeGreaterThan(0);

        // Measure a pure cache-hit loop: no generation bump occurs between the
        // warm-up and here, so every GetPower/GetToughness is a scalar hit.
        var before = GC.GetAllocatedBytesForCurrentThread();
        long total = 0;
        for (var pass = 0; pass < 500; pass++)
        {
            foreach (var c in board) total += c.GetPower() + c.GetToughness();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.Should().BeGreaterThan(0);
        // 500 passes x 30 creatures x 2 reads = 30,000 hot P/T reads. The old
        // clone-on-hit path allocated ~5 objects EACH (a CreatureCharacteristics
        // + four HashSets) ⇒ well over a megabyte here. The scalar cache returns
        // a value tuple with zero heap traffic; allow a tiny slack only for
        // incidental test-harness noise.
        allocated.Should().BeLessThan(4096,
            $"hot P/T cache hits must not clone the layered working set (allocated {allocated} bytes for 30,000 reads)");
    }

    [Fact]
    public void ScalarPt_MatchesLayeredCompute_AcrossCounterAndAnthemAndStrip()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        void AssertParity(string because)
        {
            var layered = (CreatureCharacteristics)svc.Compute(bear);
            var scalar = svc.ComputePowerToughness(bear);
            scalar.Power.Should().Be(layered.Power, because);
            scalar.Toughness.Should().Be(layered.Toughness, because);
            // The public Creature accessors must agree too.
            bear.Power.Should().Be(layered.Power, because);
            bear.Toughness.Should().Be(layered.Toughness, because);
        }

        AssertParity("printed base");

        svc.Register(new FlatPump(bear, 1, 1));
        AssertParity("after anthem register");

        bear.Counters.Add(CounterType.PlusOnePlusOne, 2);
        AssertParity("after +1/+1 counters (counter delta folded into scalar)");

        bear.Counters.Add(CounterType.MinusOneMinusOne, 1);
        AssertParity("after -1/-1 counter");

        var source = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var strip = new LoseAllAbilitiesEffect(source, new[] { bear });
        svc.Register(strip);
        AssertParity("after strip");
    }

    [Fact]
    public void ScalarPt_CdaInput_LifeChangeViaEvent_RefreshesScalarCache()
    {
        // The scalar cache must invalidate on the same out-of-band CDA surface
        // the layered cache does (Death's Shadow reads life via LifeChanged).
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new ZoneService(bus);
        var players = new PlayerService(bus);

        _alice.LifeTotal = 10;
        var shadow = DeathsShadowFactory.Create(_alice, effects, bus);
        shadow.ActiveEffects = effects;
        zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.GetPower().Should().Be(3, "13 - 10 (warms the scalar cache)");
        players.LoseLife(_alice, 5); // 10 → 5
        shadow.GetPower().Should().Be(8, "scalar cache must refresh after the LifeChangedEvent bump");
        shadow.GetToughness().Should().Be(8);
    }

    // ----------------------------------------------------------------------
    // (2) Anthem +N/+N — registering a second anthem changes the value
    //     (generation bump on Register).
    // ----------------------------------------------------------------------

    [Fact]
    public void Anthem_SecondRegister_BumpsGeneration_AndChangesPower()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        svc.Register(new FlatPump(bear, 1, 1));
        bear.Power.Should().Be(3);

        svc.Register(new FlatPump(bear, 2, 2));
        bear.Power.Should().Be(5, "registering a second anthem invalidates the cache");
        bear.Toughness.Should().Be(5);
    }

    // ----------------------------------------------------------------------
    // (3) +1/+1 / -1/-1 counters — immediate change (counter-delta live).
    // ----------------------------------------------------------------------

    [Fact]
    public void Counters_AddedAfterRead_ReflectImmediately()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        bear.Power.Should().Be(2);

        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);
        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(5);

        bear.Counters.Add(CounterType.MinusOneMinusOne, 1);
        bear.Power.Should().Be(4);
        bear.Toughness.Should().Be(4);
    }

    // ----------------------------------------------------------------------
    // (4) Humility/strip + SyncAbilityGrants reconcile.
    // ----------------------------------------------------------------------

    [Fact]
    public void Strip_RegisterThenUnregister_RestoresKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        bear.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", bear, _alice));

        ((CreatureCharacteristics)svc.Compute(bear)).Keywords.Should().Contain("Flying");

        var source = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var strip = new LoseAllAbilitiesEffect(source, new[] { bear });
        svc.Register(strip);

        ((CreatureCharacteristics)svc.Compute(bear)).Keywords
            .Should().NotContain("Flying", "strip clears keywords");

        svc.Unregister(strip);
        ((CreatureCharacteristics)svc.Compute(bear)).Keywords
            .Should().Contain("Flying", "unregister restores the printed keyword");
    }

    // ----------------------------------------------------------------------
    // (5) EOT expiry — until-EOT pump boosts; ExpireEndOfTurn → back to base.
    // ----------------------------------------------------------------------

    [Fact]
    public void EndOfTurnExpiry_LiftsBoost()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        svc.Register(new FlatPump(bear, 3, 3, expiresAtEndOfTurn: true));
        bear.Power.Should().Be(5);

        svc.ExpireEndOfTurn();
        bear.Power.Should().Be(2, "until-EOT pump lifts at end of turn");
        bear.Toughness.Should().Be(2);
    }

    // ----------------------------------------------------------------------
    // (7) CDA freshness via the engine event bus — Tarmogoyf (CardMovedEvent),
    //     Death's Shadow (LifeChangedEvent), Master of Etherium (artifact ETB).
    //     The service is wired to the bus, and state changes are routed through
    //     the production event paths (ZoneService / PlayerService).
    // ----------------------------------------------------------------------

    [Fact]
    public void Cda_Tarmogoyf_FreshAfterCardEntersGraveyard_ViaCardMovedEvent()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new ZoneService(bus);

        Func<IEnumerable<ICard>> grave = () =>
            _alice.Zones.Graveyard.GetCards().Concat(_bob.Zones.Graveyard.GetCards());

        var goyf = TarmogoyfFactory.Create(_alice, effects, bus, grave);
        goyf.ActiveEffects = effects;
        zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        goyf.Power.Should().Be(0, "empty graveyards → 0 card types");

        // Bolt dies → enters the graveyard via the zone service (CardMovedEvent
        // bumps the generation through the service's bus subscription).
        var bolt = new Creature("Goblin", "R", 1, 1) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(bolt);
        bolt.SetZone(ZoneType.Battlefield);
        zones.MoveCard(bolt, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        goyf.Power.Should().Be(1, "one card type (Creature) in a graveyard");
        goyf.Toughness.Should().Be(2);
    }

    [Fact]
    public void Cda_DeathsShadow_FreshAfterLifeChange_ViaLifeChangedEvent()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new ZoneService(bus);
        var players = new PlayerService(bus);

        _alice.LifeTotal = 20;
        var shadow = DeathsShadowFactory.Create(_alice, effects, bus);
        shadow.ActiveEffects = effects;
        zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(0, "life 20 → 13-20 clamps to 0");

        // Life loss through PlayerService fires a LifeChangedEvent → bump.
        players.LoseLife(_alice, 15); // 20 → 5
        shadow.Power.Should().Be(8, "13 - 5 = 8");
        shadow.Toughness.Should().Be(8);
    }

    [Fact]
    public void Cda_MasterOfEtherium_FreshAfterArtifactEtb_ViaCardMovedEvent()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new ZoneService(bus);

        var master = MasterOfEtheriumFactory.Create(_alice, effects, bus);
        master.ActiveEffects = effects;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        master.GetPower().Should().Be(1, "the master itself is an artifact");

        // Artifact ETB through the zone service → CardMovedEvent → bump.
        var memnite = new Artifact("Memnite", "0") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(memnite);
        zones.MoveCard(memnite, ZoneType.Library, ZoneType.Battlefield, _alice);

        master.GetPower().Should().Be(2, "master + memnite = 2 artifacts");
        master.GetToughness().Should().Be(2);
    }

    // ----------------------------------------------------------------------
    // (8) Per-surface stale-cache regression — one assertion per invalidation
    //     surface so any missed generation bump fails loudly.
    // ----------------------------------------------------------------------

    [Fact]
    public void Surface_Counter_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        bear.Power.Should().Be(2);
        bear.Counters.Add(CounterType.PlusOnePlusOne);
        bear.Power.Should().Be(3, "counter mutation must invalidate the cache");
    }

    [Fact]
    public void Surface_BasePowerSetter_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        bear.Power.Should().Be(2);
        bear.BasePower = 5;
        bear.Power.Should().Be(5, "base-P/T setter must invalidate the cache");
    }

    [Fact]
    public void Surface_Register_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        bear.Power.Should().Be(2);
        svc.Register(new FlatPump(bear, 4, 4));
        bear.Power.Should().Be(6, "Register must invalidate the cache");
    }

    [Fact]
    public void Surface_Unregister_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        var pump = new FlatPump(bear, 4, 4);
        svc.Register(pump);
        bear.Power.Should().Be(6);
        svc.Unregister(pump);
        bear.Power.Should().Be(2, "Unregister must invalidate the cache");
    }

    [Fact]
    public void Surface_ExpireEndOfTurn_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        svc.Register(new FlatPump(bear, 4, 4, expiresAtEndOfTurn: true));
        bear.Power.Should().Be(6);
        svc.ExpireEndOfTurn();
        bear.Power.Should().Be(2, "ExpireEndOfTurn must invalidate the cache");
    }

    [Fact]
    public void Surface_FaceDownToggle_Invalidates()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        bear.Power.Should().Be(2);
        bear.MarkFaceDown();
        bear.Power.Should().Be(2, "face-down → 2/2 (CR 708.2); cache invalidated");
        bear.BasePower = 7;        // change base while face-down (no visible effect yet)
        bear.TurnFaceUp();
        bear.Power.Should().Be(7, "turning face-up restores native P/T; cache invalidated");
    }

    [Fact]
    public void Surface_CdaInput_LifeChangeViaEvent_Invalidates()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var zones = new ZoneService(bus);
        var players = new PlayerService(bus);

        _alice.LifeTotal = 10;
        var shadow = DeathsShadowFactory.Create(_alice, effects, bus);
        shadow.ActiveEffects = effects;
        zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(3, "13 - 10");
        players.LoseLife(_alice, 5); // 10 → 5
        shadow.Power.Should().Be(8, "CDA input change via LifeChangedEvent must invalidate");
    }

    [Fact]
    public void Surface_Clear_Invalidates()
    {
        // A predicate reading out-of-band state with no event hook; Clear() is
        // the documented invalidate-all path.
        var svc = new ContinuousEffectsService();
        var bear = NewBear(svc);
        var gate = false;
        svc.Register(new GatedPump(bear, () => gate, 3, 3));

        bear.Power.Should().Be(2, "gate closed");
        gate = true;
        svc.Clear();
        bear.Power.Should().Be(5, "Clear() invalidates the whole cache");
    }

    // ----------------------------------------------------------------------
    // Optional perf evidence (skipped by default — wall-clock, not a gate).
    // Measured locally on this board shape (30 creatures + counters, 3 anthems,
    // 5000 passes = 300k GetPower+GetToughness reads): ~188 ms WITH the cache
    // vs ~817 ms when the generation is bumped on every read to force a
    // recompute — roughly a 4.3x speedup, plus the zero-effect fast path
    // (the common case) skips the whole CR-613 pipeline allocation-free.
    // ----------------------------------------------------------------------

    [Fact(Skip = "perf evidence only; not a correctness gate")]
    public void Perf_GetPowerHammer_30Creatures_FewAnthems()
    {
        var svc = new ContinuousEffectsService();
        var board = new List<Creature>();
        for (var i = 0; i < 30; i++)
        {
            var c = new Creature($"C{i}", "1G", 2, 2)
            {
                Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
            };
            c.Counters.Add(CounterType.PlusOnePlusOne);
            board.Add(c);
        }
        // A few board-wide anthems so the full pipeline has real work.
        foreach (var c in board.Take(3)) svc.Register(new FlatPump(c, 1, 1));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long total = 0;
        for (var pass = 0; pass < 5000; pass++)
        {
            foreach (var c in board) total += c.GetPower() + c.GetToughness();
        }
        sw.Stop();
        total.Should().BeGreaterThan(0);
        // Emitted for manual inspection.
        Assert.True(true, $"30 creatures x 5000 passes in {sw.ElapsedMilliseconds} ms");
    }

    // ----------------------------------------------------------------------
    // Helpers + test doubles
    // ----------------------------------------------------------------------

    private Creature NewBear(ContinuousEffectsService svc) => new("Bear", "1G", 2, 2)
    {
        Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
    };

    /// <summary>Flat Layer-7c +P/+T pump scoped to one creature.</summary>
    private sealed class FlatPump : ContinuousEffect
    {
        private readonly Creature _t;
        private readonly int _p, _tt;
        private readonly bool _eot;
        public FlatPump(Creature t, int p, int tt, bool expiresAtEndOfTurn = false)
        { _t = t; _p = p; _tt = tt; _eot = expiresAtEndOfTurn; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => _eot;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _t);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _tt; }
    }

    /// <summary>Layer-7c pump active only while a closure gate is true.</summary>
    private sealed class GatedPump : ContinuousEffect
    {
        private readonly Creature _t;
        private readonly Func<bool> _gate;
        private readonly int _p, _tt;
        public GatedPump(Creature t, Func<bool> gate, int p, int tt)
        { _t = t; _gate = gate; _p = p; _tt = tt; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _t) && _gate();
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _tt; }
    }

    /// <summary>An effect that never matches — forces the full pipeline
    /// (so _effects.Count > 0) without altering any characteristics.</summary>
    private sealed class NoMatchEffect : ContinuousEffect
    {
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => false;
        public override void Apply(CreatureCharacteristics chars) { }
    }
}
