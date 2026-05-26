using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodchief Ascension (Zendikar, {1}{B}).
///
/// Enchantment. Oracle text:
///   "At the beginning of each opponent's end step, if an opponent lost
///    2 or more life this turn, you may put a quest counter on Bloodchief
///    Ascension."
///   "As long as Bloodchief Ascension has three or more quest counters
///    on it, whenever a card is put into an opponent's graveyard from
///    anywhere, you may have that opponent lose 2 life. If you do, you
///    gain 2 life."
///
/// ## Implementation
///
/// - <b>Enchantment {1}{B}</b> — vanilla <see cref="Enchantment"/> shell.
/// - <b>End-step quest-counter trigger (CR 500.4 / CR 603.1 / CR 603.4)</b>:
///   a <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/>
///   gated on (<see cref="StepStartedEvent.StepType"/> ==
///   <see cref="PhaseStateType.End"/>) AND
///   (<see cref="StepStartedEvent.Player"/> is NOT Bloodchief Ascension's
///   controller). Intervening-if (CR 603.4) checks the active end-step
///   player's <see cref="Player.LifeLostThisTurn"/> &gt;=
///   <see cref="LifeLostThreshold"/> at both trigger time AND resolution
///   time. The predicate stamps a closure slot with the triggering
///   opponent so resolution can re-read their tracker. On resolution
///   the controller MAY place a <see cref="CounterType.Quest"/> counter
///   on Bloodchief Ascension — v1 always takes the "may" (no agent
///   prompt yet; same posture as the rest of the engine's power-positive
///   "may" surface).
/// - <b>Drain trigger (CR 121 / CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   filtered to (<see cref="CardMovedEvent.ToZone"/> ==
///   <see cref="ZoneType.Graveyard"/>) AND (the moved card's
///   <see cref="ICard.Owner"/> is NOT Bloodchief Ascension's controller
///   — the graveyard's owner is the card's owner per CR 404.2). The
///   "as long as Bloodchief Ascension has three or more quest counters"
///   clause is a static gate on the trigger predicate AND a resolution
///   recheck. On resolution the controller MAY have the opponent lose
///   2 life; if they do, the controller gains 2 life (paired CR 117.6
///   / CR 605.1). v1 always takes the "may".
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits trigger
/// wiring and produces a shape-only card. The
/// <see cref="Create(Player, TriggerManager?)"/> overload registers
/// both triggers so bus-driven firing works.
///
/// ## Deferred
///
/// - <b>Agent "may" prompts</b>: both triggers' "you may …" choice is
///   stubbed to "always yes" in v1. Real prompting depends on the
///   <see cref="Players.Agents.IPlayerAgent"/> surface used by the rest
///   of the optional-cost family (Bloodghast / Crawling Barrens) —
///   same posture, same deferral.
/// - <b>Multi-opponent "an opponent lost 2 or more life this turn"</b>:
///   in 1v1 this collapses to "the lone opponent's
///   <see cref="Player.LifeLostThisTurn"/>". v1 reads the triggering
///   end-step opponent's tracker directly via the StepStartedEvent
///   payload; in a true multiplayer game the predicate would also
///   need to scan the controller's full opponent set, which the engine
///   doesn't currently expose statically.
/// </summary>
[CardName("Bloodchief Ascension")]
public static class BloodchiefAscensionFactory
{
    public const string CardName = "Bloodchief Ascension";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Quest-counter threshold (CR 121 — printed "three or more").</summary>
    public const int QuestThreshold = 3;

    /// <summary>Life-lost threshold for the quest-counter trigger (printed "2 or more").</summary>
    public const int LifeLostThreshold = 2;

    /// <summary>Drain amount on the threshold-gated graveyard trigger.</summary>
    public const int DrainAmount = 2;

    /// <summary>
    /// Shape-only constructor — triggers attached for shape but NOT
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// factory-shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Bloodchief Ascension. When <paramref name="triggers"/>
    /// is supplied both the quest-counter trigger and the threshold-gated
    /// drain trigger are registered against it so bus-driven
    /// <see cref="StepStartedEvent"/> + <see cref="CardMovedEvent"/>
    /// flows queue them automatically.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(name: CardName, manaCost: PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Quest-counter trigger — CR 500.4 / CR 603.1 / CR 603.4.
        //   "At the beginning of each opponent's end step, if an opponent
        //    lost 2 or more life this turn, you may put a quest counter
        //    on Bloodchief Ascension."
        //
        // Predicate stamps a closure slot with the active end-step
        // opponent so resolution can re-read the tracker (CR 603.4 — the
        // intervening-if is checked twice: at trigger time AND on
        // resolution).
        // ----------------------------------------------------------------
        Player? lastEndStepOpponent = null;

        var questEffect = new Effect(
            $"{CardName}: put a quest counter on it (CR 121)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var opp = lastEndStepOpponent;
                if (opp == null) return;
                if (opp.LifeLostThisTurn < LifeLostThreshold) return; // CR 603.4 recheck
                card.Counters.Add(CounterType.Quest, 1);
            });

        var questTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>((e, _) =>
            {
                if (e.StepType != PhaseStateType.End) return false;
                var ctrl = card.Controller ?? owner;
                if (ReferenceEquals(e.Player, ctrl)) return false; // "each opponent's end step"
                if (e.Player.LifeLostThisTurn < LifeLostThreshold) return false;

                lastEndStepOpponent = e.Player;
                return true;
            }),
            effects: new IEffect[] { questEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(questTrigger);
        triggers?.RegisterTriggeredAbility(questTrigger);

        // ----------------------------------------------------------------
        // Drain trigger — CR 121 / CR 603.1.
        //   "As long as Bloodchief Ascension has three or more quest
        //    counters on it, whenever a card is put into an opponent's
        //    graveyard from anywhere, you may have that opponent lose 2
        //    life. If you do, you gain 2 life."
        //
        // Static-gated trigger (CR 603.6e): predicate FALSE while the
        // gate is closed, so the ability never sees the event below
        // threshold. Predicate stamps a closure slot with the opponent
        // whose graveyard received the card; resolution drains them
        // and the controller gains the paired 2 life.
        // ----------------------------------------------------------------
        Player? lastDrainOpponent = null;

        var drainEffect = new Effect(
            $"{CardName}: opponent loses 2 life; if so, you gain 2 life (paired CR 117.6)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.Counters.Count(CounterType.Quest) < QuestThreshold) return; // recheck
                var opponent = lastDrainOpponent;
                if (opponent == null) return;

                opponent.LoseLife(DrainAmount);
                var ctrl = card.Controller ?? owner;
                ctrl.GainLife(DrainAmount);
            });

        var drainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Graveyard) return false;
                if (card.Counters.Count(CounterType.Quest) < QuestThreshold) return false;

                // CR 404.2 — a graveyard is owned by its player; "into
                // an opponent's graveyard" reads the moved card's
                // OWNER (graveyards never change owner). Filtering on
                // owner instead of controller correctly catches mills
                // (library → graveyard, owner-side move) and the
                // assorted exile / hand → graveyard paths too.
                var ctrl = card.Controller ?? owner;
                var graveOwner = e.Card.Owner;
                if (graveOwner == null || ReferenceEquals(graveOwner, ctrl)) return false;

                lastDrainOpponent = graveOwner;
                return true;
            }),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drainTrigger);
        triggers?.RegisterTriggeredAbility(drainTrigger);

        return card;
    }
}
