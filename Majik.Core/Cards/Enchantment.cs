using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents an enchantment card/permanent.
/// </summary>
public class Enchantment : Permanent
{
    /// <summary>
    /// Whether this is an Aura enchantment.
    /// </summary>
    public bool IsAura => HasSubtype(CardSubtype.Aura);

    public Enchantment(string name, string manaCost, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Enchantment }, supertypes, subtypes)
    {
    }
}
