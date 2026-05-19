using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class SBARegenerationIntegrationTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LethalDamage_WithRegenerationShield_DoesNotDie()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.TakeDamage(3); // lethal — 3 ≥ toughness 2

        var bus = new ReplacementBus();
        bus.Register(new RegenerationShieldEffect(bear));
        var sba = new StateBasedActions(replacements: bus);

        sba.CheckStateBasedActions(new[] { _alice }, new[] { (ICard)bear });

        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.IsTapped.Should().BeTrue();
        bear.Damage.Should().Be(0);
    }

    [Fact]
    public void LethalDamage_NoShield_Dies()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.TakeDamage(3);
        var bus = new ReplacementBus();
        var sba = new StateBasedActions(replacements: bus);

        sba.CheckStateBasedActions(new[] { _alice }, new[] { (ICard)bear });
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void RegenerationShield_IsOneShot_SecondLethalKills()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bus = new ReplacementBus();
        bus.Register(new RegenerationShieldEffect(bear));
        var sba = new StateBasedActions(replacements: bus);

        bear.TakeDamage(3);
        sba.CheckStateBasedActions(new[] { _alice }, new[] { (ICard)bear });
        bear.Zone.Should().Be(ZoneType.Battlefield); // saved

        bear.TakeDamage(3);
        sba.CheckStateBasedActions(new[] { _alice }, new[] { (ICard)bear });
        bear.Zone.Should().Be(ZoneType.Graveyard); // shield consumed
    }
}
