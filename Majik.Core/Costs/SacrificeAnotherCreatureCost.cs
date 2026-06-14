using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice another creature" — activated-ability cost that requires the
/// controller to sacrifice a creature other than the ability's source.
///
/// Implements <see cref="ICost"/> so it can slot directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list.
///
/// ## Prompted choice (the fix)
/// Implements <see cref="IChooseCreatureToSacrificeCost"/> so the activation
/// dispatch prompts the controller (via the existing <c>ChooseAsync</c> sink —
/// rendered by the portal as a <c>ChoiceCommand</c>) to choose WHICH creature
/// to sacrifice, stamping it onto <see cref="Target"/> BEFORE <see cref="Pay"/>
/// runs (CR 700.6 — the controller chooses). When <see cref="Target"/> is left
/// null (paths that don't prompt — bot convenience wiring / factory-direct
/// tests) <see cref="Pay"/> falls back to the first eligible creature
/// deterministically.
///
/// ## Re-homable (STAGE 1)
/// Implements <see cref="IRebindableCost"/> so
/// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> can re-home the
/// captured <see cref="_self"/> when the owning ability is re-sourced onto a new
/// permanent (CR 707.2 copy machinery / Agatha's Soul Cauldron granted abilities
/// — CR 613.1f / 702.49). Without this seam a re-homed "Pay 1 life, Sacrifice
/// another creature" ability (Yawgmoth, Thran Physician) would still exclude the
/// ORIGINAL Yawgmoth from the picker rather than the BEARER, letting the bearer
/// sacrifice itself. <see cref="RebindTo"/> returns an equivalent cost whose
/// <see cref="_self"/> is the new source (carrying the same event bus); the
/// per-activation <see cref="Target"/> / <see cref="Sacrificed"/> state is reset
/// on the fresh copy (it is per-activation, not structural).
/// </summary>
public sealed class SacrificeAnotherCreatureCost : ICost, IChooseCreatureToSacrificeCost, IRebindableCost
{
    private readonly Permanent _self;
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Set by the activation dispatch (after prompting the controller via
    /// <see cref="IChooseCreatureToSacrificeCost"/>) to indicate which creature
    /// to sacrifice. When null the cost falls back to the first eligible
    /// creature on the controller's battlefield (deterministic legacy
    /// behaviour — used only on paths that don't prompt, e.g. bot convenience
    /// wiring / factory-direct tests).
    /// </summary>
    public Creature? Target { get; set; }

    /// <summary>
    /// The creature that was actually sacrificed once <see cref="Pay"/>
    /// succeeded. Null before payment. Effect closures that reference "the
    /// sacrificed creature's mana value" (Prime Speaker Vannifar — CR 202.3)
    /// read this; it captures BOTH the prompted <see cref="Target"/> and the
    /// deterministic first-eligible fallback, mirroring
    /// <see cref="SacrificeCreatureCost.Sacrificed"/> /
    /// <see cref="SacrificeACreatureAdditionalCost.Sacrificed"/>.
    /// </summary>
    public Creature? Sacrificed { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<Creature> EligibleSacrifices(Player player)
    {
        if (player == null) return Array.Empty<Creature>();
        return player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, _self))
            .ToList();
    }

    /// <inheritdoc/>
    public void ChooseSacrifice(Creature? creature) => Target = creature;

    /// <inheritdoc/>
    public ICost RebindTo(object oldSource, object newSource)
    {
        // Pure: only re-home when our captured source IS the rebound ability's
        // original source; otherwise pass through unchanged (CR 707.2). A fresh
        // copy is returned so per-activation Target/Sacrificed state never leaks
        // into the source ability's cost.
        if (!ReferenceEquals(_self, oldSource)) return this;
        if (newSource is not Permanent newPermanent)
            throw new ArgumentException(
                "SacrificeAnotherCreatureCost can only be re-homed onto a Permanent.",
                nameof(newSource));
        return new SacrificeAnotherCreatureCost(newPermanent, _eventBus);
    }

    /// <param name="self">The ability's source — excluded from the picker.</param>
    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeAnotherCreatureCost(Permanent self, IEventBus? eventBus = null)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _eventBus = eventBus;
    }

    public string Description =>
        $"sacrifice a creature other than {_self.Name}";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => !ReferenceEquals(c, _self));
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => !ReferenceEquals(c, _self));

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible creature to sacrifice.");

        // Capture the sacrificed creature BEFORE the zone move so downstream
        // effects (Prime Speaker Vannifar — "1 plus the sacrificed creature's
        // mana value", CR 202.3) can read its mana value after payment.
        Sacrificed = pick;
        SacrificeCostHelper.Sacrifice(player, pick, _eventBus);
    }
}
