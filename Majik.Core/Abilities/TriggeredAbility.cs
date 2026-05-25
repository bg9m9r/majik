using Majik.Core.Cards;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a triggered ability on the stack.
/// </summary>
public class TriggeredAbility : ITriggeredAbility
{
    private static readonly IReadOnlySet<ZoneType> DefaultActiveZones =
        new HashSet<ZoneType> { ZoneType.Battlefield };

    private ResolutionState _resolutionState;
    private readonly List<ITarget> _targets = new();
    private readonly List<IEffect> _effects = new();

    // Deferred targeting (Rule 601.2c / Rule 603.3): target requests are
    // declared at construction time; chosen targets are stored after the
    // controller's agent responds at stack-entry time.
    private IReadOnlyList<IReadOnlyList<object>> _chosenTargets =
        Array.Empty<IReadOnlyList<object>>();

    public Guid Id { get; }
    public Player Controller { get; }
    public DateTime Timestamp { get; }
    public object Source { get; }
    public IReadOnlyList<ITarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<IEffect> Effects => _effects.AsReadOnly();
    public bool IsResolving => _resolutionState.IsResolving;
    public ITriggerCondition Condition { get; }
    public Func<bool>? InterveningIf { get; }
    public IReadOnlySet<ZoneType> ActiveZones { get; }

    /// <summary>
    /// Targeting requests that the controller's agent must satisfy when the
    /// ability is about to be placed on the stack (Rule 603.3).
    /// Empty when the ability needs no targets.
    /// </summary>
    public IReadOnlyList<TargetRequest> TargetRequests { get; }

    /// <summary>
    /// CR 700.2d — modal-trigger mode labels (e.g. Deceiver Exarch's two
    /// ETB modes). Empty for non-modal triggers. When populated,
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> prompts
    /// the controller's agent via the list-returning
    /// <see cref="IPlayerAgent.ChooseModeAsync(IReadOnlyList{string}, BotIntent, int, CancellationToken)"/>
    /// before pushing the ability on the stack.
    /// </summary>
    public IReadOnlyList<string> Modes { get; }

    /// <summary>Per-mode intent (parallel to <see cref="Modes"/>), or
    /// <see cref="BotIntent.None"/> when unclassified. OR-combined into
    /// a prompt-level intent at trigger-resolve time.</summary>
    public IReadOnlyList<BotIntent> ModeIntents { get; }

    /// <summary>Required count of distinct mode picks (CR 700.2d).
    /// Defaults to 1 for "Choose one —".</summary>
    public int RequiredModeCount { get; }

    /// <summary>The chosen mode indices, populated by
    /// <see cref="SetChosenModes"/> before resolution. Empty for non-
    /// modal triggers. Effect closures read this to branch their body
    /// per chosen mode.</summary>
    public IReadOnlyList<int> ChosenModes { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// The targets chosen by the controller's agent (parallel list-of-lists
    /// matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> before the ability is pushed onto the
    /// stack. Effects should read from here, not from the legacy
    /// <see cref="Targets"/> collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    public TriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null,
        IEnumerable<ZoneType>? activeZones = null,
        IEnumerable<TargetRequest>? targetRequests = null,
        IReadOnlyList<string>? modes = null,
        IReadOnlyList<BotIntent>? modeIntents = null,
        int requiredModeCount = 1)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));

        Modes = modes ?? Array.Empty<string>();
        ModeIntents = modeIntents ?? Array.Empty<BotIntent>();
        RequiredModeCount = requiredModeCount;

        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        _resolutionState = ResolutionState.NotResolving();
        InterveningIf = interveningIf;
        ActiveZones = activeZones is null
            ? DefaultActiveZones
            : new HashSet<ZoneType>(activeZones);

        TargetRequests = targetRequests is null
            ? Array.Empty<TargetRequest>()
            : targetRequests.ToList().AsReadOnly();

        if (targets != null)
        {
            _targets.AddRange(targets);
        }

        if (effects != null)
        {
            _effects.AddRange(effects);
        }
    }

    /// <summary>
    /// Store the targets chosen by the controller's agent. Called by
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> immediately
    /// before pushing the ability onto the stack.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
    }

    /// <summary>
    /// Store the mode indices chosen by the controller's agent
    /// (CR 700.2d). Called by
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> before
    /// pushing a modal trigger onto the stack.
    /// </summary>
    public void SetChosenModes(IReadOnlyList<int> chosen)
    {
        ChosenModes = chosen ?? Array.Empty<int>();
    }

    public bool IsTriggered(GameEvent e)
    {
        if (e == null)
        {
            return false;
        }

        if (Source is ICard card && !ActiveZones.Contains(card.Zone))
        {
            return false;
        }

        return Condition.Matches(e, this);
    }

    public bool CanBePutOnStack() => InterveningIf?.Invoke() ?? true;

    public void Resolve()
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Ability is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();

        foreach (var effect in _effects)
        {
            effect.Execute();
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
