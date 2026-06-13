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
            // --- Vehicles: intentionally built Artifact + Creature (v1 modeling) ---
            // CR 301.5 / 702.122 — a Vehicle is an Artifact that becomes a
            // Creature only while crewed. v1 models it as an Artifact Creature
            // up front and gates the "is a creature" interactions through the
            // crew effect rather than dynamically adding the Creature type, so
            // the built card carries Creature in addition to the seed's printed
            // Artifact. This is a deliberate modeling choice, not a dropped
            // type — the crew gameplay (attack/block only when crewed) is
            // exercised by the dedicated Vehicle/crew tests.
            "Cultivator's Caravan",
            "Esika's Chariot",
            "Heart of Kiran",
            "Reckoner Bankbuster", // BOTH a Vehicle (built Artifact+Creature)
                                   // AND was flagged for the dropped-Artifact
                                   // bug; with types now preserved it is just
                                   // the Vehicle case.
            "Smuggler's Copter",
            "Subterranean Schooner",

            // --- Changelings: intentional all-creature-types expansion ---
            // CR 702.73a — Changeling is every creature type in every zone.
            // The engine models this as the full printed creature-subtype set
            // rather than the seed's single "Shapeshifter" subtype, so the
            // built card's Subtypes legitimately differ from the seed.
            "Mutable Explorer",
            "Unsettled Mariner",

            // --- Alt-cost-only spells: NO printed mana cost (seed MV 0) ---
            // These cards have an EMPTY printed mana cost — they can only be
            // cast via their alternative-cast mechanic (Cascade, CR 702.85),
            // never by paying mana. The seed row therefore carries an empty
            // ManaCost (MV 0). The v1 factory gives each a pragmatic stand-in
            // printed cost so the card (a) is materializable as a normal
            // Sorcery shell and (b) carries a mana VALUE that the Cascade
            // interaction needs: Crashing Footfalls / Living End sit at a
            // deliberate MV so a Modern-legal lower-MV cascade source
            // (Shardless Agent / Violent Outburst at MV 3) can cascade INTO
            // them while they themselves can't be hard-cast into the format's
            // cascade chains incorrectly. Matching the seed's empty cost
            // (MV 0) would make the shell carry MV 0 — breaking that cascade
            // mana-value interaction and removing the only sensible value the
            // card can have in v1 — so the printed-MV parity is INTENTIONALLY
            // off here pending a first-class no-mana-cost / Cascade-cast model.
            // Tracked as a deliberate v1 deferral (see v1-deferrals).
            "Crashing Footfalls",
            "Living End",
            "Glimpse of Tomorrow",

            // NOTE — the former "Tribal"-stamp bucket (All Is Dust,
            // Bitterblossom, Kozilek's Command, Nameless Inversion, Tarfire)
            // is NOT allowlisted: it was a real fix, not an intentional
            // difference. The Kindred (formerly "Tribal") card type was
            // removed from the game by the 2024 type-line errata (the seed
            // type lines carry no Kindred/Tribal type), so the engine stopped
            // stamping CardType.Tribal on these five factories. The only
            // mechanical consumer was Emrakul, the Promised End's
            // graveyard-card-type cost reducer, which under current rules must
            // NOT count a (now non-existent) Tribal type; lords/tribal-matters
            // effects key off creature SUBTYPES, never the removed card type.
            // The cards' subtypes (Eldrazi / Faerie / Shapeshifter / Goblin)
            // ride on the spell via the type line and are unaffected. (Tribal
            // product decision — resolved in the semantic-parity-tail PR.)
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
    /// Residual provable false positives the structural suppressions (loyalty
    /// closures / reminder text / self-pain damage / trigger-condition phrasing)
    /// can't programmatically reach. Each is a permanent whose flagged
    /// effect-signal IS implemented but is invisible to the
    /// <see cref="CardMechanicBlob"/> proxy because the effect lives on a
    /// continuous/replacement SERVICE (no standing ability), rides a mana
    /// ability's cost, or carries a rich description that simply omits the
    /// signal's keyword. Each entry verified working via the prod GameFacade
    /// path (the same build LiveCards uses) — re-confirm before adding here.
    ///
    /// <para>NOTE — the binder-chain-only utility LANDS were the bulk of this
    /// backlog: their <c>[CardName]</c> factories implement the ability, but
    /// lands are never routed through factories (<c>GameFacade.BuildDeckCard</c>
    /// gates the instance-swap on <c>!shell.HasType(Land)</c>), so the ability
    /// was DEAD in prod. <c>LandActivatedAbilityBinder</c> (v1-deferrals #12) now
    /// binds the bulk of them on the live binder chain — scry / draw / +1/+1
    /// counter / token / damage / gain-life / return-from-graveyard /
    /// destroy-target-land / Panorama search — so they no longer flag. The
    /// residual land entries in the allowlist below (Channel family, creature-
    /// land quoted granted abilities, count-linked / attack-rider tokens, mass
    /// keyword grant, end-of-combat-timed ping) are the deliberate deferrals
    /// that still need an engine primitive; they stay flagged-but-allowlisted as
    /// the honest deferral state.</para>
    /// </summary>
    private static readonly HashSet<string> EffectCoverageAllowlist =
        new(StringComparer.Ordinal)
        {
            // --- Token-doubling / counter-doubling / life-doubling REPLACEMENT
            // effects (CR 614). Modeled as a continuous replacement on the
            // game's replacement bus / CountersService, NOT a standing ability —
            // the card carries zero card.Abilities, so the blob is empty. All
            // verified to alter the relevant event in their own factory tests. ---
            "Anointed Procession",   // doubles tokens you'd create (replacement)
            "Doubling Season",       // doubles tokens + counters (replacement)
            "Parallel Lives",        // doubles tokens you'd create (replacement)
            "Boon Reflection",       // doubles life you'd gain (replacement)
            "Branching Evolution",   // doubles +1/+1 counters placed (replacement)
            "Hardened Scales",       // +1 extra +1/+1 counter (replacement)
            "Conclave Mentor",       // +1 extra +1/+1 counter (replacement) — the
                                     // dies-gain-life half IS a bound trigger; the
                                     // flagged +1/+1 is the counter replacement.

            // --- "Each <X> enters with an additional +1/+1 counter" / copy- or
            // revolt-enters-with-counter statics & replacements (CR 614.1d). The
            // counter rides the ETB pipeline / a static, not a standing
            // ability. ---
            "Grumgully, the Generous", // other non-Humans enter with +1/+1 (static)
            "Metallic Mimic",          // chosen-type creatures enter with +1/+1 (static)
            "Narnam Renegade",         // Revolt: enters with a +1/+1 counter (ETB replacement)
            "Spark Double",            // enters-as-copy with an extra +1/+1 counter (cast-time copy)
            "Phoenix of Ash",          // escapes WITH a +1/+1 counter — a cast-time
                                       // (escape) rider, off the permanent.

            // --- Effect IS bound but the rich Fx.Inline DESCRIPTION omits the
            // signal's keyword, so the blob's text match misses it. Verified the
            // ability + effect are present on the live card. ---
            "Boggart Harbinger",       // search bound; desc reads "stack a Goblin … on top"
            "Klothys, God of Destiny", // deals 2 to each opponent bound via Fx.DealDamageAny; desc says "2 to each opponent"
            "Nurturing Pixie",         // +1/+1 bound in the ETB trigger; desc reads "then grow if returned"
            "Phelia, Exuberant Shepherd", // +1/+1-on-Phelia bound in the attack trigger; desc omits "counter"
            "Ragavan, Nimble Pilferer", // creates a Treasure TOKEN bound; desc reads "Treasure"
            "Tasigur, the Golden Fang", // the {…}: Mill two … ability is bound; desc omits "mill"
            "Stormchaser's Talent",    // Level-2 "return target instant/sorcery to hand" bound on the level-up trigger; desc names the level
            "Generous Ent",            // ETB Food TOKEN bound (investigate-style); desc focuses on the gain-life rider
            "Old-Growth Troll",        // the Troll TOKEN is created by the granted Aura ability after it dies — off-card (granted-ability), not a standing ability

            // --- Mana-ability COST/RIDER (CR 605) the blob can't see. ---
            "Chromatic Sphere",        // "Sacrifice: Add any color. Draw a card" — the draw rides the ManaAbility additionalCostPayer

            // --- Token-rider effects that live on the CREATED TOKEN, not the
            // source permanent (off-card). ---
            "Weapons Manufacturing",   // the Munitions TOKEN deals the damage on leaving; the enchantment only creates it (bound)
            "Sedgemoor Witch",         // the Pest TOKEN's dies-trigger gains 1 life; the magecraft token-create is bound

            // --- Aura that GRANTS an activated ability to the enchanted creature
            // (CR 613 granted ability) — the token-creating ability is on the
            // host creature, not Splinter Twin itself. ---
            "Splinter Twin",

            // --- Saga chapter effects (CR 714) live in an off-card SagaState
            // (lore-counter triggers), NOT standing card.Abilities. Verified:
            // the live Urza's Saga carries a SagaState whose chapter II creates
            // the 0/0 Construct TOKEN and chapter III SEARCHES for an artifact
            // (SagaBinder.MakeUrzasSagaChapterHandler). The blob only sees the
            // chapter-I {T}: Add {C} mana ability. ---
            "Urza's Saga",

            // === GENUINELY-DEFERRED UTILITY LANDS (v1-deferrals #12) ==========
            // LandActivatedAbilityBinder now binds the bulk of the binder-chain-
            // only utility-land activated abilities in prod (scry / draw /
            // +1/+1 counter / token / damage / gain-life / return-from-graveyard
            // / destroy-target-land / Panorama search). The entries below are the
            // residual deferrals it deliberately does NOT bind — each needs an
            // engine primitive that doesn't exist on the binder-reachable path
            // yet. They stay flagged-but-allowlisted (honest deferral, not a
            // silent gap). See v1-deferrals #12 for the running list.

            // -- Channel family (CR 702.74): NOW BOUND. LandActivatedAbilityBinder
            // recognises the "Channel — {cost}, Discard this card: <effect>" line
            // and emits an activated ability with DiscardSelfCost (the hand-zone
            // activation seam, CR 702.74a) + ManaCostCost, each effect mapping to
            // an existing one-shot verb (destroy / bounce / mill / damage /
            // token). Removed from the allowlist — the detector no longer trips. --

            // -- Creature-land quoted granted abilities / conditional animate.
            // The N/N animate body binds via ManlandBinder; the QUOTED attack
            // trigger (token / counter / exile) is a granted-on-animate ability
            // with no generic primitive (same posture ManlandBinder defers).
            // Crawling Barrens is a conditional animate (counter step first). --
            "Den of the Bugbear",            // quoted "create a Goblin token" attack trigger on the animated body
            "Raging Ravine",                 // quoted "+1/+1 counter on it" attack trigger on the animated body
            "Hive of the Eye Tyrant",        // quoted "exile target card from defending player's GY" attack trigger
            "Crawling Barrens",              // counters-then-conditional-animate (no fixed-subtype animate)

            // -- Token riders with no generic primitive on the binder path. --
            // Treasure Vault ("{X}{X}, {T}, Sacrifice this land: Create X
            // Treasure tokens") is NO LONGER deferred: the count-linked Treasure
            // mint now binds in prod via LandActivatedAbilityBinder
            // (BindCreateXTreasures, reading the per-activation X off
            // ResolutionContext.ChosenX), so it stops tripping the detector and
            // is removed from this allowlist.
            "Dalkovan Encampment",           // delayed "Whenever you attack this turn, create two tapped+attacking Warriors" rider
            "Mirrex",                         // token carries toxic 1 + a quoted "can't block" ability (richer token shape)

            // -- Other residual deferrals. --
            "Demolition Field",              // destroy binds; the both-players "search for a basic land" rider is deferred
            "Desert",                         // "{T}: deal 1 to target attacking creature" gated to the end-of-combat step — no binder-reachable timing seam
            // Vault of the Archangel ("Creatures you control gain deathtouch and
            // lifelink until end of turn") is NO LONGER deferred: the mass
            // until-EOT keyword grant now binds in prod via
            // LandActivatedAbilityBinder (BindGrantKeywordsToCreaturesYouControl),
            // so it stops tripping the detector and is removed from this allowlist.
        };

    private sealed record EffectSignal(string Label, Func<string, bool> Oracle, string[] MechanicFragments);

    private static readonly EffectSignal[] Signals =
    {
        new("scry",           o => Has(o, "scry "),                                  new[] { "Scry" }),
        new("surveil",        o => Has(o, "surveil "),                               new[] { "Surveil" }),
        new("draw a card",    o => Has(o, "draw a card") || Has(o, "draw two") || Has(o, "draw three"),
                                                                                     new[] { "Draw" }),
        new("mill",           o => Has(o, "mill "),                                  new[] { "Mill" }),
        // Effect DESCRIPTIONS use the natural-language verb ("create a 1/1 white
        // Cat Soldier", "put a +1/+1 counter", "deal 1 damage") rather than the
        // C# class name, so credit the verb token too — a token/counter/damage
        // ability whose closure is anonymous (no telling class name) is still
        // recognised by its rich Fx.Inline description.
        new("create a token", o => Has(o, "create ") && Has(o, "token"),            new[] { "Token", "create " }),
        new("+1/+1 counter",  o => Has(o, "+1/+1 counter"),                         new[] { "Counter", "Plus1", "+1/+1" }),
        new("deals N damage", o => HasDamage(o),                                     new[] { "Damage", "Deal", "damage" }),
        new("gain N life",    o => Has(o, "gain ") && Has(o, "life"),               new[] { "Life", "Gain" }),
        new("destroy target", o => Has(o, "destroy target"),                        new[] { "Destroy" }),
        new("exile target",   o => Has(o, "exile target"),                          new[] { "Exile" }),
        new("return to hand", o => Has(o, "return target") && Has(o, "hand"),       new[] { "Return", "Bounce", "Hand" }),
        new("search library", o => Has(o, "search your library"),                   new[] { "Search", "Tutor", "Fetch" }),
    };

    private sealed record Suspicious(string Name, IReadOnlyList<string> MissingSignals);

    /// <summary>
    /// Per-card detection, factored out so the false-positive-suppression rules
    /// are unit-testable in isolation (see <c>EffectCoverage_*</c> tests).
    /// Returns the effect signals the card's oracle promises but whose bound
    /// mechanic the classifier cannot see — AFTER the structural false-positive
    /// suppressions below. Empty list = not flagged.
    ///
    /// <para>The suppressions (each kills a provable false-positive CLASS the
    /// blob can't see but which IS implemented off-card):</para>
    /// <list type="number">
    ///   <item><b>Loyalty closures (CR 606).</b> A planeswalker's loyalty
    ///   abilities ARE standing <see cref="LoyaltyAbility"/> entries, but their
    ///   effects are opaque <c>Fx.Inline</c> closures whose Description is just
    ///   "Loyalty +N" — the blob can't read the create-token / draw / damage
    ///   body. Loyalty abilities are dispatched + resolved end-to-end through the
    ///   priority loop, so a card carrying ANY <see cref="LoyaltyAbility"/> has
    ///   its loyalty-derived oracle signals covered. Almost all of a
    ///   planeswalker's oracle text is loyalty abilities, so we treat such a card
    ///   as covered.</item>
    ///   <item><b>Reminder text (CR 207.2).</b> Keyword reminder text in
    ///   parentheses (Lifelink "(Damage … causes you to gain that much life)",
    ///   Infect/Wither, First strike, Dredge, Connive, Explore, Bloodthirst,
    ///   Cycling "({cost}, Discard this card: Draw a card.)", Investigate / Food
    ///   / Blood token reminders) mentions an effect that belongs to a keyword
    ///   that IS bound elsewhere — never an on-card ability of this card. We scan
    ///   only the NON-parenthetical text so a reminder-only signal is not
    ///   flagged.</item>
    ///   <item><b>Self-pain mana riders (CR 120).</b> "This land/artifact deals
    ///   1 damage to you" on the painland / Talisman cycle is bound as a
    ///   <see cref="ManaAbility"/> additional-cost (LoseLife) the blob can't see.
    ///   When the ONLY damage in the oracle is self-directed ("damage to you"),
    ///   the "deals N damage" signal is suppressed.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> DetectMissingEffectSignals(ICard card, CardEntity entity)
    {
        // PERMANENTS only — instants/sorceries resolve their effects at cast
        // time off the card, so an empty on-card mechanic set is expected.
        if (!IsPermanent(card)) return Array.Empty<string>();

        var rawOracle = (entity.OracleText ?? "").ToLowerInvariant();
        if (rawOracle.Length == 0) return Array.Empty<string>();

        // Suppression #1 — loyalty closures. A planeswalker's loyalty bodies are
        // opaque Fx.Inline closures (Description == "Loyalty +N"); they ARE bound
        // + dispatched, just invisible to the blob. Treat the whole card as
        // covered.
        if (card.Abilities.OfType<LoyaltyAbility>().Any())
            return Array.Empty<string>();

        // Suppression #2 — strip parenthetical reminder text (CR 207.2) before
        // scanning. Keyword/cycling/token reminders describe an effect that
        // belongs to a bound keyword, not an on-card ability of this card.
        var oracle = StripReminderText(rawOracle);

        var blob = CardMechanicBlob(card);
        var missing = new List<string>();
        foreach (var sig in Signals)
        {
            if (!sig.Oracle(oracle)) continue;

            // Suppression #3 — self-pain mana rider. If the only damage in the
            // (reminder-stripped) oracle is "deals N damage to you", the painland
            // / Talisman LoseLife mana-cost rider covers it.
            if (sig.Label == "deals N damage" && IsOnlySelfPainDamage(oracle))
                continue;

            // Suppression #4 — the signal word is a TRIGGER CONDITION, not an
            // effect the card produces. "Whenever you gain life, …" / "Whenever
            // you draw a card, …" / "If you would gain life, …" name the EVENT
            // the ability keys off; the EFFECT is the rider (a +1/+1 counter, a
            // life-doubling replacement, …) which is bound (or is a continuous
            // replacement on a service). Suppress the gain/draw signal when its
            // only occurrence is such a condition clause.
            if (sig.Label == "gain N life" && IsOnlyGainLifeCondition(oracle))
                continue;
            if (sig.Label == "draw a card" && IsOnlyDrawCondition(oracle))
                continue;

            if (!sig.MechanicFragments.Any(f => blob.Contains(f, StringComparison.OrdinalIgnoreCase)))
                missing.Add(sig.Label);
        }
        return missing;
    }

    /// <summary>CR 207.2 — remove every parenthesized reminder-text span. The
    /// effect words inside reminder text (e.g. lifelink's "gain that much life",
    /// cycling's "Draw a card") name a bound keyword's behaviour, never an
    /// on-card ability the blob should have to carry.</summary>
    private static string StripReminderText(string oracle)
        => System.Text.RegularExpressions.Regex.Replace(oracle, @"\([^)]*\)", " ");

    /// <summary>True when EVERY "deal(s) N damage" occurrence in the oracle is
    /// self-directed ("... damage to you") — the painland / Talisman LoseLife
    /// mana-cost rider, which the blob can't see. A card that also deals damage
    /// to "any target" / "each opponent" / a creature is NOT suppressed.</summary>
    private static bool IsOnlySelfPainDamage(string oracle)
    {
        bool sawDamage = false;
        foreach (var verb in new[] { "deals ", "deal " })
        {
            int idx = oracle.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var rest = oracle.Substring(idx + verb.Length);
                int dmg = rest.IndexOf("damage", StringComparison.OrdinalIgnoreCase);
                if (dmg >= 0 && dmg < 12)
                {
                    sawDamage = true;
                    // Look at the words right after "damage" for the recipient.
                    var after = rest.Substring(dmg);
                    if (!after.StartsWith("damage to you", StringComparison.OrdinalIgnoreCase))
                        return false; // a non-self damage target exists
                }
                idx = oracle.IndexOf(verb, idx + verb.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
        return sawDamage;
    }

    /// <summary>True when every "gain … life" phrase in the oracle is a TRIGGER
    /// CONDITION ("whenever you gain life", "if you would gain life") rather than
    /// an effect that gains life. The card's actual effect is the rider (bound),
    /// or a life-gain-doubling replacement (continuous, off-card).</summary>
    private static bool IsOnlyGainLifeCondition(string oracle)
        => OnlyMatchesCondition(
            oracle,
            valuePattern: @"gain\b[^.]*?\blife",
            conditionPatterns: new[]
            {
                @"whenever\s+\w+\s+gain[s]?\b[^.]*?\blife",   // "whenever you gain life"
                @"if\s+\w+\s+would\s+gain\b[^.]*?\blife",     // "if you would gain life"
                @"can'?t\s+gain\b[^.]*?\blife",               // "players can't gain life"
            });

    /// <summary>True when every "draw a card" phrase in the oracle is a TRIGGER
    /// CONDITION ("whenever you draw a card", "if you would draw a card")
    /// rather than an effect that draws. (Cycling reminders are already stripped
    /// upstream.)</summary>
    private static bool IsOnlyDrawCondition(string oracle)
        => OnlyMatchesCondition(
            oracle,
            valuePattern: @"draw\s+(a\s+card|two|three)",
            conditionPatterns: new[]
            {
                @"whenever\s+\w+(\s+\w+)?\s+draw[s]?\s+(a\s+card|their)",  // "whenever you/an opponent draw(s) a card"
                @"if\s+\w+\s+would\s+draw\s+a\s+card",                     // "if you would draw a card"
            });

    /// <summary>Generic: true when at least one occurrence of
    /// <paramref name="valuePattern"/> exists AND every such occurrence is part
    /// of one of the <paramref name="conditionPatterns"/> (a trigger/replacement
    /// CONDITION clause), so the signal names an event the card keys off, not an
    /// effect it produces.</summary>
    private static bool OnlyMatchesCondition(
        string oracle, string valuePattern, string[] conditionPatterns)
    {
        var opts = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var valueMatches = System.Text.RegularExpressions.Regex.Matches(oracle, valuePattern, opts);
        if (valueMatches.Count == 0) return false;

        // Build the set of character spans covered by any condition clause.
        var condSpans = new List<(int Start, int End)>();
        foreach (var pat in conditionPatterns)
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(oracle, pat, opts))
                condSpans.Add((m.Index, m.Index + m.Length));

        foreach (System.Text.RegularExpressions.Match v in valueMatches)
        {
            int vs = v.Index, ve = v.Index + v.Length;
            bool covered = condSpans.Any(s => vs >= s.Start && ve <= s.End);
            if (!covered) return false; // a value occurrence that is NOT a condition → a real effect
        }
        return true;
    }

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

            var missing = DetectMissingEffectSignals(card, entity);
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
        _out.WriteLine("Loyalty closures, parenthetical reminder text (CR 207.2), and self-pain mana");
        _out.WriteLine("riders are suppressed structurally; the EffectCoverageAllowlist removes the");
        _out.WriteLine("residual provable off-card false positives. The remaining flags are the");
        _out.WriteLine("genuine-gap backlog (mostly the binder-chain-only land activated abilities");
        _out.WriteLine("whose [CardName] factories are dead in prod — see v1-deferrals land-routing).");
        _out.WriteLine($"Suspicious permanents : {ranked.Count}");
        _out.WriteLine("");
        foreach (var s in ranked)
            _out.WriteLine($"  [{s.MissingSignals.Count}] {s.Name} — missing: {string.Join(", ", s.MissingSignals)}");
    }

    // ==================================================================
    // LAYER B — heuristic-honesty GATES (these DO gate; tiny + targeted).
    // They prove the false-positive suppressions fire and a genuine gap
    // still surfaces, without pinning the whole noisy backlog.
    // ==================================================================

    /// <summary>A planeswalker (loyalty closures) must NOT be flagged — every
    /// effect signal it carries is inside a <see cref="LoyaltyAbility"/> body
    /// the blob can't read. Jace, the Mind Sculptor promises "draw … / return
    /// to hand"; suppression #1 covers it.</summary>
    [Fact]
    public void Heuristic_Planeswalker_LoyaltyClosures_NotFlagged()
    {
        var entity = Repo.GetByName("Jace, the Mind Sculptor");
        entity.Should().NotBeNull();
        LiveCards.TryGetValue("Jace, the Mind Sculptor", out var card).Should().BeTrue();
        card!.Abilities.OfType<LoyaltyAbility>().Should().NotBeEmpty(
            "Jace carries standing LoyaltyAbility entries");

        DetectMissingEffectSignals(card, entity!).Should().BeEmpty(
            "a planeswalker's loyalty-closure effects are bound + dispatched, just invisible to the blob");
    }

    /// <summary>An instant/sorcery is never a permanent, so its cast-time
    /// effects are always excluded — Lightning Bolt ("deals 3 damage") must not
    /// be flagged.</summary>
    [Fact]
    public void Heuristic_Instant_CastTimeEffect_NotFlagged()
    {
        // Pick any implemented instant/sorcery with a damage signal.
        var instant = ImplementedCardNames.All
            .Select(n => (Name: n, Entity: Repo.GetByName(n)))
            .FirstOrDefault(x => x.Entity != null
                && LiveCards.TryGetValue(x.Name, out var c)
                && !IsPermanent(c)
                && (x.Entity.OracleText ?? "").Contains("damage", StringComparison.OrdinalIgnoreCase));
        instant.Entity.Should().NotBeNull("the pool has at least one damage-dealing instant/sorcery");

        var card = LiveCards[instant.Name];
        DetectMissingEffectSignals(card, instant.Entity!).Should().BeEmpty(
            "instant/sorcery effects resolve at cast time off the card — never flagged");
    }

    /// <summary>A pain land ("deals 1 damage to you" mana rider) must NOT be
    /// flagged — suppression #3 covers the self-pain mana cost.</summary>
    [Fact]
    public void Heuristic_PainLand_SelfDamageRider_NotFlagged()
    {
        var entity = Repo.GetByName("Adarkar Wastes");
        entity.Should().NotBeNull();
        LiveCards.TryGetValue("Adarkar Wastes", out var card).Should().BeTrue();

        DetectMissingEffectSignals(card!, entity!).Should().BeEmpty(
            "'deals 1 damage to you' is a ManaAbility cost rider (LoseLife), not a missing ping");
    }

    /// <summary>A cycling land ("Cycling … Draw a card" reminder) must NOT be
    /// flagged for "draw a card" — suppression #2 strips the reminder text.
    /// (The land may still be flagged for OTHER genuine gaps; this asserts only
    /// the cycling-draw false positive is gone.)</summary>
    [Fact]
    public void Heuristic_CyclingLand_ReminderDraw_NotFlaggedForDraw()
    {
        var entity = Repo.GetByName("Tranquil Thicket");
        entity.Should().NotBeNull();
        LiveCards.TryGetValue("Tranquil Thicket", out var card).Should().BeTrue();

        DetectMissingEffectSignals(card!, entity!).Should().NotContain("draw a card",
            "cycling's 'Draw a card' lives in parenthetical reminder text (CR 207.2)");
    }

    /// <summary>The genuine-gap sentinel — a binder-chain-only land whose
    /// special effect is GENUINELY DEAD in prod MUST stay flagged. Desert
    /// promises "{T}: This land deals 1 damage to target attacking creature.
    /// Activate only during the end of combat step." — the end-of-combat-step
    /// timing gate has no binder-reachable <c>canActivate</c> timing seam, so it
    /// is a deliberate deferral (<see cref="EffectCoverageAllowlist"/> entry,
    /// v1-deferrals #12). If this ever stops flagging, the land was either fixed
    /// (move it off the deferral list) or the heuristic over-suppressed.
    ///
    /// <para>NOTE — Castle Vantress's "Scry 2" and Vault of the Archangel's mass
    /// "Creatures you control gain deathtouch and lifelink until end of turn"
    /// grant were the prior sentinels; both are now BOUND in prod via
    /// <c>LandActivatedAbilityBinder</c> (v1-deferrals #12), so the sentinel
    /// moved to a still-genuinely-deferred land (Desert's end-of-combat-timed
    /// ping).</para></summary>
    [Fact]
    public void Heuristic_GenuineLandGap_StillFlagged()
    {
        var entity = Repo.GetByName("Desert");
        entity.Should().NotBeNull();
        LiveCards.TryGetValue("Desert", out var card).Should().BeTrue();

        DetectMissingEffectSignals(card!, entity!).Should().Contain("deals N damage",
            "Desert's end-of-combat-timed ping is genuinely unbound (no binder-reachable end-of-combat timing seam — v1-deferrals #12)");

        // Vault of the Archangel's mass keyword grant, by contrast, is now BOUND
        // in prod via LandActivatedAbilityBinder.
        var vault = Repo.GetByName("Vault of the Archangel");
        LiveCards.TryGetValue("Vault of the Archangel", out var vaultCard).Should().BeTrue();
        DetectMissingEffectSignals(vaultCard!, vault!).Should().BeEmpty(
            "Vault's mass deathtouch/lifelink keyword grant now binds via LandActivatedAbilityBinder");

        // Castle Vantress's scry, by contrast, is now BOUND in prod.
        var vantress = Repo.GetByName("Castle Vantress");
        LiveCards.TryGetValue("Castle Vantress", out var vantressCard).Should().BeTrue();
        DetectMissingEffectSignals(vantressCard!, vantress!).Should().NotContain("scry",
            "Castle Vantress's Scry 2 now binds via LandActivatedAbilityBinder");
    }

    /// <summary>The EffectCoverageAllowlist must stay HONEST: every entry must
    /// be a card that the detector would otherwise flag. A stale entry (one the
    /// suppressions or a later fix already cleared) is a silent bug, so fail if
    /// any allowlisted name no longer produces a missing signal.</summary>
    [Fact]
    public void Heuristic_Allowlist_EntriesStillTripTheDetector()
    {
        var stale = new List<string>();
        foreach (var name in EffectCoverageAllowlist)
        {
            var entity = Repo.GetByName(name);
            if (entity == null) { stale.Add($"{name} (not in seed)"); continue; }
            if (!LiveCards.TryGetValue(name, out var card)) { stale.Add($"{name} (not built)"); continue; }
            if (DetectMissingEffectSignals(card, entity).Count == 0)
                stale.Add($"{name} (no longer flagged — remove it)");
        }
        stale.Should().BeEmpty(
            "every allowlist entry must still trip the detector; remove stale entries");
    }

    // ------------------------------------------------------------------
    // Heuristic helpers for Layer B.
    // ------------------------------------------------------------------
    private static bool Has(string oracle, string needle)
        => oracle.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // "deal(s) <N> damage" where N is a number / X — the signature of an ACTIVE
    // damage effect (a ping). Deliberately excludes:
    //   - "deals COMBAT damage" — a TRIGGER CONDITION ("Whenever equipped
    //     creature deals combat damage to a player, …"); combat damage is dealt
    //     by the combat system, never as an ability effect, and the bound effect
    //     is the trigger's RIDER (gain life / draw / etc.), not a ping.
    //   - "would deal … damage … deals double/that damage" — a damage-altering
    //     REPLACEMENT effect (Furnace of Rath / Gisela), modeled continuously on
    //     a service, not an on-card ping ability.
    // Requiring an explicit numeric amount right after the verb keeps both of
    // those out (they read "deals combat damage" / "deals double that damage",
    // no leading digit).
    private static readonly System.Text.RegularExpressions.Regex DamagePingRegex =
        new(@"deals?\s+(\d+|x)\s+damage",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool HasDamage(string oracle) => DamagePingRegex.IsMatch(oracle);

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
    // Materialization — delegates to the shared DeckCardShellBuilder so the
    // prod build path (RealDeckLoader → GameFacade) is exercised the same way,
    // including CR 205.1b multi-type preservation (artifact lands actually
    // carry Artifact) and the CR 202.2c color indicator.
    // ------------------------------------------------------------------
    private static ICard MaterializeReal(CardEntity entity)
        => DeckCardShellBuilder.Build(entity);
}
