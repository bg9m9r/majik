using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TurntimberSymbiosisCombinedFactory"/> — the COMBINED
/// printed name "Turntimber Symbiosis // Turntimber, Serpentine Wood"
/// (Zendikar Rising, {4}{G}{G}{G}) of the Zendikar Rising modal double-faced
/// card.
///
/// The single-face printed names ("Turntimber Symbiosis" /
/// "Turntimber, Serpentine Wood") each dispatch to their own factory
/// (<see cref="TurntimberSymbiosisFactory"/> /
/// <see cref="TurntimberSerpentineWoodFactory"/>). Scryfall (and the embedded
/// card seed) also key MDFCs under the combined "Front // Back" name, so the
/// combined name must dispatch too — to the FRONT face (the castable
/// Sorcery), carrying the same <see cref="MdfcState"/> back-face-land wiring
/// the standalone front-face factory attaches (CR 712.3 / 712.4 —
/// cast-either-face; the back face is the LAND Turntimber, Serpentine Wood).
///
/// Covers:
/// - Combined name dispatches via <see cref="NamedCardFactory"/>.
/// - Front-face identity (Sorcery, {4}{G}{G}{G}, green, owner / controller).
/// - <see cref="MdfcState"/> front + back names; starts on the front face.
/// </summary>
[Trait("Color", "G")]
public class TurntimberSymbiosisCombinedFactoryTests
{
    private const string CombinedName =
        "Turntimber Symbiosis // Turntimber, Serpentine Wood";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_DispatchesToFrontFaceSorcery()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Sorcery>(
            "the combined MDFC name dispatches to the castable front face");
        card.Name.Should().Be(CombinedName,
            "the combined-name card object carries the combined name (the split " +
            "front / back names live on MdfcState) — same convention as Wedding " +
            "Announcement // Wedding Festivity");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
    }

    [Fact]
    public void CombinedFactory_Create_FrontFaceIdentity_4GGG_Sorcery()
    {
        var card = TurntimberSymbiosisCombinedFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be(CombinedName);
        card.ManaCost.Should().Be("{4}{G}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedFactory_Create_IsGreen()
    {
        var card = TurntimberSymbiosisCombinedFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "three {G} pips make it green");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void CombinedFactory_Create_CarriesMdfcState_FrontFace_WithBackLandName()
    {
        var card = TurntimberSymbiosisCombinedFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined name builds the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Turntimber Symbiosis");
        card.MdfcState!.BackFaceName.Should().Be("Turntimber, Serpentine Wood");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Turntimber Symbiosis");
    }
}
