using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 601.3e — alternative cost for "you may cast that card from [a graveyard
/// you don't own]" effects that stamp a runtime permission on a card in a
/// graveyard and explicitly nominate the player who may cast it (Tinybones,
/// the Pickpocket: "you may cast target nonland permanent card from that
/// player's graveyard").
///
/// <para>
/// This is the graveyard mirror of <see cref="ExileCastAlternativeCost"/>
/// (the Ragavan exile analogue). Unlike <see cref="FlashbackAlternativeCost"/>,
/// <see cref="GraveyardCastAlternativeCost"/>, and
/// <see cref="EscapeAlternativeCost"/> — all of which require
/// <c>card.Owner == caster</c> (you cast from YOUR OWN graveyard) — this
/// variant reads <see cref="Card.RuntimeGraveyardNonOwnerCastAllowedCaster"/> and
/// permits exactly that nominated player to cast, who is typically NOT the
/// card's owner. The card stays in its owner's graveyard until it goes to the
/// stack; on resolution it follows the normal destination (a nonland permanent
/// enters the battlefield under the caster's control per CR 110.2, then later
/// dies to its owner's graveyard per CR 400.3).
/// </para>
///
/// <para>
/// CR 601.3e "mana of any type can be spent": the granting effect converts the
/// printed cost to an all-generic cost of equal mana value before stamping the
/// grant (generic mana accepts mana of any type), so this alt-cost only needs
/// to carry the resulting <see cref="AlternativeManaCost"/>.
/// </para>
///
/// The runtime stamp is cleared at end of turn by the granting effect's own
/// bookkeeping (same EOT pattern as Ragavan's exile-cast grant), so the
/// transient "may be cast" window outlives a failed cast attempt.
/// </summary>
public sealed class GraveyardNonOwnerCastAlternativeCost : IAlternativeCost
{
    public string Description { get; }
    public ManaCost AlternativeManaCost { get; }

    public GraveyardNonOwnerCastAlternativeCost(string description, ManaCost cost)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    /// <summary>
    /// Legal iff the card is in a graveyard and a runtime non-owner
    /// graveyard-cast grant nominates <paramref name="caster"/>. The grant is
    /// the source of truth — if the EOT cleanup has cleared it, this returns
    /// false even while the card is still in the graveyard. Crucially this does
    /// NOT require <c>card.Owner == caster</c> (CR 601.3e — a different player
    /// is granted permission to cast a card they don't own).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Graveyard) return false;
        if (card is not Card concrete) return false;
        if (concrete.RuntimeGraveyardNonOwnerCastAllowedCaster == null) return false;
        return ReferenceEquals(concrete.RuntimeGraveyardNonOwnerCastAllowedCaster, caster);
    }

    /// <summary>
    /// CR 601.3e — the spell resolves into its default destination (a nonland
    /// permanent enters the battlefield under the caster's control). The
    /// runtime stamp is cleared by the granting effect's EOT subscription, not
    /// here.
    /// </summary>
    public void OnResolved(ICard card, Player caster) { /* default destination */ }
}
