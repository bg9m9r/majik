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
/// Tests for <see cref="AgadeemsAwakeningAgadeemTheUndercryptFactory"/> — the
/// COMBINED-name dispatch arm of the Zendikar Rising modal double-faced card
/// Agadeem's Awakening // Agadeem, the Undercrypt.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Agadeem's Awakening // Agadeem, the Undercrypt"); without a factory
/// registered for that exact name the card reads as unimplemented even though
/// both single faces are individually wired. This factory closes that gap by
/// building the FRONT face (Agadeem's Awakening — the spell half that is cast
/// from hand) with the castable back-face Land descriptor attached, mirroring
/// the combined-name MDFC pattern used by
/// <see cref="SinkIntoStuporFactory"/> / <see cref="BoomBustFactory"/>.
/// </summary>
[Trait("Color", "B")]
public class AgadeemsAwakeningAgadeemTheUndercryptFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Sorcery_XBBB()
    {
        var card = AgadeemsAwakeningAgadeemTheUndercryptFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Agadeem's Awakening");
        card.ManaCost.Should().Be("{X}{B}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsBlack()
    {
        var card = AgadeemsAwakeningAgadeemTheUndercryptFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "three {B} pips make it black");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = AgadeemsAwakeningAgadeemTheUndercryptFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Agadeem's Awakening");
        card.MdfcState!.BackFaceName.Should().Be("Agadeem, the Undercrypt");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Agadeem's Awakening");
    }
}
