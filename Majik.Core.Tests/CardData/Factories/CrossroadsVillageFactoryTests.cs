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
/// Unit tests for <see cref="CrossroadsVillageFactory"/> — Crossroads Village
/// (Edge of Eternities, Land — Town). Oracle text:
///   "This land enters tapped.
///    As it enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// Mechanically identical to <see cref="ShimmerdriftValeFactory"/> (the
/// choose-a-colour tapland family) — only the printed land subtype differs:
/// <c>Town</c> (CR 205.3m) here vs Shimmerdrift Vale's Snow supertype.
///
/// Covers this card's unique shell:
/// - Identity (Land + the printed <c>Town</c> subtype, CR 205.3m).
/// - The shape-only single-arg path produces no mana ability (the chosen
///   colour isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — exactly one ManaAbility, of the
///   chosen colour, once a colour is supplied (CR 605.1a).
/// - Unconditional enters-tapped (CR 614.1c): registered on the bus.
/// - Colourless chosen colour throws.
///
/// Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "C")]
public class CrossroadsVillageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CrossroadsVillage_Identity_IsLandWithTownSubtype()
    {
        var land = CrossroadsVillageFactory.Create(_alice);
        land.Subtypes.Should().Contain(CardSubtype.Town,
            "the printed land subtype is Town (CR 205.3m)");
    }

    [Fact]
    public void CrossroadsVillage_SingleArgPath_HasNoManaAbilityYet_AndNoOtherAbilities()
    {
        // No colour chosen yet => no {T}: Add ability; nothing else either.
        var land = CrossroadsVillageFactory.Create(_alice);

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
    public void CrossroadsVillage_ChosenColor_ProducesExactlyThatColor(ManaColor chosen, string pip)
    {
        var land = CrossroadsVillageFactory.Create(
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
    public void CrossroadsVillage_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = CrossroadsVillageFactory.Create(
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
    public void CrossroadsVillage_Create_ThrowsOnNullOwner()
    {
        var act = () => CrossroadsVillageFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CrossroadsVillage_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => CrossroadsVillageFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
