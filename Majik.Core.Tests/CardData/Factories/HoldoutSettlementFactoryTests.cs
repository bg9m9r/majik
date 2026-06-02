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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HoldoutSettlementFactory"/>.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}, Tap an untapped creature you control: Add one mana of any color."
///
/// Covers:
/// - Land identity (name, Land type, owner/controller).
/// - NamedCardFactory dispatch.
/// - The {C} mana ability (from JSON) — produces one colorless/generic.
/// - Five any-colour mana abilities (one per WUBRG) with the tap-creature cost.
/// - Activation: taps the land AND another untapped creature, produces one
///   coloured mana.
/// - CanActivate false when no eligible creature available.
/// - CanActivate false when the land is already tapped.
/// - Summoning sickness on the only creature blocks the tap-cost path.
/// </summary>
public class HoldoutSettlementFactoryTests
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
    public void HoldoutSettlement_Identity()
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        land.Name.Should().Be("Holdout Settlement");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HoldoutSettlement_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Holdout Settlement", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Holdout Settlement");
    }

    // -----------------------------------------------------------------------
    // {C} mana ability (from JSON)
    // -----------------------------------------------------------------------

    [Fact]
    public void HoldoutSettlement_HasColorlessManaAbility_ProducesC()
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        // The plain {C} ability is the one mana ability that is NOT a
        // HoldoutSettlementManaAbility (those are the five coloured ones).
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(a => a is not HoldoutSettlementManaAbility);

        colorless.CanActivate().Should().BeTrue("the land is untapped and {C} needs no other cost");
        var mana = colorless.Activate();

        // {C} is tracked as colorless/generic mana — no coloured pips.
        mana.Generic.Should().Be(1, "{T}: Add {C}");
        mana.White.Should().Be(0);
        mana.Blue.Should().Be(0);
        mana.Black.Should().Be(0);
        mana.Red.Should().Be(0);
        mana.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("{T} is the activation cost of the {C} ability");
    }

    [Fact]
    public void HoldoutSettlement_ColorlessAbility_NeedsNoCreature()
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        // No creatures in play at all.
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(a => a is not HoldoutSettlementManaAbility);

        colorless.CanActivate().Should()
            .BeTrue("{T}: Add {C} has no tap-a-creature additional cost");
    }

    // -----------------------------------------------------------------------
    // Any-colour mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HoldoutSettlement_HasFiveAnyColorManaAbilities()
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        land.Abilities.OfType<HoldoutSettlementManaAbility>().Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void HoldoutSettlement_HasOneAbilityPerColor(string colorPip)
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        land.Abilities.OfType<HoldoutSettlementManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    // -----------------------------------------------------------------------
    // Any-colour activation
    // -----------------------------------------------------------------------

    [Fact]
    public void HoldoutSettlement_TapForBlue_TapsLandAndCreature_ProducesU()
    {
        var land = HoldoutSettlementFactory.Create(_alice);
        var bear = ReadyBear();

        var blue = land.Abilities.OfType<HoldoutSettlementManaAbility>()
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
    public void HoldoutSettlement_FallsBack_ToFirstEligibleCreature_WhenNoTargetSet()
    {
        var land = HoldoutSettlementFactory.Create(_alice);
        var bear = ReadyBear();

        var green = land.Abilities.OfType<HoldoutSettlementManaAbility>()
            .Single(a => a.ColorPip == "G");

        // Target intentionally unset — deterministic first-eligible fallback.
        var mana = green.Activate();

        mana.Green.Should().Be(1);
        bear.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CanActivate gates
    // -----------------------------------------------------------------------

    [Fact]
    public void HoldoutSettlement_AnyColor_CannotActivate_WhenNoOtherCreature()
    {
        var land = HoldoutSettlementFactory.Create(_alice);

        var any = land.Abilities.OfType<HoldoutSettlementManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "the tap-an-untapped-creature cost cannot be paid without an eligible creature");
    }

    [Fact]
    public void HoldoutSettlement_AnyColor_CannotActivate_WhenLandTapped()
    {
        var land = HoldoutSettlementFactory.Create(_alice);
        ReadyBear();
        land.Tap();

        var any = land.Abilities.OfType<HoldoutSettlementManaAbility>().First();
        any.CanActivate().Should().BeFalse("the land itself must be untapped to pay {T}");
    }

    [Fact]
    public void HoldoutSettlement_AnyColor_CannotActivate_WhenOnlyCreature_HasSummoningSickness()
    {
        var land = HoldoutSettlementFactory.Create(_alice);
        var sick = new Creature("Wurm", "5G", 5, 5);
        sick.SetOwner(_alice);
        sick.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sick);
        // Summoning sickness is the default on Permanent — do NOT clear.

        var any = land.Abilities.OfType<HoldoutSettlementManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "a summoning-sick creature cannot be tapped to pay a tap-cost");
    }
}
