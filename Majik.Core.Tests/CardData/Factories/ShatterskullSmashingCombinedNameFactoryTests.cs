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
/// Tests for <see cref="ShatterskullSmashingCombinedNameFactory"/> — the
/// COMBINED printed name "Shatterskull Smashing // Shatterskull, the Hammer
/// Pass" of the Zendikar Rising modal double-faced card.
///
/// The embedded Modern seed keys this MDFC under its combined name (the
/// single faces "Shatterskull Smashing" / "Shatterskull, the Hammer Pass"
/// are already registered for the cast-either-face flow, but the seed row —
/// and therefore <c>IsImplemented</c> — is keyed on the combined name).
/// Registering the combined name with a <c>[CardName]</c> arm flips that row
/// to implemented and lets a deck that references the combined name dispatch
/// to a fully-wired front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="RalMonsoonMageFactory"/> registering the combined "Ral, Monsoon
/// Mage // Ral, Leyline Prodigy" name): the combined arm builds the FRONT
/// face, which already carries the full <see cref="Majik.Core.CardData.MDFCs.MdfcState"/>
/// wiring (front = Shatterskull Smashing sorcery, castable back = the land
/// Shatterskull, the Hammer Pass).
///
/// Covers:
/// - Combined arm produces the front-face Sorcery (name, cost, type, colour).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// </summary>
[Trait("Color", "R")]
public class ShatterskullSmashingCombinedNameFactoryTests : IDisposable
{
    private const string CombinedName =
        "Shatterskull Smashing // Shatterskull, the Hammer Pass";

    public ShatterskullSmashingCombinedNameFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void CombinedArm_BuildsFrontFaceSorcery_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = ShatterskullSmashingCombinedNameFactory.Create(alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Shatterskull Smashing");
        card.ManaCost.Should().Be("{X}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
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
        var card = ShatterskullSmashingCombinedNameFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Shatterskull Smashing");
        card.MdfcState!.BackFaceName.Should().Be("Shatterskull, the Hammer Pass");
        card.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Shatterskull Smashing");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceSorcery()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create(CombinedName, alice);

        card.Should().BeOfType<Sorcery>(
            "the combined printed name dispatches to the front-face sorcery");
        card.Name.Should().Be("Shatterskull Smashing");
        ((Sorcery)card).MdfcState.Should().NotBeNull();
        ((Sorcery)card).MdfcState!.BackFaceName.Should().Be("Shatterskull, the Hammer Pass");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
