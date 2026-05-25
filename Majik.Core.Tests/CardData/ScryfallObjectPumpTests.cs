using System.Text;
using System.Text.Json;
using FluentAssertions;
using Majik.Console.Commands;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Buffer-boundary regression coverage for
/// <see cref="ExportModernCardsCommand"/>'s nested
/// <c>ScryfallObjectPump</c>. The bug being pinned here: when a card's
/// JSON spans the internal buffer's fill boundary, <c>GrowOrAdvance</c>
/// previously dropped bytes before the StartObject offset — including
/// the inter-element comma the saved <see cref="JsonReaderState"/>
/// expected next — surfacing on the very next read as
/// <c>"'{' is invalid after a value"</c>.
///
/// These tests run with an artificially small initial buffer (a few
/// dozen bytes) over synthetic multi-card arrays so the boundary is
/// crossed deterministically without needing the ~500 MB Scryfall
/// bulk file on disk.
/// </summary>
public class ScryfallObjectPumpTests
{
    [Fact]
    public void StreamsAllCards_WhenObjectsFitWithinInitialBuffer()
    {
        var json = BuildArray(new[]
        {
            CardObject("Alpha", padding: 0),
            CardObject("Beta",  padding: 0),
            CardObject("Gamma", padding: 0),
        });

        var cards = Run(json, initialBufferSize: 1024);

        NamesOf(cards).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void StreamsAllCards_WhenSingleCardSpansBufferFillBoundary()
    {
        // Buffer is 64 B. Each card's JSON ~120 B once padded; the first
        // card alone exceeds the buffer, forcing the grow + boundary
        // preservation path. Pre-fix this raised
        // "'{' is invalid after a value" on card #2.
        var json = BuildArray(new[]
        {
            CardObject("Alpha", padding: 200),
            CardObject("Beta",  padding: 200),
            CardObject("Gamma", padding: 200),
        });

        var cards = Run(json, initialBufferSize: 64);

        NamesOf(cards).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void StreamsAllCards_WhenManyCardsForceRepeatedBufferRefills()
    {
        // Every card crosses a refill boundary because the buffer is
        // intentionally narrower than a single card. Stress the case
        // where the inter-element comma is on one side of the boundary
        // and the next '{' lands at the start of a freshly-compacted
        // buffer.
        var bodies = new List<string>();
        for (int i = 0; i < 25; i++)
            bodies.Add(CardObject($"Card{i:D2}", padding: 80));

        var json = BuildArray(bodies);

        var cards = Run(json, initialBufferSize: 96);

        NamesOf(cards).Should().HaveCount(25);
        NamesOf(cards)[0].Should().Be("Card00");
        NamesOf(cards)[^1].Should().Be("Card24");
    }

    [Fact]
    public void StreamsAllCards_WhenWhitespaceBeforeStartObjectStraddlesBoundary()
    {
        // Pad the gap BETWEEN cards (newlines/spaces) so the chunk that
        // refills after a successful card lands on whitespace rather
        // than directly on '{'. Verifies the saved reader state still
        // tracks the inter-element separator across the refill.
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(CardObject("Alpha", padding: 0));
        sb.Append(',');
        sb.Append('\n', 120); // bigger than the 64 B buffer
        sb.Append(CardObject("Beta", padding: 0));
        sb.Append(',');
        sb.Append(' ', 120);
        sb.Append(CardObject("Gamma", padding: 0));
        sb.Append(']');

        var cards = Run(sb.ToString(), initialBufferSize: 64);

        NamesOf(cards).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void StreamsZeroCards_WhenInputIsEmptyArray()
    {
        var cards = Run("[]", initialBufferSize: 64);
        cards.Should().BeEmpty();
    }

    [Fact]
    public void StreamsZeroCards_WhenEmptyArrayHasOnlyWhitespace()
    {
        var cards = Run("[\n   \n]", initialBufferSize: 64);
        cards.Should().BeEmpty();
    }

    [Fact]
    public void Throws_WhenRootIsNotAJsonArray()
    {
        Action act = () => Run("{\"not\":\"an array\"}", initialBufferSize: 64);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a JSON array*");
    }

    [Fact]
    public void Throws_WhenStreamEndsMidCardObject()
    {
        // Open the array, start a card, never close it. The pump either
        // surfaces its own "mid-card object" message or lets the
        // underlying Utf8JsonReader throw on the unclosed object — both
        // are acceptable as long as we fail loudly rather than silently
        // dropping a partial card.
        var truncated = "[{\"name\":\"Alpha\",\"legalities\":{\"modern\":\"legal\"}";

        Action act = () => Run(truncated, initialBufferSize: 32);
        act.Should().Throw<Exception>()
            .Where(e => e is InvalidOperationException
                     || e is JsonException);
    }

    [Fact]
    public void CardObjectsRoundTrip_ParseableAsJson()
    {
        var json = BuildArray(new[]
        {
            CardObject("Alpha", padding: 300),
            CardObject("Beta",  padding: 0),
        });

        var cards = Run(json, initialBufferSize: 48);

        foreach (var raw in cards)
        {
            using var doc = JsonDocument.Parse(raw);
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            doc.RootElement.GetProperty("name").GetString()
                .Should().NotBeNullOrEmpty();
            doc.RootElement.GetProperty("legalities")
                .GetProperty("modern").GetString()
                .Should().Be("legal");
        }
    }

    // ----- helpers -----

    private static List<string> Run(string json, int initialBufferSize)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ExportModernCardsCommand.StreamCardJsonForTesting(
            ms, initialBufferSize);
    }

    private static List<string> NamesOf(IEnumerable<string> rawCards)
    {
        var names = new List<string>();
        foreach (var raw in rawCards)
        {
            using var doc = JsonDocument.Parse(raw);
            names.Add(doc.RootElement.GetProperty("name").GetString()!);
        }
        return names;
    }

    private static string CardObject(string name, int padding)
    {
        // `padding` bytes of filler oracle_text so the card straddles
        // an arbitrarily small buffer. Plain ASCII keeps the byte count
        // predictable.
        var pad = padding > 0 ? new string('x', padding) : "";
        return $$"""
            {"id":"id-{{name}}","name":"{{name}}","oracle_text":"{{pad}}","legalities":{"modern":"legal"},"released_at":"2024-01-01"}
            """;
    }

    private static string BuildArray(IEnumerable<string> cardBodies)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        bool first = true;
        foreach (var body in cardBodies)
        {
            if (!first) sb.Append(',');
            sb.Append('\n');
            sb.Append(body);
            first = false;
        }
        sb.Append("\n]");
        return sb.ToString();
    }
}
