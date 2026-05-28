using System.Text.Json;
using Majik.Core.ValueObjects;

namespace Majik.Core.Cards;

/// <summary>
/// CR 105 / CR 202.2 — colour of a card derived from (a) its mana cost
/// (W/U/B/R/G pips) and (b) any printed color indicator (the round dot
/// to the left of the type line — CR 202.2c). Generic mana doesn't
/// contribute colour. Hybrid pips contribute both listed colours.
/// Phyrexian pips contribute the named colour. Cards with no coloured
/// pips AND no color indicator are colourless.
///
/// <para>CR 202.2c — a color indicator is a printed cue that sets the
/// card's color independently of its mana cost. Dryad Arbor is the
/// canonical example: a Land Creature with empty mana cost printed with
/// a green color indicator, so its color is green even though no green
/// pip appears in its (empty) cost. The indicator is stored on
/// <see cref="Card.ColorIndicator"/>; when non-null its entries are
/// unioned with the mana-cost-derived colors so a tutor like Green
/// Sun's Zenith ("green creature card") finds Dryad Arbor.</para>
///
/// <para>CR 111.4 / CR 903.4 — tokens have no printed mana cost; their
/// colour is set by the effect that created them ("create a 2/2 green
/// Cat creature token"). When a card carries an explicit
/// <see cref="Card.TokenColorsOverride"/>, that override is the single
/// source of truth and both the mana-cost scan and the color-indicator
/// pathway are bypassed. An empty override list is explicit "colourless"
/// (Wurmcoil's Phyrexian Wurm tokens, Karn Scion of Urza's Construct
/// tokens).</para>
/// </summary>
public static class CardColors
{
    public static IReadOnlySet<ManaColor> GetColors(ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        // CR 111.4 — explicit token colour override wins over both the
        // mana-cost scan and the color-indicator pathway (tokens have no
        // printed mana cost or indicator; their colour is whatever the
        // creating effect stamped). Empty override == colourless by
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

        // CR 202.2c — union in any printed color indicator. This is what
        // makes Dryad Arbor green (no mana cost, indicator says green) and
        // ensures Green Sun's Zenith / Summoner's Pact / etc.'s "green
        // creature card" predicate finds it. An empty indicator list does
        // not subtract — colors stamped by an indicator are additive on
        // top of the mana-cost pips. Devoid (the indicator-overrides-to-
        // colorless case) is rare in v1 and deferred until a Devoid
        // factory needs it.
        if (card is Card withIndicator && withIndicator.ColorIndicator != null)
        {
            foreach (var c in withIndicator.ColorIndicator)
            {
                if (c != ManaColor.Generic && c != ManaColor.Colorless)
                    set.Add(c);
            }
        }

        return set;
    }

    /// <summary>
    /// Parse a Scryfall <c>colors</c> JSON array (e.g. <c>["G"]</c>,
    /// <c>["W","U"]</c>, <c>"[]"</c>) into a <see cref="ManaColor"/> list
    /// suitable for stamping on a runtime <see cref="Card"/> via
    /// <see cref="Card.SetColorIndicator"/>. Used by every card-construction
    /// path that ingests seed rows (<see cref="Majik.Core.CardData.ScryfallCardFactory"/>,
    /// the server-side deck loader's bare-shell path, the
    /// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
    /// JSON path) so the color predicate is correct for all of them.
    /// Returns an empty list when the input is null/blank/malformed/empty;
    /// callers can skip the stamp in that case.
    /// </summary>
    public static IReadOnlyList<ManaColor> ParseScryfallColors(string? scryfallColorsJson)
    {
        if (string.IsNullOrWhiteSpace(scryfallColorsJson)) return Array.Empty<ManaColor>();

        List<string>? letters;
        try
        {
            letters = JsonSerializer.Deserialize<List<string>>(scryfallColorsJson);
        }
        catch
        {
            return Array.Empty<ManaColor>();
        }
        if (letters == null || letters.Count == 0) return Array.Empty<ManaColor>();

        var colors = new List<ManaColor>(letters.Count);
        foreach (var letter in letters)
        {
            switch (letter?.Trim().ToUpperInvariant())
            {
                case "W": colors.Add(ManaColor.White); break;
                case "U": colors.Add(ManaColor.Blue); break;
                case "B": colors.Add(ManaColor.Black); break;
                case "R": colors.Add(ManaColor.Red); break;
                case "G": colors.Add(ManaColor.Green); break;
                // Other tokens (C, malformed entries) are skipped —
                // colorless is the absence of indicator entries, not a
                // positive entry.
            }
        }
        return colors;
    }
}
