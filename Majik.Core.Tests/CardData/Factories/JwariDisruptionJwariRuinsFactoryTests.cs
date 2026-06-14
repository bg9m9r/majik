using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="JwariDisruptionJwariRuinsFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Jwari Disruption // Jwari Ruins.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Jwari Disruption // Jwari Ruins"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces are
/// individually wired (<see cref="JwariDisruptionFactory"/> /
/// <see cref="JwariRuinsFactory"/>). This factory closes that gap by building
/// the FRONT face (Jwari Disruption — the spell half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "U")]
public class JwariDisruptionJwariRuinsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Instant_1U()
    {
        var card = JwariDisruptionJwariRuinsFactory.Create(_alice);

        card.Should().BeOfType<Instant>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Jwari Disruption");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsBlue()
    {
        var card = JwariDisruptionJwariRuinsFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "the single {U} pip makes it blue");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = JwariDisruptionJwariRuinsFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Jwari Disruption");
        card.MdfcState!.BackFaceName.Should().Be("Jwari Ruins");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Jwari Disruption");
    }
}
