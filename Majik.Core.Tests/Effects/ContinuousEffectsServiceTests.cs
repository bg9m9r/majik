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

    // ---- Test fixtures ----

    private sealed class PumpEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        private readonly bool _eot;

        public PumpEffect(Creature target, int p, int t, bool untilEndOfTurn = true)
        {
            _target = target; _p = p; _t = t; _eot = untilEndOfTurn;
        }

        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => _eot;
        public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _p;
            chars.Toughness += _t;
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
