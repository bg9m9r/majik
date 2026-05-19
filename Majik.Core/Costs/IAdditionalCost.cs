using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 601.2f — additional cost on top of mana cost. Resolved at spell
/// announcement, before mana payment. Examples:
///   - "As an additional cost to cast this spell, sacrifice a creature."
///   - "As an additional cost to cast this spell, discard a card."
///
/// Implementations are responsible for both checking legality + paying
/// the cost. <see cref="Pay"/> returns true on success.
/// </summary>
public interface IAdditionalCost
{
    string Description { get; }
    bool CanPay(Player caster);
    bool Pay(Player caster);
}
