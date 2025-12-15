using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// Interface for costs that must be paid to cast spells or activate abilities.
/// </summary>
public interface ICost
{
    /// <summary>
    /// Description of the cost (for display purposes).
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Check if the cost can be paid.
    /// </summary>
    bool CanPay(Player player);

    /// <summary>
    /// Pay the cost.
    /// </summary>
    void Pay(Player player);
}
