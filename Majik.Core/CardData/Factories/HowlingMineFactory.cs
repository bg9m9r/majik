using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Howling Mine (Limited Edition Alpha, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "At the beginning of each player's draw step, if this artifact is
///    untapped, that player draws an additional card."
///
/// ## Why a code factory (and not pure JSON)
/// The base identity (Artifact, {2}) is materialised from the embedded
/// JSON definition (<c>howling-mine.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The triggered ability is
/// wired in code because the JSON trigger union has no shape for a
/// SYMMETRIC "each player's draw step" trigger with an intervening-if
/// tap gate that draws for the <em>triggering</em> player (the JSON draw
/// verbs all draw for the controller). Same posture as
/// <see cref="WinterOrbFactory"/> / <see cref="OrcishBowmastersFactory"/>:
/// JSON identity + bespoke ability wiring.
///
/// ## Implemented (v1)
/// - Artifact {2} with owner/controller wiring (from JSON).
/// - <b>Symmetric draw-step trigger (CR 603.1 — "each player's draw
///   step")</b>: a single <see cref="TriggeredAbility"/> over
///   <see cref="StepStartedEvent"/> filtered to
///   <see cref="StepStateType.Draw"/> for ANY player (not just the
///   controller). When it matches, the active player is captured via
///   <see cref="TriggeredAbility.SetTriggeringPlayer"/> so the resolve
///   body draws for "that player" (CR — the additional card is drawn by
///   the player whose draw step it is), read back off
///   <see cref="ResolutionContext.TriggeringPlayer"/>.
/// - <b>Intervening-if "untapped" (CR 603.4)</b>: supplied as the
///   ability's <see cref="TriggeredAbility.InterveningIf"/>, so it is
///   re-checked both when the trigger would go on the stack
///   (<see cref="TriggerManager.EvaluateTriggers"/> consults
///   <see cref="TriggeredAbility.CanBePutOnStack"/>) AND on resolution. A
///   tapped Howling Mine grants no additional draw. Same conditional-tap
///   posture as <see cref="WinterOrbFactory"/> (<c>!card.IsTapped</c>).
/// - <b>Draw (CR 120)</b>: <see cref="Fx.DrawCards"/> draws one card for
///   the triggering player; an empty library is a no-op at the effect
///   level (the SBA draw-from-empty loss, CR 704.5c, is handled by the
///   engine's state-based-action pass).
/// </summary>
[CardName("Howling Mine")]
public static class HowlingMineFactory
{
    public const string CardName = "Howling Mine";
    public const string Slug = "howling-mine";

    /// <summary>
    /// Shape-only constructor — builds Howling Mine with correct identity
    /// and the draw-step triggered ability attached, but with no live bus
    /// wiring (the trigger never fires). Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Howling Mine fully wired against the supplied
    /// <see cref="IEventBus"/> + <see cref="TriggerManager"/>. The
    /// draw-step trigger is registered with the manager so it fires on
    /// each player's draw step (subject to the untapped intervening-if).
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------------
        // CR 603.1 — "At the beginning of each player's draw step …". A single
        // symmetric trigger over StepStartedEvent for the Draw step of ANY
        // player. The matched (active) player is stamped on the ability via
        // SetTriggeringPlayer so the resolve body draws for "that player".
        // --------------------------------------------------------------------
        var condition = new EventTriggerCondition<StepStartedEvent>((e, ability) =>
        {
            if (e.StepType != StepStateType.Draw) return false;
            if (e.Player == null) return false;

            // "That player" — captured for the resolve body. Read back off
            // ResolutionContext.TriggeringPlayer at resolution (CR — the draw
            // is performed by the player whose draw step it is).
            if (ability is TriggeredAbility ta) ta.SetTriggeringPlayer(e.Player);
            return true;
        });

        // CR 120 — "that player draws an additional card." The triggering
        // (active) player is read off the resolution context; a missing
        // triggering player (context-free fire) is a safe no-op.
        var drawEffect = new Effect(
            $"{CardName}: the active player draws an additional card",
            (ResolutionContext ctx) =>
            {
                var drawer = ctx.TriggeringPlayer;
                if (drawer != null) Fx.DrawCards(drawer, 1);
                return ValueTask.CompletedTask;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drawEffect },
            // CR 603.4 — intervening-if "if this artifact is untapped". Gates
            // BOTH stack entry (CanBePutOnStack) and resolution. Re-checked
            // live so a mid-step tap of the mine cancels the draw.
            interveningIf: () => !card.IsTapped && card.Zone == ZoneType.Battlefield,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
