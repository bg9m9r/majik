using FluentAssertions;
using Majik.Server.Matches;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class StubDeckLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsSixtyCards()
    {
        var loader = new StubDeckLoader();

        var deck = await loader.LoadAsync("anything", CancellationToken.None);

        deck.Should().HaveCount(60);
    }

    [Fact]
    public async Task LoadAsync_AcceptsAnyDeckIdString()
    {
        var loader = new StubDeckLoader();

        var d1 = await loader.LoadAsync("burn", CancellationToken.None);
        var d2 = await loader.LoadAsync("stompy", CancellationToken.None);
        var d3 = await loader.LoadAsync("", CancellationToken.None);

        d1.Should().HaveCount(60);
        d2.Should().HaveCount(60);
        d3.Should().HaveCount(60);
    }
}
