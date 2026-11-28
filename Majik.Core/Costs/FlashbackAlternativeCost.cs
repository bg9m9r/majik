using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.34 — Flashback. May be cast from the graveyard by paying the
/// flashback cost INSTEAD of the printed cost. After resolution (or if
/// the spell would otherwise leave the stack), the card is exiled.
/// </summary>
public sealed class FlashbackAlternativeCost : IAlternativeCost
{
    public string Description => $"Flashback {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }

    public FlashbackAlternativeCost(ManaCost flashbackCost)
    {
        AlternativeManaCost = flashbackCost ?? throw new ArgumentNullException(nameof(flashbackCost));
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Graveyard
        && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster)
    {
        // CR 702.34a — as it resolves, exile the card instead of putting it
        // anywhere else. Out of the stack zone list (where the cast put it),
        // not the graveyard's. The other exits (a counter, CR 608.2b) apply
        // the same rule through Spells.SpellStackExit.
        if (card.Zone == ZoneType.Exile) return;
        Majik.Core.Spells.SpellStackExit.MoveWithoutEvents(card, ZoneType.Exile);
    }
}
