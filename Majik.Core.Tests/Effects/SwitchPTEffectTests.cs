using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class SwitchPTEffectTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Switch_Swaps3_4_To_4_3()
    {
        var svc = new ContinuousEffectsService();
        var dino = new Creature("Dino", "3G", 3, 4)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new SwitchPTEffect(dino));

        dino.Power.Should().Be(4);
        dino.Toughness.Should().Be(3);
    }

    [Fact]
    public void Switch_AppliesAfterPump()
    {
        var svc = new ContinuousEffectsService();
        var dino = new Creature("Dino", "3G", 3, 4)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new SwitchPTEffect(dino));
        svc.Register(new SimplePump(dino, 1, 1));

        // 7c pump first: 4/5; then 7d switch: 5/4.
        dino.Power.Should().Be(5);
        dino.Toughness.Should().Be(4);
    }

    private sealed class SimplePump : ContinuousEffect
    {
        private readonly Creature _t; private readonly int _p, _to;
        public SimplePump(Creature t, int p, int to) { _t = t; _p = p; _to = to; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _t);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _to; }
    }
}
