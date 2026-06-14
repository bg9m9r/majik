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
/// Tests for <see cref="ZofConsumptionZofBloodbogFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Zof Consumption // Zof Bloodbog.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Zof Consumption // Zof Bloodbog"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces
/// are individually wired (<see cref="ZofConsumptionFactory"/> /
/// <see cref="ZofBloodbogFactory"/>). This factory closes that gap by building
/// the FRONT face (Zof Consumption — the spell half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "B")]
public class ZofConsumptionZofBloodbogFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Sorcery_4BB()
    {
        var card = ZofConsumptionZofBloodbogFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Zof Consumption");
        card.ManaCost.Should().Be("{4}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsBlack()
    {
        var card = ZofConsumptionZofBloodbogFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B}{B} pips make it black");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = ZofConsumptionZofBloodbogFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Zof Consumption");
        card.MdfcState!.BackFaceName.Should().Be("Zof Bloodbog");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Zof Consumption");
    }
}
