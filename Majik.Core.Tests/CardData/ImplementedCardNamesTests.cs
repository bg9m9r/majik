using FluentAssertions;
using Majik.Core.CardData.Factories;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Coverage for <see cref="ImplementedCardNames"/> — the single source of
/// truth (shared by the runtime <c>EmbeddedCardRepository</c> and the
/// export CLI) for which printed names the engine actually implements.
/// </summary>
public class ImplementedCardNamesTests
{
    [Fact]
    public void All_IncludesFactoryBackedNames()
    {
        ImplementedCardNames.All.Should().Contain("Lightning Bolt",
            "LightningBoltFactory carries [CardName(\"Lightning Bolt\")]");
        ImplementedCardNames.All.Should().Contain("Path to Exile",
            "PathToExileFactory carries [CardName(\"Path to Exile\")]");
    }

    [Fact]
    public void All_IncludesInlineFallbacks()
    {
        foreach (var name in ImplementedCardNames.InlineFallbackNames)
            ImplementedCardNames.All.Should().Contain(name);
    }

    [Fact]
    public void Contains_FactoryBacked_True()
    {
        ImplementedCardNames.Contains("Forest").Should().BeTrue();
        ImplementedCardNames.Contains("Lightning Bolt").Should().BeTrue();
    }

    [Fact]
    public void Contains_UnknownOrEmpty_False()
    {
        ImplementedCardNames.Contains("Definitely Not A Real Card").Should().BeFalse();
        ImplementedCardNames.Contains("").Should().BeFalse();
        ImplementedCardNames.Contains(null!).Should().BeFalse();
    }
}
