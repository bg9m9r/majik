using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InnkeepersTalentFactory"/> (Bloomburrow, {1}{G}).
///
/// Enchantment — Class {1}{G}. Oracle text:
///   "(Gain the next level as a sorcery to add its ability.)
///    At the beginning of combat on your turn, put a +1/+1 counter on target
///      creature you control.
///    {G}: Level 2
///    Permanents you control with counters on them have ward {1}.
///    {3}{G}: Level 3
///    If you would put one or more counters on a permanent or player, put twice
///      that many of each of those kinds of counters on that permanent or
///      player instead."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: {1}{G}, Enchantment — Class.
/// - Class state binder: Level 1, MaxLevel 3, per-level costs {G} / {3}{G}.
/// - Ability shape: one Level-1 begin-combat trigger + two sorcery-speed
///   level-up activated abilities.
/// - Level-1 begin-combat trigger: puts a +1/+1 counter on the targeted
///   creature you control.
/// - Level-2 ward grant: permanents you control with counters get ward {1}
///   only at level &gt;= 2.
/// - Level-3 counter doubling: any-kind counters on permanents AND player
///   counters double, only at level 3.
/// </summary>
[Trait("Color", "G")]
public class InnkeepersTalentFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void InnkeepersTalent_Identity_EnchantmentClass_OneGreen()
    {
        var c = InnkeepersTalentFactory.Create(_alice);
        c.Name.Should().Be("Innkeeper's Talent");
        c.HasType(CardType.Enchantment).Should().BeTrue("printed oracle is Enchantment — Class");
        c.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "CR 205.3h — Class is an enchantment subtype (CR 716)");

        var parsed = ManaCost.Parse(InnkeepersTalentFactory.PrintedManaCost);
        parsed.Generic.Should().Be(1, "the printed cost is {1}{G}");
        parsed.Green.Should().Be(1);
        parsed.TotalValue.Should().Be(2);
        c.ManaCost.Should().Be(InnkeepersTalentFactory.PrintedManaCost);
    }

    // -----------------------------------------------------------------------
    // Shape
    // -----------------------------------------------------------------------

    [Fact]
    public void InnkeepersTalent_HasOneTrigger_TheBeginCombatCounter()
    {
        var c = InnkeepersTalentFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the Level-1 'beginning of combat, +1/+1 counter on target creature' is a triggered ability; " +
            "Level 2 is a static ward grant and Level 3 is a replacement");
    }

    [Fact]
    public void InnkeepersTalent_HasTwoLevelUpActivatedAbilities_BothSorcerySpeed()
    {
        var c = InnkeepersTalentFactory.Create(_alice);
        var levelUps = c.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activated ability per level above 1 ({G}: Level 2 / {3}{G}: Level 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 — Class level-up activations are sorcery-speed only");
    }

    [Fact]
    public void InnkeepersTalent_ClassStateAttached_LevelOne_MaxThree_Costs()
    {
        var c = InnkeepersTalentFactory.Create(_alice);
        var state = ((Permanent)c).ClassState;
        state.Should().NotBeNull("CR 716 — Class enchantments carry a leveling tracker");
        state!.CurrentLevel.Should().Be(1);
        state.MaxLevel.Should().Be(3);
        state.CostFor(2).Should().Be(ManaCost.Parse("{G}"));
        state.CostFor(3).Should().Be(ManaCost.Parse("{3}{G}"));
    }

    // -----------------------------------------------------------------------
    // Level-1 begin-combat counter trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void InnkeepersTalent_LevelOne_BeginCombat_PutsCounterOnChosenTarget()
    {
        var card = InnkeepersTalentFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1,
            "the begin-combat ability needs one 'target creature you control'");
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var e in trigger.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the targeted creature you control gains one +1/+1 counter");
    }

    [Fact]
    public void InnkeepersTalent_LevelOne_BeginCombat_OnlyFiresOnControllersTurn()
    {
        var (_, _, _, triggers) = Wire();

        // CR 508.1 — "on your turn"; the opponent's begin-combat must not fire it.
        _bus.Publish(new StepStartedEvent(StepStateType.BeginningOfCombat, _bob));
        triggers.PendingCount.Should().Be(0, "the trigger is restricted to the controller's own combat");

        // The controller's begin-combat DOES queue it.
        _bus.Publish(new StepStartedEvent(StepStateType.BeginningOfCombat, _alice));
        triggers.PendingCount.Should().Be(1, "the begin-combat counter trigger queues on the controller's combat");
    }

    // -----------------------------------------------------------------------
    // Level-2 ward grant
    // -----------------------------------------------------------------------

    [Fact]
    public void InnkeepersTalent_LevelTwo_GrantsWardToControlledPermanentsWithCounters()
    {
        var continuous = new ContinuousEffectsService(_bus);

        // Members must consult the layers service for HasEffectiveKeyword.
        var withCounter = new Creature("Counterbearer", "G", 1, 1) { Owner = _alice };
        withCounter.SetController(_alice);
        withCounter.SetZone(ZoneType.Battlefield);
        withCounter.Counters.Add(CounterType.PlusOnePlusOne, 1);
        withCounter.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(withCounter);

        var noCounter = new Creature("Bare", "G", 1, 1) { Owner = _alice };
        noCounter.SetController(_alice);
        noCounter.SetZone(ZoneType.Battlefield);
        noCounter.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(noCounter);

        var card = InnkeepersTalentFactory.Create(_alice, triggers: null, eventBus: _bus,
            continuousEffects: continuous);
        card.SetZone(ZoneType.Battlefield);
        card.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(card);
        var state = ((Permanent)card).ClassState!;

        // The grant attaches while the source is off the battlefield; the
        // ZoneManager move publishes a CardMovedEvent that registers it.
        _bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        // Level 1 — no ward yet (the static is gated on level >= 2).
        withCounter.HasEffectiveKeyword("Ward").Should().BeFalse(
            "the ward grant is gated on ClassState level >= 2");

        state.LevelUpTo(2);
        // Re-sync the live membership (CR 611.2c) — a board event re-runs scope.
        _bus.Publish(new CardMovedEvent(withCounter, ZoneType.Stack, ZoneType.Battlefield));

        withCounter.HasEffectiveKeyword("Ward").Should().BeTrue(
            "at level >= 2 permanents you control WITH counters have ward {1}");
        noCounter.HasEffectiveKeyword("Ward").Should().BeFalse(
            "the grant only reaches permanents that have counters on them");
    }

    // -----------------------------------------------------------------------
    // Level-3 counter doubling
    // -----------------------------------------------------------------------

    [Fact]
    public void InnkeepersTalent_LevelThree_DoublesCountersOnPermanent_AnyKind()
    {
        var replacements = new ReplacementBus();
        var card = InnkeepersTalentFactory.Create(_alice, triggers: null, eventBus: null,
            continuousEffects: null, replacements: replacements);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        var state = ((Permanent)card).ClassState!;

        var creature = new Creature("Target", "G", 2, 2) { Owner = _alice };
        creature.SetController(_alice);
        creature.SetZone(ZoneType.Battlefield);

        // Level 1 — no doubling.
        CountersService.Add(creature, CounterType.PlusOnePlusOne, 1, replacements);
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the doubler is gated on level 3");

        state.LevelUpTo(2);
        state.LevelUpTo(3);

        CountersService.Add(creature, CounterType.PlusOnePlusOne, 1, replacements);
        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "at level 3, one would-be counter doubles to two (1 already present + 2)");

        // "each of those kinds" — any counter kind, not just +1/+1.
        CountersService.Add(creature, CounterType.Charge, 2, replacements);
        creature.Counters.Count(CounterType.Charge).Should().Be(4,
            "at level 3, two charge counters double to four (any kind)");
    }

    [Fact]
    public void InnkeepersTalent_LevelThree_DoublesPlayerCounters()
    {
        var replacements = new ReplacementBus();
        var card = InnkeepersTalentFactory.Create(_alice, triggers: null, eventBus: null,
            continuousEffects: null, replacements: replacements);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        var state = ((Permanent)card).ClassState!;
        state.LevelUpTo(2);
        state.LevelUpTo(3);

        // "or player" — poison counters routed via PlayerCountersService double.
        var placed = PlayerCountersService.Add(_bob, CounterType.Poison, 1, replacements);
        placed.Should().Be(2,
            "at level 3, one poison counter on a player doubles to two");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (Enchantment Card, ClassState State, Majik.Core.Stack.Stack Stack, TriggerManager Triggers) Wire()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = InnkeepersTalentFactory.Create(_alice, triggers, _bus);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        var state = ((Permanent)card).ClassState!;
        return (card, state, stack, triggers);
    }
}
