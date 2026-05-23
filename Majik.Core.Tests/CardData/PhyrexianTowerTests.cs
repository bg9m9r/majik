using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PhyrexianTowerFactory"/>.
///
/// Covers:
/// - Card identity (name, Legendary Land).
/// - NamedCardFactory dispatch.
/// - Two mana abilities present ({C} and {B}{B}).
/// - {T}: Add {C} — produces colorless.
/// - {T} + sacrifice: Add {B}{B} — produces {B}{B}, sacrificed creature
///   moves to graveyard, land becomes tapped.
/// - {T} + sacrifice cannot be activated when controller has no other
///   creature on the battlefield.
/// </summary>
public class PhyrexianTowerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianTower_IsLegendaryLand()
    {
        var land = PhyrexianTowerFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        land.Name.Should().Be("Phyrexian Tower");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PhyrexianTower()
    {
        var card = NamedCardFactory.Create("Phyrexian Tower", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Phyrexian Tower");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianTower_HasExactlyTwoManaAbilities()
    {
        var land = PhyrexianTowerFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {T}: Add {C} and one for {T}, Sacrifice a creature: Add {B}{B}");
    }

    [Fact]
    public void PhyrexianTower_HasColorlessManaAbility()
    {
        var land = PhyrexianTowerFactory.Create(_alice);

        // Vanilla colorless mana ability: not the sacrifice subtype.
        // {C} is bucketed as Generic +1 in ManaCost.Parse (no dedicated
        // colorless slot today — see ManaCost.cs).
        land.Abilities.OfType<ManaAbility>()
            .Where(m => m is not PhyrexianTowerManaAbility)
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1
                                      && m.ManaGenerated.Black == 0,
                "first ability is {T}: Add {C}");
    }

    [Fact]
    public void PhyrexianTower_HasSacrificeBlackBlackManaAbility()
    {
        var land = PhyrexianTowerFactory.Create(_alice);

        var sacAbility = land.Abilities.OfType<PhyrexianTowerManaAbility>().Single();
        sacAbility.ManaGenerated.Black.Should().Be(2);
        sacAbility.ManaGenerated.Generic.Should().Be(0);
        sacAbility.SacrificeChoice.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Tap-for-colorless behavior
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianTower_TapForColorless_ProducesC_AndTapsLand()
    {
        var land = PhyrexianTowerFactory.Create(_alice);
        var colorless = land.Abilities.OfType<ManaAbility>()
            .First(m => m is not PhyrexianTowerManaAbility);

        colorless.CanActivate().Should().BeTrue();
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1);
        mana.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Tap + sacrifice for {B}{B} behavior
    // -----------------------------------------------------------------------

    [Fact]
    public void PhyrexianTower_TapAndSacrifice_ProducesBB_AndCreatureGoesToGraveyard()
    {
        var land = PhyrexianTowerFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);

        var sacAbility = land.Abilities.OfType<PhyrexianTowerManaAbility>().Single();
        sacAbility.SacrificeChoice.Target = fodder;

        sacAbility.CanActivate().Should().BeTrue();
        var mana = sacAbility.Activate();

        mana.Black.Should().Be(2);
        mana.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public void PhyrexianTower_SacrificeAbility_CannotActivate_WhenNoOtherCreature()
    {
        var land = PhyrexianTowerFactory.Create(_alice);
        // Alice controls no creatures — the sac cost cannot be paid.
        var sacAbility = land.Abilities.OfType<PhyrexianTowerManaAbility>().Single();

        sacAbility.CanActivate().Should().BeFalse(
            "the controller has no other creature to sacrifice");
    }

    [Fact]
    public void PhyrexianTower_SacrificeAbility_CannotActivate_WhenLandTapped()
    {
        var land = PhyrexianTowerFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        land.Tap();

        var sacAbility = land.Abilities.OfType<PhyrexianTowerManaAbility>().Single();

        sacAbility.CanActivate().Should().BeFalse("the land is already tapped");
    }
}
