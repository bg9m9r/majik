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
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Heartfire Hero (Bloomburrow, {R}, Creature — Mouse Soldier
/// 1/1). Oracle text (verified against Scryfall):
///   "Valiant — Whenever this creature becomes the target of a spell or
///    ability you control for the first time each turn, put a +1/+1 counter
///    on it.
///    When this creature dies, it deals damage equal to its power to each
///    opponent."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Two triggered abilities: Valiant + dies.
///   - Valiant: the controller's own spell targeting Heartfire Hero puts a
///     +1/+1 counter on it (CR 603.6c / 115.6).
///   - Valiant once-per-turn cap (CR 603.2 / 603.3) + turn-boundary reset
///     (CR 500.1).
///   - Valiant does NOT trigger off a spell/ability an opponent controls.
///   - Dies trigger: deals damage equal to its power (LKI, counters included)
///     to each opponent (CR 603.6d / 603.10).
/// </summary>
public class HeartfireHeroFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private static Majik.Core.Spells.Spell NewSpellTargeting(
        Player controller, Creature target, string name = "Boon")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller, new[] { Target.Permanent(target) });
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HeartfireHero_Identity_MouseSoldier_1_1_AtCostR()
    {
        var card = HeartfireHeroFactory.Create(_alice);

        card.Name.Should().Be("Heartfire Hero");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mouse).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HeartfireHero()
    {
        var card = NamedCardFactory.Create("Heartfire Hero", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Heartfire Hero");
        card.HasSubtype(CardSubtype.Mouse).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        // Two triggered abilities: Valiant + dies.
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void HeartfireHero_HasTwoTriggeredAbilities_ValiantAndDies()
    {
        var card = HeartfireHeroFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Valiant (CR 603.6c, 115.6 — first target each turn)
    // -----------------------------------------------------------------------

    [Fact]
    public void Valiant_OwnSpellTargetsHero_PutsPlusOnePlusOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = HeartfireHeroFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var spell = NewSpellTargeting(_alice, card, "Giant Growth");
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Valiant triggers when the hero becomes the target of a spell its controller controls");

        var valiant = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Valiant")));
        foreach (var e in valiant.Effects) e.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        // ActiveEffects is wired, so the counter raises power/toughness.
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
    }

    [Fact]
    public void Valiant_SecondTargetSameTurn_DoesNotRetrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = HeartfireHeroFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var spell1 = NewSpellTargeting(_alice, card, "S1");
        bus.Publish(new TargetsChosenEvent(spell1, spell1.Targets));

        var spell2 = NewSpellTargeting(_alice, card, "S2");
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));

        triggers.PendingCount.Should().Be(1,
            "Valiant only triggers the FIRST time each turn (CR 603.2 / 603.3)");
    }

    [Fact]
    public void Valiant_TurnBoundary_ResetsFirstTargetCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = HeartfireHeroFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var spell1 = NewSpellTargeting(_alice, card, "S1");
        bus.Publish(new TargetsChosenEvent(spell1, spell1.Targets));
        triggers.PendingCount.Should().Be(1);

        // New turn — reset the once-per-turn counter (CR 500.1).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        var spell2 = NewSpellTargeting(_alice, card, "S2");
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));
        triggers.PendingCount.Should().Be(2,
            "after the turn boundary the next target re-triggers Valiant");
    }

    [Fact]
    public void Valiant_OpponentsSpellTargetingHero_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = HeartfireHeroFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var spell = NewSpellTargeting(_bob, card, "Bob's Bolt");
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Valiant only triggers off a spell or ability YOU control");
    }

    // -----------------------------------------------------------------------
    // Dies trigger (CR 603.6d — "When this creature dies, it deals damage
    // equal to its power to each opponent.")
    // -----------------------------------------------------------------------

    [Fact]
    public void Dies_DealsDamageEqualToPower_ToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = HeartfireHeroFactory.Create(
            _alice, bus, triggers, effects, opponentResolver: () => new[] { _bob, _carol });
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Bump power to 3 via two +1/+1 counters (1/1 base + 2).
        card.Counters.Add(CounterType.PlusOnePlusOne, 2);
        card.Power.Should().Be(3);

        // Hero dies: battlefield -> graveyard.
        _alice.Zones.Battlefield.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1, "the dies trigger fires on its own death (CR 603.6d)");

        var dies = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("dies")));
        foreach (var e in dies.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "each opponent takes 3 damage (LKI power including counters)");
        _carol.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Dies_NoOpponentResolver_NoDamage_LifegainSafe()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Single-arg shape: no opponent resolver -> dies effect is a no-op.
        var card = HeartfireHeroFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var dies = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("dies")));
        foreach (var e in dies.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20);
    }
}
