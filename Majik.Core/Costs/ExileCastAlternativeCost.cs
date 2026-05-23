using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — alternative cost for "you may cast that card" effects that
/// stamp a runtime permission on a card in exile and explicitly nominate
/// the player who may cast it (e.g. Ragavan, Nimble Pilferer's combat
/// trigger exiles the damaged player's top card and lets the Ragavan
/// controller cast it until end of turn).
///
/// Unlike <see cref="CastFromExileAlternativeCost"/>, which requires
/// <c>card.Owner == caster</c> (Suspend / Cascade exile the caster's own
/// cards), this variant reads <see cref="Card.RuntimeExileCastAllowedCaster"/>
/// and permits exactly that nominated player to cast — typically not the
/// card's owner. The runtime stamp is cleared at end of turn by the
/// granting effect's own bookkeeping (same EOT pattern as Snapcaster
/// Mage's flashback grant).
/// </summary>
public sealed class ExileCastAlternativeCost : IAlternativeCost
{
    public string Description { get; }
    public ManaCost AlternativeManaCost { get; }

    public ExileCastAlternativeCost(string description, ManaCost cost)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    /// <summary>
    /// Legal iff the card is in exile and a runtime exile-cast grant
    /// nominates <paramref name="caster"/>. The grant is the source of
    /// truth — if the EOT cleanup has cleared it, this returns false even
    /// while the card is still in exile.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Exile) return false;
        if (card is not Card concrete) return false;
        if (concrete.RuntimeExileCastAllowedCaster == null) return false;
        return ReferenceEquals(concrete.RuntimeExileCastAllowedCaster, caster);
    }

    /// <summary>
    /// CR 118.9 — the spell resolves into its default destination. The
    /// runtime stamp is cleared by the granting effect's EOT subscription,
    /// not here, so the card's transient "may be cast" window outlives a
    /// failed cast attempt.
    /// </summary>
    public void OnResolved(ICard card, Player caster) { /* default destination */ }
}
