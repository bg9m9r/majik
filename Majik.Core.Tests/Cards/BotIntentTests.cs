using FluentAssertions;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Tests.Cards;

public class BotIntentTests
{
    [Fact]
    public void None_IsZero()
    {
        ((ulong)BotIntent.None).Should().Be(0);
    }

    [Fact]
    public void HasAny_DetectsAnyMatchingFlag()
    {
        var i = BotIntent.Burn | BotIntent.Reach;
        i.HasAny(BotIntent.Burn).Should().BeTrue();
        i.HasAny(BotIntent.Removal).Should().BeFalse();
        i.HasAny(BotIntent.Burn | BotIntent.Removal).Should().BeTrue();
    }

    [Fact]
    public void HasAll_RequiresAllFlags()
    {
        var i = BotIntent.Burn | BotIntent.Reach;
        i.HasAll(BotIntent.Burn).Should().BeTrue();
        i.HasAll(BotIntent.Burn | BotIntent.Reach).Should().BeTrue();
        i.HasAll(BotIntent.Burn | BotIntent.Removal).Should().BeFalse();
    }

    [Fact]
    public void CombatTrick_ComposesWithBuff()
    {
        var lightningBolt = BotIntent.Burn | BotIntent.Reach;
        var giantGrowth = BotIntent.Buff | BotIntent.CombatTrick;
        lightningBolt.HasAny(BotIntent.Buff).Should().BeFalse();
        giantGrowth.HasAll(BotIntent.Buff | BotIntent.CombatTrick).Should().BeTrue();
    }
}
