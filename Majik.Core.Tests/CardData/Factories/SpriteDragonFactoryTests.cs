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
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpriteDragonFactory"/> (Ikoria: Lair of
/// Behemoths, {U}{R}).
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Faerie + Dragon subtypes,
///   Flying keyword marker, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cast-noncreature trigger fires and places a +1/+1 counter; P/T
///   recomputes to 2/2 via ContinuousEffectsService's layer 7c counter
///   handler.
/// - Cast-creature trigger does NOT fire.
/// - Opponent's noncreature cast does NOT fire (controller scoped).
/// </summary>
public class SpriteDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Spark")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Goblin")
    {
        var creatureCard = new Creature(name, "R", 1, 1) { Owner = controller };
        return new Majik.Core.Spells.Spell(creatureCard, controller);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpriteDragon_Identity_FaerieDragon_1_1_AtCostUR()
    {
        var sd = SpriteDragonFactory.Create(_alice);

        sd.Name.Should().Be("Sprite Dragon");
        sd.ManaCost.Should().Be("{U}{R}");
        sd.HasType(CardType.Creature).Should().BeTrue();
        sd.HasSubtype(CardSubtype.Faerie).Should().BeTrue(
            "Sprite Dragon is a Faerie");
        sd.HasSubtype(CardSubtype.Dragon).Should().BeTrue(
            "Sprite Dragon is a Dragon");
        sd.BasePower.Should().Be(1);
        sd.BaseToughness.Should().Be(1);
        sd.Owner.Should().BeSameAs(_alice);
        sd.Controller.Should().BeSameAs(_alice);

        // Flying keyword marker (CR 702.9).
        sd.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Flying is wired as a KeywordAbility marker");
    }

    [Fact]
    public void SpriteDragon_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sprite Dragon", _alice);

        card.Should().BeOfType<Creature>("Sprite Dragon is a Creature");
        card.Name.Should().Be("Sprite Dragon");
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "cast-noncreature-spell trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Cast-noncreature trigger fires → +1/+1 counter (P/T 1/1 → 2/2)
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncreatureSpellCastByController_AddsPlusOnePlusOneCounter_PT_Goes_To_2_2()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sd = SpriteDragonFactory.Create(_alice, triggers);
        sd.SetZone(ZoneType.Battlefield);

        // Wire continuous-effects service so layer 7c counter handler
        // (CR 122.1g) folds +1/+1 counters into the live P/T computation.
        sd.ActiveEffects = new ContinuousEffectsService();

        // Baseline 1/1 before any cast.
        sd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        sd.Power.Should().Be(1);
        sd.Toughness.Should().Be(1);

        // Cast a noncreature (Instant) spell → trigger fires.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        var trig = stack.Pop()!;
        trig.Resolve();

        sd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        sd.Power.Should().Be(2, "+1/+1 counter bumps base 1/1 to 2/2");
        sd.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Cast a creature spell → no trigger, P/T stays 1/1
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureSpellCastByController_DoesNotTrigger_NoCounter_PT_Stays_1_1()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sd = SpriteDragonFactory.Create(_alice, triggers);
        sd.SetZone(ZoneType.Battlefield);
        sd.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Goblin Guide")));

        triggers.PendingCount.Should().Be(0);
        sd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        sd.Power.Should().Be(1);
        sd.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Opponent's noncreature cast → no trigger (controller scope)
    // -----------------------------------------------------------------------

    [Fact]
    public void NoncreatureSpellCastByOpponent_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sd = SpriteDragonFactory.Create(_alice, triggers);
        sd.SetZone(ZoneType.Battlefield);
        sd.ActiveEffects = new ContinuousEffectsService();

        // Bob (opponent) casts a noncreature spell — Sprite Dragon's
        // trigger is controller-scoped (CR 603.1), so no counter.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "OpponentBolt")));

        triggers.PendingCount.Should().Be(0);
        sd.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        sd.Power.Should().Be(1);
        sd.Toughness.Should().Be(1);
    }
}
