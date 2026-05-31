using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Berserk (Limited Edition Alpha, {G}).
///
/// Instant. Oracle text:
///   "Cast this spell only before the combat damage step.
///    Target creature gains trample and gets +X/+0 until end of turn,
///    where X is its power.
///    At the beginning of the next end step, destroy that creature if
///    it attacked this turn."
///
/// ## Implemented (v1)
/// - Instant shape, printed cost {G}.
/// - Resolve-time <see cref="SpellDefinition"/> via
///   <see cref="BuildSpellDefinition"/> declares a single 1..1
///   "target creature" request. On resolution:
///   1. Samples the target's current power X (CR 613.4d — Berserk's "+X/+0"
///      is calculated at the moment the spell starts resolving).
///   2. Registers a Layer 6 Trample grant via
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/>.
///   3. Registers a Layer 7c "+X/+0" pump via
///      <see cref="PumpUntilEndOfTurnEffect"/>.
///   4. (Optional) Subscribes a one-shot
///      <see cref="CreatureAttacksEvent"/> listener that flips an
///      <c>attacked</c> flag closed over by step 5. Without an event bus
///      the listener is skipped (shape-only tests).
///   5. (Optional) Registers a <see cref="DelayedTriggeredAbility"/> on the
///      supplied <see cref="TriggerManager"/> that fires on the first
///      <see cref="StepStartedEvent"/> with <see cref="PhaseStateType.End"/>
///      strictly after this resolution. When it fires, if the
///      <c>attacked</c> flag is set the target is destroyed
///      (CR 701.7 → <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///      <see cref="ZoneMoveReason.Destroy"/>, so indestructible (CR 702.12)
///      and regeneration (CR 701.15) are honoured).
/// - CR 608.2b — illegal target / non-Creature resolver / missing
///   continuous-effects service → no-op (no throw).
///
/// ## Deferred (v1 gaps)
/// - <b>Cast-restriction "only before the combat damage step"</b>: not
///   modelled. The engine currently lacks a generic
///   <c>TimingRestriction</c> hook on instant cast-flow; once present this
///   factory should opt in. For now the restriction is documented only
///   (mirrors the per-factory gap on other timing-restricted spells such as
///   Lightning Helix's instant timing — fine for any phase).
/// - <b>"if it attacked this turn"</b>: handled via the live event-bus
///   listener wired at resolve time. The listener targets this specific
///   creature reference, so a flicker that returns a new game object
///   correctly counts as a different creature (Berserk's destroy clause
///   tracks the original object — CR 700.4 "this creature").
/// </summary>
[CardName("Berserk")]
public static class BerserkFactory
{
    public const string CardName = "Berserk";
    public const string PrintedManaCost = "{G}";

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>CardDef DSL — card shape only. The pump + delayed-destroy
    /// body lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time SpellDefinition. Single 1..1 "target
    /// creature" request. On resolution:
    /// <list type="number">
    /// <item>Trample EOT granted to target.</item>
    /// <item>+X/+0 EOT where X = target's power at the moment Berserk
    ///       starts resolving.</item>
    /// <item>When <paramref name="bus"/> + <paramref name="triggers"/> are
    ///       supplied, a delayed end-step trigger destroys the creature if
    ///       it attacked any time after Berserk resolved.</item>
    /// </list>
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="bus">Optional event bus. When supplied, a one-shot
    /// <see cref="CreatureAttacksEvent"/> listener flips an internal
    /// <c>attacked</c> flag used by the delayed end-step destroy. When
    /// null, the destroy clause is skipped (pump + trample still apply).</param>
    /// <param name="triggers">Optional trigger manager. When supplied, the
    /// delayed end-step destroy is registered (CR 603.7). When null, the
    /// destroy clause is skipped (pump + trample still apply).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IEventBus? bus = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: trample + (+X/+0) EOT + destroy at end step if attacked", () =>
                        Resolve(raw, bus, triggers)),
                };
            });
    }

    private static void Resolve(
        object raw,
        IEventBus? bus,
        TriggerManager? triggers)
    {
        // CR 608.2b — illegal target / non-Creature → no-op.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.4d — Berserk samples X (the creature's power) at the
        // moment it starts resolving.
        var x = target.GetPower();

        // Layer 6 — grant Trample until end of turn.
        target.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(target, GrantedTrample));

        // Layer 7c — +X/+0 until end of turn. Identical shape to
        // MutagenicGrowthFactory's pump, but power-only.
        if (x > 0)
        {
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, x, 0));
        }

        // "Destroy that creature if it attacked this turn" — handled
        // via live event-bus subscription. Without a bus or trigger manager
        // the destroy clause is skipped (pump + trample still apply for
        // shape-only tests).
        if (bus == null || triggers == null) return;

        var attacked = new[] { false };
        Action<CreatureAttacksEvent>? onAttack = null;
        onAttack = e =>
        {
            if (ReferenceEquals(e.Attacker, target)) attacked[0] = true;
        };
        bus.Subscribe(onAttack);

        // CR 603.7 — one-shot delayed triggered ability. Fires on the first
        // StepStartedEvent(End) strictly after Berserk resolves (timestamp
        // fence mirrors Sneak Attack / Through the Breach). On fire, if the
        // creature attacked this turn, destroy it (CR 701.7 — honours
        // indestructible/regeneration via OracleSpellBinder.MoveToGraveyard).
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var destroyEffect = new Effect(
            $"{CardName}: destroy {target.Name} at next end step if attacked",
            () =>
            {
                // Unsubscribe so the listener doesn't outlive the spell.
                bus.Unsubscribe(onAttack);

                if (!attacked[0]) return;
                if (target.Zone != ZoneType.Battlefield) return;
                // CR 701.7 — destroy. Indestructible (CR 702.12) +
                // regeneration (CR 701.15) honoured by the
                // Destroy-reason gate in MoveToGraveyard.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        var delayed = new DelayedTriggeredAbility(
            source: target,
            controller: target.Controller!,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { destroyEffect });

        triggers.RegisterDelayed(delayed);
    }
}
