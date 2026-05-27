using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Frost (Ice Age / many reprints, {1}{U}{U}).
///
/// Creature — Wall, mana cost {1}{U}{U}, 0/7.
/// Oracle text:
///   "Defender.
///    Whenever this creature blocks a creature, that creature doesn't
///    untap during its controller's next untap step."
///
/// ## Implemented (v1)
/// - 0/7 Creature — Wall, mana cost {1}{U}{U}, owner/controller wired.
/// - <b>Defender keyword (CR 702.3)</b>: wired as a
///   <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/> surfaces
///   it for block-legality (Wall of Frost cannot be declared as an
///   attacker). Mirrors <see cref="WallOfRootsFactory"/>.
/// - <b>"Whenever this creature blocks a creature" trigger (CR 603.1)</b>:
///   fires on <see cref="BlockersDeclaredEvent"/> when Wall of Frost
///   appears as a blocker in the declared set. The engine fires a single
///   <see cref="BlockersDeclaredEvent"/> carrying the whole combat; the
///   trigger condition filters to "this card is listed as a blocker".
///   This is the same binding pattern used by
///   <see cref="SmugglersCopterFactory"/>'s "attacks or blocks" trigger.
///   For each attacker that Wall of Frost is blocking, the effect
///   registers that attacker with
///   <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> so
///   it skips its controller's next untap step (CR 502.1).
///   - <b>"Next untap step" one-shot cleanup (CR 611.2b)</b>: when an
///     <see cref="IEventBus"/> is supplied, a one-shot
///     <see cref="StepStartedEvent"/> handler removes each skip after the
///     first <see cref="PhaseStateType.Untap"/> step belonging to the
///     blocked creature's controller.
///   - Wall of Frost does <b>not</b> tap the blocked creature (distinct
///     from Frost Lynx's ETB). CR 509.1 — the skip-untap is the sole
///     effect of this trigger.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — shape-only (no bus/trigger wiring).
///   Suitable for identity tests and the <see cref="NamedCardFactory"/>
///   dispatcher.
/// - <see cref="Create(Player, TriggerManager?)"/> — attaches the trigger
///   to a <see cref="TriggerManager"/>; skip-untap registrations persist
///   until <see cref="UntapStepRestrictions.Clear"/> (tests Dispose).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — full
///   wiring: trigger registration + one-shot cleanup subscriptions for
///   "next untap step" behaviour.
/// </summary>
[CardName("Wall of Frost")]
public static class WallOfFrostFactory
{
    public const string CardName = "Wall of Frost";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 0;
    public const int Toughness = 7;

    /// <summary>
    /// Construct Wall of Frost with no bus/trigger wiring. Suitable for
    /// identity tests and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Wall of Frost with trigger-manager wiring but no bus-
    /// driven one-shot cleanup.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
        => Create(owner, triggers, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Wall of Frost.
    ///
    /// When <paramref name="triggers"/> is supplied, the blocks trigger
    /// is registered for bus-driven firing. When <paramref name="eventBus"/>
    /// is also supplied, each skip-untap registration receives a one-shot
    /// <see cref="StepStartedEvent"/> cleanup handler (CR 611.2b).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the blocks trigger
    /// against. May be null — trigger is structurally attached only.</param>
    /// <param name="eventBus">Event bus for one-shot skip-untap cleanup.
    /// May be null — skip-untap persists until
    /// <see cref="UntapStepRestrictions.Clear"/> is called.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wall });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality.
        // Mirrors WallOfRootsFactory.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // --------------------------------------------------------------------
        // "Whenever this creature blocks a creature, that creature doesn't
        //  untap during its controller's next untap step." (CR 603.1)
        //
        // Engine hook: BlockersDeclaredEvent fires once per declare-blockers
        // step, carrying the full combat. The trigger condition fires when
        // this card appears in the combat's blocker list (same pattern as
        // SmugglersCopterFactory's blocks condition — CR 509.1g).
        //
        // The effect needs to know which attacker(s) Wall of Frost was
        // blocking. TriggeredAbility.Effects do not receive the triggering
        // event, so we capture the triggering combat in a closure array
        // (same technique as WallOfRootsFactory's usedThisTurn[] slot)
        // at condition-evaluation time and consume it at effect-resolution
        // time. The slot is per-Create-invocation, keyed by the card
        // instance, so concurrent cards each get their own independent
        // capture.
        // --------------------------------------------------------------------
        var capturedCombat = new Majik.Core.Combat.Combat?[] { null };

        var blocksTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<BlockersDeclaredEvent>((e, _) =>
            {
                // CR 509.1g — fires when Wall of Frost is a declared blocker.
                var isBlocker = e.Combat.GetAllBlockers()
                    .Any(b => ReferenceEquals(b.Creature, card));
                if (isBlocker)
                {
                    // Capture the combat so the effect can look up the attacker.
                    capturedCombat[0] = e.Combat;
                }
                return isBlocker;
            }),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: blocked creature(s) skip their controller's next untap step",
                    () =>
                    {
                        var combat = capturedCombat[0];
                        capturedCombat[0] = null; // consume

                        if (combat == null) return;

                        // For each attacker that Wall of Frost was blocking,
                        // register a skip-untap restriction (CR 502.1).
                        foreach (var blocker in combat.GetAllBlockers()
                                     .Where(b => ReferenceEquals(b.Creature, card)))
                        {
                            var attacked = blocker.BlockedAttacker.Creature;
                            if (attacked.Zone != ZoneType.Battlefield) continue;

                            var skipToken = new object();
                            UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, attacked);

                            if (eventBus != null)
                            {
                                // CR 611.2b — one-shot: remove the skip on the
                                // first Untap step that belongs to the attacked
                                // creature's current controller.
                                var targetController = attacked.Controller;
                                Action<GameEvent>? cleanupHandler = null;
                                cleanupHandler = ev =>
                                {
                                    if (ev is not StepStartedEvent sse) return;
                                    if (sse.StepType != PhaseStateType.Untap) return;
                                    if (!ReferenceEquals(sse.Player, targetController)) return;

                                    UntapStepRestrictions.RemoveAll(skipToken);
                                    if (cleanupHandler != null)
                                        eventBus.UnsubscribeAll(cleanupHandler);
                                };
                                eventBus.SubscribeAll(cleanupHandler);
                            }
                        }
                    }),
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(blocksTrigger);
        triggers?.RegisterTriggeredAbility(blocksTrigger);

        return card;
    }
}
