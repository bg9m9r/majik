using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, discard a card." — the plain,
/// single-card discard additional cost (CR 601.2f). Printed verbatim on
/// Wild Guess ({R}{R}, Sorcery) and Tormenting Voice's instant-coloured
/// kin.
///
/// This is the non-disjunctive sibling of
/// <see cref="DiscardACardOrPayLifeAdditionalCost"/> (Bitter Triumph) and
/// <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/> (Bone Shards):
/// there is only one payment mode, so no choice is involved. It is the
/// spell-cast (<see cref="IAdditionalCost"/>) analogue of
/// <see cref="DiscardACardCost"/> (the activated-ability <see cref="ICost"/>
/// version) — same "discard one card from hand" mechanic, different cost
/// surface. No new engine mechanic; this composes the existing
/// <see cref="IAdditionalCost"/> contract.
///
/// ## v1 picker policy
/// <see cref="Target"/> may be pre-set by the agent to nominate which card
/// to discard. When null, <see cref="Pay"/> deterministically discards the
/// first card in the caster's hand — the same v1 picker policy used by
/// <see cref="DiscardACardCost"/> and the disjunctive discard costs. Full
/// agent-driven discard prompting is deferred behind the same queue as
/// Liliana of the Veil + Faithless Looting.
///
/// ## Empty hand → uncastable (CR 601.2g)
/// "Discard a card" is mandatory, not "may". With an empty hand the cost
/// cannot be paid, so <see cref="CanPay"/> is false and the cast flow's
/// pre-check rejects the spell (CR 601.2g — an additional cost that can't
/// be paid makes the cast illegal).
/// </summary>
public sealed class DiscardACardAdditionalCost : IAdditionalCost
{
    /// <summary>
    /// Optionally set by the agent to nominate which card to discard. When
    /// null the cost falls back to the first card in the caster's hand
    /// (deterministic v1 behaviour). When non-null, the card must be in the
    /// caster's hand at <see cref="Pay"/> time.
    /// </summary>
    public ICard? Target { get; set; }

    /// <summary>The card actually discarded by <see cref="Pay"/>. Null until
    /// payment succeeds.</summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "discard a card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable only if the caster has at least one card in hand
    /// (and, when a <see cref="Target"/> is nominated, that card is actually
    /// in the caster's hand).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        if (Target != null)
            return caster.Zones.Hand.ContainsCard(Target);
        return caster.Zones.Hand.GetCards().Any();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16a — discard moves the chosen card from the caster's hand to
    /// their graveyard. Returns false (rather than throwing) when the cost
    /// cannot be paid, matching the <see cref="IAdditionalCost"/> contract.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        var pick = Target ?? caster.Zones.Hand.GetCards().FirstOrDefault();
        if (pick == null) return false;
        if (!caster.Zones.Hand.ContainsCard(pick)) return false;

        caster.Zones.Hand.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Discarded = pick;
        return true;
    }
}
