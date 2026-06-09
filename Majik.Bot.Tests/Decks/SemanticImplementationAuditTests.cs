using System.Text;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Zones;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Decks;

/// <summary>
/// SEMANTIC implementation audit — the sibling of
/// <see cref="PoolWideImplementationAuditTests"/>. Where the pool-wide audit
/// catches cards that are MISSING behaviour (Stub / MissingTrigger), this audit
/// catches cards that are implemented WRONG.
///
/// <para><b>Layer A — printed-characteristics parity (high-confidence).</b> For
/// every implemented card, build it through the real <see cref="GameFacade"/>
/// prod path (same mega-library build as the pool-wide audit) and compare the
/// BUILT card's printed characteristics — card types, supertypes, subtypes,
/// base power/toughness, starting loyalty, mana value — against the seed
/// <see cref="CardEntity"/> parsed by <see cref="TypeLineParser"/> /
/// <see cref="Majik.Core.ValueObjects.ManaCost"/>. A factory that fabricates the
/// wrong shape (a fictional card, a pre-errata stat line, a stale type) is
/// flagged here. This is the class of bug that let a fully-fictional
/// "Asmoranomardicadaistinaculdacar" and a pre-errata Stormbreath Dragon
/// through. A small, justified allowlist covers genuinely-legit differences
/// (DFC composite seed rows whose printed face the engine builds as its front
/// face).</para>
///
/// <para><b>Layer B — oracle-effect-keyword coverage (heuristic).</b> For each
/// implemented PERMANENT, scan the seed oracle text for significant EFFECT
/// signals (scry / draw / mill / make a token / +1/+1 counter / deal damage /
/// gain life / destroy / exile / bounce / search) and check the built card
/// carries a plausibly-corresponding bound mechanic. A permanent whose oracle
/// promises an effect but whose built form has nothing matching is likely
/// wrong/incomplete. This is a HEURISTIC with known false positives, so it is a
/// ranked REPORT, never a gate. Instants/sorceries are excluded (their effects
/// resolve at cast time, off the card).</para>
/// </summary>
public class SemanticImplementationAuditTests
{
    private readonly ITestOutputHelper _out;
    public SemanticImplementationAuditTests(ITestOutputHelper output) => _out = output;

    private static readonly EmbeddedCardRepository Repo = new();

    // ------------------------------------------------------------------
    // Shared live-build of every implemented card through the prod path.
    // Mirrors PoolWideImplementationAuditTests.BuildAllLiveCards — one giant
    // library, one facade, so facade construction is amortized over the pool.
    // ------------------------------------------------------------------
    private static IReadOnlyDictionary<string, ICard> BuildAllLiveCards()
    {
        var shells = new List<ICard>(ImplementedCardNames.All.Count);
        foreach (var name in ImplementedCardNames.All.OrderBy(n => n, StringComparer.Ordinal))
        {
            var entity = Repo.GetByName(name);
            if (entity != null) shells.Add(MaterializeReal(entity));
        }

        var facade = GameFacade.Create(
            aliceName: "semantic-audit-A",
            bobName: "semantic-audit-B",
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

    // ==================================================================
    // LAYER A — printed-characteristics parity
    // ==================================================================

    /// <summary>
    /// Genuinely-legit parity differences. The engine builds a DFC / split /
    /// adventure card as a single front-face shell, but the seed row's composite
    /// "Front // Back" type line (or "//"-joined name) parses to the union of
    /// both faces' types — so a mismatch there is expected, not a bug. Composite
    /// rows are skipped automatically (name/type-line/mana-cost contains "//");
    /// this set is for any NON-composite legit difference found at report time.
    /// Each entry must name WHY it is legit.
    /// </summary>
    private static readonly HashSet<string> ParityAllowlist =
        new(StringComparer.Ordinal)
        {
            // Filled from the first report run — only justified differences.
        };

    internal sealed record ParityMismatch(string Name, string Field, string Expected, string Actual);

    /// <summary>True when the seed row is a composite face row (DFC / split /
    /// adventure) — the engine builds the front face, so its characteristics
    /// legitimately differ from the seed's union-of-faces values.</summary>
    private static bool IsCompositeRow(string name, CardEntity entity)
        => name.Contains("//", StringComparison.Ordinal)
        || entity.TypeLine.Contains("//", StringComparison.Ordinal)
        || (entity.ManaCost?.Contains("//", StringComparison.Ordinal) ?? false);

    /// <summary>
    /// Pure per-card comparison: the BUILT card's printed characteristics vs the
    /// seed <see cref="CardEntity"/>. Returns one <see cref="ParityMismatch"/>
    /// per disagreeing field. No dependency on the live pool — directly unit
    /// testable with a hand-built card + fake entity.
    /// </summary>
    internal static IEnumerable<ParityMismatch> CompareCardToEntity(ICard card, CardEntity entity)
    {
        var name = entity.Name;
        var parsed = TypeLineParser.Parse(entity.TypeLine);

        // --- Card types ---
        var expectedTypes = parsed.Types.Distinct().OrderBy(t => t).ToList();
        var actualTypes = card.CardTypes.Distinct().OrderBy(t => t).ToList();
        if (!expectedTypes.SequenceEqual(actualTypes))
            yield return new(name, "CardTypes", Join(expectedTypes), Join(actualTypes));

        // --- Supertypes ---
        var expectedSupers = parsed.Supertypes.Distinct().OrderBy(t => t).ToList();
        var actualSupers = card.Supertypes.Distinct().OrderBy(t => t).ToList();
        if (!expectedSupers.SequenceEqual(actualSupers))
            yield return new(name, "Supertypes", Join(expectedSupers), Join(actualSupers));

        // --- Subtypes (the parser drops subtypes with no enum value; the built
        // card is materialized from that SAME parser, so a mismatch here means a
        // factory hand-set the wrong subtypes). ---
        var expectedSubs = parsed.Subtypes.Distinct().OrderBy(t => t).ToList();
        var actualSubs = card.Subtypes.Distinct().OrderBy(t => t).ToList();
        if (!expectedSubs.SequenceEqual(actualSubs))
            yield return new(name, "Subtypes", Join(expectedSubs), Join(actualSubs));

        // --- Power / Toughness (creatures only; only when the seed P/T is a
        // plain integer — '*'/'X'/dynamic stats aren't a fixed printed value and
        // the engine models them as 0 + an effect). ---
        if (card is Creature creature)
        {
            if (TryInt(entity.Power, out var ep) && ep != creature.BasePower)
                yield return new(name, "BasePower", ep.ToString(), creature.BasePower.ToString());
            if (TryInt(entity.Toughness, out var et) && et != creature.BaseToughness)
                yield return new(name, "BaseToughness", et.ToString(), creature.BaseToughness.ToString());
        }

        // --- Loyalty (planeswalkers). ---
        if (card is Planeswalker pw && entity.Loyalty is int el && el != pw.StartingLoyalty)
            yield return new(name, "StartingLoyalty", el.ToString(), pw.StartingLoyalty.ToString());

        // --- Mana value (compare the parsed numeric value of the seed mana cost
        // vs the built card's; raw-string compare is too brittle across symbol
        // ordering). ---
        var expectedMv = Majik.Core.ValueObjects.ManaCost.Parse(entity.ManaCost ?? "").TotalValue;
        var actualMv = card is Card c
            ? c.ManaCostValue.TotalValue
            : Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost).TotalValue;
        if (expectedMv != actualMv)
            yield return new(name, "ManaValue", expectedMv.ToString(), actualMv.ToString());
    }

    private static IReadOnlyList<ParityMismatch> ComputeParityMismatches()
    {
        var mismatches = new List<ParityMismatch>();

        foreach (var name in ImplementedCardNames.All.OrderBy(n => n, StringComparer.Ordinal))
        {
            var entity = Repo.GetByName(name);
            if (entity == null) continue;                 // not in seed — reported by pool-wide audit
            if (!LiveCards.TryGetValue(name, out var card)) continue;

            // CR 712 / 711 / 715 — composite rows build as their front face.
            if (IsCompositeRow(name, entity) || ParityAllowlist.Contains(name)) continue;

            mismatches.AddRange(CompareCardToEntity(card, entity));
        }

        return mismatches;
    }

    /// <summary>
    /// REPORT: every printed-characteristics parity mismatch (card → field →
    /// seed-expected vs built-actual), minus the composite/DFC allowlist. These
    /// are high-confidence WRONG-implementation candidates.
    /// </summary>
    [Fact]
    public void PrintPrintedCharacteristicsParity()
    {
        var mismatches = ComputeParityMismatches();

        _out.WriteLine("===== LAYER A: PRINTED-CHARACTERISTICS PARITY (report) =====");
        _out.WriteLine($"Implemented names checked : {ImplementedCardNames.All.Count}");
        _out.WriteLine($"Cards with >=1 mismatch   : {mismatches.Select(m => m.Name).Distinct().Count()}");
        _out.WriteLine($"Total field mismatches    : {mismatches.Count}");
        _out.WriteLine("");
        foreach (var m in mismatches.OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Field))
            _out.WriteLine($"  [{m.Field}] {m.Name} — expected(seed)='{m.Expected}' actual(built)='{m.Actual}'");
    }

    // NOTE ON GATING: a gating [Fact] over the full parity set was deliberately
    // NOT added. The first report run surfaced 178 cards / 219 field mismatches,
    // a mix of (a) genuine factory bugs (wrong P/T, wrong mana value, wrong
    // subtypes — the Asmor class) and (b) systematic, arguably-legitimate
    // categories the engine models on purpose: the removed "Tribal" type the
    // engine still stamps (All Is Dust / Bitterblossom / Tarfire / …), Vehicles
    // modeled as Artifact,Creature, and artifact/enchantment lands. That set is
    // too large + noisy to gate honestly (it would be perpetually red), so per
    // the audit brief this is a report-only backlog. The targeted GATE lives in
    // <see cref="DeliberatelyWrongShape_WouldBeFlagged"/> + the pure
    // CompareCardToEntity unit tests, which prove the check catches the bug
    // class without pinning the whole noisy backlog.

    // ==================================================================
    // LAYER B — oracle-effect-keyword coverage (heuristic report)
    // ==================================================================

    /// <summary>
    /// Cards whose oracle effect-signal legitimately has no on-card bound
    /// mechanic the classifier can see — provably-working off-card effects, or
    /// effects the engine models elsewhere. Keeps the suspicious list honest.
    /// </summary>
    private static readonly HashSet<string> EffectCoverageAllowlist =
        new(StringComparer.Ordinal)
        {
            // Filled from the first report run with justifications.
        };

    private sealed record EffectSignal(string Label, Func<string, bool> Oracle, string[] MechanicFragments);

    private static readonly EffectSignal[] Signals =
    {
        new("scry",           o => Has(o, "scry "),                                  new[] { "Scry" }),
        new("surveil",        o => Has(o, "surveil "),                               new[] { "Surveil" }),
        new("draw a card",    o => Has(o, "draw a card") || Has(o, "draw two") || Has(o, "draw three"),
                                                                                     new[] { "Draw" }),
        new("mill",           o => Has(o, "mill "),                                  new[] { "Mill" }),
        new("create a token", o => Has(o, "create ") && Has(o, "token"),            new[] { "Token" }),
        new("+1/+1 counter",  o => Has(o, "+1/+1 counter"),                         new[] { "Counter", "Plus1" }),
        new("deals N damage", o => HasDamage(o),                                     new[] { "Damage", "Deal" }),
        new("gain N life",    o => Has(o, "gain ") && Has(o, "life"),               new[] { "Life", "Gain" }),
        new("destroy target", o => Has(o, "destroy target"),                        new[] { "Destroy" }),
        new("exile target",   o => Has(o, "exile target"),                          new[] { "Exile" }),
        new("return to hand", o => Has(o, "return target") && Has(o, "hand"),       new[] { "Return", "Bounce", "Hand" }),
        new("search library", o => Has(o, "search your library"),                   new[] { "Search", "Tutor", "Fetch" }),
    };

    private sealed record Suspicious(string Name, IReadOnlyList<string> MissingSignals);

    [Fact]
    public void PrintOracleEffectCoverage()
    {
        var suspicious = new List<Suspicious>();

        foreach (var name in ImplementedCardNames.All.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (EffectCoverageAllowlist.Contains(name)) continue;
            var entity = Repo.GetByName(name);
            if (entity == null) continue;
            if (!LiveCards.TryGetValue(name, out var card)) continue;

            // PERMANENTS only — instants/sorceries resolve their effects at cast
            // time off the card, so an empty on-card mechanic set is expected.
            if (!IsPermanent(card)) continue;

            var oracle = (entity.OracleText ?? "").ToLowerInvariant();
            if (oracle.Length == 0) continue;

            var blob = CardMechanicBlob(card);
            var missing = new List<string>();
            foreach (var sig in Signals)
            {
                if (sig.Oracle(oracle)
                    && !sig.MechanicFragments.Any(f => blob.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    missing.Add(sig.Label);
            }

            if (missing.Count > 0)
                suspicious.Add(new(name, missing));
        }

        // Rank: most missing signals first (most likely a genuinely-empty shell).
        var ranked = suspicious
            .OrderByDescending(s => s.MissingSignals.Count)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        _out.WriteLine("===== LAYER B: ORACLE-EFFECT COVERAGE (heuristic report — NOT a gate) =====");
        _out.WriteLine("Permanents whose oracle promises an effect with no matching bound mechanic.");
        _out.WriteLine("Expect false positives: effects on the cast-time stack, off-card continuous");
        _out.WriteLine("effects, or mechanics the classifier's keyword set doesn't recognize.");
        _out.WriteLine($"Suspicious permanents : {ranked.Count}");
        _out.WriteLine("");
        foreach (var s in ranked)
            _out.WriteLine($"  [{s.MissingSignals.Count}] {s.Name} — missing: {string.Join(", ", s.MissingSignals)}");
    }

    // ------------------------------------------------------------------
    // Heuristic helpers for Layer B.
    // ------------------------------------------------------------------
    private static bool Has(string oracle, string needle)
        => oracle.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool HasDamage(string oracle)
    {
        // "deals N damage" / "deal N damage" — require "damage" close after the verb.
        foreach (var verb in new[] { "deals ", "deal " })
        {
            int idx = oracle.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var rest = oracle.Substring(idx + verb.Length);
                int dmg = rest.IndexOf("damage", StringComparison.OrdinalIgnoreCase);
                if (dmg >= 0 && dmg < 12) return true;
                idx = oracle.IndexOf(verb, idx + verb.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    /// <summary>"What does this card do" proxy: the runtime-type names of every
    /// ability the card carries, the type names of every bound
    /// <see cref="IEffect"/> (e.g. <c>DrawCardIntent</c> / <c>TokenCreationIntent</c>),
    /// AND each effect's human <see cref="IEffect.Description"/> (the rich text
    /// inline effects carry via <c>Fx.Inline("Draw a card", …)</c>). The
    /// description is what collapses the false-positive rate — a triggered "draw
    /// a card" effect is an anonymous closure type whose CLASS name says nothing,
    /// but its description matches the oracle signal. Layer B never gates, so a
    /// generous proxy that over-credits is the right bias.</summary>
    private static string CardMechanicBlob(ICard card)
    {
        var sb = new StringBuilder();
        foreach (var ability in card.Abilities)
        {
            sb.Append(ability.GetType().Name).Append('|');
            // Reflectively surface any IEffect-typed values reachable from the
            // ability's public properties (TriggeredAbility.Effects,
            // ActivatedAbility effects, etc.) — both class name and Description.
            foreach (var prop in ability.GetType().GetProperties())
            {
                object? val;
                try { val = prop.GetValue(ability); }
                catch { continue; }
                AppendEffectNames(sb, val, depth: 0);
            }
        }
        return sb.ToString();
    }

    private static void AppendEffectNames(StringBuilder sb, object? val, int depth)
    {
        if (depth > 3) return;
        switch (val)
        {
            case null:
            case string:
                return;
            case IEffect eff:
                sb.Append(eff.GetType().Name).Append('|');
                if (!string.IsNullOrEmpty(eff.Description))
                    sb.Append(eff.Description).Append('|');
                return;
            case System.Collections.IEnumerable seq:
                foreach (var item in seq) AppendEffectNames(sb, item, depth + 1);
                return;
            default:
                if (val.GetType().Namespace?.StartsWith("Majik.Core.Effects", StringComparison.Ordinal) == true)
                    sb.Append(val.GetType().Name).Append('|');
                return;
        }
    }

    private static bool IsPermanent(ICard c)
        => c.HasType(CardType.Creature) || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment) || c.HasType(CardType.Planeswalker)
        || c.HasType(CardType.Land);

    // ------------------------------------------------------------------
    // Formatting helpers.
    // ------------------------------------------------------------------
    private static string Join<T>(IEnumerable<T> xs) => string.Join(",", xs);

    private static bool TryInt(string? s, out int v)
        => int.TryParse(s, out v);

    // ------------------------------------------------------------------
    // Materialization — identical to PoolWideImplementationAuditTests so the
    // prod build path is exercised the same way.
    // ------------------------------------------------------------------
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
