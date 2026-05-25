using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoneSawFactory"/>.
///
/// Card: Bone Saw — Artifact — Equipment {0} (Mirrodin).
///   "Equipped creature gets +1/+0."
///   "Equip {2}."
/// </summary>
public class BoneSawFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BoneSaw_Identity()
    {
        var c = BoneSawFactory.Create(_alice);

        c.Name.Should().Be("Bone Saw");
        c.ManaCost.Should().Be("{0}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Bone Saw is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BoneSaw_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bone Saw", _alice);

        c.Should().BeOfType<Artifact>("Bone Saw is an Artifact");
        c.Name.Should().Be("Bone Saw");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void BoneSaw_EquipAbility_HasGenericTwoCost()
    {
        var c = BoneSawFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void BoneSaw_Equipped_Bear_Becomes_3_2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var saw = BoneSawFactory.Create(_alice, svc);
        saw.Zone = ZoneType.Battlefield;

        saw.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+0 boost from Bone Saw");
        bear.GetToughness().Should().Be(2, "Bone Saw adds +0 toughness");
    }

    [Fact]
    public void BoneSaw_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var saw = BoneSawFactory.Create(_alice, svc);
        saw.Zone = ZoneType.Battlefield;
        saw.AttachTo(bear);

        bear.GetPower().Should().Be(3);

        saw.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void BoneSaw_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var saw = BoneSawFactory.Create(_alice, svc);
        saw.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Bone Saw's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }
}
