using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Discard a creature card." — activated-ability cost (CR 117.1 /
/// CR 701.16a) restricted to creature cards in the controller's hand.
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list
/// alongside <see cref="ManaCostCost"/> (e.g. Survival of the Fittest's
/// "{G}, Discard a creature card:" composite cost).
///
/// Distinct from <see cref="DiscardACreatureCardAdditionalCost"/>,
/// which is the <see cref="IAdditionalCost"/> variant the spell-cast
/// flow uses for printed "As an additional cost to cast this spell,
/// discard a creature card." riders.
///
/// ## v1 chooser
///
/// <see cref="Target"/> may be set by the agent to nominate which
/// creature card to discard. When null, <see cref="Pay"/>
/// deterministically picks the first creature card in
/// <see cref="Player.Zones.Hand"/> (matches the v1 picker policy used by
/// <see cref="DiscardACardCost"/>). Full agent-driven discard prompting
/// is deferred behind the same queue as Liliana of the Veil + Faithless
/// Looting.
///
/// After <see cref="Pay"/> succeeds <see cref="Discarded"/> exposes the
/// chosen creature card for downstream effects that reference "the
/// discarded creature".
/// </summary>
public sealed class DiscardACreatureCardCost : ICost
{
    /// <summary>
    /// Optionally set by the agent to nominate which creature card to
    /// discard. When null the cost falls back to the first creature card
    /// in the controller's hand (deterministic v1 behaviour).
    /// </summary>
    public ICard? Target { get; set; }

    /// <summary>
    /// The creature card actually discarded once <see cref="Pay"/> has
    /// succeeded. Null before payment.
    /// </summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "discard a creature card";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — the cost is payable only if the player has at least
    /// one creature card in hand (and, when a <see cref="Target"/> is
    /// nominated, that card is actually a creature in the player's
    /// hand). "Creature card" matches any card whose type set includes
    /// <see cref="CardType.Creature"/> (CR 301.1 / CR 302.1).
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
    /// available (consistent with <see cref="ManaCostCost"/> /
    /// <see cref="DiscardACardCost"/>).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target
            ?? player.Zones.Hand.GetCards()
                .FirstOrDefault(c => c.HasType(CardType.Creature));
        if (pick == null)
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {player.Name} has no creature card in hand.");
        if (!player.Zones.Hand.ContainsCard(pick))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not in {player.Name}'s hand.");
        if (!pick.HasType(CardType.Creature))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: nominated card is not a creature.");

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        // Zone.AddCard sets card.Zone — no manual SetZone needed.
        Discarded = pick;
    }
}
