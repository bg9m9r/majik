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
/// Tests for <see cref="KazuulsFuryKazuulsCliffsFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Kazuul's Fury // Kazuul's Cliffs.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Kazuul's Fury // Kazuul's Cliffs"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces are
/// individually wired (<see cref="KazuulsFuryFactory"/> /
/// <see cref="KazuulsCliffsFactory"/>). This factory closes that gap by building
/// the FRONT face (Kazuul's Fury — the spell half cast from hand) with the
/// castable back-face Land descriptor attached, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// The front face's resolve behaviour (sacrifice-a-creature additional cost +
/// damage equal to the sacrificed creature's power) is covered by
/// <see cref="KazuulsFuryFactoryTests"/>; this suite asserts only that the
/// combined arm builds the correct castable front face with the MDFC tracker.
/// </summary>
[Trait("Color", "R")]
public class KazuulsFuryKazuulsCliffsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_Instant_2R()
    {
        var card = KazuulsFuryKazuulsCliffsFactory.Create(_alice);

        card.Should().BeOfType<Instant>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Kazuul's Fury");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_IsRed()
    {
        var card = KazuulsFuryKazuulsCliffsFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "the single {R} pip makes it red");
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithBothFaceNames_OnFrontFace()
    {
        var card = KazuulsFuryKazuulsCliffsFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Kazuul's Fury");
        card.MdfcState!.BackFaceName.Should().Be("Kazuul's Cliffs");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Kazuul's Fury");
    }
}
