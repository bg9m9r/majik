using Majik.Core.Cards;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Events;
using Majik.Core.Game;
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
    /// CR 711.3 — a face/state predicate gating whether this trigger's ABILITY
    /// is currently present on the object. Distinct from
    /// <see cref="InterveningIf"/> (which is a CR 603.4 condition re-checked at
    /// trigger time AND on resolution for an ALREADY-present ability): a false
    /// <see cref="ActiveWhen"/> means the ability isn't on the object at all —
    /// it can't trigger. Used by transform DFCs to attach BOTH faces' triggered
    /// abilities while only the active face's set fires (no register/unregister
    /// churn across the day/night flip). Null ⇒ always active.
    /// </summary>
    public Func<bool>? ActiveWhen { get; }

    /// <summary>True iff this trigger's ability is currently present on its
    /// object (its <see cref="ActiveWhen"/> face/state gate passes, or there is
    /// no gate). Reads the live face state each call — no caching.</summary>
    public bool IsActiveFace() => ActiveWhen?.Invoke() ?? true;

    /// <summary>
    /// Targeting requests that the controller's agent must satisfy when the
    /// ability is about to be placed on the stack (Rule 603.3).
    /// Empty when the ability needs no targets.
    /// </summary>
    public IReadOnlyList<TargetRequest> TargetRequests { get; }

    /// <summary>
    /// The targets chosen by the controller's agent (parallel list-of-lists
    /// matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> before the ability is pushed onto the
    /// stack. Effects should read from here, not from the legacy
    /// <see cref="Targets"/> collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    /// <summary>
    /// CR 603.3 — the player identified by THIS trigger's event as it was
    /// evaluated (the "that player" / triggering player), captured by the
    /// trigger condition the moment it matches and read back by an UNTARGETED
    /// resolve effect that punishes the triggering player (e.g. Ash Zealot's
    /// "deals 3 damage to that player"). This is the declarative analogue of the
    /// boxed-closure idiom the hand-rolled Ash Zealot / Eidolon factories use:
    /// the condition stamps it before the ability goes on the stack, so the
    /// captured player is the correct "that player" by resolution time. Null
    /// when no trigger has identified a player (or the slot was never used). The
    /// slot is reused per-instance — the engine reuses one ability instance
    /// across re-fires, so the same single-instance overwrite posture as the
    /// hand-rolled boxed idiom applies (CR 603.3 — drained before re-evaluation
    /// in the ordinary priority loop).
    /// </summary>
    public Player? TriggeringPlayer { get; private set; }

    /// <summary>
    /// Stamp the triggering player (CR 603.3) — called by a trigger condition as
    /// it matches, so an untargeted "deal N damage to that player" effect can
    /// read it at resolution off <see cref="ResolutionContext.TriggeringPlayer"/>.
    /// </summary>
    public void SetTriggeringPlayer(Player? player) => TriggeringPlayer = player;

    public TriggeredAbility(
        object source,
        Player controller,
        ITriggerCondition condition,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null,
        IEnumerable<ZoneType>? activeZones = null,
        IEnumerable<TargetRequest>? targetRequests = null,
        Func<bool>? activeWhen = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        ActiveWhen = activeWhen;

        // PLAN 08 — per-game deterministic id. Reseeded from the ambient
        // DeterministicIdSource inside a game scope; Guid.NewGuid() fallback
        // outside one.
        Id = DeterministicIdScope.NewId();
        // Determinism (PLAN 08 prerequisite): order-determining timestamp comes
        // from the per-game logical clock, not wall-clock. Consumed by
        // ApnapOrdering (.ThenBy(t => t.Timestamp)) + TriggerManager
        // (OrderBy(t => t.Timestamp)). The clock assigns in construction order
        // (the same order UtcNow approximated) so trigger ordering is unchanged
        // but now reproducible on replay.
        Timestamp = LogicalClockScope.Current.NextTimestamp();
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
    /// CR 707.2 — re-instantiate this triggered ability bound to a new source
    /// permanent + controller, preserving the structural fields (condition,
    /// effects, target requests, intervening-if, active zones, active-face
    /// gate). Used by the copy machinery to mirror a SOURCE permanent's printed
    /// triggered abilities onto the COPY: the copy's instance gates its zone /
    /// trigger checks on the copy (CR 711.3 / 603), and conditions that read
    /// <c>ability.Source</c> / <c>ability.Controller</c> see the copy.
    ///
    /// NOTE (v1 boundary): effect closures that captured the ORIGINAL source
    /// permanent directly (rather than reading it from the ability) still point
    /// at the original — those abilities must be rebuilt by a bespoke per-card
    /// rebind. This generic rebind is correct for the common case where the
    /// ability references only its own <see cref="Source"/> / <see cref="Controller"/>.
    /// </summary>
    public TriggeredAbility RebindTo(object newSource, Player newController) =>
        new(
            source: newSource ?? throw new ArgumentNullException(nameof(newSource)),
            controller: newController ?? throw new ArgumentNullException(nameof(newController)),
            condition: Condition,
            targets: _targets.Count > 0 ? _targets : null,
            effects: _effects.Count > 0 ? _effects : null,
            interveningIf: InterveningIf,
            activeZones: ActiveZones,
            targetRequests: TargetRequests.Count > 0 ? TargetRequests : null,
            activeWhen: ActiveWhen);

    /// <summary>
    /// Store the targets chosen by the controller's agent. Called by
    /// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> immediately
    /// before pushing the ability onto the stack.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
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

        // CR 711.3 — the ability isn't on the object while its face/state gate
        // is false (e.g. a front-face trigger on a back-face-up transform DFC).
        if (!IsActiveFace())
        {
            return false;
        }

        return Condition.Matches(e, this);
    }

    public bool CanBePutOnStack() => InterveningIf?.Invoke() ?? true;

    public void Resolve() => ResolveAsync(null, null).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve the triggered ability's effects (CR 608) on the
    /// async path. Builds a <see cref="ResolutionContext"/> from this
    /// ability's <see cref="Controller"/> + <see cref="ChosenTargets"/> and
    /// the resolver-supplied <paramref name="agent"/> / <paramref name="game"/>
    /// / <paramref name="ct"/>, then awaits each effect in declaration order.
    /// The synchronous <see cref="Resolve"/> is a thin shim over this.
    /// </summary>
    public async ValueTask ResolveAsync(
        IPlayerAgent? agent,
        GameContext? game,
        CancellationToken ct = default)
    {
        if (_resolutionState.IsResolving)
        {
            throw new InvalidOperationException("Ability is already resolving");
        }

        _resolutionState = ResolutionState.Resolving();

        // STAGE 1 — expose the trigger's own source (when it is a battlefield
        // permanent) so effects can read "their source" generically off the
        // context (CR 113.7) rather than capturing a specific permanent.
        var rc = ResolutionContext.For(
                Controller, agent, game, _chosenTargets, ct, source: Source as Permanent)
            with
            { TriggeringPlayer = TriggeringPlayer };

        // Attribute any counter performed during this ability's resolution to
        // its controller — "a spell or ability you control counters a spell"
        // (Baral, Chief of Compliance). Mystic Snake / Voidslime counter their
        // target as part of resolving here. Restored afterward.
        var stack = game?.Stack;
        var previousResolutionController = stack?.CurrentResolutionController;
        if (stack is not null) stack.CurrentResolutionController = Controller;
        try
        {
            foreach (var effect in _effects)
            {
                await effect.ExecuteAsync(rc).ConfigureAwait(false);
            }
        }
        finally
        {
            if (stack is not null) stack.CurrentResolutionController = previousResolutionController;
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
