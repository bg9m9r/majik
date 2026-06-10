using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for the <see cref="BotConfig.RiskVoteThreshold"/> kill-switchable risk
/// filter knob: null (the default) resolves to
/// <see cref="DeterminizedSearch.DefaultCatastropheThreshold"/> (-500),
/// <see cref="double.NegativeInfinity"/> disables the filter entirely, and any
/// explicit value passes through unchanged. <see cref="SearchStrategy"/> resolves
/// the knob once at construction and threads it as <c>catastropheThreshold</c>
/// into BOTH determinized call sites (<c>Run</c> and <c>RunBelief</c>).
/// </summary>
public class RiskVoteConfigTests
{
    [Fact]
    public void RiskVoteThreshold_DefaultsNull_AndResolves()
    {
        new BotConfig("Burn").RiskVoteThreshold.Should().BeNull();
        SearchStrategy.ResolveRiskThreshold(null).Should().Be(-500);
        SearchStrategy.ResolveRiskThreshold(double.NegativeInfinity).Should().Be(double.NegativeInfinity);
        SearchStrategy.ResolveRiskThreshold(-250).Should().Be(-250);
    }
}
