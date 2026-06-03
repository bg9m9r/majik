using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// Defines the standard phase sequence for a turn.
/// </summary>
public static class PhaseSequence
{
    /// <summary>
    /// Standard phase sequence for a normal turn.
    /// </summary>
    public static readonly StepStateType[] StandardSequence = new[]
    {
        StepStateType.Untap,
        StepStateType.Upkeep,
        StepStateType.Draw,
        StepStateType.PreCombatMain,     // Pre-combat main phase
        StepStateType.BeginningOfCombat,
        StepStateType.DeclareAttackers,
        StepStateType.DeclareBlockers,
        StepStateType.CombatDamage,
        StepStateType.EndOfCombat,
        StepStateType.PostCombatMain,    // Post-combat main phase
        StepStateType.End,
        StepStateType.Cleanup
    };

    /// <summary>
    /// Standard phase sequence for the first turn (skips draw step).
    /// </summary>
    public static readonly StepStateType[] FirstTurnSequence = new[]
    {
        StepStateType.Untap,
        StepStateType.Upkeep,
        // Draw step skipped on first turn
        StepStateType.PreCombatMain,     // Pre-combat main phase
        StepStateType.BeginningOfCombat,
        StepStateType.DeclareAttackers,
        StepStateType.DeclareBlockers,
        StepStateType.CombatDamage,
        StepStateType.EndOfCombat,
        StepStateType.PostCombatMain,    // Post-combat main phase
        StepStateType.End,
        StepStateType.Cleanup
    };

    /// <summary>
    /// Get the phase sequence for a turn.
    /// </summary>
    public static StepStateType[] GetSequence(bool isFirstTurn)
    {
        return isFirstTurn ? FirstTurnSequence : StandardSequence;
    }

    /// <summary>
    /// Get the next phase in the sequence.
    /// </summary>
    public static StepStateType? GetNextPhase(StepStateType currentPhase, bool isFirstTurn)
    {
        var sequence = GetSequence(isFirstTurn);
        var currentIndex = Array.IndexOf(sequence, currentPhase);
        
        if (currentIndex == -1 || currentIndex >= sequence.Length - 1)
        {
            return null;
        }

        return sequence[currentIndex + 1];
    }

    /// <summary>
    /// Check if a phase is in the standard sequence.
    /// </summary>
    public static bool IsInSequence(StepStateType phase, bool isFirstTurn)
    {
        var sequence = GetSequence(isFirstTurn);
        return Array.IndexOf(sequence, phase) >= 0;
    }
}
