using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conversion (Alpha / Beta / Unlimited / Revised).
///
/// Enchantment — {2}{W}{W}
/// Oracle text (original):
///   "At the beginning of your upkeep, sacrifice Conversion unless you pay {W}{W}.
///    All Mountains are Plains."
///
/// ## Implementation
///
/// The Layer 4 "All Mountains are Plains" portion is wired via the shared
/// <see cref="RetypeLandsStaticEffect"/> binder (CR 305.6 / 613.1d):
/// scope every Land whose subtype set contains <see cref="CardSubtype.Mountain"/>
/// (basic Mountains, dual lands with the Mountain subtype like Stomping
/// Ground / Sacred Foundry, and any land already retyped to Mountain by
/// Blood Moon), and retype the land-subtype slot to {Plains}. Combined
/// with PR #155's <see cref="EffectiveManaAbilities"/>, affected lands
/// lose their printed mana abilities and tap for {W}.
///
/// Note Conversion's scope is unusual relative to Blood Moon: it
/// IGNORES the basic/nonbasic distinction and keys solely on whether the
/// land has the Mountain subtype.
///
/// ## Upkeep sacrifice-unless-pay (CR 603.1 / CR 117.1)
///
/// "At the beginning of your upkeep, sacrifice this enchantment unless you
/// pay {W}{W}" is wired as a recurring upkeep
/// <see cref="TriggeredAbility"/> over <see cref="StepStateType.Upkeep"/>
/// filtered to the controller, riding the shared
/// <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/> primitive
/// (the same Stasis / Kataki / pact-cycle seam). At resolution the
/// controller's agent is prompted "Pay {W}{W}?"; on yes + affordable the
/// {W}{W} is drained and Conversion stays, on no / can't-afford it is
/// sacrificed (Battlefield → Graveyard). The legacy / shape-only sync path
/// keeps the deterministic "pay if able" posture. (This is the recurring
/// sibling of Echo's single-shot upkeep tax — see
/// <see cref="Majik.Core.Keywords.EchoFactory"/>.)
///
/// ## Deferred (v1 gaps)
/// - <b>No in-trigger tap-lands step</b>: the {W}{W} is paid from whatever
///   is already in the controller's pool when the trigger resolves; there
///   is no resolution-time "tap a land for {W}" sub-prompt.
/// - The upkeep trigger is registered with a <see cref="TriggerManager"/>
///   only via the trigger-aware overload; the legacy shape-only
///   <see cref="Create(Player)"/> attaches it to the ability list but does
///   not register it (same posture as Stasis).
/// </summary>
[CardName("Conversion")]
public static class ConversionFactory
{
    public const string CardName = "Conversion";
    public const string Cost = "{2}{W}{W}";
    public const string UpkeepCost = "{W}{W}";

    private static readonly IReadOnlySet<CardSubtype> PlainsOnly =
        new HashSet<CardSubtype> { CardSubtype.Plains };

    /// <summary>
    /// Creates a Conversion with correct card identity plus the upkeep
    /// "sacrifice unless you pay {W}{W}" trigger attached to its ability list
    /// (not registered with any <see cref="TriggerManager"/>). No live
    /// Layer 4 effect. Suitable for factory-shape / naming tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Creates a Conversion with the Layer 4 type-change lifecycle (when
    /// <paramref name="effects"/> is supplied) plus the upkeep trigger.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
        => Create(owner, effects, eventBus, triggers: null);

    /// <summary>
    /// Creates a fully-wired Conversion. When <paramref name="effects"/>
    /// is supplied, a <see cref="RetypeLandsStaticEffect"/> is attached so
    /// the Layer 4 effect registers/unregisters as Conversion enters/leaves
    /// the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. The upkeep "sacrifice unless you pay
    /// {W}{W}" trigger (CR 603.1) is always attached to the ability list and,
    /// when <paramref name="triggers"/> is supplied, registered so it fires.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.1 / CR 117.1 — "At the beginning of your upkeep, sacrifice
        // this enchantment unless you pay {W}{W}." Recurring upkeep
        // pay-or-sacrifice over the shared primitive (Stasis / Kataki seam).
        var upkeepEffect = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
            "Conversion: at upkeep, sacrifice unless you pay {W}{W}",
            owner,
            ManaCost.Parse("{W}{W}"),
            consequence: () =>
            {
                var sacrificer = card.Controller ?? owner;
                sacrificer.Zones.Battlefield.RemoveCard(card);
                sacrificer.Zones.Graveyard.AddCard(card);
                card.SetZone(ZoneType.Graveyard);
            },
            promptText: "Pay {W}{W} to keep Conversion?",
            guard: () => card.Zone == ZoneType.Battlefield);

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        if (effects != null)
        {
            // CR 305.6 — "All Mountains are Plains." Scope every Land
            // whose subtypes include Mountain (basic or nonbasic), and
            // retype to {Plains}.
            var lifecycle = new RetypeLandsStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land && p.Subtypes.Contains(CardSubtype.Mountain),
                newLandSubtypes: PlainsOnly);
            lifecycle.Attach();
        }

        return card;
    }
}
