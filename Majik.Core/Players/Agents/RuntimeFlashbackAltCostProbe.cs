using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.34 / CR 118.9 — surfaces flashback alt-cost candidates that come
/// from a RUNTIME grant (e.g. Snapcaster Mage's ETB stamps
/// <see cref="Card.RuntimeFlashbackCost"/> on a target instant/sorcery in
/// the controller's graveyard until end of turn).
///
/// Without this probe, the heuristic bot's CR 118.9 enumeration only sees
/// printed alt costs and skips Snapcaster-granted flashback, even though
/// the spell-cast pipeline would accept it. Plugging this probe into
/// <see cref="HeuristicBotAgent"/> closes the gap — when a card in the
/// caster's graveyard carries a non-null <see cref="Card.RuntimeFlashbackCost"/>,
/// the probe yields a <see cref="FlashbackAlternativeCost"/> built from
/// that stamped cost. The bot then bids it like any other alt cost.
///
/// Composable with future printed-flashback / spectacle / evoke probes via
/// <see cref="CompositeAlternativeCostProbe"/> — keep this probe focused on
/// the runtime-grant flag so the source of truth (the <c>RuntimeFlashbackCost</c>
/// property on <see cref="Card"/>) stays one-to-one with the probe that
/// reads it.
/// </summary>
public sealed class RuntimeFlashbackAltCostProbe : IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // Only Card carries the runtime flag (the interface ICard doesn't
        // expose it — the grant is a concrete-card mutable surface set by
        // SnapcasterMageFactory's ETB effect). Probes that get an ICard
        // backing instance other than Card silently yield nothing.
        if (card is not Card concrete) yield break;

        // Runtime flashback is meaningful only from the graveyard (CR 702.34
        // — flashback is cast from the graveyard). If the card has been
        // moved out of the graveyard the grant is stale; let
        // FlashbackAlternativeCost.CanCastFor reject in that case rather
        // than re-checking here so the bot sees a consistent candidate
        // shape, but skip emission when the flag isn't set at all.
        var cost = concrete.RuntimeFlashbackCost;
        if (cost is null) yield break;

        // Owner gate matches FlashbackAlternativeCost.CanCastFor — alt cost
        // is castable only by the card's owner. Pre-filter for efficiency
        // (the interface contract allows probes to do this; the bot also
        // double-checks via CanCastFor before bidding).
        if (!ReferenceEquals(concrete.Owner, caster)) yield break;

        yield return new FlashbackAlternativeCost(cost);
    }
}
