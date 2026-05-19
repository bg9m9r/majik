using Majik.Core.ValueObjects;

namespace Majik.Core.Cards;

/// <summary>
/// CR 105 — colour of a card derived from its mana cost (W/U/B/R/G pips).
/// Generic mana doesn't contribute colour. Hybrid pips contribute both
/// listed colours. Phyrexian pips contribute the named colour.
/// Cards with no coloured pips are colourless.
/// </summary>
public static class CardColors
{
    public static IReadOnlySet<ManaColor> GetColors(ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        var set = new HashSet<ManaColor>();
        var cost = string.IsNullOrEmpty(card.ManaCost)
            ? ManaCost.Zero : ManaCost.Parse(card.ManaCost);

        if (cost.White > 0) set.Add(ManaColor.White);
        if (cost.Blue > 0) set.Add(ManaColor.Blue);
        if (cost.Black > 0) set.Add(ManaColor.Black);
        if (cost.Red > 0) set.Add(ManaColor.Red);
        if (cost.Green > 0) set.Add(ManaColor.Green);

        foreach (var h in cost.HybridPips)
        {
            if (h.Color1 != ManaColor.Generic) set.Add(h.Color1);
            if (h.Color2 != ManaColor.Generic) set.Add(h.Color2);
        }
        foreach (var p in cost.PhyrexianPips) set.Add(p);

        return set;
    }
}
