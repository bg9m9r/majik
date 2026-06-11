using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 118.9 — surfaces cast-from-exile alt-cost candidates that come from a
/// RUNTIME grant: a card sitting in the Exile zone with a
/// <see cref="Card.RuntimeExileCastAllowedCaster"/> stamp that nominates a
/// specific player as a legal caster (e.g. Madness exiles the discarded card
/// and grants its controller a cast at the madness cost; Ragavan, Nimble
/// Pilferer / foretell / impulse-draw effects exile a card and grant a
/// temporary "you may cast that card" window).
///
/// Mirrors <see cref="RuntimeFlashbackAltCostProbe"/> exactly, but reads the
/// exile-cast grant (<see cref="Card.RuntimeExileCastAllowedCaster"/> /
/// <see cref="Card.RuntimeExileCastCost"/>) instead of the graveyard-flashback
/// grant. Without this probe, the bot's CR 118.9 enumeration only sees printed
/// alt costs and hand spells and skips the runtime exile-cast — even though
/// the spell-cast pipeline (<see cref="ExileCastAlternativeCost"/>) would
/// accept it.
///
/// Composable with the flashback / pitch / escape probes via
/// <see cref="AlternativeCostProbeRegistry"/> — keep this probe focused on the
/// runtime exile-cast flag so the source of truth (the
/// <c>RuntimeExileCast*</c> properties on <see cref="Card"/>) stays one-to-one
/// with the probe that reads it.
/// </summary>
public sealed class RuntimeExileCastAltCostProbe : IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // Only Card carries the runtime flag (the interface ICard doesn't
        // expose it — the grant is a concrete-card mutable surface set by the
        // granting effect, e.g. the Madness replacement or Ragavan's combat
        // trigger). Probes backed by an ICard other than Card yield nothing.
        if (card is not Card concrete) yield break;

        // The grant is meaningful only from exile (CR 118.9 — the card is cast
        // from the Exile zone). If it has moved out, the grant is stale; let
        // ExileCastAlternativeCost.CanCastFor reject in that case rather than
        // re-checking the zone here, but skip emission when the cost isn't set.
        var cost = concrete.RuntimeExileCastCost;
        if (cost is null) yield break;

        // Caster gate matches ExileCastAlternativeCost.CanCastFor — the grant
        // nominates exactly one player (typically NOT the owner: Ragavan
        // exiles the opponent's card and lets the Ragavan controller cast it).
        // Pre-filter for efficiency; the bot also double-checks via CanCastFor
        // before bidding.
        if (!ReferenceEquals(concrete.RuntimeExileCastAllowedCaster, caster)) yield break;

        yield return new ExileCastAlternativeCost(
            $"Cast {concrete.Name} from exile ({cost})", cost);
    }
}
