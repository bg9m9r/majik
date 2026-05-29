using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TempleOfTheDragonQueenFactory"/> — Temple of the
/// Dragon Queen (Tarkir: Dragonstorm). Oracle text:
///   "As this land enters, you may reveal a Dragon card from your hand. This
///    land enters tapped unless you revealed a Dragon card this way or you
///    control a Dragon.
///    As this land enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// Modelled after <see cref="CanopyVistaFactory"/> (the JSON identity + a
/// <see cref="ConditionalEntersTappedReplacement"/> registered when a
/// <see cref="ReplacementBus"/> is supplied) combined with the
/// "choose a color as this enters" up-front-resolution posture of
/// <see cref="UtopiaSprawlFactory"/> (CR 614.12 / 614.10).
///
/// Covers:
/// - Identity (Land, owner/controller, non-Basic).
/// - The shape-only single-arg path produces no mana ability (the chosen
///   color isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — exactly one ManaAbility, of the
///   chosen color, once a color is supplied (CR 605.1a).
/// - ETB-tapped predicate (CR 614.1c): enters untapped if a Dragon was
///   revealed this way OR the controller controls a Dragon; otherwise tapped.
///   Opponent's Dragons and non-Dragons don't count; self excluded.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class TempleOfTheDragonQueenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Temple_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Temple of the Dragon Queen", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Temple of the Dragon Queen");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Temple_IsNotBasic()
    {
        var land = TempleOfTheDragonQueenFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void Temple_SingleArgPath_HasNoManaAbilityYet_AndNoOtherAbilities()
    {
        // No color chosen yet => no {T}: Add ability; nothing else either.
        var land = TempleOfTheDragonQueenFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "the chosen color isn't known on the shape-only path");
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
    public void Temple_ChosenColor_ProducesExactlyThatColor(ManaColor chosen, string pip)
    {
        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: chosen, revealedDragon: false, replacements: null);

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
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Temple_EntersTapped_WhenNoDragonRevealed_AndNoDragonControlled()
    {
        var bus = new ReplacementBus();
        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Red, revealedDragon: false, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Dragon revealed and no Dragon controlled");
    }

    [Fact]
    public void Temple_EntersUntapped_WhenDragonRevealedFromHand()
    {
        var bus = new ReplacementBus();
        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Red, revealedDragon: true, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "revealing a Dragon card this way lets it enter untapped");
    }

    [Fact]
    public void Temple_EntersUntapped_WhenControllerControlsADragon()
    {
        var bus = new ReplacementBus();
        SeedDragon(_alice);
        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Red, revealedDragon: false, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "controlling a Dragon lets it enter untapped");
    }

    [Fact]
    public void Temple_EntersTapped_WhenOnlyOpponentControlsADragon()
    {
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedDragon(bob);
        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Red, revealedDragon: false, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's Dragons count ('you control a Dragon')");
    }

    [Fact]
    public void Temple_NonDragonPermanents_DoNotCount()
    {
        var bus = new ReplacementBus();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var land = TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Red, revealedDragon: false, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a non-Dragon creature does not satisfy 'you control a Dragon'");
    }

    [Fact]
    public void Temple_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only path: a fresh bus must remain inert.
        var bus = new ReplacementBus();
        var land = TempleOfTheDragonQueenFactory.Create(_alice);

        var after = ApplyEtb(bus, land, _alice);
        after.EntersTapped.Should().BeFalse(
            "no replacement registered on the shape-only path");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Temple_Create_ThrowsOnNullOwner()
    {
        var act = () => TempleOfTheDragonQueenFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Temple_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => TempleOfTheDragonQueenFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, revealedDragon: false, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedDragon(Player owner)
    {
        var dragon = new Creature("Shivan Dragon", "{4}{R}{R}", 5, 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(owner);
        owner.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);
    }

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Land land, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
