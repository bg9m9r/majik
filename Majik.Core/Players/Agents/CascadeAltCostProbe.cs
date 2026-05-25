using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.85 — Cascade. Strictly speaking, cascade is a triggered ability
/// that fires when the cascade-bearing spell is cast, NOT an alternative
/// cost (CR 118.9). Casting a cascade card costs its printed mana cost.
///
/// <para>This probe therefore yields zero
/// <see cref="IAlternativeCost"/> candidates — there's no cheaper cost path
/// to bid. Its real job is to expose a <see cref="HasCascade"/> discovery
/// surface the bot's bidding layer can read for value scoring (a 4-drop
/// with cascade is worth more than a vanilla 4-drop because cascade
/// effectively gives a free spell). Hooking the registry with this probe
/// makes that bot-side reading a one-call lookup; downstream callers (the
/// bot's bidding heuristic, the UI, future EV-search policy layers) can
/// query it without re-introspecting every card's triggered abilities.</para>
///
/// <para>The cascade-resolution free-cast (CR 702.85a — "you may cast that
/// spell without paying its mana cost") is driven separately by the
/// cascading spell's trigger via
/// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> on the
/// eligible exile card; that's a different surface (cast-from-exile at
/// trigger-resolution time) than what this probe enumerates (hand cards
/// the caster could cast right now).</para>
///
/// <para>Identification is name-based by default — cascade isn't currently
/// modeled as a <see cref="Majik.Core.Abilities.KeywordAbility"/> marker
/// (Crashing Footfalls / Living End wire it as a
/// <see cref="Majik.Core.Abilities.TriggeredAbility"/> over
/// <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/>), so a name
/// list is the deterministic discovery seam. Callers can supply a custom
/// lookup if they have a richer per-card metadata source.</para>
/// </summary>
public sealed class CascadeAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, bool> _isCascadeCard;

    public CascadeAltCostProbe(Func<ICard, bool>? isCascadeCard = null)
    {
        _isCascadeCard = isCascadeCard ?? DefaultIsCascadeCard;
    }

    /// <summary>
    /// Always yields nothing — cascade doesn't change the spell's mana
    /// cost. See <see cref="HasCascade"/> for the discovery query.
    /// </summary>
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
        => Array.Empty<IAlternativeCost>();

    /// <summary>
    /// Discovery query: does <paramref name="card"/> have the Cascade
    /// keyword (CR 702.85)? Used by the bot's bidding layer to weight
    /// cascade spells higher.
    /// </summary>
    public bool HasCascade(ICard card) => _isCascadeCard(card);

    /// <summary>
    /// Built-in name-based lookup for shipped cascade cards. Crashing
    /// Footfalls, Living End, and Shardless Agent are the current ship
    /// list; when Bloodbraid Elf / Violent Outburst etc. land, add them
    /// here.
    /// </summary>
    public static bool DefaultIsCascadeCard(ICard card)
    {
        if (card == null) return false;
        return card.Name switch
        {
            "Crashing Footfalls" => true,
            "Living End" => true,
            "Shardless Agent" => true,
            _ => false,
        };
    }
}
