using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PhyrexianAltarFactory"/>.
///
/// Covers:
/// - Card identity (Artifact, mana cost {3}).
/// - Five tapless mana abilities (one per WUBRG), each with a shared
///   sacrifice-another-creature cost.
/// - Activation: sacrifice fodder + produces one mana of the chosen
///   colour. Phyrexian Altar does NOT tap.
/// - Cannot activate when no creature is available to sacrifice.
/// </summary>
public class PhyrexianAltarTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PhyrexianAltar_IsArtifact()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        altar.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void PhyrexianAltar_NameAndCost()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        altar.Name.Should().Be("Phyrexian Altar");
        altar.ManaCost.Should().Be("{3}");
    }

    [Fact]
    public void PhyrexianAltar_OwnerAndControllerAreSet()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        altar.Owner.Should().BeSameAs(_alice);
        altar.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhyrexianAltar_HasFiveManaAbilities_OnePerColor()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        altar.Abilities.OfType<PhyrexianAltarManaAbility>().Should().HaveCount(5);
    }

    [Fact]
    public void PhyrexianAltar_AllAbilities_ShareSacrificeCost()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        var abilities = altar.Abilities.OfType<PhyrexianAltarManaAbility>().ToList();
        var firstCost = abilities[0].SacrificeChoice;
        foreach (var ab in abilities)
        {
            ab.SacrificeChoice.Should().BeSameAs(firstCost,
                "single SacrificeAnotherCreatureCost shared across all five colour abilities");
        }
    }

    [Fact]
    public void PhyrexianAltar_CannotActivate_WhenNoCreatureToSacrifice()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(altar);
        altar.SetZone(ZoneType.Battlefield);

        var ability = altar.Abilities.OfType<PhyrexianAltarManaAbility>().First();
        ability.CanActivate().Should().BeFalse(
            "no creature on the battlefield to sacrifice");
    }

    [Fact]
    public void PhyrexianAltar_Activate_ProducesOneMana_AndSacrificesFodder()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(altar);
        altar.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Fodder", "1B", 1, 1);
        fodder.SetOwner(_alice); fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        // Pick the {U} ability.
        var blueAbility = altar.Abilities
            .OfType<PhyrexianAltarManaAbility>()
            .First(a => a.ManaGenerated.Blue == 1);

        blueAbility.SacrificeChoice.Target = fodder;
        blueAbility.CanActivate().Should().BeTrue();

        var mana = blueAbility.Activate();

        mana.Blue.Should().Be(1);
        mana.Generic.Should().Be(0);

        altar.IsTapped.Should().BeFalse(
            "Phyrexian Altar's ability does not include {T} — tapsAsCost=false");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public void PhyrexianAltar_AllFiveColors_Producible()
    {
        var altar = PhyrexianAltarFactory.Create(_alice);
        var abilities = altar.Abilities.OfType<PhyrexianAltarManaAbility>().ToList();

        // Exactly one ability per WUBRG colour.
        abilities.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        abilities.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }
}
