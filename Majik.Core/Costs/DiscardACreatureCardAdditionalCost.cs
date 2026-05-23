using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, discard a creature card." —
/// non-mana additional cost (CR 601.2f / CR 701.16) restricted to creature
/// cards in the caster's hand. Used by Faithless Salvaging's printed
/// flashback rider ("Flashback—Discard a creature card.") and any other
/// "discard a creature card" alt/additional cost shape.
///
/// Sibling of <see cref="SacrificeACreatureAdditionalCost"/> — same v1
/// deterministic-picker pattern (first eligible creature card in hand)
/// because the engine has no agent-side "choose a card to discard" prompt
/// yet (same queue as Liliana of the Veil / Faithless Looting). After
/// payment, <see cref="Discarded"/> exposes the chosen card for downstream
/// effects that reference "the discarded creature".
/// </summary>
public sealed class DiscardACreatureCardAdditionalCost : IAdditionalCost
{
    /// <summary>
    /// The creature card actually discarded once <see cref="Pay"/> has
    /// succeeded. Null before payment.
    /// </summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "discard a creature card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable only if the caster has at least one creature card
    /// in hand. "Creature card" matches any card whose type set includes
    /// <see cref="CardType.Creature"/> (so Artifact Creatures and tribal
    /// instants with the Creature type both qualify — CR 301.1 / CR 302.1).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Hand.GetCards()
            .Any(c => c.HasType(CardType.Creature));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16a — moves the chosen creature card from the caster's hand
    /// to their graveyard. v1 deterministically picks the first creature
    /// card in hand (mirrors <see cref="SacrificeACreatureAdditionalCost"/>
    /// / <see cref="DiscardACardCost"/>). Returns false (no payment) when
    /// no creature card is available.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var pick = caster.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Creature));
        if (pick == null) return false;

        caster.Zones.Hand.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Discarded = pick;
        return true;
    }
}
