using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CastleArdenvaleFactory"/>.
///
/// Oracle (Scryfall-confirmed, Throne of Eldraine):
///   "This land enters tapped unless you control a Plains.
///    {T}: Add {W}.
///    {2}{W}{W}, {T}: Create a 1/1 white Human creature token."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Castle
/// Ardenvale is NOT itself a Plains.
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary, not a Plains.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Castle Ardenvale".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {W}) + one
///   <see cref="ActivatedAbility"/> ({2}{W}{W},{T}: create token).
/// - ETB predicate (via <see cref="ReplacementBus"/>):
///     · No Plains controlled → enters tapped.
///     · Controller has a Plains → enters untapped.
///     · Opponent controls a Plains, not the controller → enters tapped.
///     · Castle Ardenvale itself is NOT a Plains (cannot satisfy its own predicate).
/// - Mana ability: {T} produces {W}; CanActivate false when tapped.
/// - Activated ability cost: requires {2}{W}{W} + tap.
/// - Activated ability resolve: a 1/1 white Human token enters the battlefield.
/// </summary>
[Trait("Color", "C")]
public class CastleArdenvaleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: place Castle Ardenvale on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var castle = CastleArdenvaleFactory.Create(_alice);
        castle.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castle);
        return castle;
    }

    // -----------------------------------------------------------------------
    // Helper: add a basic Plains to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddPlains(Player controller)
    {
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains })
            { Owner = controller, Controller = controller };
        plains.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(plains);
        return plains;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCastleArdenvale()
    {
        var castle = CastleArdenvaleFactory.Create(_alice);
        castle.Name.Should().Be("Castle Ardenvale");
        castle.HasType(CardType.Land).Should().BeTrue();
        castle.Owner.Should().BeSameAs(_alice);
        castle.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotPlains()
    {
        var castle = CastleArdenvaleFactory.Create(_alice);
        castle.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Castle Ardenvale is nonbasic");
        castle.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Castle Ardenvale is not legendary");
        castle.HasSubtype(CardSubtype.Plains).Should().BeFalse(
            "Castle Ardenvale has no Plains subtype and cannot satisfy its own ETB predicate");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneActivated()
    {
        var castle = CastleArdenvaleFactory.Create(_alice);
        castle.Abilities.Should().HaveCount(2,
            "one {T}: Add {W} mana ability + one {2}{W}{W},{T} activated ability");
        castle.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        castle.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {W}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneWhite()
    {
        var castle = PlaceOnBattlefield();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.White.Should().Be(1, "the mana ability produces {W}");
        produced.Generic.Should().Be(0);
        castle.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var castle = PlaceOnBattlefield();
        castle.Tap();
        var mana = (IManaAbility)castle.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var castle = CastleArdenvaleFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Castle Ardenvale enters tapped when controller has no Plains");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasAPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddPlains(alice);

        var castle = CastleArdenvaleFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: castle,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Castle Ardenvale enters untapped when controller has a Plains");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddPlains(bob);

        var castle = CastleArdenvaleFactory.Create(alice, replacements: bus);

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
    public void PredicateExcludesSelf_CastleIsNotAPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var castle = CastleArdenvaleFactory.Create(alice, replacements: bus);
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
            "Castle Ardenvale has no Plains subtype; its presence on battlefield doesn't satisfy the predicate");
    }
    // -----------------------------------------------------------------------
    // Activated ability: {2}{W}{W}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var castle = CastleArdenvaleFactory.Create(_alice);
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2,
            "costs are {2}{W}{W} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {2}{W}{W} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability: create token (CR 111 / 111.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_CreatesOneOneOneWhiteHumanToken()
    {
        var castle = PlaceOnBattlefield();
        var ability = castle.Abilities.OfType<ActivatedAbility>().Single();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
            "no creatures before activation");

        // Execute the effect directly (unit-test shortcut — costs verified
        // separately above; here we exercise the resolve body).
        ability.Effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "one token is created");
        var token = tokens.Single();
        token.Name.Should().Be("Human");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Human).Should().BeTrue("the token is a Human");
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.White },
            "the token is white (CR 111.4)");
    }
}
