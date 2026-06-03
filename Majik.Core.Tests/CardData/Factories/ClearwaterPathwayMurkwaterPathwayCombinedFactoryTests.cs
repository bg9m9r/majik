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
/// Tests for <see cref="ClearwaterPathwayMurkwaterPathwayCombinedFactory"/> —
/// the COMBINED printed name "Clearwater Pathway // Murkwater Pathway" of the
/// Zendikar Rising modal double-faced land // land card.
///
/// The two faces are already registered as independent <c>[CardName]</c>
/// factories (<see cref="ClearwaterPathwayFactory"/> front = Land "{T}: Add {U}.";
/// <see cref="MurkwaterPathwayFactory"/> back = Land "{T}: Add {B}."), but the
/// embedded Modern seed keys this card under its <b>combined</b> name. With only
/// the two single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c>. This combined arm registers the combined name and
/// dispatches it to the FRONT face (CR 712.3 — the controller chooses which face
/// to play; the front face is the default-existing one), exactly as
/// <see cref="SpikefieldHazardCombinedNameFactory"/> /
/// <see cref="ShatterskullSmashingCombinedNameFactory"/> do.
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, owner/controller).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - The front face has the single {T}: Add {U} mana ability.
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class ClearwaterPathwayMurkwaterPathwayCombinedFactoryTests
{
    private const string CombinedName =
        "Clearwater Pathway // Murkwater Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var land = ClearwaterPathwayMurkwaterPathwayCombinedFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Clearwater Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var land = ClearwaterPathwayMurkwaterPathwayCombinedFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Clearwater Pathway");
        land.MdfcState.BackFaceName.Should().Be("Murkwater Pathway");
        land.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        land.MdfcState.ActiveFaceName.Should().Be("Clearwater Pathway");
    }

    [Fact]
    public void CombinedArm_HasSingleTapForBlueManaAbility()
    {
        var land = ClearwaterPathwayMurkwaterPathwayCombinedFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Clearwater Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Murkwater Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
