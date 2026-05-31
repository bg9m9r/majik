using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slaughter Pact (Future Sight, {0}).
///
/// Instant. Oracle text:
///   "Destroy target nonblack creature.
///    At the beginning of your next upkeep, pay {2}{B}. If you don't,
///    you lose the game."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {0}, owner / controller.
/// - <b>Destroy target nonblack creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single
///   <see cref="TargetRequest"/> for a creature; on resolution the target
///   is filtered via <see cref="CardColors.GetColors"/> (CR 105) and moved
///   to its owner's graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7 — Destroy).
/// - <b>Delayed upkeep pact</b> — mirrors the
///   <see cref="PactOfNegationFactory"/> pattern. When a
///   <see cref="TriggerManager"/> is supplied the resolve effect registers
///   a <see cref="DelayedTriggeredAbility"/> (CR 603.7) that fires on the
///   controller's next <see cref="PhaseStateType.Upkeep"/>
///   <see cref="StepStartedEvent"/>. The trigger calls
///   <see cref="Player.PayMana"/> with {2}{B} against the controller's mana
///   pool; on failure <see cref="Player.MarkLost"/> flags the controller
///   (CR 104.3 / CR 118.3 — "if you don't, you lose the game").
///
/// ## Deferred (v1 gaps)
/// - <b>Cost-payment prompt</b>: production callers pre-stage the
///   controller's mana pool to model "yes, I pay". The v1 trigger reads
///   whatever mana is already in the pool — no in-trigger tap-lands prompt.
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// handled by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate.
/// </summary>
[CardName("Slaughter Pact")]
public static class SlaughterPactFactory
{
    public const string CardName = "Slaughter Pact";
    public const string PrintedManaCost = "{0}";
    public const string DelayedUpkeepCost = "{2}{B}";

    /// <summary>
    /// Construct the Slaughter Pact card shape (Instant, {0}). Resolve
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
    /// single creature; on resolution destroys it (if still nonblack) and
    /// (when a <see cref="TriggerManager"/> is supplied) registers a
    /// one-shot delayed trigger that fires at the caster's next upkeep.
    /// </summary>
    /// <param name="caster">The controller of Slaughter Pact — also
    /// the controller of the delayed upkeep trigger.</param>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object (typically a <see cref="Creature"/>).</param>
    /// <param name="triggers">Optional trigger manager. When null the
    /// delayed upkeep payment / loss is skipped (suitable for
    /// destroy-only shape tests).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target nonblack creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Slaughter Pact: destroy target nonblack creature + queue delayed upkeep pact", () =>
                    {
                        // ----------------------------------------------------
                        // CR 701.7 — destroy the target nonblack creature.
                        // CR 105 — colour derived from mana cost pips; cards
                        // with no black pip are nonblack.
                        // ----------------------------------------------------
                        if (resolved is Creature creature
                            && !CardColors.GetColors(creature).Contains(ManaColor.Black))
                        {
                            // Indestructible (CR 702.12) / regeneration (CR 701.15)
                            // handled via the Destroy-reason gate in
                            // OracleSpellBinder.MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(creature, Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }

                        // ----------------------------------------------------
                        // CR 603.7 — register the "at the beginning of your
                        // next upkeep" delayed trigger. Mirrors Pact of
                        // Negation: fires on the first Upkeep StepStartedEvent
                        // strictly after this resolve, attempts to pay {2}{B}
                        // from the controller's pool, falls back to MarkLost
                        // on failure (CR 104.3 / CR 118.3).
                        // ----------------------------------------------------
                        if (triggers == null) return;

                        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
                        var pactCost = ManaCost.Parse(DelayedUpkeepCost);

                        var payOrLoseEffect = new Effect(
                            $"Slaughter Pact: pay {DelayedUpkeepCost} at upkeep or lose the game",
                            () =>
                            {
                                if (caster.HasLost) return;
                                if (!caster.PayMana(pactCost))
                                {
                                    caster.MarkLost();
                                }
                            });

                        var delayed = new DelayedTriggeredAbility(
                            source: caster,
                            controller: caster,
                            condition: new EventTriggerCondition<StepStartedEvent>(
                                (e, _) => e.StepType == PhaseStateType.Upkeep
                                          && ReferenceEquals(e.Player, caster)
                                          && e.Timestamp > resolvedAt),
                            effects: new IEffect[] { payOrLoseEffect });

                        triggers.RegisterDelayed(delayed);
                    }),
                };
            });
    }
}
