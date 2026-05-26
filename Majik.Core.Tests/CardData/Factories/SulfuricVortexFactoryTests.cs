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
/// Unit tests for <see cref="SulfuricVortexFactory"/> (Scourge, {1}{R}{R}).
///
/// Enchantment. Oracle text:
///   "At the beginning of each player's upkeep, Sulfuric Vortex deals 2
///    damage to that player.
///    If a player would gain life, that player gains no life instead."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Each-player's-upkeep trigger fires on both Alice and Bob's upkeep
///     (unlike Roiling Vortex's controller-only trigger).
///   - Damage routes to the active player (StepStartedEvent.Player).
///   - "Players can't gain life" replacement zeros every GainLife while
///     the bus is attached.
/// </summary>
public class SulfuricVortexFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity / dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_HasEnchantmentShape_OneRR()
    {
        var vortex = SulfuricVortexFactory.Create(_alice);

        vortex.Should().BeOfType<Enchantment>();
        vortex.Name.Should().Be("Sulfuric Vortex");
        vortex.ManaCost.Should().Be("{1}{R}{R}");
        vortex.HasType(CardType.Enchantment).Should().BeTrue();
        vortex.Owner.Should().BeSameAs(_alice);
        vortex.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsSulfuricVortexShape()
    {
        var dispatched = NamedCardFactory.Create("Sulfuric Vortex", _alice);

        dispatched.Should().BeOfType<Enchantment>();
        dispatched.Name.Should().Be("Sulfuric Vortex");
        dispatched.ManaCost.Should().Be("{1}{R}{R}");
        dispatched.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Each-player's-upkeep trigger
    // -------------------------------------------------------------------------

    [Fact]
    public void UpkeepTrigger_FiresOnControllerUpkeep_DealsTwoToController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = SulfuricVortexFactory.Create(_alice, triggers, replacements: null);
        vortex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(vortex);

        var aliceLifeBefore = _alice.LifeTotal;

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1, "your upkeep — Sulfuric Vortex's trigger queues");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(aliceLifeBefore - SulfuricVortexFactory.UpkeepDamage);
    }

    [Fact]
    public void UpkeepTrigger_FiresOnOpponentUpkeep_DealsTwoToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = SulfuricVortexFactory.Create(_alice, triggers, replacements: null);
        vortex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(vortex);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        // Bob's upkeep — Sulfuric Vortex's symmetric "each player's
        // upkeep" trigger fires (unlike Roiling Vortex which is
        // controller-only).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1, "each player's upkeep includes Bob's");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - SulfuricVortexFactory.UpkeepDamage,
            "Bob's upkeep — Bob takes the damage");
        _alice.LifeTotal.Should().Be(aliceLifeBefore,
            "Alice is not the active player this upkeep");
    }

    [Fact]
    public void UpkeepTrigger_DoesNotFireOnNonUpkeepSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vortex = SulfuricVortexFactory.Create(_alice, triggers, replacements: null);
        vortex.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(vortex);

        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(0, "only Upkeep step matters");
    }

    // -------------------------------------------------------------------------
    // Life-gain replacement
    // -------------------------------------------------------------------------

    [Fact]
    public void LifeGainReplacement_BlocksGainLifeOnEveryPlayer()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        _bob.AttachReplacementBus(bus);

        SulfuricVortexFactory.Create(_alice, triggers: null, replacements: bus);

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        _alice.GainLife(5);
        _bob.GainLife(7);

        _alice.LifeTotal.Should().Be(aliceLifeBefore, "gain rewritten to zero");
        _bob.LifeTotal.Should().Be(bobLifeBefore, "symmetric — Bob's gain zeros too");
    }

    [Fact]
    public void LifeGainReplacement_OmittedWhenNoBus_GainsNormally()
    {
        // Single-arg dispatcher posture: no replacement bus wired —
        // static silently no-ops (mirrors Roiling Vortex / Valakut).
        SulfuricVortexFactory.Create(_alice);

        var aliceLifeBefore = _alice.LifeTotal;
        _alice.GainLife(5);

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 5);
    }
}
