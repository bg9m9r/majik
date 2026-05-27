using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sleep (Seventh Edition / reprints, {2}{U}{U}).
///
/// Sorcery. Oracle text:
///   "Tap all creatures target player controls. Those creatures don't
///    untap during that player's next untap step."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}{U}, owner/controller wired.
/// - <b>SpellDefinition</b> (via <see cref="BuildSpellDefinition"/>) declares
///   one 1..1 "target player" <see cref="TargetRequest"/>. On resolution:
///     1. Enumerate every <see cref="Creature"/> the target player controls on
///        the battlefield (CR 608.2b — other zones are ignored).
///     2. Tap each creature (CR 701.20). Creatures already tapped are guarded
///        against double-tap (Permanent.Tap throws when already tapped).
///     3. Register each creature with
///        <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> so it
///        skips the target player's next untap step (CR 502.1).
///     4. When an <see cref="IEventBus"/> is supplied, schedule a one-shot
///        <see cref="StepStartedEvent"/> subscription that removes ALL of
///        Sleep's skip tokens on the FIRST Untap step whose
///        <see cref="StepStartedEvent.Player"/> matches the target player
///        (CR 502.1 / "next untap step" wording).
///
/// ## Reuse of Frost Lynx pattern
/// Frost Lynx performs the same tap + MarkPermanentDoesNotUntap + one-shot
/// StepStartedEvent cleanup for a single targeted creature. Sleep reuses
/// that identical mechanism in a loop over all creatures the target player
/// controls, sharing one cleanup subscription per resolution (a single
/// Untap-step handler clears all per-creature tokens registered in that cast).
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time legality filter</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty — production callers wanting agent-side filtering populate it
///   themselves (same posture as Thoughtseize / Solitude).
/// </summary>
[CardName("Sleep")]
public static class SleepFactory
{
    public const string CardName = "Sleep";
    public const string PrintedManaCost = "{2}{U}{U}";

    /// <summary>
    /// Construct a Sleep sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// target request + tap + skip-untap body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Sleep is cast.
    ///
    /// Single 1..1 "target player" request. On resolution: tap each
    /// creature the target player controls (CR 701.20) and register each
    /// with <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>
    /// to skip the target player's next untap step (CR 502.1). When
    /// <paramref name="eventBus"/> is non-null, a one-shot
    /// <see cref="StepStartedEvent"/> handler removes all registrations
    /// after the target player's next Untap step fires.
    /// </summary>
    /// <param name="caster">Cast-time controller. Used for "target player"
    /// resolution; may be null in shape tests.</param>
    /// <param name="eventBus">Event bus for one-shot "next untap step"
    /// cleanup. When null, the skip-untap registrations persist until the
    /// caller clears <see cref="UntapStepRestrictions"/> (test-isolation
    /// posture shared with ManaVaultFactory / FrostLynxFactory / ChokeFactory
    /// tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player? caster,
        IEventBus? eventBus)
    {
        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: tap all creatures target player controls; they skip their controller's next untap step",
                        () => Resolve(raw, eventBus)),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    private static void Resolve(object raw, IEventBus? eventBus)
    {
        // CR 608.2b — illegal-target check at resolution.
        if (raw is not Player target) return;

        // Enumerate all creatures the target player currently controls on
        // the battlefield (CR 608.2b — only battlefield permanents are in scope).
        var creatures = target.Zones.Battlefield
            .GetCards()
            .OfType<Creature>()
            .ToList();

        // Each creature gets its own skip token so per-permanent idempotency
        // in UntapStepRestrictions is maintained (same approach as Frost Lynx).
        // All tokens are collected here so the single one-shot cleanup handler
        // can sweep them all.
        var skipTokens = new List<object>(creatures.Count);

        foreach (var creature in creatures)
        {
            // CR 701.20 — tap the creature. Guard against already-tapped
            // state: Permanent.Tap throws if the permanent is already tapped,
            // but the skip-untap rider applies regardless.
            if (!creature.IsTapped)
            {
                creature.Tap();
            }

            // CR 502.1 — "doesn't untap during that player's next untap step".
            var skipToken = new object();
            UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, creature);
            skipTokens.Add(skipToken);
        }

        if (skipTokens.Count == 0 || eventBus == null) return;

        // One-shot subscription: on the first Untap step that belongs to the
        // target player, remove all skip registrations and unsubscribe
        // (CR 502.1 / "next untap step"). Uses the same SubscribeAll pattern
        // as FrostLynxFactory to receive every event type with a single
        // subscription.
        Action<GameEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (ev is not StepStartedEvent sse) return;
            if (sse.StepType != PhaseStateType.Untap) return;
            if (!ReferenceEquals(sse.Player, target)) return;

            foreach (var token in skipTokens)
            {
                UntapStepRestrictions.RemoveAll(token);
            }

            if (cleanupHandler != null)
                eventBus.UnsubscribeAll(cleanupHandler);
        };
        eventBus.SubscribeAll(cleanupHandler);
    }
}
