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
}
