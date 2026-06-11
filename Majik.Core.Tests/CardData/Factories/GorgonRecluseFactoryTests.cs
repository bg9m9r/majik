using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GorgonRecluseFactory"/> — Creature — Gorgon
/// {3}{B}{B} 2/4 whose printed ability is:
///   "Whenever this creature blocks or becomes blocked by a nonblack creature,
///    destroy that creature at end of combat."
/// (Madness {B}{B} is intrinsic via MadnessCatalog + the Fx.DiscardCard funnel;
/// not exercised here.)
///
/// Covers:
///   - Card identity (name, cost, type, subtype, P/T, owner / controller).
///   - The combat trigger fires when Gorgon Recluse blocks a nonblack creature
///     and schedules the end-of-combat destroy of that creature.
///   - The combat trigger fires when Gorgon Recluse becomes blocked by a
///     nonblack creature (Gorgon is the attacker) and schedules the destroy.
///   - A BLACK creature in the pairing does NOT trigger the ability.
///   - The destroy is deferred: nothing happens until the controller's
///     EndOfCombat step begins, then "that creature" is destroyed.
///   - Resolution-time legality re-check (CR 608.2b): a target that already
///     left the battlefield is a clean no-op.
/// </summary>
[Trait("Color", "B")]
public class GorgonRecluseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    private static TriggeredAbility GetCombatTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    private static Creature CreatureOn(Player p, string name, string cost, int pow = 2, int tough = 2)
    {
        var c = new Creature(name, cost, pow, tough);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [Fact]
    public void GorgonRecluse_IsGorgon_At3BB_TwoFour()
    {
        var c = GorgonRecluseFactory.Create(_alice);

        c.Name.Should().Be("Gorgon Recluse");
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Gorgon).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Exactly one triggered ability: the blocks/blocked-by combat trigger.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // ── Trigger condition: blocks a nonblack creature ───────────────────────

    [Fact]
    public void Blocks_NonblackCreature_SchedulesEndOfCombatDestroy()
    {
        var attacker = CreatureOn(_bob, "Grizzly Bears", "{1}{G}");

        var gorgon = GorgonRecluseFactory.Create(_alice, _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(gorgon);
        gorgon.SetZone(ZoneType.Battlefield);

        var trigger = GetCombatTrigger(gorgon);

        // Gorgon Recluse is declared as a blocker of the green attacker.
        trigger.Condition.Matches(
            new CreatureBlocksEvent(gorgon, attacker), trigger).Should().BeTrue();

        // Resolving the trigger only SCHEDULES the destroy (CR 603.7) — the
        // attacker is untouched until the end-of-combat step.
        foreach (var e in trigger.Effects) e.Execute();
        attacker.Zone.Should().Be(ZoneType.Battlefield);

        // End-of-combat step begins for the active player → destroy fires.
        _bus.Publish(new StepStartedEvent(StepStateType.EndOfCombat, _alice));

        attacker.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(attacker);
    }

    // ── Trigger condition: becomes blocked by a nonblack creature ───────────

    [Fact]
    public void BecomesBlockedBy_NonblackCreature_SchedulesEndOfCombatDestroy()
    {
        var blocker = CreatureOn(_bob, "Grizzly Bears", "{1}{G}");

        var gorgon = GorgonRecluseFactory.Create(_alice, _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(gorgon);
        gorgon.SetZone(ZoneType.Battlefield);

        var trigger = GetCombatTrigger(gorgon);

        // Gorgon Recluse (the attacker) becomes blocked by the green blocker.
        trigger.Condition.Matches(
            new CreatureBlocksEvent(blocker, gorgon), trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();
        blocker.Zone.Should().Be(ZoneType.Battlefield);

        _bus.Publish(new StepStartedEvent(StepStateType.EndOfCombat, _alice));

        blocker.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── Black creature does NOT trigger ─────────────────────────────────────

    [Fact]
    public void Blocks_BlackCreature_DoesNotTrigger()
    {
        var blackAttacker = CreatureOn(_bob, "Walking Corpse", "{2}{B}");

        var gorgon = GorgonRecluseFactory.Create(_alice, _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(gorgon);
        gorgon.SetZone(ZoneType.Battlefield);

        var trigger = GetCombatTrigger(gorgon);

        // "a nonblack creature" — a black creature in the pairing never triggers.
        trigger.Condition.Matches(
            new CreatureBlocksEvent(gorgon, blackAttacker), trigger).Should().BeFalse();
    }

    // ── Resolution-time legality re-check (CR 608.2b) ───────────────────────

    [Fact]
    public void Destroy_NoOp_WhenTargetLeftBattlefieldBeforeEndOfCombat()
    {
        var attacker = CreatureOn(_bob, "Grizzly Bears", "{1}{G}");

        var gorgon = GorgonRecluseFactory.Create(_alice, _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(gorgon);
        gorgon.SetZone(ZoneType.Battlefield);

        var trigger = GetCombatTrigger(gorgon);
        trigger.Condition.Matches(
            new CreatureBlocksEvent(gorgon, attacker), trigger).Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();

        // The attacker leaves combat (e.g. bounced) before end of combat.
        _bob.Zones.Battlefield.RemoveCard(attacker);
        _bob.Zones.Hand.AddCard(attacker);
        attacker.SetZone(ZoneType.Hand);

        Action act = () => _bus.Publish(new StepStartedEvent(StepStateType.EndOfCombat, _alice));
        act.Should().NotThrow();

        attacker.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(attacker);
    }
}
