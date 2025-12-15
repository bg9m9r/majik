namespace Majik.Core.Abilities;

/// <summary>
/// Interface for effects that can be executed when spells or abilities resolve.
/// </summary>
public interface IEffect
{
    /// <summary>
    /// Description of the effect.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the effect.
    /// </summary>
    void Execute();
}
