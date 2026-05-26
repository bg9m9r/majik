using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.33 — Kicker discovery probe. Strictly speaking, kicker is
/// an <em>additional</em> cost (CR 601.2f), not an alternative cost
/// (CR 118.9): the printed mana cost is still paid in full, plus the
/// kicker on top. Surfacing it through
/// <see cref="IAlternativeCostProbe"/> therefore yields zero
/// <see cref="IAlternativeCost"/> candidates — the registry slot is
/// re-used as the bot's per-card metadata read for "does this card
/// have a kicker rider?", mirroring
/// <see cref="CascadeAltCostProbe.HasCascade"/>'s
/// discovery-only posture.
///
/// <para>The actual cast wiring for a kicked spell goes through
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> parameter — callers (bot decision layer,
/// scripted tests, UI) layer a <see cref="KickerAdditionalCost"/>
/// onto the cast via <see cref="BuildAdditionalCost(ICard)"/> when
/// they want to pay the kicker. The probe's job here is just to
/// answer "what is this card's kicker mana cost, if any?" without
/// the bot needing to know each kicker-bearing card by name.</para>
///
/// <para>Identification is name-based by default — kicker is parsed
/// on the data side but not yet modelled as a runtime
/// <see cref="Majik.Core.Abilities.KeywordAbility"/> marker on the
/// card (same posture as <see cref="CascadeAltCostProbe"/>'s name
/// list and <see cref="OverloadAltCostProbe"/>'s name list). When
/// a kicker-bearing factory ships, register the card here.</para>
/// </summary>
public sealed class KickerAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, ManaCost?> _lookup;

    public KickerAltCostProbe(Func<ICard, ManaCost?>? lookup = null)
    {
        _lookup = lookup ?? DefaultLookup;
    }

    /// <summary>
    /// Always yields nothing — kicker doesn't replace the spell's mana
    /// cost. See <see cref="KickerCostFor"/> + <see cref="BuildAdditionalCost"/>
    /// for the discovery / construction surface.
    /// </summary>
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
        => Array.Empty<IAlternativeCost>();

    /// <summary>
    /// Discovery query: does <paramref name="card"/> have a printed
    /// kicker rider (CR 702.33)? Returns the kicker mana cost when
    /// yes, or <c>null</c> when no. Pre-filters to the cast-from-hand
    /// posture (kicker is paid at cast announcement, which only
    /// happens when casting the card; from-hand is the canonical
    /// origin for the Modern ship-list).
    /// </summary>
    public ManaCost? KickerCostFor(ICard card, Player caster)
    {
        if (card == null) return null;
        if (caster == null) return null;
        if (card.Zone != ZoneType.Hand) return null;
        if (!ReferenceEquals(card.Owner, caster)) return null;
        return _lookup(card);
    }

    /// <summary>
    /// Construct a <see cref="KickerAdditionalCost"/> for
    /// <paramref name="card"/> if the lookup recognises a kicker
    /// rider, else <c>null</c>. Convenience builder for callers that
    /// have already decided to pay the kicker — equivalent to
    /// <c>KickerCostFor(card, caster).Select(cost =&gt; new KickerAdditionalCost(card, cost))</c>
    /// minus the zone / owner pre-filter (callers passing the card
    /// off-hand can use this for hand-built tests).
    /// </summary>
    public IAdditionalCost? BuildAdditionalCost(ICard card)
    {
        if (card == null) return null;
        var cost = _lookup(card);
        return cost is null ? null : new KickerAdditionalCost(card, cost);
    }

    /// <summary>
    /// Built-in name-based lookup of shipped kicker cards. Burst
    /// Lightning is the canonical ship-list entry today; further
    /// kicker cards (Goblin Bushwhacker, Coiling Oracle's
    /// kicker-bearing cousins, kicker-bearing Modern Horizons reprints
    /// …) get added here as their factories ship. Returns the printed
    /// kicker mana cost on match; <c>null</c> otherwise.
    /// </summary>
    public static ManaCost? DefaultLookup(ICard card)
    {
        if (card == null) return null;
        return card.Name switch
        {
            "Burst Lightning" => ManaCost.Parse("{4}"),
            "Goblin Bushwhacker" => ManaCost.Parse("{R}"),
            "Vines of Vastwood" => ManaCost.Parse("{G}"),
            _ => null,
        };
    }
}
