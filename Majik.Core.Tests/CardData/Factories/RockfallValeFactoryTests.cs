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
/// Tests for <see cref="RockfallValeFactory"/> — the Innistrad: Midnight Hunt
/// R/G "slow land" Rockfall Vale.
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {R} or {G}."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic, non-Legendary).
/// - Two mana abilities producing {R} and {G}.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): "two or more other lands" ⇒ tapped at &lt;2 other lands,
///   untapped at ≥2. "other" excludes the slow land itself; "you control"
///   reads the CONTROLLER's battlefield only.
/// - <see cref="NamedCardFactory"/> dispatch resolves the printed name.
/// </summary>
public class RockfallValeFactoryTests
{
    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        RockfallValeFactory.Create(owner, bus);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RockfallVale_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = RockfallValeFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Rockfall Vale");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void RockfallVale_IsNotBasic_NotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = RockfallValeFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "slow lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void RockfallVale_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Rockfall Vale", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Rockfall Vale");
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void RockfallVale_HasTwoColouredManaAbilities_R_and_G()
    {
        var alice = new Player("Alice", 20);

        var land = RockfallValeFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "one ManaAbility per produced colour ({R} and {G})");

        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("R")),
            "Rockfall Vale produces {R}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("G")),
            "Rockfall Vale produces {G}");
    }

    [Fact]
    public void RockfallVale_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = RockfallValeFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "slow lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "slow lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void RockfallVale_EntersTapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "0 other lands is < 2, so Rockfall Vale enters tapped");
    }

    [Fact]
    public void RockfallVale_EntersTapped_WhenControllerHasOneOtherLand()
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
    public void RockfallVale_EntersUntapped_WhenControllerHasTwoOtherLands()
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
            "exactly 2 other lands meets the threshold, so it enters untapped");
    }

    [Fact]
    public void RockfallVale_PredicateExcludesSelf()
    {
        // "two or more OTHER lands" — the slow land must not count itself.
        // Seed exactly 1 other land, then place the slow land on the
        // battlefield too; the count must stay 1 (< 2 ⇒ tapped), proving the
        // slow land excludes itself from the tally.
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
    public void RockfallVale_EntersTapped_WhenOnlyOpponentHasManyLands()
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
    public void RockfallVale_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg constructs without a
        // ReplacementBus, so the ETB-tapped predicate isn't wired (matches
        // every other ETB-replacement factory's shape-only posture). Prod
        // load wires it from oracle text via ConditionalEntersTappedBinder.
        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Rockfall Vale", alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Rockfall Vale");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void RockfallVale_Create_ThrowsOnNullOwner()
    {
        var act = () => RockfallValeFactory.Create(null!);
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
