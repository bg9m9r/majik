using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Unsettled Mariner (Modern Horizons 2, {W}{U}).
///
/// Oracle text (verified against Scryfall):
///   "Changeling (This card is every creature type.)
///    Whenever you or a permanent you control becomes the target of a spell
///    or ability an opponent controls, counter that spell or ability unless
///    its controller pays {1}."
///
/// Covers:
///   - Identity (name, type, Shapeshifter subtype, {W}{U}, 2/2, owner/controller).
///   - Changeling: every modelled creature type stamped (CR 702.73).
///   - NamedCardFactory dispatch.
///   - Soft-counter trigger fires when an opponent's spell targets the
///     Mariner's controller (a player) or a permanent they control.
///   - Resolution: opponent can't pay {1} → spell countered + to graveyard;
///     opponent can pay {1} → spell stays on the stack.
///   - Negative: a spell the controller themselves cast does NOT trigger
///     (the "an opponent controls" gate).
/// </summary>
public class UnsettledMarinerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void Mariner_Identity_ShapeshifterTwoTwo_AtCostWU()
    {
        var mariner = UnsettledMarinerFactory.Create(_alice);

        mariner.Name.Should().Be("Unsettled Mariner");
        mariner.ManaCost.Should().Be("{W}{U}");
        mariner.HasType(CardType.Creature).Should().BeTrue();
        mariner.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        mariner.BasePower.Should().Be(2);
        mariner.BaseToughness.Should().Be(2);
        mariner.Owner.Should().BeSameAs(_alice);
        mariner.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Mariner_Changeling_StampsEveryModelledCreatureType()
    {
        var mariner = UnsettledMarinerFactory.Create(_alice);

        // CR 702.73 — Changeling: the card is every creature type. Spot-check
        // a spread of subtypes the engine models.
        mariner.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        mariner.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        mariner.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        mariner.HasSubtype(CardSubtype.Wizard).Should().BeTrue();

        // The Changeling keyword marker is present for inspection.
        mariner.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Changeling");
    }

    [Fact]
    public void Mariner_NamedCardFactory_Dispatches_FullShape()
    {
        var card = NamedCardFactory.Create("Unsettled Mariner", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Unsettled Mariner");
        card.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Mariner_OpponentSpellTargetsYourCreature_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mariner = UnsettledMarinerFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(mariner);
        mariner.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);

        // Bob casts Lightning Bolt targeting Alice's bear.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting a permanent Alice controls triggers the soft-counter");
    }

    [Fact]
    public void Mariner_OpponentTargetsYouThePlayer_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mariner = UnsettledMarinerFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(mariner);
        mariner.SetZone(ZoneType.Battlefield);

        // Bob's spell targets Alice (the player) — "you" becomes the target.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Player(_alice) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting Alice herself triggers the soft-counter");
    }

    [Fact]
    public void Mariner_ControllerCannotPay_SpellCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mariner = UnsettledMarinerFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(mariner);
        mariner.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bolt.SetZone(ZoneType.Stack);
        stack.Push(spell);
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        // Bob has no mana — he cannot pay {1}. Resolve the trigger.
        var trigger = mariner.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stack.GetAll().Should().NotContain(spell, "the spell is countered when its controller can't pay {1}");
        bolt.Zone.Should().Be(ZoneType.Graveyard, "a countered spell goes to its owner's graveyard (CR 701.5b)");
    }

    [Fact]
    public void Mariner_ControllerPaysTax_SpellNotCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mariner = UnsettledMarinerFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(mariner);
        mariner.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bolt.SetZone(ZoneType.Stack);
        stack.Push(spell);

        // Bob floats one generic-payable mana so he can pay {1}.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        var trigger = mariner.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        stack.GetAll().Should().Contain(spell, "Bob paid {1}, so the spell is NOT countered");
        bolt.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public void Mariner_YourOwnSpell_DoesNotTrigger()
    {
        // "an opponent controls" gate — Alice's own spell targeting her own
        // creature must NOT trigger.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mariner = UnsettledMarinerFactory.Create(_alice, stack, triggers);
        _alice.Zones.Battlefield.AddCard(mariner);
        mariner.SetZone(ZoneType.Battlefield);

        var bear = NewCreature(_alice);

        var spellCard = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(spellCard, _alice, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the soft-counter only fires for a spell or ability an OPPONENT controls");
    }
}
