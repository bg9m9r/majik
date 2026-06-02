using System;
using System.Linq;
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
/// Unit tests for <see cref="GeierReachSanitariumFactory"/> (Eldritch Moon).
///
/// Geier Reach Sanitarium — Legendary Land.
///   "{T}: Add {C}.
///    {2}, {T}: Each player draws a card, then discards a card."
///
/// Covers:
/// - Identity (Legendary Land, owner/controller) + <see cref="NamedCardFactory"/>
///   dispatch.
/// - One {C} mana ability (from the embedded JSON).
/// - One non-mana <see cref="ActivatedAbility"/> ({2}, {T}: each player draws a
///   card, then discards a card).
/// - Single-arg path: only the controller draws+discards.
/// - allPlayersResolver path: every player draws a card, THEN every player
///   discards a card (CR 121.1 sequencing — all draws complete before any
///   discard).
/// </summary>
public class GeierReachSanitariumTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler-{p.Name}-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    private static void SeedHand(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Hand-{p.Name}-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GeierReachSanitarium_Identity()
    {
        var land = GeierReachSanitariumFactory.Create(_alice);

        land.Name.Should().Be("Geier Reach Sanitarium");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Geier Reach Sanitarium is a Legendary Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GeierReachSanitarium()
    {
        var card = NamedCardFactory.Create("Geier Reach Sanitarium", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Geier Reach Sanitarium");
        card.HasType(CardType.Land).Should().BeTrue();

        // No ETB trigger — enters untapped.
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Geier Reach Sanitarium has no enters-the-battlefield trigger");

        // One {C} mana ability (from JSON).
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the {T}: Add {C} mana ability");

        // One non-mana activated ability: {2}, {T}: each player draws a card,
        // then discards a card. (ManaAbility is not an ActivatedAbility subclass.)
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}, {T}: each player draws then discards activated ability");
    }

    [Fact]
    public void GeierReachSanitarium_HasOneColorlessManaAbility()
    {
        var land = GeierReachSanitariumFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(1, "single {T}: Add {C}");
        var ma = mas[0];
        ma.ManaGenerated.Generic.Should().Be(1);
        ma.ManaGenerated.TotalValue.Should().Be(1);
        ma.ManaGenerated.White.Should().Be(0);
        ma.ManaGenerated.Blue.Should().Be(0);
        ma.ManaGenerated.Black.Should().Be(0);
        ma.ManaGenerated.Red.Should().Be(0);
        ma.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Each player draws a card, then discards a card.
    // -----------------------------------------------------------------------

    [Fact]
    public void GeierReachSanitarium_Activated_SingleArg_ControllerDrawsAndDiscards()
    {
        var land = GeierReachSanitariumFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SeedLibrary(_alice, 5);
        SeedHand(_alice, 2);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        var libBefore = _alice.Zones.Library.GetCards().Count();

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Drew one (lib -1), then discarded one (hand net unchanged: +1 -1).
        _alice.Zones.Library.GetCards().Count().Should().Be(libBefore - 1,
            "controller draws one card");
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "controller draws a card then discards a card → net hand size unchanged");
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(1,
            "the discarded card lands in the graveyard");
    }

    [Fact]
    public void GeierReachSanitarium_Activated_AllPlayers_EachDrawsThenDiscards()
    {
        var land = GeierReachSanitariumFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);
        SeedHand(_alice, 1);
        SeedHand(_bob, 1);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Each: lib -1 (drew), hand net unchanged (drew 1, discarded 1),
        // graveyard +1.
        _alice.Zones.Library.GetCards().Count().Should().Be(4);
        _bob.Zones.Library.GetCards().Count().Should().Be(4);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1);
        _bob.Zones.Hand.GetCards().Count().Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(1, "Alice discards one");
        _bob.Zones.Graveyard.GetCards().Count().Should().Be(1, "Bob discards one");
    }

    [Fact]
    public void GeierReachSanitarium_Activated_EmptyLibrary_DrawsNothing_ThenDiscardsFromHand()
    {
        // Empty library: the draw flags the SBA loss (CR 704.5b) but the
        // "then discards a card" still happens from whatever is in hand
        // (CR 701.16a — discard up to one).
        var land = GeierReachSanitariumFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SeedHand(_alice, 2); // no library

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the SBA loss (CR 704.5b)");
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(1,
            "still discards one card from the existing hand");
    }
}
