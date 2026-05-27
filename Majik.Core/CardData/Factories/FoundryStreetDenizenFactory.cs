using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Foundry Street Denizen (Magic 2014 + Mystery
/// Booster + Ravnica Clue Edition reprints, {R}).
///
/// Creature — Goblin Warrior 1/1. Oracle text (Scryfall, verified):
///   "Whenever another red creature enters under your control, Foundry
///    Street Denizen gets +1/+0 until end of turn."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Goblin Warrior at printed cost {R}; owner / controller
///   wired. <see cref="CardSubtype.Goblin"/> + <see cref="CardSubtype.Warrior"/>
///   subtypes so Goblin Chieftain / Goblin Warchief tribal scopes see it.
/// - <b>Self-pumping ETB trigger (CR 603.1 / 603.6a / 603.6d)</b>: wired
///   via <see cref="EventTriggerCondition{T}"/> over
///   <see cref="CardMovedEvent"/> filtered to:
///     1. <c>ToZone == Battlefield</c>
///     2. <c>!ReferenceEquals(e.Card, card)</c> — "another red creature"
///        excludes the Denizen entering itself (CR 109.5 — "another" =
///        any object other than the source).
///     3. The entering card has <see cref="CardType.Creature"/>.
///     4. The entering card's colour set (via <see cref="CardColors.GetColors"/>)
///        contains <see cref="ManaColor.Red"/> — CR 105 colour computation
///        from mana cost (hybrid + Phyrexian pips contribute the named
///        colour; token-colour overrides win when present, per CR 111.4).
///     5. The entering card's controller equals the Denizen's controller
///        ("under your control" — CR 109.5).
///   On resolution: register a one-turn <see cref="PumpUntilEndOfTurnEffect"/>
///   (+1/+0, CR 613.1f Layer 7c) against the supplied
///   <see cref="ContinuousEffectsService"/>. When the service is null
///   (shape-only path), the trigger fires its effect closure as a no-op —
///   the pump is silently skipped, same posture as Kappa Cannoneer's EOT
///   "can't be blocked" rider on the shape-only path.
///
/// ## Order of operations
///
/// CR 603.6d — "another red creature enters under your control" is an ETB
/// trigger that looks back at the ETB event after the resulting permanent
/// is on the battlefield (the card has already published
/// <see cref="CardMovedEvent"/> with <c>ToZone = Battlefield</c>). The
/// trigger queues onto the stack at the next priority window (CR 603.2)
/// and the pump applies on resolution; the entering creature has already
/// resolved its own ETB triggers by that point (last-in-first-out on the
/// stack is irrelevant — the pump is independent of the entering
/// creature's identity at resolve time, only the trigger condition is
/// evaluated at the event moment).
///
/// ## Trigger active zone
///
/// <c>activeZones = {Battlefield}</c> — the trigger only fires while
/// Foundry Street Denizen is itself on the battlefield (CR 603.6c —
/// printed triggered abilities only function from the battlefield unless
/// the printed text says otherwise; no such text on Denizen). The trigger
/// is unregistered automatically when Denizen LTBs via the standard
/// TriggerManager active-zones gate.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is
///   attached to the card for shape observability; no
///   <see cref="ContinuousEffectsService"/> means the pump no-ops on
///   resolve. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. The trigger registers with <paramref name="triggers"/>
///   when supplied so a qualifying <see cref="CardMovedEvent"/> automatic-
///   ally queues it on the stack (CR 603.2); the pump registers on
///   <paramref name="effects"/> at resolve time. The card's
///   <see cref="Creature.ActiveEffects"/> is set to <paramref name="effects"/>
///   when supplied so reads through <see cref="Creature.Power"/> flow
///   through the continuous-effects layers (CR 613).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Triggered self-ETB-as-red exclusion</b>: the "another" gate
///   already excludes Foundry Street Denizen from triggering itself, but
///   if the Denizen is in play and a copy of itself ETBs, the trigger
///   correctly fires (the copy is a different <c>InstanceId</c>; CR 109.5
///   "another" matches printed-name + InstanceId difference). This is the
///   intended behaviour — two Denizens pump each other on the second one
///   entering.
/// - <b>Token-colour overrides</b>: the colour predicate reads
///   <see cref="CardColors.GetColors"/> which already honours token
///   <see cref="Card.TokenColorsOverride"/> (CR 111.4); red Goblin tokens
///   from Krenko / Goblin Rabblemaster correctly trigger the pump even
///   though they have no printed mana cost.
/// </summary>
[CardName("Foundry Street Denizen")]
public static class FoundryStreetDenizenFactory
{
    public const string CardName = "Foundry Street Denizen";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Per-trigger power boost.</summary>
    public const int PumpPower = 1;

    /// <summary>Per-trigger toughness boost (printed as +1/+0).</summary>
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Foundry Street Denizen with no live wiring. The ETB
    /// trigger is attached for shape observability; the +1/+0 pump no-ops
    /// without a continuous-effects service. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Foundry Street Denizen with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not consumed directly here; reserved for
    /// future lifecycle subscribers (LTB cleanup, etc.).</param>
    /// <param name="triggers">TriggerManager for the ETB trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the +1/+0
    /// pump (CR 613.1f Layer 7c, EOT expiry via the
    /// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> pipeline / CR 514.2).
    /// May be null — the pump is silently skipped on the shape-only path.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // Wire ActiveEffects so card.Power reads flow through the layers
        // compute (CR 613 — Layer 7c applies the PumpUntilEndOfTurnEffect
        // registered at trigger resolve time). Same posture as Monastery
        // Swiftspear's prowess ActiveEffects wire-up.
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.1 / 603.6a.
        //   "Whenever another red creature enters under your control,
        //    Foundry Street Denizen gets +1/+0 until end of turn."
        //
        // Predicate:
        //   - ToZone is Battlefield (ETB).
        //   - The entering card is NOT this Denizen (CR 109.5 "another").
        //   - The entering card has CardType.Creature.
        //   - The entering card's color set contains Red (CR 105 colour
        //     computation; CardColors.GetColors honours token colour
        //     overrides per CR 111.4).
        //   - The entering card's controller is the Denizen's controller
        //     (CR 109.5 — "under your control").
        //
        // Active only while Denizen is on the battlefield (activeZones
        // gate; CR 603.6c).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && !ReferenceEquals(e.Card, card)
            && e.Card.HasType(CardType.Creature)
            && CardColors.GetColors(e.Card).Contains(ManaColor.Red)
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 until end of turn",
            () =>
            {
                // CR 613.1f Layer 7c — register a one-turn +1/+0 pump on
                // the continuous-effects service. EOT cleanup runs via
                // the ExpiresAtEndOfTurn pipeline (CR 514.2). On the
                // shape-only path (effects == null) the pump silently
                // no-ops, matching Kappa Cannoneer's posture for its EOT
                // "can't be blocked" rider.
                if (card.Zone != ZoneType.Battlefield) return;
                effects?.Register(new PumpUntilEndOfTurnEffect(
                    card, PumpPower, PumpToughness));
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
