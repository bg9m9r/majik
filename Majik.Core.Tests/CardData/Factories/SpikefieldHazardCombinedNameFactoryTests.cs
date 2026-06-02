using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpikefieldHazardCombinedNameFactory"/> — the
/// COMBINED printed name "Spikefield Hazard // Spikefield Cave" of the
/// Zendikar Rising modal double-faced card.
///
/// The embedded Modern seed keys this MDFC under its combined name (the
/// single faces "Spikefield Hazard" / "Spikefield Cave" are already
/// registered for the cast-either-face flow, but the seed row — and therefore
/// <c>IsImplemented</c> — is keyed on the combined name). Registering the
/// combined name with a <c>[CardName]</c> arm flips that row to implemented
/// and lets a deck that references the combined name dispatch to a
/// fully-wired front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="ShatterskullSmashingCombinedNameFactory"/> registering the
/// combined "Shatterskull Smashing // Shatterskull, the Hammer Pass" name):
/// the combined arm builds the FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
/// Spikefield Hazard instant, castable back = the land Spikefield Cave).
///
/// Covers:
/// - Combined arm produces the front-face Instant (name, cost, type, colour).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "R")]
public class SpikefieldHazardCombinedNameFactoryTests : IDisposable
{
    private const string CombinedName =
        "Spikefield Hazard // Spikefield Cave";

    public SpikefieldHazardCombinedNameFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void CombinedArm_BuildsFrontFaceInstant_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = SpikefieldHazardCombinedNameFactory.Create(alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Spikefield Hazard");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var alice = new Player("Alice", 20);
        var card = SpikefieldHazardCombinedNameFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Spikefield Hazard");
        card.MdfcState!.BackFaceName.Should().Be("Spikefield Cave");
        card.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Spikefield Hazard");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceInstant()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create(CombinedName, alice);

        card.Should().BeOfType<Instant>(
            "the combined printed name dispatches to the front-face instant");
        card.Name.Should().Be("Spikefield Hazard");
        ((Instant)card).MdfcState.Should().NotBeNull();
        ((Instant)card).MdfcState!.BackFaceName.Should().Be("Spikefield Cave");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
