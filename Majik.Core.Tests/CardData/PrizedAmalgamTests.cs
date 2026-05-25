using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PrizedAmalgamFactory"/> (Shadows over
/// Innistrad, {3}{U/B}).
///
/// Covers:
///   - Identity (Zombie Horror 3/3 at {3}{U/B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Graveyard-resident ETB trigger registers a delayed end-step
///     return when another creature you control enters (CR 603.6d).
///   - The delayed trigger fires on the next End step and returns
///     Amalgam to the battlefield tapped (CR 603.7).
///   - The ETB trigger does NOT fire when an OPPONENT's creature enters.
///   - The ETB trigger does NOT fire when Amalgam itself enters
///     (self-exclusion clause "another creature").
///   - The ETB trigger does NOT fire while Amalgam is on the battlefield
///     (activeZones = {Graveyard}, CR 603.6d).
/// </summary>
public class PrizedAmalgamTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PrizedAmalgam_Identity_ZombieHorror_3_3_AtCost3UB()
    {
        var card = PrizedAmalgamFactory.Create(_alice);

        card.Name.Should().Be("Prized Amalgam");
        card.ManaCost.Should().Be("{3}{U/B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PrizedAmalgam_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Prized Amalgam", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Prized Amalgam");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
    }

    [Fact]
    public void PrizedAmalgam_HasEtbTrigger_AttachedToCard()
    {
        var card = PrizedAmalgamFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "graveyard-resident ETB trigger is attached (CR 603.6d)");
    }

    // -----------------------------------------------------------------------
    // Graveyard-resident trigger — CR 603.6d
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_AnotherCreatureYouControl_QueuesDelayedReturn()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        // Place Amalgam in Alice's graveyard.
        var amalgam = PrizedAmalgamFactory.Create(_alice, zones, triggers);
        _alice.Zones.Graveyard.AddCard(amalgam);
        amalgam.SetZone(ZoneType.Graveyard);

        // Another creature Alice controls enters the battlefield.
        var goblin = new Creature("Goblin Bushwhacker", "{R}", 1, 1);
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        zones.MoveCardTo(goblin, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "Amalgam's ETB trigger queued — another creature entered under controller's control");

        // Resolve the ETB trigger — it should register a delayed end-step
        // ability on the trigger manager.
        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var triggerOnStack = (TriggeredAbility)stack.Pop()!;
        triggerOnStack.Resolve();

        // Now fire an End step — the delayed trigger should pick it up.
        var bus = GetEventBus(zones);
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "delayed end-step trigger queued by the ETB resolution");

        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var delayedOnStack = (TriggeredAbility)stack.Pop()!;
        delayedOnStack.Resolve();

        amalgam.Zone.Should().Be(ZoneType.Battlefield,
            "Amalgam returns to the battlefield at next end step");
        _alice.Zones.Battlefield.GetCards().Should().Contain(amalgam);
        amalgam.IsTapped.Should().BeTrue(
            "Amalgam returns TAPPED (printed text)");
    }

    [Fact]
    public void EtbTrigger_DoesNotFire_WhenOpponentCreatureEnters()
    {
        var (zones, _, triggers, _) = BuildEngine();

        var amalgam = PrizedAmalgamFactory.Create(_alice, zones, triggers);
        _alice.Zones.Graveyard.AddCard(amalgam);
        amalgam.SetZone(ZoneType.Graveyard);

        // Bob's creature enters — should NOT trigger Alice's Amalgam.
        var bobCreature = new Creature("Wall of Doubt", "{2}{U}", 0, 5);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        _bob.Zones.Hand.AddCard(bobCreature);
        bobCreature.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobCreature, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "Amalgam fires only on YOUR creatures entering");
    }

    [Fact]
    public void EtbTrigger_DoesNotFire_WhenAmalgamItselfEnters()
    {
        var (zones, _, triggers, _) = BuildEngine();

        // Amalgam starts in graveyard, then moves to battlefield. The
        // "another creature" exclusion means Amalgam's own enter doesn't
        // re-trigger.
        var amalgam = PrizedAmalgamFactory.Create(_alice, zones, triggers);
        _alice.Zones.Graveyard.AddCard(amalgam);
        amalgam.SetZone(ZoneType.Graveyard);

        zones.MoveCard(amalgam, ZoneType.Graveyard, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(0,
            "'another creature' clause excludes Amalgam itself (CR 603.6c)");
    }

    [Fact]
    public void EtbTrigger_DoesNotFire_WhenAmalgamOnBattlefield()
    {
        var (zones, _, triggers, _) = BuildEngine();

        var amalgam = PrizedAmalgamFactory.Create(_alice, zones, triggers);
        _alice.Zones.Battlefield.AddCard(amalgam);
        amalgam.SetZone(ZoneType.Battlefield);

        // Another creature enters — but Amalgam is on the battlefield,
        // not in the graveyard, so the trigger is inactive.
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2);
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        zones.MoveCardTo(goblin, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "graveyard-resident trigger is inactive while Amalgam is on battlefield (CR 603.6d)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private EventBus? _capturedBus;

    private (ZoneService zones, MajikStack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        _capturedBus = bus;
        return (zones, stack, triggers, rep);
    }

    private EventBus GetEventBus(ZoneService _) => _capturedBus!;
}
