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
/// Unit tests for <see cref="DesertedBeachFactory"/> — Innistrad: Midnight
/// Hunt W/U slowland.
///
/// Covers card identity, the two mana abilities ({W} + {U}), that no
/// triggered or non-mana activated abilities ship in v1, and the conditional
/// ETB-tapped "two or MORE other lands" replacement (CR 614.1c) — the inverse
/// of the fast-land condition: untapped at ≥ 2 other lands, tapped at &lt; 2.
/// Mirrors <see cref="StormcarvedCoastTests"/> (same slowland cycle) and the
/// fast-land counterpart <c>SeachromeCoastTests</c>.
/// </summary>
public class DesertedBeachTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        DesertedBeachFactory.Create(owner, bus);

    private static void SeedLands(Player controller, int count)
    {
        // Seed `count` controller-owned generic lands (use basics so they
        // are real Land permanents the predicate will tally).
        for (var i = 0; i < count; i++)
        {
            var land = (Land)NamedCardFactory.Create("Plains", controller);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }
    }

    [Fact]
    public void DesertedBeach_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void DesertedBeach_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Name.Should().Be("Deserted Beach");
    }

    [Fact]
    public void DesertedBeach_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DesertedBeach_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DesertedBeach_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DesertedBeach_HasWhiteManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void DesertedBeach_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void DesertedBeach_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void DesertedBeach_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DesertedBeach_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Deserted Beach", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Deserted Beach");
    }

    [Fact]
    public void DesertedBeach_NotBasic()
    {
        var land = (Land)NamedCardFactory.Create("Deserted Beach", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "slow lands are nonbasic");
    }

    [Fact]
    public void DesertedBeach_Create_ThrowsOnNullOwner()
    {
        var act = () => DesertedBeachFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or MORE other lands"
    // (inverse of the fast land: untapped at ≥ 2 other lands).
    // -----------------------------------------------------------------------

    [Fact]
    public void DesertedBeach_EntersTapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "0 other lands is < 2, so Deserted Beach enters tapped");
    }

    [Fact]
    public void DesertedBeach_EntersTapped_WhenControllerHasOneOtherLand()
    {
        var bus = new ReplacementBus();
        SeedLands(_alice, 1);
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "1 other land is still < 2, so it enters tapped");
    }

    [Fact]
    public void DesertedBeach_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var bus = new ReplacementBus();
        SeedLands(_alice, 2);
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "exactly 2 other lands meets the ≥ 2 threshold, so it enters untapped");
    }

    [Fact]
    public void DesertedBeach_EntersUntapped_WhenControllerHasThreeOtherLands()
    {
        var bus = new ReplacementBus();
        SeedLands(_alice, 3);
        var land = MakeWithBus(_alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "3 other lands is ≥ 2, so Deserted Beach enters untapped");
    }

    [Fact]
    public void DesertedBeach_PredicateExcludesSelf()
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
            "the slow land excludes itself, so only 1 other land ⇒ tapped");
    }

    [Fact]
    public void DesertedBeach_EntersTapped_WhenOnlyOpponentHasManyLands()
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
    public void DesertedBeach_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg constructs without a
        // ReplacementBus, so the ETB-tapped predicate isn't wired. Prod load
        // wires it from oracle text via ConditionalEntersTappedBinder.
        var land = NamedCardFactory.Create("Deserted Beach", _alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Deserted Beach");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }
}
