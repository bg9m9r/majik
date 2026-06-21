using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.102 — Fuse. A split card with Fuse may be cast as a single spell
/// that is BOTH halves, paying "the combined mana cost of both halves"
/// (CR 702.102b) INSTEAD of either half's individual cost. This is an
/// alternative cost (CR 118.9): it replaces the printed (front-half) cost the
/// combined split object carries with the field-wise SUM of both halves'
/// printed costs.
///
/// <para>Fuse may only be used to cast the split card from the player's HAND
/// (CR 702.102a). The companion fused <see cref="Game.SpellDefinition"/>
/// (built by <see cref="Game.SplitCardCast.BuildFusedDefinition"/>) supplies
/// the both-halves effect + two-half target collection; this class is purely
/// the cost gate, mirroring <see cref="OverloadAlternativeCost"/> (an alt cost
/// that swaps in a different mana cost and toggles the effect branch).</para>
///
/// <para>A fused instant/sorcery split card resolves and goes to the graveyard
/// like any other (CR 712.6 — a split card is one card), so no post-resolution
/// zone override is needed.</para>
/// </summary>
public sealed class FuseAlternativeCost : IAlternativeCost
{
    public string Description => $"Fuse {AlternativeManaCost}";

    /// <summary>CR 702.102b — the combined mana cost of both halves.</summary>
    public ManaCost AlternativeManaCost { get; }

    public FuseAlternativeCost(ManaCost combinedCost)
    {
        AlternativeManaCost = combinedCost ?? throw new ArgumentNullException(nameof(combinedCost));
    }

    /// <summary>CR 702.102a — Fuse may only be used from a player's hand.</summary>
    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Hand && ReferenceEquals(card.Owner, caster);

    /// <summary>
    /// CR 712.6 — a split card is a single card; after resolving it heads to the
    /// graveyard via the printed-type default (instants/sorceries → graveyard).
    /// No side-effect to apply.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        // No-op — combined cost is paid at cast; resolution destination is the
        // printed-type default.
    }
}
