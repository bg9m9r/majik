using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, discard X cards." — Nahiri's
/// Wrath (Eldritch Moon, {4}{R}{R}). Variable-count discard additional
/// cost (CR 601.2f) where the value of <c>X</c> is the count of cards the
/// caster chooses to discard at announcement.
///
/// Unlike <see cref="DiscardACardCost"/> (single, ability cost) and
/// <see cref="DiscardACreatureCardAdditionalCost"/> (single creature-card,
/// spell cost), this cost discards a caster-chosen multiset of cards from
/// hand and remembers the discarded set. Downstream resolution can read
/// <see cref="Discarded"/> to compute totals (mana-value sum for Nahiri's
/// Wrath; matches the printed "equal to the total mana value of the
/// discarded cards" rider).
///
/// ## v1 picker policy
/// <see cref="Targets"/> may be pre-set by the agent to nominate which
/// cards to discard (and implicitly choose <c>X</c>). When null,
/// <see cref="Pay"/> falls back to discarding the caster's entire hand —
/// matching the v1 picker policy used by
/// <see cref="DiscardACardCost"/> (deterministic first-eligible) extended
/// to a multiset. Tests and bots that want a specific subset set
/// <see cref="Targets"/> before resolving the cast.
///
/// ## Cardinality (X)
/// <see cref="CanPay"/> always returns <c>true</c> — X may legally be
/// zero (discard zero cards = no-op, total mana value 0 → all damage
/// dealings on resolution are zero, but the spell still resolves). When
/// <see cref="Targets"/> is set, every nominated card must be in the
/// caster's hand or the cost is illegal (CR 117.1).
/// </summary>
public sealed class DiscardXCardsAdditionalCost : IAdditionalCost
{
    /// <summary>
    /// Optional pre-supplied set of cards to discard. When null, all cards
    /// currently in the caster's hand are discarded (the v1 default —
    /// matches the picker policy used by <see cref="DiscardACardCost"/>).
    /// When non-null, every card must be in the caster's hand at
    /// <see cref="Pay"/> time.
    /// </summary>
    public IReadOnlyList<ICard>? Targets { get; set; }

    /// <summary>
    /// Cards actually discarded by <see cref="Pay"/>. Empty until
    /// payment. Downstream effect closures read this to compute
    /// totals (Nahiri's Wrath sums <c>card.ManaCostValue.TotalValue</c>
    /// across this list).
    /// </summary>
    public IReadOnlyList<ICard> Discarded { get; private set; } = Array.Empty<ICard>();

    /// <inheritdoc/>
    public string Description => "discard X cards";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — the cost is always payable (X = 0 is legal). When
    /// <see cref="Targets"/> is supplied, each nominated card must be in
    /// the caster's hand.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        if (Targets == null) return true;
        foreach (var c in Targets)
        {
            if (c == null) return false;
            if (!caster.Zones.Hand.ContainsCard(c)) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16 — discard moves each chosen card from hand to graveyard.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        if (!CanPay(caster)) return false;

        var picks = Targets != null
            ? Targets.ToList()
            : caster.Zones.Hand.GetCards().ToList();

        var discarded = new List<ICard>(picks.Count);
        foreach (var pick in picks)
        {
            if (!caster.Zones.Hand.ContainsCard(pick)) continue;
            caster.Zones.Hand.RemoveCard(pick);
            caster.Zones.Graveyard.AddCard(pick);
            // Zone.AddCard sets card.Zone — no manual SetZone needed.
            discarded.Add(pick);
        }

        Discarded = discarded;
        return true;
    }
}
