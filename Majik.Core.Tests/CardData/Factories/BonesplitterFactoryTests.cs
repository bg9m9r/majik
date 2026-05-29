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
/// Unit tests for <see cref="BonesplitterFactory"/>.
///
/// Card: Bonesplitter — Artifact — Equipment {1} (Mirrodin).
///   "Equipped creature gets +2/+0."
///   "Equip {1}."
///
/// Identical mechanical shape to <see cref="BoneSawFactory"/> /
/// <see cref="ColossusHammerFactory"/> (flat +X/+0 equip), differing only
/// in the boost magnitude (+2/+0) and equip cost ({1}).
/// </summary>
public class BonesplitterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Bonesplitter_Identity()
    {
        var c = BonesplitterFactory.Create(_alice);

        c.Name.Should().Be("Bonesplitter");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Bonesplitter is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Bonesplitter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bonesplitter", _alice);

        c.Should().BeOfType<Artifact>("Bonesplitter is an Artifact");
        c.Name.Should().Be("Bonesplitter");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    [Fact]
    public void Bonesplitter_EquipAbility_HasGenericOneCost()
    {
        var c = BonesplitterFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    [Fact]
    public void Bonesplitter_Equipped_Bear_Becomes_4_2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var splitter = BonesplitterFactory.Create(_alice, svc);
        splitter.Zone = ZoneType.Battlefield;

        splitter.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+0 boost from Bonesplitter");
        bear.GetToughness().Should().Be(2, "Bonesplitter adds +0 toughness");
    }

    [Fact]
    public void Bonesplitter_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var splitter = BonesplitterFactory.Create(_alice, svc);
        splitter.Zone = ZoneType.Battlefield;
        splitter.AttachTo(bear);

        bear.GetPower().Should().Be(4);

        splitter.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Bonesplitter_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var splitter = BonesplitterFactory.Create(_alice, svc);
        splitter.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Bonesplitter's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }
}
