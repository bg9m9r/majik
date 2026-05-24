using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.96 — Overload. Surfaces <see cref="OverloadAlternativeCost"/>
/// candidates for the bot's spell-cast enumeration. An overload spell is
/// identified by a lookup delegate that returns the printed overload mana
/// cost for a given card name; the probe's
/// <see cref="DefaultLookup"/> covers the ship-list of named overload cards
/// (Mizzium Mortars today; future overload imports register here).
///
/// <para>Probe-level filtering:
///   * Only emits from-hand candidates owned by the caster (matches
///     <see cref="OverloadAlternativeCost.CanCastFor"/>).
///   * Skips when the lookup returns null (card has no overload cost).</para>
///
/// <para>The bot then calls
/// <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/> + checks mana
/// availability via its existing source-picking heuristic, so the probe
/// is the pre-filter, not the source of truth.</para>
/// </summary>
public sealed class OverloadAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, ManaCost?> _lookup;

    public OverloadAltCostProbe(Func<ICard, ManaCost?>? lookup = null)
    {
        _lookup = lookup ?? DefaultLookup;
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var overloadCost = _lookup(card);
        if (overloadCost is null) yield break;

        yield return new OverloadAlternativeCost(overloadCost);
    }

    /// <summary>
    /// Built-in lookup of named overload cards. Mizzium Mortars (RTR,
    /// printed {1}{R} / overload {4}{R}{R}) is the canonical ship-list
    /// entry; further overload cards (Vandalblast, Hypersonic Dragon...)
    /// can be added here as their factories ship.
    /// </summary>
    public static ManaCost? DefaultLookup(ICard card)
    {
        if (card == null) return null;
        return card.Name switch
        {
            "Mizzium Mortars" => ManaCost.Parse("{4}{R}{R}"),
            _ => null,
        };
    }
}
