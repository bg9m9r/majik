using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AsylumVisitorFactory"/> (Shadows over Innistrad,
/// {1}{B}).
///
/// Creature — Vampire Wizard 3/1. Oracle text:
///   "At the beginning of each player's upkeep, if that player has no cards in
///    hand, you draw a card and you lose 1 life.
///    Madness {1}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Each-player's-upkeep trigger fires on both the controller's AND an
///     opponent's upkeep (intervening-if gated on that player's empty hand).
///   - Intervening-if (CR 603.4): trigger does NOT queue when the upkeep
///     player has cards in hand; queues when they have none.
///   - Resolution: the CONTROLLER (not the upkeep player) draws a card and
///     loses 1 life.
///   - Madness {1}{B} discard → exile replacement registered when a
///     ReplacementBus is supplied.
/// </summary>
[Trait("Color", "B")]
public class AsylumVisitorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity / dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasCreatureShape_VampireWizard_3_1_AtOneB()
    {
        var visitor = AsylumVisitorFactory.Create(_alice);

        visitor.Should().BeOfType<Creature>();
        visitor.Name.Should().Be("Asylum Visitor");
        visitor.ManaCost.Should().Be("{1}{B}");
        visitor.HasType(CardType.Creature).Should().BeTrue();
        visitor.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        visitor.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        visitor.BasePower.Should().Be(3);
        visitor.BaseToughness.Should().Be(1);
        visitor.Owner.Should().BeSameAs(_alice);
        visitor.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AsylumVisitor()
    {
        var card = NamedCardFactory.Create("Asylum Visitor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Asylum Visitor");
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Create_HasUpkeepTrigger_OnlyOnBattlefield()
    {
        var visitor = AsylumVisitorFactory.Create(_alice);

        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    // -------------------------------------------------------------------------
    // Each-player's-upkeep trigger + intervening-if (CR 603.4)
    // -------------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_ControllerHasEmptyHand_Queues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var visitor = AsylumVisitorFactory.Create(_alice, triggers);
        visitor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(visitor);

        // Alice (the upkeep player) has no cards in hand → intervening-if holds.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1,
            "Alice's upkeep and Alice has no cards in hand");
    }

    [Fact]
    public void UpkeepTrigger_UpkeepPlayerHasCards_DoesNotQueue()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var visitor = AsylumVisitorFactory.Create(_alice, triggers);
        visitor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(visitor);

        // Bob (the upkeep player) holds a card → intervening-if fails.
        var grip = new Instant("Lightning Bolt", "R") { Owner = _bob };
        _bob.Zones.Hand.AddCard(grip);
        grip.SetZone(ZoneType.Hand);

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "intervening-if: Bob has a card in hand, the trigger doesn't go on the stack");
    }

    [Fact]
    public void UpkeepTrigger_OpponentEmptyHand_Queues_AndControllerDrawsLosesLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice controls the Visitor; put a card on top of her library so the
        // controller-draw is observable.
        var topCard = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var visitor = AsylumVisitorFactory.Create(_alice, triggers);
        visitor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(visitor);

        // Bob's upkeep, Bob has no cards in hand → each-player trigger queues.
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1, "each player's upkeep includes Bob's");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CONTROLLER (Alice) draws and loses 1 life — NOT the upkeep player.
        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the Visitor's controller draws, not the upkeep player");
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1,
            "the Visitor's controller loses 1 life");
        _bob.LifeTotal.Should().Be(bobLifeBefore,
            "the upkeep player is untouched");
    }

    [Fact]
    public void UpkeepTrigger_DoesNotFireOnNonUpkeepSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var visitor = AsylumVisitorFactory.Create(_alice, triggers);
        visitor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(visitor);

        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(0, "only Upkeep step matters");
    }

    // -------------------------------------------------------------------------
    // Madness {1}{B} (CR 702.35)
    // -------------------------------------------------------------------------

    [Fact]
    public void Madness_DiscardRedirectsToExile_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        _alice.AttachReplacementBus(replacements);

        var visitor = AsylumVisitorFactory.Create(_alice, triggers: null, replacements: replacements);
        _alice.Zones.Hand.AddCard(visitor);
        visitor.SetZone(ZoneType.Hand);

        // Discard funnel = Hand → Graveyard ZoneMoveIntent; Madness replacement
        // redirects it to Exile (castable for {1}{B}).
        var intent = new ZoneMoveIntent(visitor, ZoneType.Hand, ZoneType.Graveyard);
        var replaced = replacements.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.ToZone.Should().Be(ZoneType.Exile,
            "Madness redirects the discard into exile (CR 702.35)");
    }

    [Fact]
    public void Madness_AltCost_IsOneB()
    {
        // ManaCost.ToString() renders compactly ("1B"); assert the parsed cost
        // matches the printed Madness cost {1}{B}.
        AsylumVisitorFactory.MadnessAltCost.AlternativeManaCost
            .Should().Be(Majik.Core.ValueObjects.ManaCost.Parse("{1}{B}"));
    }
}
