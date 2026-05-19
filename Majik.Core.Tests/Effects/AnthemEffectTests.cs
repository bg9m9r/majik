using FluentAssertions;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;

/// <summary>
/// Glorious Anthem (CR 613, Layer 7c): "Creatures you control get +1/+1."
/// Static effect — applies while source on battlefield.
/// </summary>
public class AnthemEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Anthem_BuffsAliceCreatures_NotBobs()
    {
        var svc = new ContinuousEffectsService();

        var anthem = new Enchantment("Glorious Anthem", "1WW") { Owner = _alice, Controller = _alice };
        anthem.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(anthem);

        var aliceBear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var bobBear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob, ActiveEffects = svc };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        svc.Register(new AnthemEffect(source: anthem, controller: _alice));

        aliceBear.Power.Should().Be(3);
        aliceBear.Toughness.Should().Be(3);
        bobBear.Power.Should().Be(2);
        bobBear.Toughness.Should().Be(2);
    }

    [Fact]
    public void Anthem_ExpiresWhenSourceLeavesBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var anthem = new Enchantment("Glorious Anthem", "1WW") { Owner = _alice, Controller = _alice };
        anthem.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(anthem);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        svc.Register(new AnthemEffect(anthem, _alice));
        bear.Power.Should().Be(3);

        // Source leaves battlefield → effect inactive
        anthem.SetZone(ZoneType.Graveyard);
        bear.Power.Should().Be(2);
    }

    /// <summary>
    /// Layer 7c effect: while source is on the battlefield, all creatures the
    /// controller controls get +1/+1.
    /// </summary>
    private sealed class AnthemEffect : ContinuousEffect
    {
        private readonly Majik.Core.Cards.ICard _source;
        private readonly Player _controller;
        public AnthemEffect(Majik.Core.Cards.ICard source, Player controller)
        {
            _source = source; _controller = controller;
        }
        public override Layer Layer => Layer.PT_Modify;
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c.Controller, _controller);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += 1;
            chars.Toughness += 1;
        }
    }
}
