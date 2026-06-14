using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
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
public sealed class SacrificeSelfCost : ICost, IBusAwareCost, IRebindableCost
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

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — re-home the captured "self"
    /// permanent onto <paramref name="newSource"/> when this cost's source is
    /// the ability's original source. Lets
    /// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> re-source a
    /// "Sacrifice CARDNAME:" cost onto the BEARER under Agatha's Soul Cauldron's
    /// group grant (CR 707.2 / 613.1f / 702.49) so the re-homed ability
    /// sacrifices the bearer, never the exiled card. Pure — returns a new
    /// instance, never mutates the original.
    /// </summary>
    public ICost RebindTo(object oldSource, object newSource) =>
        ReferenceEquals(_self, oldSource) && newSource is Permanent p
            ? new SacrificeSelfCost(p)
            : this;

    /// <inheritdoc/>
    public string Description => $"sacrifice {_self.Name}";

    /// <inheritdoc/>
    /// <remarks>
    /// Permanent must be on its controller's battlefield. The activating
    /// player must control the permanent at activation time
    /// (CR 701.16a). Ownership is irrelevant for sacrifice — control is
    /// what matters.
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return ReferenceEquals(_self.Controller, player)
               && _self.Zone == ZoneType.Battlefield
               && player.Zones.Battlefield.ContainsCard(_self);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves the permanent from its controller's battlefield to their
    /// graveyard (CR 701.16a). Routed through the owner's zones so the
    /// graveyard placement targets the right player when control and
    /// ownership differ (stolen permanents go to their OWNER's
    /// graveyard, CR 701.16a / CR 614 zone-change ordering).
    /// </remarks>
    public void Pay(Player player) => PayCore(player, eventBus: null);

    /// <inheritdoc/>
    /// <remarks>
    /// CR 701.16 — a sacrifice paid as a cost is still a sacrifice. Pays
    /// identically to <see cref="Pay(Player)"/> and additionally publishes a
    /// <see cref="PermanentSacrificedEvent"/> on <paramref name="eventBus"/>
    /// so "whenever a/an [player] sacrifices …" aristocrat triggers fire when
    /// this cost is the activation cost of a "Sacrifice CARDNAME:" ability —
    /// the central cost-payment seam the bare <see cref="Pay(Player)"/> never
    /// had. The token-ness snapshot is taken BEFORE the move (CR 111.7 — a
    /// token ceases to exist as an SBA the instant it hits the graveyard) and
    /// the event publishes AFTER the move (so a steal-on-sacrifice payoff
    /// reads the card from the graveyard), exactly as
    /// <see cref="Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/> does.
    /// </remarks>
    public void Pay(Player player, IEventBus eventBus)
    {
        if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
        PayCore(player, eventBus);
    }

    private void PayCore(Player player, IEventBus? eventBus)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        if (!CanPay(player))
            throw new InvalidPlayerActionException(
                $"Cannot pay {Description}: {_self.Name} is not on " +
                $"{player.Name}'s battlefield.");

        // CR 111.7 — snapshot token-ness BEFORE the move: once the permanent
        // reaches the graveyard it may be cleaned up as a state-based action,
        // so any "nontoken" filtering downstream must read the live object.
        var wasToken = _self is Permanent p && p.IsToken;

        // CR 701.16a — sacrificed permanents go to their OWNER's graveyard,
        // not the activating player's. Route through the owner so this
        // behaves correctly when the activating player has stolen the
        // permanent (its Controller is the caster, but Owner stays put).
        var owner = _self.Owner ?? player;

        player.Zones.Battlefield.RemoveCard(_self);
        owner.Zones.Graveyard.AddCard(_self);
        // Zone.AddCard internally calls card.SetZone — no manual SetZone
        // needed.

        // CR 701.16 — publish AFTER the zone move so the sacrificed card is
        // already in the graveyard for a steal-on-sacrifice payoff. The
        // sacrificing player is the controller at sacrifice time (the
        // activating player), per CR 701.16a.
        eventBus?.Publish(new PermanentSacrificedEvent(_self, player, wasToken));
    }
}
