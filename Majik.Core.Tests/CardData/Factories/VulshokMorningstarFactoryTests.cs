using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VulshokMorningstarFactory"/>.
///
/// Card: Vulshok Morningstar — Artifact — Equipment {2} (Fifth Dawn).
///   "Equipped creature gets +2/+2."
///   "Equip {2}"
///
/// Same flat +X/+X equip shape as <see cref="BonesplitterFactory"/>, differing
/// only in the boost magnitude (+2/+2) and equip cost ({2}).
/// </summary>
[Trait("Color", "C")]
public class VulshokMorningstarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VulshokMorningstar_Identity()
    {
        var c = VulshokMorningstarFactory.Create(_alice);

        c.Name.Should().Be("Vulshok Morningstar");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Vulshok Morningstar is an Equipment");
    }

    [Fact]
    public void VulshokMorningstar_EquipAbility_HasGenericTwoCost()
    {
        var c = VulshokMorningstarFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void VulshokMorningstar_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var star = VulshokMorningstarFactory.Create(_alice, svc);
        star.Zone = ZoneType.Battlefield;

        star.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+2 boost from Vulshok Morningstar");
        bear.GetToughness().Should().Be(4, "+2/+2 boost from Vulshok Morningstar");
    }

    [Fact]
    public void VulshokMorningstar_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var star = VulshokMorningstarFactory.Create(_alice, svc);
        star.Zone = ZoneType.Battlefield;
        star.AttachTo(bear);

        bear.GetPower().Should().Be(4);

        star.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void VulshokMorningstar_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var star = VulshokMorningstarFactory.Create(_alice, svc);
        star.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Vulshok Morningstar's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }
}
