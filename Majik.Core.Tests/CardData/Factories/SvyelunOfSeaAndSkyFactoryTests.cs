using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SvyelunOfSeaAndSkyFactory"/>.
///
/// Svyelun of Sea and Sky (Kaldheim, {1}{U}{U}). Legendary Creature —
/// Merfolk God 3/4. Oracle (verified against Scryfall 2026-06-02):
///   "Svyelun has indestructible as long as you control at least two other
///    Merfolk.
///    Whenever Svyelun attacks, draw a card.
///    Other Merfolk you control have ward {1}."
///
/// Coverage:
/// - Identity (name, supertype/types/subtypes, cost, colour, P/T, owner).
/// - NamedCardFactory dispatch.
/// - Conditional indestructible (CR 702.12 / 704.5): granted only while the
///   controller controls >= 2 OTHER Merfolk.
/// - Attack-trigger draw (CR 508.1f / 120.2): the controller draws a card.
/// - Ward {1} grant (CR 702.21): other controller-Merfolk gain the Ward
///   keyword marker; self / opponents' Merfolk / non-Merfolk do not.
/// </summary>
[Trait("Color", "U")]
public class SvyelunOfSeaAndSkyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeMerfolk(Player owner, string name = "Cursecatcher")
    {
        var c = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Merfolk });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature MakeNonMerfolk(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature PlaceSvyelun(Player owner, IEventBus? bus = null,
        ContinuousEffectsService? svc = null, TriggerManager? triggers = null)
    {
        // The grant lifecycles attach inside Create while Svyelun is not yet
        // on the battlefield; in production the ZoneManager move publishes a
        // CardMovedEvent that registers them. Always supply a bus and
        // simulate that move so the count-gated indestructible grant + ward
        // grant register exactly as they do in a live game.
        bus ??= new EventBus();
        var c = SvyelunOfSeaAndSkyFactory.Create(owner, bus, svc, triggers);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        if (svc != null) c.ActiveEffects = svc;
        bus.Publish(new CardMovedEvent(c, ZoneType.Stack, ZoneType.Battlefield));
        return c;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void Svyelun_Identity()
    {
        var c = SvyelunOfSeaAndSkyFactory.Create(_alice);

        c.Name.Should().Be("Svyelun of Sea and Sky");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.God).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}{U}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Svyelun_DispatchesViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Svyelun of Sea and Sky", _alice);
        c.Should().NotBeNull();
        c!.Name.Should().Be("Svyelun of Sea and Sky");
    }

    // ── Conditional indestructible ──────────────────────────────────────

    [Fact]
    public void Svyelun_NotIndestructible_WithoutTwoOtherMerfolk()
    {
        IndestructibleGrantRegistry.Clear();

        var svyelun = PlaceSvyelun(_alice);
        MakeMerfolk(_alice); // only one OTHER Merfolk

        IndestructibleGrantRegistry.HasGrantedIndestructible(svyelun).Should().BeFalse(
            "Svyelun gains indestructible only while its controller controls at " +
            "least two OTHER Merfolk (CR 702.12).");
    }

    [Fact]
    public void Svyelun_Indestructible_WithTwoOtherMerfolk()
    {
        IndestructibleGrantRegistry.Clear();

        var svyelun = PlaceSvyelun(_alice);
        MakeMerfolk(_alice, "Merfolk A");
        MakeMerfolk(_alice, "Merfolk B");

        // The grant predicate counts OTHER Merfolk lazily at the destroy gate,
        // so no re-sync event is required as Merfolk enter.
        IndestructibleGrantRegistry.HasGrantedIndestructible(svyelun).Should().BeTrue(
            "two OTHER Merfolk are controlled, so Svyelun has indestructible.");
    }

    [Fact]
    public void Svyelun_Indestructible_DoesNotCountOpponentMerfolk()
    {
        IndestructibleGrantRegistry.Clear();

        var svyelun = PlaceSvyelun(_alice);
        MakeMerfolk(_alice, "Mine");
        MakeMerfolk(_bob, "Theirs");

        IndestructibleGrantRegistry.HasGrantedIndestructible(svyelun).Should().BeFalse(
            "'you control' excludes the opponent's Merfolk (CR 109.5).");
    }

    // ── Attack-trigger draw ─────────────────────────────────────────────

    [Fact]
    public void Svyelun_HasAttackDrawTrigger()
    {
        var svyelun = SvyelunOfSeaAndSkyFactory.Create(_alice);
        svyelun.SetZone(ZoneType.Battlefield);

        var drawTrigger = svyelun.Abilities.OfType<TriggeredAbility>()
            .SingleOrDefault(t => t.Effects.Any(e =>
                e.Description.Contains("draw", System.StringComparison.OrdinalIgnoreCase)));

        drawTrigger.Should().NotBeNull(
            "Svyelun has 'Whenever Svyelun attacks, draw a card' (CR 508.1f / 120.2).");
    }

    [Fact]
    public void Svyelun_AttackTrigger_DrawsACard()
    {
        // Stock Alice's library so a draw has a card to move.
        var topCard = new Creature("Reservoir", "{U}", 1, 1);
        topCard.SetOwner(_alice);
        topCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);

        var svyelun = SvyelunOfSeaAndSkyFactory.Create(_alice);
        svyelun.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(svyelun);

        var drawTrigger = svyelun.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e =>
                e.Description.Contains("draw", System.StringComparison.OrdinalIgnoreCase)));

        var handBefore = _alice.Zones.Hand.Count;
        drawTrigger.Resolve();

        _alice.Zones.Hand.Count.Should().Be(handBefore + 1,
            "the attack trigger draws a card for Svyelun's controller (CR 120.2).");
    }

    // ── Ward {1} grant ──────────────────────────────────────────────────

    [Fact]
    public void Svyelun_GrantsWard_ToOtherControllerMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var otherMerfolk = MakeMerfolk(_alice);
        otherMerfolk.ActiveEffects = svc;

        var svyelun = PlaceSvyelun(_alice, null, svc);

        otherMerfolk.HasEffectiveKeyword("Ward").Should().BeTrue(
            "other Merfolk you control have ward {1} (CR 702.21).");
    }

    [Fact]
    public void Svyelun_DoesNotGrantWard_ToSelf()
    {
        var svc = new ContinuousEffectsService();
        var svyelun = PlaceSvyelun(_alice, null, svc);

        svyelun.HasEffectiveKeyword("Ward").Should().BeFalse(
            "printed 'Other Merfolk' excludes Svyelun itself (CR 109.5).");
    }

    [Fact]
    public void Svyelun_DoesNotGrantWard_ToOpponentMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk(_bob);
        bobMerfolk.ActiveEffects = svc;

        var svyelun = PlaceSvyelun(_alice, null, svc);

        bobMerfolk.HasEffectiveKeyword("Ward").Should().BeFalse(
            "controller-scoped grant — Bob's Merfolk are unaffected.");
    }

    [Fact]
    public void Svyelun_DoesNotGrantWard_ToNonMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonMerfolk(_alice);
        bears.ActiveEffects = svc;

        var svyelun = PlaceSvyelun(_alice, null, svc);

        bears.HasEffectiveKeyword("Ward").Should().BeFalse(
            "only Merfolk gain ward.");
    }
}
