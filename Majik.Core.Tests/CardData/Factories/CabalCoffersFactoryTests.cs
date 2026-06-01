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
/// Tests for Cabal Coffers (Torment).
/// Exercises:
///   * Card shape: Land, no subtypes, no supertypes, named "Cabal Coffers".
///   * Dispatch: NamedCardFactory.Create returns a Land.
///   * NO basic mana ability: the only ability is the {2},{T} mana ability;
///     there must be no {T}-alone ability that produces mana.
///   * Cabal Coffers is NOT a Swamp and does not count toward its own ability.
///   * Activate with N Swamps → adds N {B} to the controller's mana pool;
///     the land is tapped; {2} is consumed from the pool.
///   * Activate with 0 Swamps → legal; adds 0 mana; {2} still consumed.
///   * CanActivate: false when land is already tapped.
///   * CanActivate: false when controller cannot afford {2}.
/// </summary>
public class CabalCoffersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: add a basic Swamp to Alice's battlefield.
    // -----------------------------------------------------------------------
    private Land AddSwamp(Player controller)
    {
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = controller, Controller = controller };
        swamp.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(swamp);
        return swamp;
    }

    // -----------------------------------------------------------------------
    // Helper: put Cabal Coffers on Alice's battlefield (untapped).
    // -----------------------------------------------------------------------
    private Land PlaceOnBattlefield()
    {
        var coffers = CabalCoffersFactory.Create(_alice);
        coffers.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(coffers);
        return coffers;
    }

    // -----------------------------------------------------------------------
    // Card shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedCabalCoffers()
    {
        var coffers = CabalCoffersFactory.Create(_alice);
        coffers.Name.Should().Be("Cabal Coffers");
        coffers.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Create_HasNoSubtypes()
    {
        var coffers = CabalCoffersFactory.Create(_alice);
        // Cabal Coffers is a plain Land with no subtypes — in particular it
        // is NOT a Swamp (CR 305.6; no basic supertype, no Swamp subtype printed).
        coffers.HasSubtype(CardSubtype.Swamp).Should().BeFalse(
            because: "Cabal Coffers has no printed subtypes and therefore is not a Swamp");
        coffers.HasSubtype(CardSubtype.Forest).Should().BeFalse();
        coffers.HasSubtype(CardSubtype.Plains).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsLandShape()
    {
        var dispatched = NamedCardFactory.Create("Cabal Coffers", _alice);
        dispatched.Should().BeOfType<Land>();
        dispatched.Name.Should().Be("Cabal Coffers");
    }

    // -----------------------------------------------------------------------
    // No basic mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyOneManaAbility_AndItIsNotBasicTapAlone()
    {
        var coffers = CabalCoffersFactory.Create(_alice);

        // Exactly one ability — the {2},{T} mana ability.
        coffers.Abilities.Should().HaveCount(1,
            because: "Cabal Coffers has only one printed ability");

        coffers.Abilities[0].Should().BeAssignableTo<IManaAbility>(
            because: "the single ability is a mana ability");

        // The ability is NOT a free tap-alone ability. Verify: when the land
        // is untapped but the controller has NO mana in their pool, CanActivate
        // must return false (because {2} cannot be paid).
        var coffersBf = PlaceOnBattlefield();
        // Alice has 0 mana — cannot pay {2}.
        _alice.ManaPool.IsEmpty.Should().BeTrue();
        var ability = (IManaAbility)coffersBf.Abilities[0];
        ability.CanActivate().Should().BeFalse(
            because: "with 0 mana in pool the {2} cost of the activation cannot be paid");
    }

    // -----------------------------------------------------------------------
    // Cabal Coffers does NOT count itself
    // -----------------------------------------------------------------------

    [Fact]
    public void CountSwamps_CoffersSelf_NotCounted()
    {
        // Cabal Coffers is on the battlefield but has no Swamp subtype.
        var coffers = PlaceOnBattlefield();
        // Only the Coffers on the battlefield — no actual Swamps.
        CabalCoffersFactory.CountSwamps(_alice).Should().Be(0,
            because: "Cabal Coffers itself is not a Swamp (no Swamp subtype)");
    }

    [Fact]
    public void CountSwamps_CountsOnlySwampSubtype()
    {
        PlaceOnBattlefield(); // Coffers on battlefield — should NOT be counted.
        AddSwamp(_alice);     // 1 Swamp.
        AddSwamp(_alice);     // 2 Swamps.
        // Non-Swamp land — should not be counted.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            { Owner = _alice, Controller = _alice };
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        CabalCoffersFactory.CountSwamps(_alice).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Activation: N Swamps → N {B} added; {2} consumed; land taps
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_ThreeSwamps_AddsThreeBlack_PaysTwo_TapsLand()
    {
        var coffers = PlaceOnBattlefield();
        for (var i = 0; i < 3; i++) AddSwamp(_alice);

        // Give Alice {2} to pay the activation cost.
        _alice.AddManaToPool(ManaCost.Parse("2"));
        _alice.ManaPool.Generic.Should().Be(2);

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        // {2} consumed from pool.
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost was paid");
        // Land is now tapped.
        coffers.IsTapped.Should().BeTrue();
        // 3 black pips added.
        _alice.ManaPool.Black.Should().Be(0,
            because: "mana is returned from Activate(), not added to pool by ManaAbility itself");
        mana.Black.Should().Be(3,
            because: "3 Swamps → 3{B}");
    }

    [Fact]
    public void Activate_ZeroSwamps_LegalActivation_AddsNoMana_StillPaysTwo()
    {
        // 0 Swamps — legal to activate per CR 605.1c, produces 0 mana.
        var coffers = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        // {2} consumed even though 0 mana was generated.
        _alice.ManaPool.Generic.Should().Be(0);
        // Land still tapped.
        coffers.IsTapped.Should().BeTrue();
        // Zero mana returned.
        mana.Black.Should().Be(0);
        mana.Generic.Should().Be(0);
    }

    [Fact]
    public void Activate_OneSwamp_AddsOneBlack()
    {
        var coffers = PlaceOnBattlefield();
        AddSwamp(_alice);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var mana = ((IManaAbility)coffers.Abilities[0]).Activate();

        mana.Black.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // CanActivate guards
    // -----------------------------------------------------------------------

    [Fact]
    public void CanActivate_FalseWhenAlreadyTapped()
    {
        var coffers = PlaceOnBattlefield();
        coffers.Tap(); // tap it manually
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeFalse(
            because: "already tapped — {T} cost cannot be paid");
    }

    [Fact]
    public void CanActivate_FalseWhenCannotAffordTwo()
    {
        var coffers = PlaceOnBattlefield();
        // Pool is empty — cannot pay {2}.
        _alice.ManaPool.IsEmpty.Should().BeTrue();

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeFalse(
            because: "controller cannot pay {2}");
    }

    [Fact]
    public void CanActivate_FalseWhenOnlyOneGenericMana()
    {
        var coffers = PlaceOnBattlefield();
        // Only {1} in pool — not enough for {2}.
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeFalse(
            because: "only 1 generic mana available; need 2");
    }

    [Fact]
    public void CanActivate_TrueWhenUntappedAndCanAffordTwo()
    {
        var coffers = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = (IManaAbility)coffers.Abilities[0];
        ability.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Deferral #2 — dynamic mana ({B} per Swamp) composes with the {2}
    // additional-cost payer (declared via the ManaAbility ctor, no longer
    // inlined in the generator lambda).
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_ComposesDynamicMana_WithAdditionalTwoCost()
    {
        // Two Swamps + extra floating mana: the dynamic generator counts
        // Swamps (→ {B}{B}) AND the additional {2} cost is paid from the pool
        // in the same activation — they compose, not collide.
        var coffers = PlaceOnBattlefield();
        AddSwamp(_alice);
        AddSwamp(_alice);
        _alice.AddManaToPool(ManaCost.Parse("4")); // {2} for cost + {2} spare

        var mana = ((IManaAbility)coffers.Abilities[0]).Activate();

        mana.Black.Should().Be(2, "2 Swamps → {B}{B} (dynamic mana generator)");
        _alice.ManaPool.Generic.Should().Be(2, "only the {2} additional cost was consumed");
        coffers.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void CannotActivateWithoutTwo_AdditionalCostEnforced_LandNotTapped()
    {
        // The {2} additional cost gates activation: with an empty pool the
        // ability is illegal and nothing is tapped / paid (CR 119.4 — can't
        // pay a cost you can't afford).
        var coffers = PlaceOnBattlefield();
        AddSwamp(_alice);
        var ability = (IManaAbility)coffers.Abilities[0];

        ability.CanActivate().Should().BeFalse("cannot pay the {2} additional cost");
        coffers.IsTapped.Should().BeFalse("an illegal activation taps nothing");
        _alice.ManaPool.IsEmpty.Should().BeTrue("nothing was paid");
    }

    // -----------------------------------------------------------------------
    // BuildBlackMana internal helper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildBlackMana_ZeroOrNegative_ReturnsZero(int n)
    {
        CabalCoffersFactory.BuildBlackMana(n).Should().Be(ManaCost.Zero);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    public void BuildBlackMana_PositiveN_ReturnsNBlack(int n, int expectedBlack)
    {
        var result = CabalCoffersFactory.BuildBlackMana(n);
        result.Black.Should().Be(expectedBlack);
        result.Generic.Should().Be(0);
        result.White.Should().Be(0);
    }
}
