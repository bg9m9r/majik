using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.BotReplay;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Majik.Server.Composition;
using Xunit;

namespace Majik.Server.Tests.Composition;

/// <summary>
/// Bot-decision persistence — agent installation seams. With no recorder
/// (persistence off) the factory installs today's bare
/// <see cref="Majik.Bot.BotPlayerAgent"/> (byte-identical today-path). With a
/// recorder it wraps the bot in a <see cref="RecordingPlayerAgent"/>; on the
/// rehydrate path a non-empty replay script installs a
/// <see cref="ScriptedPlayerAgent"/> whose continuation is the recording
/// wrapper (live play continues recording at botSeq = script.Count once the
/// replay reaches the live edge).
/// </summary>
public class ServerGameFactoryBotRecordingTests
{
    [Fact]
    public void Create_WithoutRecorder_InstallsBareBotPlayerAgent()
    {
        var factory = BuildFactory();
        var facade = factory.Create(
            "Human", "Bot", BuildDeck(), BuildDeck(), botSeatArchetype: "gruul");

        var agent = AgentRegistry.Get(facade.Bob);
        agent.Should().BeOfType<Majik.Bot.BotPlayerAgent>(
            "persistence off must be byte-identical to today's path — no decorator");
    }

    [Fact]
    public void Create_WithRecorder_WrapsBotInRecordingAgent()
    {
        var factory = BuildFactory();
        var recorded = new List<BotDecisionRecord>();
        var facade = factory.Create(
            "Human", "Bot", BuildDeck(), BuildDeck(), botSeatArchetype: "gruul",
            botDecisionRecorder: r => { recorded.Add(r); return Task.CompletedTask; });

        var agent = AgentRegistry.Get(facade.Bob);
        var recording = agent.Should().BeOfType<RecordingPlayerAgent>().Subject;
        recording.Inner.Should().BeOfType<Majik.Bot.BotPlayerAgent>();
    }

    [Fact]
    public void BuildUnregisteredFacade_WithReplayScript_InstallsScriptedAgentWithRecordingContinuation()
    {
        var factory = BuildFactory();
        var script = new[]
        {
            new BotDecisionRecord(0, BotDecisionKind.YesNo, new YesNoPayload(true)),
        };

        var facade = factory.BuildUnregisteredFacade(
            "Human", "Bot", BuildDeck(), BuildDeck(), botSeatArchetype: "gruul",
            botReplayScript: script,
            botDecisionRecorder: _ => Task.CompletedTask);

        var agent = AgentRegistry.Get(facade.Bob);
        var scripted = agent.Should().BeOfType<ScriptedPlayerAgent>().Subject;
        var continuation = scripted.Continuation.Should().BeOfType<RecordingPlayerAgent>().Subject;
        continuation.Inner.Should().BeOfType<Majik.Bot.BotPlayerAgent>();
    }

    [Fact]
    public void BuildUnregisteredFacade_WithoutScript_KeepsTodaysBareBotAgent()
    {
        var factory = BuildFactory();
        var facade = factory.BuildUnregisteredFacade(
            "Human", "Bot", BuildDeck(), BuildDeck(), botSeatArchetype: "gruul");

        AgentRegistry.Get(facade.Bob).Should().BeOfType<Majik.Bot.BotPlayerAgent>();
    }

    [Fact]
    public async Task RecordingContinuation_StartsAt_ScriptCount()
    {
        // The continuation recording agent must stamp botSeq = script.Count on
        // its first record — a contiguous stream across the rehydration seam.
        var factory = BuildFactory();
        var script = new[]
        {
            new BotDecisionRecord(0, BotDecisionKind.YesNo, new YesNoPayload(true)),
            new BotDecisionRecord(1, BotDecisionKind.YesNo, new YesNoPayload(true)),
        };
        var recorded = new List<BotDecisionRecord>();

        var facade = factory.BuildUnregisteredFacade(
            "Human", "Bot", BuildDeck(), BuildDeck(), botSeatArchetype: "gruul",
            botReplayScript: script,
            botDecisionRecorder: r => { recorded.Add(r); return Task.CompletedTask; });

        var scripted = (ScriptedPlayerAgent)AgentRegistry.Get(facade.Bob)!;

        // Drain the script (two YesNo prompts), then one live prompt.
        await scripted.ChooseYesNoAsync(null, "q1", null);
        await scripted.ChooseYesNoAsync(null, "q2", null);
        await scripted.ChooseYesNoAsync(null, "q3", null);

        recorded.Should().ContainSingle().Which.BotSeq.Should().Be(2);
    }

    private static ServerGameFactory BuildFactory()
        => new(new GameRegistry());

    private static IReadOnlyList<ICard> BuildDeck()
    {
        var cards = new List<ICard>();
        for (var i = 0; i < 24; i++) cards.Add(new Land("Forest"));
        for (var i = 0; i < 12; i++) cards.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        return cards;
    }
}
