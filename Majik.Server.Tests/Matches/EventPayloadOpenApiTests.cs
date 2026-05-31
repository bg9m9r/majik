using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// PLAN 07 — verifies the typed EventDto payload records reach the OpenAPI
/// document. The SignalR <c>event</c> channel ships payloads as raw JSON,
/// so the schemas are anchored through the unused <c>GET
/// /matches/_eventschemas</c> endpoint returning <c>EventPayloadCatalog</c>
/// (which references every <c>*Payload</c> record). If the anchor or a
/// payload record is dropped, <c>ng-openapi-gen</c> would silently stop
/// emitting the corresponding portal interface — this test guards that.
/// </summary>
public class EventPayloadOpenApiTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public EventPayloadOpenApiTests(TestAppFactory factory) => _factory = factory;

    // Every currently-emitted event payload record (PLAN 07 first cut).
    private static readonly string[] PayloadSchemaNames =
    {
        "CardMovedPayload",
        "CardDrawnPayload",
        "CardRevealedPayload",
        "LifeChangedPayload",
        "PhaseStartedPayload",
        "PhaseEndedPayload",
        "PhaseChangedPayload",
        "TurnStateChangedPayload",
        "StepStartedPayload",
        "StepEndedPayload",
        "TurnStartedPayload",
        "TurnEndedPayload",
        "ExtraPhaseAddedPayload",
        "PlayerLostPayload",
        "StackObjectPayload",
        "DamageDealtPayload",
        "CounterAddedPayload",
    };

    [Fact]
    public async Task OpenApiDocument_ContainsEveryEventPayloadSchema()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/openapi/v1.json");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var schemas = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        foreach (var name in PayloadSchemaNames)
        {
            schemas.TryGetProperty(name, out _).Should().BeTrue(
                $"OpenAPI components.schemas must expose '{name}' so " +
                "ng-openapi-gen emits its portal interface (PLAN 07). " +
                "Missing means the EventPayloadCatalog anchor or the " +
                "record was dropped.");
        }

        // The catalog anchor itself must be present (it is what pulls the
        // individual payload schemas into the reachable graph under
        // ignoreUnusedModels:true on the portal side).
        schemas.TryGetProperty("EventPayloadCatalog", out _).Should().BeTrue();
    }
}
