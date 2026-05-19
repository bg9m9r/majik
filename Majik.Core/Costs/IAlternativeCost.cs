using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — alternative cost. May be paid INSTEAD of the spell's
/// printed mana cost. Each alternative may also impose its own zone
/// restrictions (e.g. Flashback only legal from graveyard) and
/// post-resolution side-effects (Flashback exiles).
/// </summary>
public interface IAlternativeCost
{
    string Description { get; }
    ManaCost AlternativeManaCost { get; }
    bool CanCastFor(ICard card, Player caster);
    /// <summary>Called after the spell resolves to apply any side-effect
    /// the alternative cost imposes (e.g. exile the card).</summary>
    void OnResolved(ICard card, Player caster);
}
