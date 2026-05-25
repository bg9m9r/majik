using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="VeteranExplorerFactory"/> (Mercadian Masques,
/// {1}{G}, Creature — Human Scout 1/2).
///
/// Covers:
///   - Card identity (Creature, Human Scout, 1/2, {1}{G}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Single dies-trigger attached with active zones Battlefield + Graveyard.
///   - Resolve: dying card's owner tutors up to two basic lands to
///     battlefield (CR 701.19a, no "tapped" qualifier).
///   - Resolve: each player in the allPlayersResolver walks the tutor.
///   - Resolve: fewer than two basics in library → tutors what's available.
///   - Resolve: zero basics in library → no-op.
/// </summary>
public class VeteranExplorerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void VeteranExplorer_IsCreatureWithHumanScout1_2_AtCost1G()
    {
        var card = VeteranExplorerFactory.Create(_alice);

        card.Name.Should().Be("Veteran Explorer");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VeteranExplorer()
    {
        var card = NamedCardFactory.Create("Veteran Explorer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Veteran Explorer");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void VeteranExplorer_HasExactlyOneTriggeredAbility()
    {
        var card = VeteranExplorerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one dies trigger on Veteran Explorer.");
    }

    [Fact]
    public void VeteranExplorer_DiesTrigger_ActiveZonesIncludeBattlefieldAndGraveyard()
    {
        var card = VeteranExplorerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve: each player tutors up to two basic lands
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DiesTrigger_OwnerTutorsUpToTwoBasicLandsToBattlefield()
    {
        // Alice has three basics in her library. Two should be tutored
        // out; the third stays.
        var forest1 = SeedBasicInLibrary("Forest", _alice);
        var forest2 = SeedBasicInLibrary("Forest", _alice);
        var forest3 = SeedBasicInLibrary("Forest", _alice);

        var card = VeteranExplorerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Count(c =>
            c.HasType(CardType.Land)).Should().Be(2,
                "exactly two basics tutored onto Alice's battlefield (CR 701.19a).");
        _alice.Zones.Library.GetCards().Should().Contain(forest3,
            "the third basic stays in the library — only up to two.");
        // Lands enter untapped — no "tapped" qualifier in Veteran Explorer's
        // oracle text.
        var landsBf = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .OfType<Permanent>()
            .ToList();
        landsBf.Should().OnlyContain(p => !p.IsTapped,
            "Veteran Explorer's tutor lands enter untapped.");
    }

    [Fact]
    public void DiesTrigger_FewerThanTwoBasics_TutorsWhatsAvailable()
    {
        var forest = SeedBasicInLibrary("Forest", _alice);

        var card = VeteranExplorerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Count(c =>
            c.HasType(CardType.Land)).Should().Be(1);
    }

    [Fact]
    public void DiesTrigger_NoBasicsInLibrary_IsNoOp()
    {
        // Alice's library has only nonbasic noise.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bears);

        var card = VeteranExplorerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "no basics → no-op (CR 701.19a — finding nothing is legal).");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void DiesTrigger_AllPlayersResolver_EachPlayerTutorsUpToTwoBasics()
    {
        // Both Alice and Bob have basics. Each should tutor up to two.
        var forestA = SeedBasicInLibrary("Forest", _alice);
        var forestA2 = SeedBasicInLibrary("Forest", _alice);
        var mountainB = SeedBasicInLibrary("Mountain", _bob);
        var mountainB2 = SeedBasicInLibrary("Mountain", _bob);

        var card = VeteranExplorerFactory.Create(
            _alice,
            triggers: null,
            allPlayersResolver: () => new[] { _alice, _bob });

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { forestA, forestA2 });
        _bob.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { mountainB, mountainB2 });
    }

    [Fact]
    public void DiesTrigger_NonBasicLandsAreNotEligible()
    {
        // Wastes IS a basic; Forest IS a basic; Strip Mine is NOT.
        var stripMine = new Land("Strip Mine");
        stripMine.SetOwner(_alice);
        stripMine.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(stripMine);

        var card = VeteranExplorerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Strip Mine is not a basic land card (CR 305.6).");
        _alice.Zones.Library.GetCards().Should().Contain(stripMine);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static ICard SeedBasicInLibrary(string name, Player owner)
    {
        var card = NamedCardFactory.Create(name, owner);
        card.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(card);
        return card;
    }
}
