using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Service for managing replacement effects.
/// Applies replacement effects to modify events (Rule 614).
/// </summary>
public class ReplacementEffectManager
{
    private readonly List<IReplacementEffect> _replacementEffects = new();

    public ReplacementEffectManager()
    {
    }

    /// <summary>
    /// Register a replacement effect.
    /// </summary>
    public void RegisterReplacementEffect(IReplacementEffect effect)
    {
        if (effect == null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (!_replacementEffects.Contains(effect))
        {
            _replacementEffects.Add(effect);
        }
    }

    /// <summary>
    /// Unregister a replacement effect.
    /// </summary>
    public void UnregisterReplacementEffect(IReplacementEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        _replacementEffects.Remove(effect);
    }

    /// <summary>
    /// Apply replacement effects to an event.
    /// Returns the modified event, or null if the event should be prevented.
    /// </summary>
    public GameEvent? ApplyReplacementEffects(GameEvent gameEvent)
    {
        if (gameEvent == null)
        {
            return null;
        }

        var currentEvent = gameEvent;

        // Apply replacement effects in order (Rule 614.1)
        // For now, apply all applicable replacements
        // In a full implementation, we'd need to handle replacement ordering
        foreach (var effect in _replacementEffects.ToList())
        {
            if (effect.CanReplace(currentEvent))
            {
                var replaced = effect.Replace(currentEvent);
                if (replaced == null)
                {
                    // Event was prevented
                    return null;
                }
                currentEvent = replaced;
            }
        }

        return currentEvent;
    }

    /// <summary>
    /// Clear all registered replacement effects.
    /// </summary>
    public void Clear()
    {
        _replacementEffects.Clear();
    }
}
