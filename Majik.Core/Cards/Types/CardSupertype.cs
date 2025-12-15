namespace Majik.Core.Cards.Types;

/// <summary>
/// Card supertypes as defined in Magic: The Gathering rules (Rule 205.4).
/// Supertypes appear before card types.
/// </summary>
public enum CardSupertype
{
    /// <summary>
    /// Basic supertype (e.g., Basic Land).
    /// </summary>
    Basic,

    /// <summary>
    /// Legendary supertype.
    /// </summary>
    Legendary,

    /// <summary>
    /// Snow supertype.
    /// </summary>
    Snow,

    /// <summary>
    /// World supertype (legacy).
    /// </summary>
    World
}
