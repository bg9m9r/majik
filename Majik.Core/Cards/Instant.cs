using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents an instant spell.
/// Instants can be cast at instant speed (any time you have priority).
/// </summary>
public class Instant : Card
{
    public Instant(string name, string manaCost, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Instant }, supertypes, subtypes)
    {
    }

    /// <summary>
    /// Check if the instant can be cast.
    /// Instants can be cast at instant speed (any time you have priority).
    /// </summary>
    public bool CanBeCast(Player player)
    {
        if (player == null)
        {
            return false;
        }

        // Instants can be cast at instant speed
        // Additional restrictions (like "only during combat") would be handled by card-specific rules
        return true;
    }
}
