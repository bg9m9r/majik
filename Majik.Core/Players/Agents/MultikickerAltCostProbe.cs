using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.32 — Multikicker discovery probe. Multikicker is a kicker
/// (CR 702.33) that "may be paid any number of times" as the spell is cast;
/// like kicker it is an <em>additional</em> cost (CR 601.2f), not an
/// alternative cost (CR 118.9), so — exactly like
/// <see cref="KickerAltCostProbe"/> — surfacing it through
/// <see cref="IAlternativeCostProbe"/> yields zero
/// <see cref="IAlternativeCost"/> candidates. The registry slot is re-used as
/// the bot's per-card metadata read: "does this card have a multikicker
/// rider, and what is the per-kick cost?"
///
/// <para>The bot pairs this discovery query with
/// <c>Majik.Bot.Heuristic.MultikickerPolicy.ChooseTimes</c> (the
/// how-many-times heuristic) and then layers a
/// <see cref="MultikickerAdditionalCost"/> onto the cast via
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
/// parameter / <see cref="PriorityAction.CastSpell.AdditionalCosts"/> — the
/// same additional-cost rail kicker rides. The probe's job is just to answer
/// "what is this card's per-kick cost, if any?" without the bot needing to
/// know each multikicker-bearing card by name.</para>
///
/// <para>Identification is name-based by default (same posture as
/// <see cref="KickerAltCostProbe.DefaultLookup"/> / the cascade + overload
/// name lists) — multikicker is parsed on the data side but not yet modelled
/// as a runtime keyword marker on the card. Everflowing Chalice is the
/// canonical entry; further Multikicker cards register here as their factories
/// ship.</para>
/// </summary>
public sealed class MultikickerAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, ManaCost?> _lookup;

    public MultikickerAltCostProbe(Func<ICard, ManaCost?>? lookup = null)
    {
        _lookup = lookup ?? DefaultLookup;
    }

    /// <summary>
    /// Always yields nothing — multikicker doesn't replace the spell's mana
    /// cost. See <see cref="MultikickerCostFor"/> +
    /// <see cref="BuildAdditionalCost"/> for the discovery / construction
    /// surface.
    /// </summary>
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
        => Array.Empty<IAlternativeCost>();

    /// <summary>
    /// Discovery query: does <paramref name="card"/> have a printed
    /// multikicker rider (CR 702.32)? Returns the per-kick mana cost when yes,
    /// or <c>null</c> when no. Pre-filters to the cast-from-hand posture —
    /// multikicker is chosen at cast announcement (CR 601.2b), which only
    /// happens when casting the card from where it's held; from-hand is the
    /// canonical origin for the Modern ship-list.
    /// </summary>
    public ManaCost? MultikickerCostFor(ICard card, Player caster)
    {
        if (card == null) return null;
        if (caster == null) return null;
        if (card.Zone != ZoneType.Hand) return null;
        if (!ReferenceEquals(card.Owner, caster)) return null;
        return _lookup(card);
    }

    /// <summary>
    /// Construct a <see cref="MultikickerAdditionalCost"/> for
    /// <paramref name="card"/> paid <paramref name="times"/> times if the
    /// lookup recognises a multikicker rider, else <c>null</c>. Convenience
    /// builder for callers that have already run
    /// <c>Majik.Bot.Heuristic.MultikickerPolicy.ChooseTimes</c> — minus
    /// the zone / owner pre-filter so hand-built tests can use it off-zone.
    /// </summary>
    public IAdditionalCost? BuildAdditionalCost(ICard card, int times)
    {
        if (card == null) return null;
        var cost = _lookup(card);
        return cost is null ? null : new MultikickerAdditionalCost(card, cost, times);
    }

    /// <summary>
    /// Built-in name-based lookup of shipped Multikicker cards. Everflowing
    /// Chalice ({2} per kick) is the canonical ship-list entry; further
    /// Multikicker cards get added here as their factories ship. Returns the
    /// printed per-kick mana cost on match; <c>null</c> otherwise.
    /// </summary>
    public static ManaCost? DefaultLookup(ICard card)
    {
        if (card == null) return null;
        return card.Name switch
        {
            "Everflowing Chalice" => ManaCost.Parse("{2}"),
            _ => null,
        };
    }
}
