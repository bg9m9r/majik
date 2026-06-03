using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
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
/// Unit tests for <see cref="LightningSkelementalFactory"/> (Modern Horizons 2,
/// {B}{R}{R}).
///
/// Creature — Elemental Skeleton 6/1. Oracle text (verified against Scryfall):
///   "Trample, haste
///    Whenever this creature deals combat damage to a player, that player
///    discards two cards.
///    At the beginning of the end step, sacrifice this creature."
///
/// Covers:
///   - Identity (Elemental Skeleton 6/1 at {B}{R}{R}, black + red,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - Trample + Haste keyword markers (CR 702.19 / CR 702.10).
///   - Combat-damage-to-player trigger: damaged player discards two cards
///     (CR 510 / CR 603.1); fewer-than-two graceful; non-fire on damage to a
///     creature and on damage from another source.
///   - End-step self-sacrifice trigger (battlefield → graveyard); unscoped —
///     fires on any player's end step (CR 603.3d).
/// </summary>
[Trait("Color", "R")]
public class LightningSkelementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void LightningSkelemental_Identity()
    {
        var c = LightningSkelementalFactory.Create(_alice);

        c.Name.Should().Be("Lightning Skelemental");
        c.ManaCost.Should().Be("{B}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(1);
        CardColors.GetColors(c).Should().Contain(new[] { ManaColor.Black, ManaColor.Red });
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Keywords ────────────────────────────────────────────────────────

    [Fact]
    public void LightningSkelemental_HasTrampleAndHaste()
    {
        var c = LightningSkelementalFactory.Create(_alice);

        CombatAbilities.HasTrample(c).Should().BeTrue("CR 702.19 — Trample");
        CombatAbilities.HasHaste(c).Should().BeTrue("CR 702.10 — haste");
    }

    // ── Trigger shape ───────────────────────────────────────────────────

    [Fact]
    public void LightningSkelemental_HasCombatDamageAndEndStepTriggers()
    {
        var c = LightningSkelementalFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(2,
                "the combat-damage discard trigger + the end-step self-sacrifice trigger");
    }

    // ── Combat-damage-to-player discard ─────────────────────────────────

    [Fact]
    public void CombatDamageToPlayer_DamagedPlayerDiscardsTwoCards()
    {
        var card = LightningSkelementalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Three cards in Bob's hand — two will be discarded.
        var junk1 = new Card("Junk1", "");
        var junk2 = new Card("Junk2", "");
        var junk3 = new Card("Junk3", "");
        foreach (var j in new[] { junk1, junk2, junk3 })
        {
            j.SetOwner(_bob);
            _bob.Zones.Hand.AddCard(j);
        }

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CombatDamageDealtEvent>);

        var dmgEvent = new CombatDamageDealtEvent(card, _bob, 6);
        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "this creature dealing combat damage to a player matches the trigger");

        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(junk3,
                "the damaged player discards two cards (CR 510 / 603.1); v1 first-cards pick");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { junk1, junk2 });
    }

    [Fact]
    public void CombatDamageToPlayer_FewerThanTwoCards_DiscardsWhatRemains()
    {
        var card = LightningSkelementalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Bob holds a single card — discards as much as possible (one).
        var only = new Card("Only", "");
        only.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(only);

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CombatDamageDealtEvent>);

        trigger.IsTriggered(new CombatDamageDealtEvent(card, _bob, 6)).Should().BeTrue();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow("discarding fewer than two cards is graceful");
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
    }

    [Fact]
    public void CombatDamage_ToCreature_DoesNotFire()
    {
        // Oracle text says "combat damage to a player" — damage to a creature
        // must NOT fire the discard trigger.
        var card = LightningSkelementalFactory.Create(_alice);
        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CombatDamageDealtEvent>);

        var dmgEvent = new CombatDamageDealtEvent(card, blocker, 6);
        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "damage to a creature does not satisfy 'combat damage to a player'");
    }

    [Fact]
    public void CombatDamage_FromAnotherSource_DoesNotFire()
    {
        // Self-sourced: only this creature's combat damage fires the trigger.
        var card = LightningSkelementalFactory.Create(_alice);
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CombatDamageDealtEvent>);

        var dmgEvent = new CombatDamageDealtEvent(other, _bob, 2);
        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "another creature's combat damage does not fire this creature's trigger");
    }

    // ── End-step self-sacrifice ─────────────────────────────────────────

    [Fact]
    public void EndStep_Resolve_SacrificesThisCreature()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = LightningSkelementalFactory.Create(_alice, triggers, zones);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in trigger.Effects) e.Execute();

        // "At the beginning of the end step, sacrifice this creature."
        // CR 701.16 — battlefield → owner's graveyard.
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void EndStep_TriggerFiresOnAnyPlayersEndStep()
    {
        // CR 603.3d — an unscoped "At the beginning of the end step" trigger
        // fires on every player's end step, not just the controller's.
        var card = LightningSkelementalFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

        var aliceEnd = new StepStartedEvent(StepStateType.End, _alice);
        var bobEnd = new StepStartedEvent(StepStateType.End, _bob);
        var bobUpkeep = new StepStartedEvent(StepStateType.Upkeep, _bob);

        trigger.Condition.Matches(aliceEnd, null!).Should().BeTrue();
        trigger.Condition.Matches(bobEnd, null!).Should().BeTrue();
        trigger.Condition.Matches(bobUpkeep, null!).Should().BeFalse();
    }

    [Fact]
    public void EndStep_WithoutZoneService_StillSacrifices()
    {
        var card = LightningSkelementalFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in trigger.Effects) e.Execute();

        card.Zone.Should().Be(ZoneType.Graveyard);
    }
}
