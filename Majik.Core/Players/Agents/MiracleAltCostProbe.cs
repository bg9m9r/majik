using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.94 / CR 118.9 — surfaces Miracle alt-cost candidates to the bot's
/// spell-cast enumeration. A miracle card whose first-card-drawn-this-turn
/// window is currently open carries a runtime grant
/// (<see cref="Card.RuntimeMiracleCost"/>) stamped by the draw hook
/// (<see cref="Majik.Core.Game.TurnDriver"/>); this probe reads that grant and
/// yields a <see cref="MiracleAlternativeCost"/> so the bot can choose to cast
/// the card for its (usually much cheaper) miracle cost.
///
/// <para>Without this probe the heuristic bot's CR 118.9 enumeration only sees
/// printed alt costs and hand spells at their printed cost, and never casts a
/// freshly-drawn Terminus / Reforge the Soul / Bonfire of the Damned for its
/// miracle cost even though the cast pipeline
/// (<see cref="MiracleAlternativeCost"/>) would accept it.</para>
///
/// <para>Probe-level filtering mirrors <see cref="RuntimeFlashbackAltCostProbe"/>:
/// hand-zone (CR 702.94a), owner-gated, and grant-present. Keeping the probe
/// focused on the runtime flag keeps the source of truth (the
/// <c>RuntimeMiracleCost</c> property on <see cref="Card"/>) one-to-one with
/// the probe that reads it. Composable with the other probes via
/// <see cref="AlternativeCostProbeRegistry"/>.</para>
/// </summary>
public sealed class MiracleAltCostProbe : IAlternativeCostProbe
{
    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // Only Card carries the runtime flag (ICard doesn't expose it — the
        // grant is a concrete-card mutable surface set by the draw hook).
        if (card is not Card concrete) yield break;

        // Miracle is cast from the hand (CR 702.94a). If the card has moved
        // out, the window grant is stale; MiracleAlternativeCost.CanCastFor
        // also re-checks the zone, but skip emission early here.
        if (concrete.Zone != ZoneType.Hand) yield break;

        // Owner gate (CR 702.94b — only the player who drew the card has the
        // window). Pre-filter for efficiency; the bot also double-checks via
        // CanCastFor before bidding.
        if (!ReferenceEquals(concrete.Owner, caster)) yield break;

        var cost = concrete.RuntimeMiracleCost;
        if (cost is null) yield break;

        yield return new MiracleAlternativeCost(cost);
    }
}
