using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rampant Growth (Tempest, {1}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for a basic land card, put that card onto the
///    battlefield tapped, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - Resolve effect: tutor a single basic land from the caster's library
///   and put it onto the battlefield <b>tapped</b>, then shuffle.
///   Delegates to
///   <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/> with
///   <c>kindRaw = "basic land"</c> and <c>tapped = true</c> so the basic
///   predicate (CR 305.6 basic land names), the agent prompt
///   (CR 701.19a), the ZoneService routing (so ETB-tapped replacements
///   and ETB triggers fire correctly), the printed-"tapped" rider, and
///   the post-search shuffle (CR 701.20a) are all shared with the rest of
///   the basic-land-to-battlefield-tapped family
///   (Cultivate / Kodama's Reach / Search for Tomorrow's untapped sibling).
/// - Mirrors <see cref="SearchForTomorrowFactory"/>'s shape — the only
///   functional difference vs. Search for Tomorrow is the printed
///   <c>tapped</c> rider and the absence of Suspend.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: same gap as
///   <see cref="SylvanScryingFactory"/> / <see cref="SearchForTomorrowFactory"/>
///   — the tutored basic moves Library → Battlefield without publishing
///   a reveal event.
/// </summary>
[CardName("Rampant Growth")]
public static class RampantGrowthFactory
{
    public const string CardName = "Rampant Growth";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Rampant Growth uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/> with
    /// kind <c>"basic land"</c> and <c>tapped = true</c>.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLandToBattlefieldSpell(caster, "basic land", tapped: true);
    }
}
