using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1c — conditional "enters tapped unless …" binder. Detects the
/// "N or more / N or fewer other lands" variant used by Kamigawa channel
/// lands (Boseiju, Who Endures; Otawara, Soaring City; …) and the Murders
/// at Karlov Manor surveil-dual cycle (Underground Mortuary; …).
///
/// Other conditional variants — "unless you control a [subtype]" (Spymaster's
/// Vault, slow lands), "unless you control two or more basic lands" (check
/// lands), etc — need their own binders. The regex here only claims the
/// "N or [more|fewer] other lands" form.
/// </summary>
public static class ConditionalEntersTappedBinder
{
    // "enters tapped unless you control <count> or <more|fewer> other lands."
    // Captures the count word and the direction so the predicate can be
    // built generically.
    private static readonly Regex OtherLandsClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+control\s+(?<count>one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+or\s+(?<direction>more|fewer)\s+other\s+lands",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;
        var m = OtherLandsClause.Match(text);
        if (!m.Success) return false;

        var threshold = WordToInt(m.Groups["count"].Value);
        var direction = m.Groups["direction"].Value.ToLowerInvariant();

        // CR rule: "other" excludes the card itself. We exclude `self`
        // from the count so the replacement is correct whether evaluated
        // before or after the entering card has been placed on the
        // battlefield (ZoneService runs replacements before the move
        // commits, so the card isn't on the battlefield yet — but
        // future call sites may differ).
        Func<Player, ICard, bool> entersUntappedIf = direction switch
        {
            "more" => (controller, self) => CountOtherLands(controller, self) >= threshold,
            "fewer" => (controller, self) => CountOtherLands(controller, self) <= threshold,
            _ => (_, _) => false,
        };

        replacements.Register(new ConditionalEntersTappedReplacement(card, entersUntappedIf));
        return true;
    }

    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
