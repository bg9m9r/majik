using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

public class MulliganControllerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public async Task Keep_OnFirstAsk_Leaves7CardsInHand()
    {
        SeedLibrary(20);
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Keep);

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(0);
        _alice.Zones.Hand.Count.Should().Be(7);
    }

    [Fact]
    public async Task OneMulligan_KeepNext_Leaves7CardsInHand_OneOnBottom()
    {
        SeedLibrary(20);
        var agent = new ScriptedAgent();
        agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => new[] { hand[0] });

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(1);
        // London mulligan: still draw 7, but bottom N after keep.
        _alice.Zones.Hand.Count.Should().Be(6);
    }

    [Fact]
    public async Task AllMulligansKeptOnLastDraw_StopsAt7Mulligans()
    {
        SeedLibrary(60);
        var agent = new ScriptedAgent();
        for (var i = 0; i < 8; i++) agent.QueueMulligan(MulliganDecision.Mulligan);
        agent.QueueMulligan(MulliganDecision.Keep);
        agent.QueueCardsToBottom(hand => hand.Take(7).ToList());

        var ctrl = new MulliganController();
        var taken = await ctrl.RunAsync(_alice, agent, NewContext());

        taken.Should().Be(7);
        _alice.Zones.Hand.Count.Should().Be(0); // 7 - 7 bottomed
    }

    private void SeedLibrary(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = NamedCardFactory.Create("Mountain", _alice);
            _alice.Zones.Library.AddCard(card);
        }
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice }, _alice, 1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());
}
