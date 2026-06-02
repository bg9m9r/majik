using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rotting Regisaur (Core Set 2020, {2}{B}).
/// Creature — Zombie Dinosaur 7/6.
///
/// ## Card text (Scryfall verified)
/// "At the beginning of your upkeep, discard a card."
///
/// ## Base shape
/// Name / Creature / Zombie Dinosaur subtypes / {2}{B} / 7/6 are
/// materialised from the embedded JSON definition
/// (<c>rotting-regisaur.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="KroxaTitanFactory"/>. The upkeep trigger is layered on here
/// because the JSON ability schema doesn't yet express
/// beginning-of-upkeep self-discard.
///
/// ## Implemented (v1)
/// - <b>Upkeep self-discard trigger (CR 603.1 / CR 500.4 / CR 701.8)</b>:
///   "At the beginning of your upkeep, discard a card." Scoped to the
///   controller's own upkeep via <see cref="Triggers.OnStepBegin"/>
///   (same shape as <see cref="DarkConfidantFactory"/>). On resolution the
///   controller discards a card from hand (CR 701.8 — the discarding player
///   chooses; agent-driven when an agent is supplied, deterministic
///   first-card fallback). An empty hand → no-op (nothing to discard).
///
/// ## Deferred (v1 gaps)
/// - <b>Discard-choice prompt UI</b>: the controller picks what to discard.
///   v1 is agent-driven when an <c>agent</c> is supplied, else deterministic
///   first-card — same gap as <see cref="TerritorialKavuFactory"/>.
/// </summary>
[CardName("Rotting Regisaur")]
public static class RottingRegisaurFactory
{
    public const string CardName = "Rotting Regisaur";
    public const string Slug = "rotting-regisaur";

    /// <summary>
    /// Construct Rotting Regisaur with no live trigger-manager wiring. The
    /// upkeep trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>; tests fire it manually. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct Rotting Regisaur with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the upkeep
    /// trigger is registered so an Upkeep <see cref="StepStartedEvent"/> for
    /// the controller automatically places it on the stack.</param>
    /// <param name="agent">Optional agent for the controller's discard pick
    /// (CR 701.8 — the discarding player chooses). Null falls back to a
    /// deterministic first-card pick.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Zombie Dinosaur, {2}{B}, 7/6). No abilities in the JSON — the
        // printed upkeep trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Upkeep self-discard trigger — CR 603.1, CR 500.4, CR 701.8.
        //   "At the beginning of your upkeep, discard a card."
        // Triggers.OnStepBegin filters StepStartedEvent on (Upkeep,
        // controller) so it only fires on the controller's own upkeeps.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: at the beginning of your upkeep, discard a card",
            () => ControllerDiscardsOne(card, owner, agent));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 701.8 — the controller discards one card of their choice. An empty
    /// hand → no discard (nothing to discard). Agent-driven when an agent is
    /// supplied, else deterministic first-card-in-hand.
    /// </summary>
    private static void ControllerDiscardsOne(Creature card, Player owner, IPlayerAgent? agent)
    {
        var controller = card.Controller ?? owner;

        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // empty hand → nothing to discard.

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromHandAsync(controller, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        controller.Zones.Hand.RemoveCard(pick);
        controller.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
