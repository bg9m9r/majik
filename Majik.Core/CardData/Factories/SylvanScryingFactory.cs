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
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b> (CR 701.19c). Same rationale as the rest of
///   <see cref="SearchSpellFactory"/> — no IZone.Shuffle entry point yet;
///   GameDriver owns shuffle. The reveal-then-shuffle clause is a no-op
///   here.
/// - <b>Reveal event</b>. The picked land moves Library → Hand without
///   publishing a reveal event; same gap as Stoneforge Mystic's ETB tutor.
/// </summary>
public static class SylvanScryingFactory
{
    public const string CardName = "Sylvan Scrying";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>
    /// Build a Sylvan Scrying sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

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
        return SearchSpellFactory.SearchLibrarySpell(caster, "land");
    }
}
