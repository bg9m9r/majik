using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShimmerdriftValeFactory"/> — Shimmerdrift Vale
/// (Kaldheim, Snow Land). Oracle text:
///   "This land enters tapped.
///    As this land enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// Modelled after <see cref="CinderBarrensFactory"/> (unconditional
/// <see cref="EntersTappedReplacement"/> registered when a
/// <see cref="ReplacementBus"/> is supplied) combined with the
/// "choose a color as this enters" up-front-resolution posture of
/// <see cref="TempleOfTheDragonQueenFactory"/> (CR 614.12).
///
/// Covers:
/// - Identity (Snow supertype, non-Basic).
/// - The shape-only single-arg path produces no mana ability (the chosen
///   colour isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — exactly one ManaAbility, of the
///   chosen colour, once a colour is supplied (CR 605.1a).
/// - Unconditional enters-tapped (CR 614.1c): registered on the bus.
/// - Colourless chosen colour throws.
/// </summary>
[Trait("Color", "C")]
public class ShimmerdriftValeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmerdriftVale_IsSnow_AndNotBasic()
    {
        var land = ShimmerdriftValeFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Shimmerdrift Vale is a Snow Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void ShimmerdriftVale_SingleArgPath_HasNoManaAbilityYet_AndNoOtherAbilities()
    {
        // No colour chosen yet => no {T}: Add ability; nothing else either.
        var land = ShimmerdriftValeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "the chosen colour isn't known on the shape-only path");
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of the chosen color (CR 605.1a)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ManaColor.White, "W")]
    [InlineData(ManaColor.Blue, "U")]
    [InlineData(ManaColor.Black, "B")]
    [InlineData(ManaColor.Red, "R")]
    [InlineData(ManaColor.Green, "G")]
    public void ShimmerdriftVale_ChosenColor_ProducesExactlyThatColor(ManaColor chosen, string pip)
    {
        var land = ShimmerdriftValeFactory.Create(
            _alice, chosenColor: chosen, replacements: null);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "{T}: Add one mana of the chosen color");

        var expected = ManaCost.Parse(pip);
        var produced = mana[0].ManaGenerated;
        produced.White.Should().Be(expected.White);
        produced.Blue.Should().Be(expected.Blue);
        produced.Black.Should().Be(expected.Black);
        produced.Red.Should().Be(expected.Red);
        produced.Green.Should().Be(expected.Green);
    }

    // -----------------------------------------------------------------------
    // Enters tapped (CR 614.1c) — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmerdriftVale_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = ShimmerdriftValeFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("\"This land enters tapped\" is unconditional");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmerdriftVale_Create_ThrowsOnNullOwner()
    {
        var act = () => ShimmerdriftValeFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ShimmerdriftVale_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => ShimmerdriftValeFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
