using Majik.Core.ValueObjects;

namespace Majik.Core.Cards;

/// <summary>
/// CR 105 — colour of a card derived from its mana cost (W/U/B/R/G pips).
/// Generic mana doesn't contribute colour. Hybrid pips contribute both
/// listed colours. Phyrexian pips contribute the named colour.
/// Cards with no coloured pips are colourless.
///
/// <para>CR 111.4 / CR 903.4 — tokens have no printed mana cost; their
/// colour is set by the effect that created them ("create a 2/2 green
/// Cat creature token"). When a card carries an explicit
/// <see cref="Card.TokenColorsOverride"/>, that override is the single
/// source of truth and the mana-cost scan is bypassed. An empty
/// override list is explicit "colourless" (Wurmcoil's Phyrexian Wurm
/// tokens, Karn Scion of Urza's Construct tokens).</para>
/// </summary>
public static class CardColors
{
    public static IReadOnlySet<ManaColor> GetColors(ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        // CR 111.4 — explicit token colour override wins over a mana-cost
        // scan (tokens have no printed mana cost; their colour is whatever
        // the creating effect stamped). Empty override == colourless by
        // explicit declaration.
        if (card is Card concrete && concrete.TokenColorsOverride != null)
        {
            var overrideSet = new HashSet<ManaColor>();
            foreach (var c in concrete.TokenColorsOverride)
            {
                if (c != ManaColor.Generic && c != ManaColor.Colorless)
                    overrideSet.Add(c);
            }
            return overrideSet;
        }

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
