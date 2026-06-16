using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pact of Negation (Future Sight, {0}).
///
/// Instant. Oracle text:
///   "Counter target spell.
///    At the beginning of your next upkeep, pay {3}{U}{U}. If you don't,
///    you lose the game."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {0}, owner / controller.
/// - <b>Counter target spell</b> — <see cref="BuildDefinition"/> builds a
///   <see cref="SpellDefinition"/> whose effect mirrors
///   <c>CounterSpellFactory.CounterTargetSpell</c> (CR 701.5 — remove from
///   stack + send to graveyard).
/// - <b>Delayed upkeep pact</b> — <see cref="BuildResolveEffect"/> wraps
///   the counter effect and, when a <see cref="TriggerManager"/> is
///   supplied, registers a <see cref="DelayedTriggeredAbility"/> (CR 603.7)
///   that fires on the controller's next <see cref="StepStateType.Upkeep"/>
///   <see cref="StepStartedEvent"/>. The trigger's effect calls
///   <see cref="Player.PayMana"/> with {3}{U}{U} against the controller's
///   mana pool; on failure the controller is flagged via
///   <see cref="Player.MarkLost"/> (CR 104.3 / CR 118.3 — "if you don't,
///   you lose the game").
///
/// ## Cost-payment prompt
/// At upkeep the caster's agent is prompted "Pay {3}{U}{U} or lose the game?"
/// via the shared
/// <see cref="Majik.Core.Primitives.UpkeepPayUnlessConsequence"/> primitive
/// (CR 117.1); on "yes" + affordable {3}{U}{U} is drained, on "no" /
/// can't-afford the caster loses (CR 104.3 / CR 118.3). The legacy /
/// shape-only sync path keeps the deterministic "pay if able" posture.
///
/// ## Deferred (v1 gaps)
/// - <b>No in-trigger tap-lands step</b>: the {3}{U}{U} is paid from
///   whatever is already in the caster's pool when the delayed trigger
///   resolves — the decision flows through the agent prompt, but there is no
///   resolution-time "tap a land for {3}{U}{U}" sub-prompt.
/// - <b>"At the beginning of your next upkeep"</b> single-step approximation
///   mirrors <see cref="MishrasBaubleFactory"/> / <see cref="WrennsResolveFactory"/>:
///   the trigger fires on the first Upkeep <see cref="StepStartedEvent"/>
///   with timestamp strictly after the resolve. Multi-player turn-skipping
///   nuances deferred.
/// </summary>
[CardName("Pact of Negation")]
public static class PactOfNegationFactory
{
    public const string CardName = "Pact of Negation";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct the Pact of Negation card shape (Instant, {0}). Resolve
    /// behaviour is built on demand via <see cref="BuildDefinition"/> so
    /// the dispatcher path can produce a shape-only card.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a
    /// single spell; on resolution counters it and (when a
    /// <see cref="TriggerManager"/> is supplied) registers a one-shot
    /// delayed trigger that fires at the caster's next upkeep.
    /// </summary>
    /// <param name="caster">The controller of Pact of Negation — also
    /// the controller of the delayed upkeep trigger.</param>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object (typically an <see cref="ISpell"/>).</param>
    /// <param name="stack">The active stack; required to remove the
    /// countered spell. When null the counter no-ops (shape only).</param>
    /// <param name="triggers">Optional trigger manager. When null the
    /// delayed upkeep payment / loss is skipped (suitable for
    /// counter-only shape tests).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Pact of Negation: counter target spell + queue delayed upkeep pact", () =>
                    {
                        // ----------------------------------------------------
                        // CR 701.5 — counter the target spell. Same shape as
                        // CounterSpellFactory.CounterTargetSpell.
                        // ----------------------------------------------------
                        if (stack != null && resolved is ISpell spell)
                        {
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }

                        // ----------------------------------------------------
                        // CR 603.7 — register the "at the beginning of your
                        // next upkeep" delayed trigger. The trigger:
                        //   - fires on the first Upkeep StepStartedEvent
                        //     with timestamp strictly after this resolve
                        //     (mirrors MishrasBauble / WrennsResolve),
                        //   - tries to pay {3}{U}{U} out of the controller's
                        //     mana pool; on failure flags the controller as
                        //     having lost the game (CR 104.3 / CR 118.3).
                        // ----------------------------------------------------
                        if (triggers == null) return;

                        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                        var pactCost = Majik.Core.ValueObjects.ManaCost.Parse("{3}{U}{U}");

                        // CR 117.1 — at upkeep the caster's agent is prompted
                        // "Pay {3}{U}{U}?" via the shared
                        // Majik.Core.Primitives.UpkeepPayUnlessConsequence
                        // primitive; on "yes" + affordable {3}{U}{U} is drained,
                        // on "no" / can't-afford the caster loses the game
                        // (CR 104.3 / CR 118.3). The legacy / shape-only sync
                        // path keeps the deterministic "pay if able" posture.
                        var payOrLoseEffect = Majik.Core.Primitives.UpkeepPayUnlessConsequence.Build(
                            "Pact of Negation: pay {3}{U}{U} at upkeep or lose the game",
                            caster,
                            pactCost,
                            consequence: caster.MarkLost,
                            promptText: "Pay {3}{U}{U} or lose the game?",
                            guard: () => !caster.HasLost);

                        var delayed = new DelayedTriggeredAbility(
                            source: caster,
                            controller: caster,
                            condition: new EventTriggerCondition<StepStartedEvent>(
                                (e, _) => e.StepType == StepStateType.Upkeep
                                          && ReferenceEquals(e.Player, caster)
                                          && e.Timestamp > resolvedAt),
                            effects: new IEffect[] { payOrLoseEffect });

                        triggers.RegisterDelayed(delayed);
                    }),
                };
            });
    }
}
