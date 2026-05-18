using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// How a player chose to pay a mana cost: the set of permanents whose mana
/// abilities will be activated (in order). Empty means "use only floating mana".
/// </summary>
public sealed record ManaPayment(IReadOnlyList<ICard> Sources)
{
    public static readonly ManaPayment Empty = new(Array.Empty<ICard>());
}
