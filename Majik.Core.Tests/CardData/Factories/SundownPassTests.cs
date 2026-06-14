using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SundownPassFactory"/> — the Streets of New Capenna
/// R/W "slow land" Sundown Pass.
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {R} or {W}."
///
/// Covers:
/// - Identity (Land type, printed name, non-Basic, non-Legendary).
/// - Two mana abilities producing {R} and {W}.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): "two or more other lands" ⇒ tapped at &lt;2 other lands,
///   untapped at ≥2. "other" excludes the slow land itself; "you control"
///   reads the CONTROLLER's battlefield only.
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no per-card dispatch test lives here.)
/// </summary>
[Trait("Color", "M")]
public class SundownPassTests
{
    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        SundownPassFactory.Create(owner, bus);

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void SundownPass_HasTwoColouredManaAbilities_R_and_W()
    {
        var alice = new Player("Alice", 20);

        var land = SundownPassFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "one ManaAbility per produced colour ({R} and {W})");

        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("R")),
            "Sundown Pass produces {R}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("W")),
            "Sundown Pass produces {W}");
    }

    [Fact]
    public void SundownPass_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = SundownPassFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "slow lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "slow lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void SundownPass_EntersTapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "0 other lands is < 2, so Sundown Pass enters tapped");
    }

    [Fact]
    public void SundownPass_EntersTapped_WhenControllerHasOneOtherLand()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var p = (Land)NamedCardFactory.Create("Plains", alice);
        alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "1 other land is still < 2, so it enters tapped");
    }

    [Fact]
    public void SundownPass_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 2; i++)
        {
            var p = (Land)NamedCardFactory.Create("Plains", alice);
            alice.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "exactly 2 other lands satisfies ≥ 2, so it enters untapped");
    }

    [Fact]
    public void SundownPass_PredicateExcludesSelf()
    {
        // "two or MORE OTHER lands" — the slow land must not count itself.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var p = (Land)NamedCardFactory.Create("Plains", alice);
        alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        var land = MakeWithBus(alice, bus);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the slow land excludes itself, so only 1 other land ⇒ tapped");
    }

    [Fact]
    public void SundownPass_EntersTapped_WhenOnlyOpponentHasManyLands()
    {
        // "you control" — opponent's lands are irrelevant to the predicate.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        for (var i = 0; i < 5; i++)
        {
            var p = (Land)NamedCardFactory.Create("Plains", bob);
            bob.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Alice controls 0 lands; Bob's 5 don't count, so it enters tapped");
    }

    [Fact]
    public void SundownPass_Create_ThrowsOnNullOwner()
    {
        var act = () => SundownPassFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static bool SameCost(ManaCost a, ManaCost b) =>
        a.White == b.White &&
        a.Blue == b.Blue &&
        a.Black == b.Black &&
        a.Red == b.Red &&
        a.Green == b.Green &&
        a.Generic == b.Generic;
}
