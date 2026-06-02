using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GhostfireBladeFactory"/>.
///
/// Card: Ghostfire Blade — Artifact — Equipment {1} (Khans of Tarkir).
///   "Equipped creature gets +2/+2."
///   "Equip {3}"
///   "This Equipment's equip ability costs {2} less to activate if it
///    targets a colorless creature."
///
/// Same flat-boost equip family as <see cref="BonesplitterFactory"/>
/// (+2/+0, Equip {1}), differing in the +2/+2 boost, the {3} equip cost,
/// and the colorless-target {2}-less rider (CR 118.5).
/// </summary>
[Trait("Color", "C")]
public class GhostfireBladeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GhostfireBlade_Identity()
    {
        var c = GhostfireBladeFactory.Create(_alice);

        c.Name.Should().Be("Ghostfire Blade");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Ghostfire Blade is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GhostfireBlade_EquipAbility_HasGenericThreeCost()
    {
        var c = GhostfireBladeFactory.Create(_alice);

        var ability = c.Abilities.OfType<EquipActivatedAbility>().Single();

        ability.EquipCost.Generic.Should().Be(3,
            "Equip {3} is the printed activation cost");
    }

    [Fact]
    public void GhostfireBlade_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var blade = GhostfireBladeFactory.Create(_alice, svc);
        blade.Zone = ZoneType.Battlefield;

        blade.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+2 boost from Ghostfire Blade");
        bear.GetToughness().Should().Be(4, "+2/+2 boost from Ghostfire Blade");
    }

    [Fact]
    public void GhostfireBlade_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var blade = GhostfireBladeFactory.Create(_alice, svc);
        blade.Zone = ZoneType.Battlefield;
        blade.AttachTo(bear);

        bear.GetPower().Should().Be(4);

        blade.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GhostfireBlade_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var blade = GhostfireBladeFactory.Create(_alice, svc);
        blade.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Ghostfire Blade's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Colorless-target equip cost reduction (CR 118.5 / 117.7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EquipCost_NoTargetChosen_IsPrintedThree()
    {
        var blade = GhostfireBladeFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;

        var mana = blade.Abilities.OfType<EquipActivatedAbility>().Single()
            .Costs.OfType<ManaCostCost>().Single();

        _alice.AddManaToPool(ManaCost.Parse("{2}"));
        mana.CanPay(_alice).Should().BeFalse(
            "with no target chosen the printed {3} applies; {2} is short");
    }

    [Fact]
    public void EquipCost_ColorlessTarget_ReducedByTwo()
    {
        // A colorless creature: an artifact creature with a generic-only
        // mana cost has an empty effective-colour set (CR 105.2).
        var golem = new Creature("Colorless Golem", "{3}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(golem);
        golem.GetEffectiveColors().Should().BeEmpty("a generic-cost creature is colorless");

        var blade = GhostfireBladeFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(blade);

        var equip = blade.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { golem } });

        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        // Only {1} floating — proves the effective cost is {3} - {2} = {1}.
        _alice.AddManaToPool(ManaCost.Parse("{1}"));
        mana.CanPay(_alice).Should().BeTrue(
            "colorless target reduces equip {3} by {2} to {1} (CR 118.5)");
    }

    [Fact]
    public void EquipCost_ColoredTarget_StaysPrintedThree()
    {
        var bear = new Creature("Green Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.GetEffectiveColors().Should().NotBeEmpty("a {G} creature is green");

        var blade = GhostfireBladeFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(blade);

        var equip = blade.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { bear } });

        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        // {2} floating — short of the un-reduced {3}.
        _alice.AddManaToPool(ManaCost.Parse("{2}"));
        mana.CanPay(_alice).Should().BeFalse(
            "a colored target gets no reduction; printed {3} applies");

        _alice.AddManaToPool(ManaCost.Parse("{1}"));
        mana.CanPay(_alice).Should().BeTrue("{3} pays the un-reduced cost");
    }

    [Fact]
    public void ColorlessReducedEquipCost_Helper_FloorsAndConditions()
    {
        // Direct unit on the cost provider — colorless yields {1}, the
        // generic floors correctly and colored stays {3}.
        var golem = new Creature("Golem", "{3}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blade = GhostfireBladeFactory.Create(_alice);
        var equip = blade.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.SetChosenTargets(new[] { new object[] { golem } });

        var effective = GhostfireBladeFactory.ColorlessReducedEquipCost(blade);
        effective.Generic.Should().Be(1, "{3} - {2} = {1} for a colorless target");
    }
}
