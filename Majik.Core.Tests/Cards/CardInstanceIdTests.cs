using FluentAssertions;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Tests.Cards;

public class CardInstanceIdTests
{
    [Fact]
    public void NewCard_HasNonEmptyInstanceId()
    {
        var card = new Creature("Bear", "1G", 2, 2);

        card.InstanceId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoCardsSameName_HaveDifferentInstanceIds()
    {
        var a = new Creature("Bear", "1G", 2, 2);
        var b = new Creature("Bear", "1G", 2, 2);

        a.InstanceId.Should().NotBe(b.InstanceId);
    }

    [Fact]
    public void InstanceId_StableAcrossAccesses()
    {
        var card = new Instant("Bolt", "R");
        var first = card.InstanceId;

        for (var i = 0; i < 100; i++)
        {
            card.InstanceId.Should().Be(first);
        }
    }
}
