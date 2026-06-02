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
/// Tests for <see cref="VastwoodFortificationCombinedFactory"/> — the COMBINED
/// printed name "Vastwood Fortification // Vastwood Thicket"
/// (Modern Horizons 3, {G}) of the modal double-faced card.
///
/// The single-face printed names ("Vastwood Fortification" /
/// "Vastwood Thicket") each dispatch to their own factory
/// (<see cref="VastwoodFortificationFactory"/> /
/// <see cref="VastwoodThicketFactory"/>). Scryfall (and the embedded card seed)
/// also key MDFCs under the combined "Front // Back" name, so the combined name
/// must dispatch too — to the FRONT face (the castable Instant), carrying the
/// same <see cref="MdfcState"/> back-face-land wiring the standalone front-face
/// factory attaches (CR 712.3 / 712.4 — cast-either-face; the back face is the
/// LAND Vastwood Thicket).
///
/// Covers:
/// - Combined name dispatches via <see cref="NamedCardFactory"/>.
/// - Front-face identity (Instant, {G}, green, owner / controller).
/// - <see cref="MdfcState"/> front + back names; starts on the front face.
/// </summary>
[Trait("Color", "G")]
public class VastwoodFortificationCombinedFactoryTests
{
    private const string CombinedName =
        "Vastwood Fortification // Vastwood Thicket";

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
    public void CombinedFactory_Create_FrontFaceIdentity_G_Instant()
    {
        var card = VastwoodFortificationCombinedFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be(CombinedName);
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedFactory_Create_IsGreen()
    {
        var card = VastwoodFortificationCombinedFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "one {G} pip makes it green");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void CombinedFactory_Create_CarriesMdfcState_FrontFace_WithBackLandName()
    {
        var card = VastwoodFortificationCombinedFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined name builds the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Vastwood Fortification");
        card.MdfcState!.BackFaceName.Should().Be("Vastwood Thicket");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Vastwood Fortification");
    }
}
