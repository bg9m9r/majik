using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IchorclawMyrFactory"/> (Scars of Mirrodin, {2}).
///
/// Ichorclaw Myr — Artifact Creature — Phyrexian Myr 1/1. Oracle text
/// (verified against Scryfall 2026-06-02):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    Whenever this creature becomes blocked, it gets +2/+2 until end of
///    turn."
///
/// Coverage:
/// - Identity (Artifact + Creature types, Phyrexian Myr subtypes, 1/1, {2},
///   colorless, owner/controller wired).
/// - NamedCardFactory dispatch (IsImplemented derives from the registry).
/// - Infect keyword marker (CR 702.90).
/// - Exactly one battlefield-active becomes-blocked TriggeredAbility,
///   self-affecting (no targets).
/// - Becomes-blocked trigger fires when this Myr (as attacker) has ≥ 1
///   declared blocker (CR 509.1h, via BlockersDeclaredEvent), and resolving
///   gives +2/+2 (CR 613.1g).
/// - The +2/+2 expires in the cleanup step (CR 514.2).
/// - The trigger does NOT fire when the Myr attacks unblocked, nor when a
///   DIFFERENT creature becomes blocked.
/// </summary>
[Trait("Color", "C")]
public class IchorclawMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IchorclawMyr_Identity_ArtifactCreaturePhyrexianMyr_1_1_Colorless2()
    {
        var myr = IchorclawMyrFactory.Create(_alice);

        myr.Name.Should().Be("Ichorclaw Myr");
        myr.ManaCost.Should().Be("{2}");
        myr.ManaCostValue.TotalValue.Should().Be(2);
        myr.HasType(CardType.Creature).Should().BeTrue();
        myr.HasType(CardType.Artifact).Should().BeTrue("CR 205.2a — Artifact Creature carries both card types");
        myr.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        myr.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        myr.BasePower.Should().Be(1);
        myr.BaseToughness.Should().Be(1);
        myr.Owner.Should().BeSameAs(_alice);
        myr.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IchorclawMyr_IsColorless()
    {
        var myr = IchorclawMyrFactory.Create(_alice);

        // {2} is a generic-only cost — no colored pips (CR 105.2c).
        CardColors.GetColors(myr).Should().BeEmpty(
            "Ichorclaw Myr's {2} cost has no colored mana symbols");
    }

    [Fact]
    public void IchorclawMyr_HasInfectKeyword()
    {
        var myr = IchorclawMyrFactory.Create(_alice);

        myr.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Infect",
                "Infect is wired as a KeywordAbility marker (CR 702.90)");
    }

    // -----------------------------------------------------------------------
    // Triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void IchorclawMyr_BecomesBlockedTrigger_IsSelfAffecting_NoTargets()
    {
        var myr = IchorclawMyrFactory.Create(_alice);

        var trigger = myr.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        trigger.Source.Should().BeSameAs(myr);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "the +2/+2 affects the Myr itself — no target is chosen");
    }

    // -----------------------------------------------------------------------
    // Becomes-blocked — fires when ≥ 1 creature blocks the Myr, pumps +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void IchorclawMyr_BecomesBlocked_QueuesTrigger_PumpsPlusTwoPlusTwo()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var myr = IchorclawMyrFactory.Create(_alice, triggers);
        myr.ActiveEffects = new ContinuousEffectsService();
        myr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myr);
        triggers.BindCard(myr);

        var blocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        blocker.SetOwner(_bob);
        blocker.SetController(_bob);
        blocker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blocker);

        // Simulate a combat in which the Myr (Alice's attacker) is blocked by
        // Bob's creature. BlockersDeclaredEvent is the engine hook for
        // "becomes blocked" (CR 509.1h).
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        var attackerObj = new Majik.Core.Combat.Attacker(myr, _bob);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(blocker, attackerObj));
        combat.TransitionToAssigningDamage();

        bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(1, "the becomes-blocked trigger fired");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        myr.GetPower().Should().Be(IchorclawMyrFactory.Power + IchorclawMyrFactory.PumpAmount);
        myr.GetToughness().Should().Be(IchorclawMyrFactory.Toughness + IchorclawMyrFactory.PumpAmount);
    }

    [Fact]
    public void IchorclawMyr_Pump_ExpiresAtEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var myr = IchorclawMyrFactory.Create(_alice, triggers);
        var svc = new ContinuousEffectsService();
        myr.ActiveEffects = svc;
        myr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myr);
        triggers.BindCard(myr);

        var blocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        blocker.SetOwner(_bob);
        blocker.SetController(_bob);
        blocker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blocker);

        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        var attackerObj = new Majik.Core.Combat.Attacker(myr, _bob);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(blocker, attackerObj));
        combat.TransitionToAssigningDamage();

        bus.Publish(new BlockersDeclaredEvent(combat));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        myr.GetPower().Should().Be(3);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        myr.GetPower().Should().Be(IchorclawMyrFactory.Power);
        myr.GetToughness().Should().Be(IchorclawMyrFactory.Toughness);
    }

    [Fact]
    public void IchorclawMyr_Unblocked_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var myr = IchorclawMyrFactory.Create(_alice, triggers);
        myr.ActiveEffects = new ContinuousEffectsService();
        myr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myr);
        triggers.BindCard(myr);

        // The Myr attacks but no blocker is declared — it does not become
        // blocked (CR 509.1h), so the trigger must not fire.
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        var attackerObj = new Majik.Core.Combat.Attacker(myr, _bob);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        combat.TransitionToAssigningDamage();

        bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "an unblocked attacker does not become blocked (CR 509.1h)");
        myr.GetPower().Should().Be(IchorclawMyrFactory.Power);
    }

    [Fact]
    public void IchorclawMyr_DoesNotFire_WhenADifferentCreatureBecomesBlocked()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var myr = IchorclawMyrFactory.Create(_alice, triggers);
        myr.ActiveEffects = new ContinuousEffectsService();
        myr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myr);
        triggers.BindCard(myr);

        // A different attacker (NOT the Myr) becomes blocked.
        var otherAttacker = new Creature("Goblin Guide", "{R}", 2, 2);
        otherAttacker.SetOwner(_alice);
        otherAttacker.SetController(_alice);
        otherAttacker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(otherAttacker);

        var blocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        blocker.SetOwner(_bob);
        blocker.SetController(_bob);
        blocker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blocker);

        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        var attackerObj = new Majik.Core.Combat.Attacker(otherAttacker, _bob);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(blocker, attackerObj));
        combat.TransitionToAssigningDamage();

        bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "the Myr's trigger only fires when the Myr itself becomes blocked");
        myr.GetPower().Should().Be(IchorclawMyrFactory.Power);
    }
}
