using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Pool-wide faithful coverage REPORT for every IMPLEMENTED card name (the
/// <see cref="ImplementedCardNames.All"/> registry — every card with a
/// <c>[CardName]</c> factory / generated JSON arm / inline fallback). It is the
/// sibling of <see cref="BotDeckImplementationAuditTests"/>, which audits only
/// the 24 bot decks and is the CI GATE. THIS class is a NON-FAILING report: it
/// surfaces the true coverage backlog across the whole implemented pool, which
/// legitimately contains gaps, so gating on it would be perpetually red.
///
/// <para>It reuses the exact same detection the bot-deck gate uses — build each
/// card through the real <see cref="GameFacade"/> binder/factory chain, then
/// read the engine-stamped <see cref="ICard.IsVanillaShell"/> + inspect bound
/// <see cref="ITriggeredAbility"/>s — but builds efficiently: every card name is
/// materialized into ONE facade's library (a synthetic mega-deck) so the
/// expensive facade construction is amortized over thousands of cards instead
/// of per-card.</para>
///
/// <para>Classification per card:
/// OK / Stub (<see cref="ICard.IsVanillaShell"/>) / MissingTrigger (permanent,
/// oracle leads with When/Whenever/At, no <see cref="ITriggeredAbility"/> bound,
/// not in the trigger-heuristic allowlist) / Partial (recorded in
/// <see cref="KnownPartialImplementations"/>). The Stub + MissingTrigger lists
/// are the backlog.</para>
/// </summary>
public class PoolWideImplementationAuditTests
{
    private readonly ITestOutputHelper _out;
    public PoolWideImplementationAuditTests(ITestOutputHelper output) => _out = output;

    // Built once for the whole class — the seed is ~22k rows.
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>Class-B heuristic false positives shared with the bot-deck
    /// audit: oracle leads with When/Whenever/At but the "trigger" is actually
    /// a keyword / replacement / Ward phrasing, not an
    /// <see cref="ITriggeredAbility"/>. Real gaps go in
    /// <see cref="KnownPartialImplementations"/>, NOT here.</summary>
    private static readonly HashSet<string> TriggerHeuristicAllowlist =
        new(StringComparer.Ordinal)
        {
            // WARD templated wording (CR 702.21) — see BotDeckImplementationAuditTests.
            "Reality Smasher",
        };

    private enum Status { Ok, Stub, Partial, MissingTrigger, Skipped }

    /// <summary>
    /// Build every implemented card name into ONE facade's library through the
    /// real <see cref="GameFacade.Create"/> binder/factory chain (the exact
    /// production path), then return the fully-bound live <see cref="ICard"/>
    /// per name. Names with no row in the embedded Modern seed (inline
    /// test-only vanilla creatures, or any non-Modern factory) are absent from
    /// the result — <see cref="Classify"/> reports them as Skipped, never OK.
    /// </summary>
    private static IReadOnlyDictionary<string, ICard> BuildAllLiveCards()
    {
        var shells = new List<ICard>(ImplementedCardNames.All.Count);
        foreach (var name in ImplementedCardNames.All.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Not in the embedded Modern seed — inline test-only vanillas
            // (Grizzly Bears / Runeclaw Bear / Hill Giant) or a non-Modern
            // factory. Cannot be faithfully materialized; left out of the
            // library and surfaced as Skipped (no silent truncation).
            var entity = Repo.GetByName(name);
            if (entity != null) shells.Add(MaterializeReal(entity));
        }

        // One facade, one giant library: amortizes facade construction over the
        // whole pool. GameFacade.Create enforces no deck-size limit, so a
        // multi-thousand-card library is fine for a static (no-game-start) audit.
        var facade = GameFacade.Create(
            aliceName: "pool-audit-A",
            bobName: "pool-audit-B",
            aliceDeck: shells,
            bobDeck: Array.Empty<ICard>(),
            cardRepo: Repo);

        var byName = new Dictionary<string, ICard>(StringComparer.Ordinal);
        foreach (var card in facade.Alice.Zones.GetZone(ZoneType.Library).GetCards())
        {
            if (!byName.ContainsKey(card.Name)) byName[card.Name] = card;
        }

        return byName;
    }

    private static readonly IReadOnlyDictionary<string, ICard> LiveCards = BuildAllLiveCards();

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

    /// <summary>Report status: registry overlay over the live-built detection.</summary>
    private static Status Classify(string name)
    {
        if (KnownPartialImplementations.TryGet(name, out var gap))
            return gap.Severity == CardGapSeverity.Stub ? Status.Stub : Status.Partial;

        if (!LiveCards.TryGetValue(name, out var card))
            return Status.Skipped; // no row in the Modern seed — reported separately

        // GameFacade.BuildDeckCard stamps IsVanillaShell only on the non-routed
        // binder-chain path; routed (factory-backed) cards are implemented by
        // definition. Same semantics as the bot-deck gate.
        if (card.IsVanillaShell)
            return Status.Stub;

        var entity = Repo.GetByName(name);
        if (entity != null
            && IsPermanent(card)
            && OracleImpliesTrigger(entity.OracleText)
            && !card.Abilities.OfType<ITriggeredAbility>().Any()
            && !TriggerHeuristicAllowlist.Contains(name))
            return Status.MissingTrigger;

        return Status.Ok;
    }

    [Fact]
    public void PrintPoolWideHealth()
    {
        var names = ImplementedCardNames.All
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var byStatus = new Dictionary<Status, List<string>>
        {
            [Status.Ok] = new(),
            [Status.Stub] = new(),
            [Status.Partial] = new(),
            [Status.MissingTrigger] = new(),
            [Status.Skipped] = new(),
        };

        foreach (var name in names)
            byStatus[Classify(name)].Add(name);

        _out.WriteLine("===== POOL-WIDE IMPLEMENTATION HEALTH (non-failing report) =====");
        _out.WriteLine($"Total implemented card names : {names.Count}");
        _out.WriteLine($"  OK             : {byStatus[Status.Ok].Count}");
        _out.WriteLine($"  Stub           : {byStatus[Status.Stub].Count}");
        _out.WriteLine($"  MissingTrigger : {byStatus[Status.MissingTrigger].Count}");
        _out.WriteLine($"  Partial        : {byStatus[Status.Partial].Count}");
        _out.WriteLine($"  Skipped        : {byStatus[Status.Skipped].Count} "
            + "(no row in the Modern seed — non-Modern factory / inline test "
            + "vanilla; not classifiable, NOT silently truncated)");
        // Sanity: every skipped-by-classifier name is one we couldn't materialize.
        if (byStatus[Status.Skipped].Count > 0)
        {
            foreach (var n in byStatus[Status.Skipped].OrderBy(x => x, StringComparer.Ordinal))
                _out.WriteLine($"    (skipped) {n}");
        }

        _out.WriteLine("");
        _out.WriteLine($"----- BACKLOG: Stub ({byStatus[Status.Stub].Count}) -----");
        foreach (var n in byStatus[Status.Stub])
        {
            var reason = KnownPartialImplementations.TryGet(n, out var gap)
                ? gap.Reason : "(detected vanilla shell — does nothing in real play)";
            _out.WriteLine($"  [Stub] {n} — {reason}");
        }

        _out.WriteLine("");
        _out.WriteLine($"----- BACKLOG: MissingTrigger ({byStatus[Status.MissingTrigger].Count}) -----");
        foreach (var n in byStatus[Status.MissingTrigger])
            _out.WriteLine($"  [MissingTrigger] {n} — oracle implies a trigger but none is bound");
    }

    // ---------------------------------------------------------------------
    // Local typed-shell materialization — same logic as
    // BotDeckImplementationAuditTests.MaterializeReal (DeckLoader.LoadReal
    // lives in the Integration project, not referenced here). Takes a resolved
    // CardEntity so the caller controls seed-miss handling.
    // ---------------------------------------------------------------------

    private static ICard MaterializeReal(CardEntity entity)
    {
        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var manaCost = entity.ManaCost ?? "";

        ICard card = PickPrimaryType(parsed.Types) switch
        {
            CardType.Creature => new Creature(
                entity.Name, manaCost,
                ParseStat(entity.Power), ParseStat(entity.Toughness),
                parsed.Supertypes, parsed.Subtypes),
            CardType.Land => new Land(entity.Name, parsed.Supertypes, parsed.Subtypes),
            CardType.Instant => new Instant(entity.Name, manaCost),
            CardType.Sorcery => new Sorcery(entity.Name, manaCost),
            CardType.Enchantment => new Enchantment(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Artifact => new Artifact(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Planeswalker => new Planeswalker(
                entity.Name, manaCost,
                startingLoyalty: entity.Loyalty ?? 0,
                parsed.Supertypes, parsed.Subtypes),
            _ => new Card(entity.Name, manaCost, parsed.Types, parsed.Supertypes, parsed.Subtypes),
        };

        // CR 202.2c — stamp the printed color indicator so the shell mirrors the
        // server loader before GameFacade rebinds.
        if (card is Card concrete)
        {
            var colors = CardColors.ParseScryfallColors(entity.Colors);
            if (colors.Count > 0) concrete.SetColorIndicator(colors);
        }

        return card;
    }

    private static CardType? PickPrimaryType(IReadOnlyList<CardType> types)
    {
        foreach (var p in new[]
        {
            CardType.Creature, CardType.Land, CardType.Instant, CardType.Sorcery,
            CardType.Enchantment, CardType.Artifact, CardType.Planeswalker,
        })
        {
            if (types.Contains(p)) return p;
        }
        return null;
    }

    private static int ParseStat(string? s)
        => int.TryParse(s, out var v) ? v : 0;
}
