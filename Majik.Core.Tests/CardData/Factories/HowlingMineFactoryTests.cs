using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="HowlingMineFactory"/> — Artifact {2}.
///
/// Oracle text (verified against Scryfall):
///   "At the beginning of each player's draw step, if this artifact is
///    untapped, that player draws an additional card."
///
/// Coverage:
///   - Identity: Artifact, {2}, owner/controller.
///   - One symmetric draw-step trigger (CR 603 — "each player's draw
///     step"), active on the battlefield, with an intervening-if
///     "untapped" gate (CR 603.4).
///   - Bus integration: each player's draw step fires the trigger and
///     the draw goes to THAT player (the active/triggering player), not
///     the controller.
///   - Intervening-if: while the artifact is tapped the ability does not
///     go on the stack (CR 603.4 — re-checked at trigger time).
/// </summary>
[Trait("Color", "C")]
public class HowlingMineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HowlingMine_Identity_Artifact_At2()
    {
        var mine = HowlingMineFactory.Create(_alice);

        mine.Should().BeOfType<Artifact>();
        mine.Name.Should().Be("Howling Mine");
        mine.ManaCost.Should().Be("{2}");
        mine.HasType(CardType.Artifact).Should().BeTrue();
        mine.Owner.Should().BeSameAs(_alice);
        mine.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HowlingMine_HasSingleDrawStepTrigger_OnBattlefield()
    {
        var mine = HowlingMineFactory.Create(_alice);

        var triggers = mine.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().ContainSingle();
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
        triggers[0].InterveningIf.Should().NotBeNull(
            "the 'if this artifact is untapped' clause is an intervening-if (CR 603.4)");
    }

    // -----------------------------------------------------------------------
    // Live bus — symmetric draw-step trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawStep_ControllersOwnDrawStep_Fires_AndDrawGoesToController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mine = HowlingMineFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);

        // Seed Alice's library so the resolved draw has a card to move.
        _alice.Zones.Library.AddCard(new Card("Top", "{0}"));
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        bus.Publish(new StepStartedEvent(StepStateType.Draw, _alice));
        triggers.PendingCount.Should().Be(1, "Alice's draw step fires Howling Mine");

        // The matched trigger stamped the active player; resolving draws for them.
        var trigger = mine.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "the additional card is drawn by the active player (Alice)");
    }

    [Fact]
    public void DrawStep_OpponentsDrawStep_Fires_AndDrawGoesToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mine = HowlingMineFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);

        // Bob's library + hand baselines — the extra card is HIS, not Alice's.
        _bob.Zones.Library.AddCard(new Card("BobTop", "{0}"));
        var bobHandBefore = _bob.Zones.Hand.GetCards().Count();
        var aliceHandBefore = _alice.Zones.Hand.GetCards().Count();

        bus.Publish(new StepStartedEvent(StepStateType.Draw, _bob));
        triggers.PendingCount.Should().Be(1,
            "Howling Mine is symmetric — it fires on EACH player's draw step");

        var trigger = mine.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Resolve();

        _bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 1,
            "the active player Bob draws the additional card");
        _alice.Zones.Hand.GetCards().Count().Should().Be(aliceHandBefore,
            "the controller does not draw on an opponent's draw step");
    }

    // -----------------------------------------------------------------------
    // Intervening-if — tapped artifact does not trigger (CR 603.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawStep_WhileTapped_DoesNotGoOnStack()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mine = HowlingMineFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);
        mine.Tap();

        bus.Publish(new StepStartedEvent(StepStateType.Draw, _alice));

        // CR 603.4 — the intervening-if "if this artifact is untapped" is
        // false, so the ability never goes on the stack.
        triggers.PendingCount.Should().Be(0,
            "a tapped Howling Mine grants no additional draw");
    }

    [Fact]
    public void DrawStep_NonDrawStep_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mine = HowlingMineFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));

        triggers.PendingCount.Should().Be(0, "Howling Mine only triggers in the draw step");
    }
}
