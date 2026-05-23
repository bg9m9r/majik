using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Discard a card." — activated-ability cost (CR 117.1 / CR 701.16a) that
/// requires the controller to discard one card from their hand. Distinct
/// from <see cref="DiscardSelfCost"/>, which discards the activated
/// ability's own card.
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list alongside
/// mana costs (e.g. Psychic Frog's "Discard a card: Put a +1/+1 counter on
/// Psychic Frog.", or any "{X}, discard a card:" composite cost).
///
/// ## v1 chooser
///
/// <see cref="Target"/> may be set by the agent to nominate which card to
/// discard. When null, <see cref="Pay"/> deterministically picks the first
/// card in <see cref="Player.Zones.Hand"/> (matches the v1 picker policy
/// used by <see cref="SacrificeAnotherCreatureCost"/> and the rest of the
/// cost surface). Full agent-driven discard prompting is deferred behind
/// the same queue as Liliana of the Veil + Faithless Looting.
/// </summary>
public sealed class DiscardACardCost : ICost
{
    /// <summary>
    /// Optionally set by the agent to nominate which card to discard. When
    /// null the cost falls back to the first card in the controller's hand
    /// (deterministic v1 behaviour). Tests / bots may set this before the
    /// ability resolves to make the discard deterministic.
    /// </summary>
    public ICard? Target { get; set; }

    /// <inheritdoc/>
    public string Description => "discard a card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — a cost can be paid only if the player has the resources.
    /// "Discard a card" requires at least one card in hand; payable only
    /// when <see cref="Player.Zones.Hand"/> is non-empty (and, when a
    /// <see cref="Target"/> is nominated, that card is actually in the
    /// player's hand).
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (Target != null)
        {
            return player.Zones.Hand.ContainsCard(Target);
        }
        return player.Zones.Hand.GetCards().Any();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16a — discard moves the chosen card from the player's hand
    /// to their graveyard. Throws when no card is available to discard
    /// (consistent with <see cref="ManaCostCost"/> / <see cref="DiscardSelfCost"/>).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Hand.GetCards().FirstOrDefault();
        if (pick == null)
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {player.Name}'s hand is empty.");
        if (!player.Zones.Hand.ContainsCard(pick))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not in {player.Name}'s hand.");

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        // Zone.AddCard sets card.Zone — no manual SetZone needed.
    }
}
