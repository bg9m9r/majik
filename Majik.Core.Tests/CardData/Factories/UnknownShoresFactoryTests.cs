using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="UnknownShoresFactory"/>.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Covers ONLY Unknown Shores' unique behaviour (the contract test asserts
/// dispatch + well-formedness for every implemented card):
/// - The {C} mana ability (from JSON) — produces one generic, no extra cost.
/// - Five any-colour mana abilities (one per WUBRG) with the {1} generic
///   additional cost.
/// - Activation: taps the land, pays {1}, produces one coloured mana.
/// - CanActivate false when the {1} is unaffordable.
/// - CanActivate false when the land is already tapped.
/// </summary>
[Trait("Color", "C")]
public class UnknownShoresFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static readonly ManaCost One = ManaCost.Parse("1");

    // The {C} ability is the only ManaAbility with no additional cost — it is
    // the one that can activate on an untapped land with an empty pool. The
    // five any-colour abilities all require {1}.
    private static ManaAbility ColorlessAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 1);

    private static System.Collections.Generic.List<ManaAbility> AnyColorAbilities(Land land) =>
        [.. land.Abilities.OfType<ManaAbility>().Where(a => a.ManaGenerated.Generic == 0)];

    // -----------------------------------------------------------------------
    // {C} mana ability (from JSON)
    // -----------------------------------------------------------------------

    [Fact]
    public void UnknownShores_HasColorlessManaAbility_ProducesC_NoExtraCost()
    {
        var land = UnknownShoresFactory.Create(_alice);

        var colorless = ColorlessAbility(land);

        // Empty pool: the {C} ability needs no {1}, so it can activate.
        colorless.CanActivate().Should().BeTrue("{T}: Add {C} has no additional cost");
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1, "{T}: Add {C}");
        mana.White.Should().Be(0);
        mana.Blue.Should().Be(0);
        mana.Black.Should().Be(0);
        mana.Red.Should().Be(0);
        mana.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("{T} is the activation cost of the {C} ability");
    }

    // -----------------------------------------------------------------------
    // Any-colour mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void UnknownShores_HasFiveAnyColorManaAbilities()
    {
        var land = UnknownShoresFactory.Create(_alice);

        AnyColorAbilities(land).Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void UnknownShores_HasOneAbilityPerColor(string colorPip)
    {
        var land = UnknownShoresFactory.Create(_alice);
        var expected = ManaCost.Parse(colorPip);

        AnyColorAbilities(land).Should()
            .ContainSingle(a =>
                a.ManaGenerated.White == expected.White &&
                a.ManaGenerated.Blue == expected.Blue &&
                a.ManaGenerated.Black == expected.Black &&
                a.ManaGenerated.Red == expected.Red &&
                a.ManaGenerated.Green == expected.Green);
    }

    // -----------------------------------------------------------------------
    // Any-colour activation — taps land, pays {1}, produces the colour
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void UnknownShores_TapForColor_PaysOne_TapsLand_ProducesColor(string colorPip)
    {
        var land = UnknownShoresFactory.Create(_alice);
        _alice.AddManaToPool(One); // one generic to pay {1}

        var expected = ManaCost.Parse(colorPip);
        var ability = AnyColorAbilities(land).Single(a =>
            a.ManaGenerated.White == expected.White &&
            a.ManaGenerated.Blue == expected.Blue &&
            a.ManaGenerated.Black == expected.Black &&
            a.ManaGenerated.Red == expected.Red &&
            a.ManaGenerated.Green == expected.Green);

        ability.CanActivate().Should().BeTrue("the land is untapped and {1} is affordable");
        var mana = ability.Activate();

        mana.White.Should().Be(expected.White);
        mana.Blue.Should().Be(expected.Blue);
        mana.Black.Should().Be(expected.Black);
        mana.Red.Should().Be(expected.Red);
        mana.Green.Should().Be(expected.Green);
        mana.Generic.Should().Be(0, "the any-colour mode adds exactly one coloured mana");

        land.IsTapped.Should().BeTrue("{T} is part of the activation cost");
        _alice.ManaPool.Generic.Should().Be(0, "the {1} additional cost was spent");
    }

    // -----------------------------------------------------------------------
    // CanActivate gates
    // -----------------------------------------------------------------------

    [Fact]
    public void UnknownShores_AnyColor_CannotActivate_WhenOneUnaffordable()
    {
        var land = UnknownShoresFactory.Create(_alice);
        // Empty pool — the {1} cannot be paid.

        var any = AnyColorAbilities(land).First();
        any.CanActivate().Should()
            .BeFalse("the {1} generic additional cost is unaffordable");
        land.IsTapped.Should().BeFalse("an illegal activation does not tap the land");
    }

    [Fact]
    public void UnknownShores_AnyColor_CannotActivate_WhenLandTapped()
    {
        var land = UnknownShoresFactory.Create(_alice);
        _alice.AddManaToPool(One);
        land.Tap();

        var any = AnyColorAbilities(land).First();
        any.CanActivate().Should()
            .BeFalse("the land itself must be untapped to pay {T}");
    }

    // -----------------------------------------------------------------------
    // Identity — colourless non-basic land
    // -----------------------------------------------------------------------

    [Fact]
    public void UnknownShores_Identity()
    {
        var land = UnknownShoresFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.ManaCostValue.IsZero.Should()
            .BeTrue("Unknown Shores has no mana cost (it is a land)");
    }
}
