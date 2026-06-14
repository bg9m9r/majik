using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice a creature or pay
/// {3}{B}." — Spark Harvest (Ikoria, {B}, Sorcery). Disjunctive additional
/// cost (CR 601.2f) where the caster picks ONE of the two payment modes at
/// announcement time: sacrifice a creature, or pay the additional
/// <see cref="ManaCost"/> on top of the spell's printed mana cost.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>
/// (Bone Shards) and <see cref="DiscardACardOrPayManaAdditionalCost"/>
/// (Lightning Axe). v1 deterministic preference: <b>sacrifice a creature
/// first</b> when one is available — matches the printed wording's
/// first-mode preference and the canonical Spark Harvest play in a
/// sacrifice/aristocrat shell (turning a dying creature or token into
/// {B} removal is strictly cheaper than spending four extra mana). When
/// the caster controls no creature but the mana is producible, the
/// pay-mana mode is used. <see cref="CanPay"/> is the OR of the two modes
/// — payable so long as EITHER mode is.
///
/// After payment exactly one of <see cref="Sacrificed"/> or
/// <see cref="PaidMana"/> is set, never both. Spark Harvest's resolution
/// does not read the sacrificed creature, but exposing the reference
/// matches the sibling-cost pattern in case a future reprint references
/// "the sacrificed creature".
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks sac-first when both modes
///   are payable. A full agent prompt ("would you rather sacrifice a
///   creature or pay {3}{B}?") shares the deferred-mode-prompt queue with
///   <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/> and
///   <see cref="DiscardACardOrPayManaAdditionalCost"/>.
/// - <b>Self-sacrifice loophole</b>: same posture as
///   <see cref="SacrificeACreatureAdditionalCost"/> — the picker does NOT
///   exclude the spell's own target; the resolution-time legality re-check
///   (CR 608.2b) makes the destroy a no-op if the sacrificed creature was
///   also the chosen target (it has moved to the graveyard first).
/// </summary>
public sealed class SacrificeCreatureOrPayManaAdditionalCost : IAdditionalCost
{
    private readonly IEventBus? _eventBus;

    /// <summary>The mana required by the pay-mana mode (Spark Harvest:
    /// {3}{B}).</summary>
    public ManaCost ManaAmount { get; }

    /// <param name="manaAmount">The additional mana the pay-mana mode
    /// costs (Spark Harvest: {3}{B}).</param>
    /// <param name="eventBus">Optional event bus — when the sacrifice mode
    /// is used, publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a) so aristocrat payoffs fire. Null preserves the
    /// publish-nothing posture.</param>
    public SacrificeCreatureOrPayManaAdditionalCost(ManaCost manaAmount, IEventBus? eventBus = null)
    {
        ManaAmount = manaAmount ?? throw new ArgumentNullException(nameof(manaAmount));
        _eventBus = eventBus;
    }

    /// <summary>The creature sacrificed by <see cref="Pay"/>, if the sac
    /// mode was chosen. Null when pay-mana mode was used or before
    /// payment.</summary>
    public Creature? Sacrificed { get; private set; }

    /// <summary>True when pay-mana mode was chosen by <see cref="Pay"/>.</summary>
    public bool PaidMana { get; private set; }

    /// <inheritdoc/>
    public string Description => $"sacrifice a creature or pay {ManaAmount}";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if EITHER mode can be paid: at least one creature
    /// on the caster's battlefield (sacrifice mode) OR enough mana in pool
    /// to pay <see cref="ManaAmount"/> (pay-mana mode). Mana legality is
    /// checked against the current pool (CR 601.2f-h — additional costs are
    /// paid after mana abilities are activated; the cast flow gives the
    /// caster the chance to float mana before this check).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var hasCreature = caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any();
        var hasEnoughMana = caster.ManaPool.Pay(ManaAmount).Success;
        return hasCreature || hasEnoughMana;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic preference: sacrifice a creature when one is
    /// available (CR 601.2f — the caster chooses the mode at announcement;
    /// v1 simplifies to a fixed preference). Falls through to the pay-mana
    /// mode when the caster controls no creature. Sacrifice picker mirrors
    /// <see cref="SacrificeACreatureAdditionalCost"/> — first eligible
    /// creature on the caster's battlefield.
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
            SacrificeCostHelper.Sacrifice(caster, sacPick, _eventBus);
            Sacrificed = sacPick;
            return true;
        }

        // Mode 2: pay the additional mana ({3}{B}).
        if (!caster.PayMana(ManaAmount)) return false;
        PaidMana = true;
        return true;
    }
}
