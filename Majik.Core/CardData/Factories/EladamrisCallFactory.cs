using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eladamri's Call (Planeshift / Modern Horizons 2,
/// {G}{W}).
///
/// Instant. Oracle text:
///   "Search your library for a creature card, reveal that card, put it
///    into your hand, then shuffle."
///
/// ## Why it gets its own factory (vs. pure template binding)
/// The "any creature card → hand" predicate matches Eladamri's Call via
/// the existing <see cref="SearchLibraryTemplate"/> ("kind = creature")
/// path — production load through <c>OracleSpellBinder</c> will already
/// produce a working definition. The factory exists so the test /
/// dispatcher path (<see cref="NamedCardFactory.Create"/>) yields a
/// typed <see cref="Instant"/> with the correct printed cost without
/// hitting the DB, and so the spell-definition is reachable via a stable
/// entry point (mirrors <see cref="SylvanScryingFactory"/> /
/// <see cref="MysticalTutorFactory"/>).
///
/// Eladamri's Call is the Bant/Naya creature-toolbox tutor of choice in
/// formats where Modern Horizons 2's reprint legalised it for Modern. {G}{W}
/// at instant speed makes it the premier flash-tutor for Chord-of-Calling
/// shells (Yawgmoth, Bant Spirits, Eldrazi Tron, Humans sideboards) — it
/// finds Stoneforge Mystic, Yawgmoth, Knight of the Reliquary, Elesh
/// Norn, Reflector Mage, etc. and surfaces them in hand rather than
/// onto the battlefield (so the tutored creature can be cast on a future
/// turn, dodging ETB-replacement hate like Containment Priest).
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {G}{W}.
/// - On-resolve effect: prompt the controller's agent (via
///   <see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind
///   <c>"creature"</c>) for a creature card from the library; move pick
///   to hand. No agent registered = deterministic first-match fallback.
///   Empty candidate list or null pick = no-op (CR 701.19a permits
///   declining to find).
/// - CR 701.20a — library is shuffled after the search via the shared
///   <see cref="Majik.Core.Zones.LibraryShuffle"/> helper.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked creature moves Library → Hand
///   without publishing a reveal event; same gap as
///   <see cref="SylvanScryingFactory"/> and the other library-tutor
///   factories.
/// </summary>
[CardName("Eladamri's Call")]
public static class EladamrisCallFactory
{
    public const string CardName = "Eladamri's Call";
    public const string PrintedManaCost = "{G}{W}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Eladamri's Call uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind
    /// <c>"creature"</c> so the engine's creature-predicate, agent
    /// prompt, and pick→hand move are shared with template-bound
    /// creature tutors (Worldly Tutor, Diabolic Tutor variants, …).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLibrarySpell(caster, "creature");
    }
}
