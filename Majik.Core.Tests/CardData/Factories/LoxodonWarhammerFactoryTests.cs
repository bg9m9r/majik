using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LoxodonWarhammerFactory"/>.
///
/// Card: Loxodon Warhammer — Artifact — Equipment {3} (Mirrodin).
///   "Equipped creature gets +3/+0 and has trample and lifelink."
///   "Equip {3}"
///
/// Same mechanical shape as <see cref="ShadowspearFactory"/>'s static
/// "+1/+1 and has trample and lifelink" line, differing only in the boost
/// magnitude (+3/+0), the equip cost ({3}), the absence of the legendary
/// supertype, and the absence of the keyword-strip activated ability.
/// </summary>
[Trait("Color", "C")]
public class LoxodonWarhammerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LoxodonWarhammer_Identity()
    {
        var c = LoxodonWarhammerFactory.Create(_alice);

        c.Name.Should().Be("Loxodon Warhammer");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Loxodon Warhammer is an Equipment");
        c.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Loxodon Warhammer is not legendary");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LoxodonWarhammer_EquipAbility_HasGenericThreeCost()
    {
        var c = LoxodonWarhammerFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(3, "Equip {3} is the printed cost");
    }

    [Fact]
    public void LoxodonWarhammer_Equipped_Bear_GetsPlusThreePowerAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var hammer = LoxodonWarhammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;

        hammer.AttachTo(bear);

        bear.GetPower().Should().Be(5, "+3/+0 boost from Loxodon Warhammer");
        bear.GetToughness().Should().Be(2, "Warhammer adds +0 toughness");
        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "Loxodon Warhammer grants Trample at Layer 6");
        CombatAbilities.HasLifelink(bear).Should().BeTrue(
            "Loxodon Warhammer grants Lifelink at Layer 6");
    }

    [Fact]
    public void LoxodonWarhammer_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var hammer = LoxodonWarhammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;
        hammer.AttachTo(bear);

        hammer.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasTrample(bear).Should().BeFalse();
        CombatAbilities.HasLifelink(bear).Should().BeFalse();
    }

    [Fact]
    public void LoxodonWarhammer_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var hammer = LoxodonWarhammerFactory.Create(_alice, svc);
        hammer.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Warhammer's effect gates on AttachedTo");
        CombatAbilities.HasTrample(bear).Should().BeFalse();
        CombatAbilities.HasLifelink(bear).Should().BeFalse();
    }
}
