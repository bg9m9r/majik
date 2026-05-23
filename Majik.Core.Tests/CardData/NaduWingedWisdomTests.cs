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
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Nadu, Winged Wisdom (Modern Horizons 3, {G}{W}{U}).
///
/// Covers:
///   - Card identity (name, type, supertype, subtypes, P/T, mana cost,
///     owner/controller, Flying).
///   - NamedCardFactory dispatch.
///   - Live wiring: a spell targeting a creature Nadu's controller
///     controls surfaces the trigger as pending (1st & 2nd casts).
///   - Per-turn cap: a third matching target in the same turn does NOT
///     surface a trigger (CR 603.2 / 603.3 — printed "this ability
///     triggers only twice each turn").
///   - Turn boundary: a TurnStartedEvent resets the per-turn counter so
///     the next turn's first match triggers again.
///   - Resolution: reveal-top → land card goes onto battlefield;
///     non-land card goes to hand.
/// </summary>
public class NaduWingedWisdomTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2) { Owner = controller };
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Card NewCardInLibrary(Player owner, string name, bool isLand)
    {
        ICard c = isLand
            ? new Land(name, supertypes: null, subtypes: new[] { CardSubtype.Forest })
            : new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    [Fact]
    public void Nadu_Identity_LegendaryBirdBard_3_4_AtCostGWU_WithFlying()
    {
        var nadu = NaduWingedWisdomFactory.Create(_alice);

        nadu.Name.Should().Be("Nadu, Winged Wisdom");
        nadu.ManaCost.Should().Be("{G}{W}{U}");
        nadu.HasType(CardType.Creature).Should().BeTrue();
        nadu.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        nadu.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        nadu.HasSubtype(CardSubtype.Bard).Should().BeTrue();
        nadu.BasePower.Should().Be(3);
        nadu.BaseToughness.Should().Be(4);
        nadu.Owner.Should().BeSameAs(_alice);
        nadu.Controller.Should().BeSameAs(_alice);
        CombatAbilities.HasFlying(nadu).Should().BeTrue();
    }

    [Fact]
    public void Nadu_NamedCardFactory_Dispatches_FullShape()
    {
        var card = NamedCardFactory.Create("Nadu, Winged Wisdom", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Nadu, Winged Wisdom");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(4);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        CombatAbilities.HasFlying((Creature)card).Should().BeTrue();
    }

    [Fact]
    public void Nadu_FirstTrigger_FiresAndPutsLandOntoBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nadu = NaduWingedWisdomFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nadu);
        nadu.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);
        var topLand = NewCardInLibrary(_alice, "Forest", isLand: true);

        // Bob casts Lightning Bolt targeting Alice's bear — Nadu sees it.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Nadu triggers when a creature Alice controls becomes the target");

        // Resolve.
        var trigger = nadu.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        topLand.Zone.Should().Be(ZoneType.Battlefield, "revealed land goes onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(topLand);
        topLand.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Nadu_SecondTrigger_Fires_PutsNonLandIntoHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nadu = NaduWingedWisdomFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nadu);
        nadu.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice, "Bear A");
        // Library has a land on top (1st trigger consumes), then a
        // non-land (2nd trigger consumes — to hand).
        var topLand = NewCardInLibrary(_alice, "Forest", isLand: true);
        var nextNonLand = NewCardInLibrary(_alice, "Shock", isLand: false);

        var trigger = nadu.Abilities.OfType<TriggeredAbility>().Single();

        // First target → first trigger.
        var bolt1 = new Instant("Bolt1", "R") { Owner = _bob };
        var spell1 = new Majik.Core.Spells.Spell(bolt1, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell1, spell1.Targets));
        triggers.PendingCount.Should().Be(1);
        foreach (var e in trigger.Effects) e.Execute();

        // Second target → second trigger.
        var bolt2 = new Instant("Bolt2", "R") { Owner = _bob };
        var spell2 = new Majik.Core.Spells.Spell(bolt2, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));
        triggers.PendingCount.Should().Be(2, "Nadu triggers twice each turn");
        foreach (var e in trigger.Effects) e.Execute();

        nextNonLand.Zone.Should().Be(ZoneType.Hand, "non-land revealed → put into hand");
        _alice.Zones.Hand.GetCards().Should().Contain(nextNonLand);
        // The land from the first trigger is on the battlefield.
        topLand.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Nadu_ThirdTriggerBlocked_TwicePerTurnCap()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nadu = NaduWingedWisdomFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nadu);
        nadu.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);
        NewCardInLibrary(_alice, "C1", isLand: false);
        NewCardInLibrary(_alice, "C2", isLand: false);
        NewCardInLibrary(_alice, "C3", isLand: false);

        for (var i = 0; i < 3; i++)
        {
            var bolt = new Instant($"Bolt{i}", "R") { Owner = _bob };
            var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
            bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        }

        triggers.PendingCount.Should().Be(2,
            "the third target this turn must NOT add a third pending trigger");
    }

    [Fact]
    public void Nadu_TurnBoundary_ResetsCounter_NextTurnTriggersAgain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nadu = NaduWingedWisdomFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nadu);
        nadu.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);
        // 4 cards in library — 2 consumed turn 1, 1 consumed turn 2.
        NewCardInLibrary(_alice, "T1A", isLand: false);
        NewCardInLibrary(_alice, "T1B", isLand: false);
        NewCardInLibrary(_alice, "T2A", isLand: false);
        NewCardInLibrary(_alice, "T2B", isLand: false);

        var trigger = nadu.Abilities.OfType<TriggeredAbility>().Single();

        // Turn 1: two triggers fire + resolve, third is capped.
        for (var i = 0; i < 3; i++)
        {
            var bolt = new Instant($"T1Bolt{i}", "R") { Owner = _bob };
            var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
            bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
            // Drain any pending so the per-turn count reflects only the
            // predicate increments; resolving doesn't affect the budget.
            if (triggers.PendingCount > 0) foreach (var e in trigger.Effects) e.Execute();
        }
        triggers.PendingCount.Should().Be(2,
            "two triggers surfaced on turn 1 before the cap halted further matches");

        // New turn — reset.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        var bolt2 = new Instant("T2Bolt", "R") { Owner = _bob };
        var spell2 = new Majik.Core.Spells.Spell(bolt2, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell2, spell2.Targets));

        triggers.PendingCount.Should().Be(3,
            "after the turn-boundary reset, the next target adds a fresh pending trigger");
    }

    [Fact]
    public void Nadu_OpponentsCreatureTargeted_DoesNotTrigger()
    {
        // Sanity: Nadu only triggers for creatures THE CONTROLLER controls.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nadu = NaduWingedWisdomFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nadu);
        nadu.SetZone(ZoneType.Battlefield);

        var bobBear = NewCreature(_bob, "Bob's Bear");

        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bolt, _alice, new[] { Target.Permanent(bobBear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Nadu doesn't care about opponents' creatures being targeted");
    }
}
