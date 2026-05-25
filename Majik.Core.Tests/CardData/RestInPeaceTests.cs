using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RestInPeaceFactory"/>.
///
/// Card: Rest in Peace — Enchantment {1}{W} (Avacyn Restored).
///   "When this enchantment enters, exile all graveyards."
///   "If a card or token would be put into a graveyard from anywhere,
///    exile it instead."
///
/// Covers:
///   - Identity / dispatch.
///   - Static replacement registered up-front; inert while in non-battlefield zones.
///   - ETB exile sweeps every supplied player's graveyard.
///   - Graveyard rewrite is unconditional (any source zone, any owner).
///   - Replacement gates off when Rest in Peace leaves the battlefield.
///   - Single-arg create is shape-only.
/// </summary>
public class RestInPeaceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RestInPeace_Identity()
    {
        var c = RestInPeaceFactory.Create(_alice);

        c.Name.Should().Be("Rest in Peace");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RestInPeace()
    {
        var card = NamedCardFactory.Create("Rest in Peace", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Rest in Peace");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}");
    }

    // -----------------------------------------------------------------------
    // ETB exile sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEtbExile_MovesAllGraveyardsToExile()
    {
        var c1 = new Creature("Bear", "{1}{G}", 2, 2);
        c1.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(c1);
        c1.SetZone(ZoneType.Graveyard);

        var c2 = new Creature("Wolf", "{1}{G}", 2, 2);
        c2.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(c2);
        c2.SetZone(ZoneType.Graveyard);

        RestInPeaceFactory.ResolveEtbExile(() => new[] { _alice, _bob }, zoneService: null);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(c1);
        _bob.Zones.Exile.GetCards().Should().Contain(c2);
        c1.Zone.Should().Be(ZoneType.Exile);
        c2.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void ResolveEtbExile_NoPlayers_NoOps()
    {
        var act = () => RestInPeaceFactory.ResolveEtbExile(allPlayersResolver: null, zoneService: null);
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Static graveyard rewrite
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_RewritesGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var rip = RestInPeaceFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(rip, _alice);

        var card = new Creature("Bear", "{1}{G}", 2, 2);
        card.SetOwner(_alice);

        var intent = new ZoneMoveIntent(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_RewritesOpponentGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var rip = RestInPeaceFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(rip, _alice);

        var bobCard = new Creature("Bear", "{1}{G}", 2, 2);
        bobCard.SetOwner(_bob);

        // Rewrite is symmetric — every grave move becomes exile.
        var intent = new ZoneMoveIntent(bobCard, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_DoesNotAffectNonGraveyardMoves()
    {
        var bus = new ReplacementBus();
        var rip = RestInPeaceFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(rip, _alice);

        var card = new Creature("Bear", "{1}{G}", 2, 2);
        card.SetOwner(_alice);

        var bounce = new ZoneMoveIntent(card, ZoneType.Battlefield, ZoneType.Hand, _alice);
        bus.Apply(bounce)!.ToZone.Should().Be(ZoneType.Hand,
            "bounce/exile/library moves are unaffected");
    }

    [Fact]
    public void Static_IsInert_WhileNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        var rip = RestInPeaceFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        // Leave Rest in Peace in its default zone — NOT placed on the battlefield.

        var card = new Creature("Bear", "{1}{G}", 2, 2);
        card.SetOwner(_alice);

        var intent = new ZoneMoveIntent(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "Rest in Peace's replacement is gated on its battlefield zone");
    }

    [Fact]
    public void Static_IsNotEndOfTurnExpirable()
    {
        var bus = new ReplacementBus();
        var rip = RestInPeaceFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(rip, _alice);

        bus.ExpireEndOfTurn();

        var card = new Creature("Bear", "{1}{G}", 2, 2);
        card.SetOwner(_alice);

        // Static still active after EOT sweep.
        var intent = new ZoneMoveIntent(card, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment rip, Player owner)
    {
        owner.Zones.Battlefield.AddCard(rip);
        rip.SetZone(ZoneType.Battlefield);
    }
}
