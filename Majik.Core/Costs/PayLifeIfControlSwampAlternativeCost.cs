using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "Pay N life rather than pay this spell's mana cost, if you
/// control a [land subtype]." The Snuff Out pattern:
///
///   "If you control a Swamp, you may pay 4 life rather than pay this
///    spell's mana cost."
///
/// This is a pay-life-only alternative cost (no card pitched, no mana
/// paid) gated on the caster controlling at least one land of the required
/// subtype. It composes the existing life-payment + battlefield-subtype
/// primitives — no new engine mechanic.
///
/// Differences vs. <see cref="PitchAlternativeCost"/> / Force of Will:
///   * No card is exiled — the life payment is the entire cost.
///   * The legality predicate is "control a land of <see cref="RequiredSubtype"/>"
///     (CR 118.9 "if you control a Swamp"), checked in <see cref="CanCastFor"/>
///     against the caster's battlefield at announce time. CR 601.3e — if the
///     caster controls no qualifying land, the alternative cost is unavailable.
///   * No mana is paid — <see cref="AlternativeManaCost"/> is
///     <see cref="ManaCost.Zero"/>.
///
/// CR 119.4 — you can't pay life you don't have; <see cref="CanCastFor"/>
/// also gates on <c>LifeTotal &gt;= LifeAmount</c>.
/// </summary>
public sealed class PayLifeIfControlSwampAlternativeCost : IAlternativeCost
{
    /// <summary>The land subtype the caster must control (Swamp for Snuff Out).</summary>
    public CardSubtype RequiredSubtype { get; }

    /// <summary>The amount of life paid in lieu of the mana cost (4 for Snuff Out).</summary>
    public int LifeAmount { get; }

    public string Description =>
        $"Pay {LifeAmount} life (if you control a {RequiredSubtype})";

    /// <summary>No mana is paid — the life payment is the entire cost (CR 118.9).</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public PayLifeIfControlSwampAlternativeCost(int lifeAmount, CardSubtype requiredSubtype = CardSubtype.Swamp)
    {
        if (lifeAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(lifeAmount),
                "Pay-life amount must be non-negative.");
        LifeAmount = lifeAmount;
        RequiredSubtype = requiredSubtype;
    }

    /// <summary>
    /// CR 118.9 — the alternative cost is available only if the caster
    /// controls at least one land with <see cref="RequiredSubtype"/> and has
    /// enough life to pay (CR 119.4). The <paramref name="card"/> parameter
    /// (the spell being cast) is intentionally unused — this alt-cost imposes
    /// no zone restriction on the spell itself.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        if (caster.LifeTotal < LifeAmount) return false;
        return caster.Zones.Battlefield.GetCards()
            .Any(c => c.HasType(CardType.Land) && c.HasSubtype(RequiredSubtype));
    }

    /// <summary>
    /// Pay the life after resolution (CR 118.8). Routes through
    /// <see cref="Player.LoseLife"/> so any life-loss replacement / triggers
    /// fire. Mirrors the life-rider handling in
    /// <see cref="PitchAlternativeCost.OnResolved"/>.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (LifeAmount > 0 && caster != null)
        {
            caster.LoseLife(LifeAmount);
        }
    }
}
