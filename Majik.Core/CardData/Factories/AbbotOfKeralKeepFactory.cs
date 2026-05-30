using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abbot of Keral Keep (Magic Origins, {1}{R}).
/// Creature — Human Monk 2/1. Oracle text (verified against Scryfall):
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    When this creature enters, exile the top card of your library. Until
///    end of turn, you may play that card."
///
/// The base shape (name, Creature, Human + Monk subtypes, {1}{R}, 2/1)
/// is materialised from the embedded JSON definition
/// (<c>abbot-of-keral-keep.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Prowess and the ETB impulse) are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers,
/// Prowess, or the "exile top + may-play-until-end-of-turn" rider, so they
/// live in the factory (same posture as
/// <see cref="EmberheartChallengerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Prowess (CR 702.108)</b> — built via
///   <see cref="ProwessFactory.Build"/>: an on-cast trigger over
///   <see cref="SpellCastEvent"/> filtered to (controller + non-Creature
///   spell) that registers a +1/+1-until-end-of-turn pump on the
///   <see cref="ContinuousEffectsService"/> (Layer 7c, CR 514.2). Only wired
///   live when a layers service is supplied; otherwise the trigger shape
///   still attaches but the pump no-ops (same posture as
///   <see cref="EmberheartChallengerFactory"/>).
/// - <b>ETB impulse (CR 603.6a / 701.20 / 118.9 / 514.2)</b> — an
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> trigger. On resolve:
///   exile the top card of the controller's library (CR 701.20) and stamp a
///   runtime exile-cast grant (<see cref="Card.GrantRuntimeExileCast"/>) so
///   the controller may play it until end of turn (CR 118.9) — the same
///   impulse-draw primitive as
///   <see cref="EmberheartChallengerFactory"/> /
///   <see cref="LightUpTheStageFactory"/>, with the "until end of turn"
///   duration being a single Cleanup (CR 514.2).
///
/// ## Deferred (v1 gaps)
/// - <b>"May play that card" includes lands</b>: the runtime exile-cast
///   grant authorises casting; an exiled land would need a parallel "play
///   this land from exile" grant. v1 ships spell-only authorisation,
///   matching the EmberheartChallenger / LightUpTheStage posture.
/// - <b>Empty-library exile</b>: if the library is empty the exile is a
///   no-op (CR 701.20 imposes no SBA flag for an exile that finds nothing).
/// - <b>Agent "may" on the play permission</b>: the grant is always stamped
///   ("you MAY play that card" is a permission, not a forced action), so no
///   agent prompt is needed at resolution.
/// </summary>
[CardName("Abbot of Keral Keep")]
public static class AbbotOfKeralKeepFactory
{
    public const string CardName = "Abbot of Keral Keep";
    public const string Slug = "abbot-of-keral-keep";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Abbot of Keral Keep with no live wiring. The ETB trigger is
    /// attached for shape observability; Prowess is attached too but its pump
    /// no-ops without a <see cref="ContinuousEffectsService"/>. Neither
    /// trigger is registered with a <see cref="TriggerManager"/>. Suitable
    /// for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Abbot of Keral Keep with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the ETB impulse schedules its
    /// "until end of turn" exile-cast cleanup on the next Cleanup step
    /// (CR 514.2).</param>
    /// <param name="triggers">TriggerManager the Prowess + ETB triggers are
    /// registered with so they surface as pending. May be null.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess +1/+1
    /// pump (Layer 7c). When null, Prowess is not wired live.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Monk subtypes, {1}{R}, 2/1). The JSON carries no
        // abilities — Prowess / ETB impulse are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.108 — Prowess. The trigger is ALWAYS attached for shape
        // observability. ProwessFactory.Build needs a non-null layers
        // service to register its +1/+1 pump (Layer 7c); when the caller
        // supplies one we bind it onto the card and register the pump live,
        // otherwise we hand the builder a throwaway service so the trigger
        // shape still attaches but the pump silently no-ops on execute.
        var prowessEffects = effects ?? new ContinuousEffectsService();
        if (effects != null)
        {
            card.ActiveEffects = effects;
        }
        var prowess = ProwessFactory.Build(card, prowessEffects);
        card.AddAbility(prowess);
        if (effects != null)
        {
            triggers?.RegisterTriggeredAbility(prowess);
        }

        // CR 603.6a — ETB impulse: "When this creature enters, exile the top
        // card of your library. Until end of turn, you may play that card."
        var etb = BuildEtbImpulse(card, owner, eventBus);
        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Build the ETB impulse trigger — "When this creature enters, exile the
    /// top card of your library. Until end of turn, you may play that card."
    /// (CR 603.6a / 701.20 / 118.9 / 514.2).
    /// </summary>
    private static TriggeredAbility BuildEtbImpulse(Creature card, Player owner, IEventBus? eventBus)
    {
        var exileEffect = new Effect(
            "Abbot of Keral Keep — exile the top card of your library; until end of turn you may play that card",
            () =>
            {
                var controller = card.Controller ?? owner;

                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — exile finds nothing (CR 701.20)

                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);

                if (top is not Card concrete) return;

                // CR 118.9 — "you may play that card" with no alternate-cost
                // rider: the grant authorises casting for the printed mana
                // cost. Same impulse-draw primitive as EmberheartChallenger.
                concrete.GrantRuntimeExileCast(controller, concrete.ManaCostValue);

                // CR 514.2 — "until end of turn" is a SINGLE Cleanup: clear
                // the grant at the next Cleanup. Abbot's ETB always resolves
                // on its controller's own turn, so the first Cleanup seen ends
                // the duration. Without a bus the grant persists until cleared
                // by hand (shape-only construction).
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = (se) =>
                {
                    if (se.StepType != PhaseStateType.Cleanup) return;
                    concrete.ClearRuntimeExileCast();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { exileEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });
    }
}
