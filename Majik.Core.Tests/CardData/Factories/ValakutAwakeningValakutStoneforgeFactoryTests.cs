using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ValakutAwakeningValakutStoneforgeFactory"/> — the
/// COMBINED printed name "Valakut Awakening // Valakut Stoneforge" of the
/// Zendikar Rising modal double-faced card.
///
/// Front face (Valakut Awakening, Instant {2}{R}):
///   "Put any number of cards from your hand on the bottom of your library,
///    then draw that many cards plus one."
/// Back face (Valakut Stoneforge, Land):
///   "This land enters tapped." / "{T}: Add {R}."
///
/// The embedded Modern seed keys this MDFC under its combined printed name
/// ("Valakut Awakening // Valakut Stoneforge"); without a <c>[CardName]</c>
/// registered for that exact string the seed row — and therefore
/// <c>IsImplemented</c> — stays false even though both single faces are already
/// wired (<see cref="ValakutAwakeningFactory"/> /
/// <see cref="ValakutStoneforgeFactory"/>). This factory closes that gap by
/// building the castable FRONT face (the spell half cast from hand) with the
/// castable back-face Land descriptor attached (CR 712.3 — cast-either-face),
/// mirroring the combined-name MDFC pattern of
/// <see cref="BalaGedRecoveryBalaGedSanctuaryFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class ValakutAwakeningValakutStoneforgeFactoryTests
{
    private const string CombinedName =
        "Valakut Awakening // Valakut Stoneforge";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Instant_2R()
    {
        var card = ValakutAwakeningValakutStoneforgeFactory.Create(_alice);

        card.Should().BeOfType<Instant>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Valakut Awakening");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsRed()
    {
        var card = ValakutAwakeningValakutStoneforgeFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "the {R} pip makes it red");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = ValakutAwakeningValakutStoneforgeFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Valakut Awakening");
        card.MdfcState!.BackFaceName.Should().Be("Valakut Stoneforge");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Valakut Awakening");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceInstant()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Instant>(
            "the combined printed name dispatches to the front-face instant");
        card.Name.Should().Be("Valakut Awakening");
        ((Instant)card).MdfcState.Should().NotBeNull();
        ((Instant)card).MdfcState!.BackFaceName.Should().Be("Valakut Stoneforge");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
