using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api.Dtos;
using Xunit;

namespace Majik.Core.Api.Tests.Dtos;

public class DtoSerializationTests
{
    [Fact]
    public void GameStateDto_RoundTrips_NoCycles_NoCustomConverter()
    {
        var state = new GameStateDto(
            GameId: Guid.NewGuid(),
            TurnNumber: 3,
            Phase: "Main",
            ActivePlayerId: Guid.NewGuid(),
            Players: new[]
            {
                new PlayerDto(
                    Id: Guid.NewGuid(),
                    Name: "Alice",
                    Life: 20,
                    HasLost: false,
                    Mana: ManaPoolDto.Empty,
                    Hand: new ZoneDto(Array.Empty<CardSnapshotDto>()),
                    Battlefield: new ZoneDto(new[]
                    {
                        new CardSnapshotDto(
                            InstanceId: Guid.NewGuid(),
                            Name: "Grizzly Bears",
                            ManaCost: "1G",
                            Types: new[] { "Creature" },
                            Power: 2,
                            Toughness: 2,
                            Tapped: false,
                            SummoningSickness: true,
                            Abilities: Array.Empty<AbilityDto>()),
                    }),
                    Graveyard: new ZoneDto(Array.Empty<CardSnapshotDto>()),
                    Library: new ZoneDto(Array.Empty<CardSnapshotDto>()),
                    Exile: new ZoneDto(Array.Empty<CardSnapshotDto>())),
            },
            Stack: Array.Empty<StackObjectDto>());

        var json = JsonSerializer.Serialize(state);

        json.Should().Contain("\"Grizzly Bears\"");
        json.Should().Contain("\"Phase\":\"Main\"");

        var roundTripped = JsonSerializer.Deserialize<GameStateDto>(json);
        roundTripped.Should().BeEquivalentTo(state);
    }
}
