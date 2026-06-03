using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.135 — Boast keyword tests. Boast is an activated ability with the
/// built-in restriction "Activate only if this creature attacked this turn
/// and only once each turn" (CR 702.135b/c). Covers:
/// <list type="bullet">
///   <item><see cref="BoastAbility.Build"/> returns an activated ability with
///       the printed cost + stamps the "Boast" keyword marker.</item>
///   <item>The activation gate is FALSE before the creature attacks (CR
///       702.135b).</item>
///   <item>The gate becomes TRUE once the creature is declared as an attacker
///       (the helper observes <see cref="AttackersDeclaredEvent"/>).</item>
///   <item>After one activation the gate closes for the rest of the turn (CR
///       702.135c — "only once each turn").</item>
///   <item>The per-turn rail resets on <see cref="TurnStartedEvent"/> — a new
///       turn requires a fresh attack and re-opens the once-per-turn cap.</item>
///   <item>A cap override (Birgi's "boast twice") lets the same creature boast
///       twice in one turn.</item>
/// </list>
/// </summary>
public class BoastAbilityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeBerserker(IEventBus bus, out EventBus typed)
    {
        typed = (EventBus)bus;
        var c = new Creature("Boast Berserker", "{B}", 1, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Core.Combat.Combat DeclareAttack(Creature attacker)
    {
        var combat = new Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Attacker(attacker, targetPlayer: _bob));
        return combat;
    }

    [Fact]
    public void Build_ReturnsActivatedAbility_WithProvidedCost_AndStampsKeyword()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out _);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        ability.Should().NotBeNull();
        ability.Costs.Should().ContainSingle().Which.Should().BeOfType<ManaCostCost>();
        berserker.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Boast");
    }

    [Fact]
    public void Gate_IsFalse_BeforeCreatureAttacks()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out _);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        // CR 702.135b — "Activate only if this creature attacked this turn."
        ability.CanActivateNow().Should().BeFalse();
    }

    [Fact]
    public void Gate_BecomesTrue_AfterCreatureDeclaredAsAttacker()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out var typed);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(berserker)));

        ability.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void Gate_IgnoresAttacksByOtherCreatures()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out var typed);
        var other = new Creature("Other", "{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        other.SetZone(ZoneType.Battlefield);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(other)));

        // Only `berserker` attacking opens its own boast gate.
        ability.CanActivateNow().Should().BeFalse();
    }

    [Fact]
    public void Gate_ClosesAfterOneActivation_SameTurn()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out var typed);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(berserker)));
        ability.CanActivateNow().Should().BeTrue();

        // CR 702.135c — "only once each turn". The activation is recorded by
        // observing AbilityActivatedEvent.
        typed.Publish(new AbilityActivatedEvent(ability));

        ability.CanActivateNow().Should().BeFalse();
    }

    [Fact]
    public void TurnStart_ResetsAttackedAndPerTurnCount()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out var typed);

        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus);

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(berserker)));
        typed.Publish(new AbilityActivatedEvent(ability));
        ability.CanActivateNow().Should().BeFalse();

        // New turn: attacked-this-turn + once-per-turn count both reset.
        typed.Publish(new TurnStartedEvent(_alice, 2));
        ability.CanActivateNow().Should().BeFalse("must attack again first");

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(berserker)));
        ability.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void CapOverride_AllowsBoastingTwicePerTurn()
    {
        var bus = new EventBus();
        var berserker = MakeBerserker(bus, out var typed);

        // Birgi's "creatures you control can boast twice each turn" raises the
        // per-turn cap from 1 to 2.
        var ability = BoastAbility.Build(
            berserker, "{1}", new IEffect[] { new Effect("noop", () => { }) }, bus,
            perTurnCap: () => 2);

        typed.Publish(new AttackersDeclaredEvent(DeclareAttack(berserker)));

        ability.CanActivateNow().Should().BeTrue();
        typed.Publish(new AbilityActivatedEvent(ability));   // first boast
        ability.CanActivateNow().Should().BeTrue("cap is 2");
        typed.Publish(new AbilityActivatedEvent(ability));   // second boast
        ability.CanActivateNow().Should().BeFalse("cap of 2 reached");
    }
}
