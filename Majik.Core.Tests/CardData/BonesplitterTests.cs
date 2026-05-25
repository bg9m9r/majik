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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BonesplitterFactory"/> (Mirrodin, {1}).
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip activated ability shape: {1} mana cost.
/// - Static +3/+0 effect: equipped 2/2 Bear becomes 5/2.
/// - Detach: P/T returns to base.
/// - Unattached: no boost.
/// </summary>
public class BonesplitterTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Bonesplitter_EquipAbility_HasGenericOneCost()
    {
        var c = BonesplitterFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +3/+0
    // -----------------------------------------------------------------------

    [Fact]
    public void Bonesplitter_Equipped_Bear_Becomes_5_2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var bonesplitter = BonesplitterFactory.Create(_alice, svc);
        bonesplitter.Zone = ZoneType.Battlefield;

        bonesplitter.AttachTo(bear);

        bear.GetPower().Should().Be(5, "+3/+0 boost from Bonesplitter");
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

        var bonesplitter = BonesplitterFactory.Create(_alice, svc);
        bonesplitter.Zone = ZoneType.Battlefield;
        bonesplitter.AttachTo(bear);

        bear.GetPower().Should().Be(5);

        bonesplitter.Unattach();

        // AttachedBoostEffect.IsActive gates on AttachedTo != null —
        // bear falls back to its printed 2/2 once unequipped.
        bear.GetPower().Should().Be(2, "boost lapses on detach");
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
        var bonesplitter = BonesplitterFactory.Create(_alice, svc);
        bonesplitter.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Bonesplitter's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }
}
