using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Spells;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Wire coverage for the stack-target overhaul: a targets prompt whose legal
/// pool includes a SPELL on the stack (counterspell "target spell") must ship
/// that spell on the new <c>PromptPayload.StackCandidates</c>
/// (id/cardName/controllerId) — the post-#3005 snapshot only carried
/// <c>OfType&lt;ICard&gt;()</c> + <c>OfType&lt;Player&gt;()</c>, silently
/// dropping stack spells, so the portal could never render a stack spell as a
/// clickable target. Inbound still resolves the spell's id
/// (CandidateMatchesId now matches <c>ISpell.Id</c>), so submitting the spell's
/// id resolves to the Spell object.
/// </summary>
public sealed class StackTargetCandidateWireTests
{
    [Fact]
    public void TargetsPrompt_with_stack_spell_ships_StackCandidates()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        // Bob's Lightning Bolt on the stack; Alice is casting a counterspell.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = bob, Controller = bob };
        var spell = new Spell(bolt, bob);

        var lookup = new Dictionary<Guid, ICard>();
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice, [bob.Id] = bob };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target spell",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { spell });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        Majik.Core.Api.PromptPayload? payloadAtPromptTime = null;
        agent.PromptRequested += _ => payloadAtPromptTime = agent.PendingPayload;

        var task = agent.ChooseTargetsAsync(ctx, req);

        payloadAtPromptTime.Should().NotBeNull();
        payloadAtPromptTime!.StackCandidates.Should().NotBeNull(
            "a targets prompt whose pool includes a stack spell ships StackCandidates");
        var sc = payloadAtPromptTime.StackCandidates!.Single();
        sc.Id.Should().Be(spell.Id);
        sc.CardName.Should().Be("Lightning Bolt");
        sc.ControllerId.Should().Be(bob.Id);

        // No cards / players in the pool → those payloads stay null.
        payloadAtPromptTime.Candidates.Should().BeNull();
        payloadAtPromptTime.PlayerCandidates.Should().BeNull();

        // Cleanup the pending prompt.
        agent.Submit(new ChooseTargetsCommand(new[] { spell.Id }) { PlayerId = alice.Id });
    }

    [Fact]
    public async Task TargetsPrompt_accepts_spell_id_inbound()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = bob, Controller = bob };
        var spell = new Spell(bolt, bob);

        var lookup = new Dictionary<Guid, ICard>();
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice, [bob.Id] = bob };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target spell",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { spell });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);
        agent.Submit(new ChooseTargetsCommand(new[] { spell.Id }) { PlayerId = alice.Id });

        var chosen = await task;
        chosen.Should().ContainSingle().Which.Should().BeSameAs(spell,
            "submitting the spell's id resolves to the Spell (CandidateMatchesId matches ISpell.Id)");
    }
}
