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
/// Tests for Magus of the Coffers (Future Sight — {4}{B}).
///
/// Creature — Human Wizard 4/4 with the Cabal Coffers ability on a body:
///   "{2}, {T}: Add {B} for each Swamp you control."
///
/// Covers ONLY the card's unique behaviour (the {2},{T} Swamp-scaled mana
/// ability) plus one identity assert for the non-vanilla stats (mana cost,
/// P/T, subtypes). Dispatch + well-formedness are covered automatically by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class MagusOfTheCoffersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private void AddSwamp(Player controller)
    {
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp })
            { Owner = controller, Controller = controller };
        swamp.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(swamp);
    }

    private Creature PlaceOnBattlefield()
    {
        var magus = MagusOfTheCoffersFactory.Create(_alice);
        magus.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(magus);
        // A {T} ability on a creature is gated by summoning sickness
        // (CR 302.6 / 605.3a) — clear it so the ability is activatable.
        magus.ClearSummoningSickness();
        return magus;
    }

    // -----------------------------------------------------------------------
    // Identity — exact non-vanilla stats (mana cost, P/T, subtypes).
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity_HumanWizard_FourFour_FourBlack()
    {
        var magus = MagusOfTheCoffersFactory.Create(_alice);

        magus.Name.Should().Be("Magus of the Coffers");
        magus.HasType(CardType.Creature).Should().BeTrue();
        magus.HasSubtype(CardSubtype.Human).Should().BeTrue();
        magus.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        magus.GetPower().Should().Be(4);
        magus.GetToughness().Should().Be(4);
        magus.ManaCost.Should().Be("{4}{B}");

        // Magus is NOT a Swamp — never counts toward its own ability (CR 305.6).
        magus.HasSubtype(CardSubtype.Swamp).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // The single ability is the {2},{T} mana ability.
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasExactlyOneManaAbility()
    {
        var magus = MagusOfTheCoffersFactory.Create(_alice);

        magus.Abilities.Should().HaveCount(1,
            because: "Magus of the Coffers has only the {2},{T} mana ability");
        magus.Abilities[0].Should().BeAssignableTo<IManaAbility>();
    }

    // -----------------------------------------------------------------------
    // Magus does NOT count itself toward the Swamp tally (CR 305.6).
    // -----------------------------------------------------------------------

    [Fact]
    public void CountSwamps_DoesNotCountMagusItself()
    {
        PlaceOnBattlefield(); // Magus on battlefield — NOT a Swamp.
        DefileFactory.CountSwamps(_alice).Should().Be(0,
            because: "Magus of the Coffers has no Swamp subtype");
    }

    // -----------------------------------------------------------------------
    // Activation: N Swamps → N {B} returned; {2} consumed; the creature taps.
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_ThreeSwamps_AddsThreeBlack_PaysTwo_Taps()
    {
        var magus = PlaceOnBattlefield();
        for (var i = 0; i < 3; i++) AddSwamp(_alice);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = (IManaAbility)magus.Abilities[0];
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        mana.Black.Should().Be(3, because: "3 Swamps → 3{B}");
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost was paid");
        magus.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void Activate_ZeroSwamps_Legal_AddsNoMana_StillPaysTwo()
    {
        // CR 605.1c — activating a mana ability that yields no mana is legal.
        var magus = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var mana = ((IManaAbility)magus.Abilities[0]).Activate();

        mana.Black.Should().Be(0);
        mana.Generic.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, because: "the {2} cost was still paid");
        magus.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CanActivate guards.
    // -----------------------------------------------------------------------

    [Fact]
    public void CanActivate_FalseWhenAlreadyTapped()
    {
        var magus = PlaceOnBattlefield();
        magus.Tap();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        ((IManaAbility)magus.Abilities[0]).CanActivate().Should().BeFalse(
            because: "already tapped — {T} cost cannot be paid");
    }

    [Fact]
    public void CanActivate_FalseWhenCannotAffordTwo()
    {
        var magus = PlaceOnBattlefield();
        _alice.ManaPool.IsEmpty.Should().BeTrue();

        ((IManaAbility)magus.Abilities[0]).CanActivate().Should().BeFalse(
            because: "controller cannot pay the {2} additional cost");
    }

    [Fact]
    public void CanActivate_TrueWhenUntappedAndCanAffordTwo()
    {
        var magus = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        ((IManaAbility)magus.Abilities[0]).CanActivate().Should().BeTrue();
    }
}
