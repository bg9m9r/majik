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
/// Tests for <see cref="EmeriasCallEmeriaShatteredSkyclaveFactory"/> — the
/// COMBINED-name dispatch arm of the Zendikar Rising modal double-faced card
/// Emeria's Call // Emeria, Shattered Skyclave.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Emeria's Call // Emeria, Shattered Skyclave"); without a factory
/// registered for that exact name the card reads as unimplemented even though
/// both single faces are individually wired (<see cref="EmeriasCallFactory"/> /
/// <see cref="EmeriaShatteredSkyclaveFactory"/>). This factory closes that gap
/// by building the FRONT face (Emeria's Call — the spell half cast from hand)
/// with the castable back-face Land descriptor attached, mirroring the
/// combined-name MDFC pattern used by
/// <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "W")]
public class EmeriasCallEmeriaShatteredSkyclaveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Sorcery_4WWW()
    {
        var card = EmeriasCallEmeriaShatteredSkyclaveFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Emeria's Call");
        card.ManaCost.Should().Be("{4}{W}{W}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsWhite()
    {
        var card = EmeriasCallEmeriaShatteredSkyclaveFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "the {W} pips make it white");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = EmeriasCallEmeriaShatteredSkyclaveFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Emeria's Call");
        card.MdfcState!.BackFaceName.Should().Be("Emeria, Shattered Skyclave");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Emeria's Call");
    }
}
