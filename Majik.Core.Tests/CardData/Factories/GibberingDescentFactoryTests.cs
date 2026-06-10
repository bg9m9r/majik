using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GibberingDescentFactory"/> (Time Spiral,
/// {4}{B}{B}).
///
/// Enchantment. Oracle text:
///   "At the beginning of each player's upkeep, that player loses 1 life and
///    discards a card.
///    Hellbent — Skip your upkeep step if you have no cards in hand.
///    Madness {2}{B}{B} (…)"
///
/// Covers ONLY the non-madness body (Madness is intrinsic via MadnessCatalog +
/// the Fx.DiscardCard funnel — see MadnessDiscardFunnelTests):
///   - Identity / shape ({4}{B}{B} Enchantment).
///   - Each-player's-upkeep trigger: the upkeep player loses 1 life AND
///     discards a card (CR 603.1 / CR 500.4).
///   - Hellbent skip (CR 702.46): the trigger is suppressed on the CONTROLLER's
///     own upkeep when the controller has no cards in hand (the upkeep step is
///     skipped). It still fires on the controller's upkeep when they hold a
///     card, and ALWAYS on an opponent's upkeep regardless of the controller's
///     hand.
/// </summary>
[Trait("Color", "B")]
public class GibberingDescentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Instant Grip(Player owner, string name = "Lightning Bolt")
    {
        var c = new Instant(name, "R") { Owner = owner };
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity / shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasEnchantmentShape_AtFourBB()
    {
        var card = GibberingDescentFactory.Create(_alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Gibbering Descent");
        card.ManaCost.Should().Be("{4}{B}{B}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasUpkeepTrigger_OnlyOnBattlefield()
    {
        var card = GibberingDescentFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    // -------------------------------------------------------------------------
    // Each-player's-upkeep trigger — loses 1 life AND discards a card
    // -------------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_OpponentUpkeep_TheyLoseLifeAndDiscard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = GibberingDescentFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        // Bob (the upkeep player) holds two cards; one will be discarded.
        var keep = Grip(_bob, "Counterspell");
        var toss = Grip(_bob, "Lightning Bolt");
        var bobLifeBefore = _bob.LifeTotal;

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1, "each player's upkeep includes Bob's");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - 1, "the upkeep player loses 1 life");
        _bob.Zones.Hand.GetCards().Should().HaveCount(1, "the upkeep player discarded one card");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(1);
        _alice.LifeTotal.Should().Be(20, "the controller is untouched on an opponent's upkeep");
    }

    [Fact]
    public void UpkeepTrigger_ControllerUpkeep_WithCardInHand_FiresAndAffectsController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = GibberingDescentFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        // Alice holds a card → Hellbent does NOT skip her upkeep.
        Grip(_alice);
        var aliceLifeBefore = _alice.LifeTotal;

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1, "Alice holds a card, her upkeep is not skipped");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1);
        _alice.Zones.Hand.GetCards().Should().BeEmpty("Alice discarded her only card");
    }

    // -------------------------------------------------------------------------
    // Hellbent — Skip your upkeep step if you have no cards in hand (CR 702.46)
    // -------------------------------------------------------------------------

    [Fact]
    public void Hellbent_ControllerUpkeep_EmptyHand_TriggerSuppressed()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = GibberingDescentFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        // Alice (controller) has no cards in hand → her upkeep step is skipped,
        // so the each-player trigger does NOT fire on her own upkeep.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        var aliceLifeBefore = _alice.LifeTotal;

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));

        triggers.PendingCount.Should().Be(0,
            "Hellbent skips the controller's upkeep step when they hold no cards");
        _alice.LifeTotal.Should().Be(aliceLifeBefore, "the skipped step does nothing");
    }

    [Fact]
    public void Hellbent_OpponentUpkeep_ControllerEmptyHand_StillFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = GibberingDescentFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        // Alice (controller) empty-handed, but Bob's upkeep — Hellbent only
        // skips YOUR (the controller's) upkeep, never an opponent's.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        Grip(_bob);

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1,
            "Hellbent never skips an opponent's upkeep");
    }

    [Fact]
    public void UpkeepTrigger_DoesNotFireOnNonUpkeepSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = GibberingDescentFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        bus.Publish(new StepStartedEvent(StepStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(StepStateType.End, _bob));

        triggers.PendingCount.Should().Be(0, "only the Upkeep step matters");
    }
}
