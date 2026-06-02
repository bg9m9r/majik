using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SeaGateRestorationSeaGateRebornFactory"/> — the
/// COMBINED-NAME registration of the Zendikar Rising modal double-faced card
/// Sea Gate Restoration // Sea Gate, Reborn.
///
/// The embedded Modern seed keys this MDFC under its printed COMBINED name
/// ("Sea Gate Restoration // Sea Gate, Reborn"), but the two single-face
/// factories register only their individual face names
/// (<see cref="SeaGateRestorationFactory"/> = "Sea Gate Restoration",
/// <see cref="SeaGateRebornFactory"/> = "Sea Gate, Reborn"). Without a
/// factory carrying the combined name, <see cref="NamedCardFactory.Create"/>
/// returns a vanilla shell for the seed row and <c>IsImplemented</c> never
/// flips on for the real card.
///
/// This combined-name factory closes that gap: dispatching the combined name
/// builds the FRONT face (the Sorcery) wired with the same
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> back-face-land descriptor
/// as <see cref="SeaGateRestorationFactory"/> (CR 712.3 / 712.4 — cast either
/// face; the back face is the land Sea Gate, Reborn).
///
/// Covers:
/// - Combined name dispatches through <see cref="NamedCardFactory"/> to the
///   front-face Sorcery (identity: name = "Sea Gate Restoration",
///   {4}{U}{U}{U}, Sorcery, not Land, blue).
/// - The MDFC face tracker is attached (front = "Sea Gate Restoration",
///   back = "Sea Gate, Reborn"; starts on the front face).
/// </summary>
[Trait("Color", "U")]
public class SeaGateRestorationSeaGateRebornFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_Create_BuildsFrontFaceSorcery_4UUU()
    {
        var card = SeaGateRestorationSeaGateRebornFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>(
            "the combined name resolves to the front face — a Sorcery (CR 712.3)");
        card.Name.Should().Be("Sea Gate Restoration",
            "the built front-face card object carries the front-face name");
        card.ManaCost.Should().Be("{4}{U}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedName_Create_IsBlue()
    {
        var card = SeaGateRestorationSeaGateRebornFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "three {U} pips make it blue");
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
    }

    [Fact]
    public void CombinedName_Create_CarriesMdfcState_BackFaceIsSeaGateReborn()
    {
        var card = SeaGateRestorationSeaGateRebornFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined card is an MDFC and must carry the face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Sea Gate Restoration");
        card.MdfcState!.BackFaceName.Should().Be("Sea Gate, Reborn");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Sea Gate Restoration");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceSorcery()
    {
        // The seed keys this MDFC under the combined printed name. Dispatch
        // through the production NamedCardFactory entry point must return the
        // real front-face Sorcery (not a vanilla shell), which is what flips
        // IsImplemented on for the seed row.
        var card = NamedCardFactory.Create(
            "Sea Gate Restoration // Sea Gate, Reborn", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sea Gate Restoration");
        ((Card)card).HasType(CardType.Sorcery).Should().BeTrue();
        ((Card)card).MdfcState.Should().NotBeNull();
        ((Card)card).MdfcState!.BackFaceName.Should().Be("Sea Gate, Reborn");
    }
}
