using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice [this permanent]." A self-targeting sacrifice cost used by
/// activated abilities whose printed cost is "Sacrifice CARDNAME:" —
/// Burrenton Forge-Tender's "Sacrifice Burrenton Forge-Tender: Prevent
/// all damage…", Spore Frog's "Sacrifice Spore Frog:", and dozens more
/// in the family (CR 701.16 — Sacrifice).
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list
/// alongside mana costs (mirrors <see cref="DiscardSelfCost"/>'s shape).
///
/// Activation zone: Battlefield. The ability cannot be activated if
/// the permanent is not currently on its controller's battlefield
/// (CR 701.16a — a player may only sacrifice a permanent they control).
/// </summary>
public sealed class SacrificeSelfCost : ICost
{
    private readonly Permanent _self;

    public SacrificeSelfCost(Permanent self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    /// <summary>The sacrificed permanent — same reference passed at
    /// construction. Exposed for tests / effects that need to read the
    /// source after payment.</summary>
    public Permanent Self => _self;

    /// <inheritdoc/>
    public string Description => $"sacrifice {_self.Name}";

    /// <inheritdoc/>
    /// <remarks>
    /// Permanent must be on its controller's battlefield. The activating
    /// player must control the permanent at activation time
    /// (CR 701.16a). Ownership is irrelevant for sacrifice — control is
    /// what matters.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return ReferenceEquals(_self.Controller, caster)
               && _self.Zone == ZoneType.Battlefield
               && caster.Zones.Battlefield.ContainsCard(_self);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves the permanent from its controller's battlefield to their
    /// graveyard (CR 701.16a). Routed through the owner's zones so the
    /// graveyard placement targets the right player when control and
    /// ownership differ (stolen permanents go to their OWNER's
    /// graveyard, CR 701.16a / CR 614 zone-change ordering).
    /// </remarks>
    public void Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        if (!CanPay(caster))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {_self.Name} is not on " +
                $"{caster.Name}'s battlefield.");

        // CR 701.16a — sacrificed permanents go to their OWNER's graveyard,
        // not the activating player's. Route through the owner so this
        // behaves correctly when the activating player has stolen the
        // permanent (its Controller is the caster, but Owner stays put).
        var owner = _self.Owner ?? caster;

        caster.Zones.Battlefield.RemoveCard(_self);
        owner.Zones.Graveyard.AddCard(_self);
        // Zone.AddCard internally calls card.SetZone — no manual SetZone
        // needed.
    }
}
