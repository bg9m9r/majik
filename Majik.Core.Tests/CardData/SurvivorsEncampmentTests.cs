using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SurvivorsEncampmentFactory"/>.
///
/// Survivors' Encampment (Hour of Devastation) is the functional twin of
/// Holdout Settlement — a Land — Desert. Oracle text (verified against
/// Scryfall):
///   "{T}: Add {C}.
///    {T}, Tap an untapped creature you control: Add one mana of any color."
///
/// Covers:
/// - Identity (name, Land, Desert subtype, no mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Vanilla {T}: Add {C} mana ability (folds to generic per ManaCost.Parse).
/// - Five "tap a creature: add any color" mana abilities (one per WUBRG),
///   reusing the Springleaf Drum any-color ability shape.
/// - Activation of the any-color path taps the land AND another creature.
/// - CanActivate gates for the any-color path.
/// </summary>
public class SurvivorsEncampmentTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature ReadyBear()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ClearSummoningSickness();
        return bear;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SurvivorsEncampment_Identity()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        land.Name.Should().Be("Survivors' Encampment");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("Type line is 'Land — Desert'");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SurvivorsEncampment_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Survivors' Encampment", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Survivors' Encampment");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SurvivorsEncampment_HasColorlessManaAbility()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Where(m => m is not SpringleafDrumManaAbility)
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1
                && m.ManaGenerated.White == 0
                && m.ManaGenerated.Blue == 0
                && m.ManaGenerated.Black == 0
                && m.ManaGenerated.Red == 0
                && m.ManaGenerated.Green == 0,
                "{T}: Add {C} — {C} folds into the generic bucket per ManaCost.Parse");
    }

    [Fact]
    public void SurvivorsEncampment_ColorlessMana_TapsLand_NeedsNoCreature()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        var colorless = land.Abilities.OfType<ManaAbility>()
            .First(m => m is not SpringleafDrumManaAbility);

        colorless.CanActivate().Should().BeTrue("the {C} ability needs only the land's own {T}");
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}, Tap an untapped creature you control: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void SurvivorsEncampment_HasFiveAnyColorManaAbilities()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        land.Abilities.OfType<SpringleafDrumManaAbility>().Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void SurvivorsEncampment_HasOneAnyColorAbilityPerColor(string colorPip)
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        land.Abilities.OfType<SpringleafDrumManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    [Fact]
    public void SurvivorsEncampment_TapForBlue_TapsLandAndCreature_ProducesU()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);
        var bear = ReadyBear();

        var blue = land.Abilities.OfType<SpringleafDrumManaAbility>()
            .Single(a => a.ColorPip == "U");
        blue.TapChoice.Target = bear;

        blue.CanActivate().Should().BeTrue();
        var mana = blue.Activate();

        mana.Blue.Should().Be(1, "{T}+tap-creature: Add one mana of any color — here U");
        mana.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue("self-tap is part of the activation cost");
        bear.IsTapped.Should().BeTrue("the tap-another-creature cost taps the bear");
    }

    [Fact]
    public void SurvivorsEncampment_AnyColor_CannotActivate_WhenNoOtherCreature()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);

        var any = land.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "the tap-an-untapped-creature cost cannot be paid without an eligible creature");
    }

    [Fact]
    public void SurvivorsEncampment_AnyColor_CannotActivate_WhenLandTapped()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);
        ReadyBear();
        land.Tap();

        var any = land.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse("the land itself must be untapped to pay {T}");
    }

    [Fact]
    public void SurvivorsEncampment_AnyColor_CannotActivate_WhenOnlyCreature_HasSummoningSickness()
    {
        var land = SurvivorsEncampmentFactory.Create(_alice);
        var sick = new Creature("Wurm", "5G", 5, 5);
        sick.SetOwner(_alice);
        sick.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sick);
        // Summoning sickness is the default on Permanent — do NOT clear.

        var any = land.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "a summoning-sick creature cannot be tapped to pay a tap-cost");
    }
}
