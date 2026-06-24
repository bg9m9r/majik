using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LeylineAxeFactory"/>.
///
/// Card: Leyline Axe — Artifact — Equipment {4} (Duskmourn: House of
/// Horror).
///   "If this card is in your opening hand, you may begin the game with it
///    on the battlefield."
///   "Equipped creature gets +1/+1 and has double strike and trample."
///   "Equip {3}"
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, type, subtype, mana cost) — single _Identity assert.
///   - Leyline opening-hand marker keyword (shared subscriber hook).
///   - Equip {3} cost.
///   - Static +1/+1 + Double strike + Trample while equipped.
///   - Detach revokes the boost + granted keywords.
/// </summary>
[Trait("Color", "C")]
public class LeylineAxeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LeylineAxe_Identity()
    {
        var c = LeylineAxeFactory.Create(_alice);

        c.Name.Should().Be("Leyline Axe");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeylineAxe_HasOpeningHandLeylineMarker()
    {
        var c = LeylineAxeFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword);
        keywords.Should().Contain(
            OpeningHandLeylineAlternativeCost.LeylineKeyword,
            "the shared Leyline opening-hand subscriber keys off this marker");
    }

    [Fact]
    public void LeylineAxe_EquipAbility_HasGenericThreeCost()
    {
        var c = LeylineAxeFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(3, "Equip {3} is the printed cost");
    }

    [Fact]
    public void LeylineAxe_Equipped_Bear_GetsPlusOnePlusOneAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var axe = LeylineAxeFactory.Create(_alice, svc);
        axe.Zone = ZoneType.Battlefield;

        axe.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1/+1 boost from Leyline Axe");
        bear.GetToughness().Should().Be(3);
        CombatAbilities.HasDoubleStrike(bear).Should().BeTrue(
            "Leyline Axe grants Double strike at Layer 6");
        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "Leyline Axe grants Trample at Layer 6");
    }

    [Fact]
    public void LeylineAxe_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var axe = LeylineAxeFactory.Create(_alice, svc);
        axe.Zone = ZoneType.Battlefield;
        axe.AttachTo(bear);

        axe.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse();
        CombatAbilities.HasTrample(bear).Should().BeFalse();
    }
}
