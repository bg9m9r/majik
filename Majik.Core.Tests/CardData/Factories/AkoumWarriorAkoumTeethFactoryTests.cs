using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AkoumWarriorAkoumTeethFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Akoum Warrior // Akoum Teeth.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Akoum Warrior // Akoum Teeth"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces
/// are individually wired (<see cref="AkoumWarriorFactory"/> /
/// <see cref="AkoumTeethFactory"/>). This factory closes that gap by building
/// the FRONT face (Akoum Warrior — the creature half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class AkoumWarriorAkoumTeethFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Creature_4_5_Red5R()
    {
        var card = AkoumWarriorAkoumTeethFactory.Create(_alice);

        card.Should().BeOfType<Creature>(
            "the combined-name arm builds the castable FRONT face (the creature half)");
        card.Name.Should().Be("Akoum Warrior");
        card.ManaCost.Should().Be("{5}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Power.Should().Be(4);
        card.Toughness.Should().Be(5);
        card.Subtypes.Should().Contain(CardSubtype.Minotaur);
        card.Subtypes.Should().Contain(CardSubtype.Warrior);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsRed()
    {
        var card = AkoumWarriorAkoumTeethFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Red,
            "the single {R} pip makes it red");
    }

    [Fact]
    public void CombinedName_HasTrample_KeywordMarker()
    {
        var card = AkoumWarriorAkoumTeethFactory.Create(_alice);

        // CR 702.19 — Trample present as a KeywordAbility marker, read by the
        // combat-keyword lookup.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
        CombatAbilities.HasTrample(card).Should().BeTrue();
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithCastableLandBackFace()
    {
        var card = AkoumWarriorAkoumTeethFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Akoum Warrior");
        card.MdfcState.BackFaceName.Should().Be("Akoum Teeth");
        card.MdfcState.IsBackFace.Should().BeFalse("the front face starts on the front face");
        card.MdfcState.CastableBackFace.Should().NotBeNull();
        card.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        card.MdfcState.CastableBackFace.Name.Should().Be("Akoum Teeth");
    }
}
