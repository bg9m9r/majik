using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
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
/// Unit tests for <see cref="StensiaMasqueradeFactory"/>.
///
/// Stensia Masquerade (Shadows over Innistrad, {2}{R}). Enchantment. Oracle
/// text (verified against Scryfall):
///   "Attacking creatures you control have first strike.
///    Whenever a Vampire you control deals combat damage to a player, put a
///    +1/+1 counter on it.
///    Madness {2}{R}"
///
/// Covers:
/// - Identity ({2}{R} mono-red Enchantment).
/// - The attacking-creatures first-strike anthem (CR 613.1f / 702.7): an
///   attacking creature you control gains first strike; a non-attacking one
///   does not; an opponent's attacker does not; the grant is revoked when
///   Stensia leaves play.
/// - The Vampire combat-damage trigger (CR 603.1 / 510): a Vampire you control
///   dealing combat damage to a player gets a +1/+1 counter; a non-Vampire
///   does not; combat damage to a creature does not; an opponent's Vampire
///   does not.
/// </summary>
[Trait("Color", "R")]
public class StensiaMasqueradeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly CombatMembershipRegistry _combat = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public StensiaMasqueradeFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus) { PlayersProvider = AllPlayers };
        _zones = new ZoneService(_bus);
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    private System.Collections.Generic.IEnumerable<Player> AllPlayers() => new[] { _alice, _bob };

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private Creature Creature(string name, Player controller, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{1}{R}", 2, 2, subtypes: subtypes.Length == 0 ? null : subtypes);
        c.ChangeOwner(controller);
        c.ChangeController(controller);
        c.ActiveEffects = _effects;
        return c;
    }

    /// <summary>
    /// Mirror the live <see cref="Majik.Core.Combat.CombatFlow"/> declaration:
    /// record the creature as a declared attacker in the membership registry AND
    /// publish the per-attacker <see cref="CreatureAttacksEvent"/> the live
    /// engine fires (CR 508.1). The published event flows through the
    /// continuous-effects service's <c>SubscribeAll</c> handler, invalidating its
    /// memoization cache so the next <c>Compute</c> re-evaluates the combat
    /// predicate — exactly the live-game invalidation path.
    /// </summary>
    private void DeclareAttacker(Creature attacker, Player defendingPlayer)
    {
        _combat.RecordAttacker(attacker);
        _bus.Publish(new CreatureAttacksEvent(attacker, defendingPlayer));
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StensiaMasquerade_Identity()
    {
        var c = StensiaMasqueradeFactory.Create(_alice);

        c.Name.Should().Be("Stensia Masquerade");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse();
        c.ManaCost.Should().Be("{2}{R}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StensiaMasquerade_IsMonoRed()
    {
        var c = StensiaMasqueradeFactory.Create(_alice);
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // "Attacking creatures you control have first strike."
    // -----------------------------------------------------------------------

    [Fact]
    public void Anthem_AttackingCreatureYouControl_GainsFirstStrike()
    {
        var attacker = Creature("Bear", _alice);
        PutOnBattlefield(attacker, _alice);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        using var scope = CombatMembershipRegistryProvider.PushScope(_combat);

        // Before declaration: not attacking → no first strike (CR 508.4).
        CombatAbilities.HasFirstStrike(attacker).Should().BeFalse(
            "the creature is not attacking yet");

        // Declared as an attacker → the live combat predicate now includes it.
        DeclareAttacker(attacker, _bob);

        CombatAbilities.HasFirstStrike(attacker).Should().BeTrue(
            "CR 702.7 — an attacking creature you control has first strike while Stensia Masquerade is in play");

        // Leaves combat → grant drops (CR 511.3). Publish a benign event so the
        // effects cache re-evaluates (the live engine fires combat-end events).
        _combat.RemoveFromCombat(attacker);
        _bus.Publish(new TurnStartedEvent(_alice, 2));
        CombatAbilities.HasFirstStrike(attacker).Should().BeFalse(
            "the creature is no longer attacking");
    }

    [Fact]
    public void Anthem_NonAttackingCreature_NoFirstStrike()
    {
        var bench = Creature("Bench", _alice);
        PutOnBattlefield(bench, _alice);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        using var scope = CombatMembershipRegistryProvider.PushScope(_combat);

        CombatAbilities.HasFirstStrike(bench).Should().BeFalse(
            "a creature you control that is NOT attacking does not get first strike");
    }

    [Fact]
    public void Anthem_OpponentsAttacker_NoFirstStrike()
    {
        var enemy = Creature("Enemy", _bob);
        PutOnBattlefield(enemy, _bob);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        using var scope = CombatMembershipRegistryProvider.PushScope(_combat);
        DeclareAttacker(enemy, _alice);

        CombatAbilities.HasFirstStrike(enemy).Should().BeFalse(
            "the anthem is scoped to 'creatures YOU control' — an opponent's attacker is excluded");
    }

    [Fact]
    public void Anthem_RevokedWhenStensiaLeavesPlay()
    {
        var attacker = Creature("Bear", _alice);
        PutOnBattlefield(attacker, _alice);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        using var scope = CombatMembershipRegistryProvider.PushScope(_combat);
        DeclareAttacker(attacker, _bob);
        CombatAbilities.HasFirstStrike(attacker).Should().BeTrue();

        // Stensia leaves the battlefield → the anthem is revoked (CR 611.2c).
        _zones.MoveCard(stensia, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        CombatAbilities.HasFirstStrike(attacker).Should().BeFalse(
            "the anthem ends when Stensia Masquerade leaves play");
    }

    // -----------------------------------------------------------------------
    // "Whenever a Vampire you control deals combat damage to a player, put a
    //  +1/+1 counter on it."
    // -----------------------------------------------------------------------

    [Fact]
    public void StensiaMasquerade_HasOneTriggeredAbility()
    {
        // The Vampire combat-damage trigger is attached as a real
        // ITriggeredAbility on BOTH the shape-only and effects-aware paths so the
        // prod TriggerManager binds it on ETB (and the trigger-wiring audit sees
        // it).
        StensiaMasqueradeFactory.Create(_alice)
            .Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    /// <summary>
    /// Publish a <see cref="CombatDamageDealtEvent"/>, then run any pending
    /// triggers the bound <see cref="TriggerManager"/> queued onto the stack and
    /// resolve the top — the live trigger path (CR 603.3 → 608).
    /// </summary>
    private void FireCombatDamage(CombatDamageDealtEvent e)
    {
        _bus.Publish(e);
        if (_triggers.PendingCount == 0) return;
        _triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(_stack, _alice, _alice, _bob);
    }

    [Fact]
    public void Trigger_VampireYouControl_CombatDamageToPlayer_GetsCounter()
    {
        var vampire = Creature("Nighthawk", _alice, CardSubtype.Vampire);
        PutOnBattlefield(vampire, _alice);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        FireCombatDamage(new CombatDamageDealtEvent(vampire, _bob, amount: 2));

        vampire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 603.1 — a Vampire you control dealing combat damage to a player gets a +1/+1 counter");
    }

    [Fact]
    public void Trigger_NonVampire_NoCounter()
    {
        var bear = Creature("Bear", _alice);
        PutOnBattlefield(bear, _alice);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        FireCombatDamage(new CombatDamageDealtEvent(bear, _bob, amount: 2));

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the trigger is scoped to Vampires — a non-Vampire gets no counter");
    }

    [Fact]
    public void Trigger_CombatDamageToCreature_NoCounter()
    {
        var vampire = Creature("Nighthawk", _alice, CardSubtype.Vampire);
        PutOnBattlefield(vampire, _alice);
        var blocker = Creature("Wall", _bob);
        PutOnBattlefield(blocker, _bob);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        // Combat damage to a CREATURE, not a player.
        FireCombatDamage(new CombatDamageDealtEvent(vampire, blocker, amount: 2));

        vampire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the trigger requires combat damage to a PLAYER (CR 510)");
    }

    [Fact]
    public void Trigger_OpponentsVampire_NoCounter()
    {
        var enemyVampire = Creature("EnemyVamp", _bob, CardSubtype.Vampire);
        PutOnBattlefield(enemyVampire, _bob);

        var stensia = StensiaMasqueradeFactory.Create(_alice, _effects);
        PutOnBattlefield(stensia, _alice);

        FireCombatDamage(new CombatDamageDealtEvent(enemyVampire, _alice, amount: 2));

        enemyVampire.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the trigger is scoped to a Vampire YOU control");
    }
}
