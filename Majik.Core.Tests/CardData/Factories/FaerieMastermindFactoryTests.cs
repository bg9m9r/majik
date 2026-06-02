using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Faerie Mastermind (March of the Machine: The Aftermath,
/// {1}{U}). Creature — Faerie Rogue 2/1. Oracle text (verified against
/// Scryfall):
///   "Flash
///    Flying
///    Whenever an opponent draws their second card each turn, you draw a card.
///    {3}{U}: Each player draws a card."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the same shape.
///   - Flash + Flying keyword markers.
///   - One triggered ability + one activated ability present on the card.
///   - The {3}{U} activated ability has a single {3}{U} mana cost.
///   - Mechanic: an opponent's FIRST draw each turn does not trigger; the
///     SECOND does → controller draws one card. The controller's own draws
///     never trigger. A third+ opponent draw does not retrigger.
///   - Mechanic: a TurnStartedEvent resets the per-opponent count so the next
///     turn's second opponent-draw triggers again.
/// </summary>
public class FaerieMastermindFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    [Fact]
    public void Identity_FaerieRogue_2_1_AtCost1U()
    {
        var fm = FaerieMastermindFactory.Create(_alice);

        fm.Name.Should().Be("Faerie Mastermind");
        fm.ManaCost.Should().Be("{1}{U}");
        fm.HasType(CardType.Creature).Should().BeTrue();
        fm.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        fm.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        fm.BasePower.Should().Be(2);
        fm.BaseToughness.Should().Be(1);
        fm.Owner.Should().BeSameAs(_alice);
        fm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Faerie Mastermind", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Faerie Mastermind");
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void HasFlash_Flying_OneTrigger_OneActivatedAbility()
    {
        var fm = FaerieMastermindFactory.Create(_alice);

        CombatAbilities.HasFlying(fm).Should().BeTrue();
        TimingRules.CanCastAtInstantSpeed(fm).Should().BeTrue();
        fm.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        var activated = fm.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1);
        var manaCost = activated[0].Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost;
        manaCost.Generic.Should().Be(3);
        manaCost.Blue.Should().Be(1);
    }

    [Fact]
    public void OpponentFirstDraw_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fm = FaerieMastermindFactory.Create(_alice, bus, triggers);
        fm.SetZone(ZoneType.Battlefield);

        var bobCard = NewCardInLibrary(_bob, "BobCard");

        // Bob's first draw of the turn — no trigger.
        bus.Publish(new CardDrawnEvent(bobCard, _bob));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void OpponentSecondDraw_Triggers_ControllerDrawsOneCard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fm = FaerieMastermindFactory.Create(_alice, bus, triggers);
        fm.SetZone(ZoneType.Battlefield);

        var aliceTop = NewCardInLibrary(_alice, "AliceTop");
        var bob1 = NewCardInLibrary(_bob, "Bob1");
        var bob2 = NewCardInLibrary(_bob, "Bob2");

        // Bob's first draw — no trigger.
        bus.Publish(new CardDrawnEvent(bob1, _bob));
        triggers.PendingCount.Should().Be(0);

        // Bob's second draw — Alice draws a card.
        bus.Publish(new CardDrawnEvent(bob2, _bob));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { aliceTop });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void OpponentThirdDraw_DoesNotRetrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fm = FaerieMastermindFactory.Create(_alice, bus, triggers);
        fm.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "AliceTop");
        var bob1 = NewCardInLibrary(_bob, "Bob1");
        var bob2 = NewCardInLibrary(_bob, "Bob2");
        var bob3 = NewCardInLibrary(_bob, "Bob3");

        bus.Publish(new CardDrawnEvent(bob1, _bob)); // no trigger
        bus.Publish(new CardDrawnEvent(bob2, _bob)); // triggers
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bus.Publish(new CardDrawnEvent(bob3, _bob)); // must not retrigger
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void ControllerOwnDraws_NeverTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fm = FaerieMastermindFactory.Create(_alice, bus, triggers);
        fm.SetZone(ZoneType.Battlefield);

        var a1 = NewCardInLibrary(_alice, "A1");
        var a2 = NewCardInLibrary(_alice, "A2");
        var a3 = NewCardInLibrary(_alice, "A3");

        // Alice (the controller) draws several cards — "an opponent draws"
        // never matches the controller, so no trigger.
        bus.Publish(new CardDrawnEvent(a1, _alice));
        bus.Publish(new CardDrawnEvent(a2, _alice));
        bus.Publish(new CardDrawnEvent(a3, _alice));

        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnSecondDrawTriggersAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fm = FaerieMastermindFactory.Create(_alice, bus, triggers);
        fm.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "AliceT1");
        NewCardInLibrary(_alice, "AliceT2");
        var b1 = NewCardInLibrary(_bob, "B1");
        var b2 = NewCardInLibrary(_bob, "B2");
        var b3 = NewCardInLibrary(_bob, "B3");
        var b4 = NewCardInLibrary(_bob, "B4");

        // Turn 1 — Bob's second draw triggers.
        bus.Publish(new CardDrawnEvent(b1, _bob));
        bus.Publish(new CardDrawnEvent(b2, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Turn boundary — reset the per-opponent count (CR 500.1).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — first opponent draw no longer counts as the "second".
        bus.Publish(new CardDrawnEvent(b3, _bob));
        triggers.PendingCount.Should().Be(0);

        bus.Publish(new CardDrawnEvent(b4, _bob));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void EachPlayerDraws_ActivatedAbility_DrawsForEveryPlayer()
    {
        var aliceTop = NewCardInLibrary(_alice, "AliceTop");
        var bobTop = NewCardInLibrary(_bob, "BobTop");

        var players = new[] { _alice, _bob };
        var fm = FaerieMastermindFactory.Create(
            _alice, eventBus: null, triggers: null, allPlayersResolver: () => players);

        var activated = fm.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in activated.Effects)
        {
            effect.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { aliceTop });
        _bob.Zones.Hand.GetCards().Should().Equal(new[] { bobTop });
    }
}
