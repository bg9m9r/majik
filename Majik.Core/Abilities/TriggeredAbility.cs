using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a triggered ability on the stack.
/// </summary>
public class TriggeredAbility : ITriggeredAbility
{
    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<IEffect> _effects = new();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public object Source { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;

    public TriggeredAbility(object source, Player controller, IEnumerable<ITarget>? targets = null, IEnumerable<IEffect>? effects = null)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (controller == null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        Source = source;
        Controller = controller;
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        _resolutionState = ResolutionState.NotResolving();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    public bool IsTriggered()
    {
        // TODO: Check trigger condition
        // For now, always return true
        return true;
    }

    public bool CanBePutOnStack()
    {
        // TODO: Check if ability can be put on stack (intervening-if clauses, etc.)
        // For now, always return true
        return true;
    }

    public void Resolve()
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Ability is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();
        
        // Resolution logic (Rule 608)
        // Execute all effects
        foreach (var effect in _effects)
        {
            effect.Execute();
        }
        
        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
