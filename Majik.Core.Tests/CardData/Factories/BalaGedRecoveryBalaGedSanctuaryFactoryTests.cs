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
/// Tests for <see cref="BalaGedRecoveryBalaGedSanctuaryFactory"/> — the
/// COMBINED-name dispatch arm of the Zendikar Rising modal double-faced card
/// Bala Ged Recovery // Bala Ged Sanctuary.
///
/// Front face (Bala Ged Recovery, Sorcery {2}{G}):
///   "Return target card from your graveyard to your hand."
/// Back face (Bala Ged Sanctuary, Land):
///   "This land enters tapped." / "{T}: Add {G}."
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Bala Ged Recovery // Bala Ged Sanctuary"); without a factory registered
/// for that exact name the card reads as unimplemented even though both single
/// faces are individually wired (<see cref="BalaGedRecoveryFactory"/> /
/// <see cref="BalaGedSanctuaryFactory"/>). This factory closes that gap by
/// building the FRONT face (the spell half that is cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by
/// <see cref="AgadeemsAwakeningAgadeemTheUndercryptFactory"/> /
/// <see cref="SeaGateRestorationSeaGateRebornFactory"/>.
/// </summary>
[Trait("Color", "G")]
public class BalaGedRecoveryBalaGedSanctuaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Sorcery_2G()
    {
        var card = BalaGedRecoveryBalaGedSanctuaryFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Bala Ged Recovery");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsGreen()
    {
        var card = BalaGedRecoveryBalaGedSanctuaryFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "the {G} pip makes it green");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = BalaGedRecoveryBalaGedSanctuaryFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Bala Ged Recovery");
        card.MdfcState!.BackFaceName.Should().Be("Bala Ged Sanctuary");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Bala Ged Recovery");
    }
}
