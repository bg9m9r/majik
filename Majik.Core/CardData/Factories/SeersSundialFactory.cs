using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seer's Sundial (Worldwake, {4}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "Landfall — Whenever a land you control enters, you may pay {2}. If
///    you do, draw a card."
///
/// A repeatable landfall card-advantage engine: every land you drop banks
/// an optional cantrip for {2}.
///
/// ## Why a code factory (and not pure JSON)
/// The base identity (Artifact, {4}) is materialised from the embedded
/// JSON definition (<c>seers-sundial.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The triggered ability is
/// wired in code because the JSON trigger union has no shape for a landfall
/// trigger carrying a "may pay {N}. If you do, draw" optional rider — same
/// posture as <see cref="HowlingMineFactory"/> / <see cref="TirelessTrackerFactory"/>
/// (JSON identity + bespoke ability wiring).
///
/// ## Implemented (v1)
/// - Artifact {4} with owner/controller wiring (from JSON).
/// - <b>Landfall triggered ability (CR 603.1 / CR 603.6a / CR 702.142)</b>:
///   "Whenever a land you control enters, …". Uses the shared
///   <see cref="Triggers.OnLandEntersUnderControl"/> predicate (a
///   <see cref="Majik.Core.Events.CardMovedEvent"/> for a Land entering the
///   battlefield under the controller's control) — the same predicate as
///   <see cref="SteppeLynxFactory"/> / <see cref="LotusCobraFactory"/>. No
///   <see cref="TargetRequest"/>: the effect draws for the controller, so
///   nothing is targeted.
/// - <b>"You may pay {2}" optional rider (CR 117.5)</b>: consults the
///   controller's <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>. Agent-less callers auto-pay
///   if able (same posture as <see cref="MentorOfTheMeekFactory"/>).
///   <see cref="Player.PayMana"/> returns false when the pool can't satisfy
///   {2}; the trigger fizzles harmlessly (CR 117.5).
/// - <b>"If you do, draw a card" (CR 120)</b>: <see cref="Fx.DrawCards"/>
///   draws one card for the controller; an empty library is a no-op at the
///   effect level (the draw-from-empty SBA loss, CR 704.5c, is handled by
///   the engine's state-based-action pass).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for
///   structural / dispatcher inspection; not registered with any
///   <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired. The
///   landfall trigger registers so a matching land ETB queues the
///   may-pay-then-draw effect.
/// </summary>
[CardName("Seer's Sundial")]
public static class SeersSundialFactory
{
    public const string CardName = "Seer's Sundial";
    public const string Slug = "seers-sundial";

    /// <summary>Optional mana cost paid to draw on each landfall (CR 117.5).</summary>
    public const int OptionalManaCost = 2;

    /// <summary>
    /// Shape-only constructor — builds Seer's Sundial with correct identity
    /// and the landfall triggered ability attached, but with no live bus
    /// wiring (the trigger never fires). Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Seer's Sundial. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the
    /// may-pay-{2}-then-draw effect.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / CR 603.6a / CR 702.142.
        //   "Whenever a land you control enters, you may pay {2}. If you
        //    do, draw a card."
        // Predicate is shared with Steppe Lynx / Lotus Cobra. No target:
        // the draw is performed by the controller.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: landfall — may pay {{{OptionalManaCost}}} → draw a card",
            async ctx =>
            {
                // CR 603.6c — the Sundial must still be on the battlefield.
                // activeZones gates the event match; this is defence-in-depth
                // for manual Execute() calls.
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // "You may pay {2}" — consult the controller's agent.
                // Agent-less fallback: auto-pay if able (Mentor of the Meek
                // / Animation Module posture).
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                bool pay;
                if (agent != null)
                {
                    pay = await agent.ChooseYesNoAsync(
                        $"Pay {{{OptionalManaCost}}} to draw a card?",
                        BotIntent.Draw).ConfigureAwait(false);
                }
                else
                {
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 — optional may-pay; the trigger fizzles when the
                // mana isn't available.
                var cost = ManaCost.Zero.AddGenericCost(OptionalManaCost);
                if (!controller.PayMana(cost)) return;

                // CR 120 — "If you do, draw a card." An empty library is a
                // no-op here; the draw-from-empty SBA loss (CR 704.5c) is
                // resolved by the engine's state-based-action pass.
                Fx.DrawCards(controller, 1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
