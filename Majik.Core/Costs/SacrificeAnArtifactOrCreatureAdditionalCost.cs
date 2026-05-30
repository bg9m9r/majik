using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice an artifact or
/// creature." — Deadly Dispute (Commander Legends: Battle for Baldur's Gate
/// / reprints, {1}{B}). Disjunctive additional cost (CR 601.2f) where the
/// caster picks ONE permanent — an artifact OR a creature they control — to
/// sacrifice at announcement time.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeAnArtifactOrDiscardCardAdditionalCost"/>
/// (Demand Answers — same OR-disjunction, but its second mode is a discard
/// instead of a creature sacrifice) and to <see cref="SacrificeACreatureAdditionalCost"/>
/// / <see cref="SacrificeAnArtifactAdditionalCost"/>. Both modes here are
/// sacrifices, so the picker collapses to a single battlefield scan: the
/// first eligible permanent the caster controls that is an artifact OR a
/// creature is sacrificed (CR 701.16). An artifact creature qualifies under
/// either branch — the combined OR-filter accepts it. <see cref="CanPay"/> is
/// the OR of the two modes — payable so long as the caster controls at least
/// one artifact or creature.
///
/// After payment <see cref="Sacrificed"/> holds the chosen permanent so
/// downstream effects can reference it. Deadly Dispute's resolve doesn't read
/// the sacrificed permanent (it draws two cards and makes a Treasure
/// regardless), but exposing the reference matches the sibling-cost pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven choice</b>: v1 picks the first eligible artifact-or-creature
///   (deterministic). A full agent prompt ("which permanent do you sacrifice?")
///   shares a queue with the sibling sacrifice-picker costs' deferred prompts.
/// - <b>Self-sacrifice loophole</b>: same posture as
///   <see cref="SacrificeAnArtifactAdditionalCost"/> — the picker does NOT
///   exclude any specific permanent; first eligible wins. Deadly Dispute is an
///   Instant, not a permanent, so it can never sacrifice itself.
/// </summary>
public sealed class SacrificeAnArtifactOrCreatureAdditionalCost : IAdditionalCost
{
    /// <summary>The permanent sacrificed by <see cref="Pay"/> (an artifact or
    /// a creature). Null before payment.</summary>
    public Permanent? Sacrificed { get; private set; }

    /// <inheritdoc/>
    public string Description => "sacrifice an artifact or creature";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if the caster controls at least one permanent that
    /// is an artifact or a creature (CR 601.2f — the disjunction is satisfied
    /// by either type).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(IsArtifactOrCreature);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic pick: the first eligible artifact-or-creature on the
    /// caster's battlefield is sacrificed (CR 601.2f — the caster chooses the
    /// permanent at announcement; v1 simplifies to a fixed first-eligible
    /// pick). CR 701.16 — sacrifice is an owner-routed move to the graveyard,
    /// bypassing Indestructible / regeneration.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(IsArtifactOrCreature);
        if (pick == null) return false;

        caster.Zones.Battlefield.RemoveCard(pick);
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Sacrificed = pick;
        return true;
    }

    private static bool IsArtifactOrCreature(Permanent p) =>
        p.HasType(CardType.Artifact) || p.HasType(CardType.Creature);
}
