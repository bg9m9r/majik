using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sylvan Scrying (Mirrodin, {1}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for a land card, reveal it, put it into your
///    hand, then shuffle."
///
/// ## Why it gets its own factory (vs. pure template binding)
/// The "any land card" predicate matches Sylvan Scrying via the existing
/// <see cref="SearchLibraryTemplate"/> ("kind = land") path — production
/// load through <c>OracleSpellBinder</c> will already produce a working
/// definition. The factory exists so the test/dispatcher path
/// (<see cref="NamedCardFactory.Create"/>) yields a typed
/// <see cref="Sorcery"/> with the correct printed cost without needing to
/// hit the DB, and so the spell-definition is reachable via a stable
/// entry point (mirrors Treasure Cruise / Rift Bolt / Force of Will).
///
/// Sylvan Scrying tutors ANY land — basic or nonbasic — which is what
/// makes it distinct from Cultivate-style "basic land" tutors. Tron
/// decks rely on it to find Urza's Mine / Tower / Power Plant.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - On-resolve effect: prompt the controller's agent (via
///   <see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind
///   <c>"land"</c>) for a land card from the library; move pick to hand.
///   No agent registered = deterministic first-match fallback.
///   Empty candidate list or null pick = no-op (CR 701.19a permits
///   declining to find).
///
/// ## Reveal (CR 701.18)
/// - The printed "reveal it" step IS surfaced: the shared
///   <see cref="SearchSpellFactory.SearchLibrarySpell"/> publishes a
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> (tagged
///   <see cref="ZoneType.Library"/>) for the found land, so "whenever you
///   reveal a card" payoffs + the portal reveal-flash UI observe it.
/// </summary>
[CardName("Sylvan Scrying")]
public static class SylvanScryingFactory
{
    public const string CardName = "Sylvan Scrying";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Sylvan Scrying uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind
    /// <c>"land"</c> so the engine's land-predicate, agent prompt, and
    /// pick→hand move are shared with template-bound land tutors.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        // CR 701.18 — "reveal it": surface the found land as a public reveal.
        return SearchSpellFactory.SearchLibrarySpell(caster, "land", revealReason: CardName);
    }
}
