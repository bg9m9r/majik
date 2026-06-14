using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Discard this card." A self-targeting discard cost used by Channel
/// abilities (CR 702.74) and any other discard-self activated ability.
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list alongside
/// mana costs.
///
/// Activation zone: Hand. The ability cannot be activated if the card is
/// not currently in the activating player's hand (CR 702.74a).
/// </summary>
public sealed class DiscardSelfCost : ICost, IRebindableCost
{
    private readonly ICard _self;

    public DiscardSelfCost(ICard self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    /// <summary>The card this cost discards (for inspection / re-home checks).</summary>
    public ICard Self => _self;

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — re-home the captured self-card when
    /// the owning <see cref="Majik.Core.Abilities.ActivatedAbility"/> is
    /// re-sourced (CR 707.2 / 613.1f). Returns a fresh cost discarding
    /// <paramref name="newSource"/> when (and only when) the current captured
    /// card is reference-equal to <paramref name="oldSource"/>; otherwise
    /// returns this instance unchanged. Pure — the original is never mutated.
    /// (Without this, Agatha's Soul Cauldron re-homing a Channel ability would
    /// leave a DiscardSelfCost pointed at the EXILED card, which can never be
    /// paid from a battlefield bearer.)
    /// </summary>
    public ICost RebindTo(object oldSource, object newSource)
    {
        if (ReferenceEquals(_self, oldSource) && newSource is ICard newCard)
        {
            return new DiscardSelfCost(newCard);
        }
        return this;
    }

    /// <inheritdoc/>
    public string Description => $"discard {_self.Name}";

    /// <inheritdoc/>
    /// <remarks>
    /// Card must be in the activating player's hand (CR 702.74a).
    /// Ownership check ensures the card belongs to this player's hand, not
    /// a borrowed or stolen card.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return ReferenceEquals(_self.Owner, caster)
               && _self.Zone == ZoneType.Hand
               && caster.Zones.Hand.ContainsCard(_self);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves the card from the caster's hand to their graveyard (CR 702.74a).
    /// Throws if preconditions are not met (consistent with <see cref="ManaCostCost"/>).
    /// </remarks>
    public void Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        if (!CanPay(caster))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: card is not in {caster.Name}'s hand.");

        // CR 701.8 — route through the central discard chokepoint so a
        // DiscardedEvent fires (wasCost: true) and "Whenever you discard a
        // card …" triggers see it.
        Majik.Core.Primitives.Fx.DiscardCard(caster, _self, wasCost: true);
    }
}
