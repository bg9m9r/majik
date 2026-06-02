using System;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Nykthos, Shrine to Nyx (Theros) — Legendary Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: Choose a color. Add an amount of mana of that color equal to
///    your devotion to that color. (Your devotion to a color is the number of
///    mana symbols of that color in the mana costs of permanents you control.)"
///
/// Exercises:
///   * Card shape: Legendary Land, no subtypes, named "Nykthos, Shrine to Nyx".
///   * Dispatch through <see cref="NamedCardFactory"/> returns a Land.
///   * Shape-only path wires only the JSON "{T}: Add {C}" mana ability.
///   * Full overload wires the second {2},{T} devotion mana ability.
///   * Devotion-to-a-color computation across all five colors (CR 700.5).
///   * Activate: devotion N → adds N pips of the chosen color, pays {2}, taps.
///   * Activate with devotion 0 → legal, adds no mana, still pays {2}.
///   * CanActivate guards: tapped land / cannot afford {2}.
///   * Colorless is not a color (CR 105.1) → rejected.
/// </summary>
[Trait("Color", "C")]
public class NykthosShrineToNyxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Land PlaceOnBattlefield(ManaColor chosenColor)
    {
        var nykthos = NykthosShrineToNyxFactory.Create(_alice, chosenColor);
        nykthos.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(nykthos);
        return nykthos;
    }

    private Permanent AddPermanent(string name, string manaCost)
    {
        var creature = new Creature(name, manaCost, power: 1, toughness: 1)
            { Owner = _alice, Controller = _alice };
        creature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    // -----------------------------------------------------------------------
    // Card shape + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLegendaryLand_NamedNykthos()
    {
        var nykthos = NykthosShrineToNyxFactory.Create(_alice);

        nykthos.Name.Should().Be("Nykthos, Shrine to Nyx");
        nykthos.HasType(CardType.Land).Should().BeTrue();
        nykthos.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        nykthos.Owner.Should().BeSameAs(_alice);
        nykthos.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Nykthos, Shrine to Nyx", _alice);
        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Nykthos, Shrine to Nyx");
    }

    // -----------------------------------------------------------------------
    // First ability: {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_HasSingleColorlessManaAbility()
    {
        var nykthos = NykthosShrineToNyxFactory.Create(_alice);

        nykthos.Abilities.Should().HaveCount(1,
            because: "the shape-only path wires only the JSON \"{T}: Add {C}\" ability");
        nykthos.Abilities[0].Should().BeAssignableTo<IManaAbility>();
    }

    [Fact]
    public void FirstAbility_AddsOneColorless()
    {
        var nykthos = PlaceOnBattlefield(ManaColor.Red);

        var colorless = (IManaAbility)nykthos.Abilities[0];
        var mana = colorless.Activate();

        // {C} has no dedicated bucket — it is modelled as +1 generic.
        mana.Generic.Should().Be(1, because: "{T}: Add {C} produces one colorless pip");
        mana.White.Should().Be(0);
        nykthos.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Full overload wires the second ability
    // -----------------------------------------------------------------------

    [Fact]
    public void FullOverload_HasTwoManaAbilities()
    {
        var nykthos = NykthosShrineToNyxFactory.Create(_alice, ManaColor.Green);
        nykthos.Abilities.Should().HaveCount(2,
            because: "{T}: Add {C} plus the {2},{T} devotion ability");
        nykthos.Abilities[0].Should().BeAssignableTo<IManaAbility>();
        nykthos.Abilities[1].Should().BeAssignableTo<IManaAbility>();
    }

    // -----------------------------------------------------------------------
    // Devotion to a color (CR 700.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeDevotion_CountsColoredPipsAcrossControlledPermanents()
    {
        PlaceOnBattlefield(ManaColor.Green); // {C}/{} — no green pip on the land itself
        AddPermanent("Elvish Mystic", "{G}");        // +1 green
        AddPermanent("Llanowar Elves", "{G}");       // +1 green
        AddPermanent("Leatherback Baloth", "{G}{G}{G}"); // +3 green
        AddPermanent("Doom Blade Dummy", "{1}{B}");  // +1 black, 0 green

        NykthosShrineToNyxFactory.ComputeDevotionToColor(_alice, ManaColor.Green)
            .Should().Be(5, because: "1 + 1 + 3 green pips = devotion 5");
        NykthosShrineToNyxFactory.ComputeDevotionToColor(_alice, ManaColor.Black)
            .Should().Be(1, because: "one {B} pip across controlled permanents");
        NykthosShrineToNyxFactory.ComputeDevotionToColor(_alice, ManaColor.White)
            .Should().Be(0);
    }

    [Fact]
    public void ComputeDevotion_NullPlayer_IsZero()
    {
        NykthosShrineToNyxFactory.ComputeDevotionToColor(null!, ManaColor.Red)
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Second ability: {2},{T}: add devotion-to-color mana
    // -----------------------------------------------------------------------

    [Fact]
    public void SecondAbility_DevotionThree_AddsThreeOfColor_PaysTwo_TapsLand()
    {
        var nykthos = PlaceOnBattlefield(ManaColor.Red);
        AddPermanent("Goblin Guide", "{R}");
        AddPermanent("Lightning Bolt Body", "{R}{R}"); // permanent stand-in with 2 red pips

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var devotionAbility = (IManaAbility)nykthos.Abilities[1];
        devotionAbility.CanActivate().Should().BeTrue();

        var mana = devotionAbility.Activate();

        mana.Red.Should().Be(3, because: "devotion to red = 1 + 2 = 3");
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost was paid");
        nykthos.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_DevotionZero_LegalActivation_AddsNoMana_StillPaysTwo()
    {
        var nykthos = PlaceOnBattlefield(ManaColor.Blue);
        // No blue permanents → devotion to blue = 0.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var devotionAbility = (IManaAbility)nykthos.Abilities[1];
        devotionAbility.CanActivate().Should().BeTrue();

        var mana = devotionAbility.Activate();

        mana.Blue.Should().Be(0);
        mana.Generic.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost is paid even at devotion 0");
        nykthos.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenCannotAffordTwo()
    {
        var nykthos = PlaceOnBattlefield(ManaColor.White);
        AddPermanent("Savannah Lions", "{W}");
        _alice.ManaPool.IsEmpty.Should().BeTrue();

        var devotionAbility = (IManaAbility)nykthos.Abilities[1];
        devotionAbility.CanActivate().Should().BeFalse(
            because: "the {2} additional cost cannot be paid from an empty pool");
    }

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenAlreadyTapped()
    {
        var nykthos = PlaceOnBattlefield(ManaColor.White);
        _alice.AddManaToPool(ManaCost.Parse("2"));
        nykthos.Tap();

        var devotionAbility = (IManaAbility)nykthos.Abilities[1];
        devotionAbility.CanActivate().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Choose a color: colorless is not a color (CR 105.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void FullOverload_RejectsColorless()
    {
        var act = () => NykthosShrineToNyxFactory.Create(_alice, ManaColor.Colorless);
        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "colorless is not a color — \"choose a color\" is one of W/U/B/R/G");
    }
}
