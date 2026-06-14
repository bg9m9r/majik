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
/// Tests for <see cref="MalakirRebirthMalakirMireFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Malakir Rebirth // Malakir Mire.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Malakir Rebirth // Malakir Mire"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces are
/// individually wired (<see cref="MalakirRebirthFactory"/> /
/// <see cref="MalakirMireFactory"/>). This factory closes that gap by building
/// the FRONT face (Malakir Rebirth — the spell half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by
/// <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "B")]
public class MalakirRebirthMalakirMireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Instant_B()
    {
        var card = MalakirRebirthMalakirMireFactory.Create(_alice);

        card.Should().BeOfType<Instant>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Malakir Rebirth");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsBlack()
    {
        var card = MalakirRebirthMalakirMireFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the single {B} pip makes it black");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = MalakirRebirthMalakirMireFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Malakir Rebirth");
        card.MdfcState!.BackFaceName.Should().Be("Malakir Mire");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Malakir Rebirth");
    }
}
