using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.35 — Madness. Reusable mechanic gluing the discard → exile
/// replacement (<see cref="MadnessReplacement"/>) to the cast-for-madness-cost
/// window and the "else put it into the graveyard" fallback.
///
/// <para>Usage: a Madness card's factory registers a
/// <see cref="MadnessReplacement"/> on the <see cref="ReplacementBus"/> for the
/// card, and exposes its <see cref="MadnessAlternativeCost"/>. When the card is
/// discarded, route the discard through <see cref="Discard"/> (which uses the
/// replacement-aware <see cref="ZoneService"/> funnel so the card lands in exile
/// instead of the graveyard), then the controller decides whether to cast it
/// for its madness cost. If they decline (or can't), the card moves from exile
/// to the graveyard (CR 702.35c — "put it into your graveyard").</para>
///
/// <para>The cast / pay decision is supplied by the caller (<c>tryCastForMadness</c>)
/// so the mechanic stays UI-agnostic — the runtime threads an agent-driven cast
/// through the spell pipeline; tests pass a deterministic decision.</para>
/// </summary>
public static class MadnessHelper
{
    /// <summary>
    /// Result of a Madness discard: did the card end up cast (or pending cast)
    /// for its madness cost, or did it fall through to the graveyard?
    /// </summary>
    public enum Outcome
    {
        /// <summary>The card was cast for its madness cost (CR 702.35c).</summary>
        CastForMadness,

        /// <summary>The madness window closed unused; the card was put into the
        /// graveyard (CR 702.35c — "if you don't, put it into your graveyard").</summary>
        ToGraveyard,
    }

    /// <summary>
    /// Discard <paramref name="card"/> from <paramref name="owner"/>'s hand
    /// through the replacement-aware <paramref name="zones"/> funnel. The
    /// registered <see cref="MadnessReplacement"/> redirects the discard into
    /// exile; the controller then decides via <paramref name="tryCastForMadness"/>
    /// whether to cast it for its madness cost. On decline / failure the card
    /// moves from exile to its owner's graveyard.
    /// </summary>
    /// <param name="card">The Madness card being discarded.</param>
    /// <param name="owner">The card's owner (the discarding player).</param>
    /// <param name="zones">A <see cref="ZoneService"/> wired with the
    /// <see cref="ReplacementBus"/> that carries the card's
    /// <see cref="MadnessReplacement"/>.</param>
    /// <param name="tryCastForMadness">Decision + action: returns true if the
    /// card was cast for its madness cost (the callback owns the actual cast
    /// pipeline / mana payment). Returns false to decline.</param>
    public static Outcome Discard(
        ICard card,
        Player owner,
        ZoneService zones,
        Func<ICard, bool> tryCastForMadness)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(tryCastForMadness);

        if (card.Zone != ZoneType.Hand || !owner.Zones.Hand.ContainsCard(card))
            throw new InvalidOperationException(
                $"Cannot discard {card.Name}: it is not in {owner.Name}'s hand.");

        // CR 702.35b — the discard funnels Hand → Graveyard through the
        // replacement bus; MadnessReplacement rewrites the destination to exile.
        // ZoneService owns the actual zone-collection move (hand removal + add
        // to the replaced destination), so we must NOT pre-remove the card.
        zones.MoveCard(card, ZoneType.Hand, ZoneType.Graveyard, controller: owner);

        if (card.Zone == ZoneType.Exile)
        {
            // CR 702.35c — the controller may cast it for its madness cost.
            if (tryCastForMadness(card))
                return Outcome.CastForMadness;

            // Declined / couldn't pay → put it into the graveyard.
            MoveExileToGraveyard(card, owner);
            return Outcome.ToGraveyard;
        }

        // No madness replacement fired (the card had no Madness, or it was
        // already removed): the discard resolved into the graveyard normally.
        return Outcome.ToGraveyard;
    }

    /// <summary>CR 702.35c — the madness window closed unused; move the card
    /// from exile to its owner's graveyard. Exposed for runtimes that drive the
    /// window asynchronously and need to apply the fallback themselves.</summary>
    public static void MoveExileToGraveyard(ICard card, Player owner)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);
        if (card.Zone != ZoneType.Exile) return;

        owner.Zones.Exile.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
