using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DarksteelAxeFactory"/>.
///
/// Card: Darksteel Axe — Artifact — Equipment {1} (Aether Revolt).
///   "Indestructible (Effects that say \"destroy\" don't destroy this
///    Equipment.)"
///   "Equipped creature gets +2/+0."
///   "Equip {2}"
///
/// The indestructible cousin of <see cref="BonesplitterFactory"/>: same flat
/// +2/+0 buff, but Equip {2} and the intrinsic Indestructible keyword
/// (CR 702.12). Tests cover the UNIQUE behaviour — the printed Indestructible
/// marker + the +2/+0 equip — plus a single identity assert.
/// </summary>
[Trait("Color", "C")]
public class DarksteelAxeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DarksteelAxe_Identity()
    {
        var c = DarksteelAxeFactory.Create(_alice);

        c.Name.Should().Be("Darksteel Axe");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Darksteel Axe is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DarksteelAxe_HasPrintedIndestructibleKeyword()
    {
        // CR 702.12 — the destroy gate (OracleSpellBinder.HasIndestructible)
        // reads this KeywordAbility marker off the non-creature Equipment and
        // cancels "destroy" effects against the Axe itself.
        var c = DarksteelAxeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelAxe_EquipAbility_HasGenericTwoCost()
    {
        var c = DarksteelAxeFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void DarksteelAxe_Equipped_Bear_Becomes_4_2()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var axe = DarksteelAxeFactory.Create(_alice, svc);
        axe.Zone = ZoneType.Battlefield;

        axe.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+0 boost from Darksteel Axe");
        bear.GetToughness().Should().Be(2, "Darksteel Axe adds +0 toughness");
    }

    [Fact]
    public void DarksteelAxe_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var axe = DarksteelAxeFactory.Create(_alice, svc);
        axe.Zone = ZoneType.Battlefield;
        axe.AttachTo(bear);

        bear.GetPower().Should().Be(4);

        axe.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }
}
