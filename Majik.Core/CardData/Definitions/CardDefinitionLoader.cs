using System.Reflection;
using System.Text.Json;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Loads <see cref="CardDefinition"/> instances from JSON. Two sources:
/// <list type="bullet">
///   <item><see cref="FromJson"/> — parse a string in-process. Useful
///   for tests and for inline registrations where the JSON is short.</item>
///   <item><see cref="FromEmbeddedResource"/> — read a JSON file
///   bundled into the <c>Majik.Core</c> assembly under
///   <c>Majik.Core.CardData.Cards.&lt;slug&gt;.json</c>. The slug matches
///   the file name without extension; per-card factory wrappers (e.g.
///   <c>WastewoodVergeFactory</c>) call this with a constant slug.</item>
/// </list>
///
/// Both routes share a single <see cref="JsonSerializerOptions"/> tuned
/// for the schema's discriminated-union ability list.
/// </summary>
public static class CardDefinitionLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parse JSON text into a <see cref="CardDefinition"/>.
    /// Throws <see cref="JsonException"/> on malformed input.</summary>
    public static CardDefinition FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<CardDefinition>(json, JsonOpts)
            ?? throw new JsonException("Empty card definition.");
    }

    /// <summary>
    /// Load a card definition from an embedded JSON resource. The
    /// <paramref name="slug"/> maps to the resource
    /// <c>Majik.Core.CardData.Cards.&lt;slug&gt;.json</c>. The file must
    /// be marked <c>EmbeddedResource</c> in the project (so the assembly
    /// is self-contained — no filesystem access at runtime).
    /// </summary>
    public static CardDefinition FromEmbeddedResource(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var assembly = typeof(CardDefinitionLoader).Assembly;
        var resourceName = $"Majik.Core.CardData.Cards.{slug}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded card definition '{resourceName}' not found. " +
                "Confirm the JSON file lives at Majik.Core/CardData/Cards/ and is " +
                "marked <EmbeddedResource> in the csproj.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}
