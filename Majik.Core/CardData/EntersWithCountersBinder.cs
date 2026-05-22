using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1d — detects "[Card] enters the battlefield with N +1/+1 counter(s)
/// on it" oracle text and registers an <see cref="EntersWithCountersReplacement"/>
/// on the supplied <see cref="ReplacementBus"/>.
///
/// Matched shapes:
///   * "enters with a +1/+1 counter on it"
///   * "enters with two +1/+1 counters on it"
///   * "enters the battlefield with three +1/+1 counters on it"
///
/// Variable-X counter loads (Walking Ballista, "with X +1/+1 counters") are
/// intentionally NOT matched — those need <c>ChosenSpellParams.X</c>
/// threaded through <see cref="ZoneMoveIntent"/>, deferred to a follow-up.
/// Conditional "with N counters" clauses ("for each Y, enters with a +1/+1
/// counter") also stay unbound — they need a context-aware predicate.
/// </summary>
public static class EntersWithCountersBinder
{
    private static readonly Regex Pattern = new(
        @"\benters\s+(?:the\s+battlefield\s+)?with\s+(?<n>a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+\+1/\+1\s+counters?\s+on\s+it\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Pattern.Match(text);
        if (!m.Success) return false;

        var n = WordToInt(m.Groups["n"].Value);
        if (n <= 0) return false;

        replacements.Register(new EntersWithCountersReplacement(card, n));
        return true;
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var v) ? v : 0,
        };
}
