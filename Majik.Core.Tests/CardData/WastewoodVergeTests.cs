using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WastewoodVergeFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Owner and controller assignment
/// - Two mana abilities: {G} and {B}
/// - Mana outputs are correct and exclusive
/// - Oracle restriction on {B}: "Activate only if you control a Swamp
///   or a Forest" (CR 605.1a) — gated via <c>canActivateCheck</c>, with
///   the verge itself excluded per CR 109.2.
/// </summary>
public class WastewoodVergeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WastewoodVerge_IsLand()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void WastewoodVerge_NameIsCorrect()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Name.Should().Be("Wastewood Verge");
    }

    [Fact]
    public void WastewoodVerge_IsNotLegendary()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void WastewoodVerge_OwnerAndControllerAreSet()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void WastewoodVerge_HasExactlyTwoManaAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {G} and one for {B}");
    }

    [Fact]
    public void WastewoodVerge_HasGreenManaAbility()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {G} mana ability");
    }

    [Fact]
    public void WastewoodVerge_HasBlackManaAbility()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void WastewoodVerge_GreenManaAbility_ProducesOnlyGreen()
    {
        var land = WastewoodVergeFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.ManaGenerated.Generic.Should().Be(0);
        green.ManaGenerated.White.Should().Be(0);
        green.ManaGenerated.Blue.Should().Be(0);
        green.ManaGenerated.Black.Should().Be(0);
        green.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void WastewoodVerge_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = WastewoodVergeFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void WastewoodVerge_HasNoTriggeredAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wastewood Verge has no triggered abilities");
    }

    [Fact]
    public void WastewoodVerge_HasNoActivatedAbilities()
    {
        var land = WastewoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Wastewood Verge has no non-mana activated abilities in v1");
    }

    // -----------------------------------------------------------------------
    // Oracle restriction: "Activate only if you control a Swamp or a Forest"
    //
    // CR 605.1a — mana abilities still honour activation restrictions.
    // CR 109.2 — "other" excludes the source itself; one lone Wastewood
    // Verge can't satisfy its own restriction.
    // -----------------------------------------------------------------------

    [Fact]
    public void BlackMana_Blocked_WhenSoleVergeOnBattlefield_NoOtherSwampOrForest()
    {
        var verge = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge, _alice);

        var black = FindBlackAbility(verge);
        var green = FindGreenAbility(verge);

        black.CanActivate().Should().BeFalse(
            "no other Swamp or Forest under Alice's control — CR 109.2 'other'");
        green.CanActivate().Should().BeTrue(
            "the {G} mode carries no oracle restriction");
    }

    [Fact]
    public void BlackMana_Allowed_WhenControllerAlsoControlsBasicSwamp()
    {
        var verge = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge, _alice);
        AddBasicLand(_alice, CardSubtype.Swamp);

        var black = FindBlackAbility(verge);

        black.CanActivate().Should().BeTrue(
            "Alice controls a Swamp under her control alongside Wastewood Verge");
    }

    [Fact]
    public void BlackMana_Allowed_WhenControllerAlsoControlsBasicForest()
    {
        var verge = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge, _alice);
        AddBasicLand(_alice, CardSubtype.Forest);

        var black = FindBlackAbility(verge);

        black.CanActivate().Should().BeTrue(
            "Alice controls a Forest under her control alongside Wastewood Verge");
    }

    [Fact]
    public void BlackMana_Blocked_WhenOnlyOpponentControlsASwamp()
    {
        var verge = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge, _alice);
        AddBasicLand(_bob, CardSubtype.Swamp);

        var black = FindBlackAbility(verge);

        black.CanActivate().Should().BeFalse(
            "Bob's Swamp doesn't satisfy Alice's 'you control a Swamp or a Forest'");
    }

    [Fact]
    public void BlackMana_Blocked_WhenVergeIsTapped_EvenWithSwampInPlay()
    {
        // CR 106.1 / cost-paying: a tapped permanent cannot pay its own
        // {T} cost. Passing canActivateCheck bypasses the default
        // !IsTapped guard, so the factory folds the tap check back in.
        var verge = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge, _alice);
        AddBasicLand(_alice, CardSubtype.Swamp);
        verge.Tap();

        var black = FindBlackAbility(verge);

        black.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent — factory must " +
            "preserve the default !IsTapped guard inside canActivateCheck");
    }

    [Fact]
    public void GreenMana_AlwaysAvailable_RegardlessOfBoardState()
    {
        // Sole verge, no other lands.
        var v1 = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(v1, _alice);
        FindGreenAbility(v1).CanActivate().Should().BeTrue(
            "{G} mode has no oracle restriction");

        // Opponent-only Swamp.
        var v2 = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(v2, _alice);
        AddBasicLand(_bob, CardSubtype.Swamp);
        FindGreenAbility(v2).CanActivate().Should().BeTrue(
            "{G} mode is unaffected by opponent's board");

        // Alice also controls a Swamp.
        var v3 = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(v3, _alice);
        AddBasicLand(_alice, CardSubtype.Swamp);
        FindGreenAbility(v3).CanActivate().Should().BeTrue(
            "{G} mode is unaffected by Alice's other lands");
    }

    [Fact]
    public void BlackMana_Blocked_WhenAnotherWastewoodVergeIsTheOnlyOtherLand()
    {
        // Wastewood Verge has no subtypes — a second copy doesn't satisfy
        // the "Swamp or Forest" requirement.
        var verge1 = WastewoodVergeFactory.Create(_alice);
        var verge2 = WastewoodVergeFactory.Create(_alice);
        PlaceOnBattlefield(verge1, _alice);
        PlaceOnBattlefield(verge2, _alice);

        FindBlackAbility(verge1).CanActivate().Should().BeFalse(
            "Wastewood Verge has no Swamp/Forest subtype, so two of them " +
            "still don't satisfy the restriction");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindGreenAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);

    private static ManaAbility FindBlackAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);

    private static void PlaceOnBattlefield(Land land, Player controller)
    {
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    private static void AddBasicLand(Player controller, CardSubtype subtype)
    {
        var land = new Land(subtype.ToString(), supertypes: null, subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.SetController(controller);
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }
}
