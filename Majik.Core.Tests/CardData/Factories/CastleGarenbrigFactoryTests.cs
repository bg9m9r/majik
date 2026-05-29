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
/// Tests for <see cref="CastleGarenbrigFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "Castle Garenbrig enters tapped unless you control a Forest.
///    {T}: Add {G}.
///    {2}{G}{G}, {T}: Add six {G}. Spend this mana only to cast creature
///    spells or activate abilities of creatures."
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary, not a Forest.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Castle Garenbrig".
/// - Two abilities: the vanilla {T}: Add {G} mana ability + the
///   {2}{G}{G},{T}: Add six {G} mana ability.
/// - ETB predicate (via <see cref="ReplacementBus"/>):
///     · No Forest controlled -> enters tapped.
///     · Controller has a Forest -> enters untapped.
///     · Only the opponent has a Forest -> enters tapped.
///     · Castle Garenbrig itself is NOT a Forest (cannot satisfy its own predicate).
/// - {T}: Add {G} produces one green; CanActivate false when tapped.
/// - {2}{G}{G},{T}: Add six {G} — gated on affording {2}{G}{G}; pays the
///   {2}{G}{G} when activated; produces six green; taps the land.
/// </summary>
public class CastleGarenbrigFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Castle Garenbrig on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var castle = CastleGarenbrigFactory.Create(_alice);
        castle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castle);
        return castle;
    }

    // -----------------------------------------------------------------------
    // Helper: add a basic Forest to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddForest(Player controller)
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            { Owner = controller, Controller = controller };
        forest.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(forest);
        return forest;
    }

    // -----------------------------------------------------------------------
    // The simple {T}: Add {G} ability is the one whose production is a
    // single green (Generic 0). The big ability produces six green.
    // -----------------------------------------------------------------------
    private static ManaAbility BasicGreenAbility(Land castle) =>
        castle.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCastleGarenbrig()
    {
        var castle = CastleGarenbrigFactory.Create(_alice);
        castle.Name.Should().Be("Castle Garenbrig");
        castle.HasType(CardType.Land).Should().BeTrue();
        castle.Owner.Should().BeSameAs(_alice);
        castle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotForest()
    {
        var castle = CastleGarenbrigFactory.Create(_alice);
        castle.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Castle Garenbrig is nonbasic");
        castle.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Castle Garenbrig is not legendary");
        // Castle Garenbrig has no printed subtypes -> it cannot satisfy
        // its own ETB predicate.
        castle.HasSubtype(CardSubtype.Forest).Should().BeFalse(
            "Castle Garenbrig has no Forest subtype and cannot satisfy its own ETB predicate");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Castle Garenbrig", _alice);
        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Castle Garenbrig");
    }

    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoManaAbilities()
    {
        var castle = CastleGarenbrigFactory.Create(_alice);
        // Both abilities are mana abilities (CR 605.1a — the {2}{G}{G},{T}
        // ability produces mana, has no target, and doesn't use the stack).
        castle.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one {T}: Add {G} + one {2}{G}{G},{T}: Add six {G}");
        castle.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the second ability is a mana ability, not a stack-using activated ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void BasicManaAbility_Activate_ProducesOneGreen()
    {
        var castle = PlaceOnBattlefield();
        var mana = (IManaAbility)BasicGreenAbility(castle);

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Green.Should().Be(1, "{T}: Add {G} produces one green");
        produced.Generic.Should().Be(0);
        castle.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void BasicManaAbility_CanActivate_FalseWhenTapped()
    {
        var castle = PlaceOnBattlefield();
        castle.Tap();
        var mana = (IManaAbility)BasicGreenAbility(castle);
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var castle = CastleGarenbrigFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Garenbrig enters tapped when controller has no Forest");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasAForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddForest(alice);

        var castle = CastleGarenbrigFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Castle Garenbrig enters untapped when controller has a Forest");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Bob controls a Forest — Alice does not.
        AddForest(bob);

        var castle = CastleGarenbrigFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the 'you control' predicate checks the controller's battlefield, not the opponent's");
    }

    [Fact]
    public void PredicateExcludesSelf_CastleIsNotAForest()
    {
        // Castle Garenbrig has no Forest subtype so even if it's on the
        // battlefield it cannot satisfy its own predicate.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var castle = CastleGarenbrigFactory.Create(alice, replacements: bus);
        alice.Zones.Battlefield.AddCard(castle);
        castle.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Garenbrig has no Forest subtype; its presence on battlefield doesn't satisfy the predicate");
    }

    [Fact]
    public void SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only path via NamedCardFactory.Create — no replacement bus.
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Castle Garenbrig", alice);
        card.Should().BeOfType<Land>();
        ((Land)card).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // {2}{G}{G}, {T}: Add six {G} — mana cost gate + production
    // -----------------------------------------------------------------------

    [Fact]
    public void BigManaAbility_CannotActivate_WithoutPayingMana()
    {
        // With an empty mana pool the controller cannot afford {2}{G}{G},
        // so the big ability is not activatable.
        var castle = PlaceOnBattlefield();
        var big = castle.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green != 1);

        big.CanActivate().Should().BeFalse(
            "cannot activate {2}{G}{G},{T} with an empty mana pool");
    }

    [Fact]
    public void BigManaAbility_Activate_PaysFourMana_ProducesSixGreen()
    {
        var castle = PlaceOnBattlefield();
        // Give Alice {2}{G}{G} to pay the activation cost.
        _alice.AddManaToPool(ManaCost.Parse("{2}{G}{G}"));

        var big = (IManaAbility)castle.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green != 1);

        big.CanActivate().Should().BeTrue("Alice can now afford {2}{G}{G}");
        var produced = big.Activate();

        produced.Green.Should().Be(6, "{2}{G}{G},{T} adds six {G}");
        castle.IsTapped.Should().BeTrue("activating taps the land ({T} cost)");
        // The {2}{G}{G} was drained from the pool as part of the activation
        // cost (CR 602.2a — costs paid up front). Pool is now empty.
        _alice.ManaPool.CanPay(ManaCost.Parse("{G}")).Should().BeFalse(
            "the {2}{G}{G} activation cost emptied the pool");
    }
}
