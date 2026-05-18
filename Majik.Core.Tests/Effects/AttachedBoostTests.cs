using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;

public class AttachedBoostTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HolyStrength_Aura_Grants_PlusOnePlusTwo()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var aura = new Enchantment("Holy Strength", "W",
            subtypes: new[] { CardSubtype.Aura })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        aura.AttachTo(bear);
        svc.Register(new AttachedBoostEffect(aura, power: 1, toughness: 2));

        bear.Power.Should().Be(3);
        bear.Toughness.Should().Be(4);
    }

    [Fact]
    public void Aura_Unattached_NoEffect()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var aura = new Enchantment("Holy Strength", "W",
            subtypes: new[] { CardSubtype.Aura })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        svc.Register(new AttachedBoostEffect(aura, power: 1, toughness: 2));

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void Equipment_GrantsPowerAndKeyword()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var sword = new Artifact("Sword of Strength", "3",
            subtypes: new[] { CardSubtype.Equipment })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        sword.AttachTo(bear);
        svc.Register(new AttachedBoostEffect(sword,
            power: 2, toughness: 2,
            grantedKeywords: new[] { "First strike" }));

        bear.Power.Should().Be(4);
        bear.Toughness.Should().Be(4);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
    }

    [Fact]
    public void Equipment_TransfersBoost_WhenReEquipped()
    {
        var svc = new ContinuousEffectsService();
        var bear1 = new Creature("Bear1", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var bear2 = new Creature("Bear2", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var sword = new Artifact("Sword", "3",
            subtypes: new[] { CardSubtype.Equipment })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        sword.AttachTo(bear1);
        svc.Register(new AttachedBoostEffect(sword, power: 2, toughness: 2));

        bear1.Power.Should().Be(4);
        bear2.Power.Should().Be(2);

        sword.AttachTo(bear2);

        bear1.Power.Should().Be(2);
        bear2.Power.Should().Be(4);
    }
}
