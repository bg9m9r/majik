using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Pawpatch Recruit (Bloomburrow, {G}). Creature — Rabbit Warrior 2/1.
/// Oracle text (Scryfall, verified 2026-06-03):
///   "Offspring {2} (...)
///    Trample
///    Whenever a creature you control becomes the target of a spell or ability
///    an opponent controls, put a +1/+1 counter on target creature you control
///    other than that creature."
///
/// Covers the residual "becomes the target of an opponent's spell or ability"
/// trigger (CR 603.6c / 115.6) wired via <see cref="TargetsChosenEvent"/> —
/// previously deferred as "no becomes-the-target event seam". The seam already
/// exists (the event is published by both SpellCaster and AbilityActivator);
/// this card's distinguishing filters are (a) opponent-controlled source and
/// (b) a +1/+1 counter on a chosen OTHER creature you control.
/// </summary>
public class PawpatchRecruitFactoryTests
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

    [Fact]
    public void Pawpatch_Identity_RabbitWarrior_2_1_AtG_WithTrampleAndOffspring()
    {
        var pawpatch = PawpatchRecruitFactory.Create(_alice);

        pawpatch.Name.Should().Be("Pawpatch Recruit");
        pawpatch.ManaCost.Should().Be("{G}");
        pawpatch.HasType(CardType.Creature).Should().BeTrue();
        pawpatch.HasSubtype(CardSubtype.Rabbit).Should().BeTrue();
        pawpatch.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        pawpatch.BasePower.Should().Be(2);
        pawpatch.BaseToughness.Should().Be(1);
        pawpatch.Owner.Should().BeSameAs(_alice);
        pawpatch.Controller.Should().BeSameAs(_alice);
        CombatAbilities.HasTrample(pawpatch).Should().BeTrue();
        pawpatch.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Offspring");
    }

    [Fact]
    public void Pawpatch_NamedCardFactory_Dispatches_FullShape()
    {
        var card = NamedCardFactory.Create("Pawpatch Recruit", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Pawpatch Recruit");
        card.HasSubtype(CardSubtype.Rabbit).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        card.Abilities.OfType<TriggeredAbility>()
            .Should().NotBeEmpty("the becomes-the-target trigger is attached for shape observability");
    }

    [Fact]
    public void Pawpatch_OpponentTargetsYourCreature_FiresAndAddsCounterToOtherCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var recipient = NewCreature(_alice, "Llanowar Elves");
        var pawpatch = PawpatchRecruitFactory.Create(
            _alice, bus, triggers, counterRecipientResolver: targeted =>
                // "target creature you control other than that creature"
                ReferenceEquals(targeted, recipient) ? null : recipient);
        _alice.Zones.Battlefield.AddCard(pawpatch);
        pawpatch.SetZone(ZoneType.Battlefield);

        // Bob (opponent) casts Lightning Bolt targeting Alice's Pawpatch.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(pawpatch) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting a creature Alice controls fires Pawpatch's residual trigger");

        var trigger = pawpatch.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("+1/+1 counter")));
        foreach (var e in trigger.Effects) e.Execute();

        recipient.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counter goes on the chosen OTHER creature you control");
        pawpatch.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the counter goes on the OTHER creature, not the targeted one");
    }

    [Fact]
    public void Pawpatch_YourOwnSpellTargetsYourCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var pawpatch = PawpatchRecruitFactory.Create(_alice, bus, triggers, counterRecipientResolver: _ => null);
        _alice.Zones.Battlefield.AddCard(pawpatch);
        pawpatch.SetZone(ZoneType.Battlefield);

        // Alice (the controller) casts a spell targeting her own creature —
        // "an opponent controls" is NOT satisfied, so the trigger must not fire.
        var pump = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(pump, _alice, new[] { Target.Permanent(pawpatch) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires off a spell or ability an OPPONENT controls");
    }

    [Fact]
    public void Pawpatch_OpponentTargetsTheirOwnCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var pawpatch = PawpatchRecruitFactory.Create(_alice, bus, triggers, counterRecipientResolver: _ => null);
        _alice.Zones.Battlefield.AddCard(pawpatch);
        pawpatch.SetZone(ZoneType.Battlefield);

        var bobCreature = NewCreature(_bob, "Goblin Guide");

        // Bob targets HIS OWN creature — not "a creature you control" (Alice).
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bobCreature) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the trigger requires a creature ALICE controls to become the target");
    }
}
