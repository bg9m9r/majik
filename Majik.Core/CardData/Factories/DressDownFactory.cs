using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dress Down (Modern Horizons 2).
///
/// Enchantment — {1}{U}
/// Oracle text:
///   "Flash
///    Creatures lose all abilities and have base power and toughness 1/1.
///    At the beginning of the end step, sacrifice Dress Down."
///
/// ## Implemented (v1)
/// - Enchantment shell with mana cost {1}{U}.
/// - <see cref="KeywordAbility"/> Flash marker (CR 702.8).
/// - <see cref="DressDownStaticEffect"/> lifecycle binder wiring the
///   Layer 6 (CR 613.6) "lose all abilities" + Layer 7b (CR 613.7b)
///   "base P/T 1/1" pair when the runtime overload is used. The pair
///   activates while Dress Down is on the battlefield and unregisters on
///   LTB (CardMovedEvent driven, mirroring Blood Moon / Tarmogoyf).
/// - End-step sacrifice trigger (CR 500 / CR 603.1) — registered via the
///   supplied <see cref="TriggerManager"/> when present so the runtime
///   places the trigger on the stack at the start of the controller's End
///   step. Resolution moves the source to its owner's graveyard.
///
/// ## Deferred (v1 gaps)
/// - The candidate pool for the Layer 6 + 7b pair is a snapshot at ETB
///   time. Creatures entering AFTER Dress Down are NOT scoped — matches
///   the conservative Humility wiring on <see cref="LoseAllAbilitiesEffect"/>.
///   Extending coverage to later ETBs would need a CardMovedEvent watcher
///   that grows the pool.
/// - The printed replacement-style ETB drawback ("As long as Dress Down is
///   on the battlefield, if another creature would enter the battlefield,
///   it enters with all abilities removed and as a 1/1") is NOT modelled.
///   The current snapshot covers the prevailing on-battlefield population
///   only.
/// - The shape-only <see cref="Create(Player)"/> path attaches the Flash
///   keyword and the end-step trigger to the card so dispatcher/identity
///   tests see them, but neither the static-effect lifecycle nor the
///   live TriggerManager registration runs without the runtime overload.
/// </summary>
[CardName("Dress Down")]
public static class DressDownFactory
{
    public const string CardName = "Dress Down";
    public const string Cost = "{1}{U}";

    /// <summary>
    /// Construct Dress Down with no live continuous-effects or trigger-
    /// manager wiring. Suitable for shape / dispatcher / identity tests.
    /// The Flash keyword and an end-step sacrifice trigger are attached to
    /// the card so structural assertions still see them.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, triggers: null, creaturePoolSource: null);

    /// <summary>
    /// Construct a fully-wired Dress Down. When <paramref name="effects"/>
    /// and <paramref name="creaturePoolSource"/> are supplied, a
    /// <see cref="DressDownStaticEffect"/> attaches so the Layer 6 + 7b
    /// pair registers/unregisters as Dress Down enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="triggers"/> is
    /// supplied the end-step sacrifice trigger is registered so it surfaces
    /// on the stack at the start of the controller's End step.
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. Pass null for
    /// shape-only.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking and triggered
    /// ability registration. May be null.</param>
    /// <param name="triggers">Trigger manager for live registration of the
    /// end-step sacrifice trigger. May be null.</param>
    /// <param name="creaturePoolSource">Closure returning the set of
    /// creatures the static effect should scope over (snapshot at ETB
    /// time). Typically <c>() =&gt; allPlayers.SelectMany(p =&gt;
    /// p.Zones.Battlefield.GetCards()).OfType&lt;Creature&gt;()</c>. Pass
    /// null for shape-only.</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<IEnumerable<Creature>>? creaturePoolSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 500.4 / CR 603.1 — "At the beginning of the end step,
        // sacrifice Dress Down." Triggers.OnStepBegin filters
        // StepStartedEvent on (End, controller) so it only fires on the
        // controller's own end step. Resolution = move the source to its
        // owner's graveyard (CR 701.16 sacrifice).
        var sacEffect = new Effect(
            "Dress Down: sacrifice the source at the beginning of the end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                OracleSpellBinder.MoveToGraveyard(card);
            });

        var endStepSac = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.End),
            effects: new IEffect[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepSac);
        triggers?.RegisterTriggeredAbility(endStepSac);

        // CR 613.6 (Layer 6) + CR 613.7b (Layer 7b) — "Creatures lose all
        // abilities and have base power and toughness 1/1." Live wiring
        // only fires when the runtime supplies a continuous-effects
        // service AND a creature-pool source; otherwise the shape-only
        // path produces the card without the lifecycle binder.
        if (effects != null && creaturePoolSource != null)
        {
            var lifecycle = new DressDownStaticEffect(
                card,
                effects,
                eventBus,
                creaturePoolSource);
            lifecycle.Attach();
        }

        return card;
    }
}
