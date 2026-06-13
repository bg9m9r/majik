namespace Majik.Bot.Strategies;

/// <summary>
/// Marks an <see cref="IDeckStrategy"/> as the strategy for a named archetype.
/// <c>AllowMultiple = true</c> lets one strategy class serve several archetype
/// keys when they share a plan (e.g. the WU <c>AzoriusLotusBelcher</c> and the
/// red <c>Belcher</c> both win with the Goblin Charbelcher belch → one
/// <see cref="BelcherComboSolver"/> carries both).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DeckStrategyAttribute(string deckName) : Attribute
{
    public string DeckName { get; } = deckName;
}
