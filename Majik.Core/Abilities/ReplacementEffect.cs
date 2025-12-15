using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a replacement effect that modifies events.
/// Replacement effects modify events before they occur (Rule 614).
/// </summary>
public class ReplacementEffect : IReplacementEffect
{
    private readonly Func<GameEvent, bool>? _canReplaceCheck;
    private readonly Func<GameEvent, GameEvent?>? _replaceAction;

    public object Source { get; }
    public Player Controller { get; }
    public string Description { get; }

    public ReplacementEffect(object source, Player controller, string description, Func<GameEvent, bool>? canReplaceCheck = null, Func<GameEvent, GameEvent?>? replaceAction = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _canReplaceCheck = canReplaceCheck;
        _replaceAction = replaceAction;
    }

    public bool CanReplace(GameEvent gameEvent)
    {
        if (gameEvent == null)
        {
            return false;
        }

        if (_canReplaceCheck != null)
        {
            return _canReplaceCheck(gameEvent);
        }

        // Default: can replace if source is a permanent on the battlefield
        if (Source is Cards.Permanent permanent)
        {
            return permanent.Zone == Zones.ZoneType.Battlefield;
        }

        return false;
    }

    public GameEvent? Replace(GameEvent gameEvent)
    {
        if (gameEvent == null)
        {
            return null;
        }

        if (!CanReplace(gameEvent))
        {
            return gameEvent; // Return original event if can't replace
        }

        if (_replaceAction != null)
        {
            return _replaceAction(gameEvent);
        }

        // Default: return original event (no replacement)
        return gameEvent;
    }
}
