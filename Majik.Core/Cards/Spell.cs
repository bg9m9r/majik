namespace Majik.Core.Cards;

/// <summary>
/// Base class for spell cards (cards that go on the stack).
/// Includes: Instants and Sorceries.
/// </summary>
public class Spell : Card
{
    public Spell(string name, string manaCost = "")
        : base(name, manaCost)
    {
    }

    /// <summary>Simulation copy constructor. Chains through
    /// <see cref="Card(Card)"/> for all base runtime state.
    /// <see cref="Spell"/> has no additional mutable runtime fields.
    /// </summary>
    protected Spell(Spell src) : base(src) { }

    /// <inheritdoc cref="Card.CloneForSim"/>
    internal override Card CloneForSim() => new Spell(this);
}
