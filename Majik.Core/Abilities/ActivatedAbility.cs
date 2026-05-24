using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents an activated ability on the stack.
/// </summary>
public class ActivatedAbility : IActivatedAbility
{
    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<ICost> _costs = new();
    private readonly List<IEffect> _effects = new();

    // Deferred targeting (Rule 602.2b): target requests are declared at
    // construction time; chosen targets are stored after the activating
    // player's agent responds during AbilityActivationFlow.ActivateAsync.
    private IReadOnlyList<IReadOnlyList<object>> _chosenTargets =
        Array.Empty<IReadOnlyList<object>>();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public object Source { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<ICost> Costs => _costs.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;

    /// <summary>
    /// Targeting requests the activating player's agent must satisfy during
    /// <see cref="AbilityActivationFlow.ActivateAsync"/> (Rule 602.2b).
    /// Empty when the ability needs no targets.
    /// </summary>
    public IReadOnlyList<TargetRequest> TargetRequests { get; }

    /// <summary>
    /// CR 117.1a / 307.5 — "Activate only as a sorcery" rider. When true,
    /// <see cref="Rules.ActionValidator"/> rejects activations outside the
    /// controller's main phase or when the stack is non-empty. Defaults
    /// to false (any-time activation).
    /// </summary>
    public bool IsSorcerySpeed { get; }

    /// <summary>
    /// The targets chosen by the activating player's agent (parallel
    /// list-of-lists matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> after the agent responds. Effects
    /// should read from here, not from the legacy <see cref="Targets"/>
    /// collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    public ActivatedAbility(
        object source,
        Player controller,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<ICost>? costs = null,
        IEnumerable<IEffect>? effects = null,
        IEnumerable<TargetRequest>? targetRequests = null,
        bool sorcerySpeed = false)
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
        IsSorcerySpeed = sorcerySpeed;
        _resolutionState = ResolutionState.NotResolving();

        TargetRequests = targetRequests is null
            ? Array.Empty<TargetRequest>()
            : targetRequests.ToList().AsReadOnly();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (costs != null)
        {
            _costs.AddRange(costs);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// Store the targets chosen by the activating player's agent. Called by
    /// <see cref="AbilityActivationFlow.ActivateAsync"/> after the agent
    /// responds to each <see cref="TargetRequest"/>.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
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
