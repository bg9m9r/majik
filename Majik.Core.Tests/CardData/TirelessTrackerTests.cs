using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TirelessTrackerFactory"/>.
///
/// Covers:
///   - Card identity (name, type, mana cost, subtypes Human + Scout, 3/2,
///     owner / controller, exactly one TriggeredAbility + one ActivatedAbility).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - End-to-end landfall trigger: land ETB under controller → exactly one
///     Clue token appears on the battlefield (verified through
///     ZoneService + TriggerManager + Stack resolution).
///   - Multiple lands ETB → one Clue per land (additive trigger fires).
///   - Activated ability: paying {2} + sacrificing a Clue puts a +1/+1
///     counter on Tireless Tracker; the Clue moves from battlefield to
///     graveyard.
///   - The created Clue's own activated ability ({2}, Sacrifice: Draw a
///     card) works as advertised — drawing 1 card from library.
///   - Trigger does NOT fire when the entering permanent is a non-land
///     (creature ETB → no Clue).
///   - Trigger does NOT fire when an opponent controls the entering land
///     (oracle: "under YOUR control").
/// </summary>
public class TirelessTrackerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TirelessTracker_Identity_HumanScout32At2G()
    {
        var t = TirelessTrackerFactory.Create(_alice);

        t.Name.Should().Be("Tireless Tracker");
        t.ManaCost.Should().Be("{2}{G}");
        t.HasType(CardType.Creature).Should().BeTrue();
        t.HasSubtype(CardSubtype.Human).Should().BeTrue();
        t.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        t.BasePower.Should().Be(3);
        t.BaseToughness.Should().Be(2);
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);

        // One landfall-style trigger + one activated +1/+1 ability.
        t.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        t.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TirelessTracker_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Tireless Tracker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Tireless Tracker");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Triggered ability — landfall Clue
    // -----------------------------------------------------------------------

    [Fact]
    public void LandEntersUnderController_CreatesOneClueToken()
    {
        var (zones, stack, triggers) = BuildEngine();

        var tracker = TirelessTrackerFactory.Create(_alice, zones, triggers);
        tracker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tracker);

        // Play a Forest under Alice's control via ZoneService — the
        // CardMovedEvent it publishes drives the trigger.
        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1, "exactly one landfall trigger should be queued");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(1, "Tireless Tracker creates a single Clue token per land ETB");
        clues[0].IsToken.Should().BeTrue();
    }

    [Fact]
    public void TwoLandsEnter_CreatesTwoClueTokens()
    {
        var (zones, stack, triggers) = BuildEngine();

        var tracker = TirelessTrackerFactory.Create(_alice, zones, triggers);
        tracker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tracker);

        for (int i = 0; i < 2; i++)
        {
            var land = new Land($"Forest {i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
            land.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(land);
            land.SetZone(ZoneType.Hand);
            zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

            // Drain pending triggers between lands so the second land's
            // event is queued fresh (mirrors APNAP draining between
            // priority windows — CR 603.3).
            triggers.PutPendingTriggersOnStack(_alice);
            while (stack.Count > 0) stack.Pop()!.Resolve();
        }

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(2, "two landfall triggers across two land ETBs → two Clue tokens");
    }

    [Fact]
    public void NonLandEnters_NoClueCreated()
    {
        var (zones, _, triggers) = BuildEngine();

        var tracker = TirelessTrackerFactory.Create(_alice, zones, triggers);
        tracker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tracker);

        // A creature ETB does not satisfy the "a land enters" predicate.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "the landfall trigger gates on HasType(Land); a creature ETB doesn't match");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Clue))
            .Should().BeFalse();
    }

    [Fact]
    public void LandEntersUnderOpponent_NoClueCreated()
    {
        var (zones, _, triggers) = BuildEngine();

        // Alice controls Tireless Tracker.
        var tracker = TirelessTrackerFactory.Create(_alice, zones, triggers);
        tracker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tracker);

        // Bob plays a land — oracle says "under YOUR control", so Alice's
        // Tracker must NOT trigger.
        var bobForest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobForest, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "opponent's land does not satisfy 'under your control'");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Clue))
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Activated ability — sac Clue → +1/+1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_SacrificeClue_PutsPlusOnePlusOneCounter_AndMovesClueToGraveyard()
    {
        var alice = new Player("Alice", 20);

        var tracker = TirelessTrackerFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(tracker);
        tracker.SetZone(ZoneType.Battlefield);

        // Seed a Clue on Alice's battlefield. Use TokenFactory so it has
        // the real Clue subtype and the {2}, sac: draw activated ability.
        var clue = Majik.Core.Tokens.TokenFactory.CreateClue(alice);
        clue.IsTapped.Should().BeFalse();
        alice.Zones.Battlefield.GetCards().Should().Contain(clue);

        // Pay the mana cost ({2}) into Alice's pool. ManaPool.AddManaForTest
        // is not available in this codebase — we add directly via the
        // ManaPool's Add API (mirrors how other tests pre-load mana).
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("2"));

        // Find the {2}, sac-Clue activated ability.
        var ability = tracker.Abilities.OfType<TirelessTrackerActivatedAbility>().Single();
        ability.SacrificeChoice.Target = clue;

        // Pay each cost in order, then run the effects (mirrors how
        // other named-factory tests execute activated abilities).
        foreach (var cost in ability.Costs) cost.Pay(alice);
        foreach (var effect in ability.Effects) effect.Execute();

        tracker.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the resolved effect puts a +1/+1 counter on Tireless Tracker");

        alice.Zones.Battlefield.GetCards().Should().NotContain(clue,
            "the Clue was sacrificed");
        alice.Zones.Graveyard.GetCards().Should().Contain(clue,
            "sacrificed permanents go to their owner's graveyard");
        clue.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ActivateClueToken_PaysTwoAndSacs_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        // Seed a card on top of the library so the draw is observable.
        var topDeck = new Creature("Top Bear", "1G", 2, 2);
        topDeck.SetOwner(alice);
        alice.Zones.Library.AddCard(topDeck);
        topDeck.SetZone(ZoneType.Library);

        var clue = Majik.Core.Tokens.TokenFactory.CreateClue(alice);
        clue.IsTapped.Should().BeFalse();

        // Activated ability on the Clue itself: {2}, Sacrifice: Draw a card.
        var clueAbility = clue.Abilities.OfType<ActivatedAbility>().Single();

        // Pay {2} into Alice's pool, then run the cost + effect.
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("2"));

        foreach (var cost in clueAbility.Costs) cost.Pay(alice);
        foreach (var effect in clueAbility.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topDeck,
            "Clue's sac-draw moves the top card of Alice's library to her hand");
        alice.Zones.Library.GetCards().Should().NotContain(topDeck);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
