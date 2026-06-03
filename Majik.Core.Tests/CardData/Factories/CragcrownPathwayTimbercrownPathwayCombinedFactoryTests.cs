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
/// Tests for <see cref="CragcrownPathwayTimbercrownPathwayCombinedFactory"/> —
/// the COMBINED printed name "Cragcrown Pathway // Timbercrown Pathway" of the
/// Kaldheim modal double-faced "Pathway" dual land.
///
/// The embedded Modern seed keys this MDFC under its combined name (the single
/// faces "Cragcrown Pathway" / "Timbercrown Pathway" are already registered for
/// the cast-either-face flow, but the seed row — and therefore
/// <c>IsImplemented</c> — is keyed on the combined name). Registering the
/// combined name with a <c>[CardName]</c> arm flips that row to implemented and
/// lets a deck that references the combined name dispatch to a fully-wired
/// front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="BranchloftPathwayBoulderloftPathwayCombinedFactory"/> /
/// <see cref="SpikefieldHazardCombinedNameFactory"/>): the combined arm builds
/// the FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front = Cragcrown
/// Pathway land, back = the land Timbercrown Pathway). Both Pathway faces are
/// plain "{T}: Add &lt;C&gt;" lands, so the combined arm returns a
/// <see cref="Land"/>.
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, owner/controller).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - Front: single {T}: Add {R} mana ability (CR 605.1).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class CragcrownPathwayTimbercrownPathwayCombinedFactoryTests
{
    private const string CombinedName =
        "Cragcrown Pathway // Timbercrown Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var land = CragcrownPathwayTimbercrownPathwayCombinedFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Cragcrown Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var land = CragcrownPathwayTimbercrownPathwayCombinedFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Cragcrown Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Timbercrown Pathway");
        land.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        land.MdfcState!.ActiveFaceName.Should().Be("Cragcrown Pathway");
    }

    [Fact]
    public void CombinedArm_HasTapForRedManaAbility()
    {
        var land = CragcrownPathwayTimbercrownPathwayCombinedFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {R} — one red mana (CR 605.1, mana ability, no stack).
        mana.ManaGenerated.Red.Should().Be(1);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {R} produces exactly one mana");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Cragcrown Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Timbercrown Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
