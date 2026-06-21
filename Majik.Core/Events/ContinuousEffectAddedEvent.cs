using Majik.Core.Effects;

namespace Majik.Core.Events;

/// <summary>
/// CR 613 — a continuous (layer) effect has entered the game's active-effects
/// set (registered on <see cref="ContinuousEffectsService"/>). Log-only and
/// public information (continuous effects live on the battlefield, CR 613), so
/// it carries no per-viewer masking. Consumed by the portal action log to
/// render a "{source} effect added" line.
/// </summary>
public class ContinuousEffectAddedEvent : GameEvent
{
    /// <summary>The continuous effect that entered the active set.</summary>
    public ContinuousEffect Effect { get; }

    public ContinuousEffectAddedEvent(ContinuousEffect effect)
        : base()
    {
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
    }
}
