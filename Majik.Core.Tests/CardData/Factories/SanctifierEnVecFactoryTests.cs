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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SanctifierEnVecFactory"/>.
///
/// Card: Sanctifier en-Vec — Creature — Human Cleric {W}{W} 2/2 (Time Spiral).
///   "Protection from black and from red"
///   "When this creature enters, exile all cards that are black or red
///    from all graveyards."
///   "If a black or red permanent, spell, or card not on the battlefield
///    would be put into a graveyard, exile it instead."
///
/// Mirrors <see cref="RestInPeaceFactory"/> (ETB exile-all-graveyards +
/// static graveyard→exile replacement) but FILTERED to black-or-red
/// objects, and adds the two protection qualities (CR 702.16).
/// </summary>
public class SanctifierEnVecFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch / keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctifierEnVec_Identity_AndPT_AndSubtypes()
    {
        var c = SanctifierEnVecFactory.Create(_alice);

        c.Name.Should().Be("Sanctifier en-Vec");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SanctifierEnVec_HasProtectionFromBlackAndRed()
    {
        var c = SanctifierEnVecFactory.Create(_alice);

        var qualities = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().Contain("black");
        qualities.Should().Contain("red");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SanctifierEnVec()
    {
        var card = NamedCardFactory.Create("Sanctifier en-Vec", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sanctifier en-Vec");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // ETB exile sweep — only black or red cards
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEtbExile_ExilesOnlyBlackOrRedCards_FromAllGraveyards()
    {
        var blackCard = new Creature("Gravedigger", "{3}{B}", 2, 2);
        blackCard.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(blackCard);
        blackCard.SetZone(ZoneType.Graveyard);

        var redCard = new Creature("Goblin", "{R}", 1, 1);
        redCard.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(redCard);
        redCard.SetZone(ZoneType.Graveyard);

        var greenCard = new Creature("Bear", "{1}{G}", 2, 2);
        greenCard.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(greenCard);
        greenCard.SetZone(ZoneType.Graveyard);

        SanctifierEnVecFactory.ResolveEtbExile(() => new[] { _alice, _bob }, zoneService: null);

        // Black + red gone; green stays.
        _alice.Zones.Exile.GetCards().Should().Contain(blackCard);
        _bob.Zones.Exile.GetCards().Should().Contain(redCard);
        _bob.Zones.Exile.GetCards().Should().NotContain(greenCard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(greenCard);
        blackCard.Zone.Should().Be(ZoneType.Exile);
        redCard.Zone.Should().Be(ZoneType.Exile);
        greenCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ResolveEtbExile_NoPlayers_NoOps()
    {
        var act = () => SanctifierEnVecFactory.ResolveEtbExile(allPlayersResolver: null, zoneService: null);
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Static graveyard rewrite — only black or red objects
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_RewritesBlackGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(c, _alice);

        var black = new Creature("Gravedigger", "{3}{B}", 2, 2);
        black.SetOwner(_bob);

        var intent = new ZoneMoveIntent(black, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_RewritesRedGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(c, _alice);

        var red = new Instant("Lightning Bolt", "{R}");
        red.SetOwner(_alice);

        var intent = new ZoneMoveIntent(red, ZoneType.Stack, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_DoesNotAffect_NonBlackNonRedGraveyardMove()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(c, _alice);

        var green = new Creature("Bear", "{1}{G}", 2, 2);
        green.SetOwner(_bob);

        var intent = new ZoneMoveIntent(green, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "non-black non-red objects head to the graveyard normally");
    }

    [Fact]
    public void Static_DoesNotAffect_NonGraveyardMoves()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(c, _alice);

        var black = new Creature("Gravedigger", "{3}{B}", 2, 2);
        black.SetOwner(_bob);

        var bounce = new ZoneMoveIntent(black, ZoneType.Battlefield, ZoneType.Hand, _bob);
        bus.Apply(bounce)!.ToZone.Should().Be(ZoneType.Hand,
            "bounce/exile/library moves are unaffected even for black cards");
    }

    [Fact]
    public void Static_IsInert_WhileNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        // Not placed on the battlefield.

        var black = new Creature("Gravedigger", "{3}{B}", 2, 2);
        black.SetOwner(_bob);

        var intent = new ZoneMoveIntent(black, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "the replacement is gated on the creature's battlefield zone");
    }

    [Fact]
    public void Static_IsNotEndOfTurnExpirable()
    {
        var bus = new ReplacementBus();
        var c = SanctifierEnVecFactory.Create(_alice, allPlayersResolver: null, replacements: bus,
            zoneService: null, triggers: null);
        PlaceOnBattlefield(c, _alice);

        bus.ExpireEndOfTurn();

        var black = new Creature("Gravedigger", "{3}{B}", 2, 2);
        black.SetOwner(_bob);

        var intent = new ZoneMoveIntent(black, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        var c = SanctifierEnVecFactory.Create(_alice);

        var black = new Creature("Gravedigger", "{3}{B}", 2, 2);
        black.SetOwner(_bob);

        var emptyBus = new ReplacementBus();
        var intent = new ZoneMoveIntent(black, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        emptyBus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "no replacement is registered on the single-arg path");
    }

    private static void PlaceOnBattlefield(Creature c, Player owner)
    {
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }
}
