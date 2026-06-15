using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fabricate (Mirrodin, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Search your library for an artifact card, reveal it, put it into your
///    hand, then shuffle."
///
/// ## Why it gets its own factory
/// Identical tutor-to-hand shape to <see cref="SylvanScryingFactory"/> — only
/// the predicate differs (artifact instead of land). The factory yields a
/// typed <see cref="Sorcery"/> with the correct printed cost via the
/// dispatcher path and reuses the engine's shared search resolution
/// (<see cref="SearchSpellFactory.SearchLibrarySpell"/> with kind
/// <c>"artifact"</c>), so the artifact-predicate, agent prompt, and
/// pick→hand move are shared with all other artifact tutors.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}.
/// - On-resolve effect: prompt the controller's agent for an artifact card
///   from the library; move the pick to hand. No agent registered =
///   deterministic first-match fallback. Empty candidate list or null pick =
///   no-op (CR 701.19a permits declining to find). Library is shuffled
///   afterward (CR 701.20a).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked artifact moves Library → Hand without
///   publishing a reveal event; same gap as Sylvan Scrying's tutor.
/// </summary>
[CardName("Fabricate")]
public static class FabricateFactory
{
    public const string CardName = "Fabricate";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Fabricate uses on resolution.
    /// Delegates to <see cref="SearchSpellFactory.SearchLibrarySpell"/> with
    /// kind <c>"artifact"</c> so the engine's artifact-predicate, agent
    /// prompt, and pick→hand move are shared with template-bound artifact
    /// tutors.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLibrarySpell(caster, "artifact");
    }
}
