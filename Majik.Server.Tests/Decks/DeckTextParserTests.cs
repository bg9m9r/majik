using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Xunit;

namespace Majik.Server.Tests.Decks;

public class DeckTextParserTests
{
    private sealed class FakeCardRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _cards = new(StringComparer.OrdinalIgnoreCase);

        public FakeCardRepo(params (string Name, bool Implemented)[] known)
        {
            foreach (var (name, impl) in known)
                _cards[name] = new CardEntity { Name = name, IsImplemented = impl };
        }

        public CardEntity? GetByName(string name) =>
            _cards.TryGetValue(name, out var c) ? c : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => throw new NotImplementedException();

        public void SetImplemented(string name, bool value) => throw new NotImplementedException();
    }

    private static DeckTextParser ParserWith(params (string Name, bool Implemented)[] cards) =>
        new DeckTextParser(new FakeCardRepo(cards));

    [Fact]
    public async Task Parses_arena_format_with_deck_and_sideboard_headers()
    {
        var parser = ParserWith(("Lightning Bolt", true), ("Mountain", true), ("Searing Blaze", true));
        var text = """
            Deck
            4 Lightning Bolt
            20 Mountain

            Sideboard
            3 Searing Blaze
            """;
        var r = await parser.ParseAsync(text, CancellationToken.None);

        r.Mainboard.Should().BeEquivalentTo(new[]
        {
            new DeckCardEntry { Name = "Lightning Bolt", Count = 4 },
            new DeckCardEntry { Name = "Mountain", Count = 20 },
        });
        r.Sideboard.Should().BeEquivalentTo(new[]
        {
            new DeckCardEntry { Name = "Searing Blaze", Count = 3 },
        });
        r.Unknown.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_blank_line_split_when_no_headers()
    {
        var parser = ParserWith(("Forest", true), ("Grizzly Bears", true));
        var text = "60 Forest\n\n15 Grizzly Bears";
        var r = await parser.ParseAsync(text, CancellationToken.None);

        r.Mainboard.Single().Should().BeEquivalentTo(new DeckCardEntry { Name = "Forest", Count = 60 });
        r.Sideboard.Single().Should().BeEquivalentTo(new DeckCardEntry { Name = "Grizzly Bears", Count = 15 });
    }

    [Fact]
    public async Task All_mainboard_when_no_headers_and_no_blank()
    {
        var parser = ParserWith(("Forest", true), ("Grizzly Bears", true));
        var text = "60 Forest\n4 Grizzly Bears";
        var r = await parser.ParseAsync(text, CancellationToken.None);

        r.Mainboard.Should().HaveCount(2);
        r.Sideboard.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_x_form_count()
    {
        var parser = ParserWith(("Lightning Bolt", true));
        var text = "4x Lightning Bolt";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Single().Count.Should().Be(4);
    }

    [Fact]
    public async Task Strips_set_code_suffix()
    {
        var parser = ParserWith(("Lightning Bolt", true));
        var text = "4 Lightning Bolt (LEA)";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Single().Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public async Task Strips_set_code_and_collector_number()
    {
        var parser = ParserWith(("Lightning Bolt", true));
        var text = "4 Lightning Bolt (LEA) 123";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Single().Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public async Task Skips_comment_lines()
    {
        var parser = ParserWith(("Forest", true));
        var text = "// notes\n# also notes\n60 Forest";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Should().HaveCount(1);
    }

    [Fact]
    public async Task Merges_duplicate_entries_in_same_zone()
    {
        var parser = ParserWith(("Lightning Bolt", true));
        var text = "Deck\n3 Lightning Bolt\n1 Lightning Bolt";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Single().Should().BeEquivalentTo(new DeckCardEntry { Name = "Lightning Bolt", Count = 4 });
    }

    [Fact]
    public async Task Unknown_cards_go_to_unknown_list_not_entries()
    {
        var parser = ParserWith(("Forest", true));
        var text = "Deck\n60 Forest\n4 NotARealCard";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Mainboard.Single().Name.Should().Be("Forest");
        r.Unknown.Should().Contain("NotARealCard");
    }

    [Fact]
    public async Task Not_implemented_card_generates_warning()
    {
        var parser = ParserWith(("Black Lotus", false));
        var text = "Deck\n1 Black Lotus";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Warnings.Should().Contain(w => w.Contains("not implemented") && w.Contains("Black Lotus"));
    }

    [Fact]
    public async Task Malformed_line_generates_warning_others_still_parse()
    {
        var parser = ParserWith(("Forest", true));
        var text = "Deck\nthis is not a card line\n60 Forest";
        var r = await parser.ParseAsync(text, CancellationToken.None);
        r.Warnings.Should().Contain(w => w.Contains("malformed"));
        r.Mainboard.Should().HaveCount(1);
    }

    [Fact]
    public async Task Empty_text_returns_empty_result()
    {
        var parser = ParserWith();
        var r = await parser.ParseAsync("   \n  \n", CancellationToken.None);
        r.Mainboard.Should().BeEmpty();
        r.Sideboard.Should().BeEmpty();
        r.Unknown.Should().BeEmpty();
    }
}
