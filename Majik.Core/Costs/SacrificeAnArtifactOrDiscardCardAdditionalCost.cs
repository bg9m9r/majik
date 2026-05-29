using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice an artifact or
/// discard a card." — Demand Answers (Murders at Karlov Manor, {1}{R}).
/// Disjunctive additional cost (CR 601.2f) where the caster picks ONE of
/// the two payment modes at announcement time.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>
/// (which mirrors the same OR-disjunction but over creatures) and to
/// <see cref="SacrificeAnArtifactCost"/> + <see cref="DiscardACardCost"/>.
/// v1 deterministic preference: <b>sacrifice an artifact first</b> when one
/// is available (matches the printed wording's first-mode preference — in
/// the canonical Demand Answers play a spent Treasure / Clue / Blood token
/// or a depleted artifact is strictly cheaper to pitch than a real card in
/// hand). When the caster controls no artifact but has a card in hand, the
/// discard mode is used. <see cref="CanPay"/> is the OR of the two modes —
/// payable so long as EITHER mode is.
///
/// After payment <see cref="Sacrificed"/> or <see cref="Discarded"/>
/// (exactly one, never both) holds the chosen card so downstream effects
/// can reference it. Demand Answers' resolve doesn't read the
/// sacrificed/discarded card, but exposing the references matches the
/// sibling-cost pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks sac-artifact-first when both
///   are payable. Full agent prompt ("would you rather sacrifice an artifact
///   or discard a card?") shares a queue with
///   <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>'s deferred
///   mode prompt and <see cref="DiscardACardCost"/>'s deferred
///   discard-target prompt.
/// - <b>Self-sacrifice loophole</b>: same posture as
///   <see cref="SacrificeAnArtifactCost"/> — the picker does NOT exclude any
///   specific artifact; first eligible wins.
/// </summary>
public sealed class SacrificeAnArtifactOrDiscardCardAdditionalCost : IAdditionalCost
{
    /// <summary>The artifact sacrificed by <see cref="Pay"/>, if the sac
    /// mode was chosen. Null when discard mode was used or before
    /// payment.</summary>
    public Permanent? Sacrificed { get; private set; }

    /// <summary>The card discarded by <see cref="Pay"/>, if the discard
    /// mode was chosen. Null when sac mode was used or before
    /// payment.</summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "sacrifice an artifact or discard a card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if EITHER mode can be paid: at least one
    /// artifact on the caster's battlefield OR at least one card in
    /// the caster's hand.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var hasArtifact = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(p => p.HasType(CardType.Artifact));
        var hasHandCard = caster.Zones.Hand.GetCards().Any();
        return hasArtifact || hasHandCard;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic preference: sacrifice an artifact when one is
    /// available. Falls through to discard mode when the caster
    /// controls no artifact (CR 601.2f — the caster chooses the mode at
    /// announcement; v1 simplifies to a fixed preference).
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        // Mode 1: sacrifice an artifact. Same picker as
        // SacrificeAnArtifactCost — first eligible artifact on the
        // caster's battlefield.
        var sacPick = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.HasType(CardType.Artifact));
        if (sacPick != null)
        {
            // CR 701.16 — sacrifice: owner-routed move to the graveyard,
            // bypassing Indestructible / regeneration (a sacrifice is not
            // a "destroy" effect).
            caster.Zones.Battlefield.RemoveCard(sacPick);
            caster.Zones.Graveyard.AddCard(sacPick);
            sacPick.SetZone(ZoneType.Graveyard);
            Sacrificed = sacPick;
            return true;
        }

        // Mode 2: discard a card. Same picker as DiscardACardCost — first
        // card in hand.
        var discardPick = caster.Zones.Hand.GetCards().FirstOrDefault();
        if (discardPick == null) return false;

        caster.Zones.Hand.RemoveCard(discardPick);
        caster.Zones.Graveyard.AddCard(discardPick);
        discardPick.SetZone(ZoneType.Graveyard);
        Discarded = discardPick;
        return true;
    }
}
