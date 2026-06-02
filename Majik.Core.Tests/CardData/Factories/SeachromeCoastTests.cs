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
/// Tests for <see cref="SeachromeCoastFactory"/> — the Scars of Mirrodin
/// "fast land" Seachrome Coast.
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {W} or {U}."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic, non-Legendary).
/// - Two mana abilities producing {W} and {U}.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): "two or fewer other lands" ⇒ untapped at ≤2 other lands,
///   tapped at ≥3. "other" excludes the fast land itself; "you control"
///   reads the CONTROLLER's battlefield only.
/// - <see cref="NamedCardFactory"/> dispatch resolves the printed name.
/// </summary>
[Trait("Color", "C")]
public class SeachromeCoastTests
{
    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        SeachromeCoastFactory.Create(owner, bus);

    private static Land SeedLands(Player controller, int count)
    {
        // Seed `count` controller-owned generic lands (use basics so they
        // are real Land permanents the predicate will tally).
        for (var i = 0; i < count; i++)
        {
            var land = (Land)NamedCardFactory.Create("Plains", controller);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        return SeachromeCoastFactory.Create(controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeachromeCoast_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = SeachromeCoastFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Seachrome Coast");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void SeachromeCoast_IsNotBasic_NotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = SeachromeCoastFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "fast lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void SeachromeCoast_HasTwoColouredManaAbilities_W_and_U()
    {
        var alice = new Player("Alice", 20);

        var land = SeachromeCoastFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "one ManaAbility per produced colour ({W} and {U})");

        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("W")),
            "Seachrome Coast produces {W}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("U")),
            "Seachrome Coast produces {U}");
    }

    [Fact]
    public void SeachromeCoast_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = SeachromeCoastFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "fast lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "fast lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or fewer other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void SeachromeCoast_EntersUntapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "0 other lands is ≤ 2, so Seachrome Coast enters untapped");
    }

    [Fact]
    public void SeachromeCoast_EntersUntapped_WhenControllerHasTwoOtherLands()
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
            "exactly 2 other lands is still ≤ 2, so it enters untapped");
    }

    [Fact]
    public void SeachromeCoast_EntersTapped_WhenControllerHasThreeOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 3; i++)
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
        after!.EntersTapped.Should().BeTrue(
            "3 other lands exceeds 2, so Seachrome Coast enters tapped");
    }

    [Fact]
    public void SeachromeCoast_PredicateExcludesSelf()
    {
        // "two or FEWER OTHER lands" — the fast land must not count itself.
        // Seed exactly 2 other lands, then place the fast land on the
        // battlefield too; the count must stay 2 (≤ 2 ⇒ untapped).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 2; i++)
        {
            var p = (Land)NamedCardFactory.Create("Plains", alice);
            alice.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        var land = MakeWithBus(alice, bus);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "the fast land excludes itself, so 2 other lands ⇒ untapped");
    }

    [Fact]
    public void SeachromeCoast_EntersUntapped_WhenOnlyOpponentHasManyLands()
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
        after!.EntersTapped.Should().BeFalse(
            "Alice controls 0 lands; Bob's 5 don't count, so it enters untapped");
    }
    [Fact]
    public void SeachromeCoast_Create_ThrowsOnNullOwner()
    {
        var act = () => SeachromeCoastFactory.Create(null!);
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
