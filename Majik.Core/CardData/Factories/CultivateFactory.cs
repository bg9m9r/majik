using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cultivate (Magic 2010, {2}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for up to two basic land cards, reveal those
///    cards, put one onto the battlefield tapped and the other into your
///    hand, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{G}.
/// - Resolve effect: up to two basic-land picks from the caster's
///   library; first pick goes to the battlefield tapped, second pick
///   goes to hand, single shuffle at end. Delegates to
///   <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>
///   so the predicate (CR 305.6 basic-land names), per-pick agent
///   prompt (CR 701.19a), ZoneService routing (ETB-tapped replacements
///   + ETB triggers fire on the tutored land), printed-"tapped" rider,
///   and post-search shuffle (CR 701.20a) are shared with
///   <see cref="KodamasReachFactory"/>.
/// - Cultivate is a functional reprint of
///   <see cref="KodamasReachFactory"/> — the two factories share the
///   same spell-definition helper. The only printed difference is that
///   Kodama's Reach has the Arcane subtype (CR 205.3k) for
///   Splice onto Arcane targeting.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: same gap as
///   <see cref="SylvanScryingFactory"/> / <see cref="RampantGrowthFactory"/>
///   — the picks move Library → Battlefield/Hand without publishing
///   a reveal event.
/// - <b>Player choice between the two picks</b>: the helper currently
///   asks the agent to pick the "battlefield" card first, then the
///   "hand" card — the agent doesn't get a second decision to swap
///   them after seeing both. CR 701.19a permits the spell to read as
///   one search that finds two cards then partitions, but the
///   observable behaviour is identical when the agent's scoring
///   reflects the prompt label ("...to put onto the battlefield
///   tapped" vs. "...to put into your hand"). Future work: a single
///   ChooseLibraryPicks multi-target prompt that lets the agent
///   designate the partition.
/// </summary>
[CardName("Cultivate")]
public static class CultivateFactory
{
    public const string CardName = "Cultivate";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Cultivate uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell(caster, CardName);
    }
}
