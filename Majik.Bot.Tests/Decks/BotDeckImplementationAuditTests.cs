using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// Faithful static coverage audit for every card in every bot deck (mainboard +
/// sideboard). Builds each archetype through the REAL
/// <see cref="GameFacade.Create"/> + <see cref="GameFacade.PopulateSideboard"/>
/// path — the exact binder/factory chain production uses, with live services
/// (TriggerManager / ZoneService / EventBus / ContinuousEffects) and the
/// named-factory routing for non-land cards — then reads the now-authoritative
/// <see cref="ICard.IsVanillaShell"/> flag that <c>GameFacade.BuildDeckCard</c>
/// stamps via the shared <see cref="VanillaShellClassifier"/>.
///
/// <para>This supersedes the prior unit-level audit (which built cards via the
/// bare <see cref="ScryfallCardFactory"/> and so false-flagged fetchlands /
/// horizon lands whose land binders run only on the live path). Because lands
/// ARE bound by <c>OracleLandActivatedAbilityBinder</c> here, fetchlands etc.
/// are no longer detected as shells.</para>
///
/// <para>This audit lives in <c>Majik.Bot.Tests</c> (the CI-gated bot suite) so
/// the gate actually blocks PRs — <c>Majik.Bot.Tests.Integration</c> is omitted
/// from CI as supplementary/flaky. Because <c>DeckLoader.LoadReal</c> lives in
/// the Integration project, the typed-shell materialization is replicated here
/// in <see cref="MaterializeReal"/> (it uses only <c>Majik.Core</c> types).</para>
///
/// <para>The gate (<see cref="BotDeckCards_HaveNoUnregisteredGaps"/>) fails on
/// drift versus <see cref="KnownPartialImplementations"/>; the report
/// (<see cref="PrintPerDeckHealth"/>) prints a per-deck breakdown.</para>
///
/// <para>Class D (silent-wrong-but-complete impls, e.g. a keyword that grants
/// the wrong subtype) is NOT covered here — those need per-keyword golden tests
/// (see <c>EarthbendActionTests</c> as the seed example).</para>
/// </summary>
public class BotDeckImplementationAuditTests
{
    private readonly ITestOutputHelper _out;
    public BotDeckImplementationAuditTests(ITestOutputHelper output) => _out = output;

    // Built once for the whole class — the seed is ~22k rows.
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>Class-B heuristic false positives: oracle text leads with
    /// When/Whenever/At, but the "trigger" is actually a keyword / replacement /
    /// Ward phrasing, not an <see cref="ITriggeredAbility"/>. Real gaps go in
    /// <see cref="KnownPartialImplementations"/>, NOT here.</summary>
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

    /// <summary>Stub-heuristic false positives: <see cref="ICard.IsVanillaShell"/>
    /// is true (the binder chain attached no <c>card.Abilities</c>) but the
    /// card's printed behaviour DOES run — it lives in an OFF-CARD effect the
    /// classifier cannot see (a continuous/replacement effect on a live per-game
    /// service, not a <c>card.Ability</c>). Verified working via the GameFacade
    /// prod path in <c>Majik.Core.Api.Tests.OffCardEffectLandBinderTests</c>.
    /// Each entry names WHY. Real gaps go in
    /// <see cref="KnownPartialImplementations"/>, NOT here. (None of these are
    /// in the bot decks today, so the gate is unaffected — the allowlist is
    /// kept in lock-step with the pool-wide audit's copy so the shared
    /// detection logic stays consistent.)</summary>
    private static readonly HashSet<string> StubHeuristicAllowlist =
        new(StringComparer.Ordinal)
        {
            // CR 305.7 — additive land-retype static, bound by
            // AdditiveLandSubtypeBinder as an off-card continuous effect on the
            // game's ContinuousEffectsService (not a card.Ability).
            "Urborg, Tomb of Yawgmoth",
            "Yavimaya, Cradle of Growth",
            // CR 706.2 — "enter tapped as a copy of any land", bound by
            // EntersAsCopyBinder as an off-card replacement effect on the
            // game's ReplacementBus (not a card.Ability).
            "Vesuva",
        };

    /// <summary>Detection result for one distinct card name.</summary>
    private enum RawSignal { None, Stub, MissingTrigger }

    /// <summary>Report-facing status (raw signal overlaid with the registry).</summary>
    private enum Status { Ok, Stub, Partial, MissingTrigger }

    /// <summary>
    /// Build EVERY distinct bot-deck card (mainboard + sideboard, all
    /// archetypes) through the real <see cref="GameFacade"/> path ONCE and
    /// return the fully-bound live <see cref="ICard"/> per name. Building each
    /// archetype's deck + sideboard with a fresh facade mirrors production;
    /// the first live instance seen for a name wins (identical across decks).
    /// </summary>
    private static IReadOnlyDictionary<string, ICard> BuildAllLiveCards()
    {
        var byName = new Dictionary<string, ICard>(StringComparer.Ordinal);

        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var facade = GameFacade.Create(
                aliceName: $"{archetype}-A",
                bobName: $"{archetype}-B",
                aliceDeck: LoadReal(archetype),
                bobDeck: System.Array.Empty<ICard>(),
                cardRepo: Repo);

            facade.PopulateSideboard(facade.Alice, LoadRealSideboard(archetype));

            var libraryCards = facade.Alice.Zones.GetZone(ZoneType.Library).GetCards();
            var sideboardCards = facade.Alice.Zones.Sideboard.GetCards();

            foreach (var card in libraryCards.Concat(sideboardCards))
            {
                if (!byName.ContainsKey(card.Name)) byName[card.Name] = card;
            }
        }

        return byName;
    }

    // Built once — the live-engine build of every archetype is expensive.
    private static readonly IReadOnlyDictionary<string, ICard> LiveCards = BuildAllLiveCards();

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

    /// <summary>Pure detection on the live-built card — does NOT consult the registry.</summary>
    private static RawSignal DetectRaw(string name)
    {
        if (!LiveCards.TryGetValue(name, out var card))
            return RawSignal.None; // not a bot-deck card (shouldn't happen for callers)

        // GameFacade.BuildDeckCard now stamps IsVanillaShell ONLY on the
        // non-routed binder-chain path (lands + factory-less cards). Routed
        // (factory-backed) cards are implemented by definition — even when
        // their behaviour lives in off-card continuous / replacement / CDA
        // effects the classifier can't see — so they are never stamped and no
        // allowlist is needed here.
        //
        // LANDS are the exception: they are never routed through a [CardName]
        // factory (the instance-swap is gated on !HasType(Land)), so a land
        // whose ONLY behaviour is an off-card continuous/replacement effect
        // (Urborg additive static, Vesuva enters-as-copy replacement) reaches
        // the binder-chain path with zero card.Abilities and IS stamped a
        // vanilla shell — a false positive. The StubHeuristicAllowlist clears
        // these provably-working off-card-effect lands.
        if (card.IsVanillaShell && !StubHeuristicAllowlist.Contains(name))
            return RawSignal.Stub;

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

            // A registry Stub entry that is a bot-deck card but no longer a
            // shell → fail (clean it up).
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

    // ---------------------------------------------------------------------
    // Local typed-shell materialization.
    //
    // DeckLoader.LoadReal / LoadRealSideboard live in the Integration project
    // (not referenced from this CI-gated project), so the shell-build logic is
    // replicated here verbatim. It uses only Majik.Core types — abilities are
    // NOT bound here; they are bound when the shells run through
    // GameFacade.Create with a cardRepo (the same binder/factory chain
    // production uses).
    // ---------------------------------------------------------------------

    private static IReadOnlyList<ICard> LoadReal(string archetype)
        => BotDeckCatalog.Get(archetype).Select(MaterializeReal).ToList();

    private static IReadOnlyList<ICard> LoadRealSideboard(string archetype)
        => BotDeckCatalog.GetSideboard(archetype).Select(MaterializeReal).ToList();

    private static ICard MaterializeReal(string name)
    {
        var entity = Repo.GetByName(name)
            ?? throw new InvalidOperationException(
                $"bot-deck card not in embedded seed: '{name}'");

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

        // CR 202.2c — stamp the printed color indicator (Dryad Arbor et al.)
        // so the shell mirrors the server loader before GameFacade rebinds.
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
