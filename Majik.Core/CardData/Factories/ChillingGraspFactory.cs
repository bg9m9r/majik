using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for the instant Chilling Grasp (MTG Madness reprint
/// pool, {2}{U}).
///
/// Oracle text (verified against Scryfall 2026-06-10):
///   "Tap up to two target creatures. Those creatures don't untap during
///    their controller's next untap step.
///    Madness {3}{U} (If you discard this card, discard it into exile.
///    When you do, cast it for its madness cost or put it into your
///    graveyard.)"
///
/// ## Madness — intrinsic, NOT wired here (CR 702.35)
/// Madness works for every catalogued card via
/// <c>Majik.Core/Keywords/MadnessCatalog.cs</c> (name → cost) consulted by
/// the central discard funnel <c>Fx.DiscardCard</c>: a discarded madness
/// card is routed to exile and offered for its madness cost automatically.
/// Chilling Grasp is catalogued at {3}{U}, so the "Madness {3}{U}" line
/// needs no factory code. This factory implements ONLY the spell body.
///
/// ## Implemented (v1)
/// - Instant identity at {2}{U} (blue, mana value 3), built from the embedded
///   JSON def (<c>chilling-grasp.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>"Tap up to two target creatures"</b> — one 0..2 "target creature"
///   request (CR 601.2c — "up to two"), mirroring the multi-target shape of
///   <see cref="AbandonReasonFactory"/>. On resolution each chosen target
///   that is still a Creature on the battlefield (CR 608.2b) is tapped
///   (CR 701.20).
/// - <b>"don't untap during their controller's next untap step"</b> — each
///   tapped creature is registered with
///   <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> so it
///   skips its controller's next untap step (CR 502.1). This is the same
///   tap + MarkPermanentDoesNotUntap + one-shot StepStartedEvent cleanup
///   mechanism used by <see cref="FrostLynxFactory"/> and
///   <see cref="SleepFactory"/>. Because Chilling Grasp's targets can have
///   DIFFERENT controllers, the per-creature skip token is cleaned up on the
///   FIRST Untap step belonging to that creature's own controller (CR 502.1
///   "their controller's next untap step"), matching the per-target-controller
///   keying of Frost Lynx.
/// - <b>CR 608.2b guards</b>: each chosen target is resolved independently and
///   dropped if it is not a Creature on the battlefield; remaining legal
///   targets still resolve. Already-tapped creatures still receive the
///   skip-untap marker (Permanent.Tap is guarded against double-tap).
/// </summary>
[CardName("Chilling Grasp")]
public static class ChillingGraspFactory
{
    public const string CardName = "Chilling Grasp";
    public const string Slug = "chilling-grasp";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Build Chilling Grasp as an Instant from the embedded JSON def, with
    /// owner / controller wired. Suitable for identity / shape / dispatcher
    /// tests.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Chilling Grasp.
    ///
    /// One 0..2 "target creature" request (CR 601.2c — "up to two"). On
    /// resolution each chosen target still a Creature on the battlefield
    /// (CR 608.2b) is tapped (CR 701.20) and registered with
    /// <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/> to skip
    /// its controller's next untap step (CR 502.1). When
    /// <paramref name="eventBus"/> is non-null, a one-shot
    /// <see cref="StepStartedEvent"/> handler removes each registration after
    /// that creature's controller's next Untap step fires.
    /// </summary>
    /// <param name="targetResolver">Maps each agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="eventBus">Event bus for the one-shot "next untap step"
    /// cleanup. When null, the skip-untap registrations persist until the
    /// caller clears <see cref="UntapStepRestrictions"/> (test-isolation
    /// posture shared with FrostLynxFactory / SleepFactory tests).</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to two target creatures",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets[0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: tap up to two target creatures; they skip their controller's next untap step",
                        () =>
                        {
                            foreach (var token in rawTargets)
                            {
                                Resolve(targetResolver(token), eventBus);
                            }
                        }),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body — per target (CR 608.2b / 701.20 / 502.1)
    // -------------------------------------------------------------------------

    private static void Resolve(object resolved, IEventBus? eventBus)
    {
        // CR 608.2b — illegal target: only Creatures still on the battlefield
        // are affected; everything else is a clean no-op.
        if (resolved is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // CR 701.20 — tap the target creature. Permanent.Tap throws if already
        // tapped, but the skip-untap rider still applies regardless.
        if (!target.IsTapped)
        {
            target.Tap();
        }

        // CR 502.1 — "doesn't untap during their controller's next untap step".
        var skipToken = new object();
        UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, target);

        ScheduleSkipUntapCleanup(target, skipToken, eventBus);
    }

    private static void ScheduleSkipUntapCleanup(Creature target, object skipToken, IEventBus? eventBus)
    {
        if (eventBus == null) return;

        // One-shot: remove the skip on the FIRST Untap step that belongs to the
        // target's current controller (CR 502.1 / "their controller's next
        // untap step"). Keyed per-target controller because Chilling Grasp's
        // two targets may be controlled by different players.
        var targetController = target.Controller;
        Action<StepStartedEvent>? cleanupHandler = null;
        cleanupHandler = ev =>
        {
            if (ev.StepType != StepStateType.Untap) return;
            if (!ReferenceEquals(ev.Player, targetController)) return;

            UntapStepRestrictions.RemoveAll(skipToken);
            if (cleanupHandler != null)
                eventBus.Unsubscribe(cleanupHandler);
        };
        eventBus.Subscribe(cleanupHandler);
    }
}
