using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Diagnostic test that prints, per bot deck, which cards bind via the
/// OracleSpellBinder registry today vs. need bespoke implementation.
/// Run with `dotnet test --filter DeckBindingAuditTests -l "console;verbosity=detailed"`
/// to see the report. The assertion is that every deck card resolves to
/// a row in the embedded card pool (deck-spelling sanity check).
///
/// Pre-cards.db-deletion this hit the user's local SQLite DB; now it
/// runs against the bundled embedded seed and so always executes — no
/// filesystem skip path needed.
/// </summary>
public class DeckBindingAuditTests
{
    private readonly ITestOutputHelper _out;

    public DeckBindingAuditTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void Audit_PrintsBindingsPerDeck()
    {
        var repo = new EmbeddedCardRepository();
        var synth = new Player("Synth", 20);

        foreach (var archetype in BotDeckCatalog.Archetypes.OrderBy(a => a))
        {
            _out.WriteLine($"=== {archetype} ===");
            var unique = BotDeckCatalog.Get(archetype).Distinct(StringComparer.Ordinal).OrderBy(n => n);
            foreach (var name in unique)
            {
                var entity = repo.GetByName(name);
                if (entity is null)
                {
                    _out.WriteLine($"  MISSING-DB  {name}");
                    continue;
                }
                var impl = entity.IsImplemented ? "IMPL" : "stub";
                var tl = entity.TypeLine ?? "";
                if (tl.Contains("Instant", StringComparison.OrdinalIgnoreCase) ||
                    tl.Contains("Sorcery", StringComparison.OrdinalIgnoreCase))
                {
                    var ctx = new SpellBindContext(entity, synth, _ => _, null, null);
                    var template = OracleSpellBinder.Registry.OrderedTemplates
                        .FirstOrDefault(t => t.TryBind(ctx) is not null);
                    var tname = template?.Name ?? "—";
                    _out.WriteLine($"  {impl,-4}  bind={tname,-32}  {name}  [{tl}]");
                }
                else
                {
                    _out.WriteLine($"  {impl,-4}  non-spell                          {name}  [{tl}]");
                }
            }
            _out.WriteLine("");
        }

        var missing = BotDeckCatalog.Archetypes
            .SelectMany(a => BotDeckCatalog.Get(a).Distinct())
            .Where(n => repo.GetByName(n) is null)
            .Distinct()
            .ToList();
        missing.Should().BeEmpty(
            "every bot-deck card must resolve to a row in the embedded pool");
    }
}
