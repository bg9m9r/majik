using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Combat Celebrant (Hour of Devastation, {2}{R}).
/// Creature — Human Warrior 4/1. Oracle text (verified against Scryfall):
///   "If this creature hasn't been exerted this turn, you may exert it as
///    it attacks. When you do, untap all other creatures you control and
///    after this phase, there is an additional combat phase. (An exerted
///    creature won't untap during your next untap step.)"
///
/// ## Implementation
///
/// Combat Celebrant is the card-triggered driver of the additional-combat
/// machinery (CR 506.4). Its exert trigger mirrors
/// <see cref="GlorybringerFactory"/>'s "you may exert as it attacks" shape:
/// an <see cref="AttackersDeclaredEvent"/> trigger gated to "this card's
/// controller is the attacking player AND this card is among the declared
/// attackers" (CR 508.1f / 702.139a). On resolution, when the controller
/// chooses to exert:
///
///   1. <b>"hasn't been exerted this turn"</b> (CR 702.139b) — a boxed
///      once-per-turn cell, reset on each <see cref="TurnStartedEvent"/>.
///      A creature can be exerted only once per turn; the chooser is never
///      offered a second time the same turn.
///   2. <b>CR 702.139c exert rider</b> — "won't untap during your next
///      untap step." Registered via
///      <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>; lifts
///      on the controller's next <see cref="PhaseStateType.Untap"/> step when
///      an event bus is wired (same posture as Glorybringer / Arena of Glory).
///   3. <b>"untap all OTHER creatures you control"</b> (CR 701.20a) — every
///      creature the controller controls except Combat Celebrant itself.
///   4. <b>"after this phase, there is an additional combat phase"</b>
///      (CR 506.4) — enqueue a combat-ONLY grant on the per-game
///      <see cref="AdditionalCombatRegistryProvider"/> queue that
///      <see cref="TurnDriver"/> drains after the current combat. No
///      additional main phase follows (unlike Relentless Assault / World at
///      War), so this enqueues with <c>followedByMainPhase: false</c>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (the
///   <see cref="NamedCardFactory"/> dispatch target). The exert trigger is
///   attached for observability but declines (no chooser).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?, Func{bool}?)"/>
///   — fully wired: the once-per-turn gate resets on <see cref="TurnStartedEvent"/>
///   and the untap-skip rider lifts on the next untap step.
///
/// ## Deferred (v1 gaps, isolated)
/// - <b>Agent-driven exert choice</b>: the exert decision is a
///   <see cref="bool"/> chooser, not a full agent prompt (same posture as
///   Glorybringer / Inti). The trigger itself is production-wired through the
///   live <see cref="TriggerManager"/>.
/// </summary>
[CardName("Combat Celebrant")]
public static class CombatCelebrantFactory
{
    public const string CardName = "Combat Celebrant";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 4;
    public const int Toughness = 1;

    /// <summary>Construct Combat Celebrant with no live wiring (the dispatch
    /// target). The exert trigger is attached but declines (no chooser).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, mayExert: null);

    /// <summary>Construct Combat Celebrant with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the exert attack trigger is
    /// registered so it surfaces as pending.</param>
    /// <param name="eventBus">When supplied, the once-per-turn exert gate is
    /// reset on each <see cref="TurnStartedEvent"/> and the "won't untap"
    /// rider lifts on the controller's next <see cref="PhaseStateType.Untap"/>
    /// step.</param>
    /// <param name="mayExert">"You may exert it as it attacks" chooser.
    /// Returns true to exert. Null defaults to declining (the safe shape-only
    /// posture).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus,
        Func<bool>? mayExert = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            CardName, PrintedManaCost, Power, Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.139b — "If this creature hasn't been exerted this turn."
        // Boxed once-per-turn cell shared by the resolve body (sets it the
        // first time the controller exerts) + the TurnStartedEvent reset.
        var exertedThisTurn = new bool[] { false };

        Majik.Core.Combat.Combat? capturedCombat = null;

        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
        {
            // "As it attacks" (CR 508.1f / 702.139a) — only when this card's
            // controller is the attacking player AND this card is among the
            // declared attackers. (CR 702.139b — "hasn't been exerted this
            // turn" is checked at resolution so a re-declare can't slip past
            // the gate, mirroring the chooser guard below.)
            var controller = card.Controller ?? owner;
            if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
            if (!e.Combat.Attackers.Any(a => ReferenceEquals(a?.Creature, card))) return false;
            capturedCombat = e.Combat;
            return true;
        });

        var exertEffect = new Effect(
            $"{CardName}: may exert as it attacks; when you do, untap all OTHER creatures you control + an additional combat phase",
            () =>
            {
                var combat = capturedCombat;
                capturedCombat = null;
                if (combat == null) return;

                var controller = card.Controller ?? owner;

                // CR 702.139b — only if it hasn't already been exerted this
                // turn. (A creature can be exerted only once per turn.)
                if (exertedThisTurn[0]) return;

                // "You may exert it as it attacks." CR 702.139a. Default:
                // decline (shape-only posture).
                var wantsExert = mayExert?.Invoke() ?? false;
                if (!wantsExert) return;

                exertedThisTurn[0] = true;

                // CR 702.139c — "won't untap during your next untap step."
                UntapStepRestrictions.MarkPermanentDoesNotUntap(card, card);
                ScheduleNextUntapClear(card, controller, eventBus);

                // "When you do, untap all OTHER creatures you control."
                // CR 701.20a. Combat Celebrant itself is excluded (it stays
                // exerted/tapped), so the extra combat needs fresh attackers.
                foreach (var c in controller.Zones.Battlefield.GetCards()
                             .OfType<Creature>().ToList())
                {
                    if (ReferenceEquals(c, card)) continue;
                    if (c.IsTapped) c.Untap();
                }

                // "and after this phase, there is an additional combat phase."
                // CR 506.4 — enqueue a combat-ONLY grant (no following main
                // phase) on the per-game queue TurnDriver drains.
                AdditionalCombatRegistryProvider.Current.EnqueueAdditional(
                    followedByMainPhase: false);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { exertEffect },
            // CR 113.6 — the trigger functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 702.139b — reset the once-per-turn exert gate at the start of
        // each turn.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => exertedThisTurn[0] = false);
        }

        return card;
    }

    private static void ScheduleNextUntapClear(Creature card, Player controller, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.Untap) return;
            if (!ReferenceEquals(e.Player, controller)) return;

            UntapStepRestrictions.RemoveAll(card);
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
