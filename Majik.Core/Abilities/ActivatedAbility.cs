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

    // GAP 2 (per-activation variable-X ledger) — the X chosen by the
    // activating player's agent during AbilityActivationFlow / the TurnDriver
    // dispatcher. Null = no {X} in cost / not yet chosen. Reset per activation
    // (mirror _chosenTargets) so a re-activation that doesn't choose X cannot
    // reuse a stale value.
    private int? _chosenX;

    // CR 601.2d / CR 119.4 — deferred divide-damage announcement for a
    // "deals N damage divided as you choose among …" ACTIVATED / loyalty
    // ability (the activated-ability analogue of the trigger path's
    // TriggeredAbility.DamageDivision and the spell path's
    // SpellDefinition.DamageDivision). The spec is declared at construction
    // time; the chosen per-target split is recorded after the activating
    // player's agent responds at dispatch time (CR 601.2d, right after
    // ChosenTargets — TurnDriver/GameFacade DispatchActivate + DispatchLoyalty),
    // then threaded into ResolutionContext.DamageDivision at resolve time. Null
    // until prompted (or when the ability deals no divided damage).
    private IReadOnlyList<Game.DamageAllocation>? _chosenDamageDivision;

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
    /// CR 602.5c / 605.1a — context-aware variant of the "Activate only if"
    /// gate. When set, it is preferred over the context-less
    /// <see cref="_canActivateCheck"/> IF a live <see cref="GameContext"/> is
    /// available at the activation-legality check (the bot's
    /// <c>LegalActionEnumerator</c> and the live driver both have one in scope).
    /// Lets the gate read live game state — the opponent set, per-turn tallies —
    /// that a captured build-time resolver can't reach on the production routed
    /// build (Hired Claw's "an opponent lost life this turn"). Null ⇒ no
    /// context-aware gate; the engine falls back to <see cref="_canActivateCheck"/>.
    /// </summary>
    private readonly Func<GameContext, bool>? _canActivateCheckCtx;

    /// <summary>
    /// CR 602.5c — true iff this ability's "Activate only if" condition (if
    /// any) is currently satisfied. Always true when no gate was supplied.
    /// Re-evaluates the predicate on each call. Context-less form; callers that
    /// hold a <see cref="GameContext"/> should prefer
    /// <see cref="CanActivateNow(GameContext)"/> so a context-aware gate fires.
    /// </summary>
    public bool CanActivateNow() => CanActivateNow(null);

    /// <summary>
    /// CR 602.5c — context-aware overload. Prefers the context-aware gate
    /// (<see cref="_canActivateCheckCtx"/>) when one was supplied AND a non-null
    /// <paramref name="game"/> is available; otherwise falls back to the
    /// context-less <see cref="_canActivateCheck"/> (so call sites without a
    /// GameContext still gate correctly). Always true when neither gate was
    /// supplied.
    /// </summary>
    public bool CanActivateNow(GameContext? game)
    {
        if (_canActivateCheckCtx != null && game != null)
        {
            return _canActivateCheckCtx(game);
        }
        return _canActivateCheck?.Invoke() ?? true;
    }

    /// <summary>
    /// The "Activate only if" gate predicate, exposed so the activation
    /// service can mirror it onto the copy it puts on the stack (CR 602.4 —
    /// the stack object must carry the same characteristics as the
    /// original). Null when no gate was supplied.
    /// </summary>
    public Func<bool>? CanActivateCheck => _canActivateCheck;

    /// <summary>
    /// The context-aware "Activate only if" gate predicate, exposed so the
    /// activation service can mirror it onto the stack copy (CR 602.4). Null
    /// when no context-aware gate was supplied.
    /// </summary>
    public Func<GameContext, bool>? CanActivateCheckCtx => _canActivateCheckCtx;

    /// <summary>
    /// The targets chosen by the activating player's agent (parallel
    /// list-of-lists matching <see cref="TargetRequests"/>). Populated by
    /// <see cref="SetChosenTargets"/> after the agent responds. Effects
    /// should read from here, not from the legacy <see cref="Targets"/>
    /// collection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>> ChosenTargets => _chosenTargets;

    /// <summary>
    /// GAP 2 — the X chosen by the activating player's agent for a variable-X
    /// ({X}-cost) activated ability. Parallel to <see cref="ChosenTargets"/>:
    /// populated by <see cref="SetChosenX"/> during activation
    /// (<see cref="Game.AbilityActivationFlow.ActivateAsync"/> + the live
    /// <c>TurnDriver</c> dispatcher), then surfaced to resolution effects via
    /// <see cref="ResolutionContext.ChosenX"/>. Null when the cost has no {X}
    /// or the agent has not yet chosen — effects treat null as 0
    /// (<c>ChosenX ?? 0</c>). Reset per activation so a stale value can't leak
    /// into a later re-activation.
    /// </summary>
    public int? ChosenX => _chosenX;

    /// <summary>
    /// CR 601.2d / CR 119.4 — the "deals N damage divided as you choose among …"
    /// announcement spec for an ACTIVATED / loyalty ability. Declared at
    /// construction (the activated-ability analogue of
    /// <see cref="TriggeredAbility.DamageDivision"/> and
    /// <see cref="SpellDefinition.DamageDivision"/>). The live driver's
    /// dispatcher (TurnDriver / GameFacade DispatchActivate + DispatchLoyalty)
    /// spots it, prompts the activating player's agent for the per-target split
    /// right after targets are chosen (CR 601.2d), and records the answer via
    /// <see cref="SetChosenDamageDivision"/>. Null ⇒ the ability deals no
    /// divided damage and no division prompt is raised.
    /// </summary>
    public DamageDivisionSpec? DamageDivision { get; }

    /// <summary>
    /// CR 601.2d / CR 119.4 — the per-target division announced by the
    /// activating player's agent for this ability's <see cref="DamageDivision"/>.
    /// Populated by <see cref="SetChosenDamageDivision"/> at dispatch time
    /// (mirrored onto the stack copy by <see cref="Services.AbilityActivator"/>),
    /// then threaded into <see cref="ResolutionContext.DamageDivision"/> at
    /// resolution. Null when no division was prompted (no spec, no agent, or an
    /// empty target slot) — the resolution effect falls back to its own even
    /// split.
    /// </summary>
    public IReadOnlyList<Game.DamageAllocation>? ChosenDamageDivision => _chosenDamageDivision;

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
        bool rebindSafe = false,
        Func<GameContext, bool>? canActivateCheckCtx = null,
        DamageDivisionSpec? damageDivision = null)
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
        DamageDivision = damageDivision;
        _canActivateCheck = canActivateCheck;
        _canActivateCheckCtx = canActivateCheckCtx;
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
            // STAGE 1 — re-home source-capturing costs onto the new source so
            // the rebound ability pays its cost with the new permanent. Two
            // re-home seams are consulted in order: AdditionalCost ({T} /
            // sacrifice) via its bespoke RebindSource, and the bare
            // counter-payment costs (add / remove +1/+1 or charge counter) via
            // the IRebindableCost interface. Any other ICost passes through
            // untouched (mana / pay-life carry no source permanent).
            costs: _costs.Count > 0
                ? _costs.Select(c => c switch
                {
                    AdditionalCost ac => ac.RebindSource(Source, newSource),
                    IRebindableCost rc => rc.RebindTo(Source, newSource),
                    _ => c,
                })
                : null,
            effects: _effects.Count > 0 ? _effects : null,
            // STAGE 1 (gatherer) — re-home each "... you control"-scoped
            // candidate gatherer onto the NEW controller so a re-sourced
            // "target creature YOU control" request gathers the bearer's
            // controller's board, not the exiled card owner's
            // (agatha-mother-of-runes-style-candidate-gatherer-controller-rebind).
            // Requests with a static pool or a non-rebindable closure pass
            // through unchanged (RebindController no-ops).
            targetRequests: TargetRequests.Count > 0
                ? TargetRequests.Select(r => r.RebindController(newController)).ToList()
                : null,
            sorcerySpeed: IsSorcerySpeed,
            canActivateCheck: _canActivateCheck,
            rebindSafe: RebindSafe,
            canActivateCheckCtx: _canActivateCheckCtx,
            // CR 601.2d — a re-sourced copy still deals the same divided damage;
            // the chosen split is re-announced per activation, so only the
            // declarative SPEC carries over (not the per-activation _chosenDamageDivision).
            damageDivision: DamageDivision);

    /// <summary>
    /// Store the targets chosen by the activating player's agent. Called by
    /// <see cref="AbilityActivationFlow.ActivateAsync"/> after the agent
    /// responds to each <see cref="TargetRequest"/>.
    /// </summary>
    public void SetChosenTargets(IReadOnlyList<IReadOnlyList<object>> chosen)
    {
        _chosenTargets = chosen ?? Array.Empty<IReadOnlyList<object>>();
    }

    /// <summary>
    /// GAP 2 — record the X chosen for this activation (CR 601.2e analogue for
    /// activated abilities). Called by the activation flow after the agent's
    /// <see cref="Players.Agents.IPlayerAgent.ChooseXAsync"/> response, before
    /// mana payment. The {X} in the mana cost is expanded to X generic at
    /// payment time (shared with the spell path via
    /// <see cref="ValueObjects.ManaCost.AddGenericCost"/>); this value is what
    /// the resolution effect reads.
    /// </summary>
    public void SetChosenX(int x) => _chosenX = x;

    /// <summary>
    /// CR 601.2d / CR 119.4 — record the per-target damage division announced by
    /// the activating player's agent for this ability's <see cref="DamageDivision"/>.
    /// Called by the live dispatcher (TurnDriver / GameFacade DispatchActivate +
    /// DispatchLoyalty) right after target collection, before the ability is put
    /// on the stack; threaded into <see cref="ResolutionContext.DamageDivision"/>
    /// at resolution. Null clears any prior split (no-op fallback to even split).
    /// </summary>
    public void SetChosenDamageDivision(IReadOnlyList<Game.DamageAllocation>? chosen) =>
        _chosenDamageDivision = chosen;

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
            Controller, agent, game, _chosenTargets, ct, source: Source as Cards.Permanent,
            chosenX: _chosenX)
            with
            {
                // CR 601.2d / CR 119.4 — thread the agent-announced per-target
                // split (recorded at dispatch) so the deal-damage effect reads
                // ResolutionContext.DamageDivision instead of an even-split
                // fallback. Null on a non-divided ability / no-agent path.
                DamageDivision = _chosenDamageDivision,
            };

        // Attribute any counter performed during this ability's resolution to
        // its controller — "a spell or ability you control counters a spell"
        // (Baral, Chief of Compliance). Restored afterward.
        var stack = game?.Stack;
        var previousResolutionController = stack?.CurrentResolutionController;
        if (stack is not null) stack.CurrentResolutionController = Controller;
        try
        {
            // Resolution logic (Rule 608) — await each effect in order.
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
