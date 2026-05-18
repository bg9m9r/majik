using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

public class LayeredCreatureTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Creature_NoService_ReturnsBaseStats()
    {
        var bear = new Creature("Bear", "1G", 2, 2);

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void Creature_WithService_ReturnsModifiedPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { ActiveEffects = svc };
        svc.Register(new PumpEffect(bear, 3, 3));

        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(5);
    }

    [Fact]
    public void Creature_WithGrantedFlying_CombatAbilitiesSeesIt()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { ActiveEffects = svc };
        svc.Register(new GrantKeywordEffect(bear, "Flying"));

        CombatAbilities.HasFlying(bear).Should().BeTrue();
    }

    [Fact]
    public void Creature_PrintedFlying_StillSeenViaLayerSystem()
    {
        var svc = new ContinuousEffectsService();
        var air = new Creature("Air Elemental", "3UU", 4, 4) { ActiveEffects = svc, Owner = _alice };
        air.AddAbility(new KeywordAbility("Flying", air, _alice));

        CombatAbilities.HasFlying(air).Should().BeTrue();
    }

    private sealed class PumpEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public PumpEffect(Creature t, int p, int tough) { _target = t; _p = p; _t = tough; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }

    private sealed class GrantKeywordEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly string _kw;
        public GrantKeywordEffect(Creature t, string kw) { _target = t; _kw = kw; }
        public override Layer Layer => Layer.Abilities;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars) => chars.Keywords.Add(_kw);
    }
}
