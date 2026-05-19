using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class BecomesPTEffectTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Becomes0_0_OverridesBasePT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 4, 4)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        svc.Register(new BecomesPTEffect(bear, 0, 0));

        bear.Power.Should().Be(0);
        bear.Toughness.Should().Be(0);
    }

    [Fact]
    public void Becomes4_4_ThenPump_Plus1Plus1_IsLayer7cOnTopOfBase()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 1, 1)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        svc.Register(new BecomesPTEffect(bear, 4, 4));
        // Same kind of pump emitted by Giant Growth, applied at 7c.
        svc.Register(new SimplePumpL7c(bear, 1, 1));

        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(5);
    }

    [Fact]
    public void Becomes_Inactive_OffBattlefield_RestoresPrintedPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new BecomesPTEffect(bear, 0, 0));
        bear.Power.Should().Be(0);

        bear.Zone = ZoneType.Graveyard;
        bear.Power.Should().Be(2);
    }

    private sealed class SimplePumpL7c : ContinuousEffect
    {
        private readonly Creature _t; private readonly int _p, _to;
        public SimplePumpL7c(Creature t, int p, int to) { _t = t; _p = p; _to = to; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _t);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _to; }
    }
}
