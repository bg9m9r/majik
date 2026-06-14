using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Pays down the <c>spell-copier-distinct-copy-stack-object</c> deferral:
/// a "copy that spell" effect (CR 707.10) must put a distinct, independent
/// copy as a NEW <see cref="Majik.Core.Stack.IStackObject"/> on the stack
/// (CR 706.10a — "a copy of a spell is itself a spell, placed on the stack")
/// that resolves on top FIRST and then ceases to exist (CR 707.10c /
/// 110.5g — a copy on the stack ceases to exist as a state-based action),
/// leaving the original where it was — NOT re-executing the original's
/// effects in place.
/// </summary>
public class SpellCopierStackObjectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell BuildSpell(Player controller, List<string> log, string tag, object? target = null)
    {
        var instant = new Instant("Bolt", "R") { Owner = controller };
        var effect = new Effect($"verb-{tag}", ctx =>
        {
            var hit = ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0
                ? ctx.ChosenTargets[0][0]?.ToString()
                : "<none>";
            log.Add($"{tag}->{hit}");
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
        var spell = new Majik.Core.Spells.Spell(instant, controller, effects: new IEffect[] { effect });
        if (target is not null) spell.ChosenTargets.Add(target);
        return spell;
    }

    [Fact]
    public void Copy_IsPushedAsDistinctStackObject_AboveTheOriginal()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var added = new List<Majik.Core.Stack.IStackObject>();
        bus.Subscribe<StackObjectAddedEvent>(e => added.Add(e.StackObject));

        var log = new List<string>();
        var original = BuildSpell(_alice, log, "orig", target: _bob);
        stack.Push(original);

        SpellCopier.PushCopyOfTopSpell(stack, original);

        // A distinct copy stack object now sits ABOVE the original (CR 706.10a).
        stack.Count.Should().Be(2, "the copy is a real, distinct stack object");
        stack.Top.Should().NotBeSameAs(original, "the copy is its own IStackObject, not the original");
        added.Should().HaveCount(2, "StackObjectAddedEvent fired for both the original push and the copy");

        var copy = stack.Top.Should().BeOfType<Majik.Core.Spells.Spell>().Subject;
        copy.IsCopy.Should().BeTrue("the top object is flagged as a copy (CR 707)");
        copy.Id.Should().NotBe(original.Id, "the copy has its own identity");
        copy.Controller.Should().BeSameAs(_alice, "the copy is controlled by the copier (CR 707.10)");

        // No effects have run yet — the copy hasn't resolved.
        log.Should().BeEmpty();
    }

    [Fact]
    public void Copy_ResolvesFirst_ThenCeasesToExist_LeavingOriginalUntouched()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var resolver = new StackResolver(bus);

        var log = new List<string>();
        var original = BuildSpell(_alice, log, "orig", target: _bob);
        stack.Push(original);

        SpellCopier.PushCopyOfTopSpell(stack, original);
        stack.Count.Should().Be(2);

        // Resolve the top (the copy) — it resolves against the original's
        // chosen targets (CR 707.10a — targets reused verbatim in v1) and then
        // ceases to exist without moving any card to a zone.
        resolver.ResolveTop(stack);

        log.Should().ContainSingle("the copy resolved its effect once")
            .Which.Should().StartWith("orig->Bob",
                "the copy resolved against the original's chosen target (CR 707.10a)");
        stack.Count.Should().Be(1, "after the copy resolves it ceases to exist; the original remains");
        stack.Top.Should().BeSameAs(original, "the original is left exactly where it was (CR 707.10)");

        // The copy resolving must NOT have dragged the original card off the
        // stack into the graveyard — the original instant is still a spell.
        original.Card.Zone.Should().NotBe(ZoneType.Graveyard,
            "resolving the copy must not move the original card to a zone");
    }

    [Fact]
    public void Copy_OfUntargetedSpell_StillResolvesOnce()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var resolver = new StackResolver(bus);

        var log = new List<string>();
        var original = BuildSpell(_alice, log, "orig"); // no target
        stack.Push(original);

        SpellCopier.PushCopyOfTopSpell(stack, original);
        resolver.ResolveTop(stack);

        log.Should().ContainSingle().Which.Should().Be("orig-><none>");
        stack.Count.Should().Be(1);
    }
}
