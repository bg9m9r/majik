using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Majik.Server.Tests;

/// <summary>
/// Contract-drift gate. Majik.Server emits its OpenAPI document at
/// <c>/openapi/v1.json</c>; the portal commits that snapshot
/// (<c>majik.portal/openapi.json</c>) and regenerates its typed client from
/// it via <c>npm run openapi</c>. Nothing on the portal side verifies the
/// committed snapshot still matches what the server emits, so a server DTO /
/// event / endpoint change with a forgotten <c>npm run openapi</c> builds
/// green against a stale contract and only breaks at runtime.
///
/// This test pins the emitted contract to a snapshot checked into this repo
/// (<c>Majik.Server.Tests/Snapshots/openapi.v1.json</c>). Any change that
/// alters the contract fails CI here — at PR time, in one repo — until the
/// snapshot is regenerated. Regenerating the snapshot is the cue to also run
/// the portal's <c>npm run openapi</c> and ship the API change first under the
/// api-first deploy ordering. See <c>majik.core/CLAUDE.md</c>.
/// </summary>
public sealed class OpenApiContractDriftTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Resolved relative to the source file so the snapshot is read from the
    // repo working tree (not the copy under bin/), keeping "regenerate" a
    // single in-place file write.
    private static readonly string SnapshotPath = Path.Combine(
        SourceDirectory(), "Snapshots", "openapi.v1.json");

    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiContractDriftTests(WebApplicationFactory<Program> factory) =>
        // "Testing" env wires the test auth handler stand-ins; the OpenAPI
        // document itself is environment-independent.
        _factory = factory.WithWebHostBuilder(b => b.UseSetting(
            "environment", "Testing"));

    [Fact]
    public async Task EmittedOpenApiDocument_MatchesCommittedSnapshot()
    {
        var emitted = await FetchEmittedContractAsync();

        File.Exists(SnapshotPath).Should().BeTrue(
            $"the committed OpenAPI snapshot must exist at {SnapshotPath}. " +
            RegenInstructions());

        var committed = Normalize(JsonNode.Parse(
            await File.ReadAllTextAsync(SnapshotPath))!);

        emitted.Should().Be(
            committed,
            "the server's emitted OpenAPI contract drifted from the committed " +
            "snapshot. " + RegenInstructions());
    }

    /// <summary>
    /// GET the exact document the portal consumes, then canonicalize it.
    /// We hit <c>/openapi/v1.json</c> on the in-process test host rather than
    /// reading the build-time document so the gate covers the same bytes the
    /// portal's <c>openapi:fetch</c> curl would receive.
    /// </summary>
    private async Task<string> FetchEmittedContractAsync()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/openapi/v1.json");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return Normalize(JsonNode.Parse(json)!);
    }

    /// <summary>
    /// Deterministic serialization: recursively sort object keys and strip
    /// fields that vary by host/build but are not part of the type contract.
    /// </summary>
    private static string Normalize(JsonNode root)
    {
        // The `servers` block is the request host the document was generated
        // against (localhost under test, the Render URL in prod) — volatile,
        // not contractual.
        if (root is JsonObject obj)
        {
            obj.Remove("servers");

            // `info.version` is a release stamp, not part of the type surface.
            if (obj["info"] is JsonObject info)
            {
                info.Remove("version");
            }
        }

        var sorted = SortKeys(root);
        return sorted.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static JsonNode SortKeys(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                var result = new JsonObject();
                foreach (var key in o.Select(kvp => kvp.Key)
                             .OrderBy(k => k, StringComparer.Ordinal))
                {
                    var child = o[key];
                    result[key] = child is null
                        ? null
                        : SortKeys(child.DeepClone());
                }

                return result;
            }
            case JsonArray a:
            {
                var result = new JsonArray();
                foreach (var item in a)
                {
                    result.Add(item is null ? null : SortKeys(item.DeepClone()));
                }

                return result;
            }
            default:
                return node.DeepClone();
        }
    }

    private static string RegenInstructions() =>
        "OpenAPI contract changed — regenerate the snapshot, then run the " +
        "portal client regen:\n" +
        "  1. Update " + SnapshotPath + " with the emitted document.\n" +
        "  2. In majik.portal, run `npm run openapi` to refresh the committed " +
        "openapi.json and the generated typed client.\n" +
        "  3. Ship the API change first (api-first deploy ordering).\n" +
        "To capture the current document, run this test's serializer against " +
        "GET /openapi/v1.json (see test source) or `curl " +
        "${MAJIK_API}/openapi/v1.json`.";

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
