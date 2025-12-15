namespace Majik.Core.Abilities;

/// <summary>
/// Interface for mana abilities.
/// Mana abilities generate mana and don't use the stack (Rule 605).
/// </summary>
public interface IManaAbility
{
    /// <summary>
    /// The source of this mana ability (usually a land or permanent).
    /// </summary>
    object Source { get; }

    /// <summary>
    /// The controller of this mana ability.
    /// </summary>
    Players.Player Controller { get; }

    /// <summary>
    /// The mana cost this ability generates.
    /// </summary>
    ValueObjects.ManaCost ManaGenerated { get; }

    /// <summary>
    /// Check if this mana ability can be activated.
    /// </summary>
    bool CanActivate();

    /// <summary>
    /// Activate the mana ability and generate mana.
    /// </summary>
    ValueObjects.ManaCost Activate();
}
