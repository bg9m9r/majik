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
/// Tests for <see cref="BranchloftPathwayBoulderloftPathwayCombinedFactory"/> —
/// the COMBINED printed name "Branchloft Pathway // Boulderloft Pathway" of the
/// Zendikar Rising modal double-faced "Pathway" dual land.
///
/// The embedded Modern seed keys this MDFC under its combined name (the single
/// faces "Branchloft Pathway" / "Boulderloft Pathway" are already registered
/// for the cast-either-face flow, but the seed row — and therefore
/// <c>IsImplemented</c> — is keyed on the combined name). Registering the
/// combined name with a <c>[CardName]</c> arm flips that row to implemented and
/// lets a deck that references the combined name dispatch to a fully-wired
/// front face.
///
/// This mirrors the combined-name MDFC pattern (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> /
/// <see cref="ShatterskullSmashingCombinedNameFactory"/>): the combined arm
/// builds the FRONT face, which already carries the full
/// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
/// Branchloft Pathway land, back = the land Boulderloft Pathway). Unlike those
/// instant/sorcery fronts, BOTH Pathway faces are plain "{T}: Add &lt;C&gt;"
/// lands, so the combined arm returns a <see cref="Land"/>.
///
/// Covers:
/// - Combined arm produces the front-face Land (name, type, owner/controller).
/// - The produced card carries MdfcState (front + back names, on front face).
/// - Front: single {T}: Add {G} mana ability (CR 605.1).
/// - <see cref="NamedCardFactory"/> dispatches the combined printed name.
/// - The combined name is registered in <see cref="ImplementedCardNames"/>.
/// </summary>
[Trait("Color", "C")]
public class BranchloftPathwayBoulderloftPathwayCombinedFactoryTests
{
    private const string CombinedName =
        "Branchloft Pathway // Boulderloft Pathway";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CombinedArm_BuildsFrontFaceLand_Identity()
    {
        var land = BranchloftPathwayBoulderloftPathwayCombinedFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Branchloft Pathway");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CombinedArm_CarriesMdfcState_OnFrontFace()
    {
        var land = BranchloftPathwayBoulderloftPathwayCombinedFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "the combined arm builds the front face, which carries the MDFC tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Branchloft Pathway");
        land.MdfcState!.BackFaceName.Should().Be("Boulderloft Pathway");
        land.MdfcState!.IsBackFace.Should().BeFalse("starts on the front face");
        land.MdfcState!.ActiveFaceName.Should().Be("Branchloft Pathway");
    }

    [Fact]
    public void CombinedArm_HasTapForGreenManaAbility()
    {
        var land = BranchloftPathwayBoulderloftPathwayCombinedFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        // {T}: Add {G} — one green mana (CR 605.1, mana ability, no stack).
        mana.ManaGenerated.Green.Should().Be(1);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.TotalValue.Should().Be(1, "{T}: Add {G} produces exactly one mana");
    }

    [Fact]
    public void NamedCardFactory_DispatchesCombinedName_ToFrontFaceLand()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Land>(
            "the combined printed name dispatches to the front-face land");
        card.Name.Should().Be("Branchloft Pathway");
        ((Land)card).MdfcState.Should().NotBeNull();
        ((Land)card).MdfcState!.BackFaceName.Should().Be("Boulderloft Pathway");
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName,
            "registering the combined-name [CardName] arm flips the seed row to implemented");
    }
}
