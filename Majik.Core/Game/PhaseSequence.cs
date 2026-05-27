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
    public static readonly PhaseStateType[] StandardSequence = new[]
    {
        PhaseStateType.Untap,
        PhaseStateType.Upkeep,
        PhaseStateType.Draw,
        PhaseStateType.PreCombatMain,     // Pre-combat main phase
        PhaseStateType.BeginningOfCombat,
        PhaseStateType.DeclareAttackers,
        PhaseStateType.DeclareBlockers,
        PhaseStateType.CombatDamage,
        PhaseStateType.EndOfCombat,
        PhaseStateType.PostCombatMain,    // Post-combat main phase
        PhaseStateType.End,
        PhaseStateType.Cleanup
    };

    /// <summary>
    /// Standard phase sequence for the first turn (skips draw step).
    /// </summary>
    public static readonly PhaseStateType[] FirstTurnSequence = new[]
    {
        PhaseStateType.Untap,
        PhaseStateType.Upkeep,
        // Draw step skipped on first turn
        PhaseStateType.PreCombatMain,     // Pre-combat main phase
        PhaseStateType.BeginningOfCombat,
        PhaseStateType.DeclareAttackers,
        PhaseStateType.DeclareBlockers,
        PhaseStateType.CombatDamage,
        PhaseStateType.EndOfCombat,
        PhaseStateType.PostCombatMain,    // Post-combat main phase
        PhaseStateType.End,
        PhaseStateType.Cleanup
    };

    /// <summary>
    /// Get the phase sequence for a turn.
    /// </summary>
    public static PhaseStateType[] GetSequence(bool isFirstTurn)
    {
        return isFirstTurn ? FirstTurnSequence : StandardSequence;
    }

    /// <summary>
    /// Get the next phase in the sequence.
    /// </summary>
    public static PhaseStateType? GetNextPhase(PhaseStateType currentPhase, bool isFirstTurn)
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
    public static bool IsInSequence(PhaseStateType phase, bool isFirstTurn)
    {
        var sequence = GetSequence(isFirstTurn);
        return Array.IndexOf(sequence, phase) >= 0;
    }
}
