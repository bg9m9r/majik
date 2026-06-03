using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HengegatePathwayCombinedNameFactory"/> — the COMBINED
/// printed name "Hengegate Pathway // Mistgate Pathway" of the Kaldheim
/// modal double-faced land.
///
/// The two single faces ("Hengegate Pathway" front / "Mistgate Pathway" back)
/// are already registered as <c>[CardName]</c> factories for the
/// play-either-face flow, but the embedded Modern seed keys this card under
/// its COMBINED name — that is the row whose <c>IsImplemented</c> the engine
/// derives from the <c>[CardName]</c> registry. Registering the combined-name
/// arm flips that row to implemented and lets a deck referencing the combined
/// name dispatch to the fully-wired front face.
///
/// Mirrors the combined-name MDFC pattern (e.g.
/// <see cref="RiverglidePathwayCombinedNameFactory"/>): the combined arm builds
/// the FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
/// Hengegate Pathway land, back = Mistgate Pathway land). Both faces are plain
/// lands (CR 712.3 / 712.4 — choose which face to play; faces do not
/// transform).
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, white mana
///   ability for {W}).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class HengegatePathwayCombinedNameFactoryTests
{
    private const string CombinedName =
        "Hengegate Pathway // Mistgate Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var card = HengegatePathwayCombinedNameFactory.Create(_alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hengegate Pathway");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_HasSingleTapForWhiteManaAbility()
    {
        var card = HengegatePathwayCombinedNameFactory.Create(_alice);

        card.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var card = HengegatePathwayCombinedNameFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Hengegate Pathway");
        card.MdfcState!.BackFaceName.Should().Be("Mistgate Pathway");
        card.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Hengegate Pathway");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Hengegate Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Mistgate Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
