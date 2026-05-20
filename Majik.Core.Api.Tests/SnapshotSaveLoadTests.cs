using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Xunit;

namespace Majik.Core.Api.Tests;

public class SnapshotSaveLoadTests
{
    [Fact]
    public async Task Save_ReturnsValidJson_RoundTripsViaLoad()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();

        var blob = facade.Save();
        var loaded = SpectatorSnapshot.Load(blob);

        loaded.Should().BeEquivalentTo(facade.GetState());
    }

    [Fact]
    public async Task Save_ProducesParseableJson()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        await facade.StartAsync();

        var blob = facade.Save();

        var text = System.Text.Encoding.UTF8.GetString(blob);
        var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("Players").GetArrayLength().Should().Be(2);
    }
}
