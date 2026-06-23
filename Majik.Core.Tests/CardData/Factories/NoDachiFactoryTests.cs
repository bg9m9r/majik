using System.Linq;
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
/// Unit tests for <see cref="NoDachiFactory"/>.
///
/// Card: No-Dachi — Artifact — Equipment {2} (Champions of Kamigawa).
///   "Equipped creature gets +2/+0 and has first strike."
///   "Equip {3}"
///
/// Same paired-grant equip shape as <see cref="ONaginataFactory"/> (+P/+0
/// boost + keyword grant); the unique behaviour is the +2/+0 magnitude paired
/// with a granted First Strike keyword (CR 702.7) and the Equip {3} cost.
/// </summary>
[Trait("Color", "C")]
public class NoDachiFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NoDachi_Identity()
    {
        var c = NoDachiFactory.Create(_alice);

        c.Name.Should().Be("No-Dachi");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "No-Dachi is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NoDachi_EquipAbility_HasGenericThreeCost()
    {
        var c = NoDachiFactory.Create(_alice);

        var ability = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(3,
            "Equip {3} is the printed activation cost");
    }

    [Fact]
    public void NoDachi_Equipped_Gives_Plus2_0_And_FirstStrike()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var noDachi = NoDachiFactory.Create(_alice, svc);
        noDachi.Zone = ZoneType.Battlefield;

        noDachi.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+0 boost from No-Dachi");
        bear.GetToughness().Should().Be(2, "No-Dachi adds +0 toughness");
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "No-Dachi grants first strike to the equipped creature");
    }

    [Fact]
    public void NoDachi_Detach_RestoresPT_And_RemovesFirstStrike()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var noDachi = NoDachiFactory.Create(_alice, svc);
        noDachi.Zone = ZoneType.Battlefield;
        noDachi.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();

        noDachi.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "the granted first strike lapses with the attachment");
    }

    [Fact]
    public void NoDachi_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var noDachi = NoDachiFactory.Create(_alice, svc);
        noDachi.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped No-Dachi's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "an unattached No-Dachi grants nothing");
    }
}
