using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Galvanic Iteration (Innistrad: Midnight Hunt, {1}{R}).
///
/// Covers:
///   - Card identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch returns the correct shape.
///   - Resolve effect registers a delayed trigger that copies the next
///     instant/sorcery the caster casts (CR 707.10 via
///     <see cref="Majik.Core.Services.SpellCopier"/>).
///   - The delayed trigger ignores creature spells (predicate gates on
///     instant or sorcery).
///   - The delayed trigger ignores opponent's casts (controller-gated).
///   - Printed Flashback {U}{R} alt-cost is built via the parser.
/// </summary>
public class GalvanicIterationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    [Fact]
    public void GalvanicIteration_Identity_Instant_AtCost1R()
    {
        var gi = GalvanicIterationFactory.Create(_alice);

        gi.Name.Should().Be("Galvanic Iteration");
        gi.ManaCost.Should().Be("{1}{R}");
        gi.HasType(CardType.Instant).Should().BeTrue();
        gi.Owner.Should().BeSameAs(_alice);
        gi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GalvanicIteration_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Galvanic Iteration", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Galvanic Iteration");
    }

    [Fact]
    public void Resolve_RegistersDelayedTrigger_OnNextInstantCast_CopyFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Track effect invocations by giving the source instant an
        // observable effect — we count by checking the spell's effect-list
        // invocations through a captured side effect.
        var copyCount = 0;
        var observable = new Instant("Observable Bolt", "R") { Owner = _alice };
        var sourceSpell = new Majik.Core.Spells.Spell(
            observable,
            _alice,
            effects: new IEffect[] { new Effect("count", () => copyCount++) });

        // Run the Galvanic Iteration resolve effect — registers the delayed
        // trigger. No instants cast yet → no copy.
        var effects = GalvanicIterationFactory.BuildResolveEffect(_alice, triggers, stack);
        foreach (var e in effects) e.Execute();
        copyCount.Should().Be(0, "no instant cast yet; trigger is dormant");

        // Now cast the observable instant. Publishing SpellCastEvent runs
        // the delayed-trigger condition → captures the spell → queues the
        // copy effect to the trigger pending list.
        bus.Publish(new SpellCastEvent(sourceSpell));
        triggers.PendingCount.Should().Be(1, "delayed trigger fired on instant cast");

        // Put the pending copy trigger on the stack and resolve it. The
        // copy effect re-runs every effect on the captured spell —
        // SpellCopier v1 stub semantics.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        copyCount.Should().Be(1, "SpellCopier re-executed the captured spell's effects once");
    }

    [Fact]
    public void Resolve_RegistersDelayedTrigger_OnSorceryCast_CopyFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var copyCount = 0;
        var observable = new Sorcery("Observable Lava", "1R") { Owner = _alice };
        var sourceSpell = new Majik.Core.Spells.Spell(
            observable,
            _alice,
            effects: new IEffect[] { new Effect("count", () => copyCount++) });

        var effects = GalvanicIterationFactory.BuildResolveEffect(_alice, triggers, stack);
        foreach (var e in effects) e.Execute();

        bus.Publish(new SpellCastEvent(sourceSpell));
        triggers.PendingCount.Should().Be(1, "delayed trigger fires on sorcery cast too");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        copyCount.Should().Be(1);
    }

    [Fact]
    public void DelayedTrigger_IgnoresCreatureSpells()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var effects = GalvanicIterationFactory.BuildResolveEffect(_alice, triggers, stack);
        foreach (var e in effects) e.Execute();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));
        triggers.PendingCount.Should().Be(0,
            "creature spell isn't instant/sorcery — delayed trigger must not fire");
    }

    [Fact]
    public void DelayedTrigger_IgnoresOpponentsSpells()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var effects = GalvanicIterationFactory.BuildResolveEffect(_alice, triggers, stack);
        foreach (var e in effects) e.Execute();

        // Bob casts an instant — not Alice's "next cast", so no copy.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));
        triggers.PendingCount.Should().Be(0,
            "the delayed trigger gates on the caster — opponent casts must not fire it");
    }

    [Fact]
    public void Resolve_WithoutTriggerManager_IsShapeOnlyNoOp()
    {
        // Shape-only path: no TriggerManager / Stack supplied. The resolve
        // effect runs without throwing; the rider just doesn't subscribe.
        // Mirrors Snapcaster Mage's no-bus path.
        var effects = GalvanicIterationFactory.BuildResolveEffect(_alice, triggers: null, stack: null);
        var act = () =>
        {
            foreach (var e in effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Flashback_AltCost_IsURMana()
    {
        var alt = GalvanicIterationFactory.BuildFlashbackCost();

        alt.Should().NotBeNull();
        var cost = alt.AlternativeManaCost;
        cost.Blue.Should().Be(1, "Flashback {U}{R}");
        cost.Red.Should().Be(1, "Flashback {U}{R}");
        cost.Generic.Should().Be(0);
        cost.TotalValue.Should().Be(2);
    }
}
