using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Torpor Orb (Scars of Mirrodin).
///
/// Artifact — {2}
/// Oracle text: "Creatures entering the battlefield don't cause abilities to trigger."
///
/// ## Implementation (v1)
/// The suppression is wired via <see cref="TorporOrbStaticEffect"/>, which
/// subscribes to <see cref="CardMovedEvent"/> and increments/decrements
/// <see cref="TriggerManager.CreatureEtbTriggerSuppressionCount"/> as the
/// Orb moves onto/off the battlefield (CR 614 / CR 603.3).
///
/// Callers that control the <see cref="TriggerManager"/> and
/// <see cref="IEventBus"/> should use
/// <see cref="Create(Player, TriggerManager, IEventBus)"/> so the static
/// effect is fully wired. The single-argument overload produces an
/// Artifact with correct identity but without runtime suppression — useful
/// for pure card-shape tests.
///
/// ## Deferred (v1 gaps)
/// - Torpor Orb suppresses ALL abilities triggered by creatures entering
///   (including non-creature triggers that watch for creatures entering,
///   e.g. "Whenever a creature enters the battlefield under your control…"
///   on a Panharmonicon-style card). The current gate in
///   <see cref="TriggerManager.EvaluateTriggers"/> checks that the
///   <em>triggering event</em> is a creature ETB; it does not inspect the
///   source of the triggered ability itself. This matches the Oracle ruling
///   for Torpor Orb (abilities on OTHER permanents that trigger from
///   creatures entering are also suppressed). Future precision may be
///   needed if the engine adds "player-controlled" suppression semantics.
/// </summary>
public static class TorporOrbFactory
{
    /// <summary>
    /// Creates a Torpor Orb with correct card identity only (no runtime
    /// suppression wired). Suitable for factory-shape and naming tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, triggerManager: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Torpor Orb. The <see cref="TorporOrbStaticEffect"/>
    /// is attached to <paramref name="eventBus"/> and will suppress creature
    /// ETB triggers via <paramref name="triggerManager"/> for as long as the
    /// Orb remains on the battlefield.
    /// </summary>
    /// <param name="owner">Owner and initial controller.</param>
    /// <param name="triggerManager">The game's TriggerManager. May be null —
    /// suppression is silently skipped if null.</param>
    /// <param name="eventBus">The game's EventBus. May be null — suppression
    /// is silently skipped if null.</param>
    public static Artifact Create(Player owner, TriggerManager? triggerManager, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var orb = new Artifact("Torpor Orb", "{2}");
        orb.SetOwner(owner);
        orb.SetController(owner);

        if (triggerManager != null)
        {
            var effect = new TorporOrbStaticEffect(orb, triggerManager, eventBus);
            effect.Attach();
        }

        return orb;
    }
}
