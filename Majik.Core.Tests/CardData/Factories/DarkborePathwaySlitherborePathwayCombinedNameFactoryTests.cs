using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DarkborePathwaySlitherborePathwayCombinedNameFactory"/>
/// — the COMBINED printed name "Darkbore Pathway // Slitherbore Pathway" of the
/// Kaldheim land // land modal double-faced card.
///
/// The embedded Modern seed keys this MDFC under its combined name (the single
/// faces "Darkbore Pathway" / "Slitherbore Pathway" are already registered for
/// the play-either-face flow, but the seed row — and therefore
/// <c>IsImplemented</c> — is keyed on the combined name). Registering the
/// combined name with a <c>[CardName]</c> arm flips that row to implemented and
/// lets a deck that references the combined name dispatch to a fully-wired
/// front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/>): the combined arm builds
/// the FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
/// Darkbore Pathway land, back = the land Slitherbore Pathway).
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, non-basic, colour).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class DarkborePathwaySlitherborePathwayCombinedNameFactoryTests
{
    private const string CombinedName =
        "Darkbore Pathway // Slitherbore Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var land = DarkborePathwaySlitherborePathwayCombinedNameFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Darkbore Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Darkbore Pathway is a non-basic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_HasSingleBlackManaAbility()
    {
        var land = DarkborePathwaySlitherborePathwayCombinedNameFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "the front face Darkbore Pathway has {T}: Add {B}");

        var black = ManaCost.Parse("B");
        manaAbilities[0].ManaGenerated.Black.Should().Be(black.Black);
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0,
            "the front face produces only black mana");
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var land = DarkborePathwaySlitherborePathwayCombinedNameFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Darkbore Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Slitherbore Pathway");
        land.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        land.MdfcState!.ActiveFaceName.Should().Be("Darkbore Pathway");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Darkbore Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Slitherbore Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
