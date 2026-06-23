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
/// Unit tests for <see cref="UnchartedHavenFactory"/> — Uncharted Haven
/// (Bloomburrow, Land). Oracle text:
///   "This land enters tapped. As it enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// The colourless twin of <see cref="ShimmerdriftValeFactory"/> — same
/// unconditional <see cref="EntersTappedReplacement"/> + "choose a color as it
/// enters" up-front-resolution posture (CR 614.12), but a bare Land (no Snow
/// supertype).
///
/// Covers:
/// - Identity (bare Land — neither Snow nor Basic).
/// - The shape-only single-arg path produces no mana ability (the chosen
///   colour isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — exactly one ManaAbility, of the
///   chosen colour, once a colour is supplied (CR 605.1a).
/// - Unconditional enters-tapped (CR 614.1c): registered on the bus.
/// - Colourless chosen colour throws.
/// </summary>
[Trait("Color", "C")]
public class UnchartedHavenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UnchartedHaven_IsBareLand_NotSnowNorBasic()
    {
        var land = UnchartedHavenFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Snow).Should().BeFalse(
            "Uncharted Haven is a bare Land, not Snow");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void UnchartedHaven_SingleArgPath_HasNoManaAbilityYet_AndNoOtherAbilities()
    {
        // No colour chosen yet => no {T}: Add ability; nothing else either.
        var land = UnchartedHavenFactory.Create(_alice);

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
    public void UnchartedHaven_ChosenColor_ProducesExactlyThatColor(ManaColor chosen, string pip)
    {
        var land = UnchartedHavenFactory.Create(
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
    public void UnchartedHaven_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = UnchartedHavenFactory.Create(
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
    public void UnchartedHaven_Create_ThrowsOnNullOwner()
    {
        var act = () => UnchartedHavenFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnchartedHaven_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => UnchartedHavenFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
