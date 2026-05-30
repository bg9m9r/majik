using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Exile a land card from your graveyard." — the non-mana component of an
/// activated ability's cost (CR 602.1 / 118.4 — a cost may include non-mana
/// actions; CR 406.3 — exiling a card from the graveyard).
///
/// First consumer: Hostile Desert (Hour of Devastation) —
///   "{2}, Exile a land card from your graveyard: This land becomes a 3/4
///    Elemental creature until end of turn. It's still a land."
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list alongside a
/// <see cref="ManaCost"/>/<c>ManaCostCost</c> (mirrors the
/// <see cref="SacrificeSelfCost"/> / <see cref="DiscardSelfCost"/> shape, but
/// the pick comes from the activating player's graveyard rather than a fixed
/// self-permanent).
///
/// v1 picks the first land card in the activating player's graveyard
/// deterministically (no agent prompt yet — same posture as
/// <see cref="ExileCardsFromGraveyardAdditionalCost"/>). <see cref="Exiled"/>
/// captures the card moved so downstream effects can read it; Hostile Desert
/// doesn't reference the exiled card, but the hook parallels the additional-cost
/// sibling.
/// </summary>
public sealed class ExileLandCardFromGraveyardCost : ICost
{
    private ICard? _exiled;

    /// <summary>The land card actually exiled once <see cref="Pay"/> has
    /// succeeded. Null before payment.</summary>
    public ICard? Exiled => _exiled;

    /// <inheritdoc/>
    public string Description => "exile a land card from your graveyard";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 602.1 — cost legality is checked at activation time. True when the
    /// activating player's graveyard contains at least one card with the
    /// Land card type (CR 305.1).
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return FindFirstLand(player) != null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 406.3 — moves the chosen land card from the activating player's
    /// graveyard to their exile zone. Picks the first land card
    /// deterministically (v1, no agent prompt).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = FindFirstLand(player)
            ?? throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: no land card in {player.Name}'s graveyard.");

        player.Zones.Graveyard.RemoveCard(pick);
        player.Zones.Exile.AddCard(pick); // Zone.AddCard calls card.SetZone
        _exiled = pick;
    }

    private static ICard? FindFirstLand(Player player) =>
        player.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Land));
}
