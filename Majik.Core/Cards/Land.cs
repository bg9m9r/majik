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

    /// <summary>Simulation copy constructor. Chains through
    /// <see cref="Permanent(Permanent)"/> for all base + Permanent runtime
    /// state. <see cref="Land"/> has no additional mutable runtime fields.
    /// </summary>
    protected Land(Land src) : base(src) { }

    /// <inheritdoc cref="Card.CloneForSim"/>
    internal override Card CloneForSim() => new Land(this);

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
