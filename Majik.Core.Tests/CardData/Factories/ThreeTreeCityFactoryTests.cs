using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Three Tree City (Bloomburrow) — Legendary Land.
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "As Three Tree City enters, choose a creature type.
///    {T}: Add {C}.
///    {2}, {T}: Choose a color. Add an amount of mana of that color equal to the
///    number of creatures you control of the chosen type."
///
/// Exercises only the UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   * Legendary Land identity (single non-vanilla *_Identity assert).
///   * ETB creature-type choice stored + retrievable, per-card (CR 614.12).
///   * Shape-only path wires only the JSON "{T}: Add {C}" mana ability.
///   * Full overload wires the second {2},{T} ability.
///   * Creature-of-chosen-type counting across controlled permanents.
///   * Activate: count N → adds N pips of chosen color, pays {2}, taps.
///   * Activate with count 0 → legal, adds no mana, still pays {2}.
///   * CanActivate guards: tapped land / cannot afford {2}.
///   * Colorless is not a color (CR 105.1) → rejected.
/// </summary>
[Trait("Color", "C")]
public class ThreeTreeCityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Land PlaceOnBattlefield(CardSubtype chosenType, ManaColor chosenColor)
    {
        var land = ThreeTreeCityFactory.Create(_alice, chosenType, chosenColor);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    private void AddCreature(string name, params CardSubtype[] subtypes)
    {
        var creature = new Creature(name, "{1}", power: 1, toughness: 1, subtypes: subtypes)
        { Owner = _alice, Controller = _alice };
        creature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(creature);
    }

    // -----------------------------------------------------------------------
    // Identity (single non-vanilla assert — exact supertype/type)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLegendaryLand()
    {
        var land = ThreeTreeCityFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // ETB creature-type choice (CR 614.12)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_LeavesChosenTypeUnset()
    {
        var land = ThreeTreeCityFactory.Create(_alice);
        ThreeTreeCityFactory.GetChosenType(land).Should().BeNull(
            because: "the shape-only path makes no ETB type choice");
    }

    [Fact]
    public void StoresChosenType_FromEtbChoice()
    {
        var land = ThreeTreeCityFactory.Create(_alice, CardSubtype.Rabbit);
        ThreeTreeCityFactory.GetChosenType(land).Should().Be(CardSubtype.Rabbit);
    }

    [Fact]
    public void ChosenTypeIsPerCard()
    {
        var a = ThreeTreeCityFactory.Create(_alice, CardSubtype.Rabbit);
        var b = ThreeTreeCityFactory.Create(_alice, CardSubtype.Goblin);

        ThreeTreeCityFactory.GetChosenType(a).Should().Be(CardSubtype.Rabbit);
        ThreeTreeCityFactory.GetChosenType(b).Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // Abilities wired
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_HasSingleColorlessManaAbility()
    {
        var land = ThreeTreeCityFactory.Create(_alice);

        land.Abilities.Should().HaveCount(1,
            because: "the shape-only path wires only the JSON \"{T}: Add {C}\" ability");
        land.Abilities[0].Should().BeAssignableTo<IManaAbility>();
    }

    [Fact]
    public void FirstAbility_AddsOneColorless()
    {
        var land = PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.Green);

        var colorless = (IManaAbility)land.Abilities[0];
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1, because: "{T}: Add {C} produces one colorless pip");
        mana.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void FullOverload_HasTwoManaAbilities()
    {
        var land = ThreeTreeCityFactory.Create(_alice, CardSubtype.Rabbit, ManaColor.Green);
        land.Abilities.Should().HaveCount(2,
            because: "{T}: Add {C} plus the {2},{T} count ability");
        land.Abilities[0].Should().BeAssignableTo<IManaAbility>();
        land.Abilities[1].Should().BeAssignableTo<IManaAbility>();
    }

    // -----------------------------------------------------------------------
    // Counting creatures of the chosen type (the unique mechanic)
    // -----------------------------------------------------------------------

    [Fact]
    public void CountCreaturesOfChosenType_CountsOnlyMatchingCreatures()
    {
        PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.Green);
        AddCreature("Rabbit A", CardSubtype.Rabbit);
        AddCreature("Rabbit B", CardSubtype.Rabbit);
        AddCreature("Rabbit-Soldier", CardSubtype.Rabbit, CardSubtype.Soldier);
        AddCreature("A Goblin", CardSubtype.Goblin);

        ThreeTreeCityFactory.CountCreaturesOfChosenType(_alice, CardSubtype.Rabbit)
            .Should().Be(3, because: "three controlled creatures are Rabbits");
        ThreeTreeCityFactory.CountCreaturesOfChosenType(_alice, CardSubtype.Goblin)
            .Should().Be(1);
        ThreeTreeCityFactory.CountCreaturesOfChosenType(_alice, CardSubtype.Elf)
            .Should().Be(0);
    }

    [Fact]
    public void CountCreaturesOfChosenType_NullPlayer_IsZero()
    {
        ThreeTreeCityFactory.CountCreaturesOfChosenType(null!, CardSubtype.Rabbit)
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: add (creatures of chosen type) mana of chosen color
    // -----------------------------------------------------------------------

    [Fact]
    public void SecondAbility_CountThree_AddsThreeOfColor_PaysTwo_TapsLand()
    {
        var land = PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.Green);
        AddCreature("Rabbit A", CardSubtype.Rabbit);
        AddCreature("Rabbit B", CardSubtype.Rabbit);
        AddCreature("Rabbit C", CardSubtype.Rabbit);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var countAbility = (IManaAbility)land.Abilities[1];
        countAbility.CanActivate().Should().BeTrue();

        var mana = countAbility.Activate();

        mana.Green.Should().Be(3, because: "three Rabbits = three green pips");
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost was paid");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_CountZero_LegalActivation_AddsNoMana_StillPaysTwo()
    {
        var land = PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.Blue);
        // No Rabbits → count = 0.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var countAbility = (IManaAbility)land.Abilities[1];
        countAbility.CanActivate().Should().BeTrue();

        var mana = countAbility.Activate();

        mana.Blue.Should().Be(0);
        mana.Generic.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost is paid even at count 0");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenCannotAffordTwo()
    {
        var land = PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.White);
        AddCreature("Rabbit A", CardSubtype.Rabbit);
        _alice.ManaPool.IsEmpty.Should().BeTrue();

        var countAbility = (IManaAbility)land.Abilities[1];
        countAbility.CanActivate().Should().BeFalse(
            because: "the {2} additional cost cannot be paid from an empty pool");
    }

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenAlreadyTapped()
    {
        var land = PlaceOnBattlefield(CardSubtype.Rabbit, ManaColor.White);
        _alice.AddManaToPool(ManaCost.Parse("2"));
        land.Tap();

        var countAbility = (IManaAbility)land.Abilities[1];
        countAbility.CanActivate().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Choose a color: colorless is not a color (CR 105.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void FullOverload_RejectsColorless()
    {
        var act = () => ThreeTreeCityFactory.Create(_alice, CardSubtype.Rabbit, ManaColor.Colorless);
        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "colorless is not a color — \"choose a color\" is one of W/U/B/R/G");
    }
}
