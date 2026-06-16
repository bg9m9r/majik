using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Wire coverage for the targeting overhaul: a targets prompt whose legal pool
/// includes a PLAYER must ship that player on the new
/// <c>PromptPayload.PlayerCandidates</c> (id/name/life) — the old
/// <c>.OfType&lt;ICard&gt;()</c> snapshot silently dropped players, so the
/// portal could never render a player as a clickable target. Inbound still
/// resolves the player's id (CandidateMatchesId matches Player.Id), so
/// submitting the player's id resolves to the Player object.
/// </summary>
public sealed class PlayerTargetCandidateWireTests
{
    [Fact]
    public void TargetsPrompt_with_player_in_pool_ships_PlayerCandidates()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
        };
        bob.Zones.Battlefield.AddCard(bear);

        var lookup = new Dictionary<Guid, ICard> { [bear.InstanceId] = bear };
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice, [bob.Id] = bob };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        // "any target" — the pool already carries a creature + the opponent.
        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "any target",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { bear, bob });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        Majik.Core.Api.PromptPayload? payloadAtPromptTime = null;
        agent.PromptRequested += _ => payloadAtPromptTime = agent.PendingPayload;

        var task = agent.ChooseTargetsAsync(ctx, req);

        payloadAtPromptTime.Should().NotBeNull();
        payloadAtPromptTime!.PlayerCandidates.Should().NotBeNull(
            "a targets prompt whose pool includes a player ships PlayerCandidates");
        var pc = payloadAtPromptTime.PlayerCandidates!.Single();
        pc.Id.Should().Be(bob.Id);
        pc.Name.Should().Be("Bob");
        pc.Life.Should().Be(17);

        // The card is still shipped on Candidates.
        payloadAtPromptTime.Candidates.Should().ContainSingle()
            .Which.InstanceId.Should().Be(bear.InstanceId);

        // Cleanup the pending prompt.
        agent.Submit(new ChooseTargetsCommand(new[] { bear.InstanceId }) { PlayerId = alice.Id });
    }

    [Fact]
    public async Task TargetsPrompt_accepts_player_id_inbound()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var lookup = new Dictionary<Guid, ICard>();
        var players = new Dictionary<Guid, Player> { [alice.Id] = alice, [bob.Id] = bob };
        var agent = new RemoteAgent(
            alice,
            id => lookup.GetValueOrDefault(id),
            id => players.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "any target",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { bob });

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);
        agent.Submit(new ChooseTargetsCommand(new[] { bob.Id }) { PlayerId = alice.Id });

        var chosen = await task;
        chosen.Should().ContainSingle().Which.Should().BeSameAs(bob,
            "submitting the player's id resolves to the Player (CandidateMatchesId matches Player.Id)");
    }
}
