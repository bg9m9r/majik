using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KazanduMammothKazanduValleyFactory"/> — the COMBINED-name
/// dispatch arm of the Zendikar Rising modal double-faced card
/// Kazandu Mammoth // Kazandu Valley.
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Kazandu Mammoth // Kazandu Valley"); without a factory registered for that
/// exact name the card reads as unimplemented even though both single faces are
/// individually wired (<see cref="KazanduMammothFactory"/> /
/// <see cref="KazanduValleyFactory"/>). This factory closes that gap by building
/// the FRONT face (Kazandu Mammoth — the spell half cast from hand) with the
/// castable back-face Land descriptor + landfall trigger attached, mirroring the
/// combined-name MDFC pattern used by
/// <see cref="SilundiVisionSilundiIsleFactory"/>.
/// </summary>
[Trait("Color", "G")]
public class KazanduMammothKazanduValleyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_BuildsFrontFace_CreatureElephant_3_3_1GG()
    {
        var card = KazanduMammothKazanduValleyFactory.Create(_alice);

        card.Should().BeOfType<Creature>(
            "the combined-name arm builds the castable FRONT face (the spell half)");
        card.Name.Should().Be("Kazandu Mammoth");
        card.ManaCost.Should().Be("{1}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Subtypes.Should().Contain(CardSubtype.Elephant);
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_CarriesMdfcState_WithCastableLandBackFace()
    {
        var card = KazanduMammothKazanduValleyFactory.Create(_alice);

        // CR 712.3 — the combined-name front face carries the MDFC face tracker
        // with a castable Land back-face descriptor (Kazandu Valley).
        card.MdfcState.Should().NotBeNull(
            "the combined-name front face must carry the MDFC face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Kazandu Mammoth");
        card.MdfcState.BackFaceName.Should().Be("Kazandu Valley");
        card.MdfcState.IsBackFace.Should().BeFalse(
            "the front face starts on the front face");
        card.MdfcState.CastableBackFace.Should().NotBeNull();
        card.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
    }

    [Fact]
    public void CombinedName_CarriesLandfallTrigger()
    {
        var card = KazanduMammothKazanduValleyFactory.Create(_alice);

        // CR 702.142 / 603.6a — the front face keeps its landfall self-pump.
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(card);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.TargetRequests.Should().BeEmpty(
            "landfall pump affects the Mammoth itself — no target is chosen");
    }
}
