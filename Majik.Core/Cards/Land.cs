using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents a land card/permanent.
/// </summary>
public class Land : Permanent
{
    public Land(string name, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, "", new[] { CardType.Land }, supertypes, subtypes)
    {
    }

    /// <summary>
    /// Check if the land can be tapped for mana.
    /// For now, all lands can be tapped. Mana abilities will be added in future.
    /// </summary>
    public bool CanTapForMana()
    {
        return !IsTapped;
    }

    /// <summary>
    /// Tap the land for mana.
    /// Returns the mana generated (to be implemented with mana abilities).
    /// </summary>
    public void TapForMana()
    {
        if (!CanTapForMana())
        {
            throw new InvalidOperationException("Land cannot be tapped for mana");
        }

        Tap();
        // TODO: Generate mana through mana abilities
    }
}
