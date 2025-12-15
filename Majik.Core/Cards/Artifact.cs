using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents an artifact card/permanent.
/// </summary>
public class Artifact : Permanent
{
    /// <summary>
    /// Whether this is an Equipment artifact.
    /// </summary>
    public bool IsEquipment => HasSubtype(CardSubtype.Equipment);

    /// <summary>
    /// Whether this is a Vehicle artifact.
    /// </summary>
    public bool IsVehicle => HasSubtype(CardSubtype.Vehicle);

    public Artifact(string name, string manaCost, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Artifact }, supertypes, subtypes)
    {
    }
}
