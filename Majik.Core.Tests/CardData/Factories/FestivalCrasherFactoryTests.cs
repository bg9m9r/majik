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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FestivalCrasherFactory"/> (Modern Horizons 2,
/// {1}{R}).
///
/// Card: Creature — Devil 1/3.
///   "Whenever you cast an instant or sorcery spell, this creature gets
///    +2/+0 until end of turn."
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Devil subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Single-arg shape-only path attaches no trigger (mirrors Soul-Scar Mage /
///   Sprite Dragon shape-only posture).
/// - Instant cast by controller → +2/+0 until end of turn (P/T 1/3 → 3/3).
/// - Sorcery cast by controller → +2/+0 until end of turn.
/// - Creature spell does NOT fire the trigger.
/// - Opponent's instant cast does NOT fire (controller scoped, CR 603.1).
/// </summary>
[Trait("Color", "R")]
public class FestivalCrasherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Spark")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Bolt")
    {
        var sorcery = new Sorcery(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
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
    public void FestivalCrasher_Identity_Devil_1_3_AtCost1R()
    {
        var fc = FestivalCrasherFactory.Create(_alice);

        fc.Name.Should().Be("Festival Crasher");
        fc.ManaCost.Should().Be("{1}{R}");
        fc.HasType(CardType.Creature).Should().BeTrue();
        fc.HasSubtype(CardSubtype.Devil).Should().BeTrue("Festival Crasher is a Devil");
        fc.BasePower.Should().Be(1);
        fc.BaseToughness.Should().Be(3);
        fc.Owner.Should().BeSameAs(_alice);
        fc.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SingleArg_ShapeOnly_DoesNotWireTrigger()
    {
        var fc = FestivalCrasherFactory.Create(_alice);

        // Shape-only path: no cast trigger attached (mirrors Soul-Scar Mage /
        // Sprite Dragon shape-only posture used by dispatcher tests).
        fc.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "single-arg dispatcher path is shape-only — no cast trigger");
    }

    // -----------------------------------------------------------------------
    // Instant cast by controller → +2/+0 until end of turn (1/3 → 3/3)
    // -----------------------------------------------------------------------

    [Fact]
    public void InstantSpellCastByController_PumpsPlus2Plus0_PT_Goes_To_3_3()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var fc = FestivalCrasherFactory.Create(_alice, effects, triggers);
        fc.SetZone(ZoneType.Battlefield);

        // Baseline 1/3 before any cast.
        fc.Power.Should().Be(1);
        fc.Toughness.Should().Be(3);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        var trig = stack.Pop()!;
        trig.Resolve();

        fc.Power.Should().Be(3, "+2/+0 bumps base 1/3 to 3/3 until end of turn");
        fc.Toughness.Should().Be(3, "toughness is unchanged by +2/+0");
    }

    // -----------------------------------------------------------------------
    // Sorcery cast by controller → +2/+0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void SorcerySpellCastByController_PumpsPlus2Plus0()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var fc = FestivalCrasherFactory.Create(_alice, effects, triggers);
        fc.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Wrath")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        fc.Power.Should().Be(3);
        fc.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Creature spell → no trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureSpellCastByController_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var fc = FestivalCrasherFactory.Create(_alice, effects, triggers);
        fc.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Goblin Guide")));

        triggers.PendingCount.Should().Be(0);
        fc.Power.Should().Be(1, "creature spells don't pump Festival Crasher");
        fc.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Opponent's instant cast → no trigger (controller scope, CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void InstantSpellCastByOpponent_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var fc = FestivalCrasherFactory.Create(_alice, effects, triggers);
        fc.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "OpponentBolt")));

        triggers.PendingCount.Should().Be(0);
        fc.Power.Should().Be(1);
        fc.Toughness.Should().Be(3);
    }
}
