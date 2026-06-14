using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.94 — Miracle. A miracle card may be cast for its miracle cost
/// (CR 118.9 alternative cost) from the HAND, instead of its printed mana
/// cost, while the temporary "you may cast this card" window from the
/// first-card-drawn-this-turn reveal is open (CR 702.94b–c).
///
/// <para>The window is represented by a runtime grant stamped on the card
/// (<see cref="Card.RuntimeMiracleCost"/>) by the draw hook that fires when
/// the card is the first card its controller drew this turn. Unlike
/// Flashback (cast from graveyard, exiles on resolution), Miracle is cast
/// from the hand and the resolved card follows its printed-type default
/// destination (CR 608.2 — an instant/sorcery cast for its miracle cost
/// still goes to the graveyard, a permanent enters the battlefield).</para>
///
/// <para><b>One-shot window.</b> <see cref="OnResolved"/> clears the runtime
/// grant so the miracle window does not survive the cast. The grant is also
/// cleared by end-of-turn cleanup (the draw hook's bookkeeping) if the
/// window lapses unused, matching the "next time you would receive priority"
/// scope of CR 702.94c at the granularity this engine models.</para>
///
/// Mirrors <see cref="FlashbackAlternativeCost"/> (zone-restricted, owner-
/// gated, with a post-resolution side-effect) but the legal zone is the hand
/// and the side-effect is a grant clear rather than an exile.
/// </summary>
public sealed class MiracleAlternativeCost : IAlternativeCost
{
    public string Description => $"Miracle {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }

    public MiracleAlternativeCost(ManaCost miracleCost)
    {
        AlternativeManaCost = miracleCost ?? throw new ArgumentNullException(nameof(miracleCost));
    }

    /// <summary>
    /// Legal iff the card is in the caster's hand (CR 702.94a) and a runtime
    /// miracle grant is currently stamped (the reveal window is open,
    /// CR 702.94b). The grant is the source of truth — once the window has
    /// been cleared (cast or EOT lapse) this returns false even while the
    /// card is still in hand.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        if (card is not Card concrete) return false;
        return concrete.RuntimeMiracleCost != null;
    }

    /// <summary>
    /// CR 702.94 — after the spell resolves (into its printed-type default
    /// destination), clear the one-shot miracle window so it cannot be reused.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (card is Card concrete)
        {
            concrete.ClearRuntimeMiracle();
        }
    }
}
