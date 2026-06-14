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
/// Tests for <see cref="SongMadTreacherySongMadRuinsFactory"/> — the
/// COMBINED-name dispatch arm of the Kamigawa: Neon Dynasty modal double-faced
/// card Song-Mad Treachery // Song-Mad Ruins.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Song-Mad Treachery // Song-Mad Ruins"); without a factory registered for
/// that exact name the card reads as unimplemented even though both single
/// faces are individually wired (<see cref="SongMadTreacheryFactory"/> /
/// <see cref="SongMadRuinsFactory"/>). This factory closes that gap by building
/// the FRONT face (Song-Mad Treachery — the spell half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class SongMadTreacherySongMadRuinsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Sorcery_3RR()
    {
        var card = SongMadTreacherySongMadRuinsFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Song-Mad Treachery");
        card.ManaCost.Should().Be("{3}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsRed()
    {
        var card = SongMadTreacherySongMadRuinsFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "the {R}{R} pips make it red");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = SongMadTreacherySongMadRuinsFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Song-Mad Treachery");
        card.MdfcState!.BackFaceName.Should().Be("Song-Mad Ruins");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Song-Mad Treachery");
    }
}
