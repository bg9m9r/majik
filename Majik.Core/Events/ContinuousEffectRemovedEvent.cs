using Majik.Core.Effects;

namespace Majik.Core.Events;

/// <summary>
/// CR 613 — a continuous (layer) effect has left the game's active-effects set
/// (unregister, inactive-effect prune, or end-of-turn cleanup). Twin of
/// <see cref="ContinuousEffectAddedEvent"/>: log-only and public information,
/// so it carries no per-viewer masking. Consumed by the portal action log to
/// render a "{source} effect removed" line.
/// </summary>
public class ContinuousEffectRemovedEvent : GameEvent
{
    /// <summary>The continuous effect that left the active set.</summary>
    public ContinuousEffect Effect { get; }

    public ContinuousEffectRemovedEvent(ContinuousEffect effect)
        : base()
    {
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
    }
}
