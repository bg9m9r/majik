using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BrightclimbPathwayCombinedNameFactory"/> — the
/// COMBINED printed name "Brightclimb Pathway // Grimclimb Pathway" of the
/// Kaldheim modal double-faced land.
///
/// The embedded Modern seed keys this MDFC under its combined name (the single
/// faces "Brightclimb Pathway" / "Grimclimb Pathway" are already registered for
/// the play-either-face flow, but the seed row — and therefore
/// <c>IsImplemented</c> — is keyed on the combined name). Registering the
/// combined name with a <c>[CardName]</c> arm flips that row to implemented and
/// lets a deck that references the combined name dispatch to a fully-wired
/// front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> registering the combined
/// "Spikefield Hazard // Spikefield Cave" name): the combined arm builds the
/// FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
/// Brightclimb Pathway land producing {W}, back = the land Grimclimb Pathway
/// producing {B}).
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, colour).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class BrightclimbPathwayCombinedNameFactoryTests
{
    private const string CombinedName =
        "Brightclimb Pathway // Grimclimb Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var card = BrightclimbPathwayCombinedNameFactory.Create(_alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Brightclimb Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var card = BrightclimbPathwayCombinedNameFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Brightclimb Pathway");
        card.MdfcState!.BackFaceName.Should().Be("Grimclimb Pathway");
        card.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Brightclimb Pathway");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Brightclimb Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Grimclimb Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
