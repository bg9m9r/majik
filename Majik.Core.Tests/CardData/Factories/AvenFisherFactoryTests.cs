using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AvenFisherFactory"/> — Creature — Bird Soldier
/// {3}{U} 2/2 with Flying and a dies-triggered "you may draw a card".
///
/// Covers:
/// - Card identity (name, cost, type, subtypes, P/T, owner/controller).
/// - Mana value (CR 202.3 — {3}{U} = 4).
/// - Flying keyword marker present (CR 702.9).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one TriggeredAbility attached, active in Battlefield + Graveyard.
/// - Live dies trigger: fires on Battlefield → Graveyard and draws 1 card
///   from a stocked library (CR 603.6c / 700.4).
/// - No trigger on non-death zone changes (bounce, exile).
/// </summary>
public class AvenFisherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void StackLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Pile {i}", "{0}", 1, 1);
            c.SetOwner(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // ------------------------------------------------------------------
    // Shape / identity
    // ------------------------------------------------------------------

    [Fact]
    public void AvenFisher_IsCorrect_Identity()
    {
        var card = AvenFisherFactory.Create(_alice);

        card.Name.Should().Be("Aven Fisher");
        card.ManaCost.Should().Be("{3}{U}");
        card.ManaCostValue.TotalValue.Should().Be(4, "mana value of {3}{U} is 4 (CR 202.3)");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AvenFisher_HasFlying_Keyword()
    {
        var card = AvenFisherFactory.Create(_alice);

        card.Abilities
            .OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Aven Fisher has Flying (CR 702.9)");
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_AvenFisher()
    {
        var card = NamedCardFactory.Create("Aven Fisher", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Aven Fisher");
        var creature = (Creature)card;
        creature.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        creature.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Aven Fisher has exactly one triggered ability (the dies trigger)");
    }

    // ------------------------------------------------------------------
    // Triggered ability zones
    // ------------------------------------------------------------------

    [Fact]
    public void AvenFisher_DiesTrigger_IsActiveInGraveyardZone()
    {
        // The dies trigger must include Graveyard in its active zones because
        // ZoneService stamps card.Zone = Graveyard BEFORE publishing the
        // CardMovedEvent (same pattern as Stitcher's Supplier / Young Wolf).
        var card = AvenFisherFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "dies trigger must remain observable after zone stamp (CR 603.6c)");
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ------------------------------------------------------------------
    // Live dies trigger — draws 1 card
    // ------------------------------------------------------------------

    [Fact]
    public void AvenFisher_Dies_DrawsOneCard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Stock the library with 5 cards so the draw has something to take.
        StackLibrary(_alice, 5);
        _alice.Zones.Library.GetCards().Should().HaveCount(5);

        var fisher = AvenFisherFactory.Create(_alice, triggers);
        fisher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fisher);

        // Kill it: Battlefield → Graveyard via ZoneService.
        zones.MoveCard(fisher, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1,
            "the dies trigger must queue on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Library.GetCards().Should().HaveCount(4,
            "dies trigger draws 1 card: library 5 → 4");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "drawn card goes to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(fisher,
            "Aven Fisher itself is in the graveyard after dying");
    }

    // ------------------------------------------------------------------
    // No trigger on non-death zone changes
    // ------------------------------------------------------------------

    [Fact]
    public void AvenFisher_BouncedToHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var fisher = AvenFisherFactory.Create(_alice, triggers);
        fisher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fisher);

        // Bounce: battlefield → hand (NOT graveyard).
        zones.MoveCard(fisher, ZoneType.Battlefield, ZoneType.Hand, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on a bounce (Battlefield → Hand is not death)");
    }

    [Fact]
    public void AvenFisher_ExiledFromBattlefield_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var fisher = AvenFisherFactory.Create(_alice, triggers);
        fisher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fisher);

        // Exile: battlefield → exile (skips graveyard, not a death per CR 700.4).
        zones.MoveCard(fisher, ZoneType.Battlefield, ZoneType.Exile, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on Battlefield → Exile (not death per CR 700.4)");
    }
}
