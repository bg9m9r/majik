namespace Majik.Core.Players.Agents;

/// <summary>London mulligan choice (Rule 103.4).</summary>
public enum MulliganDecision
{
    /// <summary>Keep the current hand.</summary>
    Keep,

    /// <summary>Mulligan: shuffle hand back, redraw 7, then put N cards on bottom.</summary>
    Mulligan,
}
