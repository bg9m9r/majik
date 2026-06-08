using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Discard a land card." — activated-ability cost (CR 117.1 /
/// CR 701.16a) that requires the controller to discard one <b>land</b>
/// card from their hand.
///
/// The land-card-restricted sibling of <see cref="DiscardACardCost"/> /
/// <see cref="DiscardACreatureCardCost"/>: same <see cref="ICost"/> shape
/// (so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list), but the
/// affordability gate + chooser are filtered to cards whose type set
/// includes <see cref="CardType.Land"/>. Canonical case: Borborygmos
/// Enraged's "Discard a land card: Borborygmos Enraged deals 3 damage to
/// any target."
///
/// "Land card" matches any card whose type set includes
/// <see cref="CardType.Land"/> (so basics, nonbasics, and land-typed duals
/// all qualify — CR 305.1), exactly as
/// <see cref="DiscardACreatureCardCost"/> matches creature cards for the
/// activated-ability cost shape.
///
/// ## v1 chooser
///
/// <see cref="Target"/> may be set by the agent to nominate which land
/// card to discard. When null, <see cref="Pay"/> deterministically picks
/// the first land card in <see cref="Player.Zones.Hand"/> (the same v1
/// picker policy used by <see cref="DiscardACardCost"/> and
/// <see cref="DiscardACreatureCardCost"/>). Full agent-driven discard
/// prompting is deferred behind the same queue as Liliana of the Veil +
/// Faithless Looting.
/// </summary>
public sealed class DiscardALandCardCost : ICost
{
    /// <summary>
    /// Optionally set by the agent to nominate which land card to discard.
    /// When null the cost falls back to the first land card in the
    /// controller's hand (deterministic v1 behaviour). Tests / bots may set
    /// this before the ability resolves to make the discard deterministic.
    /// </summary>
    public ICard? Target { get; set; }

    /// <inheritdoc/>
    public string Description => "discard a land card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — a cost can be paid only if the player has the resources.
    /// "Discard a land card" requires at least one land card in hand;
    /// payable only when the controller's hand holds a card with the
    /// <see cref="CardType.Land"/> type (and, when a <see cref="Target"/>
    /// is nominated, that card is actually a land card in the player's
    /// hand).
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (Target != null)
        {
            return Target.HasType(CardType.Land)
                && player.Zones.Hand.ContainsCard(Target);
        }
        return player.Zones.Hand.GetCards().Any(c => c.HasType(CardType.Land));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16a — discard moves the chosen land card from the player's
    /// hand to their graveyard. Throws when no land card is available to
    /// discard (consistent with <see cref="DiscardACreatureCardCost"/> /
    /// <see cref="ManaCostCost"/>).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target
            ?? player.Zones.Hand.GetCards().FirstOrDefault(c => c.HasType(CardType.Land));
        if (pick == null)
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {player.Name} has no land card in hand.");
        if (!pick.HasType(CardType.Land))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not a land card.");
        if (!player.Zones.Hand.ContainsCard(pick))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not in {player.Name}'s hand.");

        // CR 701.8 — route through the central discard chokepoint so a
        // DiscardedEvent fires (wasCost: true) and "Whenever you discard a
        // card …" triggers see it.
        Majik.Core.Primitives.Fx.DiscardCard(player, pick, wasCost: true);
    }
}
