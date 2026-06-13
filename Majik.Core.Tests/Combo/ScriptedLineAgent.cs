using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// Phase B1 — a hand-authored decision script for the combo-line tests
/// (plan 2026-06-13). Subclasses <see cref="Majik.Core.Tests.Helpers.DelegatingAgent"/>
/// so EVERY prompt the line does not anticipate throws loudly
/// (<see cref="NotSupportedException"/>) — a combo that reaches an unscripted
/// decision means the engine asked something the line author didn't expect,
/// which is exactly the finding the line tests exist to surface.
///
/// <para>The agent drives a single seat through one combo line:
/// <list type="bullet">
///   <item><see cref="ChoosePriorityActionAsync"/> dequeues the next scripted
///   <see cref="PriorityAction"/> step. Each step is a factory taking the live
///   <see cref="GameContext"/> so it can resolve cards from current zones
///   (e.g. "the Charbelcher now on the battlefield"). When the queue is empty
///   the seat passes priority (so the game can progress to the next step /
///   the opponent / resolution) — passing is never a "decision the line
///   needs", it is the natural idle.</item>
///   <item>Mana payment (<see cref="ChooseManaSourcesAsync"/>) returns
///   <see cref="ManaPayment.Empty"/> — the universal "auto-tap from untapped
///   sources / floating pool" convention every agent uses. Tapping for mana is
///   mechanical, not a branch point, so it is not scripted.</item>
///   <item>MDFC face choice, scry, and any other <see cref="ChooseAsync"/> /
///   target / X prompt the line genuinely decides is supplied through the
///   scripted hooks below; anything not supplied throws via the base class.</item>
/// </list></para>
/// </summary>
public class ScriptedLineAgent : Majik.Core.Tests.Helpers.DelegatingAgent
{
    private readonly Queue<Func<GameContext, PriorityAction>> _actions = new();

    /// <summary>
    /// Optional per-line handler for an MDFC face choice (CR 712.3). When the
    /// engine raises a face prompt, this is consulted; if it returns a choice
    /// the line uses it, otherwise the prompt throws (unscripted).
    /// </summary>
    public Func<GameContext, ChoiceRequest, IReadOnlyList<object>?>? OnChoose { get; init; }

    /// <summary>
    /// Optional per-line target handler. Null → any target prompt throws.
    /// </summary>
    public Func<GameContext, TargetRequest, IReadOnlyList<object>?>? OnChooseTargets { get; init; }

    /// <summary>
    /// Optional per-line X handler (variable-cost spells, e.g. Whir of
    /// Invention). Null → any X prompt throws.
    /// </summary>
    public Func<GameContext, ICard, int?>? OnChooseX { get; init; }

    /// <summary>True once the scripted action queue is drained.</summary>
    public bool ScriptExhausted => _actions.Count == 0;

    /// <summary>Number of scripted priority steps still pending.</summary>
    public int PendingSteps => _actions.Count;

    /// <summary>Append a scripted priority step (resolved against live state).</summary>
    public ScriptedLineAgent Then(Func<GameContext, PriorityAction> step)
    {
        _actions.Enqueue(step);
        return this;
    }

    /// <summary>Append a fixed scripted priority step.</summary>
    public ScriptedLineAgent Then(PriorityAction step)
    {
        _actions.Enqueue(_ => step);
        return this;
    }

    public override Task<PriorityAction> ChoosePriorityActionAsync(
        GameContext ctx, CancellationToken ct = default)
    {
        if (_actions.Count == 0)
        {
            // No scripted action left — idle by passing. The line's kill has
            // either fired (assert post-game) or the harness turn-cap stops it.
            return Task.FromResult(PriorityAction.Pass);
        }

        // The combo lines run at sorcery speed (artifact casts, the belch we
        // sequence in one main-phase window). Hold the script until the
        // controller's OWN main phase with an empty stack (CR 116.2a). Earlier
        // windows (upkeep / draw / opponent priority / a spell still resolving
        // on the stack) just pass — those are not "decisions the line needs",
        // they are the natural idle before the kill turn's main phase.
        var inOwnMainWindow =
            ReferenceEquals(ctx.ActivePlayer, ctx.Self)
            && ctx.CurrentPhase is { } phase && phase.IsMain()
            && ctx.Stack.Count == 0;

        if (!inOwnMainWindow)
        {
            return Task.FromResult(PriorityAction.Pass);
        }

        var next = _actions.Dequeue();
        return Task.FromResult(next(ctx));
    }

    public override Task<ManaPayment> ChooseManaSourcesAsync(
        GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Task.FromResult(ManaPayment.Empty); // auto-tap; mechanical, not scripted

    public override Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
    {
        var picked = OnChoose?.Invoke(ctx, req);
        if (picked != null) return Task.FromResult(picked);
        // Fall through to the base (throws NotSupportedException) — unscripted.
        return base.ChooseAsync(ctx, req, ct);
    }

    public override Task<IReadOnlyList<object>> ChooseTargetsAsync(
        GameContext ctx, TargetRequest request, CancellationToken ct = default)
    {
        var picked = OnChooseTargets?.Invoke(ctx, request);
        if (picked != null) return Task.FromResult(picked);
        return base.ChooseTargetsAsync(ctx, request, ct);
    }

    public override Task<int> ChooseXAsync(
        GameContext ctx, ICard source, CancellationToken ct = default)
    {
        var x = OnChooseX?.Invoke(ctx, source);
        if (x != null) return Task.FromResult(x.Value);
        return base.ChooseXAsync(ctx, source, ct);
    }
}
