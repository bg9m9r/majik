using Majik.Core.Players;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a static ability that creates continuous effects.
/// Static abilities don't use the stack (Rule 604).
/// </summary>
public class StaticAbility : IStaticAbility
{
    private readonly Func<bool>? _isActiveCheck;
    private readonly Action? _applyEffect;

    public object Source { get; }
    public Player Controller { get; }
    public string Description { get; }

    public StaticAbility(object source, Player controller, string description, Func<bool>? isActiveCheck = null, Action? applyEffect = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _isActiveCheck = isActiveCheck;
        _applyEffect = applyEffect;
    }

    public bool IsActive()
    {
        if (_isActiveCheck != null)
        {
            return _isActiveCheck();
        }

        // Default: active if source is a permanent on the battlefield
        if (Source is Cards.Permanent permanent)
        {
            return permanent.Zone == Zones.ZoneType.Battlefield;
        }

        return true;
    }

    public void ApplyEffect()
    {
        if (!IsActive())
        {
            return;
        }

        _applyEffect?.Invoke();
    }
}
