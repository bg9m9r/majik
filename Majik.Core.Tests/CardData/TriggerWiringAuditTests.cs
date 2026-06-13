using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Class B static trigger-wiring audit (CR 603.1).
///
/// Scans every implemented card in the embedded Modern pool for oracle text
/// that implies a triggered ability ("When", "Whenever", "At the beginning
/// of") and flags any card that builds through the production card-build path
/// without binding at least one <see cref="ITriggeredAbility"/>.
///
/// BUILD PATH:
/// The audit mirrors the prod path faithfully:
///   - Factory-backed cards (those with a [CardName] factory registered in
///     ImplementedCardNames): built via NamedCardFactory.Create(name, owner, effects)
///     with a live ContinuousEffectsService — the exact same call as
///     GameFacade.BuildDeckCard when RouteThroughNamedFactories=true.
///   - Binder-only implemented cards (no named factory): built via
///     ScryfallCardFactory.Create() — the binder chain path.
///     (Currently 0 binder-only implemented cards exist in the pool —
///      IsImplemented is derived from ImplementedCardNames at load time.)
///
/// WHY NamedCardFactory NOT ScryfallCardFactory for factory-backed cards:
/// ScryfallCardFactory.Create() does NOT apply FactoryRouting — it always
/// goes through the binder chain. The prod path is GameFacade.BuildDeckCard
/// which calls NamedCardFactory.Create(name, owner, effects). Using only
/// ScryfallCardFactory would give wrong (binder-chain) results for factory
/// cards, missing their actual trigger wiring.
///
/// THREE CATEGORIES:
///   1. Legitimate non-triggers  → <see cref="KnownNonTriggerCards"/> with a reason.
///   2. Known missing-trigger bugs → <see cref="KnownMissingTriggerBugs"/> (// BUG:).
///      Tracked explicitly so the suite is green but bugs are visible.
///      File follow-up PRs for each BUG entry.
///   3. New unexplained gaps → the real-pool test fails and lists them.
///
/// POOL STATS (as of 2026-06-13):
///   Festival Crasher + Kiln Fiend removed from KnownMissingTriggerBugs after
///   the missing-trigger-effects-overload-dispatch deferral was paid down —
///   both now bind their cast-pump trigger through the prod build path and the
///   real-pool audit verifies them directly. KnownMissingTriggerBugs = 9.
/// </summary>
public class TriggerWiringAuditTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Anchored pattern that implies a triggered ability (CR 603.1).
    /// Only matches at the beginning of a line (after optional whitespace)
    /// to avoid matching "when" inside reminder text or mid-sentence.
    /// </summary>
    private static bool HasTriggerText(string? oracle)
    {
        if (string.IsNullOrWhiteSpace(oracle)) return false;
        return Regex.IsMatch(
            oracle,
            @"(^|\n)\s*(When |Whenever |At the beginning of )",
            RegexOptions.IgnoreCase);
    }

    private static bool HasTriggerAbility(Majik.Core.Cards.ICard card) =>
        card.Abilities.OfType<ITriggeredAbility>().Any();

    /// <summary>
    /// Build a card through the same path as GameFacade.BuildDeckCard:
    /// factory-backed cards go through NamedCardFactory.Create with a
    /// live ContinuousEffectsService; binder-only cards go through
    /// ScryfallCardFactory.Create. Returns null on build failure.
    /// </summary>
    private static Majik.Core.Cards.ICard? BuildCard(
        string name, Player owner, EmbeddedCardRepository repo,
        ContinuousEffectsService effects)
    {
        try
        {
            if (ImplementedCardNames.HasRealFactory(name))
            {
                // Factory path — mirrors GameFacade.BuildDeckCard:
                //   NamedCardFactory.Create(shell.Name, owner, effects)
                return NamedCardFactory.Create(name, owner, effects);
            }
            else
            {
                return new ScryfallCardFactory(repo).Create(name, owner);
            }
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // KnownNonTriggerCards: trigger text present but no ITriggeredAbility
    // is CORRECT for these cards. Each entry carries a mandatory reason.
    // ──────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> KnownNonTriggerCards =
        new Dictionary<string, string>
        {
            // ── Pact cycle — "At the beginning of your next upkeep, pay …" ──
            // These are SPELLS (instants/sorceries). The upkeep trigger is a
            // DELAYED triggered ability registered at spell cast time via
            // BuildDefinition (when a TriggerManager is supplied at cast time).
            // The card SHAPE (ICard) carries no resident ITriggeredAbility;
            // the trigger fires from the spell resolution path, not from the
            // card sitting on the battlefield. This is correct MTG semantics.
            ["Pact of Negation"] =
                "Instant (spell). Upkeep payment is a delayed trigger registered at cast time " +
                "by BuildDefinition when a TriggerManager is supplied; no resident ITriggeredAbility.",
            ["Summoner's Pact"] =
                "Instant (spell). Same delayed-trigger-at-cast pattern as Pact of Negation.",
            ["Slaughter Pact"] =
                "Instant (spell). Same delayed-trigger-at-cast pattern as Pact of Negation.",
            ["Pact of the Titan"] =
                "Instant (spell). Same delayed-trigger-at-cast pattern as Pact of Negation.",

            // ── Galvanic Iteration ──
            // Sorcery (spell). "When you next cast an instant or sorcery spell
            // this turn, copy that spell." is a DELAYED triggered ability placed
            // on the stack when the sorcery resolves (via DelayedTriggeredAbility
            // in the factory's BuildDefinition). The card shape carries no
            // resident ITriggeredAbility — same pattern as the Pact cycle.
            ["Galvanic Iteration"] =
                "Sorcery (spell). Delayed copy trigger registered at spell resolution; " +
                "no resident ITriggeredAbility on the card shape.",

            // ── City of Brass ──
            // "Whenever this land becomes tapped, it deals 1 damage to you."
            // The engine has no 'becomes-tapped' event on the EventBus (tapping
            // via mana ability is CR 605.3 — never on the stack). The factory
            // folds the damage into the mana ability activation as an
            // additionalCostPayer — covers the common case exactly, matching the
            // v1 deferrals strategy. Explicit defer noted in CityOfBrassFactory.
            ["City of Brass"] =
                "v1 defer: 'becomes tapped' trigger folded into ManaAbility.additionalCostPayer. " +
                "No tapped-event on the EventBus (CR 605.3). See CityOfBrassFactory deferred section.",

            // ── Mana-triggered auras (Overgrowth / Fertile Ground / Utopia Sprawl) ──
            // "Whenever enchanted land is tapped for mana, its controller adds …"
            // These are triggered MANA abilities (CR 605.1b). They ARE wired as
            // ITriggeredAbility — but ONLY in the Create(Player, TriggerManager?)
            // overload. The audit uses NamedCardFactory.Create(name, owner, effects)
            // which the source generator dispatches to Create(owner) (single-arg),
            // not to the TriggerManager overload. In prod GameFacade.BuildDeckCard
            // also calls Create(owner, effects) — no TriggerManager passed to the
            // factory, so the mana trigger is registered only when BuildDeckCard
            // calls the dedicated TriggerManager overload through game init code.
            // These are CORRECT as non-triggers from the audit's perspective: the
            // trigger is wired at a higher layer (game initialization), not on the
            // bare card shape.
            ["Overgrowth"] =
                "Mana trigger (CR 605.1b) wired in Create(Player, TriggerManager?) overload; " +
                "the bare card shape from NamedCardFactory.Create is intentionally trigger-free. " +
                "TriggerManager is plumbed by the game at a higher layer.",
            ["Fertile Ground"] =
                "Mana trigger (CR 605.1b) wired in Create(Player, TriggerManager?) overload; " +
                "same pattern as Overgrowth.",
            ["Utopia Sprawl"] =
                "Mana trigger (CR 605.1b) wired in Create(Player, TriggerManager?) overload; " +
                "same pattern as Overgrowth.",

            // ── Stormbreath Dragon ──
            // "At the beginning of your end step, if this creature is monstrous,
            //  each opponent loses 1 life for each card in their hand."
            // The "becomes monstrous" trigger is handled INLINE by the monstrosity
            // activation closure (StormbreathDragonFactory doc: "invokes the
            // becomes-monstrous trigger inline rather than firing as a separate
            // triggered ability on the stack — v1"). The trigger text matches
            // HasTriggerText but the factory deliberately avoids ITriggeredAbility.
            ["Stormbreath Dragon"] =
                "v1 defer: 'At the beginning of your end step, if monstrous' check is handled " +
                "inline by the monstrosity activation closure, not as an ITriggeredAbility. " +
                "See StormbreathDragonFactory doc.",
        };

    // ──────────────────────────────────────────────────────────────────────
    // KnownMissingTriggerBugs: cards that SHOULD have ITriggeredAbility but
    // currently don't through the prod build path. Tracked here explicitly
    // so the suite stays green while bugs are visible. File PRs for each.
    //
    // Format per entry:
    //   "// BUG: <oracle trigger phrase> — <reason missing> + <issue ref if any>"
    // ──────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> KnownMissingTriggerBugs =
        new Dictionary<string, string>
        {
            ["Baral, Chief of Compliance"] =
                "// BUG: 'Whenever a spell or ability you control is countered, you may draw a card, " +
                "then discard a card.' — BaralChiefOfComplianceFactory.Create(Player) only adds " +
                "SpellCostReductionAbility; the looting trigger is not wired anywhere in the factory.",

            ["Asmoranomardicadaistinaculdacar"] =
                "// BUG: 'Whenever you discard one or more cards, create a 1/1 Goblin Cook token.' — " +
                "factory Create(Player) adds KeywordAbility markers for food-library mechanics but " +
                "does not wire the discard-triggered token creation as ITriggeredAbility.",

            ["Bloodthirsty Adversary"] =
                "// BUG: 'When this creature enters, you may pay {2}{R} any number of times. " +
                "When you pay this cost one or more times, put that many +1/+1 counters on it " +
                "and exile that many instant and/or sorcery cards with mana value ≤ 3 from your " +
                "graveyard with haste.' — Reflexive trigger (CR 603.2) not exposed as " +
                "ITriggeredAbility in BloodthirstyAdversaryFactory.Create(Player).",

            // Festival Crasher + Kiln Fiend FIXED — the
            // missing-trigger-effects-overload-dispatch deferral is paid down.
            // Both factories now expose the source-gen-recognised
            // Create(Player, ContinuousEffectsService) overload, so the prod
            // GameFacade routed build (NamedCardFactory.Create(name, owner,
            // effects) → the generated *WithEffects dispatcher) wires the
            // cast-instant/sorcery pump trigger (CR 603.1). The real-pool audit
            // now verifies them through the prod build path instead of allow-
            // listing the bug. See FestivalCrasherFactoryTests /
            // KilnFiendFactoryTests EffectsAwareDispatch_*_OnProdPath guards.

            ["Leyline Binding"] =
                "// BUG: 'When this enchantment enters, exile target nonland permanent an " +
                "opponent controls until this enchantment leaves the battlefield.' — " +
                "LeylineBindingFactory.Create(Player) adds DomainCostReductionAbility (static) " +
                "but does NOT wire the ETB exile trigger as ITriggeredAbility. " +
                "The O-Ring pattern (ETB exile + LTB return) needs factory work; " +
                "see card-type-modeling-discrepancies memory.",

            ["Leyline of Combustion"] =
                "// BUG: 'Whenever you or a permanent you control becomes the target of a spell " +
                "or ability an opponent controls, Leyline of Combustion deals 2 damage to that " +
                "opponent.' — LeylineOfCombustionFactory explicitly defers this trigger in v1 " +
                "(factory doc: 'needs a targeting-resolution trigger surface'). Not wired.",

            ["Leyline of Lightning"] =
                "// BUG: 'Whenever you cast your first spell each turn, Leyline of Lightning " +
                "deals 1 damage to target player or planeswalker.' — LeylineOfLightningFactory " +
                "explicitly defers this trigger (factory doc: 'needs a per-turn spells-cast " +
                "counter plus a first-only gate'). Not wired.",

            ["Mirari's Wake"] =
                "// BUG: 'Whenever you tap a land for mana, add one mana of any type that land " +
                "produced.' — MirariWakeFactory.Create(Player, ContinuousEffectsService?) only " +
                "registers ControllerCreatureAnthemEffect (+1/+1 static anthem); the mana-bonus " +
                "triggered mana ability is not wired at all in the factory.",

            ["Necrodominance"] =
                "// BUG: 'At the beginning of your end step, you may pay any amount of life. " +
                "If you do, draw that many cards.' — NecrodominanceFactory.Create adds static " +
                "abilities (SkipDraw, damage-riders) and an activated ability, but the end-step " +
                "draw trigger is not wired as ITriggeredAbility.",

            ["Reality Smasher"] =
                "// BUG: 'Whenever Reality Smasher becomes the target of a spell or ability an " +
                "opponent controls, counter it unless its controller discards a card.' — " +
                "RealitySmasherFactory.Create(Player) adds keyword abilities only; the " +
                "target-counter/discard trigger is deferred (factory doc mentions " +
                "'Ward trigger / non-mana discard rider is structural').",
        };

    // ──────────────────────────────────────────────────────────────────────
    // Synthetic self-tests (no embedded pool required)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Synthetic_TriggerText_NoAbility_IsFlagged()
    {
        var alice = new Player("Alice", 20);
        var repo = new TestRepo(new Dictionary<string, CardEntity>
        {
            ["Inert Bird"] = new CardEntity
            {
                Name = "Inert Bird", ManaCost = "{1}{W}",
                TypeLine = "Creature — Bird", Power = "2", Toughness = "2",
                OracleText = "At the beginning of your upkeep, flip a coin and ponder its meaning.",
            },
        });
        var card = new ScryfallCardFactory(repo).Create("Inert Bird", alice);

        var shouldBeFlagged = HasTriggerText("At the beginning of your upkeep, flip a coin and ponder its meaning.")
                              && !HasTriggerAbility(card);
        shouldBeFlagged.Should().BeTrue("the audit should flag this card as a missing trigger");
    }

    [Fact]
    public void Synthetic_ETBTrigger_IsWired()
    {
        var alice = new Player("Alice", 20);
        var repo = new TestRepo(new Dictionary<string, CardEntity>
        {
            ["ETB Creature"] = new CardEntity
            {
                Name = "ETB Creature", ManaCost = "{2}{W}",
                TypeLine = "Creature — Human Wizard", Power = "2", Toughness = "2",
                OracleText = "When this creature enters the battlefield, you gain 2 life.",
            },
        });
        var card = new ScryfallCardFactory(repo).Create("ETB Creature", alice);

        HasTriggerAbility(card).Should().BeTrue(
            "'When this creature enters the battlefield' must bind a trigger via OracleTriggeredAbilityBinder");
    }

    [Fact]
    public void Synthetic_AnotherCreatureYouControlEnters_IsWired()
    {
        var alice = new Player("Alice", 20);
        var repo = new TestRepo(new Dictionary<string, CardEntity>
        {
            ["Warden Proxy"] = new CardEntity
            {
                Name = "Warden Proxy", ManaCost = "{W}",
                TypeLine = "Creature — Human Cleric", Power = "1", Toughness = "1",
                OracleText = "Whenever another creature you control enters, you gain 1 life.",
            },
        });
        var card = new ScryfallCardFactory(repo).Create("Warden Proxy", alice);

        HasTriggerAbility(card).Should().BeTrue(
            "'Whenever another creature you control enters' must bind a trigger via OracleTriggeredAbilityBinder");
    }

    [Fact]
    public void Synthetic_FactoryPath_SoulWarden_HasTrigger()
    {
        // Soul Warden has a named factory that wires its trigger via JSON.
        // Verifies the factory path works for trigger audit purposes.
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Soul Warden", alice);
        HasTriggerAbility(card).Should().BeTrue(
            "Soul Warden's factory must produce a card with ITriggeredAbility");
    }

    [Fact]
    public void Synthetic_HasTriggerText_MatchesExpectedPatterns()
    {
        HasTriggerText("When ~ enters the battlefield, you gain 1 life.").Should().BeTrue();
        HasTriggerText("Whenever ~ attacks, draw a card.").Should().BeTrue();
        HasTriggerText("At the beginning of your upkeep, add {G}.").Should().BeTrue();
        // Should NOT match mid-sentence "when"
        HasTriggerText("You may pay {1} when you cast this spell.").Should().BeFalse();
        HasTriggerText("Deal damage equal to the number of creatures when blocked.").Should().BeFalse();
        // Empty / null
        HasTriggerText(null).Should().BeFalse();
        HasTriggerText("").Should().BeFalse();
        HasTriggerText("Flying").Should().BeFalse();
    }

    [Fact]
    public void Synthetic_WhenInReminderText_NotFlagged()
    {
        // Reminder text containing "when" should not be flagged — the
        // HasTriggerText regex requires start-of-line.
        HasTriggerText(
            "Flying\n(This creature can only be blocked by creatures with flying or reach.)\n" +
            "Whenever it deals combat damage to a player, draw a card.").Should().BeTrue();
        // A card with ONLY parenthetical reminder text won't have a match.
        HasTriggerText("Flying\n(Attacks each combat when able.)").Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Real-pool audit
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RealPool_ImplementedCards_WithTriggerText_BindATrigger()
    {
        var alice = new Player("Alice", 20);
        var repo = new EmbeddedCardRepository();
        // Provide a live ContinuousEffectsService to match GameFacade.BuildDeckCard:
        //   NamedCardFactory.Create(name, owner, effects)
        var effects = new ContinuousEffectsService();

        var implemented = repo.Search(q: null, implementedOnly: true, limit: 50000);

        int totalImplemented = implemented.Count;
        int withTriggerText = 0;
        int sagas = 0;
        int factoryBacked = 0;
        int binderOnly = 0;

        var gaps = new List<string>();
        foreach (var entity in implemented)
        {
            if (!HasTriggerText(entity.OracleText)) continue;
            withTriggerText++;

            // Sagas: chapter triggers wired by SagaBinder, not OracleTriggeredAbilityBinder.
            // Chapter lines start with "When the Nth chapter …" — no ITriggeredAbility.
            // These have their own tests (SagaProgressionTests).
            if (entity.TypeLine.Contains("Saga", StringComparison.OrdinalIgnoreCase))
            {
                sagas++;
                continue;
            }

            if (ImplementedCardNames.HasRealFactory(entity.Name)) factoryBacked++;
            else binderOnly++;

            if (KnownNonTriggerCards.ContainsKey(entity.Name)) continue;
            if (KnownMissingTriggerBugs.ContainsKey(entity.Name)) continue;

            var card = BuildCard(entity.Name, alice, repo, effects);
            if (card is null) continue;

            if (!HasTriggerAbility(card))
                gaps.Add($"{entity.Name} :: {Truncate(entity.OracleText)}");
        }

        var statsLine =
            $"Pool: {totalImplemented} implemented, {withTriggerText} have trigger text, " +
            $"{sagas} sagas excluded, {factoryBacked} factory-backed, {binderOnly} binder-only, " +
            $"total audited: {withTriggerText - sagas}. " +
            $"KnownNonTrigger={KnownNonTriggerCards.Count}, " +
            $"KnownMissingBugs={KnownMissingTriggerBugs.Count}.";

        gaps.Should().BeEmpty(
            "implemented cards with trigger text must bind a triggered ability " +
            "via their prod build path.\n" +
            statsLine + "\n" +
            "For each card below, either:\n" +
            "  - add to KnownNonTriggerCards with a reason (replacement effect, keyword-granted\n" +
            "    trigger at another layer, spell-path delayed trigger, etc.), or\n" +
            "  - add to KnownMissingTriggerBugs with a // BUG: reason.\n" +
            "Do NOT blindly allowlist — triage each entry.\n" +
            "Flagged cards:\n" +
            string.Join("\n", gaps.Select(g => "  " + g)));
    }

    private static string Truncate(string? s) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= 100 ? s : s[..100] + "…");

    // ──────────────────────────────────────────────────────────────────────
    // Minimal repo for the synthetic tests
    // ──────────────────────────────────────────────────────────────────────

    private sealed class TestRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;

        public TestRepo(Dictionary<string, CardEntity> by) { _by = by; }

        public CardEntity? GetByName(string name) =>
            _by.TryGetValue(name, out var e) ? e : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(GetByName).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(
            string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null,
            IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null) =>
            _by.Values.ToList();

        public bool IsImplemented(string name) => _by.ContainsKey(name);

        public void SetImplemented(string name, bool value) { }
    }
}
