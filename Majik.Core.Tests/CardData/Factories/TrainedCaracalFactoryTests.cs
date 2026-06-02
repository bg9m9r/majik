using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Trained Caracal (Ixalan/M19, {W}) — Creature — Cat 1/1.
///   "Lifelink"
///
/// Validates:
///   * Card identity (Cat at {W}, 1/1) + dispatcher entry.
///   * Lifelink keyword marker attached (CR 702.15).
///   * White colour via CardColors.GetColors (CR 105).
///   * Mana value = 1 (CR 202.3).
/// </summary>
[Trait("Color", "W")]
public class TrainedCaracalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Card identity
    // ------------------------------------------------------------------

    [Fact]
    public void TrainedCaracal_IsCreatureCat_AtCostW_1_1()
    {
        var caracal = TrainedCaracalFactory.Create(_alice);

        caracal.Name.Should().Be("Trained Caracal");
        caracal.HasType(CardType.Creature).Should().BeTrue();
        caracal.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        caracal.ManaCost.Should().Be("{W}");
        caracal.Power.Should().Be(1);
        caracal.Toughness.Should().Be(1);
        caracal.Owner.Should().BeSameAs(_alice);
        caracal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TrainedCaracal_ManaValue_IsOne()
    {
        var caracal = TrainedCaracalFactory.Create(_alice);

        // CR 202.3 — mana value of {W} is 1 (TotalValue counts each coloured
        // pip as 1, generic as face value).
        ManaCost.Parse("{W}").TotalValue.Should().Be(1);
    }

    [Fact]
    public void TrainedCaracal_IsWhite()
    {
        var caracal = TrainedCaracalFactory.Create(_alice);

        // CR 105 — {W} in the mana cost stamps the card as white.
        CardColors.GetColors(caracal).Should().Contain(ManaColor.White);
    }

    // ------------------------------------------------------------------
    // Lifelink keyword
    // ------------------------------------------------------------------

    [Fact]
    public void TrainedCaracal_HasLifelinkKeyword()
    {
        var caracal = TrainedCaracalFactory.Create(_alice);

        // CR 702.15 — Lifelink keyword marker, consumed by the standard
        // combat-damage life-gain pipeline.
        caracal.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Lifelink",
                "Trained Caracal has Lifelink (CR 702.15)");
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------
}
