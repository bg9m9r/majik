namespace Majik.Core.Cards.Types;

/// <summary>
/// Card types as defined in Magic: The Gathering rules (Rule 3).
/// Cards can have multiple types.
/// </summary>
public enum CardType
{
    /// <summary>
    /// Artifact card type.
    /// </summary>
    Artifact,

    /// <summary>
    /// Creature card type.
    /// </summary>
    Creature,

    /// <summary>
    /// Enchantment card type.
    /// </summary>
    Enchantment,

    /// <summary>
    /// Instant card type.
    /// </summary>
    Instant,

    /// <summary>
    /// Land card type.
    /// </summary>
    Land,

    /// <summary>
    /// Planeswalker card type.
    /// </summary>
    Planeswalker,

    /// <summary>
    /// Sorcery card type.
    /// </summary>
    Sorcery,

    /// <summary>
    /// Tribal card type (legacy).
    /// </summary>
    Tribal
}
