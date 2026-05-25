using Majik.Core.StateMachine;

namespace Majik.Core.Api;

/// <summary>
/// Maps the engine's two-level phase representation to a single wire
/// string. The PhaseStateMachine collapses both main phases under
/// <see cref="PhaseStateType.Main"/>; clients need them distinguished
/// (CR 505 names them explicitly) so this helper consults the outer
/// <see cref="TurnStateType"/> to recover the missing label.
/// </summary>
public static class PhaseLabelResolver
{
    public const string PreCombatMain = "PreCombatMain";
    public const string PostCombatMain = "PostCombatMain";

    /// <summary>
    /// Wire label for the given phase. When <paramref name="phase"/> is
    /// <see cref="PhaseStateType.Main"/> and <paramref name="turnState"/>
    /// is one of the two main turn-states, returns the disambiguated
    /// "PreCombatMain" / "PostCombatMain" label. Otherwise falls back to
    /// <c>phase.ToString()</c>, which already matches the wire vocabulary
    /// for every other step (Untap, Upkeep, Draw, BeginningOfCombat, …).
    /// </summary>
    public static string Resolve(PhaseStateType phase, TurnStateType? turnState)
    {
        if (phase != PhaseStateType.Main) return phase.ToString();
        return turnState switch
        {
            TurnStateType.PreCombatMain => PreCombatMain,
            TurnStateType.PostCombatMain => PostCombatMain,
            // Unknown / not-yet-tracked turn state: fall back to the raw
            // enum name so existing tests + spectator-only callers that
            // don't wire turn-state tracking still see a stable string.
            _ => phase.ToString(),
        };
    }

    /// <summary>Nullable overload mirroring <see cref="Resolve(PhaseStateType, TurnStateType?)"/>.
    /// Returns <c>null</c> when <paramref name="phase"/> is null.</summary>
    public static string? Resolve(PhaseStateType? phase, TurnStateType? turnState)
        => phase is { } p ? Resolve(p, turnState) : null;
}
