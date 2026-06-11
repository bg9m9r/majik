using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gibbering Descent (Time Spiral, {4}{B}{B}).
///
/// Enchantment. Oracle text (current Scryfall):
///   "At the beginning of each player's upkeep, that player loses 1 life and
///    discards a card.
///    Hellbent — Skip your upkeep step if you have no cards in hand.
///    Madness {2}{B}{B} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Shape source
/// Card identity (name, {4}{B}{B}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/gibbering-descent.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The upkeep trigger (incl. the Hellbent
/// skip) is attached in code below.
///
/// ## Implemented (v1)
///
/// - Enchantment {4}{B}{B}, owner/controller wired.
/// - <b>Each-player's-upkeep triggered ability (CR 603.1 / CR 500.4)</b>: fires
///   on <see cref="StepStartedEvent"/> matching <see cref="StepStateType.Upkeep"/>
///   for ANY player (the printed text reads "each player's upkeep", same
///   symmetric shape as <see cref="SulfuricVortexFactory"/> /
///   <see cref="AsylumVisitorFactory"/>). The active upkeep player is captured
///   off <see cref="StepStartedEvent.Player"/>. On resolution <b>that player</b>
///   (not the controller) loses 1 life (<see cref="Player.LoseLife"/>,
///   CR 119.3) and discards a card (<see cref="Fx.Discard"/>, CR 701.8 — routes
///   through the central discard funnel so a discarded Madness card is offered
///   for its madness cost automatically).
/// - <b>Hellbent — "Skip your upkeep step if you have no cards in hand"
///   (CR 702.46 ability word; the rules effect is a CR 614 / CR 725 step
///   skip)</b>: modeled functionally as an intervening-if (CR 603.4) on the
///   trigger. A skipped step performs no turn-based / triggered actions, so
///   suppressing this card's own upkeep trigger when the CONTROLLER's upkeep
///   starts with an empty hand is behaviourally equivalent to skipping the
///   controller's upkeep step (this card is the only thing keyed off the
///   skipped step in the engine today — see "Deferred" for the engine-wide
///   gap). Hellbent only skips the CONTROLLER's own upkeep: the trigger always
///   fires on an opponent's upkeep regardless of the controller's hand.
/// - <b>Madness {2}{B}{B} (CR 702.35)</b> — INTRINSIC. Handled engine-wide via
///   <c>Majik.Core/Keywords/MadnessCatalog.cs</c> + the
///   <see cref="Fx.DiscardCard"/> funnel; no per-card wiring here.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The upkeep trigger is attached
///   for shape observability; nothing is registered on a trigger manager.
/// - <see cref="Create(Player, TriggerManager?)"/> — upkeep trigger registered.
///
/// ## Deferred (v1 gaps)
///
/// - <b>First-listed-card discard</b>: <see cref="Fx.Discard"/> discards the
///   first card in hand rather than prompting the upkeep player to choose
///   (CR 701.8a "the player chooses"). Same agent-driven-choice gap as
///   Faithless Looting / Liliana of the Veil; tracked engine-wide, not a
///   Gibbering Descent regression.
/// - <b>True step-skip plumbing</b>: the engine has no first-class "skip this
///   step" surface (CR 725), so Hellbent is modeled as the trigger-suppression
///   above. The only observable difference would be OTHER abilities keyed off
///   the controller's upkeep step — none exist alongside this card today. A
///   future CR 725 step-skip pass would let this card request the skip directly
///   instead of self-suppressing.
/// </summary>
[CardName("Gibbering Descent")]
public static class GibberingDescentFactory
{
    public const string CardName = "Gibbering Descent";
    public const string PrintedManaCost = "{4}{B}{B}";
    public const int LifeLoss = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("gibbering-descent");

    /// <summary>
    /// Construct Gibbering Descent with no live trigger manager. The upkeep
    /// trigger is attached for shape observability only.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Gibbering Descent with optional <see cref="TriggerManager"/>
    /// wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied, the
    /// each-player's-upkeep trigger registers so the bus drives it
    /// automatically.</param>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Each-player's-upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of each player's upkeep, that player loses 1
        //    life and discards a card."
        // Symmetric: fires on EVERY player's upkeep (active player whose
        // upkeep is starting, read off StepStartedEvent.Player).
        //
        // Hellbent (CR 702.46) — "Skip your upkeep step if you have no cards
        // in hand." A skipped step performs no triggered actions, so we gate
        // the trigger with an intervening-if (CR 603.4) that suppresses it on
        // the CONTROLLER's own upkeep when the controller has an empty hand.
        // It never suppresses an opponent's upkeep.
        // ----------------------------------------------------------------
        Player? upkeepPlayer = null;

        // CR 702.46 step-skip, modeled as trigger suppression: only the
        // controller's own upkeep is skipped, and only when the controller
        // holds no cards.
        bool HellbentSuppresses(Player p) =>
            ReferenceEquals(p, card.Controller ?? owner)
            && !p.Zones.Hand.GetCards().Any();

        var upkeepCondition = new EventTriggerCondition<StepStartedEvent>((e, _) =>
        {
            if (e.StepType != StepStateType.Upkeep) return false;
            if (HellbentSuppresses(e.Player)) return false;
            upkeepPlayer = e.Player;
            return true;
        });

        var upkeepEffect = new Effect(
            $"{CardName}: the active upkeep player loses {LifeLoss} life and discards a card",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var target = upkeepPlayer;
                upkeepPlayer = null;
                if (target == null) return;
                if (target.HasLost) return;

                // CR 119.3 — life loss (not damage).
                target.LoseLife(LifeLoss);
                // CR 701.8 — discard a card. Routes through the central
                // discard funnel (Fx.DiscardCard) so a discarded Madness card
                // is offered for its madness cost automatically.
                Fx.Discard(target, 1);
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: upkeepCondition,
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return card;
    }
}
