using Majik.Core.Events;

namespace Majik.Core.Game;

/// <summary>
/// Engine SAFETY NET against a runaway loop. This is not the CR 732 loop
/// shortcut: it does not detect or shorten loops. It is a hard cap that stops a
/// game that has stopped advancing (an engine bug, or an agent that repeats
/// an optional action forever), so a live match or a test run never hangs.
///
/// <para>Two caps, both counted by <see cref="PriorityLoop"/> over ONE step
/// (one <c>RunUntilRoundEndsAsync</c> call; the game "advances" when the step
/// ends):</para>
/// <list type="bullet">
///   <item><see cref="MaxActionsPerStep"/>: committed priority actions plus
///   stack resolutions within the step;</item>
///   <item><see cref="MaxStackDepth"/>: objects on the stack at once.</item>
/// </list>
/// <para>Both are far above anything a real game reaches. The gate's measured
/// maxima are recorded next to each constant; each cap is at least 20 times
/// its maximum (LoopSafetyNetTests pins that).</para>
///
/// <para>When a cap trips, the game ends as a draw. CR 104.4b makes a loop of
/// mandatory actions that no player can stop a draw; this is the closest rule.
/// Every player still in the game loses at once, which CR 104.4a also makes a
/// draw. The trip is published as a <see cref="LoopSafetyNetTrippedEvent"/>,
/// which GameFacade reports through its contained-fault channel.</para>
/// </summary>
public static class LoopSafetyNet
{
    /// <summary>Most committed actions + resolutions in one step. The full-game
    /// gate (1250 bot games, 3 runs) peaked at 26; this is about 385 times that,
    /// leaving room for long human combo turns (mana abilities count).</summary>
    public const int MaxActionsPerStep = 10_000;

    /// <summary>Most objects on the stack at once. The full-game gate peaked at
    /// 12; this is about 83 times that.</summary>
    public const int MaxStackDepth = 1_000;

    // Test seams: a lower cap for the current async flow only, so a test can
    // trip the net with a short constructed loop without affecting parallel
    // tests.
    private static readonly AsyncLocal<int?> ActionLimitOverride = new();
    private static readonly AsyncLocal<int?> StackDepthLimitOverride = new();

    internal static int ActionLimit => ActionLimitOverride.Value ?? MaxActionsPerStep;
    internal static int StackDepthLimit => StackDepthLimitOverride.Value ?? MaxStackDepth;

    /// <summary>Test seam: lower the caps for the current async flow.</summary>
    internal static IDisposable OverrideForTest(int? actionLimit, int? stackDepthLimit)
    {
        var (a, s) = (ActionLimitOverride.Value, StackDepthLimitOverride.Value);
        ActionLimitOverride.Value = actionLimit;
        StackDepthLimitOverride.Value = stackDepthLimit;
        return new Restore(() => { ActionLimitOverride.Value = a; StackDepthLimitOverride.Value = s; });
    }

    /// <summary>
    /// Observability for measuring the caps' headroom: called once per step
    /// with that step's action count and peak stack depth. Process-wide;
    /// null by default. The full-game gate uses it to record the maxima.
    /// </summary>
    public static Action<int, int>? StepObserver { get; set; }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

/// <summary>
/// The <see cref="LoopSafetyNet"/> tripped: the game stopped advancing and was
/// ended as a draw (CR 104.4b). It is a safety net, not a rules outcome.
/// </summary>
public sealed class LoopSafetyNetTrippedEvent : GameEvent
{
    public LoopSafetyNetTrippedEvent(string message, int actionsThisStep, int stackDepth)
    {
        Message = message;
        ActionsThisStep = actionsThisStep;
        StackDepth = stackDepth;
    }

    /// <summary>Why the net tripped, with the turn and step.</summary>
    public string Message { get; }

    /// <summary>Committed actions + resolutions in the step so far.</summary>
    public int ActionsThisStep { get; }

    /// <summary>Objects on the stack when it tripped.</summary>
    public int StackDepth { get; }
}

/// <summary>The fault GameFacade reports when the <see cref="LoopSafetyNet"/>
/// trips. It is never thrown.</summary>
public sealed class LoopSafetyNetException(string message) : Exception(message);
