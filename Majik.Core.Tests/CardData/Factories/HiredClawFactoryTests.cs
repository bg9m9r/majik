using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HiredClawFactory"/> (Outlaws of Thunder Junction,
/// {R}). Creature — Lizard Mercenary 1/1.
///
/// Oracle text (verified against Scryfall):
///   "Whenever you attack with one or more Lizards, this creature deals
///    1 damage to target opponent.
///    {1}{R}: Put a +1/+1 counter on this creature. Activate only if an
///    opponent lost life this turn and only once each turn."
///
/// Covers:
///   - Identity (name, type, Lizard + Mercenary subtypes, {R}, 1/1,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - Attack-with-Lizards trigger (CR 508.1f):
///       * Fires when the controller attacks with one or more Lizards.
///       * Does NOT fire when no attacker is a Lizard.
///       * Does NOT fire when an opponent declares attackers.
///   - Trigger body: target opponent takes 1 damage (CR 119.3).
///   - Activated +1/+1-counter ability (CR 602.1):
///       * Gated by "an opponent lost life this turn" (CR 602.5c).
///       * Gated "only once each turn" (CR 602.5e).
///       * On resolution: a +1/+1 counter lands on Hired Claw.
/// </summary>
[Trait("Color", "R")]
public class HiredClawFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeLizard(Player owner, string name = "Basilisk")
    {
        var c = new Creature(name, "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Lizard });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeBear(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    private static ActivatedAbility GetCounterAbility(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HiredClaw_Identity_LizardMercenary_1_1_AtCostR()
    {
        var card = HiredClawFactory.Create(_alice);

        card.Name.Should().Be("Hired Claw");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mercenary).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HiredClaw_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hired Claw", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hired Claw");
        card.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mercenary).Should().BeTrue();
    }

    [Fact]
    public void HiredClaw_HasOneAttackTrigger_AndOneActivatedAbility()
    {
        var card = HiredClawFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Attack-with-Lizards trigger (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_FiresWhenLizardAttacks()
    {
        var card = HiredClawFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        // Hired Claw itself is a Lizard — attacking with it satisfies the gate.
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(card, _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue(
            "Hired Claw is a Lizard and Alice is the attacking player.");
    }

    [Fact]
    public void AttackTrigger_DoesNotFireWithoutLizards()
    {
        var card = HiredClawFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        var bear = MakeBear(_alice);
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(bear, _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "no Lizard is attacking — the 'one or more Lizards' gate fails (CR 700.2).");
    }

    [Fact]
    public void AttackTrigger_DoesNotFireOnOpponentAttacks()
    {
        var card = HiredClawFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        var bobLizard = MakeLizard(_bob);
        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(bobLizard, _alice));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "CR 109.5 — 'you attack' = the trigger source's controller is the attacking player.");
    }

    [Fact]
    public void AttackTrigger_Body_Deals1DamageToTargetOpponent()
    {
        var card = HiredClawFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: null,
            opponentResolver: () => new List<Player> { _alice, _bob });
        card.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "CR 119.3 — the target opponent takes 1 damage.");
        _alice.LifeTotal.Should().Be(20, "the controller is never the damage target.");
    }

    // -----------------------------------------------------------------------
    // {1}{R}: +1/+1 counter — gated on opponent life loss, once per turn
    // (CR 602.5c / 602.5e)
    // -----------------------------------------------------------------------

    [Fact]
    public void CounterAbility_CannotActivate_WhenNoOpponentLostLife()
    {
        var card = HiredClawFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: null,
            opponentResolver: () => new List<Player> { _alice, _bob });
        card.SetZone(ZoneType.Battlefield);

        var ability = GetCounterAbility(card);
        ability.CanActivateNow().Should().BeFalse(
            "CR 602.5c — no opponent has lost life this turn.");
    }

    [Fact]
    public void CounterAbility_CanActivate_AfterOpponentLostLife()
    {
        var card = HiredClawFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: null,
            opponentResolver: () => new List<Player> { _alice, _bob });
        card.SetZone(ZoneType.Battlefield);

        _bob.LoseLife(3);

        var ability = GetCounterAbility(card);
        ability.CanActivateNow().Should().BeTrue(
            "CR 602.5c — an opponent (Bob) lost life this turn.");
    }

    [Fact]
    public void CounterAbility_Resolution_PutsPlusOnePlusOneCounter()
    {
        var card = HiredClawFactory.Create(
            _alice,
            eventBus: null,
            triggers: null,
            replacements: null,
            opponentResolver: () => new List<Player> { _alice, _bob });
        card.SetZone(ZoneType.Battlefield);

        var ability = GetCounterAbility(card);
        foreach (var e in ability.Effects) e.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 121.1 — resolution puts a +1/+1 counter on Hired Claw.");
    }

    [Fact]
    public void CounterAbility_OncePerTurn_LocksAfterResolution_UntilTurnReset()
    {
        var bus = new EventBus();
        var card = HiredClawFactory.Create(
            _alice,
            eventBus: bus,
            triggers: null,
            replacements: null,
            opponentResolver: () => new List<Player> { _alice, _bob });
        card.SetZone(ZoneType.Battlefield);

        _bob.LoseLife(2);

        var ability = GetCounterAbility(card);
        ability.CanActivateNow().Should().BeTrue("first activation this turn is allowed.");

        // Resolve once — flips the once-per-turn lock (CR 602.5e).
        foreach (var e in ability.Effects) e.Execute();

        // Opponent still down life this turn, but the per-turn lock is closed.
        ability.CanActivateNow().Should().BeFalse(
            "CR 602.5e — 'only once each turn' closes after the first activation.");

        // New turn resets the lock (CR 500.1). Bob must again have lost life.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));
        _bob.LoseLife(1);
        ability.CanActivateNow().Should().BeTrue(
            "after the turn boundary the once-per-turn lock reopens.");
    }
}
