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

    /// <summary>Simulation copy constructor. Chains through
    /// <see cref="Card(Card)"/> for all base runtime state.
    /// <see cref="Sorcery"/> has no additional mutable runtime fields.
    /// </summary>
    protected Sorcery(Sorcery src) : base(src) { }

    /// <inheritdoc cref="Card.CloneForSim"/>
    internal override Card CloneForSim() => new Sorcery(this);

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
