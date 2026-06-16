using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression guard for the stack/graveyard target surface: a card in a
/// GRAVEYARD is an <see cref="ICard"/>, so it already rides the wire on
/// <c>PromptPayload.Candidates</c> and inbound validation already matches its
/// <c>InstanceId</c> — the graveyard half of the surface needs NO engine
/// change. This test pins that contract so the stack-candidate work (or any
/// future candidate-snapshot change) can't silently regress graveyard targets.
/// </summary>
public sealed class GraveyardTargetCandidateWireTests
{
    [Fact]
    public void TargetsPrompt_with_graveyard_card_rides_Candidates_unchanged()
    {
        var alice = new Player("Alice", 20);

        // A creature sitting in Alice's graveyard (Raise Dead "target creature
        // card in a graveyard"). It is an ICard, so it rides Candidates.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = alice, Controller = alice,
        };
        bear.SetZone(ZoneType.Graveyard);

        var lookup = new Dictionary<Guid, ICard> { [bear.InstanceId] = bear };
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target creature card in a graveyard",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { bear });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        Majik.Core.Api.PromptPayload? payloadAtPromptTime = null;
        agent.PromptRequested += _ => payloadAtPromptTime = agent.PendingPayload;

        var task = agent.ChooseTargetsAsync(ctx, req);

        payloadAtPromptTime.Should().NotBeNull();
        // The graveyard card rides Candidates unchanged (no StackCandidates,
        // no PlayerCandidates synthesized for it).
        payloadAtPromptTime!.Candidates.Should().ContainSingle()
            .Which.InstanceId.Should().Be(bear.InstanceId);
        payloadAtPromptTime.StackCandidates.Should().BeNull();
        payloadAtPromptTime.PlayerCandidates.Should().BeNull();

        // Cleanup the pending prompt.
        agent.Submit(new ChooseTargetsCommand(new[] { bear.InstanceId }) { PlayerId = alice.Id });
    }

    [Fact]
    public async Task TargetsPrompt_accepts_graveyard_card_instanceId_inbound()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = alice, Controller = alice,
        };
        bear.SetZone(ZoneType.Graveyard);

        var lookup = new Dictionary<Guid, ICard> { [bear.InstanceId] = bear };
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target creature card in a graveyard",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { bear });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);
        agent.Submit(new ChooseTargetsCommand(new[] { bear.InstanceId }) { PlayerId = alice.Id });

        var chosen = await task;
        chosen.Should().ContainSingle().Which.Should().BeSameAs(bear,
            "submitting the graveyard card's InstanceId resolves to the ICard (unchanged)");
    }
}
