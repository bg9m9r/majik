using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Discard a creature card." — activated-ability cost (CR 117.1 /
/// CR 701.16a) that requires the controller to discard one <b>creature</b>
/// card from their hand.
///
/// The creature-card-restricted sibling of <see cref="DiscardACardCost"/>:
/// same <see cref="ICost"/> shape (so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list), but the
/// affordability gate + chooser are filtered to cards whose type set
/// includes <see cref="CardType.Creature"/>. Canonical case: Lotleth
/// Troll's "Discard a creature card: Put a +1/+1 counter on this creature."
///
/// "Creature card" matches any card whose type set includes
/// <see cref="CardType.Creature"/> (so Artifact Creatures and tribal cards
/// carrying the Creature type both qualify — CR 301.1 / CR 302.1), exactly
/// as <see cref="DiscardACreatureCardAdditionalCost"/> matches for the
/// cast-time additional-cost shape.
///
/// ## v1 chooser
///
/// <see cref="Target"/> may be set by the agent to nominate which creature
/// card to discard. When null, <see cref="Pay"/> deterministically picks
/// the first creature card in <see cref="Player.Zones.Hand"/> (the same v1
/// picker policy used by <see cref="DiscardACardCost"/> and
/// <see cref="DiscardACreatureCardAdditionalCost"/>). Full agent-driven
/// discard prompting is deferred behind the same queue as Liliana of the
/// Veil + Faithless Looting.
/// </summary>
public sealed class DiscardACreatureCardCost : ICost
{
    /// <summary>
    /// Optionally set by the agent to nominate which creature card to
    /// discard. When null the cost falls back to the first creature card
    /// in the controller's hand (deterministic v1 behaviour). Tests / bots
    /// may set this before the ability resolves to make the discard
    /// deterministic.
    /// </summary>
    public ICard? Target { get; set; }

    /// <inheritdoc/>
    public string Description => "discard a creature card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — a cost can be paid only if the player has the resources.
    /// "Discard a creature card" requires at least one creature card in
    /// hand; payable only when the controller's hand holds a card with the
    /// <see cref="CardType.Creature"/> type (and, when a <see cref="Target"/>
    /// is nominated, that card is actually a creature card in the player's
    /// hand).
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (Target != null)
        {
            return Target.HasType(CardType.Creature)
                && player.Zones.Hand.ContainsCard(Target);
        }
        return player.Zones.Hand.GetCards().Any(c => c.HasType(CardType.Creature));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16a — discard moves the chosen creature card from the
    /// player's hand to their graveyard. Throws when no creature card is
    /// available to discard (consistent with <see cref="DiscardACardCost"/>
    /// / <see cref="ManaCostCost"/>).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target
            ?? player.Zones.Hand.GetCards().FirstOrDefault(c => c.HasType(CardType.Creature));
        if (pick == null)
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {player.Name} has no creature card in hand.");
        if (!pick.HasType(CardType.Creature))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not a creature card.");
        if (!player.Zones.Hand.ContainsCard(pick))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not in {player.Name}'s hand.");

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        // Zone.AddCard sets card.Zone — no manual SetZone needed.
    }
}
