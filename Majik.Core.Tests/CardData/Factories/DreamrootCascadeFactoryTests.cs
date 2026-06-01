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
/// Tests for <see cref="DreamrootCascadeFactory"/> — the Wilds of Eldraine
/// G/U "slow land" Dreamroot Cascade.
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {G} or {U}."
///
/// Complements <see cref="Majik.Core.Tests.CardData.DreamrootCascadeTests"/>
/// (identity + mana coverage) with the slow-land ETB-tapped predicate via
/// <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c): "two or MORE
/// other lands" ⇒ tapped at &lt;2 other lands, untapped at ≥2. "other"
/// excludes the slow land itself; "you control" reads the CONTROLLER's
/// battlefield only. This is the mirror image of the fast-land "two or fewer"
/// threshold exercised by <see cref="SeachromeCoastFactory"/>.
/// </summary>
public class DreamrootCascadeFactoryTests
{
    private static Land MakeWithBus(Player owner, ReplacementBus bus) =>
        DreamrootCascadeFactory.Create(owner, bus);

    private static void SeedLands(Player controller, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = (Land)NamedCardFactory.Create("Plains", controller);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }
    }

    // -----------------------------------------------------------------------
    // Mana abilities — G and U
    // -----------------------------------------------------------------------

    [Fact]
    public void DreamrootCascade_HasTwoColouredManaAbilities_G_and_U()
    {
        var alice = new Player("Alice", 20);

        var land = DreamrootCascadeFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "one ManaAbility per produced colour ({G} and {U})");

        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("G")),
            "Dreamroot Cascade produces {G}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("U")),
            "Dreamroot Cascade produces {U}");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void DreamrootCascade_EntersTapped_WhenControllerHasZeroOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "0 other lands is < 2, so Dreamroot Cascade enters tapped");
    }

    [Fact]
    public void DreamrootCascade_EntersTapped_WhenControllerHasOneOtherLand()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedLands(alice, 1);

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "1 other land is still < 2, so it enters tapped");
    }

    [Fact]
    public void DreamrootCascade_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedLands(alice, 2);

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "exactly 2 other lands meets the ≥ 2 threshold, so it enters untapped");
    }

    [Fact]
    public void DreamrootCascade_EntersUntapped_WhenControllerHasThreeOtherLands()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedLands(alice, 3);

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "3 other lands exceeds the ≥ 2 threshold, so it enters untapped");
    }

    [Fact]
    public void DreamrootCascade_PredicateExcludesSelf()
    {
        // "two or MORE OTHER lands" — the slow land must not count itself.
        // Seed exactly 1 other land, then place the slow land on the
        // battlefield too; the count must stay 1 (< 2 ⇒ tapped), proving the
        // card excludes itself rather than tallying to 2.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        SeedLands(alice, 1);

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
    public void DreamrootCascade_TappedState_IgnoresOpponentLands()
    {
        // "you control" — opponent's lands are irrelevant to the predicate.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        SeedLands(bob, 5);

        var land = MakeWithBus(alice, bus);

        var after = bus.Apply(new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield, Controller: alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Alice controls 0 lands; Bob's 5 don't count, so it enters tapped");
    }

    [Fact]
    public void DreamrootCascade_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — single-arg constructs without a
        // ReplacementBus, so the ETB-tapped predicate isn't wired (matches
        // every other ETB-replacement factory's shape-only posture). Prod
        // load wires it from oracle text via ConditionalEntersTappedBinder.
        var alice = new Player("Alice", 20);
        var land = NamedCardFactory.Create("Dreamroot Cascade", alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Dreamroot Cascade");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DreamrootCascade_Create_ThrowsOnNullOwner()
    {
        var act = () => DreamrootCascadeFactory.Create(null!);
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
