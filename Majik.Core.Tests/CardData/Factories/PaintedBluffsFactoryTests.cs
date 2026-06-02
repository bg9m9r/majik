using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PaintedBluffsFactory"/> — Painted Bluffs
/// (Apocalypse). Land — Desert. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Painted Bluffs is structurally a colourless-{C} land (like Survivors'
/// Encampment's first mode) whose any-colour mode costs an additional {1}
/// instead of tapping a creature. The {C} mode + Land — Desert identity are
/// declared in JSON; the five any-colour ({1},{T}) modes are attached in C#
/// because the data-only ManaAbilityDefinition schema carries only a
/// <c>Produces</c> string (no five-colour fan-out, no generic-mana
/// additional cost).
///
/// Covers:
/// - Identity (name, Land, Desert subtype, no mana cost, owner/controller,
///   non-Basic, non-Legendary).
/// - NamedCardFactory dispatch.
/// - Vanilla {T}: Add {C} mana ability (folds to generic per ManaCost.Parse).
/// - Five "{1},{T}: add any color" mana abilities (one per WUBRG).
/// - Activating the any-colour path taps the land AND spends {1}.
/// - CanActivate gates: needs {1} available, needs the land untapped.
/// </summary>
[Trait("Color", "C")]
public class PaintedBluffsFactoryTests
{
    private const string CardName = "Painted Bluffs";

    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PaintedBluffs_Identity()
    {
        var land = PaintedBluffsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be(CardName);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("Type line is 'Land — Desert'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Painted Bluffs is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PaintedBluffs_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create(CardName, _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(CardName);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void PaintedBluffs_HasColorlessManaAbility()
    {
        var land = PaintedBluffsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Where(m => m is not PaintedBluffsManaAbility)
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1
                && m.ManaGenerated.White == 0
                && m.ManaGenerated.Blue == 0
                && m.ManaGenerated.Black == 0
                && m.ManaGenerated.Red == 0
                && m.ManaGenerated.Green == 0,
                "{T}: Add {C} — {C} folds into the generic bucket per ManaCost.Parse");
    }

    [Fact]
    public void PaintedBluffs_ColorlessMana_TapsLand_NeedsNoPayment()
    {
        var land = PaintedBluffsFactory.Create(_alice);

        var colorless = land.Abilities.OfType<ManaAbility>()
            .First(m => m is not PaintedBluffsManaAbility);

        colorless.CanActivate().Should().BeTrue("the {C} ability needs only the land's own {T}");
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void PaintedBluffs_HasFiveAnyColorManaAbilities()
    {
        var land = PaintedBluffsFactory.Create(_alice);

        land.Abilities.OfType<PaintedBluffsManaAbility>().Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void PaintedBluffs_HasOneAnyColorAbilityPerColor(string colorPip)
    {
        var land = PaintedBluffsFactory.Create(_alice);

        land.Abilities.OfType<PaintedBluffsManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void PaintedBluffs_AnyColor_TapsLandAndSpendsOne_ProducesColor(string colorPip)
    {
        var land = PaintedBluffsFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("1")); // a floating {1} to pay the cost

        var ability = land.Abilities.OfType<PaintedBluffsManaAbility>()
            .Single(a => a.ColorPip == colorPip);

        ability.CanActivate().Should().BeTrue("the controller can afford the {1} additional cost");
        var mana = ability.Activate();

        // Exactly one mana of the requested colour, nothing else.
        mana.White.Should().Be(colorPip == "W" ? 1 : 0);
        mana.Blue.Should().Be(colorPip == "U" ? 1 : 0);
        mana.Black.Should().Be(colorPip == "B" ? 1 : 0);
        mana.Red.Should().Be(colorPip == "R" ? 1 : 0);
        mana.Green.Should().Be(colorPip == "G" ? 1 : 0);
        mana.Generic.Should().Be(0);

        land.IsTapped.Should().BeTrue("self-tap is part of the activation cost");
        _alice.ManaPool.Total.Should().Be(0, "the {1} additional cost was spent from the pool");
    }

    [Fact]
    public void PaintedBluffs_AnyColor_CannotActivate_WhenCannotPayOne()
    {
        var land = PaintedBluffsFactory.Create(_alice);
        // No mana in the pool — the {1} additional cost is unpayable.

        var any = land.Abilities.OfType<PaintedBluffsManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "the {1} additional cost cannot be paid from an empty pool");
    }

    [Fact]
    public void PaintedBluffs_AnyColor_CannotActivate_WhenLandTapped()
    {
        var land = PaintedBluffsFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        land.Tap();

        var any = land.Abilities.OfType<PaintedBluffsManaAbility>().First();
        any.CanActivate().Should().BeFalse("the land itself must be untapped to pay {T}");
    }
}
