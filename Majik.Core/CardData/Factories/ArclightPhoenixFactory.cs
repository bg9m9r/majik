using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arclight Phoenix (Guilds of Ravnica, {3}{R}).
///
/// Creature — Phoenix 3/2. Oracle text:
///   "Flying. Haste.
///    At the beginning of combat on your turn, if you've cast three or more
///    instant and/or sorcery spells this turn, you may return Arclight
///    Phoenix from your graveyard to the battlefield."
///
/// ## Implemented (v1)
/// - 3/2 Phoenix creature with mana cost {3}{R}.
/// - <see cref="KeywordAbility"/> markers for Flying (CR 702.9) and Haste
///   (CR 702.10).
/// - Triggered ability scoped to <see cref="ZoneType.Graveyard"/>
///   (CR 603.6d — abilities that function only from a non-battlefield zone),
///   firing on <see cref="StepStartedEvent"/> for
///   <see cref="Majik.Core.StateMachine.PhaseStateType.BeginningOfCombat"/>
///   filtered to the controller's own turns. On match, the effect checks the
///   instant+sorcery cast count for this turn and — when ≥ 3 — moves the
///   Phoenix from its controller's graveyard to the controller's battlefield.
/// - Per-turn instant+sorcery count is held in a closure private to this
///   card instance, incremented on every <see cref="SpellCastEvent"/> whose
///   spell is controlled by the Phoenix's controller and whose card has
///   <see cref="CardType.Instant"/> or <see cref="CardType.Sorcery"/>. Reset
///   on each <see cref="TurnStartedEvent"/> when an event bus is supplied
///   (CR 500.1).
/// - "From your graveyard" is enforced at resolve time by re-checking the
///   Phoenix's zone before moving it — if it's not in the controller's
///   graveyard the effect no-ops, satisfying CR 603.10's "intervening if"
///   shape for the printed condition.
/// - "May" is auto-accepted at v1 (same simplification as Sneak Attack /
///   Through the Breach / Tireless Tracker's Clue trigger). The whole
///   ability still no-ops when the cast count is below 3.
///
/// ## Deferred (v1 gaps)
/// - Real "you may" prompt — no Yes/No agent surface yet.
/// - <c>activeZones = {Graveyard}</c> on the trigger relies on the
///   <see cref="TriggerManager"/> respecting graveyard-resident triggers.
///   The single-arg dispatcher path attaches the trigger to the card so
///   the ability shape is observable; bus-driven firing requires the
///   (owner, bus, triggers) overload.
/// - Cast count counts every controller instant/sorcery cast — including
///   the Phoenix's own combat re-trigger window (the count is captured at
///   the moment of the begin-combat step, so spells cast AFTER the trigger
///   resolves don't retroactively requalify the Phoenix the same turn).
/// </summary>
[CardName("Arclight Phoenix")]
public static class ArclightPhoenixFactory
{
    public const string CardName = "Arclight Phoenix";
    public const string PrintedManaCost = "{3}{R}";

    /// <summary>
    /// Construct Arclight Phoenix with no live bus / trigger-manager wiring.
    /// The triggered ability is attached to the card so structural tests
    /// (and the <see cref="NamedCardFactory"/> dispatch path) see the
    /// ability shape, but the trigger is not registered with a
    /// <see cref="TriggerManager"/>; tests fire it manually via
    /// <see cref="TriggeredAbility.IsTriggered"/> or by executing the
    /// effect directly. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Arclight Phoenix with optional event bus + trigger manager.
    /// When <paramref name="eventBus"/> is supplied, a
    /// <see cref="SpellCastEvent"/> subscription increments the per-turn
    /// instant+sorcery count and a <see cref="TurnStartedEvent"/>
    /// subscription resets it. When <paramref name="triggers"/> is supplied,
    /// the begin-combat trigger is registered so a
    /// <see cref="StepStartedEvent"/> for
    /// <see cref="Majik.Core.StateMachine.PhaseStateType.BeginningOfCombat"/>
    /// on the controller's turn automatically places it on the stack.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
        => Create(owner, eventBus, triggers, agent: null);

    /// <summary>
    /// Construct Arclight Phoenix with the agent-prompt MVP wiring. When
    /// <paramref name="agent"/> is supplied, the "you may return" trigger
    /// consults <see cref="IPlayerAgent.ChooseYesNoAsync"/>
    /// (<see cref="BotIntent.Reanimate"/> | <see cref="BotIntent.CardAdvantage"/>);
    /// false declines and leaves the Phoenix in the graveyard. Null
    /// preserves the legacy auto-accept posture.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Phoenix });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. CR 702.10 — Haste.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Per-turn instant+sorcery cast count. Closure shared between the
        // SpellCastEvent subscription and the trigger effect.
        // ----------------------------------------------------------------
        var instantSorceryCastsThisTurn = new int[] { 0 };

        if (eventBus != null)
        {
            // CR 603.2 — count only the controller's own casts. Filter by
            // CardType.Instant / Sorcery.
            eventBus.Subscribe<SpellCastEvent>(e =>
            {
                if (!ReferenceEquals(e.Spell.Controller, owner)) return;
                var spellCard = e.Spell.Card;
                if (spellCard.HasType(CardType.Instant) || spellCard.HasType(CardType.Sorcery))
                {
                    instantSorceryCastsThisTurn[0]++;
                }
            });

            // CR 500.1 — reset the per-turn count when a new turn starts.
            eventBus.Subscribe<TurnStartedEvent>(_ => instantSorceryCastsThisTurn[0] = 0);
        }

        // ----------------------------------------------------------------
        // Begin-combat trigger — CR 603.1 / CR 603.6d / CR 500.4.
        //   "At the beginning of combat on your turn, if you've cast three
        //    or more instant and/or sorcery spells this turn, you may
        //    return Arclight Phoenix from your graveyard to the
        //    battlefield."
        // Triggers.OnStepBegin filters StepStartedEvent on
        // (BeginningOfCombat, controller) so it only fires on the
        // controller's own combat steps.
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard to battlefield if ≥3 instant/sorcery spells cast this turn",
            () =>
            {
                // CR 603.10 — intervening "if". Re-check the count at
                // resolve time. "May" prompt: when an agent is wired,
                // consult ChooseYesNoAsync(Reanimate | CardAdvantage)
                // before the return. No agent → legacy auto-accept.
                if (instantSorceryCastsThisTurn[0] < 3) return;
                if (agent != null)
                {
                    var yes = agent.ChooseYesNoAsync(
                        "Return Arclight Phoenix from graveyard to battlefield?",
                        BotIntent.Reanimate | BotIntent.CardAdvantage)
                        .GetAwaiter().GetResult();
                    if (!yes) return;
                }

                // CR 603.6d — the ability functions from graveyard. Guard
                // against the Phoenix having moved out of the graveyard
                // between trigger detection and resolution.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                owner.Zones.Graveyard.RemoveCard(card);
                owner.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(owner);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.BeginningOfCombat),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(trigger);

        // Live registration with TriggerManager so the bus actually surfaces
        // the trigger as pending when a BeginningOfCombat step starts.
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
