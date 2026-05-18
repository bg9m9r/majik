using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Players;
using Majik.Core.Services;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Services;

public class ApnapOrderingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Order_ActivePlayerTriggersFirst_ThenOpponentTriggers()
    {
        var aliceTrig = TrigFor(_alice, timestamp: 100);
        var bobTrig = TrigFor(_bob, timestamp: 50);

        var ordered = ApnapOrdering.Order(
            new[] { bobTrig, aliceTrig },
            activePlayer: _alice);

        ordered.Should().Equal(aliceTrig, bobTrig);
    }

    [Fact]
    public void Order_WithinSamePlayer_OrdersByTimestampAscending()
    {
        var first = TrigFor(_alice, timestamp: 10);
        var second = TrigFor(_alice, timestamp: 20);
        var third = TrigFor(_alice, timestamp: 30);

        var ordered = ApnapOrdering.Order(
            new[] { third, first, second },
            activePlayer: _alice);

        ordered.Should().Equal(first, second, third);
    }

    [Fact]
    public void Order_MixedPlayers_PreservesApnapAndDeterministicSubOrder()
    {
        var aliceA = TrigFor(_alice, timestamp: 5);
        var aliceB = TrigFor(_alice, timestamp: 15);
        var bobA = TrigFor(_bob, timestamp: 10);
        var bobB = TrigFor(_bob, timestamp: 20);

        var ordered = ApnapOrdering.Order(
            new[] { bobB, aliceB, bobA, aliceA },
            activePlayer: _alice);

        ordered.Should().Equal(aliceA, aliceB, bobA, bobB);
    }

    [Fact]
    public void Order_EmptyInput_ReturnsEmpty()
    {
        ApnapOrdering.Order(Array.Empty<ITriggeredAbility>(), _alice)
            .Should().BeEmpty();
    }

    private static ITriggeredAbility TrigFor(Player controller, long timestamp)
    {
        var mock = new Mock<ITriggeredAbility>();
        mock.SetupGet(a => a.Controller).Returns(controller);
        mock.SetupGet(a => a.Timestamp).Returns(new DateTime(2026, 1, 1).AddTicks(timestamp));
        return mock.Object;
    }
}
