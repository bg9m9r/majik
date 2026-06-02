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
/// Tests for Cabal Stronghold (Dominaria) — Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {3}, {T}: Add {B} for each basic Swamp you control."
///
/// Exercises:
///   * Card shape: Land, no subtypes, no supertypes, named "Cabal Stronghold".
///   * Dispatch through <see cref="NamedCardFactory"/> returns a Land.
///   * Cabal Stronghold is NOT a (basic) Swamp and does not count itself.
///   * First ability ({T}: Add {C}) — wired from JSON.
///   * Second ability ({3},{T}: Add {B} per basic Swamp): N basic Swamps →
///     N {B} added, {3} consumed, land tapped.
///   * Only BASIC Swamps count — a non-basic Swamp land does not.
///   * Zero basic Swamps → legal activation, 0 mana, {3} still paid.
///   * CanActivate guards: tapped land / cannot afford {3}.
/// </summary>
[Trait("Color", "C")]
public class CabalStrongholdFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Add a basic Swamp (Basic supertype + Swamp subtype).</summary>
    private Land AddBasicSwamp(Player controller)
    {
        var swamp = new Land(
            "Swamp",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Swamp })
        { Owner = controller, Controller = controller };
        swamp.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(swamp);
        return swamp;
    }

    /// <summary>Add a NON-basic land that is a Swamp (no Basic supertype).</summary>
    private Land AddNonBasicSwamp(Player controller)
    {
        var dual = new Land(
            "Watery Grave",
            subtypes: new[] { CardSubtype.Island, CardSubtype.Swamp })
        { Owner = controller, Controller = controller };
        dual.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(dual);
        return dual;
    }

    private Land PlaceOnBattlefield()
    {
        var stronghold = CabalStrongholdFactory.Create(_alice);
        stronghold.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(stronghold);
        return stronghold;
    }

    // -----------------------------------------------------------------------
    // Card shape + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCabalStronghold()
    {
        var stronghold = CabalStrongholdFactory.Create(_alice);

        stronghold.Name.Should().Be("Cabal Stronghold");
        stronghold.HasType(CardType.Land).Should().BeTrue();
        stronghold.Owner.Should().BeSameAs(_alice);
        stronghold.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasNoSubtypesOrSupertypes()
    {
        var stronghold = CabalStrongholdFactory.Create(_alice);

        // Cabal Stronghold is a plain Land — in particular it is NOT a basic
        // Swamp (no Basic supertype, no Swamp subtype printed) and so never
        // counts toward its own ability (CR 305.6 / CR 205.4a).
        stronghold.HasSubtype(CardSubtype.Swamp).Should().BeFalse(
            because: "Cabal Stronghold has no printed subtypes — it is not a Swamp");
        stronghold.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            because: "Cabal Stronghold is not a basic land");
        stronghold.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Cabal Stronghold", _alice);
        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Cabal Stronghold");
    }

    // -----------------------------------------------------------------------
    // Both abilities present
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasTwoManaAbilities()
    {
        var stronghold = CabalStrongholdFactory.Create(_alice);

        stronghold.Abilities.Should().HaveCount(2,
            because: "{T}: Add {C} (JSON) plus the {3},{T} basic-Swamp ability");
        stronghold.Abilities[0].Should().BeAssignableTo<IManaAbility>();
        stronghold.Abilities[1].Should().BeAssignableTo<IManaAbility>();
    }

    // -----------------------------------------------------------------------
    // First ability: {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstAbility_AddsOneColorless_TapsLand()
    {
        var stronghold = PlaceOnBattlefield();

        var colorless = (IManaAbility)stronghold.Abilities[0];
        var mana = colorless.Activate();

        // {C} has no dedicated bucket — it is modelled as +1 generic.
        mana.Generic.Should().Be(1, because: "{T}: Add {C} produces one colorless pip");
        mana.Black.Should().Be(0);
        stronghold.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CountBasicSwamps (CR 305.6 + CR 205.4a)
    // -----------------------------------------------------------------------

    [Fact]
    public void CountBasicSwamps_StrongholdSelf_NotCounted()
    {
        PlaceOnBattlefield(); // only Cabal Stronghold on battlefield
        CabalStrongholdFactory.CountBasicSwamps(_alice).Should().Be(0,
            because: "Cabal Stronghold itself is not a basic Swamp");
    }

    [Fact]
    public void CountBasicSwamps_CountsOnlyBasicSwamps()
    {
        PlaceOnBattlefield();
        AddBasicSwamp(_alice);    // counts
        AddBasicSwamp(_alice);    // counts
        AddNonBasicSwamp(_alice); // Swamp subtype but NOT basic → does NOT count

        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest })
        { Owner = _alice, Controller = _alice };
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest); // basic, but not a Swamp → no count

        CabalStrongholdFactory.CountBasicSwamps(_alice).Should().Be(2,
            because: "only the two basic Swamps count; the non-basic dual and the basic Forest do not");
    }

    [Fact]
    public void CountBasicSwamps_NullPlayer_IsZero()
    {
        CabalStrongholdFactory.CountBasicSwamps(null!).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Second ability: {3},{T}: Add {B} per basic Swamp
    // -----------------------------------------------------------------------

    [Fact]
    public void SecondAbility_ThreeBasicSwamps_AddsThreeBlack_PaysThree_TapsLand()
    {
        var stronghold = PlaceOnBattlefield();
        for (var i = 0; i < 3; i++) AddBasicSwamp(_alice);

        _alice.AddManaToPool(ManaCost.Parse("3"));
        _alice.ManaPool.Generic.Should().Be(3);

        var ability = (IManaAbility)stronghold.Abilities[1];
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        mana.Black.Should().Be(3, because: "3 basic Swamps → 3{B}");
        _alice.ManaPool.Generic.Should().Be(0, because: "the {3} cost was paid");
        stronghold.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_NonBasicSwampDoesNotProduceMana()
    {
        var stronghold = PlaceOnBattlefield();
        AddNonBasicSwamp(_alice); // Swamp subtype but not basic
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var mana = ((IManaAbility)stronghold.Abilities[1]).Activate();

        mana.Black.Should().Be(0, because: "a non-basic Swamp does not count toward 'each basic Swamp'");
        stronghold.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_ZeroBasicSwamps_LegalActivation_AddsNoMana_StillPaysThree()
    {
        var stronghold = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var ability = (IManaAbility)stronghold.Abilities[1];
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        mana.Black.Should().Be(0);
        mana.Generic.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, because: "the {3} cost is paid even at zero basic Swamps");
        stronghold.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SecondAbility_OneBasicSwamp_AddsOneBlack()
    {
        var stronghold = PlaceOnBattlefield();
        AddBasicSwamp(_alice);
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var mana = ((IManaAbility)stronghold.Abilities[1]).Activate();

        mana.Black.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CanActivate guards
    // -----------------------------------------------------------------------

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenAlreadyTapped()
    {
        var stronghold = PlaceOnBattlefield();
        stronghold.Tap();
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var ability = (IManaAbility)stronghold.Abilities[1];
        ability.CanActivate().Should().BeFalse(
            because: "already tapped — {T} cost cannot be paid");
    }

    [Fact]
    public void SecondAbility_CanActivate_FalseWhenCannotAffordThree()
    {
        var stronghold = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2")); // only {2}, need {3}

        var ability = (IManaAbility)stronghold.Abilities[1];
        ability.CanActivate().Should().BeFalse(
            because: "only 2 generic mana available; need 3");
        stronghold.IsTapped.Should().BeFalse("an illegal activation taps nothing");
    }

    [Fact]
    public void SecondAbility_CanActivate_TrueWhenUntappedAndCanAffordThree()
    {
        var stronghold = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("3"));

        var ability = (IManaAbility)stronghold.Abilities[1];
        ability.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // BuildBlackMana internal helper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildBlackMana_ZeroOrNegative_ReturnsZero(int n)
    {
        CabalStrongholdFactory.BuildBlackMana(n).Should().Be(ManaCost.Zero);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    public void BuildBlackMana_PositiveN_ReturnsNBlack(int n, int expectedBlack)
    {
        var result = CabalStrongholdFactory.BuildBlackMana(n);
        result.Black.Should().Be(expectedBlack);
        result.Generic.Should().Be(0);
    }
}
