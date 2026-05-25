using FluentAssertions;
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
/// Unit tests for <see cref="LeylineOfTheVoidFactory"/>.
///
/// Card: Leyline of the Void — Enchantment {2}{B}{B} (Guildpact +).
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "If a card would be put into an opponent's graveyard from anywhere,
///    exile it instead."
///
/// Covers:
///   - Identity / dispatch.
///   - Static replacement rewrites opponent graveyard moves to exile.
///   - Static replacement does NOT rewrite controller's own graveyard
///     moves (one-sided, distinct from Rest in Peace).
///   - Inert when not on the battlefield.
///   - No-owner card no-ops (defensive null guard).
/// </summary>
public class LeylineOfTheVoidTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Leyline_Identity()
    {
        var c = LeylineOfTheVoidFactory.Create(_alice);

        c.Name.Should().Be("Leyline of the Void");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Leyline()
    {
        var card = NamedCardFactory.Create("Leyline of the Void", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Leyline of the Void");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}{B}");
    }

    [Fact]
    public void Static_RewritesOpponentGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        PlaceOnBattlefield(leyline, _alice);

        var bobCard = new Creature("Bear", "{1}{G}", 2, 2);
        bobCard.SetOwner(_bob);

        var intent = new ZoneMoveIntent(bobCard, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_DoesNotRewriteControllersOwnGraveyardMove()
    {
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        PlaceOnBattlefield(leyline, _alice);

        var aliceCard = new Creature("Bear", "{1}{G}", 2, 2);
        aliceCard.SetOwner(_alice);

        var intent = new ZoneMoveIntent(aliceCard, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "Leyline is one-sided — controller's own graveyard moves pass through");
    }

    [Fact]
    public void Static_DoesNotAffectNonGraveyardMoves()
    {
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        PlaceOnBattlefield(leyline, _alice);

        var bobCard = new Creature("Bear", "{1}{G}", 2, 2);
        bobCard.SetOwner(_bob);

        var bounce = new ZoneMoveIntent(bobCard, ZoneType.Battlefield, ZoneType.Hand, _bob);
        bus.Apply(bounce)!.ToZone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Static_RewritesGraveyardMoveFromAnywhere_Library()
    {
        // Milled card — from Library → Graveyard.
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        PlaceOnBattlefield(leyline, _alice);

        var milled = new Creature("Some Card", "{2}{U}", 2, 2);
        milled.SetOwner(_bob);

        var intent = new ZoneMoveIntent(milled, ZoneType.Library, ZoneType.Graveyard, null);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile,
            "'from anywhere' includes library / hand source zones");
    }

    [Fact]
    public void Static_IsInert_WhileNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        // Don't place on battlefield.

        var bobCard = new Creature("Bear", "{1}{G}", 2, 2);
        bobCard.SetOwner(_bob);

        var intent = new ZoneMoveIntent(bobCard, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Static_NoOwner_NoOps()
    {
        var bus = new ReplacementBus();
        var leyline = LeylineOfTheVoidFactory.Create(_alice, bus);
        PlaceOnBattlefield(leyline, _alice);

        var orphan = new Creature("Orphan", "{1}{G}", 2, 2);
        // No owner set — defensive predicate must short-circuit.

        var intent = new ZoneMoveIntent(orphan, ZoneType.Battlefield, ZoneType.Graveyard, null);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "owner-less card → conservative no-op");
    }

    private static void PlaceOnBattlefield(Enchantment leyline, Player owner)
    {
        owner.Zones.Battlefield.AddCard(leyline);
        leyline.SetZone(ZoneType.Battlefield);
    }
}
