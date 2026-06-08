using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Game;
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
    /// CR 602.5c / 605.1a — optional "Activate only if &lt;condition&gt;"
    /// gate evaluated against live game state. Distinct from the timing-only
    /// <see cref="IsSorcerySpeed"/> rider: this carries an arbitrary
    /// predicate (Metalcraft, Delirium, "you control a Forest", a hand-size
    /// check, …). <see cref="Rules.ActionValidator"/> and
    /// <see cref="Services.AbilityActivator"/> reject the activation when it
    /// returns false. Null ⇒ no extra gate (always activatable, subject to
    /// the usual timing rules). Re-evaluated on every call so the condition
    /// reflects current state.
    /// </summary>
    private readonly Func<bool>? _canActivateCheck;

    /// <summary>
    /// CR 602.5c — true iff this ability's "Activate only if" condition (if
    /// any) is currently satisfied. Always true when no gate was supplied.
    /// Re-evaluates the predicate on each call.
    /// </summary>
    public bool CanActivateNow() => _canActivateCheck?.Invoke() ?? true;

    /// <summary>
    /// The "Activate only if" gate predicate, exposed so the activation
    /// service can mirror it onto the copy it puts on the stack (CR 602.4 —
    /// the stack object must carry the same characteristics as the
    /// original). Null when no gate was supplied.
    /// </summary>
    public Func<bool>? CanActivateCheck => _canActivateCheck;

    /// <summary>
    /// The targets chosen by the activating player's agent (parallel
    /// list-of-lists matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> after the agent responds. Effects
    /// should read from here, not from the legacy <see cref="Targets"/>
    /// collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    /// <summary>
    /// STAGE 2/3 (re-sourceable abilities) — provenance flag asserting that
    /// EVERY effect of this ability reads its source / subject off the live
    /// <see cref="ResolutionContext"/> (its own <see cref="ResolutionContext.Source"/>,
    /// <see cref="Controller"/>, or <see cref="ChosenTargets"/>) rather than
    /// capturing a specific authoring permanent in a closure. When true, the
    /// whole ability is SOUND to re-home via <see cref="RebindTo"/>: costs
    /// already re-home (Stage 1) and the effects re-source themselves at
    /// resolution. When false (the default), a re-sourced copy may still
    /// reference the ORIGINAL source through a captured closure, so a consumer
    /// like Agatha's Soul Cauldron must NOT blindly RebindTo it.
    ///
    /// <para>
    /// Set true only by build paths that have audited every effect they emit
    /// — currently the data-driven CardDef path
    /// (<see cref="Majik.Core.CardData.Definitions.CardDefActivatedAbility"/>),
    /// whose self-source verbs (pump / connive / explore) were migrated to read
    /// <see cref="ResolutionContext.Source"/>, with all other verbs already
    /// scoped to the controller / chosen targets. Preserved across
    /// <see cref="RebindTo"/>.
    /// </para>
    /// </summary>
    public bool RebindSafe { get; init; }

    public ActivatedAbility(
        object source,
        Player controller,
        IEnumerable<ITarget>? targets = null,
        IEnumerable<ICost>? costs = null,
        IEnumerable<IEffect>? effects = null,
        IEnumerable<TargetRequest>? targetRequests = null,
        bool sorcerySpeed = false,
        Func<bool>? canActivateCheck = null,
        bool rebindSafe = false)
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
        // PLAN 08 — per-game deterministic id. Reseeded from the ambient
        // DeterministicIdSource inside a game scope; Guid.NewGuid() fallback
        // outside one.
        Id = Majik.Core.Game.DeterministicIdScope.NewId();
        Timestamp = DateTime.UtcNow;
        IsSorcerySpeed = sorcerySpeed;
        RebindSafe = rebindSafe;
        _canActivateCheck = canActivateCheck;
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
    /// CR 707.2 — re-instantiate this activated ability bound to a new source
    /// permanent + controller, preserving the structural fields (costs,
    /// effects, target requests, sorcery-speed rider, "activate only if" gate).
    /// Used by the copy machinery to mirror a SOURCE permanent's printed
    /// activated abilities onto the COPY so the copy can activate them as its
    /// own (the stack object is sourced from the copy via
    /// <see cref="Services.AbilityActivator"/>).
    ///
    /// STAGE 1 — COSTS re-home. Each <see cref="AdditionalCost"/> whose captured
    /// permanent is reference-equal to this ability's own <see cref="Source"/>
    /// is rebound to <paramref name="newSource"/> via
    /// <see cref="AdditionalCost.RebindSource"/>, so a re-sourced ability taps /
    /// sacrifices the NEW source rather than the original permanent. Mana costs
    /// and non-matching costs pass through unchanged.
    ///
    /// STAGE 2/3 — EFFECTS are REUSED (the same <see cref="IEffect"/> objects)
    /// and re-home iff they read their source off the live
    /// <see cref="ResolutionContext"/> (<see cref="ResolutionContext.Source"/> =
    /// the new source at resolution, since the rebound ability's own
    /// <see cref="Source"/> is <paramref name="newSource"/>). The
    /// <see cref="RebindSafe"/> flag (preserved here) records whether EVERY
    /// effect does so; a caller that requires soundness (Agatha's grant) only
    /// re-homes abilities for which it is true. Effects that still capture the
    /// ORIGINAL source in a closure (un-migrated bespoke factory abilities,
    /// <see cref="RebindSafe"/> = false) are NOT made sound by this method — the
    /// caller must gate on the flag.
    /// </summary>
    public ActivatedAbility RebindTo(object newSource, Player newController) =>
        new(
            source: newSource ?? throw new ArgumentNullException(nameof(newSource)),
            controller: newController ?? throw new ArgumentNullException(nameof(newController)),
            targets: _targets.Count > 0 ? _targets : null,
            // STAGE 1 — re-home source-capturing costs ({T} / sacrifice) onto
            // the new source so the rebound ability pays its cost with the new
            // permanent; other ICost types pass through untouched.
            costs: _costs.Count > 0
                ? _costs.Select(c =>
                    c is AdditionalCost ac ? ac.RebindSource(Source, newSource) : c)
                : null,
            effects: _effects.Count > 0 ? _effects : null,
            targetRequests: TargetRequests.Count > 0 ? TargetRequests : null,
            sorcerySpeed: IsSorcerySpeed,
            canActivateCheck: _canActivateCheck,
            rebindSafe: RebindSafe);

    /// <summary>
    /// Store the targets chosen by the activating player's agent. Called by
    /// <see cref="AbilityActivationFlow.ActivateAsync"/> after the agent
    /// responds to each <see cref="TargetRequest"/>.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
    }

    public void Resolve() => ResolveAsync(null, null).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve the ability's effects (CR 608) on the async path.
    /// Builds a <see cref="ResolutionContext"/> from this ability's
    /// <see cref="Controller"/> + <see cref="ChosenTargets"/> and the
    /// resolver-supplied <paramref name="agent"/> / <paramref name="game"/> /
    /// <paramref name="ct"/>, then awaits each effect in declaration order.
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

        // STAGE 1 — expose the ability's own source (when it is a battlefield
        // permanent) so effects can read "their source" generically off the
        // context (CR 113.7) rather than capturing a specific permanent.
        var rc = ResolutionContext.For(
            Controller, agent, game, _chosenTargets, ct, source: Source as Cards.Permanent);

        // Resolution logic (Rule 608) — await each effect in order.
        foreach (var effect in _effects)
        {
            await effect.ExecuteAsync(rc).ConfigureAwait(false);
        }

        _resolutionState = ResolutionState.Resolved(DateTime.UtcNow);
    }
}
