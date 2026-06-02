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
/// Tests for <see cref="KhalniAmbushKhalniTerritoryCombinedFactory"/> — the
/// COMBINED printed name "Khalni Ambush // Khalni Territory" (Zendikar Rising,
/// {2}{G}) of the Zendikar Rising modal double-faced card.
///
/// The single-face printed names ("Khalni Ambush" / "Khalni Territory") each
/// dispatch to their own factory (<see cref="KhalniAmbushFactory"/> /
/// <see cref="KhalniTerritoryFactory"/>). Scryfall (and the embedded card seed)
/// also key MDFCs under the combined "Front // Back" name, so the combined name
/// must dispatch too — to the FRONT face (the castable Instant), carrying the
/// same <see cref="MdfcState"/> back-face-land wiring the standalone front-face
/// factory attaches (CR 712.3 / 712.4 — cast-either-face; the back face is the
/// LAND Khalni Territory).
///
/// Covers:
/// - Combined name dispatches via <see cref="NamedCardFactory"/>.
/// - Front-face identity (Instant, {2}{G}, green, owner / controller).
/// - <see cref="MdfcState"/> front + back names; starts on the front face.
/// </summary>
[Trait("Color", "G")]
public class KhalniAmbushKhalniTerritoryCombinedFactoryTests
{
    private const string CombinedName = "Khalni Ambush // Khalni Territory";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedName_DispatchesToFrontFaceInstant()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Instant>(
            "the combined MDFC name dispatches to the castable front face");
        card.Name.Should().Be(CombinedName,
            "the combined-name card object carries the combined name (the split " +
            "front / back names live on MdfcState) — same convention as " +
            "Turntimber Symbiosis // Turntimber, Serpentine Wood");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
    }

    [Fact]
    public void CombinedFactory_Create_FrontFaceIdentity_2G_Instant()
    {
        var card = KhalniAmbushKhalniTerritoryCombinedFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be(CombinedName);
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedFactory_Create_IsGreen()
    {
        var card = KhalniAmbushKhalniTerritoryCombinedFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "the {G} pip makes it green");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void CombinedFactory_Create_CarriesMdfcState_FrontFace_WithBackLandName()
    {
        var card = KhalniAmbushKhalniTerritoryCombinedFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined name builds the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Khalni Ambush");
        card.MdfcState!.BackFaceName.Should().Be("Khalni Territory");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Khalni Ambush");
    }
}
