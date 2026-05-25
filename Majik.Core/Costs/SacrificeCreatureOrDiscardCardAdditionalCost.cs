using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a creature or
/// discard a card." — Bone Shards (MH2, {B}). Disjunctive additional
/// cost (CR 601.2f) where the caster picks ONE of the two payment
/// modes at announcement time.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeACreatureAdditionalCost"/> +
/// <see cref="DiscardACreatureCardAdditionalCost"/>. v1 deterministic
/// preference: <b>sacrifice a creature first</b> when one is available
/// (matches the printed wording's first-mode preference and the
/// canonical Bone Shards play — sacrificing a sticky token or a
/// previously-Snow-Day'd creature is strictly cheaper than discarding
/// a real card; both burn shells and reanimator shells use the sac
/// mode by default). When the caster controls no creature but has a
/// card in hand, the discard mode is used. <see cref="CanPay"/> is
/// the OR of the two modes — payable so long as EITHER mode is.
///
/// After payment <see cref="Sacrificed"/> or <see cref="Discarded"/>
/// (exactly one, never both) holds the chosen card so downstream
/// effects can reference it. Bone Shards' resolve doesn't actually
/// read the sacrificed/discarded card, but exposing the references
/// matches the sibling-cost pattern in case future cards
/// (e.g. functional reprints with "X equals sacrificed creature's
/// power") need them.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks sac-first when both are
///   payable. Full agent prompt ("would you rather sacrifice a
///   creature or discard a card?") shares a queue with
///   <see cref="DiscardACardCost"/>'s deferred discard-target prompt.
/// - <b>Self-sacrifice loophole</b>: same posture as
///   <see cref="SacrificeACreatureAdditionalCost"/> — the picker does
///   NOT exclude any specific creature; first eligible wins.
/// </summary>
public sealed class SacrificeCreatureOrDiscardCardAdditionalCost : IAdditionalCost
{
    /// <summary>The creature sacrificed by <see cref="Pay"/>, if the sac
    /// mode was chosen. Null when discard mode was used or before
    /// payment.</summary>
    public Creature? Sacrificed { get; private set; }

    /// <summary>The card discarded by <see cref="Pay"/>, if the discard
    /// mode was chosen. Null when sac mode was used or before
    /// payment.</summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "sacrifice a creature or discard a card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if EITHER mode can be paid: at least one
    /// creature on the caster's battlefield OR at least one card in
    /// the caster's hand.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var hasCreature = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any();
        var hasHandCard = caster.Zones.Hand.GetCards().Any();
        return hasCreature || hasHandCard;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic preference: sacrifice a creature when one is
    /// available. Falls through to discard mode when the caster
    /// controls no creature (CR 601.2f — the caster chooses the mode
    /// at announcement; v1 simplifies to a fixed preference).
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        // Mode 1: sacrifice a creature. Same picker as
        // SacrificeACreatureAdditionalCost — first eligible on the
        // caster's battlefield.
        var sacPick = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault();
        if (sacPick != null)
        {
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
