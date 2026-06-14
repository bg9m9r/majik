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
/// Tests for <see cref="WitchEnchanterWitchBlessedMeadowFactory"/> — the
/// COMBINED-name dispatch arm of the Wilds of Eldraine modal double-faced card
/// Witch Enchanter // Witch-Blessed Meadow ({3}{W}).
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Witch Enchanter // Witch-Blessed Meadow"); without a factory registered
/// for that exact name the card reads as unimplemented even though both single
/// faces are individually wired (<see cref="WitchEnchanterFactory"/> /
/// <see cref="WitchBlessedMeadowFactory"/>). This factory closes that gap by
/// building the FRONT face (Witch Enchanter — the castable creature half)
/// with its MDFC face tracker, mirroring the combined-name MDFC pattern used
/// by <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "W")]
public class WitchEnchanterWitchBlessedMeadowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Creature_HumanWarlock_2_2_At3W()
    {
        var card = WitchEnchanterWitchBlessedMeadowFactory.Create(_alice);

        card.Should().BeOfType<Creature>(
            "the combined-name arm builds the castable FRONT face (the creature half)");
        card.Name.Should().Be("Witch Enchanter");
        card.ManaCost.Should().Be("{3}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsWhite()
    {
        var card = WitchEnchanterWitchBlessedMeadowFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "the single {W} pip makes it white");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = WitchEnchanterWitchBlessedMeadowFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Witch Enchanter");
        card.MdfcState!.BackFaceName.Should().Be("Witch-Blessed Meadow");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Witch Enchanter");
    }
}
