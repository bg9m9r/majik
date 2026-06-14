using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Pays down the residual <c>spell-copy-stack-object</c> deferral —
/// CR 707.10a "you may choose new targets for the copy".
///
/// <para>
/// The distinct copy stack object already exists (<see cref="SpellCopierStackObjectTests"/>);
/// the open gap was that the copy reused the original spell's chosen targets
/// verbatim, with no way for the copier's controller to retarget. The new
/// <see cref="SpellCopier.PushCopyOfTopSpellAsync"/> overload reads the
/// original spell's retained per-slot <see cref="Targeting.TargetRequest"/>s
/// (<see cref="Spell.RetargetRequests"/>) and prompts the supplied agent to
/// choose new targets for the copy (CR 707.10a). When the agent declines (or
/// no requests / no agent are available) the copy keeps the original's targets
/// — the prior verbatim behaviour.
/// </para>
/// </summary>
public class SpellCopierRetargetTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private static GameContext NewContext(Player active, params Player[] all) =>
        new(active, all, active, 1, Majik.Core.StateMachine.StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());

    /// <summary>
    /// Build a targeted instant spell that reads its target off
    /// <see cref="ResolutionContext.ChosenTargets"/> index 0 (exactly how a
    /// JSON targeted verb resolves) and records whom it hit. The spell carries
    /// a single 1..1 target request over the candidate pool so the copier can
    /// re-prompt with the same legality (CR 707.10a).
    /// </summary>
    private static Spell BuildRetargetableSpell(
        Player controller, object initialTarget, IReadOnlyList<object> candidates, List<object> hits)
    {
        var instant = new Instant("Bolt", "R") { Owner = controller };
        var effect = new Effect("targeted-verb", ctx =>
        {
            if (ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0)
                hits.Add(ctx.ChosenTargets[0][0]);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });

        var spell = new Spell(instant, controller, effects: new IEffect[] { effect });
        spell.ChosenTargets.Add(initialTarget);
        spell.RetargetRequests = new[]
        {
            new TargetRequest("any target", 1, 1, candidates),
        };
        return spell;
    }

    [Fact]
    public async Task CopyHonorsAgentChosenNewTargets()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var resolver = new StackResolver(bus);

        var hits = new List<object>();
        // Original targets Bob; the copier's agent retargets to Carol.
        var original = BuildRetargetableSpell(
            _alice, initialTarget: _bob, candidates: new object[] { _bob, _carol }, hits);
        stack.Push(original);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)_carol });
        var game = NewContext(_alice, _alice, _bob, _carol);

        await SpellCopier.PushCopyOfTopSpellAsync(stack, original, agent, game);

        stack.Count.Should().Be(2, "the copy is a distinct stack object above the original");
        var copy = stack.Top.Should().BeOfType<Spell>().Subject;
        copy.IsCopy.Should().BeTrue();
        copy.ChosenTargets.Should().ContainSingle()
            .Which.Should().BeSameAs(_carol, "the copier chose a new target (CR 707.10a)");

        // Resolve the copy: it must hit the RE-chosen target, not the original.
        resolver.ResolveTop(stack);
        hits.Should().ContainSingle().Which.Should().BeSameAs(_carol);

        // The ORIGINAL spell's chosen target is untouched — retargeting the copy
        // does not mutate the original (CR 707.10a applies to the copy only).
        original.ChosenTargets.Should().ContainSingle().Which.Should().BeSameAs(_bob);
    }

    [Fact]
    public async Task CopyKeepsOriginalTargets_WhenAgentDeclinesRetarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var hits = new List<object>();
        var original = BuildRetargetableSpell(
            _alice, initialTarget: _bob, candidates: new object[] { _bob, _carol }, hits);
        stack.Push(original);

        // Agent returns no picks ⇒ decline retarget ⇒ keep the original's target.
        var agent = new ScriptedAgent();
        agent.QueueTargets(System.Array.Empty<object>());
        var game = NewContext(_alice, _alice, _bob, _carol);

        await SpellCopier.PushCopyOfTopSpellAsync(stack, original, agent, game);

        var copy = stack.Top.Should().BeOfType<Spell>().Subject;
        copy.ChosenTargets.Should().ContainSingle()
            .Which.Should().BeSameAs(_bob, "declining retarget keeps the original's target verbatim");
    }

    [Fact]
    public async Task CopyKeepsOriginalTargets_WhenNoRetargetRequests()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var hits = new List<object>();
        var instant = new Instant("Bolt", "R") { Owner = _alice };
        var effect = new Effect("v", ctx =>
        {
            if (ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0)
                hits.Add(ctx.ChosenTargets[0][0]);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
        var original = new Spell(instant, _alice, effects: new IEffect[] { effect });
        original.ChosenTargets.Add(_bob);
        // No RetargetRequests stamped (e.g. a hand-built spell) ⇒ verbatim reuse.
        stack.Push(original);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)_carol }); // would-be retarget, ignored
        var game = NewContext(_alice, _alice, _bob, _carol);

        await SpellCopier.PushCopyOfTopSpellAsync(stack, original, agent, game);

        var copy = stack.Top.Should().BeOfType<Spell>().Subject;
        copy.ChosenTargets.Should().ContainSingle().Which.Should().BeSameAs(_bob);
    }
}
