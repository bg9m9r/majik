using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
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
            // The "Whenever this creature becomes the target of a spell an
            // opponent controls, counter that spell unless its controller
            // discards a card" line is the templated wording of the WARD
            // keyword (CR 702.21), implemented by RealitySmasherFactory as a
            // KeywordAbility("Ward") + a bound WardEffect — not a separate
            // unimplemented triggered ability. Heuristic false positive.
            "Reality Smasher",
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

    /// <summary>
    /// Materialize a card the way the LIVE engine does. Production
    /// (<c>GameFacade.BuildDeckCard</c>, <c>RouteThroughNamedFactories</c>
    /// default true) routes NON-LAND cards that have a real <c>[CardName]</c>
    /// factory through <see cref="NamedCardFactory"/> — so a card whose bespoke
    /// abilities live only in its factory (e.g. Emry's ETB mill) is fully live
    /// in a real match even though the bare binder chain alone produces a shell.
    /// Lands are NEVER routed (their factories omit fetch/enters-tapped and
    /// defer to the binder chain), so they build via the binder chain here too.
    /// Detecting against the bare <c>ScryfallCardFactory.Create</c> would
    /// mis-flag ~130 working cards as Stubs; mirroring the routing keeps the
    /// baseline truthful — only cards that genuinely do nothing in play remain.
    /// </summary>
    private static ICard BuildAsLiveEngine(string name)
    {
        var shell = Factory.Create(name, Dummy);
        if (!shell.HasType(CardType.Land) && ImplementedCardNames.HasRealFactory(name))
            return NamedCardFactory.Create(name, Dummy);
        return shell;
    }

    /// <summary>Pure detection — does NOT consult the registry.</summary>
    private static RawSignal DetectRaw(string name)
    {
        var card = BuildAsLiveEngine(name);
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

    [Fact]
    public void BotDeckCards_HaveNoUnregisteredGaps()
    {
        var botDeckNames = AllBotDeckCardNames();
        var newGaps = new List<string>();
        var stale = new List<string>();

        foreach (var name in botDeckNames)
        {
            var raw = DetectRaw(name);
            var known = KnownPartialImplementations.TryGet(name, out var gap);

            // A detected gap that nobody recorded → fail (implement it, or
            // register it with a reason if the gap is intentional).
            if (raw != RawSignal.None && !known)
            {
                newGaps.Add(raw == RawSignal.Stub
                    ? $"{name}: does nothing (vanilla shell) — implement, or register as Stub"
                    : $"{name}: oracle implies a trigger but none is bound — implement, "
                      + "register as a gap, or allowlist the heuristic false positive");
            }

            // A registry Stub entry that is no longer a shell → fail (clean it up).
            if (known && gap.Severity == CardGapSeverity.Stub && raw != RawSignal.Stub)
            {
                stale.Add($"{name}: registered as Stub but is no longer a vanilla shell "
                    + "— remove or downgrade the registry entry");
            }
        }

        var failures = newGaps.Concat(stale).ToList();
        failures.Should().BeEmpty(
            "bot-deck cards must be implemented or have their gap recorded in "
            + "KnownPartialImplementations / the trigger-heuristic allowlist. "
            + "Run PrintPerDeckHealth for the full picture.\n"
            + string.Join("\n", failures));
    }
}
