using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HagraMaulingHagraBroodpitFactory"/> — the COMBINED
/// printed name "Hagra Mauling // Hagra Broodpit" of the Zendikar Rising
/// modal double-faced card.
///
/// Front face (Hagra Mauling, Instant {2}{B}{B}):
///   "This spell costs {1} less to cast if an opponent controls no basic
///    lands. Destroy target creature."
/// Back face (Hagra Broodpit, Land):
///   "This land enters tapped." / "{T}: Add {B}."
///
/// The embedded Modern seed keys this MDFC under its combined printed name;
/// without a <c>[CardName]</c> registered for that exact string the seed row —
/// and therefore <c>IsImplemented</c> — stays false even though both single
/// faces are already wired (<see cref="HagraMaulingFactory"/> /
/// <see cref="HagraBroodpitFactory"/>). This factory closes that gap by
/// building the castable FRONT face (the spell half cast from hand) with the
/// castable back-face Land descriptor attached (CR 712.3 — cast-either-face),
/// mirroring the combined-name MDFC pattern of
/// <see cref="ValakutAwakeningValakutStoneforgeFactory"/>.
///
/// These asserts cover only the UNIQUE-to-this-card behaviour (front-face
/// identity at {2}{B}{B}, the MDFC face tracker, black colour, and the
/// opponent-board-aware {1} cost reduction the combined arm preserves by
/// delegating to the front factory). Dispatch + well-formedness are covered
/// automatically by CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class HagraMaulingHagraBroodpitFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Instant_2BB()
    {
        var card = HagraMaulingHagraBroodpitFactory.Create(_alice);

        card.Should().BeOfType<Instant>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Hagra Mauling");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsBlack()
    {
        var card = HagraMaulingHagraBroodpitFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B}{B} pips make it black");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = HagraMaulingHagraBroodpitFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Hagra Mauling");
        card.MdfcState!.BackFaceName.Should().Be("Hagra Broodpit");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Hagra Mauling");
    }

    [Fact]
    public void CombinedName_PreservesOpponentBoardCostReduction()
    {
        // CR 117.7 — the combined arm delegates to the front factory, so the
        // opponent-board-aware {1} reducer must still be attached: {2}{B}{B}
        // when an opponent controls a basic land, {1}{B}{B} when none do.
        var card = HagraMaulingHagraBroodpitFactory.Create(_alice);
        var roster = new[] { _alice, _bob };

        // No opponent basic land → discount applies (generic 2 → 1).
        var discounted = CostReduction.GetEffectiveCost(card, _alice, roster);
        discounted.Generic.Should().Be(1,
            "no opponent controls a basic land, so the {1} discount applies");

        // Give Bob a basic land → discount no longer applies.
        var swamp = new Land(
            "Swamp",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_bob);
        swamp.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(swamp);

        var full = CostReduction.GetEffectiveCost(card, _alice, roster);
        full.Generic.Should().Be(2,
            "Bob controls a basic land, so no discount applies — full {2}{B}{B}");
    }
}
