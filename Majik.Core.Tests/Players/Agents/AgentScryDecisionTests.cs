using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Verifies that DeterministicBotAgent, HeuristicBotAgent, and ScriptedAgent
/// all implement ChooseScryDecisionAsync / ChooseSurveilDecisionAsync correctly.
/// </summary>
public class AgentScryDecisionTests
{
    private static Card MakeCard(string name)
    {
        var c = new Card(name, "");
        return c;
    }

    // -------------------------------------------------------------------------
    // DeterministicBotAgent — Scry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeterministicBot_ScryDecision_DefaultsAllToBottom()
    {
        var agent = new DeterministicBotAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        var decision = await agent.ChooseScryDecisionAsync(null, peeked);

        decision.ToBottom.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task DeterministicBot_ScryDecision_EmptyPeeked_ReturnsEmpty()
    {
        var agent = new DeterministicBotAgent();

        var decision = await agent.ChooseScryDecisionAsync(null, Array.Empty<ICard>());

        decision.ToBottom.Should().BeEmpty();
        decision.TopOrder.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // DeterministicBotAgent — Surveil
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeterministicBot_SurveilDecision_DefaultsAllToGraveyard()
    {
        var agent = new DeterministicBotAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        var decision = await agent.ChooseSurveilDecisionAsync(null, peeked);

        decision.ToGraveyard.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task DeterministicBot_SurveilDecision_EmptyPeeked_ReturnsEmpty()
    {
        var agent = new DeterministicBotAgent();

        var decision = await agent.ChooseSurveilDecisionAsync(null, Array.Empty<ICard>());

        decision.ToGraveyard.Should().BeEmpty();
        decision.TopOrder.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // HeuristicBotAgent — Scry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HeuristicBot_ScryDecision_DefaultsAllToBottom()
    {
        var agent = new HeuristicBotAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        var decision = await agent.ChooseScryDecisionAsync(null, peeked);

        decision.ToBottom.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // HeuristicBotAgent — Surveil
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HeuristicBot_SurveilDecision_DefaultsAllToGraveyard()
    {
        var agent = new HeuristicBotAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        var decision = await agent.ChooseSurveilDecisionAsync(null, peeked);

        decision.ToGraveyard.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ScriptedAgent — Scry queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScriptedAgent_ScryDecision_RespectsQueue()
    {
        var agent = new ScriptedAgent();
        var c1 = MakeCard("A");
        var c2 = MakeCard("B");

        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { c1 },
            TopOrder: new[] { c2 }));

        var decision = await agent.ChooseScryDecisionAsync(null, new ICard[] { c1, c2 });

        decision.ToBottom.Should().ContainSingle().Which.Should().BeSameAs(c1);
        decision.TopOrder.Should().ContainSingle().Which.Should().BeSameAs(c2);
    }

    [Fact]
    public async Task ScriptedAgent_ScryDecision_FallsBackToAllBottomWhenQueueEmpty()
    {
        var agent = new ScriptedAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        // No QueueScryDecision call — should fall back to all-bottom default.
        var decision = await agent.ChooseScryDecisionAsync(null, peeked);

        decision.ToBottom.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task ScriptedAgent_ScryDecision_ConsumesQueueFifo()
    {
        var agent = new ScriptedAgent();
        var c1 = MakeCard("A");
        var c2 = MakeCard("B");

        var first = new ScryAction.ScryDecision(ToBottom: new[] { c1 }, TopOrder: new[] { c2 });
        var second = new ScryAction.ScryDecision(ToBottom: new[] { c2 }, TopOrder: new[] { c1 });

        agent.QueueScryDecision(first);
        agent.QueueScryDecision(second);

        var d1 = await agent.ChooseScryDecisionAsync(null, new ICard[] { c1, c2 });
        var d2 = await agent.ChooseScryDecisionAsync(null, new ICard[] { c1, c2 });

        d1.Should().BeSameAs(first);
        d2.Should().BeSameAs(second);
    }

    // -------------------------------------------------------------------------
    // ScriptedAgent — Surveil queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScriptedAgent_SurveilDecision_RespectsQueue()
    {
        var agent = new ScriptedAgent();
        var c1 = MakeCard("A");
        var c2 = MakeCard("B");

        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: new[] { c1 },
            TopOrder: new[] { c2 }));

        var decision = await agent.ChooseSurveilDecisionAsync(null, new ICard[] { c1, c2 });

        decision.ToGraveyard.Should().ContainSingle().Which.Should().BeSameAs(c1);
        decision.TopOrder.Should().ContainSingle().Which.Should().BeSameAs(c2);
    }

    [Fact]
    public async Task ScriptedAgent_SurveilDecision_FallsBackToAllGraveyardWhenQueueEmpty()
    {
        var agent = new ScriptedAgent();
        var peeked = new ICard[] { MakeCard("A"), MakeCard("B") };

        // No QueueSurveilDecision call — should fall back to all-graveyard default.
        var decision = await agent.ChooseSurveilDecisionAsync(null, peeked);

        decision.ToGraveyard.Should().BeEquivalentTo(peeked);
        decision.TopOrder.Should().BeEmpty();
    }
}
