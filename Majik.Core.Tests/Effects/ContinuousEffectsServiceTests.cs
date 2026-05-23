using System.Threading;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

public class ContinuousEffectsServiceTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Compute_NoEffects_ReturnsBaseStats()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Compute_BakesPrintedKeywords()
    {
        var svc = new ContinuousEffectsService();
        var air = new Creature("Air Elemental", "3UU", 4, 4) { Owner = _alice };
        air.AddAbility(new KeywordAbility("Flying", air, _alice));

        var chars = svc.Compute(air);

        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Layer7c_PumpEffect_AddsPowerAndToughness()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);
        svc.Register(new PumpEffect(bear, 3, 3));

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(5);
        chars.Toughness.Should().Be(5);
    }

    [Fact]
    public void Layer6_GrantsKeyword()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);
        svc.Register(new GrantKeywordEffect(bear, "Flying"));

        var chars = svc.Compute(bear);

        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void ExpireEndOfTurn_RemovesUntilEndOfTurnEffects()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);
        svc.Register(new PumpEffect(bear, 3, 3, untilEndOfTurn: true));
        svc.Register(new PumpEffect(bear, 1, 1, untilEndOfTurn: false));

        svc.Compute(bear).Power.Should().Be(6); // 2 + 3 + 1

        svc.ExpireEndOfTurn();
        svc.Compute(bear).Power.Should().Be(3); // 2 + 1 (permanent)
    }

    [Fact]
    public void Layers_AppliedInOrder()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);
        // Layer 6 grant flying; layer 7c pump should see flying already granted (later layer).
        svc.Register(new PumpEffect(bear, 3, 3));
        svc.Register(new GrantKeywordEffect(bear, "Flying"));

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(5);
        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void DependsOn_AppliesDependencyBeforeDependent_RegardlessOfTimestamp()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        // B is the "dependency": +1/+1 pump, Layer 7c, registered FIRST with
        // an artificially earlier timestamp... wait, requirement says B has
        // later timestamp. So we register A first (earlier ts) then B.
        var a = new DoubleIfPumpedEffect(bear);
        Thread.Sleep(2);
        var b = new PumpEffect(bear, 1, 1);
        a.SetDependency(b);

        // Register out of dependency order — by timestamp alone A would run
        // first and see base Power (2 == base) → not double, then B would
        // add +1 → final 3. With dependency ordering A.DependsOn(B), so B
        // runs first: 2+1=3, then A sees 3>2 and doubles → final 6.
        svc.Register(a);
        svc.Register(b);

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(6); // (2+1)*2, proves B was applied before A
    }

    [Fact]
    public void DependsOn_Cycle_FallsBackToTimestamp()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        // A and B both Layer 7c, mutually dependent → cycle. A has the
        // earlier timestamp, so timestamp fallback should run A then B.
        // A multiplies Power by 2; B adds +1. A-then-B → (2*2)+1 = 5.
        // B-then-A would be (2+1)*2 = 6, so 5 vs 6 distinguishes order.
        var a = new MultiplyPowerEffect(bear, 2);
        Thread.Sleep(2);
        var b = new PumpEffect(bear, 1, 0);
        a.SetCycleMate(b);
        b.MarkDependsOn(a);

        // Register in reverse-ts order to prove ordering uses timestamp,
        // not registration order.
        svc.Register(b);
        svc.Register(a);

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(5); // (2*2)+1 — A first, then B
    }

    [Fact]
    public void DependsOn_NoDependencies_PreservesTimestampOrder()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        // Two unrelated 7c effects. A: multiply by 2, registered first
        // (earlier ts). B: +1, registered second. Independent → timestamp
        // order: A then B → (2*2)+1 = 5.
        var a = new MultiplyPowerEffect(bear, 2);
        Thread.Sleep(2);
        var b = new PumpEffect(bear, 1, 0);

        svc.Register(b); // register out of order to confirm ordering is by ts
        svc.Register(a);

        var chars = svc.Compute(bear);

        chars.Power.Should().Be(5);
    }

    // ---- Test fixtures ----

    private sealed class PumpEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        private readonly bool _eot;
        private ContinuousEffect? _dependsOn;

        public PumpEffect(Creature target, int p, int t, bool untilEndOfTurn = true)
        {
            _target = target; _p = p; _t = t; _eot = untilEndOfTurn;
        }

        public void MarkDependsOn(ContinuousEffect other) => _dependsOn = other;

        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => _eot;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
        public override bool DependsOn(ContinuousEffect other) =>
            _dependsOn != null && ReferenceEquals(other, _dependsOn);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _p;
            chars.Toughness += _t;
        }

    }

    /// <summary>
    /// Layer 7c effect that doubles current Power iff it has been pumped
    /// (Power > base). Declares a dependency on a specific other effect to
    /// force that effect to run first regardless of timestamp.
    /// </summary>
    private sealed class DoubleIfPumpedEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private ContinuousEffect? _dependsOn;

        public DoubleIfPumpedEffect(Creature target) { _target = target; }

        public void SetDependency(ContinuousEffect other) => _dependsOn = other;

        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
        public override bool DependsOn(ContinuousEffect other) =>
            _dependsOn != null && ReferenceEquals(other, _dependsOn);
        public override void Apply(CreatureCharacteristics chars)
        {
            if (chars.Power > _target.BasePower)
            {
                chars.Power *= 2;
            }
        }
    }

    /// <summary>Layer 7c effect that multiplies Power by a factor. Supports
    /// declaring a "cycle mate" — a mutual dependency for cycle tests.</summary>
    private sealed class MultiplyPowerEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _factor;
        private ContinuousEffect? _cycleMate;

        public MultiplyPowerEffect(Creature target, int factor)
        {
            _target = target; _factor = factor;
        }

        public void SetCycleMate(ContinuousEffect other) => _cycleMate = other;

        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
        public override bool DependsOn(ContinuousEffect other) =>
            _cycleMate != null && ReferenceEquals(other, _cycleMate);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power *= _factor;
        }
    }

    private sealed class GrantKeywordEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly string _keyword;
        public GrantKeywordEffect(Creature target, string keyword)
        {
            _target = target; _keyword = keyword;
        }
        public override Layer Layer => Layer.Abilities;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
        public override void Apply(CreatureCharacteristics chars) => chars.Keywords.Add(_keyword);
    }
}
