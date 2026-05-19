using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Generic "cast this card from exile" alternative cost. Used by Suspend,
/// Foretell, Cascade, Adventure follow-ups, Plot, Impulse-draw effects,
/// etc. By default the card resolves into its normal destination
/// (graveyard for instant/sorcery, battlefield for permanent) — same as
/// any other spell.
/// </summary>
public sealed class CastFromExileAlternativeCost : IAlternativeCost
{
    public string Description { get; }
    public ManaCost AlternativeManaCost { get; }

    public CastFromExileAlternativeCost(string description, ManaCost cost)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Exile && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster) { /* default destination */ }
}
