using Majik.Core.StateMachine;

namespace Majik.Core.Api;

/// <summary>
/// Maps the engine's phase value to its wire string. Since Slice 3 the
/// engine carries the precombat / postcombat distinction as first-class
/// <see cref="PhaseStateType.PreCombatMain"/> / <see cref="PhaseStateType.PostCombatMain"/>
/// values (CR 505 names them explicitly), so the label is simply
/// <c>phase.ToString()</c> — no reconstruction from <see cref="TurnStateType"/>
/// is needed. The <paramref name="turnState"/> parameter is retained for
/// call-site compatibility but no longer participates in disambiguation.
/// </summary>
public static class PhaseLabelResolver
{
    public const string PreCombatMain = "PreCombatMain";
    public const string PostCombatMain = "PostCombatMain";

    /// <summary>
    /// Wire label for the given phase. Returns <c>phase.ToString()</c>,
    /// which already matches the wire vocabulary for every step
    /// (Untap, Upkeep, Draw, PreCombatMain, BeginningOfCombat, …,
    /// PostCombatMain, End, Cleanup).
    /// </summary>
    public static string Resolve(PhaseStateType phase, TurnStateType? turnState)
        => phase.ToString();

    /// <summary>Nullable overload mirroring <see cref="Resolve(PhaseStateType, TurnStateType?)"/>.
    /// Returns <c>null</c> when <paramref name="phase"/> is null.</summary>
    public static string? Resolve(PhaseStateType? phase, TurnStateType? turnState)
        => phase is { } p ? Resolve(p, turnState) : null;
}
