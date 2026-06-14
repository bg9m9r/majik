using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flare of Cultivation (Modern Horizons 3, {1}{G}{G}).
///
/// Sorcery. Oracle text:
///   "You may sacrifice a nontoken green creature rather than pay this spell's
///    mana cost.
///    Search your library for up to two basic land cards, reveal those cards,
///    put one onto the battlefield tapped and the other into your hand, then
///    shuffle."
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost {1}{G}{G}, green identity (MV 3).
/// - NamedCardFactory dispatch via <c>[CardName]</c> source-generator.
/// - Resolve effect: identical to <see cref="CultivateFactory"/> — up to two
///   basic-land picks from the caster's library; first pick goes to the
///   battlefield tapped, second pick goes to hand, single shuffle at end.
///   Delegates to
///   <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>
///   so the predicate (CR 305.6 basic-land names), per-pick agent prompt
///   (CR 701.19a), ZoneService routing, and post-search shuffle (CR 701.20a)
///   are shared with <see cref="CultivateFactory"/> / <see cref="KodamasReachFactory"/>.
/// - Alternative cost (<see cref="SacrificeNontokenGreenCreatureAlternativeCost"/>):
///   the caster may sacrifice a nontoken green creature they control on the
///   battlefield instead of paying {1}{G}{G}. Recolored sibling of Flare of
///   Denial's blue alt cost. No printed timing restriction (CR 118.9).
///
/// Bot probe: <see cref="FlareOfCultivationAltCostProbe"/> surfaces sacrifice
/// candidates for the heuristic bot agent; to be wired into
/// <see cref="Majik.Core.Players.Agents.AlternativeCostProbeRegistry.CreateDefault"/>
/// alongside the rest of the Flare cycle when the alt-cost probes are
/// registered (the cycle's probes are not yet registered there — same posture
/// as <see cref="FlareOfDenialAltCostProbe"/>).
///
/// CR citations:
///   CR 118.9   — alternative cost
///   CR 701.18  — sacrifice
///   CR 701.19a — search a library
///   CR 701.20a — post-search shuffle
///
/// ## Deferred (v1 gaps)
/// - Same as <see cref="CultivateFactory"/>: no reveal event published; the
///   agent picks the battlefield card first then the hand card without a
///   second swap decision.
/// </summary>
[CardName("Flare of Cultivation")]
public static class FlareOfCultivationFactory
{
    public const string CardName = "Flare of Cultivation";
    public const string PrintedManaCost = "{1}{G}{G}";

    /// <summary>CardDef DSL — card shape only. Resolve-time tutor body is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Flare of Cultivation uses on
    /// resolution. Delegates to
    /// <see cref="SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell"/>
    /// — identical to <see cref="CultivateFactory.BuildSpellDefinition"/>.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SearchSpellFactory.SearchUpToTwoBasicsBattlefieldAndHandSpell(caster, CardName);
    }
}

/// <summary>
/// Bot probe: surfaces <see cref="SacrificeNontokenGreenCreatureAlternativeCost"/>
/// candidates for Flare of Cultivation during the heuristic bot's spell-cast
/// enumeration.
///
/// For each nontoken green creature the caster controls on the battlefield,
/// yields one <see cref="SacrificeNontokenGreenCreatureAlternativeCost"/>
/// instance (one per eligible sacrifice candidate). Recolored sibling of
/// <see cref="FlareOfDenialAltCostProbe"/>. No timing restriction is emitted.
/// </summary>
public sealed class FlareOfCultivationAltCostProbe : Majik.Core.Players.Agents.IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Name != FlareOfCultivationFactory.CardName) yield break;
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        foreach (var battlefield in caster.Zones.Battlefield.GetCards())
        {
            if (battlefield is not Permanent perm) continue;
            if (!perm.HasType(CardType.Creature)) continue;
            if (perm.IsToken) continue;
            if (!CardColors.GetColors(perm).Contains(ManaColor.Green)) continue;
            yield return new SacrificeNontokenGreenCreatureAlternativeCost(perm);
        }
    }
}
