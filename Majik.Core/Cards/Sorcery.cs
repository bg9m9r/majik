using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents a sorcery spell.
/// Sorceries can only be cast during main phase with empty stack.
/// </summary>
public class Sorcery : Card
{
    public Sorcery(string name, string manaCost, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Sorcery }, supertypes, subtypes)
    {
    }

    /// <summary>
    /// Check if the sorcery can be cast.
    /// Sorceries can only be cast during main phase with empty stack (Rule 307.1).
    /// </summary>
    public bool CanBeCast(Player player, bool isMainPhase, bool isStackEmpty)
    {
        if (player == null)
        {
            return false;
        }

        // Sorceries can only be cast during main phase with empty stack
        return isMainPhase && isStackEmpty;
    }
}
