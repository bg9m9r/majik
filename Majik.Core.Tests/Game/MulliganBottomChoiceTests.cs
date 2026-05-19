using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;
using Creature = Majik.Core.Cards.Creature;

public class MulliganBottomChoiceTests
{
    private readonly Player _alice = new("Alice", 20);

    private void StackLibrary(int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = i < n / 2
                ? (ICard)new Land($"Land{i}") { Owner = _alice }
                : new Creature($"Bear{i}", "1G", 2, 2) { Owner = _alice };
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }
    }

    [Fact]
    public async Task SingleMulligan_AgentChoosesTwoCardsToBottom()
    {
        StackLibrary(60);
        var ctrl = new MulliganController();

        // Agent: mulligan once, then keep — and on bottom-choice, pick the
        // first two creatures in hand.
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        // Bottom-choice will run once (after final keep with 1 mulligan).
        // Agent picks the last card in hand to bottom.
        agent.QueueCardsToBottom(hand =>
            new[] { hand[hand.Count - 1] });

        await ctrl.RunAsync(_alice, agent, new GameContext(_alice, new[] { _alice }, _alice, 0, null, new Majik.Core.Stack.Stack()));

        _alice.Zones.Hand.GetCards().Should().HaveCount(7 - 1);
    }

    [Fact]
    public async Task NoMulligan_NoBottomChoice()
    {
        StackLibrary(60);
        var ctrl = new MulliganController();
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Keep);

        await ctrl.RunAsync(_alice, agent, new GameContext(_alice, new[] { _alice }, _alice, 0, null, new Majik.Core.Stack.Stack()));
        _alice.Zones.Hand.GetCards().Should().HaveCount(7);
    }
}
