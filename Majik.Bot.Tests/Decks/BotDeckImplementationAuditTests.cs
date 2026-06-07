using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Static coverage audit for every card in every bot deck (mainboard +
/// sideboard). Builds each distinct card through the real
/// <see cref="ScryfallCardFactory"/> (full binder/factory chain, same as
/// production) and classifies it. The gate fails on drift versus
/// <see cref="KnownPartialImplementations"/>; the report prints a per-deck
/// breakdown.
///
/// Class D (silent-wrong-but-complete impls, e.g. a keyword that grants the
/// wrong subtype) is NOT covered here — those need per-keyword golden tests
/// (see <c>EarthbendActionTests</c> as the seed example).
/// </summary>
public class BotDeckImplementationAuditTests
{
    private readonly ITestOutputHelper _out;
    public BotDeckImplementationAuditTests(ITestOutputHelper output) => _out = output;

    // Built once for the whole class — the seed is ~22k rows.
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);
    private static readonly Player Dummy = new("Audit", 20);

    /// <summary>Class-B heuristic false positives: oracle text leads with
    /// When/Whenever/At, but the "trigger" is actually a keyword or replacement
    /// effect, not an <see cref="ITriggeredAbility"/>. Real gaps go in
    /// <see cref="KnownPartialImplementations"/>, NOT here. Seeded in Task 3.</summary>
    private static readonly HashSet<string> TriggerHeuristicAllowlist =
        new(StringComparer.Ordinal)
        {
            // Seeded in Task 3 from the first audit run.
        };

    /// <summary>Raw detection result, ignoring the registry.</summary>
    private enum RawSignal { None, Stub, MissingTrigger }

    /// <summary>Report-facing status (raw signal overlaid with the registry).</summary>
    private enum Status { Ok, Stub, Partial, MissingTrigger }

    private static IReadOnlyList<string> AllBotDeckCardNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            foreach (var n in BotDeckCatalog.Get(archetype)) names.Add(n);
            foreach (var n in BotDeckCatalog.GetSideboard(archetype)) names.Add(n);
        }
        return names.ToList();
    }

    private static bool OracleImpliesTrigger(string? oracle)
    {
        if (string.IsNullOrWhiteSpace(oracle)) return false;
        foreach (var raw in oracle.Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("When ", StringComparison.Ordinal)
                || line.StartsWith("Whenever ", StringComparison.Ordinal)
                || line.StartsWith("At ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsPermanent(ICard c)
        => c.HasType(CardType.Creature) || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment) || c.HasType(CardType.Planeswalker)
        || c.HasType(CardType.Land);

    /// <summary>Pure detection — does NOT consult the registry.</summary>
    private static RawSignal DetectRaw(string name)
    {
        var card = Factory.Create(name, Dummy);
        if (card.IsVanillaShell) return RawSignal.Stub;

        var entity = Repo.GetByName(name);
        if (entity != null
            && IsPermanent(card)
            && OracleImpliesTrigger(entity.OracleText)
            && !card.Abilities.OfType<ITriggeredAbility>().Any()
            && !TriggerHeuristicAllowlist.Contains(name))
            return RawSignal.MissingTrigger;

        return RawSignal.None;
    }

    /// <summary>Report status: registry overlay over the raw signal.</summary>
    private static Status ReportStatus(string name)
    {
        if (KnownPartialImplementations.TryGet(name, out var gap))
            return gap.Severity == CardGapSeverity.Stub ? Status.Stub : Status.Partial;

        return DetectRaw(name) switch
        {
            RawSignal.Stub => Status.Stub,
            RawSignal.MissingTrigger => Status.MissingTrigger,
            _ => Status.Ok,
        };
    }

    [Fact]
    public void PrintPerDeckHealth()
    {
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var names = BotDeckCatalog.Get(archetype)
                .Concat(BotDeckCatalog.GetSideboard(archetype))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var problems = names
                .Select(n => (Name: n, Status: ReportStatus(n)))
                .Where(x => x.Status != Status.Ok)
                .ToList();

            _out.WriteLine($"=== {BotDeckCatalog.DisplayName(archetype)} "
                + $"({problems.Count}/{names.Count} flagged) ===");
            foreach (var (n, status) in problems)
            {
                var reason = KnownPartialImplementations.TryGet(n, out var gap)
                    ? gap.Reason : "(detected — not yet registered)";
                _out.WriteLine($"  [{status}] {n} — {reason}");
            }
        }
    }
}
