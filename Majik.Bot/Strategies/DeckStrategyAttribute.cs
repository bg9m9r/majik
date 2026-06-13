namespace Majik.Bot.Strategies;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DeckStrategyAttribute(string deckName) : Attribute
{
    public string DeckName { get; } = deckName;
}
