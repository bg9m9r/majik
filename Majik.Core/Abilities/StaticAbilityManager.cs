using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Service for managing static abilities.
/// Applies continuous effects from static abilities (Rule 604).
/// </summary>
public class StaticAbilityManager
{
    private readonly List<IStaticAbility> _staticAbilities = new();
    private readonly IEventBus? _eventBus;

    public StaticAbilityManager(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Register a static ability.
    /// </summary>
    public void RegisterStaticAbility(IStaticAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        if (!_staticAbilities.Contains(ability))
        {
            _staticAbilities.Add(ability);
        }
    }

    /// <summary>
    /// Unregister a static ability.
    /// </summary>
    public void UnregisterStaticAbility(IStaticAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        _staticAbilities.Remove(ability);
    }

    /// <summary>
    /// Apply all active static abilities.
    /// This should be called whenever game state changes.
    /// </summary>
    public void ApplyStaticAbilities()
    {
        foreach (var ability in _staticAbilities.ToList())
        {
            if (ability.IsActive())
            {
                ability.ApplyEffect();
            }
        }
    }

    /// <summary>
    /// Get all active static abilities.
    /// </summary>
    public IEnumerable<IStaticAbility> GetActiveAbilities()
    {
        return _staticAbilities.Where(a => a.IsActive());
    }

    /// <summary>
    /// Clear all registered static abilities.
    /// </summary>
    public void Clear()
    {
        _staticAbilities.Clear();
    }
}
