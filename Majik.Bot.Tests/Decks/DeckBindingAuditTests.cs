using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Diagnostic test that prints, per bot deck, which cards bind via the
/// OracleSpellBinder registry today vs. need bespoke implementation.
/// Run with `dotnet test --filter DeckBindingAuditTests -l "console;verbosity=detailed"`
/// to see the report. The assertion is just that every deck card resolves
/// to a DB row (deck-spelling sanity check) — runs against the user's local
/// cards.db so it's skipped if that file is absent.
/// </summary>
public class DeckBindingAuditTests
{
    private readonly ITestOutputHelper _out;

    public DeckBindingAuditTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void Audit_PrintsBindingsPerDeck()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Majik", "cards.db");
        if (!File.Exists(dbPath))
        {
            // Linux fallback — match CardDbContext default location.
            var xdg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "Majik", "cards.db");
            if (!File.Exists(xdg))
            {
                _out.WriteLine($"cards.db not found at {dbPath} or {xdg} — skipping audit");
                return;
            }
        }

        // File-existence alone isn't enough: other tests / the test host can
        // leave an empty SQLite file at this path with no Cards table. Skip
        // when the schema isn't populated.
        if (!HasCardsTable(dbPath))
        {
            _out.WriteLine($"cards.db at {dbPath} has no Cards table — skipping audit");
            return;
        }

        using var db = new CardDbContext();
        var repo = new DbCardRepository(db);
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
        missing.Should().BeEmpty("every bot-deck card must resolve to a DB row");
    }

    private static bool HasCardsTable(string dbPath)
    {
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Cards'";
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch
        {
            return false;
        }
    }
}
