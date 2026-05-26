using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Patchwork Automaton (Streets of New Capenna / Aetherdrift
/// reprint oracle).
///
/// Per the Modern-seed oracle:
///   - Artifact Creature — Construct, {2}, 1/1.
///   - Ward {2} (marker — actual Ward consultation is a deferred slice).
///   - Whenever you cast an artifact spell, +1/+1 counter on this creature.
///
/// Covers:
///   - Card shape (name, types, subtype, P/T, mana cost, owner/controller).
///   - Ward keyword marker.
///   - Cast trigger fires on a controller's artifact spell (counter goes
///     down).
///   - Cast trigger does NOT fire on a non-artifact spell.
///   - Cast trigger does NOT fire on an opponent's artifact spell.
///   - NamedCardFactory dispatch.
/// </summary>
public class PatchworkAutomatonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Tin Can")
    {
        var artifact = new Artifact(name, "0", subtypes: null) { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller)
    {
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    [Fact]
    public void PatchworkAutomaton_IsArtifactCreature_Construct_1_1_AtCost2()
    {
        var c = PatchworkAutomatonFactory.Create(_alice);

        c.Name.Should().Be("Patchwork Automaton");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PatchworkAutomaton_HasWardKeywordMarker()
    {
        var c = PatchworkAutomatonFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Ward");
    }

    [Fact]
    public void PatchworkAutomaton_BuildWardEffect_Returns2Cost()
    {
        var c = PatchworkAutomatonFactory.Create(_alice);
        var ward = PatchworkAutomatonFactory.BuildWardEffect(c);

        ward.Should().NotBeNull();
        // Generic cost {2}: ManaCost.ToString flattens to "2".
        ward.Cost.Generic.Should().Be(2);
        ward.Source.Should().BeSameAs(c);
    }

    [Fact]
    public void PatchworkAutomaton_CastArtifactSpell_QueuesTrigger_AndAddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var auto = PatchworkAutomatonFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(auto);
        auto.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "casting an artifact spell fires Patchwork Automaton's trigger (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        auto.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void PatchworkAutomaton_CastNonArtifactSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var auto = PatchworkAutomatonFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(auto);
        auto.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "a vanilla creature spell is not an artifact spell");
        auto.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void PatchworkAutomaton_OpponentCastsArtifactSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var auto = PatchworkAutomatonFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(auto);
        auto.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' is controller-scoped (CR 109.5)");
        auto.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void PatchworkAutomaton_MultipleArtifactCasts_StackCounters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var auto = PatchworkAutomatonFactory.Create(_alice, bus, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(auto);
        auto.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Memnite")));
        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Ornithopter")));
        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Bone Saw")));

        triggers.PendingCount.Should().Be(3);
        triggers.PutPendingTriggersOnStack(_alice);
        // Drain the stack.
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }

        auto.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "each artifact cast lands an independent +1/+1 counter");
    }

    [Fact]
    public void PatchworkAutomaton_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Patchwork Automaton", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Patchwork Automaton");
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }
}
