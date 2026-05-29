using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Farseek (Champions of Kamigawa, {1}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for a Plains, Island, Swamp, or Mountain card,
///    put it onto the battlefield tapped, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - Resolve effect: tutor a single land carrying one of the four basic
///   land TYPES — Plains, Island, Swamp, or Mountain (CR 305.6) — from the
///   caster's library and put it onto the battlefield <b>tapped</b>, then
///   shuffle. Delegates to
///   <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/> with
///   <c>kindRaw = SearchSpellFactory.PlainsIslandSwampMountainKind</c> and
///   <c>tapped = true</c>.
/// - Key difference vs. <see cref="RampantGrowthFactory"/> (the analogue):
///   Farseek matches by basic land TYPE (the subtype), not by basic-land
///   NAME. So it can fetch a nonbasic dual / shock land / triome that
///   carries one of the four types — but it deliberately can NOT fetch a
///   Forest (the fifth basic land type is excluded by the oracle text).
///   The post-search shuffle (CR 701.20a), the agent search prompt
///   (CR 701.19a), the ZoneService routing (so ETB-tapped replacements and
///   ETB triggers fire), and the printed-"tapped" rider are all shared
///   with the rest of the land-to-battlefield-tapped family.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: same gap as the rest of the search family — the
///   tutored land moves Library → Battlefield without publishing a reveal
///   event.
/// </summary>
[CardName("Farseek")]
public static class FarseekFactory
{
    public const string CardName = "Farseek";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Farseek uses on resolution.
    /// Delegates to <see cref="SearchSpellFactory.SearchLandToBattlefieldSpell"/>
    /// with the Plains/Island/Swamp/Mountain land-type kind and
    /// <c>tapped = true</c>.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchLandToBattlefieldSpell(
            caster, SearchSpellFactory.PlainsIslandSwampMountainKind, tapped: true);
    }
}
