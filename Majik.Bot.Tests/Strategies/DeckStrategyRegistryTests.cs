using System.Reflection;
using FluentAssertions;
using Majik.Bot.Strategies;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

public sealed class DeckStrategyRegistryTests
{
    [Fact]
    public void For_ResolvesAttributedStrategy_AndNullForUnknown()
    {
        var asm = Assembly.GetExecutingAssembly();
        DeckStrategyRegistry.For("TestDeck", asm).Should().BeOfType<TestDeckStrategy>();
        DeckStrategyRegistry.For("NoSuchDeck", asm).Should().BeNull();
    }
}

[DeckStrategy("TestDeck")]
public sealed class TestDeckStrategy : IDeckStrategy
{
    public double StrategicScore(GameContext ctx, Player self) => 0;
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => null;
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int n) => null;
}
