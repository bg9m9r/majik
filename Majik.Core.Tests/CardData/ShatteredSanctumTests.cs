using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ShatteredSanctumFactory"/> — Innistrad: Crimson
/// Vow W/B slowland.
///
/// Covers card identity, the two mana abilities ({W} + {B}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" is a replacement effect handled by
/// the binder layer in production — CR 614.1c). Mirrors
/// <see cref="DesertedBeachTests"/> (same slowland cycle).
/// </summary>
public class ShatteredSanctumTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ShatteredSanctum_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ShatteredSanctum_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Name.Should().Be("Shattered Sanctum");
    }

    [Fact]
    public void ShatteredSanctum_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ShatteredSanctum_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ShatteredSanctum_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ShatteredSanctum_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ShatteredSanctum_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void ShatteredSanctum_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void ShatteredSanctum_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ShatteredSanctum_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Shattered Sanctum", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Shattered Sanctum");
    }

    [Fact]
    public void ShatteredSanctum_IsNotBasic()
    {
        var land = (Land)NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "slow lands are nonbasic");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — slow land: "two or MORE other lands".
    // Mirror image of the fast-land "two or fewer" condition. Enters untapped
    // iff the controller controls >= 2 OTHER lands; "other" excludes the slow
    // land itself; "you control" reads the CONTROLLER's battlefield only.
    // -----------------------------------------------------------------------

    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        ShatteredSanctumFactory.Create(owner, bus);

    private static void SeedLands(Player controller, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = (Land)NamedCardFactory.Create("Plains", controller);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }
    }

    [Fact]
    public void ShatteredSanctum_EntersTapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "0 other lands is < 2, so the slow land enters tapped");
    }

    [Fact]
    public void ShatteredSanctum_EntersTapped_WhenControllerHasOneOtherLand()
    {
        var bus = new ReplacementBus();
        SeedLands(_alice, 1);
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "1 other land is still < 2, so the slow land enters tapped");
    }

    [Fact]
    public void ShatteredSanctum_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var bus = new ReplacementBus();
        SeedLands(_alice, 2);
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "exactly 2 other lands meets 'two or more', so it enters untapped");
    }

    [Fact]
    public void ShatteredSanctum_PredicateExcludesSelf()
    {
        // "two or more OTHER lands" — the slow land must not count itself.
        // Seed exactly 1 other land, then place the slow land on the
        // battlefield too; the count must stay 1 (< 2 ⇒ tapped).
        var bus = new ReplacementBus();
        SeedLands(_alice, 1);

        var land = MakeWithBus(_alice, bus);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the slow land excludes itself, so 1 other land ⇒ tapped");
    }

    [Fact]
    public void ShatteredSanctum_EntersTapped_WhenOnlyOpponentHasManyLands()
    {
        // "you control" — opponent's lands are irrelevant to the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedLands(bob, 5);

        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Alice controls 0 lands; Bob's 5 don't count, so it enters tapped");
    }

    [Fact]
    public void ShatteredSanctum_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg constructs without a
        // ReplacementBus, so the ETB-tapped predicate isn't wired. Prod load
        // wires it from oracle text via ConditionalEntersTappedBinder.
        var land = NamedCardFactory.Create("Shattered Sanctum", _alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Shattered Sanctum");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ShatteredSanctum_Create_ThrowsOnNullOwner()
    {
        var act = () => ShatteredSanctumFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
